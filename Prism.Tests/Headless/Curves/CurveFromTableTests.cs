using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Data;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Curves;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/curves/README.md#curve-from-a-table">Curve from a table.</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class CurveFromTableTests
{
    const string Choice = "Use a table from the Data panel";

    static DataSheet Sheet(CadSession session, string name, params double[][] rows)
    {
        var sheet = new DataSheet { Name = name, Rows = [["x", "y", "z"], .. rows.Select(r => r.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList())] };
        return session.AddSheet(sheet);
    }

    [AvaloniaFact(DisplayName = "CRV-H050 | Curve from Data lists the Data panel's tables and takes its points from the one chosen")]
    [Trait("TestId", "CRV-H050")]
    public void CurveFromAPanelTable()
    {
        var session = new CadSession();
        Sheet(session, "Ribs", [0, 0, 0], [10, 5, 0], [20, 0, 0], [30, 5, 10]);
        Sheet(session, "Other", [0, 0, 0], [1, 1, 1]);
        var command = Begin(session, () => new EquationCurveCommand(true));
        var choice = command.Params.OfType<ChoiceParam>().Single(p => p.Label == Choice);
        Assert.Equal(["None: type or paste the points below", "Ribs", "Other"], choice.Options);
        SetChoice(command, Choice, "Ribs");
        SetChoice(command, "Curve", "Straight segments");
        var curve = Assert.Single(BuildOutput(command).Add).Shape!;
        var props = curve.Props();
        AssertPoint(new Vec3(0, 0, 0), props.Min, 1e-6, "Curve minimum");
        AssertPoint(new Vec3(30, 5, 10), props.Max, 1e-6, "Curve maximum");
        Assert.Equal(3, curve.Count(SubKind.Edge));
    }

    [AvaloniaFact(DisplayName = "CRV-H051 | New table for this curve adds a sheet, points the curve at it and asks for the Data panel")]
    [Trait("TestId", "CRV-H051")]
    public void NewTableForTheCurve()
    {
        var session = new CadSession();
        int asked = 0; session.DataPanelRequested += () => asked++;
        var command = Begin(session, () => new EquationCurveCommand(true));
        command.Params.OfType<ActionParam>().Single(p => p.Label == "Open the Data panel").Run();
        Assert.Equal(1, asked);
        command.Params.OfType<ActionParam>().Single(p => p.Label == "New table for this curve").Run();
        Assert.Equal(2, asked);
        var sheet = Assert.Single(session.DataSheets);
        Assert.Equal("@" + sheet.Name, command.Params.OfType<TextParam>().Single(p => p.Label == "Points").Value);
        Assert.Equal(sheet.Name, command.Params.OfType<ChoiceParam>().Single(p => p.Label == Choice).Options.ElementAt(command.Params.OfType<ChoiceParam>().Single(p => p.Label == Choice).Index));
    }

    [AvaloniaFact(DisplayName = "CRV-H052 | A Curve from Data step saved before the table choice existed reopens with its points and settings")]
    [Trait("TestId", "CRV-H052")]
    public void OldRecipesKeepTheirValues()
    {
        var session = new CadSession();
        var source = Begin(session, () => new EquationCurveCommand(true));
        SetText(source, "Points", "1,2,3; 4,5,6; 7,8,9");
        SetChoice(source, "Curve", "Straight segments");
        var saved = source.Capture()!;
        int at = source.Params.IndexOf(source.Params.OfType<ChoiceParam>().Single(p => p.Label == Choice));
        var old = saved with { Values = saved.Values.Where((_, i) => i < at || i >= at + 3).ToList() };      // the values before the choice and its two buttons were added
        var reopened = Begin(session, () => new EquationCurveCommand(true));
        reopened.Restore(old);
        Assert.Equal("1,2,3; 4,5,6; 7,8,9", reopened.Params.OfType<TextParam>().Single(p => p.Label == "Points").Value);
        Assert.Equal("Straight segments", reopened.Params.OfType<ChoiceParam>().Single(p => p.Label == "Curve").Options.ElementAt(reopened.Params.OfType<ChoiceParam>().Single(p => p.Label == "Curve").Index));
    }
}
