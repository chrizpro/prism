using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Prism.Kernel;
using Prism.Core;

namespace Prism.Simulation;

public enum StudyType { Static }

public readonly record struct FaceRef(Guid Obj, int Face);

public enum FixtureKind
{
    /// <summary>All three directions locked.</summary>
    Fixed,
    /// <summary>Roller / symmetry: the face may slide but cannot move along its normal (axis-aligned planar faces).</summary>
    Slider,
    /// <summary>Lock chosen global directions only.</summary>
    Directional,
}

public sealed class Fixture
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Fixture";
    public FixtureKind Kind { get; set; }
    public List<FaceRef> Faces { get; set; } = [];
    public bool LockX { get; set; } = true;
    public bool LockY { get; set; } = true;
    public bool LockZ { get; set; } = true;
}

public enum LoadKind { Force, Pressure, Torque, Gravity }

/// <summary>The part of a face a load covers: all of it, a circle, or a rectangle (owner request 2026-11-16: put a force on a certain area of a face).</summary>
public enum LoadRegionShape { WholeFace, Circle, Rectangle }

public sealed class Load
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Load";
    public LoadKind Kind { get; set; }
    public List<FaceRef> Faces { get; set; } = [];
    /// <summary>Force direction (unit) in global axes. When AlongNormal is set the face normal is used instead.</summary>
    public Vec3 Direction { get; set; } = -Vec3.UnitZ;
    public bool AlongNormal { get; set; }
    /// <summary>N for forces, MPa for pressure, N·mm for torque, g multiples for gravity.</summary>
    public double Value { get; set; } = 100;
    /// <summary>Total force is shared over the faces (true) or applied to each face (false).</summary>
    public bool Total { get; set; } = true;

    // ---- a load on part of one flat face. Stored in global axes so the solver needs no face frame; files saved before this had no region and stay whole-face.
    public LoadRegionShape Region { get; set; } = LoadRegionShape.WholeFace;
    /// <summary>The middle of the region, on the face (mm).</summary>
    public Vec3 RegionCentre { get; set; }
    /// <summary>Two unit directions in the face that the region is measured along; U is the rectangle's width direction.</summary>
    public Vec3 RegionU { get; set; } = Vec3.UnitX;
    public Vec3 RegionV { get; set; } = Vec3.UnitY;
    /// <summary>Circle: the radius. Rectangle: the width along U (mm).</summary>
    public double RegionA { get; set; }
    /// <summary>Rectangle: the height along V (mm).</summary>
    public double RegionB { get; set; }

    /// <summary>The area the region asks for (mm²), before it is cut off by the face's own outline; 0 for the whole face.</summary>
    [JsonIgnore] public double RegionArea => Region switch { LoadRegionShape.Circle => Math.PI * RegionA * RegionA, LoadRegionShape.Rectangle => RegionA * RegionB, _ => 0 };

    /// <summary>True when the point, projected into the face, lies inside the region (always true for a whole-face load).</summary>
    public bool InRegion(Vec3 point)
    {
        if (Region == LoadRegionShape.WholeFace) return true;
        var rel = point - RegionCentre; double x = Vec3.Dot(rel, RegionU), y = Vec3.Dot(rel, RegionV);
        return Region == LoadRegionShape.Circle ? x * x + y * y <= RegionA * RegionA : Math.Abs(x) <= RegionA / 2 && Math.Abs(y) <= RegionB / 2;
    }

    /// <summary>The region's outline as a closed polyline of points on the face, for drawing.</summary>
    public List<Vec3> RegionOutline()
    {
        var pts = new List<Vec3>();
        if (Region == LoadRegionShape.Circle)
            for (int i = 0; i < 48; i++) { double a = 2 * Math.PI * i / 48; pts.Add(RegionCentre + RegionU * (RegionA * Math.Cos(a)) + RegionV * (RegionA * Math.Sin(a))); }
        else if (Region == LoadRegionShape.Rectangle)
        {
            double hx = RegionA / 2, hy = RegionB / 2;
            pts.AddRange([RegionCentre - RegionU * hx - RegionV * hy, RegionCentre + RegionU * hx - RegionV * hy, RegionCentre + RegionU * hx + RegionV * hy, RegionCentre - RegionU * hx + RegionV * hy]);
        }
        return pts;
    }
}

/// <summary>A simulation: its body (and any others bonded to it, each with its own material), fixtures and loads, mesh size and the latest results.</summary>
public sealed class StudyDef
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Static 1";
    public StudyType Type { get; set; }
    public Guid BodyId { get; set; }
    /// <summary>More bodies in the study, bonded where they touch. Empty for a one-body study.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)] public List<Guid> OtherBodies { get; } = [];
    public bool Has(Guid body) => body == BodyId || OtherBodies.Contains(body);
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)] public List<Fixture> Fixtures { get; } = [];
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)] public List<Load> Loads { get; } = [];
    public string? ShapeSignature { get; set; }
    /// <summary>Target element edge length in mm (0 = automatic).</summary>
    public double ElementSize { get; set; }
    [JsonIgnore] public StudyResults? Results { get; set; }
}

/// <summary>Everything the viewer needs after a run.</summary>
public sealed class StudyResults
{
    public SceneObject? SourceBody { get; init; }
    /// <summary>The other bodies of a multi-body study, as they were when it was solved.</summary>
    public SceneObject[] OtherBodies { get; init; } = [];
    public required FemMesh Mesh { get; init; }
    public required FemMesh.StaticResult Solution { get; init; }
    /// <summary>Outward normal of each boundary triangle (from the CAD surface).</summary>
    public Vec3[] BoundaryNormal { get; init; } = [];
    public double[] SafetyFactor { get; init; } = [];
    public double Yield { get; init; }
    public double MaxVonMises { get; init; }
    public double MaxDisplacement { get; init; }
    public int MaxVonMisesNode { get; init; }
    public int MaxDisplacementNode { get; init; }
    public double MinSafety { get; init; }
    public double SolveSeconds { get; init; }
    public string MaterialName { get; init; } = "";
    public List<string> Notes { get; init; } = [];
}
