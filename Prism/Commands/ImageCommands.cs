using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

static class ImageUtil
{
    static readonly Dictionary<string, (DateTime stamp, string b64, int w, int h)> Cache = [];

    /// <summary>Read a picture file: its bytes (kept in the document as base64) and its size in pixels.</summary>
    public static (string b64, int w, int h) Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("There is no picture at " + path);
        var stamp = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var c) && c.stamp == stamp) return (c.b64, c.w, c.h);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 30_000_000) throw new InvalidOperationException("That picture is larger than 30 MB. Shrink it first.");
        int w, h;
        try { using var bmp = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes)); w = bmp.PixelSize.Width; h = bmp.PixelSize.Height; }
        catch (Exception) { throw new InvalidOperationException("That file is not a picture the app can read. Use PNG, JPG, BMP or GIF."); }
        var b64 = Convert.ToBase64String(bytes);
        Cache[path] = (stamp, b64, w, h);
        return (b64, w, h);
    }

    public static Shape Frame(Plane3 pl, ImageInfo info)
    {
        double hw = info.WidthMm / 2, hh = info.HeightMm / 2;
        return Occ.Polyline([pl.PointAt(-hw, -hh), pl.PointAt(hw, -hh), pl.PointAt(hw, hh), pl.PointAt(-hw, hh)], true);
    }

    public static Vec3 Rotate(Vec3 v, Vec3 axis, double ang)
    {
        double c = Math.Cos(ang), s = Math.Sin(ang);
        return v * c + Vec3.Cross(axis, v) * s + axis * (Vec3.Dot(axis, v) * (1 - c));
    }

    /// <summary>Where the pointer ray meets a plane (null when it runs parallel).</summary>
    public static Vec3? RayOnPlane(CadSession s, double px, double py, Plane3 pl)
    {
        var (o, d) = s.Camera.Ray(px, py); var n = pl.Normal.Normalized();
        double den = Vec3.Dot(d, n); if (Math.Abs(den) < 1e-9) return null;
        double t = Vec3.Dot(pl.Origin - o, n) / den;
        return t < 0 ? null : o + d * t;
    }
}

/// <summary>Bring a picture into the model as a reference: on a standard plane, or on any plane or flat face.</summary>
public sealed class PlaceImageCommand : Command, IImageGizmoCommand
{
    public (ImageInfo info, Plane3 plane)? ImagePreview { get; private set; }
    readonly TextParam _file;
    readonly ChoiceParam _on;
    readonly SelectionSlot _plane;
    readonly NumberParam _width, _opacity, _offset, _rot, _du, _dv;
    readonly BoolParam _flip;
    public ChoiceParam GizmoMode { get; }
    public NumberParam GizmoWidth => _width;
    public NumberParam GizmoTurn => _rot;
    public NumberParam GizmoU => _du;
    public NumberParam GizmoV => _dv;
    public Plane3? GizmoPlane => ImagePreview?.plane;

    public PlaceImageCommand(string? path = null)
    {
        _file = Add(new TextParam { Label = "Image file", Hint = "Path to a PNG, JPG, BMP or GIF" });
        _on = Choice("Place it on", 0, "Front plane (XZ)", "Top plane (XY)", "Right plane (YZ)", "A plane or flat face I pick");
        _plane = Slot(new SelectionSlot { Label = "Plane or flat face", Filter = SelFilter.Plane | SelFilter.Face, IsVisible = false, Optional = true });
        _width = Num("Width", 200, 0.01, 1e6, 10);
        _opacity = Num("Opacity", 60, 0, 100, 10, "%");
        _offset = Num("Move it back or forward", 0, -1e6, 1e6, 5);
        _flip = Check("Mirror left to right", false);
        _du = Num("Move right", 0, -1e6, 1e6, 5);
        _dv = Num("Move up", 0, -1e6, 1e6, 5);
        _rot = Num("Turn", 0, -360, 360, 5, "°");
        GizmoMode = Choice("Gizmo (T move, R rotate, S scale)", 0, "Move", "Rotate", "Scale");
        Tool = new ImageGizmoTool(this);
        _on.Changed = () => { UpdateVisibility(); OnChanged(); };
        if (path != null) _file.Value = path;
    }

    public override string Title => "Import Image";
    public override string Icon => "image";
    public override string OkText => "Place image";
    public override bool SupportsPreview => true;
    public override bool CanEdit => true;
    public override string Hint => "A picture to trace or measure against. Choose the plane it lies on and a rough width, then use Set Scale to make it true size. It sits behind the model and is never exported.";

    protected override void UpdateVisibility() { _plane.IsVisible = _on.Index == 3; if (_plane.IsVisible) foreach (var s in Slots) s.IsActive = s == _plane; }

    protected override CommandOutput? Build()
    {
        ImagePreview = null;
        Results.Clear();
        var path = _file.Value.Trim().Trim('"');
        if (path.Length == 0) return null;
        var (b64, w, h) = ImageUtil.Load(path);
        Plane3 pl;
        if (_on.Index == 3)
        {
            if (_plane.Items.Count == 0) return null;
            pl = S.PlaneOf(_plane.Items[0]) ?? throw new InvalidOperationException("Pick a plane or a flat face.");
        }
        else pl = CadSession.OriginPlane(_on.Index == 0 ? 1 : _on.Index == 1 ? 0 : 2);
        var normal = pl.Normal.Normalized();
        var origin = pl.Origin + normal * _offset.Value + pl.U * _du.Value + pl.V * _dv.Value;
        double angle = _rot.Value * Math.PI / 180;
        pl = new Plane3(origin, ImageUtil.Rotate(pl.U, normal, angle), ImageUtil.Rotate(pl.V, normal, angle));
        var info = new ImageInfo(b64, w, h, _width.Value, _opacity.Value / 100, _flip.Value, false, Path.GetFileName(path));
        ImagePreview = (info, pl);
        Results.Add(new ResultLine("Picture", $"{w} × {h} pixels"));
        Results.Add(new ResultLine("Size on the plane", $"{info.WidthMm:0.#} × {info.HeightMm:0.#} mm"));
        var o = new CommandOutput { Label = "Import image", Icon = "image" };
        o.Add.Add(new(ImageUtil.Frame(pl, info), "Image", ObjKind.Plane, null, pl, null, null, null, info));
        return o;
    }

}

/// <summary>Change how a picture is shown: opacity, size, mirroring, position and turn.</summary>
public sealed class EditImageCommand : Command, IImageGizmoCommand
{
    readonly SelectionSlot _img;
    readonly NumberParam _opacity, _width, _rot, _du, _dv;
    readonly BoolParam _flipH, _flipV;
    Guid _seeded; ImageInfo? _base; Plane3 _basePlane; bool _seeding;
    public ChoiceParam GizmoMode { get; }
    public NumberParam GizmoWidth => _width;
    public NumberParam GizmoTurn => _rot;
    public NumberParam GizmoU => _du;
    public NumberParam GizmoV => _dv;
    public Plane3? GizmoPlane => _base != null && _seeded != Guid.Empty && S.ImageOverrides.TryGetValue(_seeded, out var p) ? p.plane : null;

    public EditImageCommand()
    {
        _img = Slot(new SelectionSlot { Label = "Picture", Filter = SelFilter.Plane });
        _opacity = Num("Opacity", 60, 0, 100, 10, "%");
        _width = Num("Width", 200, 0.01, 1e6, 10);
        _rot = Num("Turn", 0, -360, 360, 5, "°");
        _du = Num("Move right", 0, -1e6, 1e6, 5);
        _dv = Num("Move up", 0, -1e6, 1e6, 5);
        _flipH = Check("Mirror left to right", false);
        _flipV = Check("Mirror top to bottom", false);
        GizmoMode = Choice("Gizmo (T move, R rotate, S scale)", 0, "Move", "Rotate", "Scale");
        Tool = new ImageGizmoTool(this);
    }

    public override string Title => "Edit Image";
    public override string Icon => "image-edit";
    public override bool SupportsPreview => true;
    public override bool CanEdit => true;
    public override string Hint => "Pick a picture (its frame), then change its opacity, size, position or turn. Changing the width keeps the picture centred; use Set Scale to size it from a known distance.";

    void Seed(SceneObject obj)
    {
        _seeding = true;
        _base = (ImageInfo)obj.Asm!; _basePlane = obj.SketchPlane!.Value; _seeded = obj.Id;
        _opacity.Value = Math.Round(_base.Opacity * 100); _width.Value = _base.WidthMm; _rot.Value = 0; _du.Value = 0; _dv.Value = 0;
        _flipH.Value = _base.FlipH; _flipV.Value = _base.FlipV;
        _seeding = false;
    }

    protected override CommandOutput? Build()
    {
        if (_seeding) return null;
        var obj = ObjectsOf(_img).FirstOrDefault(o => o.Asm is ImageInfo && o.SketchPlane != null);
        if (obj == null) { S.ImageOverrides.Clear(); if (_img.Items.Count > 0) throw new InvalidOperationException("That is not a picture. Pick the frame of an imported image."); return null; }
        if (obj.Id != _seeded) Seed(obj);
        var info = _base! with { WidthMm = _width.Value, Opacity = _opacity.Value / 100, FlipH = _flipH.Value, FlipV = _flipV.Value };
        var n = _basePlane.Normal.Normalized(); double a = _rot.Value * Math.PI / 180;
        var u = ImageUtil.Rotate(_basePlane.U, n, a); var v = ImageUtil.Rotate(_basePlane.V, n, a);
        var pl = new Plane3(_basePlane.Origin + _basePlane.U * _du.Value + _basePlane.V * _dv.Value, u, v);
        S.ImageOverrides[obj.Id] = (info, pl);
        var frame = ImageUtil.Frame(pl, info);
        var o = new CommandOutput { Label = "Edit image", Icon = "image-edit" };
        o.Replace.Add((obj, frame));
        o.After = s => { s.Update(obj.Id, x => x.WithImage(info, frame, pl), checkpoint: false); s.ImageOverrides.Clear(); };
        return o;
    }

}

public interface IImageGizmoCommand
{
    Plane3? GizmoPlane { get; }
    ChoiceParam GizmoMode { get; }
    NumberParam GizmoWidth { get; }
    NumberParam GizmoTurn { get; }
    NumberParam GizmoU { get; }
    NumberParam GizmoV { get; }
}

/// <summary>Make a picture true size: click two points on it, say how far apart they really are, and the picture is scaled to match.</summary>
public sealed class CalibrateImageCommand : Command
{
    public readonly SelectionSlot Img;
    readonly NumberParam _real;
    readonly ChoiceParam _unit;
    public List<Vec3> Points { get; } = [];
    static int _lastUnit;
    static readonly (string name, double mm)[] Units = [("mm", 1), ("cm", 10), ("m", 1000), ("in", 25.4), ("ft", 304.8)];

    public CalibrateImageCommand()
    {
        Img = Slot(new SelectionSlot { Label = "Picture", Filter = SelFilter.Plane });
        _real = Num("Real distance between the two points", 18, 1e-6, 1e7, 1, "");
        _unit = Choice("In", _lastUnit, Units.Select(u => u.name).ToArray());
        Add(new ActionParam { Label = "Pick the two points again", Run = () => { Points.Clear(); S.Say(Hint); OnChanged(); } });
        Tool = new CalibrateTool(this);
    }

    public override string Title => "Set Image Scale";
    public override string Icon => "image-scale";
    public override string OkText => "Apply scale";
    public override string Hint => "Click the picture, then two points whose real distance you know (hold Shift to keep the line level or upright), such as the bottom and top of a tyre. Type the real distance and its unit, then apply.";

    public (SceneObject obj, ImageInfo info, Plane3 plane)? Target
    {
        get
        {
            var obj = ObjectsOf(Img).FirstOrDefault(o => o.Asm is ImageInfo && o.SketchPlane != null);
            return obj == null ? null : (obj, (ImageInfo)obj.Asm!, obj.SketchPlane!.Value);
        }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        _lastUnit = _unit.Index;
        if (Target is not { } t) { S.ImageOverrides.Clear(); Results.Add(new ResultLine("Step 1", "Pick the picture")); return null; }
        if (Points.Count < 2) { Results.Add(new ResultLine("Step 2", $"Click the {(Points.Count == 0 ? "first" : "second")} point on the picture")); return null; }
        double measured = Points[0].DistanceTo(Points[1]);
        if (measured < 1e-6) { Points.RemoveAt(1); return null; }
        double real = _real.Value * Units[_unit.Index].mm, k = real / measured;
        Results.Add(new ResultLine("Measured on the picture now", $"{measured:0.##} mm"));
        Results.Add(new ResultLine("Real distance", $"{real:0.##} mm"));
        Results.Add(new ResultLine("The picture changes by", $"× {k:0.####}", Math.Abs(k - 1) > 0.5 ? 1 : 0));
        var info = t.info with { WidthMm = t.info.WidthMm * k };
        var pl = new Plane3(Points[0] + (t.plane.Origin - Points[0]) * k, t.plane.U, t.plane.V);       // it grows or shrinks about the first point
        S.ImageOverrides[t.obj.Id] = (info, pl);
        var frame = ImageUtil.Frame(pl, info);
        var o = new CommandOutput { Label = "Set image scale", Icon = "image-scale" };
        o.Replace.Add((t.obj, frame));
        o.After = s => { s.Update(t.obj.Id, x => x.WithImage(info, frame, pl), checkpoint: false); s.ImageOverrides.Clear(); };
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var p in Points) o.AddDot(p, 0xFFFF8A1F, 11);
        if (Points.Count == 2) { o.AddLine(Points[0], Points[1], 0xFF35B6FF, 2.6f); o.AddLabel((Points[0] + Points[1]) * 0.5, $"{Points[0].DistanceTo(Points[1]):0.##} mm now", true, 1); }
    }
}

public sealed class CalibrateTool(CalibrateImageCommand cmd) : IViewportTool
{
    Vec3? _hover;
    public string Prompt => cmd.Target == null ? "Click the picture (its frame) first." : cmd.Points.Count < 2 ? $"Click point {cmd.Points.Count + 1} of 2 on the picture. Shift keeps the line level or upright." : "Type the real distance in the panel, then apply. Click again to pick new points.";

    Vec3? OnImage(CadSession s, PointerInfo p)
    {
        if (cmd.Target is not { } t) return null;
        var hit = ImageUtil.RayOnPlane(s, p.X, p.Y, t.plane);
        if (hit is not { } h) return null;
        if (p.Shift && cmd.Points.Count == 1)
        {
            var a = cmd.Points[0]; var d = h - a;
            double du = Vec3.Dot(d, t.plane.U.Normalized()), dv = Vec3.Dot(d, t.plane.V.Normalized());
            h = Math.Abs(du) >= Math.Abs(dv) ? a + t.plane.U.Normalized() * du : a + t.plane.V.Normalized() * dv;
        }
        return h;
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (cmd.Target == null)
        {
            // Origin and construction planes can overlap a picture. Pick pictures directly
            // here so those unrelated planes cannot consume the required picture slot.
            var image = s.Objects.Where(o => o.Visible && o.Asm is ImageInfo && o.SketchPlane != null)
                .Select(o => (obj: o, hit: ImageUtil.RayOnPlane(s, p.X, p.Y, o.SketchPlane!.Value)))
                .Where(x => x.hit is { } h && Inside(x.obj, h))
                .OrderBy(x => x.hit!.Value.DistanceTo(s.Camera.Eye)).FirstOrDefault().obj;
            if (image != null) cmd.Pick(SelItem.Of(image), false);
            else if (s.Pick(p.X, p.Y, SelFilter.Plane) == null && s.Objects.Count(o => o.Asm is ImageInfo) == 1)
                cmd.Pick(SelItem.Of(s.Objects.First(o => o.Asm is ImageInfo)), false);
            return true;
        }
        if (OnImage(s, p) is not { } pt) { s.Say("The view runs along the picture. Turn the view so you can see it face on.", true); return true; }
        if (cmd.Points.Count >= 2) cmd.Points.Clear();
        cmd.Points.Add(pt); cmd.OnChanged(); s.Say(Prompt); s.Invalidate();
        return true;
    }

    static bool Inside(SceneObject obj, Vec3 hit)
    {
        var info = (ImageInfo)obj.Asm!;
        var (u, v) = obj.SketchPlane!.Value.Project(hit);
        return Math.Abs(u) <= info.WidthMm / 2 && Math.Abs(v) <= info.HeightMm / 2;
    }

    public void PointerMove(CadSession s, PointerInfo p) { _hover = OnImage(s, p); s.Invalidate(); }
    public void PointerUp(CadSession s, PointerInfo p) { }
    public bool Key(CadSession s, string key) => false;
    public void Deactivate(CadSession s) { s.ImageOverrides.Clear(); }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        if (cmd.Points.Count == 1 && _hover is { } h) { o.AddLine(cmd.Points[0], h, 0x9035B6FF, 1.6f); o.AddLabel((cmd.Points[0] + h) * 0.5, $"{cmd.Points[0].DistanceTo(h):0.##} mm", true, 1); }
    }
}
