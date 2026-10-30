using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>An editor-local manipulator frame. Placing it never changes geometry.</summary>
public sealed class GizmoBinding
{
    public Plane3? Frame {get;private set;}
    public bool Positioning {get;private set;}
    public bool Bound {get;private set;}
    Plane3? _before;bool _wasBound;
    HashSet<int> _targets=[];
    public Vec3[] Axes=>Frame is {} f?[f.U,f.V,f.Normal]:GizmoVisual.WorldAxes;
    public Vec3 Center(Vec3 fallback)=>Frame?.Origin??fallback;
    public void Begin(Vec3 center)
    {
        if(Positioning)return;
        _before=Frame;_wasBound=Bound;Frame??=new Plane3(center,Vec3.UnitX,Vec3.UnitY);Positioning=true;
    }
    public bool Bind(IEnumerable<int> targets)
    {
        var selected=targets.ToHashSet();if(selected.Count==0)return false;
        _targets=selected;Bound=true;Positioning=false;return true;
    }
    public void Cancel(){Frame=_before;Bound=_wasBound;Positioning=false;}
    public void Reset(){Frame=null;Bound=false;Positioning=false;_targets.Clear();}
    public void CheckTargets(IEnumerable<int> targets){if(Bound&&!Positioning&&!_targets.SetEquals(targets))Reset();}
    public void SetOrigin(Vec3 origin){if(Positioning&&Finite(origin))Frame=new(origin,Frame!.Value.U,Frame.Value.V);}
    static bool Finite(Vec3 p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
    public void Transform(Plane3 start,Func<Vec3,Vec3> transform)
    {
        var origin=transform(start.Origin);var u=(transform(start.Origin+start.U)-origin).Normalized();
        var v=transform(start.Origin+start.V)-origin;v=(v-u*Vec3.Dot(v,u)).Normalized();
        if(Finite(origin)&&u.Length>.9&&v.Length>.9)Frame=new(origin,u,v);
    }
    public static Func<Vec3,Vec3> Scale(Vec3 center,Vec3[] axes,double x,double y,double z)=>p=>
    {
        var d=p-center;return center+axes[0]*(Vec3.Dot(d,axes[0])*x)+axes[1]*(Vec3.Dot(d,axes[1])*y)+axes[2]*(Vec3.Dot(d,axes[2])*z);
    };
}
