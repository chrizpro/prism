using System.Globalization;
using System.IO.Compression;
using System.Text;
using Prism.Kernel;
using Prism.Meshing;

// Mesh import: units, up axis, welding, colours, parts, robustness, progress and cancellation. Pure C#: no CAD kernel needed.
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }
T Throws<T>(Action a, string what) where T : Exception
{
    try { a(); } catch (T e) { return e; } catch (Exception e) { throw new Exception($"FAIL {what}: threw {e.GetType().Name}: {e.Message}"); }
    throw new Exception($"FAIL {what}: nothing was thrown");
}

var dir = Path.Combine(Path.GetTempPath(), "prism-mesh-import-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
string F(string n) => Path.Combine(dir, n);
var inv = CultureInfo.InvariantCulture;
string N(double d) => d.ToString("R", inv);

// ---- helpers: an axis-aligned box as outward-facing quads
(double, double, double)[][] BoxQuads(double x0, double y0, double z0, double x1, double y1, double z1) =>
[
    [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)], [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
    [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)],
    [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
];
string AsciiSolid(string name, double x0, double y0, double z0, double x1, double y1, double z1)
{
    var sb = new StringBuilder($"solid {name}\n");
    foreach (var q in BoxQuads(x0, y0, z0, x1, y1, z1))
        foreach (var tri in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } })
        {
            sb.Append("facet normal 0 0 0\n outer loop\n");
            foreach (int i in tri) sb.Append($"  vertex {N(q[i].Item1)} {N(q[i].Item2)} {N(q[i].Item3)}\n");
            sb.Append(" endloop\nendfacet\n");
        }
    return sb.Append($"endsolid {name}\n").ToString();
}
TriMesh Box(double x0, double y0, double z0, double x1, double y1, double z1)
{
    var v = new List<float>(); var t = new List<int>();
    foreach (var q in BoxQuads(x0, y0, z0, x1, y1, z1))
    {
        int b = v.Count / 3;
        foreach (var p in q) { v.Add((float)p.Item1); v.Add((float)p.Item2); v.Add((float)p.Item3); }
        t.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
    }
    return new TriMesh([.. v], [.. t]);
}
string ObjBox(double x0, double y0, double z0, double x1, double y1, double z1, int vOffset = 0)
{
    var sb = new StringBuilder();
    foreach (var q in BoxQuads(x0, y0, z0, x1, y1, z1))
        foreach (var p in q) sb.Append($"v {N(p.Item1)} {N(p.Item2)} {N(p.Item3)}\n");
    for (int f = 0; f < 6; f++) sb.Append($"f {vOffset + 4 * f + 1} {vOffset + 4 * f + 2} {vOffset + 4 * f + 3} {vOffset + 4 * f + 4}\n");
    return sb.ToString();
}

try
{
    // ================= STL
    File.WriteAllText(F("two.stl"), AsciiSolid("left", 0, 0, 0, 1, 1, 1) + AsciiSolid("right", 5, 0, 0, 6, 1, 1));
    var stl = MeshIO.Read(F("two.stl"));
    Check(stl.PartCount == 2 && stl.PartNames[0] == "left" && stl.PartNames[1] == "right" && stl.Mesh.TriCount == 24, "ASCII STL with two solids reads as two named parts");
    Check(Math.Abs(stl.Mesh.Volume() - 2) < 1e-6, "STL cubes are outward-facing with the expected volume");
    var split = MeshImporter.Process(stl, new MeshImportOptions { Split = ImportSplit.Groups });
    Check(split.Parts.Count == 2 && split.Parts.All(p => p.Mesh.TriCount == 12) && split.Parts[1].Name == "right", "split by file parts gives one body per STL solid");
    var one = MeshImporter.Process(stl, new MeshImportOptions());
    Check(one.Parts.Count == 1 && one.TriCount == 24, "STL stays one body by default");
    var comps = MeshImporter.Process(stl, new MeshImportOptions { Split = ImportSplit.Components });
    Check(comps.Parts.Count == 2, "split by connected pieces finds both cubes");
    File.WriteAllBytes(F("bin.stl"), MeshIO.SaveStl(Box(0, 0, 0, 2, 3, 4)));
    Check(MeshIO.Read(F("bin.stl")).Mesh.TriCount == 12 && MeshIO.Load(F("bin.stl")).VertexCount == 8, "binary STL reads back, corners merged");

    // ================= units + size check
    var big = MeshIO.Read(F("bin.stl"));
    var cm = MeshImporter.Process(big, new MeshImportOptions { Units = ImportUnits.Centimeter });
    Check(Math.Abs(cm.SizeMm.X - 20) < 1e-3 && Math.Abs(cm.SizeMm.Z - 40) < 1e-3, "centimetre option scales by 10");
    var inch = MeshImporter.Process(big, new MeshImportOptions { Units = ImportUnits.Inch });
    Check(Math.Abs(inch.SizeMm.Z - 101.6) < 1e-3, "inch option scales by 25.4");
    var metre = MeshImporter.Process(big, new MeshImportOptions { Units = ImportUnits.Meter });
    Check(Math.Abs(metre.SizeMm.Z - 4000) < 1e-3 && metre.SizeWarning != null && metre.SizeWarning.Contains("did you mean") && metre.SizeWarning.Contains("4,000"), "a 4 m result warns 'did you mean ...' with the size: " + metre.SizeWarning);
    Check(MeshImporter.Process(big).SizeWarning == null, "a sensible size gives no warning");
    var hint = MeshImporter.SizeHint(25400, 1, "millimetres");
    Check(hint != null && hint.Contains("25,400 mm"), "a 25 400 mm model is flagged: " + hint);
    var inchHint = MeshImporter.SizeHint(1000, 25.4, "inches");
    Check(inchHint != null && inchHint.Contains("25,400 mm") && inchHint.Contains("did you mean millimetres"), "1000 read as inches suggests millimetres: " + inchHint);
    var tiny = MeshImporter.SizeHint(0.05, 1, "millimetres");
    Check(tiny != null && tiny.Contains("metres"), "a 0.05 mm model suggests larger units: " + tiny);

    string Make3mf(string path, string unit, string objects, string build, string extraResources = "")
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string n, string c) { using var w = new StreamWriter(zip.CreateEntry(n).Open(), new UTF8Encoding(false)); w.Write(c); }
        Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>");
        Put("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"r0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
        Put("3D/3dmodel.model", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><model unit=\"{unit}\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources>{extraResources}{objects}</resources><build>{build}</build></model>");
        return path;
    }
    string MeshXml(TriMesh m, string triAttrs = "")
    {
        var sb = new StringBuilder("<mesh><vertices>");
        for (int i = 0; i < m.VertexCount; i++) sb.Append($"<vertex x=\"{N(m.V[3 * i])}\" y=\"{N(m.V[3 * i + 1])}\" z=\"{N(m.V[3 * i + 2])}\"/>");
        sb.Append("</vertices><triangles>");
        for (int t = 0; t < m.TriCount; t++) sb.Append($"<triangle v1=\"{m.T[3 * t]}\" v2=\"{m.T[3 * t + 1]}\" v3=\"{m.T[3 * t + 2]}\" {triAttrs}/>");
        return sb.Append("</triangles></mesh>").ToString();
    }

    Make3mf(F("inch.3mf"), "inch", $"<object id=\"1\" type=\"model\" name=\"Block\">{MeshXml(Box(0, 0, 0, 1, 2, 3))}</object>", "<item objectid=\"1\"/>");
    var f3 = MeshIO.Read(F("inch.3mf"));
    Check(f3.Unit == "inch" && f3.UnitToMm == 25.4, "3MF unit attribute is read");
    var auto3 = MeshImporter.Process(f3);
    Check(Math.Abs(auto3.SizeMm.Z - 76.2) < 1e-3 && auto3.UnitUsed == "inches", "Auto units use the 3MF's inch unit");
    var forced = MeshImporter.Process(f3, new MeshImportOptions { Units = ImportUnits.Millimeter });
    Check(Math.Abs(forced.SizeMm.Z - 3) < 1e-3, "an explicit unit overrides the 3MF's");
    Check(Math.Abs(MeshImporter.Process(big).SizeMm.Z - 4) < 1e-3, "Auto means millimetres for formats without units");

    // ================= up axis
    var tall = MeshIO.Read(F("bin.stl"));                     // 2 x 3 x 4
    var yUp = MeshImporter.Process(tall, new MeshImportOptions { UpAxis = ImportUpAxis.Y }).Parts[0].Mesh;
    Check(Math.Abs(yUp.Size.X - 2) < 1e-4 && Math.Abs(yUp.Size.Y - 4) < 1e-4 && Math.Abs(yUp.Size.Z - 3) < 1e-4, "Y-up: old Y becomes Z, old Z becomes -Y");
    Check(Math.Abs(yUp.Min.Y + 4) < 1e-4 && Math.Abs(yUp.Volume() - 24) < 1e-4, "Y-up rotation is proper (volume stays positive)");
    var xUp = MeshImporter.Process(tall, new MeshImportOptions { UpAxis = ImportUpAxis.X }).Parts[0].Mesh;
    Check(Math.Abs(xUp.Size.Z - 2) < 1e-4 && Math.Abs(xUp.Size.X - 4) < 1e-4 && Math.Abs(xUp.Volume() - 24) < 1e-4, "X-up: old X becomes Z");
    var zUp = MeshImporter.Process(tall).Parts[0].Mesh;
    Check(Math.Abs(zUp.Size.Z - 4) < 1e-4, "Z-up keeps the file as it is");

    // ================= weld
    File.WriteAllText(F("gap.stl"), "solid s\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nfacet normal 0 0 1\nouter loop\nvertex 1.0004 0 0\nvertex 1 1 0\nvertex 0 1.0003 0\nendloop\nendfacet\nendsolid s\n");
    var gap = MeshIO.Read(F("gap.stl"));
    Check(gap.Mesh.VertexCount == 6, "bit-exact merge leaves near-identical corners apart");
    Check(MeshImporter.Process(gap, new MeshImportOptions { WeldTolerance = 0.01 }).Parts[0].Mesh.VertexCount == 4, "a weld tolerance merges near-identical corners (via MeshOps.Weld)");
    Check(MeshIO.FromSoup(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.0004f, 0, 0, 1, 1, 0, 0, 1.0003f, 0 }, 0.01).VertexCount == 4, "FromSoup accepts a weld tolerance");

    // ================= auto repair
    var cube = Box(0, 0, 0, 1, 1, 1); var jv = new List<float>(); var jt = new List<int>();
    for (int t = 0; t < cube.TriCount; t++)
        for (int k = 0; k < 3; k++) { var p = cube.P(cube.T[3 * t + k]); jt.Add(jv.Count / 3); jv.Add((float)(p.X + (t % 3) * 5e-7)); jv.Add((float)p.Y); jv.Add((float)p.Z); }
    File.WriteAllBytes(F("split.stl"), MeshIO.SaveStl(new TriMesh([.. jv], [.. jt])));
    var soupMesh = MeshIO.Read(F("split.stl")).Mesh;
    var rep = MeshImporter.Import(F("split.stl"), new MeshImportOptions { AutoRepair = true });
    Check(soupMesh.VertexCount > 8 && rep.Parts[0].Mesh.VertexCount == 8 && MeshAnalysis.Report(rep.Parts[0].Mesh).Printable && rep.Messages.Any(m => m.StartsWith("Repaired")), "auto-repair welds a jittered soup and reports a printable result");

    // ================= colours: PLY
    File.WriteAllText(F("col.ply"), "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty uchar alpha\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0 255 0 0 255\n1 0 0 0 255 0 128\n0 1 0 0 0 255 255\n3 0 1 2\n");
    var ply = MeshIO.Read(F("col.ply")).Mesh;
    Check(ply.VertexColors is { Length: 3 } && ply.VertexColors[0] == 0xFFFF0000 && ply.VertexColors[1] == 0x8000FF00 && ply.VertexColors[2] == 0xFF0000FF, "PLY red/green/blue/alpha vertex colours are read");
    Check(ply.ColorDisplay != null && ply.ColorDisplay.TriangleCount == 1 && ply.ColorDisplay.Scalars!.Length == ply.ColorDisplay.VertexCount, "coloured meshes provide a colour render mesh");
    File.WriteAllBytes(F("round.ply"), MeshIO.SavePly(ply));
    var back = MeshIO.Read(F("round.ply")).Mesh;
    Check(back.VertexColors != null && back.VertexColors.SequenceEqual(ply.VertexColors!), "binary PLY colours round-trip");
    Check(MeshIO.FromBase64(MeshIO.ToBase64(ply)).VertexColors!.SequenceEqual(ply.VertexColors!) && !MeshIO.FromBase64(MeshIO.ToBase64(Box(0, 0, 0, 1, 1, 1))).HasColors, "colours survive the .prism storage format and uncoloured data still loads");
    var colored = Box(0, 0, 0, 1, 1, 1).WithColors(Enumerable.Range(0, 24).Select(i => (uint)(0xFF000000 | i)).ToArray(), null);
    Check(colored.Transformed(p => p + new Vec3(1, 0, 0)).VertexColors!.Length == 24 && colored.Subset([0, 1]).VertexColors!.Length == 4, "transform and subset keep vertex colours");
    Check(MeshOps.Weld(colored, 1e-6).VertexColors == null || MeshOps.Weld(colored, 1e-6).VertexColors!.Length == 8, "weld keeps colours consistent with its vertices");
    Check(MeshOps.Clean(colored).mesh.VertexColors != null, "clean keeps vertex colours");
    Check(TriMesh.Merge([colored, Box(3, 0, 0, 4, 1, 1)]).FaceColors is { Length: 24 }, "merging coloured with plain meshes gives per-face colours");
    var dec = MeshOps.Decimate(colored, 6);
    Check(dec.VertexColors == null || dec.VertexColors.Length == dec.VertexCount, "decimate never leaves mismatched colour arrays");
    Check(new TriMesh([0, 0, 0], [], [1, 2, 3]).VertexColors == null, "a wrong-length colour array is dropped, not kept");

    // ================= colours + parts: OBJ
    File.WriteAllText(F("m.mtl"), "newmtl red\nKd 1 0 0\nnewmtl blue\nKd 0 0 1\nnewmtl broken\nKd x y\n");
    File.WriteAllText(F("m.obj"), "# test\nmtllib m.mtl\no cubeA\nusemtl red\n" + ObjBox(0, 0, 0, 1, 1, 1) + "o cubeB\nusemtl blue\n" + ObjBox(3, 0, 0, 4, 1, 1, 24));
    var obj = MeshIO.Read(F("m.obj"));
    Check(obj.PartCount == 2 && obj.PartNames[0] == "cubeA" && obj.PartNames[1] == "cubeB" && obj.Mesh.TriCount == 24, "OBJ 'o' objects become parts, quads are triangulated");
    var objParts = MeshImporter.Process(obj, new MeshImportOptions { Split = ImportSplit.Groups });
    Check(objParts.Parts.Count == 2 && objParts.Parts[0].Color == 0xFFFF0000 && objParts.Parts[1].Color == 0xFF0000FF, "MTL Kd colours become the colour of each OBJ part");
    var objOne = MeshImporter.Process(obj);
    Check(objOne.Parts.Count == 1 && objOne.Parts[0].Mesh.FaceColors is { Length: 24 } && objOne.Parts[0].Mesh.FaceColors![0] == 0xFFFF0000 && objOne.Parts[0].Mesh.FaceColors![23] == 0xFF0000FF, "merged OBJ keeps per-face material colours");

    File.WriteAllText(F("grp.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\ng top\nf 1/1/1 2/2/2 3/3/3\ng bottom\nf 2//1 4//1 3//1\n");
    var grp = MeshIO.Read(F("grp.obj"));
    Check(grp.PartNames.SequenceEqual(["top", "bottom"]) && grp.Mesh.TriCount == 2, "OBJ 'g' groups and v/vt/vn, v//vn corners parse");
    File.WriteAllText(F("neg.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf -4 -3 -2\nf -4 -2 -1\nf 1 2 4\n");
    var neg = MeshIO.Read(F("neg.obj")).Mesh;
    Check(neg.TriCount == 3 && neg.T[0] == 0 && neg.T[1] == 1 && neg.T[2] == 2 && neg.T[3] == 0 && neg.T[5] == 3, "OBJ negative (relative) indices resolve");
    File.WriteAllText(F("vc.obj"), "v 0 0 0 1 0 0\nv 1 0 0 0 1 0\nv 0 1 0 0 0 1\nf 1 2 3\n");
    Check(MeshIO.Read(F("vc.obj")).Mesh.VertexColors is { } ovc && ovc[0] == 0xFFFF0000 && ovc[2] == 0xFF0000FF, "OBJ 'v x y z r g b' vertex colours are read");
    File.WriteAllText(F("ptr.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvn 0 0 1\ns off\nl 1 2\np 3\nf 1 2\nf 1 2 3\n");
    Check(MeshIO.Read(F("ptr.obj")).Mesh.TriCount == 1, "unknown directives and too-short faces are skipped");

    // ================= 3MF: multi-part, colours, transforms
    var m1 = Box(0, 0, 0, 1, 1, 1);
    Make3mf(F("multi.3mf"), "millimeter",
        $"<object id=\"1\" type=\"model\" name=\"Base\">{MeshXml(m1, "pid=\"9\" p1=\"0\"")}</object><object id=\"2\" type=\"model\" name=\"Lid\">{MeshXml(m1, "pid=\"9\" p1=\"1\"")}</object>",
        "<item objectid=\"1\"/><item objectid=\"2\" transform=\"1 0 0 0 1 0 0 0 1 10 0 0\"/>",
        "<basematerials id=\"9\"><base name=\"Red\" displaycolor=\"#FF0000FF\"/><base name=\"Green\" displaycolor=\"#00FF00\"/></basematerials>");
    var multi = MeshIO.Read(F("multi.3mf"));
    Check(multi.PartCount == 2 && multi.PartNames[0] == "Base" && multi.PartNames[1] == "Lid", "3MF objects are separate named parts");
    var multiRes = MeshImporter.Process(multi);                  // Auto splits 3MF
    Check(multiRes.Parts.Count == 2 && multiRes.Parts[0].Name == "Base" && Math.Abs(multiRes.Parts[1].Mesh.Min.X - 10) < 1e-4, "3MF yields one body per object, honouring the item transform");
    Check(multiRes.Parts[0].Color == 0xFFFF0000 && multiRes.Parts[1].Color == 0xFF00FF00, "3MF base material colours are kept (#RRGGBBAA and #RRGGBB)");
    Check(MeshImporter.Process(multi, new MeshImportOptions { Split = ImportSplit.None }).Parts.Count == 1, "3MF can be forced into one body");

    // the app's own exporter -> importer round trip
    MeshIO.Save3mf([Box(0, 0, 0, 1, 1, 1), Box(5, 5, 5, 6, 7, 8)], F("exp.3mf"), ["Alpha", "Beta"]);
    var exp = MeshImporter.Import(F("exp.3mf"));
    Check(exp.Parts.Count == 2 && exp.Parts[0].Name == "Alpha" && exp.Parts[1].Name == "Beta" && exp.TriCount == 24, "the 3MF exporter's bodies come back as separate named bodies");

    Make3mf(F("comp.3mf"), "millimeter",
        $"<object id=\"1\" type=\"model\" name=\"Leaf\">{MeshXml(m1)}</object><object id=\"2\" type=\"model\" name=\"Pair\"><components><component objectid=\"1\"/><component objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 0 5 0\"/></components></object>",
        "<item objectid=\"2\" transform=\"1 0 0 0 1 0 0 0 1 0 0 7\"/>");
    var comp = MeshIO.Read(F("comp.3mf"));
    Check(comp.PartCount == 1 && comp.Mesh.TriCount == 24 && Math.Abs(comp.Mesh.Min.Z - 7) < 1e-4 && Math.Abs(comp.Mesh.Max.Y - 6) < 1e-4, "3MF components compose component and item transforms");
    Make3mf(F("mirror.3mf"), "millimeter", $"<object id=\"1\" name=\"M\">{MeshXml(m1)}</object>", "<item objectid=\"1\" transform=\"-1 0 0 0 1 0 0 0 1 0 0 0\"/>");
    Check(MeshIO.Read(F("mirror.3mf")).Mesh.Volume() > 0.99, "a mirroring 3MF transform keeps triangles outward-facing");

    // ================= robustness
    var cultureBefore = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
    try
    {
        File.WriteAllText(F("cul.obj"), "v 0.5 0 0\nv 1.5 0 0\nv 0 2.25 0\nf 1 2 3\n");
        var cul = MeshIO.Read(F("cul.obj")).Mesh;
        Check(Math.Abs(cul.V[3] - 1.5f) < 1e-6 && Math.Abs(cul.V[7] - 2.25f) < 1e-6, "numbers parse with the invariant culture (de-DE machine)");
    }
    finally { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = cultureBefore; }

    File.WriteAllText(F("bad1.obj"), "v 0 0 0\nv 1 0 0\n# fine\nv 1 abc 0\nf 1 2 3\n");
    var e1 = Throws<InvalidDataException>(() => MeshIO.Read(F("bad1.obj")), "bad OBJ vertex");
    Check(e1.Message.Contains("line 4") && e1.Message.Contains("bad1.obj"), "a bad OBJ vertex reports file and line number: " + e1.Message);
    File.WriteAllText(F("bad2.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 9\n");
    var e2 = Throws<InvalidDataException>(() => MeshIO.Read(F("bad2.obj")), "OBJ index out of range");
    Check(e2.Message.Contains("line 4") && e2.Message.Contains('9'), "an out-of-range OBJ face index reports its line: " + e2.Message);
    File.WriteAllText(F("bad3.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 x/1 3\n");
    Check(Throws<InvalidDataException>(() => MeshIO.Read(F("bad3.obj")), "OBJ bad corner").Message.Contains("line 4"), "a non-numeric OBJ face corner reports its line");
    File.WriteAllText(F("bad.stl"), "solid s\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 zz\nvertex 0 1 0\nendloop\nendfacet\nendsolid\n");
    Check(Throws<InvalidDataException>(() => MeshIO.Read(F("bad.stl")), "STL bad vertex").Message.Contains("line 5"), "a bad ASCII STL vertex reports its line");
    File.WriteAllText(F("short.stl"), "solid s\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nendloop\nendfacet\nendsolid s\n");
    Check(Throws<InvalidDataException>(() => MeshIO.Read(F("short.stl")), "STL incomplete facet").Message.Contains("multiple of 3"), "an incomplete STL facet is reported");
    var trunc = MeshIO.SaveStl(Box(0, 0, 0, 1, 1, 1)); File.WriteAllBytes(F("trunc.stl"), trunc[..(trunc.Length - 70)]);
    var tr = MeshIO.Read(F("trunc.stl"));
    Check(tr.Mesh.TriCount == 10 && tr.Warnings.Count == 1, "a truncated binary STL imports what is there and warns");
    File.WriteAllBytes(F("junk.stl"), new byte[20]);
    Throws<InvalidDataException>(() => MeshIO.Read(F("junk.stl")), "junk STL");
    File.WriteAllText(F("bad.ply"), "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 7\n");
    Check(Throws<InvalidDataException>(() => MeshIO.Read(F("bad.ply")), "PLY bad index").Message.Contains("vertex 7"), "a PLY face with a bad vertex index is reported");
    File.WriteAllText(F("notply.ply"), "hello\n");
    Throws<InvalidDataException>(() => MeshIO.Read(F("notply.ply")), "not a PLY");
    File.WriteAllText(F("cut.ply"), "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n1 0\n");
    Throws<InvalidDataException>(() => MeshIO.Read(F("cut.ply")), "PLY ends early");
    File.WriteAllText(F("fake.3mf"), "not a zip");
    Throws<InvalidDataException>(() => MeshIO.Read(F("fake.3mf")), "3MF not a zip");
    Make3mf(F("badidx.3mf"), "millimeter", "<object id=\"1\"><mesh><vertices><vertex x=\"0\" y=\"0\" z=\"0\"/></vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object>", "<item objectid=\"1\"/>");
    Throws<InvalidDataException>(() => MeshIO.Read(F("badidx.3mf")), "3MF bad index");
    Make3mf(F("badxml.3mf"), "millimeter", "<object id=\"1\"><mesh><vertices><vertex x=\"0\" y=\"0\" z=\"q\"/></vertices></mesh></object>", "<item objectid=\"1\"/>");
    Check(Throws<InvalidDataException>(() => MeshIO.Read(F("badxml.3mf")), "3MF bad number").Message.Contains("line"), "a bad 3MF vertex reports a line number");
    File.WriteAllText(F("x.txt"), "x");
    Throws<InvalidDataException>(() => MeshIO.Read(F("x.txt")), "unknown extension");
    File.WriteAllText(F("empty.obj"), "# nothing\n");
    Check(MeshImporter.Import(F("empty.obj")).Parts.Count == 0, "a file without triangles yields no bodies (the session reports it)");

    // ================= progress, cancellation, async
    var bigObj = new StringBuilder();
    for (int i = 0; i < 60000; i++) bigObj.Append($"v {i % 300} {i / 300} {(i * 7) % 13}\n");
    for (int i = 0; i < 59000; i++) bigObj.Append($"f {i + 1} {i + 2} {i + 3}\n");
    File.WriteAllText(F("big.obj"), bigObj.ToString());
    var reports = new List<ImportProgress>();
    var res = MeshImporter.Import(F("big.obj"), null, new SyncProgress<ImportProgress>(reports.Add));
    Check(reports.Count >= 3 && reports.All(r => r.Fraction is >= 0 and <= 1 && r.Status.Length > 0) && reports[^1].Fraction == 1 && res.TriCount == 59000, $"large files report progress with status text ({reports.Count} reports)");
    var cts = new CancellationTokenSource(); cts.Cancel();
    Throws<OperationCanceledException>(() => MeshIO.Read(F("big.obj"), null, cts.Token), "pre-cancelled read");
    var cts2 = new CancellationTokenSource();
    Throws<OperationCanceledException>(() => MeshIO.Read(F("big.obj"), new SyncProgress<ImportProgress>(p => { if (p.Fraction < 1) cts2.Cancel(); }), cts2.Token), "cancel mid-read");
    Check(true, "cancelling during a read stops it with OperationCanceledException");
    int callerThread = Environment.CurrentManagedThreadId; int workThread = -1;
    var asyncRes = await MeshImporter.ImportAsync(F("big.obj"), null, new SyncProgress<ImportProgress>(_ => workThread = Environment.CurrentManagedThreadId));
    Check(asyncRes.TriCount == 59000 && workThread != -1 && workThread != callerThread, "ImportAsync parses on a worker thread");
    var cts3 = new CancellationTokenSource(); cts3.Cancel();
    bool cancelled = false; try { await MeshImporter.ImportAsync(F("big.obj"), null, null, cts3.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "ImportAsync honours a cancelled token");

    // ================= session integration: separate SceneObjects, colour, options
    var session = new Prism.Core.CadSession();
    var made = session.ImportMeshAll(F("multi.3mf"));
    Check(made.Count == 2 && session.Objects.Count(o => o.Tri != null) == 2 && made[0].Name.StartsWith("Base") && made[0].Color == 0xFFFF0000, "a multi-part 3MF becomes separate, named, coloured SceneObjects");
    var one3 = session.ImportMesh(F("bin.stl"), new MeshImportOptions { Units = ImportUnits.Centimeter, UpAxis = ImportUpAxis.Y });
    Check(one3.Tri != null && Math.Abs(one3.Tri.Size.Z - 30) < 1e-3, "ImportMesh(path, options) applies the options; ImportMesh(path) keeps working with defaults");
    Check(session.ImportMesh(F("col.ply")).Tri!.VertexColors != null, "vertex colours reach the scene object's mesh");
    var asyncObjs = await session.ImportMeshAsync(F("big.obj"));
    Check(asyncObjs.Count == 1, "CadSession.ImportMeshAsync adds the bodies");
    Throws<InvalidDataException>(() => session.ImportMesh(F("empty.obj")), "empty import");

    // the options step (Import Mesh panel): reads in the background, applies options, commits as one step; remembered options and auto mode
    Prism.Commands.MeshImportPrefs.File = F("prefs.json");
    async Task<bool> Until(Func<bool> c) { for (int i = 0; i < 300 && !c(); i++) await Task.Delay(20); return c(); }
    var s2 = new Prism.Core.CadSession(); int before = s2.Objects.Count;
    var cmd = new Prism.Commands.ImportMeshCommand(F("bin.stl"));
    s2.StartCommand(cmd);
    Check(await Until(() => cmd.Results.Any(r => r.Label == "File")), "the options step reads the file in the background and shows its details");
    cmd.Params.OfType<Prism.Commands.ChoiceParam>().First(p => p.Label.StartsWith("Units")).Index = 2;      // centimetres
    Check(await Until(() => cmd.Results.Any(r => r.Label == "Size" && r.Value.Contains("40"))), "changing an option re-processes in the background and updates the size read-out");
    s2.CommitCommand();
    Check(s2.Objects.Count == before + 1 && Math.Abs(s2.Objects[^1].Tri!.Size.Z - 40) < 1e-3 && File.Exists(F("prefs.json")), "committing imports with the chosen options and remembers them");
    var cmd2 = new Prism.Commands.ImportMeshCommand(F("bin.stl"));
    s2.StartCommand(cmd2);
    Check(cmd2.Params.OfType<Prism.Commands.ChoiceParam>().First(p => p.Label.StartsWith("Units")).Index == 2 || await Until(() => cmd2.Params.OfType<Prism.Commands.ChoiceParam>().First(p => p.Label.StartsWith("Units")).Index == 2), "the remembered options are the defaults next time");
    s2.CancelCommand();
    var s3 = new Prism.Core.CadSession();
    new Prism.Commands.MeshImportPrefs(2, 0, 0, false, 0, false).Save();
    s3.StartCommand(new Prism.Commands.ImportMeshCommand(F("bin.stl"), true));
    Check(await Until(() => s3.Objects.Count == 1) && Math.Abs(s3.Objects[0].Tri!.Size.Z - 40) < 1e-3, "with 'show options' off the import runs by itself using the remembered options");
}
finally { try { Directory.Delete(dir, true); } catch { } }
Console.WriteLine($"SUCCESS: {checks} checks");

sealed class SyncProgress<T>(Action<T> a) : IProgress<T> { public void Report(T v) => a(v); }
