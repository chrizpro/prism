using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Automation;

/// <summary>What an agent reads back while building a Freeform: edit replies that describe the change, and numeric/text probes of the shape.
/// Requirements are the approved items in docs/freeform-studies/agent-tooling-proposal.md (P2 shape probes, P3 edit replies).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/freeform-studies/agent-tooling-proposal.md">Freeform for agents.</seealso>
[Trait("Feature", "Automation")]
[Trait("Layer", "Headless")]
public class FreeformAgentToolTests
{
    static JsonObject Args(object value) => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value))!.AsObject();

    static async Task<(AutomationApi api, string name)> NewBox(double w, double d, double h, int fx = 2, int fy = 2, int fz = 2)
    {
        var api = new AutomationApi(new MainViewModel(), null);
        var made = await api.CallAsync("run_command", Args(new { name = "form_box", @params = new Dictionary<string, object> { ["Width"] = w, ["Depth"] = d, ["Height"] = h, ["Faces X"] = fx, ["Faces Y"] = fy, ["Faces Z"] = fz } }));
        return (api, (string)made!["created"]![0]!["name"]!);
    }

    [AvaloniaFact(DisplayName = "API-H003 | A Freeform extrude reply names the appended vertices and faces, the new bounds and the size change")]
    [Trait("TestId", "API-H003")]
    public async Task ExtrudeReplyDescribesTheChange()
    {
        var (api, name) = await NewBox(40, 40, 40);
        var before = (await api.CallAsync("form_info", Args(new { @object = name, summary = true })))!;
        int v0 = (int)before["vertexCount"]!, f0 = (int)before["faceCount"]!;
        var top = (await api.CallAsync("form_find", Args(new { @object = name, kind = "faces", facing = new[] { 0, 0, 1 }, angle = 10 })))!["results"]![0]!["index"]!.GetValue<int>();
        var r = (await api.CallAsync("form_op", Args(new { @object = name, op = "extrude_faces", faces = new[] { top }, distance = 10 })))!;
        var created = r["created"]!;
        Assert.Equal(v0, (int)created["vertices"]![0]!);
        Assert.Equal((int)r["vertexCount"]! - 1, (int)created["vertices"]![1]!);
        Assert.Equal(f0, (int)created["faces"]![0]!);
        Assert.Equal((int)r["faceCount"]! - 1, (int)created["faces"]![1]!);
        Assert.Equal(50, (double)r["bounds"]!["max"]![2]!, 3);
        Assert.Equal(10, (double)r["sizeChange"]![2]!, 3);
        Assert.Equal(0, (double)r["sizeChange"]![0]!, 3);
        Assert.True(r["warnings"] == null, r["warnings"]?.ToJsonString());
    }

    [AvaloniaFact(DisplayName = "API-H004 | A Freeform move reports how many points moved and the largest distance, and nothing was created")]
    [Trait("TestId", "API-H004")]
    public async Task MoveReplyReportsDisplacement()
    {
        var (api, name) = await NewBox(40, 40, 40);
        var r = (await api.CallAsync("form_op", Args(new { @object = name, op = "move", vertices = new[] { 0 }, delta = new[] { 3.0, 4.0, 0.0 } })))!;
        Assert.Equal(1, (int)r["moved"]!["count"]!);
        Assert.Equal(5, (double)r["moved"]!["maxDistance"]!, 3);
        Assert.Equal(0, (int)r["moved"]!["maxAtVertex"]!);
        Assert.Null(r["created"]);
    }

    [AvaloniaFact(DisplayName = "API-H005 | Dragging a Freeform corner through the opposite side warns that faces folded over")]
    [Trait("TestId", "API-H005")]
    public async Task FoldOverIsReportedAsAWarning()
    {
        var (api, name) = await NewBox(40, 40, 40, 1, 1, 1);
        // all four top corners dropped far below the bottom: the whole top and bottom swap sides
        var info = (await api.CallAsync("form_find", Args(new { @object = name, kind = "vertices", box = new[] { new[] { -30, -30, 30 }, new[] { 30, 30, 50 } } })))!;
        var ids = info["results"]!.AsArray().Select(x => (int)x!["index"]!).ToArray();
        var r = (await api.CallAsync("form_op", Args(new { @object = name, op = "move", vertices = ids, delta = new[] { 0.0, 0.0, -100.0 } })))!;
        Assert.True((int)r["flippedFaces"]! > 0);
        Assert.Contains("folded", r["warnings"]!.ToJsonString());
    }

    [AvaloniaFact(DisplayName = "API-H006 | solid_profile reports a Freeform box's size, sections with exact extents and area, and a filled silhouette")]
    [Trait("TestId", "API-H006")]
    public async Task ProfileOfABoxMatchesItsDimensions()
    {
        // a primitive Freeform box is smoothed (level 3), so it is smaller than its cage: compare against the reported size instead of the cage
        var (api, name) = await NewBox(40, 20, 60);
        var info = (await api.CallAsync("solid_profile", Args(new { @object = name, count = 3, axis = "z", views = new[] { "front", "right", "top" }, width = 40 })))!;
        double sx = (double)info["size"]![0]!, sy = (double)info["size"]![1]!, sz = (double)info["size"]![2]!;
        Assert.InRange(sx, 30, 40.0001); Assert.InRange(sy, 10, 20.0001); Assert.InRange(sz, 50, 60.0001);
        var cuts = info["sections"]!["cuts"]!.AsArray();
        Assert.Equal(3, cuts.Count);
        var mid = cuts[1]!;
        double width = (double)mid["x"]![1]! - (double)mid["x"]![0]!, depth = (double)mid["y"]![1]! - (double)mid["y"]![0]!;
        Assert.Equal(sx, width, 1); Assert.Equal(sy, depth, 1);
        Assert.Equal(1, (int)mid["outlines"]!);
        // the section area of a near-rectangle is close to width x depth (rounded corners lose a little)
        Assert.InRange((double)mid["area"]!, width * depth * 0.85, width * depth * 1.001);
        Assert.Equal(3, info["silhouettes"]!.AsArray().Count);
        var front = info["silhouettes"]![0]!["rows"]!.AsArray().Select(r => r!.ToString()).ToList();
        Assert.Contains(front, row => row.Contains('#'));
        Assert.All(front, row => Assert.True(row.Length <= 40));
    }

    [AvaloniaFact(DisplayName = "API-H007 | solid_profile sections report empty beyond the shape and several outlines for a split cut")]
    [Trait("TestId", "API-H007")]
    public async Task ProfileSectionsShowGapsAndSeparatePieces()
    {
        var (api, name) = await NewBox(10, 10, 10);
        var info = (await api.CallAsync("solid_profile", Args(new { @object = name, at = new[] { 5.0, 500.0 }, views = Array.Empty<string>() })))!;
        var cuts = info["sections"]!["cuts"]!.AsArray();
        Assert.Null(cuts[0]!["empty"]);
        Assert.True((bool)cuts[1]!["empty"]!);
        Assert.Null(info["silhouettes"]);

        // two separate boxes cut between their tops make two outlines in one section
        var second = await api.CallAsync("run_command", Args(new { name = "form_box", @params = new Dictionary<string, object> { ["Width"] = 10, ["Depth"] = 10, ["Height"] = 10, ["Position X"] = 40, ["Faces X"] = 2, ["Faces Y"] = 2, ["Faces Z"] = 2 } }));
        string other = (string)second!["created"]![0]!["name"]!;
        var both = (await api.CallAsync("solid_profile", Args(new { @object = new[] { name, other }, at = new[] { 5.0 }, views = Array.Empty<string>() })))!;
        Assert.Equal(2, (int)both["sections"]!["cuts"]![0]!["outlines"]!);
    }

    [Fact(DisplayName = "API-H008 | ShapeProbe slices a closed box exactly: extents, area, centroid and one outline")]
    [Trait("TestId", "API-H008")]
    public void SliceOfAClosedBoxIsExact()
    {
        // a 4 x 2 x 6 box with its corner at (1, 1, 1), 12 triangles, outward winding
        var (pos, tri) = Box(1, 1, 1, 4, 2, 6);
        var s = ShapeProbe.Slice(pos, tri, 2, 3.5);
        Assert.False(s.Empty);
        Assert.Equal(8, s.Area, 9);
        Assert.Equal(1, s.MinU, 9); Assert.Equal(5, s.MaxU, 9); Assert.Equal(1, s.MinV, 9); Assert.Equal(3, s.MaxV, 9);
        Assert.Equal(3, s.CentroidU, 9); Assert.Equal(2, s.CentroidV, 9);
        Assert.Equal(1, s.Loops);
        Assert.True(ShapeProbe.Slice(pos, tri, 2, 20).Empty);
        // a cut exactly through the top face neither doubles nor loses the outline
        Assert.InRange(ShapeProbe.Slice(pos, tri, 2, 7).Area, 0, 8.0001);
        // other axes: x-section of the box is 2 (y) by 6 (z)
        Assert.Equal(12, ShapeProbe.Slice(pos, tri, 0, 3).Area, 9);
    }

    [Fact(DisplayName = "API-H009 | ShapeProbe silhouette draws a box as a filled rectangle at the stated scale and a thin wall does not vanish")]
    [Trait("TestId", "API-H009")]
    public void SilhouetteOfABoxIsAFilledRectangle()
    {
        var (pos, tri) = Box(0, 0, 0, 40, 10, 20);
        var pic = ShapeProbe.Silhouette(pos, tri, "front", 40, false);
        Assert.Equal(1.0, pic.MmPerColumn, 9);
        Assert.Equal(10, pic.Rows.Count);                        // 20 mm tall at 2 mm per row
        Assert.All(pic.Rows, r => Assert.Equal(new string('#', 40), r));
        var top = ShapeProbe.Silhouette(pos, tri, "top", 40, false);
        Assert.Equal(5, top.Rows.Count);                         // 10 mm deep at 2 mm per row

        // a single upright sheet seen edge-on is still drawn
        float[] sheet = [0, 0, 0, 40, 0, 0, 40, 0, 20, 0, 0, 20];
        var wall = ShapeProbe.Silhouette(sheet, [0, 1, 2, 0, 2, 3], "right", 20, false);
        Assert.Contains(wall.Rows, r => r.Contains('#'));
    }

    [Fact(DisplayName = "API-H010 | ShapeProbe silhouettes of a very large mesh finish (no per-triangle stack growth) and keep to the row limit")]
    [Trait("TestId", "API-H010")]
    public void SilhouetteOfAHugeMeshDoesNotOverflowTheStack()
    {
        // 400 x 400 quads = 320,000 triangles on a gently curved sheet, one million stack bytes would have been used by a stackalloc in the loop
        const int n = 400;
        var pos = new float[(n + 1) * (n + 1) * 3];
        for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) { int k = (j * (n + 1) + i) * 3; pos[k] = i; pos[k + 1] = j; pos[k + 2] = (float)(10 * Math.Sin(i / 40.0)); }
        var tri = new List<int>();
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1; tri.AddRange([a, b, d, a, d, c]); }
        var pic = ShapeProbe.Silhouette(pos, tri.ToArray(), "top", 48, false, 20);
        Assert.InRange(pic.Rows.Count, 1, 20);
        Assert.All(pic.Rows, r => Assert.True(r.Length <= 48));
        var tall = ShapeProbe.Silhouette(pos, tri.ToArray(), "front", 48, true, 12);
        Assert.InRange(tall.Rows.Count, 1, 12);
    }

    [AvaloniaFact(DisplayName = "API-H011 | The guide tool lists the Freeform skill and returns its text so any agent or the in-app assistant can read it")]
    [Trait("TestId", "API-H011")]
    public async Task GuideToolServesTheFreeformSkill()
    {
        var api = new AutomationApi(new MainViewModel(), null);
        var list = (await api.CallAsync("guide", Args(new { })))!;
        Assert.Contains(list["guides"]!.AsArray(), g => (string)g!["topic"]! == "freeform");
        var guide = (await api.CallAsync("guide", Args(new { topic = "freeform" })))!;
        var text = (string)guide["text"]!;
        Assert.DoesNotContain("name: prism-freeform", text);      // front matter is not part of the text
        foreach (var needle in new[] { "form_from_silhouettes", "cad.profile", "views", "flippedFaces" }) Assert.Contains(needle, text);
        var unknown = await Assert.ThrowsAsync<ArgumentException>(() => api.CallAsync("guide", Args(new { topic = "nonsense" })));
        Assert.Contains("freeform", unknown.Message);
    }

    [Fact(DisplayName = "API-H012 | Guide front matter is split from the Markdown body and the description is read")]
    [Trait("TestId", "API-H012")]
    public void GuideFrontMatterIsParsed()
    {
        var (description, body) = AgentGuides.Parse("---\r\nname: prism-x\r\ndescription: How to do x well.\r\n---\r\n\r\n# Title\r\ntext");
        Assert.Equal("How to do x well.", description);
        Assert.StartsWith("# Title", body);
        Assert.Equal(("", "plain"), AgentGuides.Parse("plain"));
    }

    static (float[] pos, int[] tri) Box(double x, double y, double z, double w, double d, double h)
    {
        float[] p = [(float)x, (float)y, (float)z, (float)(x + w), (float)y, (float)z, (float)(x + w), (float)(y + d), (float)z, (float)x, (float)(y + d), (float)z,
            (float)x, (float)y, (float)(z + h), (float)(x + w), (float)y, (float)(z + h), (float)(x + w), (float)(y + d), (float)(z + h), (float)x, (float)(y + d), (float)(z + h)];
        int[] t = [0, 3, 2, 0, 2, 1,   4, 5, 6, 4, 6, 7,   0, 1, 5, 0, 5, 4,   1, 2, 6, 1, 6, 5,   2, 3, 7, 2, 7, 6,   3, 0, 4, 3, 4, 7];
        return (p, t);
    }
}
