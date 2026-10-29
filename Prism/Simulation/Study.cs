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
}

/// <summary>A simulation: one body, its fixtures and loads, mesh size and the latest results.</summary>
public sealed class StudyDef
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Static 1";
    public StudyType Type { get; set; }
    public Guid BodyId { get; set; }
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
