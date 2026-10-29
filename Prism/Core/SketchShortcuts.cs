namespace Prism.Core;

/// <summary>Canonical sketch shortcut catalogue. Keep gesture changes and docs/sketch-hotkeys.md together.</summary>
public static class SketchShortcuts
{
    public const string GizmoCommand="SketchGizmo", EditCommand="SketchEdit";
    public const string AxisLockGesture="Shift";
    public static bool ShiftHeld(PointerInfo pointer)=>pointer.Shift;
    public static readonly (string Gesture,string Action)[] All =
    [ ("L","Line"),("R","Rectangle; rotate its live preview after the first point"),("C","Ellipse with circle proportions"),("S","Spline"),
      ("A","Arc"),("D","Dimension"),("Shift","Hold axis direction for lines, splines, arc directions, slot centers and polygons; square rectangles; circle ellipses"),
      ("G","Toggle gizmo setup in 3D or for an active 2D datum gizmo"),("W / R / S with a gizmo target","Move / rotate / scale from the center handle"),("E","Toggle geometry editing in 3D"),
      ("Delete","Delete selection"),("Ctrl+Z","Undo"),("Alt","Bypass snapping"),
      ("Tab","Next numeric field"),("Enter","Accept numeric entry / finish current curve"),("Escape","End current drawing, retaining placed geometry") ];
}
