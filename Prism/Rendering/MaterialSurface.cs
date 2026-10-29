using System;
using System.Numerics;
using Prism.Core;

namespace Prism.Rendering;

/// <summary>Small procedural finishes shared in form with the viewport shader. No texture images or per-vertex data.</summary>
public static class MaterialSurface
{
    public static SurfacePattern Pattern(Appearance appearance)
    {
        if(appearance.Pattern!=SurfacePattern.Automatic)return appearance.Pattern;
        string? id=appearance.MaterialId;
        if(id is "oak" or "fir")return SurfacePattern.Wood;
        if(id=="cfrp")return SurfacePattern.CarbonWeave;
        if(id is "cast-iron" or "cast-al")return SurfacePattern.Cast;
        return appearance.Metal>.5f&&appearance.Gloss<=.75f?SurfacePattern.Brushed:SurfacePattern.Plain;
    }
    public static float Variation(SurfacePattern pattern,Vector3 position,Vector3 origin,float scale,float angle)
    {
        var p=(position-origin)/Math.Max(scale,.1f); float radians=angle*MathF.PI/180,c=MathF.Cos(radians),s=MathF.Sin(radians);
        p=new(c*p.X+s*p.Y,-s*p.X+c*p.Y,p.Z);
        if(pattern==SurfacePattern.Wood)
        {
            float warp=.18f*MathF.Sin(p.X*.8f)+.08f*MathF.Sin(p.X*3.1f)+.025f*MathF.Sin(p.X*11);
            float radius=MathF.Sqrt((p.Y+warp)*(p.Y+warp)+(p.Z*.85f+.18f)*(p.Z*.85f+.18f));
            float irregular=MathF.Sin(p.X*.7f+p.Y*1.3f)*MathF.Sin(p.X*2.1f+p.Z*.8f);
            float ring=.5f+.5f*MathF.Sin(radius*24+1.2f*irregular);
            float fiber=.5f+.5f*MathF.Sin(p.Y*180+MathF.Sin(p.X*1.3f)*5+p.Z*7+irregular*4);
            // Narrow latewood bands and long pores, rather than alternating broad stripes.
            return .98f-.25f*MathF.Pow(ring,12)-.10f*MathF.Pow(fiber,14)+.04f*irregular;
        }
        if(pattern==SurfacePattern.Brushed)return .99f+.01f*MathF.Sin(p.Y*160+.6f*MathF.Sin(p.X*2));
        if(pattern==SurfacePattern.Cast)return .91f+.09f*MathF.Sin(p.X*37+MathF.Sin(p.Y*41))*MathF.Sin(p.Z*39+p.Y*43);
        if(pattern==SurfacePattern.CarbonWeave)
        {
            float a=MathF.Sin(p.X*22),b=MathF.Sin(p.Y*22);
            return a*b>=0?.65f+.35f*MathF.Abs(a):.65f+.35f*MathF.Abs(b);
        }
        return 1;
    }
    public static Vector3 LinearColor(uint color,float variation=1)
    {
        float r=Math.Clamp(((color>>16)&255)/255f*variation,0,1),g=Math.Clamp(((color>>8)&255)/255f*variation,0,1),b=Math.Clamp((color&255)/255f*variation,0,1);
        return new(MathF.Pow(r,2.2f),MathF.Pow(g,2.2f),MathF.Pow(b,2.2f));
    }
}
