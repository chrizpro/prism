using Xunit;

namespace Prism.IntegrationTests;

/// <summary>Python command feedback documented in docs/Scripting.md and docs/MCP.md.</summary>
[Trait("Feature", "Scripting")]
[Trait("Layer", "Integration")]
public class ScriptCommandReportTests
{
    [Fact(DisplayName = "API-I001 | Python command reports retain trim feedback and ordinary runs return created objects")]
    [Trait("TestId", "API-I001")]
    public async Task PythonReportsInPlaceTrim()
    {
        await using var mcp = new DesktopMcp("API-I001", "Light");
        await mcp.Initialize();
        await mcp.Tool("run_script", new { code = """
            import cadkit as cad
            a, = cad.run("curve_through", points=[(-30,-20,0), (-30,20,0)])
            b, = cad.run("curve_through", points=[(30,-20,0), (30,20,0)])
            assert isinstance(a, cad.Obj)
            surface, = cad.run("loft_surface", slots={"Sections":[a,b]})
            tool = cad.cylinder(radius=5, height=20, position_z=-10, operation="New body")
            args = {"slots":{"Surface to trim":surface, "Cutting tool":tool}}
            preview = cad.run_report("trim_surface", preview=True, **args)
            assert preview.get("activeCommand"), preview
            cad.call("command_cancel")
            report = cad.run_report("trim_surface", **args)
            changed, = report["changed"]
            assert report["created"] == [] and tool.name in report["removed"], report
            assert changed["name"] == surface.name and changed["valid"], changed
            assert abs(changed["areaDelta"] + 25 * 3.14159265) < .01, changed
            """ });
    }
}
