using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>Reduce faces on import (MSH-P01) and the streamed OBJ reader (MSH-P02).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide, import.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class MeshImportReduceTests
{
    static string WriteObj(TriMesh mesh, bool quadsAndNegative = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prism-import-{Guid.NewGuid():N}.obj");
        var sb = new StringBuilder("# test\no part\n");
        for (int i = 0; i < mesh.VertexCount; i++)
            sb.Append(CultureInfo.InvariantCulture, $"v {mesh.V[3 * i]} {mesh.V[3 * i + 1]} {mesh.V[3 * i + 2]}\n");
        for (int t = 0; t < mesh.TriCount; t++)
        {
            int a = mesh.T[3 * t] + 1, b = mesh.T[3 * t + 1] + 1, c = mesh.T[3 * t + 2] + 1;
            if (quadsAndNegative && t == mesh.TriCount - 1) sb.Append(CultureInfo.InvariantCulture, $"f {a - mesh.VertexCount - 1}//1 {b - mesh.VertexCount - 1}//1 {c - mesh.VertexCount - 1}//1\n");
            else sb.Append(CultureInfo.InvariantCulture, $"f {a} {b} {c}\n");
        }
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    [AvaloniaFact(DisplayName = "MSH-H148 | Reduce faces is off by default and, when on, imports a quarter of a closed mesh's triangles")]
    [Trait("TestId", "MSH-H148")]
    public void ReduceOnImport()
    {
        var sphere = SphereMesh(20);
        var path = WriteObj(sphere);
        try
        {
            var file = MeshIO.Read(path);
            Assert.Equal(sphere.TriCount, file.Mesh.TriCount);
            var plain = MeshImporter.Process(file);
            Assert.Equal(sphere.TriCount, plain.TriCount);
            Assert.Equal(plain.TriCount, plain.TriCountBeforeReduce);

            var reduced = MeshImporter.Process(file, new MeshImportOptions { Reduce = true, ReducePercent = 25 });
            AssertClose(sphere.TriCount * 0.25, reduced.TriCount, sphere.TriCount * 0.01, "reduced triangles");
            Assert.Equal(sphere.TriCount, reduced.TriCountBeforeReduce);
            Assert.Equal(0, reduced.Parts[0].Mesh.Topology().BoundaryEdges);
            Assert.Contains(reduced.Messages, m => m.StartsWith("Reduced from"));

            var counted = MeshImporter.Process(file, new MeshImportOptions { Reduce = true, ReduceToCount = 1000 });
            AssertClose(1000, counted.TriCount, 10, "triangle count target");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "MSH-H149 | Import Mesh shows the reduction and the Undo step removes the whole import")]
    [Trait("TestId", "MSH-H149")]
    public void ImportCommandReports()
    {
        var prefs = MeshImportPrefs.File;
        MeshImportPrefs.File = Path.Combine(Path.GetTempPath(), $"prism-prefs-{Guid.NewGuid():N}.json");
        var path = WriteObj(SphereMesh(20));
        try
        {
            new MeshImportPrefs(Reduce: true, ReducePercent: 25).Save();
            var session = new CadSession();
            var command = Begin(session, () => new ImportMeshCommand(path));
            for (int i = 0; i < 400 && command.Error == "Still reading the file."; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
            session.CommitCommand();
            var mesh = Assert.Single(session.Objects, o => o.Kind == ObjKind.Mesh);
            Assert.True(mesh.Tri!.TriCount < SphereMesh(20).TriCount / 3);
            session.Undo();
            Assert.DoesNotContain(session.Objects, o => o.Kind == ObjKind.Mesh);
        }
        finally { File.Delete(path); File.Delete(MeshImportPrefs.File); MeshImportPrefs.File = prefs; }
    }

    [AvaloniaFact(DisplayName = "MSH-H150 | The streamed OBJ reader keeps parts, polygon fans and negative indices")]
    [Trait("TestId", "MSH-H150")]
    public void StreamedObjReader()
    {
        var box = BoxMesh(Prism.Kernel.Vec3.Zero, 10, 10, 10);
        var path = WriteObj(box, quadsAndNegative: true);
        try
        {
            File.AppendAllText(path, "g lid\nv 0 0 20\nv 10 0 20\nv 10 10 20\nv 0 10 20\nf -4 -3 -2 -1\n");
            var file = MeshIO.Read(path);
            Assert.Equal(box.TriCount + 2, file.Mesh.TriCount);
            Assert.Equal(2, file.PartCount);
            Assert.Equal(box.T.Take(box.T.Length - 3), file.Mesh.T.Take(box.T.Length - 3));
        }
        finally { File.Delete(path); }
    }
}
