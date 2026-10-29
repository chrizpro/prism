using System.Runtime.InteropServices;
using System.Numerics;
using System.Reflection;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Rendering;

NativeSearch.Initialize();
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message); checks++;
}
var session = new CadSession();
var ball = session.AddObject(Occ.Sphere(10), "Material test", ObjKind.Solid);
session.Update(ball.Id, o => o.WithMaterial(Materials.Find("hdpe")!).WithColor(0xFFDA7B2F).WithLook(new("hdpe", 0, .45f)));
session.Camera.Target = Vec3.Zero; session.Camera.Distance = 55;
var folder = Path.Combine(Path.GetTempPath(), "prism-render-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
session.Save(Path.Combine(folder, "material.prism"));
var reopened = new CadSession(); reopened.Open(session.FilePath!);
var restored = reopened.Objects.Single();
Check(restored.Look.MaterialId == "hdpe" && restored.Material?.Id == "hdpe", "material identity survives save and reopen");
Check(restored.Color == 0xFFDA7B2F && restored.Look.Gloss == .45f && restored.Look.Metal == 0,
    "custom colour and finish survive save and reopen");

byte[] Render(RenderBg bg)
{
    var tracer = new PathTracer(new RenderSettings { Width = 48, Height = 48, Samples = 32, Bounces = 3, Background = bg, Ground = false });
    Check(tracer.LoadScene(session), "material scene loads for " + bg);
    byte[] result = []; int samples = 0; bool finished = false;
    tracer.Render(session.Camera, (p, w, h, n, done) => { result = (byte[])p.Clone(); samples = n; finished = done; }, default);
    Check(finished && samples == 32 && result.Length == 48 * 48 * 4, "render completes requested samples for " + bg);
    return result;
}
var transparent = Render(RenderBg.Transparent); var white = Render(RenderBg.White);
Check(transparent[3] == 0, "transparent background has zero alpha");
Check(white.Take(4).All(v => v == 255), "white background exports exact opaque white");
Check(Enumerable.Range(0, 48 * 48).Any(i => transparent[4 * i + 3] is > 0 and < 255), "fixture exercises antialiased silhouette coverage");
bool compositingMatches = true;
for (int i = 0; i < 48 * 48; i++)
{
    int alpha = transparent[4 * i + 3];
    for (int c = 0; c < 3; c++)
        compositingMatches &= Math.Abs(white[4 * i + c] - (transparent[4 * i + c] + 255 - alpha)) <= 2;
}
Check(compositingMatches, "white and transparent renders composite consistently at silhouettes");
Check(Enumerable.Range(0, 48 * 48).Any(i => transparent[4 * i + 3] == 255 && transparent[4 * i + 2] > transparent[4 * i] * 1.4),
    "render uses the assigned orange material colour");
var settings = new RenderSettings { Width = 48, Height = 48, Samples = 4, Bounces = 2, Ground = false };
var job = session.StartRender(settings);
var until = DateTime.UtcNow.AddSeconds(15);
while (job.Running && DateTime.UtcNow < until) await Task.Delay(10);
Check(job.Finished && job.Error == null && job.Samples == 4, "session reports successful completion and sample count");
var path = Path.Combine(folder, "nested", "render.png");
Check(session.SaveRender(path) == path && File.ReadAllBytes(path).Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
    "save creates the output directory and writes a PNG");
session.StartRender(new RenderSettings { Width = 48, Height = 48, Samples = 4096 });
var stopped = session.Render!; session.StopRender(); await Task.Delay(100);
Check(!stopped.Running && !stopped.Finished && stopped.Cts.IsCancellationRequested, "stop cancels without reporting completion");
session.CloseRender(); Check(session.Render == null, "close clears the render");
Check(session.SaveRender(Path.Combine(folder, "none.png")) == null, "saving without a render reports failure");
Check(!new RenderCommand().SupportsPreview, "render does not expose a modeling preview toggle");
var evaluate=typeof(PathTracer).GetMethod("Bsdf",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<EvaluateBsdf>();
foreach(float metal in new[]{0f,1f})foreach(float nv in new[]{1f,.3f})
{
    var view=new Vector3(MathF.Sqrt(1-nv*nv),0,nv);var integral=Vector3.Zero;bool finite=true;
    const int rows=160,columns=320;
    for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
    {
        float z=(row+.5f)/rows,phi=2*MathF.PI*(col+.5f)/columns,r=MathF.Sqrt(1-z*z);
        var f=evaluate(Vector3.UnitZ,view,new(r*MathF.Cos(phi),r*MathF.Sin(phi),z),Vector3.One,metal,.5f,out float pdf);
        finite&=float.IsFinite(f.X)&&f.X>=0&&float.IsFinite(pdf)&&pdf>=0;
        integral+=f*(z*2*MathF.PI/(rows*columns));
    }
    Check(finite&&integral.X>0&&integral.X<=1.001f,$"white-furnace energy bounded for metallic={metal}, N·V={nv} (reflectance {integral.X:F4})");
}
var testLight=Vector3.Normalize(new Vector3(.3f,.1f,1));
var before=evaluate(Vector3.UnitZ,Vector3.UnitZ,testLight,new(.5f),.499f,.5f,out _);
var after=evaluate(Vector3.UnitZ,Vector3.UnitZ,testLight,new(.5f),.501f,.5f,out _);
Check((after-before).Length()<.01,"metallicity remains continuous across the former 0.5 switch");
var grazing=evaluate(Vector3.UnitZ,Vector3.Normalize(new Vector3(1,0,.00001f)),testLight,new(.7f),1,.06f,out float grazingPdf);
Check(float.IsFinite(grazing.X)&&grazing.X>=0&&float.IsFinite(grazingPdf),"polished-metal grazing evaluation remains finite");
Console.WriteLine($"SUCCESS: {checks} checks; artifacts: {folder}");

delegate Vector3 EvaluateBsdf(Vector3 normal,Vector3 view,Vector3 light,Vector3 albedo,float metallic,float roughness,out float pdf);

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
