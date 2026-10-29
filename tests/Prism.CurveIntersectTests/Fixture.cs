using Prism.Core;
using Prism.Kernel;

public static class CurveIntersectFixture
{
    public static (SceneObject first, SceneObject second) Add(CadSession s)
    {
        var a = s.AddObject(Occ.NurbsCurve([new(-60,-20,-35),new(-20,45,-35),new(20,-45,-35),new(60,20,-35)]), "Front profile (XY)", ObjKind.Curve, 0xFFF6B85B, new(new(0,0,-35),Vec3.UnitX,Vec3.UnitY));
        var b = s.AddObject(Occ.NurbsCurve([new(-60,-50,0),new(-20,-50,65),new(20,-50,-25),new(60,-50,40)]), "Side profile (XZ)", ObjKind.Curve, 0xFFB697FF, new(new(0,-50,0),Vec3.UnitX,Vec3.UnitZ));
        return (a,b);
    }
}
