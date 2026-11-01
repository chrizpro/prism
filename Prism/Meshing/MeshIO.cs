using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Prism.Meshing;

/// <summary>Reading and writing the mesh formats 3D printing and scanning workflows use: STL (binary and ASCII), OBJ, PLY and 3MF.</summary>
public static class MeshIO
{
    public static readonly string[] ReadPatterns = ["*.stl", "*.obj", "*.ply", "*.3mf"];
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static TriMesh Load(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".stl": return LoadStl(File.ReadAllBytes(path));
            case ".obj": return LoadObj(File.ReadAllLines(path));
            case ".ply": return LoadPly(File.ReadAllBytes(path));
            case ".3mf": return Load3mf(path);
            case ".xyz" or ".pts" or ".xyzn" or ".csv": throw new InvalidDataException("That is a point cloud, not a mesh. Use Mesh > Import Point Cloud to build a surface from it.");
            default: throw new InvalidDataException("Unknown mesh format. Use STL, OBJ, PLY or 3MF.");
        }
    }

    public static void Save(TriMesh m, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".stl": File.WriteAllBytes(path, SaveStl(m)); break;
            case ".obj": SaveObj(m, path); break;
            case ".ply": File.WriteAllBytes(path, SavePly(m)); break;
            case ".3mf": Save3mf([m], path); break;
            default: throw new InvalidDataException("Unknown mesh format. Use STL, OBJ, PLY or 3MF.");
        }
    }

    // ---------------------------------------------------------------- STL
    /// <summary>STL stores every triangle separately; identical corners are merged so the mesh is connected.</summary>
    public static TriMesh LoadStl(byte[] data)
    {
        var raw = new List<float>();
        bool binary = false;
        if (data.Length >= 84)
        {
            uint n = BitConverter.ToUInt32(data, 80);
            binary = 84L + n * 50L == data.Length;
        }
        if (!binary)
        {
            var text = Encoding.ASCII.GetString(data);
            if (!text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase) && data.Length >= 84) binary = true;   // binary exporters sometimes write short files
            else
            {
                foreach (var line in text.Split('\n'))
                {
                    var l = line.Trim();
                    if (!l.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                    var p = l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4) { raw.Add(float.Parse(p[1], Inv)); raw.Add(float.Parse(p[2], Inv)); raw.Add(float.Parse(p[3], Inv)); }
                }
                return FromSoup([.. raw]);
            }
        }
        uint count = BitConverter.ToUInt32(data, 80);
        count = (uint)Math.Min(count, (data.Length - 84) / 50);
        var v = new float[count * 9];
        for (int i = 0; i < count; i++)
        {
            int o = 84 + i * 50 + 12;
            for (int k = 0; k < 9; k++) v[i * 9 + k] = BitConverter.ToSingle(data, o + 4 * k);
        }
        return FromSoup(v);
    }

    /// <summary>Builds an indexed mesh from a triangle soup (9 floats per triangle), merging bit-identical corners.</summary>
    public static TriMesh FromSoup(float[] soup)
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
        return new TriMesh([.. verts], tris);
    }

    public static byte[] SaveStl(TriMesh m)
    {
        var ms = new MemoryStream(84 + m.TriCount * 50);
        var w = new BinaryWriter(ms);
        var head = new byte[80]; Encoding.ASCII.GetBytes("Prism binary STL").CopyTo(head, 0);
        w.Write(head); w.Write((uint)m.TriCount);
        for (int t = 0; t < m.TriCount; t++)
        {
            var n = m.FaceNormal(t);
            w.Write((float)n.X); w.Write((float)n.Y); w.Write((float)n.Z);
            for (int k = 0; k < 3; k++) { int v = m.T[3 * t + k]; w.Write(m.V[3 * v]); w.Write(m.V[3 * v + 1]); w.Write(m.V[3 * v + 2]); }
            w.Write((ushort)0);
        }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- OBJ
    public static TriMesh LoadObj(string[] lines)
    {
        var v = new List<float>(); var t = new List<int>();
        foreach (var raw in lines)
        {
            var l = raw.Trim();
            if (l.StartsWith("v ")) { var p = l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries); v.Add(float.Parse(p[1], Inv)); v.Add(float.Parse(p[2], Inv)); v.Add(float.Parse(p[3], Inv)); }
            else if (l.StartsWith("f "))
            {
                var p = l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(s =>
                {
                    int i = int.Parse(s.Split('/')[0], Inv);
                    return i < 0 ? v.Count / 3 + i : i - 1;
                }).ToArray();
                for (int i = 1; i + 1 < p.Length; i++) { t.Add(p[0]); t.Add(p[i]); t.Add(p[i + 1]); }   // fan-triangulate polygons
            }
        }
        return new TriMesh([.. v], [.. t]);
    }

    public static void SaveObj(TriMesh m, string path)
    {
        var sb = new StringBuilder("# Prism\n");
        for (int i = 0; i < m.VertexCount; i++) sb.Append("v ").Append(m.V[3 * i].ToString("R", Inv)).Append(' ').Append(m.V[3 * i + 1].ToString("R", Inv)).Append(' ').Append(m.V[3 * i + 2].ToString("R", Inv)).Append('\n');
        for (int t = 0; t < m.TriCount; t++) sb.Append("f ").Append(m.T[3 * t] + 1).Append(' ').Append(m.T[3 * t + 1] + 1).Append(' ').Append(m.T[3 * t + 2] + 1).Append('\n');
        File.WriteAllText(path, sb.ToString());
    }

    // ---------------------------------------------------------------- PLY
    public static TriMesh LoadPly(byte[] data)
    {
        int pos = 0;
        string ReadLine() { int s = pos; while (pos < data.Length && data[pos] != '\n') pos++; var line = Encoding.ASCII.GetString(data, s, pos - s).Trim(); pos++; return line; }
        if (ReadLine() != "ply") throw new InvalidDataException("Not a PLY file.");
        string format = "ascii"; int nv = 0, nf = 0;
        var vprops = new List<(string type, string name)>(); string? faceCountType = null, faceIdxType = null; string cur = "";
        while (pos < data.Length)
        {
            var l = ReadLine(); var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (l == "end_header") break;
            if (p.Length == 0) continue;
            if (p[0] == "format") format = p[1];
            else if (p[0] == "element") { cur = p[1]; if (cur == "vertex") nv = int.Parse(p[2]); else if (cur == "face") nf = int.Parse(p[2]); }
            else if (p[0] == "property")
            {
                if (cur == "vertex") vprops.Add((p[1], p[2]));
                else if (cur == "face" && p[1] == "list") { faceCountType = p[2]; faceIdxType = p[3]; }
            }
        }
        var v = new float[nv * 3]; var t = new List<int>();
        int ix = vprops.FindIndex(q => q.name == "x"), iy = vprops.FindIndex(q => q.name == "y"), iz = vprops.FindIndex(q => q.name == "z");
        if (ix < 0 || iy < 0 || iz < 0) throw new InvalidDataException("PLY has no vertex positions.");
        if (format == "ascii")
        {
            var rest = Encoding.ASCII.GetString(data, pos, data.Length - pos).Split('\n');
            for (int i = 0; i < nv; i++) { var p = rest[i].Split(' ', StringSplitOptions.RemoveEmptyEntries); v[3 * i] = float.Parse(p[ix], Inv); v[3 * i + 1] = float.Parse(p[iy], Inv); v[3 * i + 2] = float.Parse(p[iz], Inv); }
            for (int i = 0; i < nf; i++)
            {
                var p = rest[nv + i].Split(' ', StringSplitOptions.RemoveEmptyEntries); int n = int.Parse(p[0]);
                for (int k = 1; k + 1 < n; k++) { t.Add(int.Parse(p[1])); t.Add(int.Parse(p[1 + k])); t.Add(int.Parse(p[2 + k])); }
            }
        }
        else
        {
            bool big = format == "binary_big_endian";
            double Read(string type)
            {
                int n = type switch { "char" or "uchar" or "int8" or "uint8" => 1, "short" or "ushort" or "int16" or "uint16" => 2, "double" or "float64" => 8, _ => 4 };
                var b = new byte[n]; Array.Copy(data, pos, b, 0, n); pos += n;
                if (big) Array.Reverse(b);
                return type switch
                {
                    "char" or "int8" => (sbyte)b[0], "uchar" or "uint8" => b[0], "short" or "int16" => BitConverter.ToInt16(b), "ushort" or "uint16" => BitConverter.ToUInt16(b),
                    "int" or "int32" => BitConverter.ToInt32(b), "uint" or "uint32" => BitConverter.ToUInt32(b), "double" or "float64" => BitConverter.ToDouble(b), _ => BitConverter.ToSingle(b),
                };
            }
            for (int i = 0; i < nv; i++)
                for (int k = 0; k < vprops.Count; k++)
                {
                    double d = Read(vprops[k].type);
                    if (k == ix) v[3 * i] = (float)d; else if (k == iy) v[3 * i + 1] = (float)d; else if (k == iz) v[3 * i + 2] = (float)d;
                }
            for (int i = 0; i < nf && faceCountType != null; i++)
            {
                int n = (int)Read(faceCountType); var idx = new int[n];
                for (int k = 0; k < n; k++) idx[k] = (int)Read(faceIdxType!);
                for (int k = 1; k + 1 < n; k++) { t.Add(idx[0]); t.Add(idx[k]); t.Add(idx[k + 1]); }
            }
        }
        return new TriMesh(v, [.. t]);
    }

    public static byte[] SavePly(TriMesh m)
    {
        var ms = new MemoryStream();
        var head = $"ply\nformat binary_little_endian 1.0\ncomment Prism\nelement vertex {m.VertexCount}\nproperty float x\nproperty float y\nproperty float z\nelement face {m.TriCount}\nproperty list uchar int vertex_indices\nend_header\n";
        ms.Write(Encoding.ASCII.GetBytes(head));
        var w = new BinaryWriter(ms);
        foreach (var f in m.V) w.Write(f);
        for (int t = 0; t < m.TriCount; t++) { w.Write((byte)3); w.Write(m.T[3 * t]); w.Write(m.T[3 * t + 1]); w.Write(m.T[3 * t + 2]); }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- 3MF
    static readonly XNamespace Ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    public static TriMesh Load3mf(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("No model in this 3MF.");
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        var objects = doc.Descendants(Ns + "object").ToDictionary(o => o.Attribute("id")!.Value);
        var parts = new List<TriMesh>();
        // build items honour their transforms; fall back to all meshes when there is no build section
        var items = doc.Descendants(Ns + "item").ToList();
        IEnumerable<(XElement obj, double[]? tr)> sources = items.Count > 0
            ? items.Select(i => (objects[i.Attribute("objectid")!.Value], ParseTransform(i.Attribute("transform")?.Value)))
            : objects.Values.Select(o => (o, (double[]?)null));
        foreach (var (obj, tr) in sources)
            foreach (var m in MeshesOf(obj, objects, tr)) parts.Add(m);
        if (parts.Count == 0) throw new InvalidDataException("This 3MF contains no meshes.");
        return TriMesh.Merge(parts);
    }

    static IEnumerable<TriMesh> MeshesOf(XElement obj, Dictionary<string, XElement> objects, double[]? tr)
    {
        var mesh = obj.Element(Ns + "mesh");
        if (mesh != null)
        {
            var vs = mesh.Element(Ns + "vertices")!.Elements(Ns + "vertex").ToList();
            var v = new float[vs.Count * 3];
            for (int i = 0; i < vs.Count; i++)
            {
                double x = double.Parse(vs[i].Attribute("x")!.Value, Inv), y = double.Parse(vs[i].Attribute("y")!.Value, Inv), z = double.Parse(vs[i].Attribute("z")!.Value, Inv);
                if (tr != null) (x, y, z) = (tr[0] * x + tr[3] * y + tr[6] * z + tr[9], tr[1] * x + tr[4] * y + tr[7] * z + tr[10], tr[2] * x + tr[5] * y + tr[8] * z + tr[11]);
                v[3 * i] = (float)x; v[3 * i + 1] = (float)y; v[3 * i + 2] = (float)z;
            }
            var ts = mesh.Element(Ns + "triangles")!.Elements(Ns + "triangle").ToList();
            var t = new int[ts.Count * 3];
            for (int i = 0; i < ts.Count; i++) { t[3 * i] = int.Parse(ts[i].Attribute("v1")!.Value); t[3 * i + 1] = int.Parse(ts[i].Attribute("v2")!.Value); t[3 * i + 2] = int.Parse(ts[i].Attribute("v3")!.Value); }
            yield return new TriMesh(v, t);
        }
        var comps = obj.Element(Ns + "components");
        if (comps != null)
            foreach (var c in comps.Elements(Ns + "component"))
                foreach (var m in MeshesOf(objects[c.Attribute("objectid")!.Value], objects, tr)) yield return m;
    }

    static double[]? ParseTransform(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, Inv)).ToArray();
        return p.Length == 12 ? p : null;
    }

    /// <summary>Writes a 3MF package (one object per mesh, millimetres). 3MF keeps units and multiple bodies, unlike STL.</summary>
    public static void Save3mf(IReadOnlyList<TriMesh> meshes, string path, IReadOnlyList<string>? names = null)
    {
        var model = new XElement(Ns + "model", new XAttribute("unit", "millimeter"), new XAttribute(XNamespace.Xml + "lang", "en-US"));
        var res = new XElement(Ns + "resources"); var build = new XElement(Ns + "build");
        for (int k = 0; k < meshes.Count; k++)
        {
            var m = meshes[k];
            var verts = new XElement(Ns + "vertices"); var tris = new XElement(Ns + "triangles");
            for (int i = 0; i < m.VertexCount; i++) verts.Add(new XElement(Ns + "vertex", new XAttribute("x", m.V[3 * i].ToString("R", Inv)), new XAttribute("y", m.V[3 * i + 1].ToString("R", Inv)), new XAttribute("z", m.V[3 * i + 2].ToString("R", Inv))));
            for (int t = 0; t < m.TriCount; t++) tris.Add(new XElement(Ns + "triangle", new XAttribute("v1", m.T[3 * t]), new XAttribute("v2", m.T[3 * t + 1]), new XAttribute("v3", m.T[3 * t + 2])));
            res.Add(new XElement(Ns + "object", new XAttribute("id", k + 1), new XAttribute("type", "model"), new XAttribute("name", names != null && k < names.Count ? names[k] : $"Body {k + 1}"), new XElement(Ns + "mesh", verts, tris)));
            build.Add(new XElement(Ns + "item", new XAttribute("objectid", k + 1)));
        }
        model.Add(res, build);
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, string content) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(content); }
        Put("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>");
        Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
        Put("3D/3dmodel.model", new XDocument(new XDeclaration("1.0", "UTF-8", null), model).ToString(SaveOptions.DisableFormatting));
    }

    // ---------------------------------------------------------------- compact storage inside .prism files
    public static string ToBase64(TriMesh m)
    {
        var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true))
        {
            var w = new BinaryWriter(z);
            w.Write(m.VertexCount); w.Write(m.TriCount);
            foreach (var f in m.V) w.Write(f);
            foreach (var i in m.T) w.Write(i);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    public static TriMesh FromBase64(string s)
    {
        using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(s)), CompressionMode.Decompress);
        var r = new BinaryReader(z);
        int nv = r.ReadInt32(), nt = r.ReadInt32();
        var v = new float[nv * 3]; for (int i = 0; i < v.Length; i++) v[i] = r.ReadSingle();
        var t = new int[nt * 3]; for (int i = 0; i < t.Length; i++) t[i] = r.ReadInt32();
        return new TriMesh(v, t);
    }
}
