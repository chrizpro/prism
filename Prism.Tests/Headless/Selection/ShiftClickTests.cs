using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Selection;

/// <summary>
/// Shift-click adds faces, edges and points to the selection and a click on empty space clears it, even when the hand moves the mouse a few
/// pixels during the click (owner report 2026-11-20: a few pixels of movement turned each click into a tiny, empty drag box).
/// </summary>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public sealed class ShiftClickTests
{
    [AvaloniaFact(DisplayName = "SEL-H069 | Shift-click adds faces, edges and points on a box, with a little hand movement, under every select method; a click on empty space clears")]
    [Trait("TestId", "SEL-H069")]
    public void ShiftClickAddsWithWobble()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        s.AddObject(Occ.Box(30, 30, 30), "Box", ObjKind.Solid);
        s.ZoomExtents();
        void Click(double x, double y, bool shift)
        {
            s.PointerMove(new PointerInfo(x, y, false, shift, false, false));
            s.PointerDown(new PointerInfo(x, y, true, shift, false, false));
            s.PointerMove(new PointerInfo(x + 3, y + 2, true, shift, false, false));
            s.PointerMove(new PointerInfo(x + 5, y + 3, true, shift, false, false));
            s.PointerUp(new PointerInfo(x + 5, y + 3, false, shift, false, false));
        }
        void ClickAt(Vec3 at, bool shift) { var p = s.Camera.Project(at)!.Value; Click(p.x, p.y, shift); }
        var cases = new (SelMode mode, SelKind kind, Vec3 a, Vec3 b)[]
        {
            (SelMode.Face, SelKind.Face, new(0, 0, 30), new(15, 0, 15)),
            (SelMode.Edge, SelKind.Edge, new(0, 15, 30), new(15, 0, 30)),
            (SelMode.Vertex, SelKind.Vertex, new(15, 15, 30), new(-15, 15, 30)),
        };
        foreach (var method in Enum.GetValues<SelectMethod>())
            foreach (var (mode, kind, a, b) in cases)
            {
                s.Method = method;
                vm.SetSelectionMode(mode);
                ClickAt(a, false);
                ClickAt(b, true);
                Assert.True(s.Selection.Count(i => i.Kind == kind) == 2, $"{method}/{mode}: {s.Selection.Count} selected after a Shift-click");
                Click(3, 3, false);
                Assert.Empty(s.Selection);
            }
    }
}
