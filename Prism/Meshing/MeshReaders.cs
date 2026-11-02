using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Prism.Meshing;

/// <summary>A progress report from a long-running mesh import: how far along it is (0 to 1) and what it is doing.</summary>
public readonly record struct ImportProgress(double Fraction, string Status);

/// <summary>
/// What a mesh file contained, before any import options are applied: one indexed mesh, which triangles belong to which named part
/// (3MF object, OBJ object or group, STL solid), the colours and the unit the file declared.
/// </summary>
public sealed class MeshFile
{
    public string Name { get; init; } = "Mesh";
    public string Format { get; init; } = "";
    public TriMesh Mesh { get; set; } = TriMesh.Empty;
    /// <summary>One name per part. A single-part file has one entry.</summary>
    public List<string> PartNames { get; } = [];
    /// <summary>Part index of every triangle; null when the file has a single part.</summary>
    public int[]? PartOfTri { get; set; }
    /// <summary>Unit the file declares (3MF only), such as "inch"; null when the format does not say.</summary>
    public string? Unit { get; set; }
    /// <summary>Millimetres per file unit when the file declares one.</summary>
    public double? UnitToMm { get; set; }
    /// <summary>Things the reader noticed and worked around (skipped lines, truncated data, unsupported features).</summary>
    public List<string> Warnings { get; } = [];
    public int PartCount => Math.Max(1, PartNames.Count);
}

/// <summary>Reading side of <see cref="MeshIO"/>: tolerant text parsing with line numbers, progress and cancellation.</summary>
public static partial class MeshIO
{
    /// <summary>Bookkeeping shared by the readers: progress throttling, cancellation and the file name used in error messages.</summary>
    sealed class Rd(string name, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        public readonly string Name = name;
        long _last;
        public void Check() => ct.ThrowIfCancellationRequested();
        public void Report(double fraction, string status)
        {
            ct.ThrowIfCancellationRequested();
            if (progress == null) return;
            long now = Environment.TickCount64;
            if (now - _last < 40 && fraction < 1) return;
            _last = now;
            progress.Report(new ImportProgress(Math.Clamp(fraction, 0, 1), status));
        }
        public InvalidDataException Bad(int line, string message) => new($"{Name}: line {line}: {message}");
        public InvalidDataException Bad(string message) => new($"{Name}: {message}");
    }

    const uint DefaultGrey = 0xFFC8D0DA;

    /// <summary>Read a mesh file (STL, OBJ, PLY, 3MF) without applying any import options. Throws <see cref="InvalidDataException"/> for damaged files and <see cref="OperationCanceledException"/> when cancelled.</summary>
    public static MeshFile Read(string path, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var rd = new Rd(Path.GetFileName(path), progress, ct);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        rd.Report(0, $"Reading {Path.GetFileName(path)}…");
        MeshFile f = ext switch
        {
            ".stl" => ReadStl(File.ReadAllBytes(path), rd, name),
            ".obj" => ReadObj(File.ReadAllText(path), Path.GetDirectoryName(Path.GetFullPath(path)), rd, name),
            ".ply" => ReadPly(File.ReadAllBytes(path), rd, name),
            ".3mf" => Read3mf(path, rd, name),
            ".xyz" or ".pts" or ".xyzn" or ".csv" => throw new InvalidDataException("That is a point cloud, not a mesh. Use Mesh > Import Point Cloud to build a surface from it."),
            _ => throw new InvalidDataException("Unknown mesh format. Use STL, OBJ, PLY or 3MF."),
        };
        rd.Report(1, "Read " + Path.GetFileName(path));
        return f;
    }

    // ---------------------------------------------------------------- tokenising helpers
    static bool IsWs(char c) => c is ' ' or '\t' or '\r' or '\n' or '\v' or '\f';

    static bool Next(ReadOnlySpan<char> s, ref int i, out ReadOnlySpan<char> tok)
    {
        while (i < s.Length && IsWs(s[i])) i++;
        if (i >= s.Length) { tok = default; return false; }
        int st = i;
        while (i < s.Length && !IsWs(s[i])) i++;
        tok = s[st..i];
        return true;
    }

    static bool F(ReadOnlySpan<char> s, out float v) => float.TryParse(s, NumberStyles.Float, Inv, out v) && float.IsFinite(v);

    static bool NextFloat(ReadOnlySpan<char> s, ref int i, out float v)
    {
        v = 0;
        return Next(s, ref i, out var t) && F(t, out v);
    }

    /// <summary>Next line of <paramref name="s"/> as [start, end) without its terminator; false at the end of the text.</summary>
    static bool NextLine(string s, ref int pos, out int start, out int end)
    {
        start = pos; end = pos;
        if (pos >= s.Length) return false;
        int nl = s.IndexOf('\n', pos);
        if (nl < 0) { end = s.Length; pos = s.Length; }
        else { end = nl; pos = nl + 1; }
        if (end > start && s[end - 1] == '\r') end--;
        return true;
    }

    static uint Argb(double r, double g, double b, double a = 1)
    {
        static uint C(double x) => (uint)Math.Clamp((int)Math.Round(x * 255), 0, 255);
        return C(a) << 24 | C(r) << 16 | C(g) << 8 | C(b);
    }

    // ---------------------------------------------------------------- STL
    /// <summary>STL stores every triangle separately; identical corners are merged so the mesh is connected.</summary>
    public static TriMesh LoadStl(byte[] data) => ReadStl(data, new Rd("STL", null, default), "STL").Mesh;

    /// <summary>Builds an indexed mesh from a triangle soup (9 floats per triangle). Bit-identical corners are always merged; with a positive <paramref name="weldTolerance"/> corners closer than that distance are merged too.</summary>
    public static TriMesh FromSoup(float[] soup, double weldTolerance = 0)
    {
        int nt = soup.Length / 9;
        var map = new Dictionary<(int, int, int), int>(nt * 2);
        var verts = new List<float>(nt * 3); var tris = new int[nt * 3];
        for (int i = 0; i < nt * 3; i++)
        {
            float x = soup[3 * i], y = soup[3 * i + 1], z = soup[3 * i + 2];
            var key = (BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(y), BitConverter.SingleToInt32Bits(z));
            if (!map.TryGetValue(key, out int id)) { id = verts.Count / 3; map[key] = id; verts.Add(x); verts.Add(y); verts.Add(z); }
            tris[i] = id;
        }
        var m = new TriMesh([.. verts], tris);
        return weldTolerance > 0 ? MeshOps.Weld(m, weldTolerance) : m;
    }

    static MeshFile ReadStl(byte[] data, Rd rd, string name)
    {
        var file = new MeshFile { Name = name, Format = "STL" };
        bool binary = false;
        if (data.Length >= 84)
        {
            uint n = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(80));
            binary = 84L + n * 50L == data.Length;
        }
        if (!binary)
        {
            int i = 0;
            while (i < data.Length && (data[i] <= 32 || data[i] >= 0xEF && i < 3)) i++;     // whitespace and a UTF-8 byte order mark
            bool solid = i + 5 <= data.Length && Encoding.ASCII.GetString(data, i, 5).Equals("solid", StringComparison.OrdinalIgnoreCase);
            if (solid)
            {
                var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096));
                binary = data.Length >= 84 && !head.Contains("facet", StringComparison.OrdinalIgnoreCase) && !head.Contains("vertex", StringComparison.OrdinalIgnoreCase) && !head.Contains("endsolid", StringComparison.OrdinalIgnoreCase);
            }
            else if (data.Length >= 84) binary = true;       // binary exporters sometimes write short or odd files
            else throw rd.Bad("this is not an STL file (no 'solid' header and too short to be binary).");
        }

        if (binary)
        {
            if (data.Length < 84) throw rd.Bad("binary STL is shorter than its 84-byte header.");
            uint declared = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(80));
            long avail = (data.Length - 84) / 50;
            if (declared > avail) file.Warnings.Add($"The STL header promises {declared:#,0} triangles but the file only holds {avail:#,0}; the rest was ignored.");
            int count = (int)Math.Min(declared, avail);
            var soup = new float[count * 9]; int kept = 0, dropped = 0;
            for (int i = 0; i < count; i++)
            {
                int o = 84 + i * 50 + 12; bool ok = true;
                for (int k = 0; k < 9; k++) { float x = BitConverter.ToSingle(data, o + 4 * k); if (!float.IsFinite(x)) ok = false; soup[kept * 9 + k] = x; }
                if (ok) kept++; else dropped++;
                if ((i & 0xFFFF) == 0) rd.Report(0.9 * i / Math.Max(count, 1), "Reading STL triangles…");
            }
            if (dropped > 0) file.Warnings.Add($"{dropped:#,0} triangle(s) with non-numeric coordinates were skipped.");
            Array.Resize(ref soup, kept * 9);
            file.Mesh = FromSoup(soup);
            file.PartNames.Add(name);
            return file;
        }

        // ASCII, possibly several "solid ... endsolid" blocks in one file
        var text = Encoding.UTF8.GetString(data);
        var all = new List<float>(); var partOf = new List<int>(); var names = new List<string>();
        List<float>? cur = null; string curName = ""; int lineNo = 0, pos = 0;
        void Close(int line)
        {
            if (cur == null) return;
            if (cur.Count % 9 != 0) throw rd.Bad(line, $"solid '{curName}' has {cur.Count / 3} vertices, which is not a multiple of 3 (a facet is incomplete).");
            if (cur.Count > 0)
            {
                names.Add(curName.Length > 0 ? curName : $"Solid {names.Count + 1}");
                all.AddRange(cur);
                for (int t = 0; t < cur.Count / 9; t++) partOf.Add(names.Count - 1);
            }
            cur = null;
        }
        while (NextLine(text, ref pos, out int a, out int b))
        {
            lineNo++;
            if ((lineNo & 0x3FFF) == 0) rd.Report(0.9 * pos / text.Length, "Reading STL facets…");
            var line = text.AsSpan(a, b - a); int p = 0;
            if (!Next(line, ref p, out var cmd)) continue;
            if (cmd.Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                if (!NextFloat(line, ref p, out float x) || !NextFloat(line, ref p, out float y) || !NextFloat(line, ref p, out float z))
                    throw rd.Bad(lineNo, "expected 'vertex x y z' with three numbers, found: " + Clip(line));
                cur ??= [];
                cur.Add(x); cur.Add(y); cur.Add(z);
            }
            else if (cmd.Equals("solid", StringComparison.OrdinalIgnoreCase))
            {
                Close(lineNo);
                cur = []; curName = line[p..].Trim().ToString();
            }
            else if (cmd.Equals("endsolid", StringComparison.OrdinalIgnoreCase)) Close(lineNo);
        }
        Close(lineNo);
        file.Mesh = FromSoup([.. all]);
        if (names.Count <= 1) file.PartNames.Add(names.Count == 1 && names[0].Length > 0 && !names[0].StartsWith("Solid ") ? names[0] : name);
        else { file.PartNames.AddRange(names); file.PartOfTri = [.. partOf]; }
        return file;
    }

    static string Clip(ReadOnlySpan<char> s) => s.Length > 60 ? s[..60].ToString() + "…" : s.ToString();

    // ---------------------------------------------------------------- OBJ
    public static TriMesh LoadObj(string[] lines) => ReadObj(string.Join('\n', lines), null, new Rd("OBJ", null, default), "OBJ").Mesh;

    static MeshFile ReadObj(string text, string? dir, Rd rd, string stem)
    {
        var file = new MeshFile { Name = stem, Format = "OBJ" };
        var v = new List<float>(); List<uint>? vcol = null;
        var tris = new List<int>(); var faceCol = new List<uint>(); bool anyFaceCol = false;
        var names = new List<string>(); var byName = new Dictionary<string, int>(); var partOf = new List<int>();
        string obj = "", grp = ""; int curPart = -1;
        var mats = new Dictionary<string, uint>(); uint curCol = 0; bool warnedMat = false, warnedShort = false;
        int pos = 0, lineNo = 0, skippedFaces = 0;
        var idx = new List<int>();

        int EnsurePart()
        {
            if (curPart >= 0) return curPart;
            string nm = obj.Length == 0 && grp.Length == 0 ? stem : grp.Length == 0 ? obj : obj.Length == 0 || obj == grp ? grp : obj + "/" + grp;
            if (!byName.TryGetValue(nm, out curPart)) { curPart = names.Count; names.Add(nm); byName[nm] = curPart; }
            return curPart;
        }

        while (NextLine(text, ref pos, out int a, out int b))
        {
            lineNo++;
            if ((lineNo & 0x1FFF) == 0) rd.Report(0.9 * pos / text.Length, "Reading OBJ…");
            var line = text.AsSpan(a, b - a); int p = 0;
            if (!Next(line, ref p, out var cmd) || cmd[0] == '#') continue;
            if (cmd.Length == 1 && cmd[0] == 'v')
            {
                if (!NextFloat(line, ref p, out float x) || !NextFloat(line, ref p, out float y) || !NextFloat(line, ref p, out float z))
                    throw rd.Bad(lineNo, "invalid vertex, expected 'v x y z' with three numbers, found: " + Clip(line));
                // optional fourth value w (ignored) or three extra values r g b (the common vertex-colour extension)
                uint c = DefaultGrey; bool has = false;
                int save = p;
                if (NextFloat(line, ref p, out float c1) && NextFloat(line, ref p, out float c2) && NextFloat(line, ref p, out float c3))
                {
                    double k = c1 > 1 || c2 > 1 || c3 > 1 ? 1.0 / 255 : 1;
                    c = Argb(c1 * k, c2 * k, c3 * k); has = true;
                }
                else p = save;
                if (has && vcol == null) { vcol = []; for (int q = 0; q < v.Count / 3; q++) vcol.Add(DefaultGrey); }
                v.Add(x); v.Add(y); v.Add(z); vcol?.Add(c);
            }
            else if (cmd.Length == 1 && cmd[0] == 'f')
            {
                idx.Clear(); int nv = v.Count / 3;
                while (Next(line, ref p, out var tok))
                {
                    int slash = tok.IndexOf('/');
                    var head = slash >= 0 ? tok[..slash] : tok;
                    if (!int.TryParse(head, NumberStyles.AllowLeadingSign, Inv, out int ix))
                        throw rd.Bad(lineNo, $"invalid face corner '{tok.ToString()}' (expected v, v/vt, v//vn or v/vt/vn).");
                    if (ix == 0) throw rd.Bad(lineNo, "face index 0 is not valid (OBJ indices start at 1).");
                    int id = ix < 0 ? nv + ix : ix - 1;
                    if (id < 0 || id >= nv) throw rd.Bad(lineNo, $"face refers to vertex {ix} but only {nv} vertices are defined at this point.");
                    idx.Add(id);
                }
                if (idx.Count < 3) { skippedFaces++; continue; }
                int part = EnsurePart();
                for (int i = 1; i + 1 < idx.Count; i++)
                {
                    tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]);   // fan-triangulate polygons
                    partOf.Add(part); faceCol.Add(curCol); if (curCol != 0) anyFaceCol = true;
                }
            }
            else if (cmd.SequenceEqual("o")) { obj = line[p..].Trim().ToString(); grp = ""; curPart = -1; }
            else if (cmd.SequenceEqual("g")) { grp = line[p..].Trim().ToString(); if (grp == "default") grp = ""; curPart = -1; }
            else if (cmd.SequenceEqual("usemtl"))
            {
                var m = line[p..].Trim().ToString();
                if (!mats.TryGetValue(m, out curCol)) { curCol = 0; if (!warnedMat) { warnedMat = true; file.Warnings.Add($"Material '{m}' has no colour (missing .mtl file or no Kd); faces using it keep the default colour."); } }
            }
            else if (cmd.SequenceEqual("mtllib") && dir != null)
            {
                var rest = line[p..].Trim().ToString();
                foreach (var cand in new[] { rest }.Concat(rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)).Distinct())
                {
                    string mp; try { mp = Path.Combine(dir, cand); } catch (ArgumentException) { continue; }
                    if (!File.Exists(mp)) continue;
                    try { ParseMtl(File.ReadAllText(mp), mats); } catch (IOException) { file.Warnings.Add("The material library " + cand + " could not be read."); }
                    break;
                }
            }
        }
        if (skippedFaces > 0 && !warnedShort) file.Warnings.Add($"{skippedFaces:#,0} face line(s) with fewer than 3 corners were skipped.");
        uint[]? fc = null;
        if (anyFaceCol) fc = faceCol.Select(c => c == 0 ? DefaultGrey : c).ToArray();
        file.Mesh = new TriMesh([.. v], [.. tris], vcol?.ToArray(), fc);
        if (names.Count <= 1) file.PartNames.Add(names.Count == 1 ? names[0] : stem);
        else { file.PartNames.AddRange(names); file.PartOfTri = [.. partOf]; }
        return file;
    }

    /// <summary>Diffuse colours (Kd) of a Wavefront material library. Lines it cannot make sense of are skipped.</summary>
    static void ParseMtl(string text, Dictionary<string, uint> mats)
    {
        string? cur = null; int pos = 0;
        while (NextLine(text, ref pos, out int a, out int b))
        {
            var line = text.AsSpan(a, b - a); int p = 0;
            if (!Next(line, ref p, out var cmd) || cmd[0] == '#') continue;
            if (cmd.Equals("newmtl", StringComparison.OrdinalIgnoreCase)) cur = line[p..].Trim().ToString();
            else if (cur != null && cmd.Equals("Kd", StringComparison.OrdinalIgnoreCase) && NextFloat(line, ref p, out float r) && NextFloat(line, ref p, out float g) && NextFloat(line, ref p, out float bl))
                mats[cur] = Argb(r, g, bl);
        }
    }

    // ---------------------------------------------------------------- PLY
    public static TriMesh LoadPly(byte[] data) => ReadPly(data, new Rd("PLY", null, default), "PLY").Mesh;

    enum PT { I8, U8, I16, U16, I32, U32, F32, F64 }
    static PT PlyType(Rd rd, string t) => t switch
    {
        "char" or "int8" => PT.I8, "uchar" or "uint8" => PT.U8, "short" or "int16" => PT.I16, "ushort" or "uint16" => PT.U16,
        "int" or "int32" => PT.I32, "uint" or "uint32" => PT.U32, "float" or "float32" => PT.F32, "double" or "float64" => PT.F64,
        _ => throw rd.Bad($"unknown PLY property type '{t}'."),
    };
    static int PlySize(PT t) => t switch { PT.I8 or PT.U8 => 1, PT.I16 or PT.U16 => 2, PT.F64 => 8, _ => 4 };

    sealed class PlyProp { public string Name = ""; public PT Type; public bool IsList; public PT CountType; }
    sealed class PlyElement { public string Name = ""; public int Count; public List<PlyProp> Props = []; }

    static MeshFile ReadPly(byte[] data, Rd rd, string name)
    {
        var file = new MeshFile { Name = name, Format = "PLY" };
        int pos = 0;
        string HeaderLine()
        {
            if (pos >= data.Length) throw rd.Bad("the PLY header ends early (no end_header).");
            int s = pos; while (pos < data.Length && data[pos] != '\n') pos++;
            var line = Encoding.ASCII.GetString(data, s, pos - s).Trim(); pos++; return line;
        }
        if (HeaderLine() != "ply") throw rd.Bad("this is not a PLY file (it does not start with 'ply').");
        string format = "ascii"; var elements = new List<PlyElement>(); int hl = 1;
        while (true)
        {
            var l = HeaderLine(); hl++;
            if (l == "end_header") break;
            var p = l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "format":
                    if (p.Length < 2) throw rd.Bad(hl, "format line needs a format name."); format = p[1]; break;
                case "element":
                    if (p.Length < 3 || !int.TryParse(p[2], NumberStyles.None, Inv, out int cnt)) throw rd.Bad(hl, "element line needs a name and a count.");
                    elements.Add(new PlyElement { Name = p[1], Count = cnt }); break;
                case "property":
                    if (elements.Count == 0) throw rd.Bad(hl, "property before any element.");
                    if (p.Length >= 5 && p[1] == "list") elements[^1].Props.Add(new PlyProp { IsList = true, CountType = PlyType(rd, p[2]), Type = PlyType(rd, p[3]), Name = p[4] });
                    else if (p.Length >= 3) elements[^1].Props.Add(new PlyProp { Type = PlyType(rd, p[1]), Name = p[2] });
                    else throw rd.Bad(hl, "malformed property line.");
                    break;
            }
        }
        if (format is not ("ascii" or "binary_little_endian" or "binary_big_endian")) throw rd.Bad($"unsupported PLY format '{format}'.");
        bool ascii = format == "ascii", big = format == "binary_big_endian";

        // value readers over the body
        string body = ascii ? Encoding.ASCII.GetString(data, pos, data.Length - pos) : "";
        int bp = 0, bodyLine = hl + 1;
        double ReadAscii()
        {
            while (bp < body.Length && IsWs(body[bp])) { if (body[bp] == '\n') bodyLine++; bp++; }
            if (bp >= body.Length) throw rd.Bad("the PLY data ends before all elements were read.");
            int s = bp; while (bp < body.Length && !IsWs(body[bp])) bp++;
            if (!double.TryParse(body.AsSpan(s, bp - s), NumberStyles.Float, Inv, out double d)) throw rd.Bad(bodyLine, "expected a number, found: " + Clip(body.AsSpan(s, bp - s)));
            return d;
        }
        double ReadBin(PT t)
        {
            int n = PlySize(t);
            if (pos + n > data.Length) throw rd.Bad("the PLY data ends before all elements were read.");
            var sp = data.AsSpan(pos, n); pos += n;
            return t switch
            {
                PT.I8 => (sbyte)sp[0], PT.U8 => sp[0],
                PT.I16 => big ? BinaryPrimitives.ReadInt16BigEndian(sp) : BinaryPrimitives.ReadInt16LittleEndian(sp),
                PT.U16 => big ? BinaryPrimitives.ReadUInt16BigEndian(sp) : BinaryPrimitives.ReadUInt16LittleEndian(sp),
                PT.I32 => big ? BinaryPrimitives.ReadInt32BigEndian(sp) : BinaryPrimitives.ReadInt32LittleEndian(sp),
                PT.U32 => big ? BinaryPrimitives.ReadUInt32BigEndian(sp) : BinaryPrimitives.ReadUInt32LittleEndian(sp),
                PT.F32 => big ? BinaryPrimitives.ReadSingleBigEndian(sp) : BinaryPrimitives.ReadSingleLittleEndian(sp),
                _ => big ? BinaryPrimitives.ReadDoubleBigEndian(sp) : BinaryPrimitives.ReadDoubleLittleEndian(sp),
            };
        }
        double Read(PT t) => ascii ? ReadAscii() : ReadBin(t);

        float[]? verts = null; uint[]? vcol = null; var tris = new List<int>(); List<uint>? fcol = null;
        int total = elements.Sum(e => e.Count), done = 0;
        foreach (var el in elements)
        {
            long minBytes = el.Props.Sum(pr => pr.IsList ? PlySize(pr.CountType) : PlySize(pr.Type));
            long remaining = ascii ? body.Length - bp : data.Length - pos;
            if (el.Count > 0 && (ascii ? el.Count > remaining : el.Count * Math.Max(minBytes, 1L) > remaining))
                throw rd.Bad($"the PLY header promises {el.Count:#,0} '{el.Name}' entries but the file is too short to hold them.");
            int ix = el.Props.FindIndex(q => q.Name == "x"), iy = el.Props.FindIndex(q => q.Name == "y"), iz = el.Props.FindIndex(q => q.Name == "z");
            int ir = el.Props.FindIndex(q => q.Name is "red" or "r" or "diffuse_red"), ig = el.Props.FindIndex(q => q.Name is "green" or "g" or "diffuse_green");
            int ib = el.Props.FindIndex(q => q.Name is "blue" or "b" or "diffuse_blue"), ia = el.Props.FindIndex(q => q.Name is "alpha" or "a" or "diffuse_alpha");
            bool hasColor = ir >= 0 && ig >= 0 && ib >= 0;
            double Chan(int k, double val) => el.Props[k].Type is PT.F32 or PT.F64 ? val : val / 255.0;
            if (el.Name == "vertex")
            {
                if (ix < 0 || iy < 0 || iz < 0) throw rd.Bad("the PLY vertex element has no x, y, z properties.");
                verts = new float[el.Count * 3]; if (hasColor) vcol = new uint[el.Count];
                var val = new double[el.Props.Count];
                for (int i = 0; i < el.Count; i++)
                {
                    for (int k = 0; k < el.Props.Count; k++)
                    {
                        var pr = el.Props[k];
                        if (pr.IsList) { int n = (int)Read(pr.CountType); for (int q = 0; q < n; q++) Read(pr.Type); }
                        else val[k] = Read(pr.Type);
                    }
                    verts[3 * i] = (float)val[ix]; verts[3 * i + 1] = (float)val[iy]; verts[3 * i + 2] = (float)val[iz];
                    if (!float.IsFinite(verts[3 * i]) || !float.IsFinite(verts[3 * i + 1]) || !float.IsFinite(verts[3 * i + 2])) throw rd.Bad($"vertex {i} has non-numeric coordinates.");
                    if (vcol != null) vcol[i] = Argb(Chan(ir, val[ir]), Chan(ig, val[ig]), Chan(ib, val[ib]), ia >= 0 ? Chan(ia, val[ia]) : 1);
                    if ((++done & 0x3FFF) == 0) rd.Report(0.95 * done / Math.Max(total, 1), "Reading PLY…");
                }
            }
            else if (el.Name == "face")
            {
                int li = el.Props.FindIndex(q => q.IsList && q.Name is "vertex_indices" or "vertex_index");
                if (li < 0) li = el.Props.FindIndex(q => q.IsList);
                int nv = verts == null ? 0 : verts.Length / 3;
                var val = new double[el.Props.Count]; var corner = new List<int>();
                if (hasColor) fcol ??= [];
                for (int i = 0; i < el.Count; i++)
                {
                    corner.Clear();
                    for (int k = 0; k < el.Props.Count; k++)
                    {
                        var pr = el.Props[k];
                        if (pr.IsList)
                        {
                            int n = (int)Read(pr.CountType);
                            if (n < 0) throw rd.Bad($"face {i} has a negative corner count.");
                            for (int q = 0; q < n; q++) { double d = Read(pr.Type); if (k == li) corner.Add((int)d); }
                        }
                        else val[k] = Read(pr.Type);
                    }
                    if (corner.Count < 3) continue;
                    foreach (int c in corner) if (c < 0 || c >= nv) throw rd.Bad($"face {i} refers to vertex {c} but the file has {nv} vertices.");
                    uint col = hasColor ? Argb(Chan(ir, val[ir]), Chan(ig, val[ig]), Chan(ib, val[ib]), ia >= 0 ? Chan(ia, val[ia]) : 1) : 0;
                    for (int k = 1; k + 1 < corner.Count; k++) { tris.Add(corner[0]); tris.Add(corner[k]); tris.Add(corner[k + 1]); fcol?.Add(col); }
                    if ((++done & 0x3FFF) == 0) rd.Report(0.95 * done / Math.Max(total, 1), "Reading PLY…");
                }
            }
            else
            {
                for (int i = 0; i < el.Count; i++)
                {
                    foreach (var pr in el.Props)
                    {
                        if (pr.IsList) { int n = (int)Read(pr.CountType); for (int q = 0; q < n; q++) Read(pr.Type); }
                        else Read(pr.Type);
                    }
                    if ((++done & 0x3FFF) == 0) rd.Report(0.95 * done / Math.Max(total, 1), "Reading PLY…");
                }
            }
        }
        if (verts == null) throw rd.Bad("the PLY file has no vertex element.");
        file.Mesh = new TriMesh(verts, [.. tris], vcol, fcol?.ToArray());
        file.PartNames.Add(name);
        return file;
    }

    // ---------------------------------------------------------------- 3MF
    static readonly XNamespace Ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    public static TriMesh Load3mf(string path) => Read3mf(path, new Rd(Path.GetFileName(path), null, default), Path.GetFileNameWithoutExtension(path)).Mesh;

    /// <summary>Millimetres per 3MF model unit, or null for a unit this reader does not know.</summary>
    public static double? UnitToMillimetres(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        null or "" or "millimeter" or "millimetre" => 1, "micron" or "micrometer" => 0.001, "centimeter" or "centimetre" => 10,
        "inch" => 25.4, "foot" => 304.8, "meter" or "metre" => 1000, _ => null,
    };

    static ZipArchiveEntry FindModelEntry(ZipArchive zip)
    {
        var rels = zip.GetEntry("_rels/.rels");
        if (rels != null)
        {
            try
            {
                using var rs = rels.Open();
                foreach (var r in XDocument.Load(rs).Descendants().Where(e => e.Name.LocalName == "Relationship"))
                {
                    var type = (string?)r.Attribute("Type") ?? ""; var target = ((string?)r.Attribute("Target") ?? "").TrimStart('/');
                    if (type.EndsWith("3dmodel", StringComparison.OrdinalIgnoreCase) && zip.Entries.FirstOrDefault(e => e.FullName.Equals(target, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
                }
            }
            catch (XmlException) { }
        }
        return zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("3dmodel.model", StringComparison.OrdinalIgnoreCase))
            ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("No model in this 3MF.");
    }

    // 3MF transforms are row-vector affine matrices: m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32
    static double[]? ParseTransform(string? s, int line, Rd rd)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var p = new double[parts.Length];
        for (int i = 0; i < p.Length; i++) if (!double.TryParse(parts[i], NumberStyles.Float, Inv, out p[i]) || !double.IsFinite(p[i])) throw rd.Bad(line, "invalid transform value '" + parts[i] + "'.");
        if (p.Length != 12) throw rd.Bad(line, $"a transform needs 12 numbers, found {p.Length}.");
        return p;
    }

    /// <summary>The transform that applies <paramref name="first"/> and then <paramref name="then"/> (either may be null for identity).</summary>
    static double[]? Chain(double[]? first, double[]? then)
    {
        if (first == null) return then; if (then == null) return first;
        var r = new double[12];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++) r[3 * i + j] = first[3 * i] * then[j] + first[3 * i + 1] * then[3 + j] + first[3 * i + 2] * then[6 + j];
        for (int j = 0; j < 3; j++) r[9 + j] = first[9] * then[j] + first[10] * then[3 + j] + first[11] * then[6 + j] + then[9 + j];
        return r;
    }

    static double Det(double[] t) => t[0] * (t[4] * t[8] - t[5] * t[7]) - t[1] * (t[3] * t[8] - t[5] * t[6]) + t[2] * (t[3] * t[7] - t[4] * t[6]);

    static bool ParseHexColor(string? s, out uint argb)
    {
        argb = 0;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.TrimStart('#');
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, Inv, out uint v)) return false;
        argb = s.Length == 6 ? 0xFF000000 | v : (v & 0xFF) << 24 | v >> 8;      // 3MF writes #RRGGBBAA
        return true;
    }

    static int Line(XObject e) => e is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

    static MeshFile Read3mf(string path, Rd rd, string name)
    {
        var file = new MeshFile { Name = name, Format = "3MF" };
        XDocument doc;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = FindModelEntry(zip);
            using var s = entry.Open();
            doc = XDocument.Load(s, LoadOptions.SetLineInfo);
        }
        catch (XmlException e) { throw new InvalidDataException($"{rd.Name}: the 3MF model XML is damaged at line {e.LineNumber}: {e.Message}", e); }
        catch (InvalidDataException e) when (!e.Message.StartsWith(rd.Name) && !e.Message.StartsWith("No model")) { throw new InvalidDataException($"{rd.Name}: not a valid 3MF package ({e.Message})", e); }
        var root = doc.Root ?? throw rd.Bad("the 3MF model is empty.");
        var ns = root.Name.Namespace;
        file.Unit = ((string?)root.Attribute("unit") ?? "millimeter").Trim().ToLowerInvariant();
        file.UnitToMm = UnitToMillimetres(file.Unit);
        if (file.UnitToMm == null) file.Warnings.Add($"The 3MF declares the unit '{file.Unit}', which is not known; millimetres were assumed.");
        var resources = root.Element(ns + "resources") ?? throw rd.Bad("the 3MF has no resources section.");

        // colour tables: <basematerials><base displaycolor/></basematerials> and the materials extension's <colorgroup><color color/></colorgroup>
        var tables = new Dictionary<string, List<uint>>();
        foreach (var e in resources.Elements())
        {
            var id = (string?)e.Attribute("id"); if (id == null) continue;
            if (e.Name.LocalName == "basematerials") tables[id] = e.Elements().Select(x => ParseHexColor((string?)x.Attribute("displaycolor"), out var c) ? c : 0u).ToList();
            else if (e.Name.LocalName == "colorgroup") tables[id] = e.Elements().Select(x => ParseHexColor((string?)x.Attribute("color"), out var c) ? c : 0u).ToList();
        }
        var objects = new Dictionary<string, XElement>();
        foreach (var o in resources.Elements(ns + "object")) if ((string?)o.Attribute("id") is { } oid) objects[oid] = o;

        var v = new List<float>(); var t = new List<int>(); var fcol = new List<uint>(); var partOf = new List<int>(); bool anyColor = false, warnedProduction = false;
        var names = new List<string>();

        void AddObject(XElement o, double[]? tr, int part, int depth, HashSet<string> stack)
        {
            if (depth > 32) throw rd.Bad(Line(o), "components are nested too deeply.");
            string oid = (string?)o.Attribute("id") ?? "?";
            if (!stack.Add(oid)) throw rd.Bad(Line(o), $"object {oid} contains itself.");
            string pid = (string?)o.Attribute("pid") ?? "", pidx = (string?)o.Attribute("pindex") ?? "0";
            if (o.Element(ns + "mesh") is { } mesh)
            {
                int baseV = v.Count / 3;
                var vs = mesh.Element(ns + "vertices")?.Elements(ns + "vertex").ToList() ?? [];
                bool flip = tr != null && Det(tr) < 0;
                foreach (var ve in vs)
                {
                    if (!TryD(ve, "x", out double x) || !TryD(ve, "y", out double y) || !TryD(ve, "z", out double z)) throw rd.Bad(Line(ve), $"a vertex of object {oid} is missing or has a non-numeric x, y or z.");
                    if (tr != null) (x, y, z) = (tr[0] * x + tr[3] * y + tr[6] * z + tr[9], tr[1] * x + tr[4] * y + tr[7] * z + tr[10], tr[2] * x + tr[5] * y + tr[8] * z + tr[11]);
                    v.Add((float)x); v.Add((float)y); v.Add((float)z);
                }
                int nv = vs.Count, k = 0;
                foreach (var te in mesh.Element(ns + "triangles")?.Elements(ns + "triangle") ?? [])
                {
                    if (!TryI(te, "v1", out int a) || !TryI(te, "v2", out int b) || !TryI(te, "v3", out int c)) throw rd.Bad(Line(te), $"a triangle of object {oid} is missing v1, v2 or v3.");
                    if (a < 0 || b < 0 || c < 0 || a >= nv || b >= nv || c >= nv) throw rd.Bad(Line(te), $"a triangle of object {oid} refers to a vertex outside 0..{nv - 1}.");
                    if (flip) (b, c) = (c, b);
                    t.Add(baseV + a); t.Add(baseV + b); t.Add(baseV + c); partOf.Add(part);
                    string tp = (string?)te.Attribute("pid") ?? pid, p1 = (string?)te.Attribute("p1") ?? (te.Attribute("pid") != null ? "0" : pidx);
                    uint col = 0;
                    if (tp.Length > 0 && tables.TryGetValue(tp, out var tab) && int.TryParse(p1, NumberStyles.None, Inv, out int pi) && pi < tab.Count) col = tab[pi];
                    if (col != 0) anyColor = true;
                    fcol.Add(col);
                    if ((++k & 0x7FFF) == 0) rd.Report(0.9, "Reading 3MF triangles…");
                }
            }
            if (o.Element(ns + "components") is { } comps)
                foreach (var c in comps.Elements(ns + "component"))
                {
                    if (c.Attributes().Any(a => a.Name.LocalName == "path")) { if (!warnedProduction) { warnedProduction = true; file.Warnings.Add("Components stored in separate model files (3MF production extension) are not supported and were skipped."); } continue; }
                    string cid = (string?)c.Attribute("objectid") ?? "";
                    if (!objects.TryGetValue(cid, out var child)) throw rd.Bad(Line(c), $"a component refers to object {cid}, which does not exist.");
                    AddObject(child, Chain(ParseTransform((string?)c.Attribute("transform"), Line(c), rd), tr), part, depth + 1, stack);
                }
            stack.Remove(oid);
        }

        var items = root.Element(ns + "build")?.Elements(ns + "item").ToList() ?? [];
        var sources = new List<(XElement obj, double[]? tr)>();
        if (items.Count > 0)
            foreach (var it in items)
            {
                string oid = (string?)it.Attribute("objectid") ?? "";
                if (!objects.TryGetValue(oid, out var o)) throw rd.Bad(Line(it), $"a build item refers to object {oid}, which does not exist.");
                sources.Add((o, ParseTransform((string?)it.Attribute("transform"), Line(it), rd)));
            }
        else
        {
            var referenced = objects.Values.SelectMany(o => o.Descendants(ns + "component")).Select(c => (string?)c.Attribute("objectid")).ToHashSet();
            foreach (var o in objects.Values) if (!referenced.Contains((string?)o.Attribute("id"))) sources.Add((o, null));
        }
        foreach (var (obj, tr) in sources)
        {
            var type = (string?)obj.Attribute("type") ?? "model";
            if (type is "support" or "solidsupport") continue;
            int part = names.Count; int before = t.Count;
            string nm = ((string?)obj.Attribute("name"))?.Trim() is { Length: > 0 } n ? n : $"Object {(string?)obj.Attribute("id")}";
            int dup = names.Count(x => x == nm || x.StartsWith(nm + " (")); if (dup > 0) nm += $" ({dup + 1})";
            names.Add(nm);
            AddObject(obj, tr, part, 0, []);
            if (t.Count == before) names.RemoveAt(names.Count - 1);
        }
        if (names.Count == 0 && t.Count == 0) throw rd.Bad("this 3MF contains no meshes.");
        file.Mesh = new TriMesh([.. v], [.. t], null, anyColor ? fcol.Select(c => c == 0 ? DefaultGrey : c).ToArray() : null);
        if (names.Count <= 1) file.PartNames.Add(names.Count == 1 ? names[0] : name);
        else { file.PartNames.AddRange(names); file.PartOfTri = [.. partOf]; }
        return file;

        static bool TryD(XElement e, string a, out double d) { d = 0; return e.Attribute(a) is { } at && double.TryParse(at.Value, NumberStyles.Float, Inv, out d) && double.IsFinite(d); }
        static bool TryI(XElement e, string a, out int i) { i = 0; return e.Attribute(a) is { } at && int.TryParse(at.Value, NumberStyles.AllowLeadingSign, Inv, out i); }
    }
}
