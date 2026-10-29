using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
namespace Prism.Core.Cam;

public sealed record GcodePreview(ToolpathInfo Path,string[] Notes);
/// <summary>Read-only Cartesian G0/G1 preview. Unsupported motion is rejected, never silently drawn as a straight line.</summary>
public static class LinearGcode
{
    static readonly Regex Words=new(@"([A-Z])\s*([-+]?(?:\d*\.\d+|\d+\.?\d*))",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    public static GcodePreview Parse(string text)
    {
        if(text.Length>32_000_000)throw new InvalidOperationException("G-code preview is limited to 32 MB.");
        double[] xyz=[0,0,0],offset=[0,0,0];double e=0,unit=1,feed=1200,layerZ=double.NaN;bool absolute=true,absE=true;
        var moves=new List<double>{0,0,0,0};var feeds=new List<double>{1200};var layers=new List<int>{0};var notes=new HashSet<string>{"Cartesian G0/G1 path preview; initial position is assumed to be [0,0,0]. Homing, acceleration, dwell time, offsets in the machine, and extrusion physics are not simulated."};
        int lineNo=0;
        foreach(string raw in text.Split('\n'))
        {
            lineNo++;string line=Regex.Replace(raw.Split(';')[0],@"\([^)]*\)","").ToUpperInvariant();var words=Words.Matches(line).Cast<Match>().ToArray();if(words.Length==0)continue;
            double Value(Match w)=>double.Parse(w.Groups[2].Value,CultureInfo.InvariantCulture);
            var gs=words.Where(w=>w.Groups[1].Value=="G").Select(Value).ToArray();
            foreach(double g in gs)
            {
                if(g is 0 or 1)continue;
            else if(g==90){absolute=true;absE=true;}else if(g==91){absolute=false;absE=false;}else if(g==20)unit=25.4;else if(g==21)unit=1;
                else if(g==28){if(moves.Count>4)throw new InvalidOperationException($"Line {lineNo}: homing after motion cannot be previewed reliably.");notes.Add("Initial G28 homing is omitted.");}
                else if(g==4)notes.Add("G4 dwell is omitted from playback timing.");
                else if(g is not (17 or 92))throw new InvalidOperationException($"Line {lineNo}: unsupported G{g}. Only linear Cartesian G0/G1 motion is accepted; arcs, probing and coordinate transforms need a suitable interpreter.");
            }
            if(gs.Any(g=>g is 0 or 1)&&words.Any(w=>w.Groups[1].Value=="M"))throw new InvalidOperationException($"Line {lineNo}: mixed motion and machine commands must be on separate lines.");
            foreach(var m in words.Where(w=>w.Groups[1].Value=="M")){double code=Value(m);if(code==82)absE=true;else if(code==83)absE=false;else if(code is not (104 or 109 or 140 or 190 or 106 or 107 or 84 or 18 or 400 or 117 or 73))throw new InvalidOperationException($"Line {lineNo}: unsupported M{code}; machine actions are not executed.");}
            if(!words.Any(w=>w.Groups[1].Value=="M"))
            {
                if(words.Any(w=>!"GNXYZEFSPT".Contains(w.Groups[1].Value)))throw new InvalidOperationException($"Line {lineNo}: unsupported axis or word.");
                string residue=Words.Replace(line,"").Trim();
                if(residue.Length>0 && !(gs.Contains(28)&&residue.All(c=>char.IsWhiteSpace(c)||"XYZ".Contains(c))))throw new InvalidOperationException($"Line {lineNo}: malformed or unsupported G-code text.");
                if(words.Any(w=>w.Groups[1].Value=="T"&&Value(w)!=0))throw new InvalidOperationException($"Line {lineNo}: multiple-nozzle tool changes are not supported.");
            }
            var dict=words.Where(w=>w.Groups[1].Value is "X" or "Y" or "Z" or "E" or "F").GroupBy(w=>w.Groups[1].Value).ToDictionary(g=>g.Key,g=>Value(g.Last()));
            if(dict.Values.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("Non-finite G-code coordinate.");
            if(gs.Contains(92)){for(int a=0;a<3;a++)if(dict.TryGetValue("XYZ"[a].ToString(),out double v))offset[a]=xyz[a]-v*unit;if(dict.TryGetValue("E",out double ev))e=ev*unit;continue;}
            if(gs.Any(g=>g is 28 or 4)||words.Any(w=>w.Groups[1].Value=="M"))continue;
            if(dict.TryGetValue("F",out double f)){feed=f*unit;if(feed<=0)throw new InvalidOperationException($"Line {lineNo}: feed must be positive.");}
            double extrusion=0;if(dict.TryGetValue("E",out double ep)){extrusion=absE?ep*unit-e:ep*unit;e=absE?ep*unit:e+ep*unit;}
            bool spatial=dict.Keys.Any(k=>k is "X" or "Y" or "Z");if(!spatial)continue;
            for(int a=0;a<3;a++)if(dict.TryGetValue("XYZ"[a].ToString(),out double v))xyz[a]=absolute?v*unit+offset[a]:xyz[a]+v*unit;
            if(xyz.Any(v=>Math.Abs(v)>1_000_000))throw new InvalidOperationException($"Line {lineNo}: coordinate exceeds preview bounds.");
            if(extrusion>1e-8&&(double.IsNaN(layerZ)||Math.Abs(xyz[2]-layerZ)>1e-5)){if(!double.IsNaN(layerZ))layers.Add(moves.Count/4);layerZ=xyz[2];}
            moves.AddRange([xyz[0],xyz[1],xyz[2],(int)(extrusion<=1e-8?MoveKind.Rapid:MoveKind.OuterWall)]);feeds.Add(feed);
            if(moves.Count/4>500000)throw new InvalidOperationException("Preview is limited to 500,000 moves.");
        }
        if(moves.Count<8)throw new InvalidOperationException("No linear moves found.");
        return new(new("Imported linear G-code","Nozzle",.4,1200,300,0,xyz[2],moves.ToArray(),layers.ToArray(),true,false,Feeds:feeds.ToArray()),notes.ToArray());
    }
}
