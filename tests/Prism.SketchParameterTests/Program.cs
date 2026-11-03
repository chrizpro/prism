using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message); checks++;
}
double Volume(CadSession session) => session.Objects.Where(o => o.Kind == ObjKind.Solid).Sum(o => Math.Abs(Occ.Mass(o.Shape).Volume));
bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

// a fully dimensioned rectangle whose two sizes are named "width" and "height"
SketchModel Rect(double w, double h, string? widthFormula, string? heightFormula)
{
    var m = new SketchModel();
    int b = m.AddPoint(w, 0), c = m.AddPoint(w, h), d = m.AddPoint(0, h);
    int bottom = m.AddLine(0, b), right = m.AddLine(b, c), top = m.AddLine(c, d), left = m.AddLine(d, 0);
    m.Add(CType.Horizontal, 0, 0, false, bottom); m.Add(CType.Vertical, 0, 0, false, right);
    m.Add(CType.Horizontal, 0, 0, false, top); m.Add(CType.Vertical, 0, 0, false, left);
    var width = m.Add(CType.LineLength, w, 0, true, bottom); width.Name = "width"; width.Formula = widthFormula;
    var height = m.Add(CType.LineLength, h, 0, true, right); height.Name = "height"; height.Formula = heightFormula;
    return m;
}
SceneObject MakeSketch(CadSession session, SketchModel model)
{
    session.EnterSketch(Plane3.XY, model); session.ExitSketch(true);
    return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
}
void Extrude(CadSession session, SceneObject sketch, double distance)
{
    session.SelectItems([SelItem.Of(sketch)]);
    session.StartCommand(() => new ExtrudeCommand());
    session.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Distance").Value = distance;
    session.CommitCommand();
}
SkConstraint Dim(SceneObject sketch, string name) => sketch.SketchData!.Constraints.Single(c => c.Name == name);
SceneObject Fresh(CadSession session, SceneObject sketch) => session.Find(sketch.Id)!;

// ---- names and the parser
Check(SketchParameters.ScopeKey("Sketch 1") == "sketch1" && SketchParameters.ScopeKey("3D Sketch") == "s3dsketch", "a sketch is named in formulas by its object name without spaces");
Check(SketchParameters.ValidName("width") && !SketchParameters.ValidName("doc") && !SketchParameters.ValidName("2x") && !SketchParameters.ValidName("a.b"), "dimension names are plain identifiers and cannot shadow doc or units");
Check(Expression.Parse("sketch1.width * 2 + doc.wall").Eval(n => n switch { "sketch1.width" => 10, "doc.wall" => 3, _ => throw new FormatException(n) }) == 23, "dotted names parse as one name and 2.5 still parses as a number");
Check(Expression.Parse("2.5 + 1").Eval(n => throw new FormatException(n)) == 3.5, "decimals are unaffected by dotted names");

// ---- two sketches that both have a "width" and "height" do not collide
var s = new CadSession();
Check(s.ApplyDocumentParameters([new("wall", "10 mm")]), "a document parameter can be defined");
var one = MakeSketch(s, Rect(40, 20, "wall * 4", null));
var two = MakeSketch(s, Rect(20, 20, "sketch1.width / 2", "sketch1.height"));
Check(one.Name == "Sketch 1" && two.Name == "Sketch 2", "sketches are named Sketch 1 and Sketch 2");
Check(Dim(one, "width").Value == 40 && Dim(two, "width").Value == 20 && Dim(two, "height").Value == 20, "each sketch keeps its own width and height");
Extrude(s, one, 5); Extrude(s, two, 5);
Check(Near(Volume(s), 40 * 20 * 5 + 20 * 20 * 5), "both sketches extrude");

// ---- a parameter drives the first sketch, the first sketch drives the second, features follow
var before = s.Current;
Check(s.ApplyDocumentParameters([new("wall", "15 mm")]) && !s.StatusIsError, "changing a parameter used only by a sketch rebuilds");
Check(Near(Dim(Fresh(s, one), "width").Value, 60) && Near(Dim(Fresh(s, two), "width").Value, 30), "wall drives Sketch 1, and Sketch 1 drives Sketch 2");
Check(Near(Volume(s), 60 * 20 * 5 + 30 * 20 * 5), "the extrusions built on the sketches were rebuilt");
var sk1 = Fresh(s, one).SketchData!;
Check(Near(sk1.Points.Max(p => p.X), 60), "the sketch geometry follows its dimension");
var rebuilt = s.Current;
s.GoTo(before); Check(Near(Dim(Fresh(s, one), "width").Value, 40) && Near(Volume(s), 6000), "history still holds the earlier values");
s.GoTo(rebuilt);

// ---- edits from the panel: rename and re-drive a dimension
var heightOne = Dim(Fresh(s, one), "height");
Check(s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(one.Id, 0, heightOne.Id, "height", "wall * 2")]), "a dimension formula can be set from the panel");
Check(Near(Dim(Fresh(s, one), "height").Value, 30) && Near(Dim(Fresh(s, two), "height").Value, 30), "the dependent sketch follows the edited one");
Check(Near(Volume(s), 60 * 30 * 5 + 30 * 30 * 5), "features follow sketch edits made in the panel");
Check(Dim(Fresh(s, one), "height").Formula == "wall * 2", "the formula is stored with the dimension");

// ---- bad input leaves the document alone
var safe = s.Current; double safeVolume = Volume(s);
Check(!s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(two.Id, 0, Dim(Fresh(s, two), "width").Id, "width", "sketch9.width")]) && s.Current == safe, "a formula naming a missing sketch is refused");
Check(!s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(two.Id, 0, Dim(Fresh(s, two), "width").Id, "width", "height"), new SketchDimEdit(two.Id, 0, Dim(Fresh(s, two), "height").Id, "height", "width")]) && s.Current == safe, "circular dimension formulas are refused");
Check(!s.ApplyDocumentParameters([new("wall", "-3 mm")]) && s.Current == safe && Near(Volume(s), safeVolume), "a parameter that makes a size negative is refused before anything changes");
Check(!s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(two.Id, 0, Dim(Fresh(s, two), "width").Id, "height", null)]) && s.Current == safe, "two dimensions of one sketch cannot share a name");
Check(!s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(two.Id, 0, Dim(Fresh(s, two), "width").Id, "doc", null)]) && s.Current == safe, "doc cannot be used as a dimension name");

// ---- the sketch's own name wins, doc. reaches the parameter
var shadow = MakeSketch(s, Rect(5, 5, null, null));
var wallDim = Dim(shadow, "width");
Check(s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(shadow.Id, 0, wallDim.Id, "wall", "doc.wall + 1")]) && Near(Dim(Fresh(s, shadow), "wall").Value, 16), "doc.wall reaches the parameter even when the sketch has its own wall");
var heightShadow = Dim(Fresh(s, shadow), "height");
Check(s.ApplyDocumentParameters(s.DocumentParameters, [new SketchDimEdit(shadow.Id, 0, heightShadow.Id, "height", "wall")]) && Near(Dim(Fresh(s, shadow), "height").Value, 16), "a bare name prefers the sketch's own dimension");

// ---- saving keeps names and formulas
var path = Path.Combine(Path.GetTempPath(), "prism-sketch-params-" + Guid.NewGuid().ToString("N") + ".prism");
s.Save(path); var reopened = new CadSession(); reopened.Open(path);
var reopenedOne = reopened.Objects.Single(o => o.Name == "Sketch 1");
Check(Dim(reopenedOne, "width").Formula == "wall * 4" && Dim(reopened.Objects.Single(o => o.Name == "Sketch 2"), "width").Formula == "sketch1.width / 2", "names and formulas survive save and reopen");
Check(reopened.ApplyDocumentParameters([new("wall", "20 mm")]) && Near(Dim(reopened.Objects.Single(o => o.Name == "Sketch 1"), "width").Value, 80), "a reopened document still follows its parameters");
File.Delete(path);

// ---- the parameters panel
var vm = new MainViewModel(); var session = vm.Session;
session.ApplyDocumentParameters([new("wall", "10 mm")]);
var a = MakeSketch(session, Rect(40, 20, "wall * 4", null));
var b = MakeSketch(session, Rect(20, 20, "sketch1.width / 2", null));
Extrude(session, a, 5); Extrude(session, b, 5);
vm.OpenStudio("parameters");
Check(vm.SketchDimRows.Count == 4 && vm.HasSketchDims, "the panel lists every sketch dimension");
Check(vm.SketchDimRows.Where(r => r.ShowHeader).Select(r => r.SketchKey).SequenceEqual(new[] { "sketch1", "sketch2" }), "each sketch has its own group and key in the panel");
Check(vm.SketchDimRows.First(r => r.SketchKey == "sketch2" && r.Name == "width").Reference == "sketch2.width", "a dimension's full reference is shown");
vm.DocumentParameterRows.Single().Formula = "12 mm";
vm.PreviewDocumentParameters();
Check(vm.SketchDimRows.First(r => r.SketchKey == "sketch1" && r.Name == "width").Result == "48" && vm.SketchDimRows.First(r => r.SketchKey == "sketch2" && r.Name == "width").Result == "24", "the panel previews sketch values for the draft parameters");
vm.ApplyStudioParameters();
Check(!session.StatusIsError && Near(Dim(Fresh(session, a), "width").Value, 48) && Near(Volume(session), 48 * 20 * 5 + 24 * 20 * 5), "applying in the panel rebuilds sketches and features");
var row = vm.SketchDimRows.First(r => r.SketchKey == "sketch1" && r.Name == "height");
row.Formula = "wall + 8";
vm.ApplyStudioParameters();
Check(!session.StatusIsError && Near(Dim(Fresh(session, a), "height").Value, 20) && Near(Volume(session), 48 * 20 * 5 + 24 * 20 * 5), "a formula that gives the same size is accepted without moving geometry");
vm.SketchDimRows.First(r => r.SketchKey == "sketch1" && r.Name == "height").Formula = "wall + 18";
vm.ApplyStudioParameters();
Check(!session.StatusIsError && Near(Dim(Fresh(session, a), "height").Value, 30) && Near(Volume(session), 48 * 30 * 5 + 24 * 20 * 5), "editing a dimension formula in the panel applies it");
vm.SketchDimRows.First(r => r.SketchKey == "sketch2" && r.Name == "width").Formula = "nothing + 1";
vm.PreviewDocumentParameters();
Check(vm.StudioMessage.Contains("nothing") && vm.SketchDimRows.All(r => r.Result == "—"), "a bad formula is reported in the panel");

Console.WriteLine($"{checks} checks passed");

sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") });
    }
}
