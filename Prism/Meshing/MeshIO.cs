using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Threading;

namespace Prism.Meshing;

/// <summary>Reading and writing the mesh formats 3D printing and scanning workflows use: STL (binary and ASCII), OBJ, PLY and 3MF.</summary>
public static partial class MeshIO
{
    public static readonly string[] ReadPatterns = ["*.stl", "*.obj", "*.ply", "*.3mf"];
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static TriMesh Load(string path) => Read(path).Mesh;

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

    // ---------------------------------------------------------------- STL (reading lives in MeshReaders.cs)
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
    public static void SaveObj(TriMesh m, string path)
    {
        var sb = new StringBuilder("# Prism\n");
        for (int i = 0; i < m.VertexCount; i++)
        {
            sb.Append("v ").Append(m.V[3 * i].ToString("R", Inv)).Append(' ').Append(m.V[3 * i + 1].ToString("R", Inv)).Append(' ').Append(m.V[3 * i + 2].ToString("R", Inv));
            if (m.VertexColors != null) { uint c = m.VertexColors[i]; sb.Append(' ').Append(((c >> 16 & 255) / 255.0).ToString("0.####", Inv)).Append(' ').Append(((c >> 8 & 255) / 255.0).ToString("0.####", Inv)).Append(' ').Append(((c & 255) / 255.0).ToString("0.####", Inv)); }
            sb.Append('\n');
        }
        for (int t = 0; t < m.TriCount; t++) sb.Append("f ").Append(m.T[3 * t] + 1).Append(' ').Append(m.T[3 * t + 1] + 1).Append(' ').Append(m.T[3 * t + 2] + 1).Append('\n');
        File.WriteAllText(path, sb.ToString());
    }

    // ---------------------------------------------------------------- PLY
    public static byte[] SavePly(TriMesh m)
    {
        var ms = new MemoryStream();
        var head = $"ply\nformat binary_little_endian 1.0\ncomment Prism\nelement vertex {m.VertexCount}\nproperty float x\nproperty float y\nproperty float z\n{(m.VertexColors != null ? "property uchar red\nproperty uchar green\nproperty uchar blue\nproperty uchar alpha\n" : "")}element face {m.TriCount}\nproperty list uchar int vertex_indices\nend_header\n";
        ms.Write(Encoding.ASCII.GetBytes(head));
        var w = new BinaryWriter(ms);
        for (int i = 0; i < m.VertexCount; i++)
        {
            w.Write(m.V[3 * i]); w.Write(m.V[3 * i + 1]); w.Write(m.V[3 * i + 2]);
            if (m.VertexColors != null) { uint c = m.VertexColors[i]; w.Write((byte)(c >> 16)); w.Write((byte)(c >> 8)); w.Write((byte)c); w.Write((byte)(c >> 24)); }
        }
        for (int t = 0; t < m.TriCount; t++) { w.Write((byte)3); w.Write(m.T[3 * t]); w.Write(m.T[3 * t + 1]); w.Write(m.T[3 * t + 2]); }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- 3MF
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
            // optional colour block (older files simply end here)
            w.Write((byte)((m.VertexColors != null ? 1 : 0) | (m.FaceColors != null ? 2 : 0)));
            if (m.VertexColors != null) foreach (var c in m.VertexColors) w.Write(c);
            if (m.FaceColors != null) foreach (var c in m.FaceColors) w.Write(c);
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
        uint[]? vc = null, fc = null;
        int flags = z.ReadByte();
        if ((flags & 1) != 0) { vc = new uint[nv]; for (int i = 0; i < nv; i++) vc[i] = r.ReadUInt32(); }
        if ((flags & 2) != 0) { fc = new uint[nt]; for (int i = 0; i < nt; i++) fc[i] = r.ReadUInt32(); }
        return new TriMesh(v, t, vc, fc);
    }
}
