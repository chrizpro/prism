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

// ---------------------------------------------------------------- backdrops
byte[] RenderWith(Action<RenderSettings> configure, int samples = 8)
{
    var custom = new RenderSettings { Width = 40, Height = 40, Samples = samples, Bounces = 2, Ground = false };
    configure(custom);
    var tracer = new PathTracer(custom);
    Check(tracer.LoadScene(session), "scene loads for custom render");
    byte[] result = [];
    tracer.Render(session.Camera, (p, w, h, n, done) => { result = (byte[])p.Clone(); }, default);
    return result;
}
var green = RenderWith(s => s.Background = RenderBg.Green);
Check(green[0] == 64 && green[1] == 177 && green[2] == 0 && green[3] == 255, "green screen is opaque chroma-key green (0,177,64)");
var black = RenderWith(s => s.Background = RenderBg.Black);
Check(black[0] == 0 && black[1] == 0 && black[2] == 0 && black[3] == 255, "black background is opaque black");
var gradient = RenderWith(s => s.Background = RenderBg.Gradient);
int PixelSum(byte[] b, int x, int y) { int i = (y * 40 + x) * 4; return b[i] + b[i + 1] + b[i + 2]; }
Check(gradient[3] == 255 && PixelSum(gradient, 0, 0) < PixelSum(gradient, 20, 2) && PixelSum(gradient, 20, 2) > 400, "studio gradient is opaque and fades towards the corners");
Check(PixelSum(green, 20, 20) != 64 + 177 && PixelSum(green, 20, 20) > 0, "model is drawn in front of the green screen");
Check(RenderCommand.Backgrounds.Length == Enum.GetValues<RenderBg>().Length && RenderCommand.Backgrounds[3].Contains("Green") && RenderCommand.Backgrounds[4] == "Black", "render panel offers every background");
Check(RenderCommand.Lightings.Length == Enum.GetValues<RenderEnv>().Length, "render panel offers every lighting environment");

// ---------------------------------------------------------------- photometry
var tungsten = Photometry.KelvinToRgb(2700); var daylight = Photometry.KelvinToRgb(6500); var blueSky = Photometry.KelvinToRgb(12000);
float Lum(Vector3 c) => .2126f * c.X + .7152f * c.Y + .0722f * c.Z;
Check(Math.Abs(Lum(tungsten) - 1) < 1e-3 && Math.Abs(Lum(daylight) - 1) < 1e-3 && Math.Abs(Lum(blueSky) - 1) < 1e-3, "colour temperature keeps luminance at 1");
Check(tungsten.X > tungsten.Z * 2 && blueSky.Z > blueSky.X && Math.Abs(daylight.X - daylight.Z) < .15f, "2700 K is orange, 12000 K is blue, 6500 K is close to white");
Check(Math.Abs(Photometry.IlluminanceLux(Photometry.PointCandela(4000), 2000) / Photometry.IlluminanceLux(Photometry.PointCandela(4000), 1000) - .25) < 1e-9, "point-light illuminance follows the inverse-square law");
Check(Math.Abs(Photometry.IlluminanceLux(1000, 1000) - 1000) < 1e-9, "1000 cd at 1 m (1000 mm) gives 1000 lux");
Check(Photometry.SpotCandela(1000, 30) > Photometry.PointCandela(1000) * 10, "a narrow spot is much brighter on its axis than a bare lamp of the same power");
Check(Math.Abs(Photometry.LumensFor(LightKind.Point, 1.5, 250, 50) - 1.5 * Photometry.LuxPerUnit * .0625 * 4 * Math.PI) < 1e-6, "lumens needed for a target illuminance round-trips");
float cosO = MathF.Cos(.4f), cosI = MathF.Cos(.3f);
Check(Photometry.SpotFalloff(1, cosO, cosI) == 1 && Photometry.SpotFalloff(MathF.Cos(.5f), cosO, cosI) == 0
    && Photometry.SpotFalloff(MathF.Cos(.35f), cosO, cosI) is > 0 and < 1, "spot cone: full inside, zero outside, smooth edge");

// ---------------------------------------------------------------- lights in the tracer
var shadowed = new CadSession();
shadowed.AddObject(Occ.Sphere(10), "Ball", ObjKind.Solid);
shadowed.AddObject(Occ.Translate(Occ.Sphere(3), new Vec3(0, 0, 40)), "Blocker", ObjKind.Solid);
float Probe(PathTracer t, Vector3 from, Vector3 to) { var c = t.ProbeRadiance(from, to, 400); return c.X + c.Y + c.Z; }
PathTracer Tracer(CadSession s, params RenderLight[] lights)
{
    var t = new PathTracer(new RenderSettings { Env = RenderEnv.LightsOnly, Ground = false, Bounces = 1, Lights = [.. lights] });
    t.LoadScene(s); return t;
}
RenderLight Lamp(double z, double lumens = 20000, LightKind kind = LightKind.Point) => new() { Kind = kind, Position = new(0, 0, z), Aim = Vec3.Zero, Intensity = lumens, Size = .5, Kelvin = 6500 };
var topIn = new Vector3(0, 0, 100); var down = new Vector3(0, 0, -1);
var plain = new CadSession(); plain.AddObject(Occ.Sphere(10), "Ball", ObjKind.Solid);
float near = Probe(Tracer(plain, Lamp(110)), topIn, down), far = Probe(Tracer(plain, Lamp(210)), topIn, down);
Check(near > 0 && Math.Abs(near / far - 4) < .2, $"a lamp twice as far lights the surface a quarter as much (ratio {near / far:F2})");
Check(Probe(Tracer(plain), topIn, down) < 1e-6, "with only my lights and none placed the scene is dark");
float doubled = Probe(Tracer(plain, Lamp(110, 40000)), topIn, down);
Check(Math.Abs(doubled / near - 2) < .05, "doubling the lumens doubles the reflected light");
float lampLight = Probe(Tracer(plain, Lamp(110, (float)Photometry.LumensFor(LightKind.Point, 4, 100, 50))), topIn, down);
float sunLight = Probe(Tracer(plain, new RenderLight { Kind = LightKind.Sun, Position = new(0, 0, 100), Aim = Vec3.Zero, Intensity = 4 * Photometry.LuxPerUnit, Kelvin = 6500 }), topIn, down);
Check(Math.Abs(lampLight / sunLight - 1) < .05, $"a lamp and a sun giving equal illuminance light a surface equally ({lampLight / sunLight:F3})");
float sunSlanted = Probe(Tracer(plain, new RenderLight { Kind = LightKind.Sun, Position = new(0, 100, 100), Aim = Vec3.Zero, Intensity = 4 * Photometry.LuxPerUnit, Kelvin = 6500 }), topIn, down);
Check(sunSlanted > 0 && sunSlanted < sunLight * .85, "a sun at 45 degrees lights a flat-on surface less than one overhead (cosine law)");
var shadowSide = new Vector3(30, 0, 30); var shadowDirection = new Vector3(-30, 0, -20);
float open = Probe(Tracer(plain, Lamp(110)), shadowSide, shadowDirection), shaded = Probe(Tracer(shadowed, Lamp(110)), shadowSide, shadowDirection);
Check(open > 0 && shaded < open * .02, $"a body between the lamp and the surface casts a shadow ({shaded:F4} against {open:F4})");
float sunShaded = Probe(Tracer(shadowed, new RenderLight { Kind = LightKind.Sun, Position = new(0, 0, 100), Aim = Vec3.Zero, Intensity = 100000, Kelvin = 6500 }), shadowSide, shadowDirection);
float sunOpen = Probe(Tracer(plain, new RenderLight { Kind = LightKind.Sun, Position = new(0, 0, 100), Aim = Vec3.Zero, Intensity = 100000, Kelvin = 6500 }), shadowSide, shadowDirection);
Check(sunOpen > 0 && sunShaded < sunOpen * .05, "the sun casts shadows too");
var spot = Lamp(110, 20000, LightKind.Spot); spot.ConeAngle = 4; spot.Softness = 0;
float onAxis = Probe(Tracer(plain, spot), topIn, down);
var side = new Vector3(1, 0, 1) / MathF.Sqrt(2);
float offAxis = Probe(Tracer(plain, spot), side * 30, -side);
Check(onAxis > 0 && offAxis < onAxis * .001, "a spot lights inside its cone and not outside");
var warm = Lamp(110); warm.Kelvin = 2700;
var warmColour = Tracer(plain, warm).ProbeRadiance(topIn, down, 200);
Check(warmColour.X > warmColour.Z * 1.8, "a 2700 K lamp lights the surface orange");
var switchedOff = Lamp(110); switchedOff.Enabled = false;
Check(Probe(Tracer(plain, switchedOff), topIn, down) < 1e-6, "a switched-off light adds nothing");

// the ground catches the model's shadow over any backdrop
var catcherSettings = new RenderSettings { Width = 64, Height = 64, Samples = 24, Bounces = 2, Env = RenderEnv.LightsOnly, Background = RenderBg.White, Ground = true,
    Lights = [new RenderLight { Kind = LightKind.Sun, Position = new(40, -40, 80), Aim = Vec3.Zero, Intensity = 100000, Kelvin = 6500 }] };
var catcher = new PathTracer(catcherSettings); catcher.LoadScene(session);
byte[] catcherPixels = [];
catcher.Render(session.Camera, (p, w, h, n, done) => catcherPixels = (byte[])p.Clone(), default);
Check(catcherPixels[3] == 255 && catcherPixels[0] == 255 && catcherPixels[2] == 255, "shadow catcher leaves the white backdrop pure white away from the shadow");
Check(Enumerable.Range(0, 64 * 64).Any(i => catcherPixels[4 * i + 3] == 255 && catcherPixels[4 * i + 1] < 235 && catcherPixels[4 * i + 1] == catcherPixels[4 * i + 2]), "the model shadow darkens the white backdrop");

// ---------------------------------------------------------------- the document remembers lights and backdrop
var designed = new CadSession();
designed.AddObject(Occ.Sphere(10), "Ball", ObjKind.Solid);
designed.Camera.Target = Vec3.Zero; designed.Camera.Distance = 60; designed.Camera.Width = 800; designed.Camera.Height = 600;
var sun = designed.AddLight(LightKind.Sun); var lamp = designed.AddLight(LightKind.Point); var beam = designed.AddLight(LightKind.Spot);
Check(designed.Lights.Count == 3 && designed.SelectedLight == beam && sun.Name == "Sun 1" && lamp.Intensity > 0, "adding lights names and selects them");
Check((sun.Position - sun.Aim).Length > 30 && lamp.Position.DistanceTo(Vec3.Zero) > 20, "new lights start outside the model");
lamp.Position = new Vec3(12.5, -30, 22); lamp.Kelvin = 3200; beam.ConeAngle = 33; beam.Softness = .5; sun.Intensity = 64000; sun.Enabled = false;
designed.RenderSetup = new RenderSetup { Env = RenderEnv.LightsOnly, Background = RenderBg.Green, Ground = false, Exposure = 1.6 };
designed.Save(Path.Combine(folder, "lights.prism"));
var back = new CadSession(); back.Open(Path.Combine(folder, "lights.prism"));
Check(back.Lights.Count == 3 && back.Lights.Select(l => l.Id).SequenceEqual(designed.Lights.Select(l => l.Id)), "lights survive save and reopen");
var backLamp = back.Lights.Single(l => l.Kind == LightKind.Point); var backBeam = back.Lights.Single(l => l.Kind == LightKind.Spot); var backSun = back.Lights.Single(l => l.Kind == LightKind.Sun);
Check(backLamp.Position == lamp.Position && backLamp.Kelvin == 3200 && backBeam.ConeAngle == 33 && backBeam.Softness == .5 && backSun.Intensity == 64000 && !backSun.Enabled, "light positions, colour, cone and switches survive");
Check(back.RenderSetup.Background == RenderBg.Green && back.RenderSetup.Env == RenderEnv.LightsOnly && !back.RenderSetup.Ground && back.RenderSetup.Exposure == 1.6, "backdrop, lighting, ground and exposure survive");
var bare = new CadSession(); bare.AddObject(Occ.Sphere(5), "Ball", ObjKind.Solid); bare.Save(Path.Combine(folder, "bare.prism"));
var bareBack = new CadSession(); bareBack.Open(Path.Combine(folder, "bare.prism"));
Check(bareBack.Lights.Count == 0 && bareBack.RenderSetup.Equals(new RenderSetup()), "a document without lights opens with defaults");
back.NewDocument(); Check(back.Lights.Count == 0, "a new document starts without lights");

// ---------------------------------------------------------------- the render panel and viewport
var renderCommand = new RenderCommand(); designed.StartCommand(renderCommand);
var backgroundChoice = renderCommand.Params.OfType<ChoiceParam>().Single(p => p.Label == "Background");
Check(backgroundChoice.Index == (int)RenderBg.Green, "render panel starts from the document backdrop");
backgroundChoice.Index = (int)RenderBg.Black;
Check(designed.RenderSetup.Background == RenderBg.Black && designed.Dirty, "choosing a backdrop is stored in the document");
designed.CancelCommand();
var panel = new LightsCommand(); designed.StartCommand(panel);
Check(panel.Tool is LightTool && designed.ActiveTool is LightTool, "the lights panel owns a drag tool");
var overlay = new Overlay(); designed.BuildOverlay(overlay);
Check(overlay.Dots.Count(d => d.Color == 0xFFFFC53D || d.Color == 0xFF8A8F98) >= 4 && overlay.Labels.Any(l => l.Text.Contains("lm")) && overlay.Labels.Any(l => l.Text.Contains("lx")), "the viewport draws every light with its rating");
var screen = designed.Camera.Project(lamp.Position)!.Value;
Check(designed.PointerDown(new PointerInfo(screen.x + 3, screen.y - 2, true, false, false, false)) && designed.SelectedLightId == lamp.Id, "pressing on a lamp selects it");
var moved = lamp.Position;
designed.PointerMove(new PointerInfo(screen.x + 60, screen.y + 40, true, false, false, false));
designed.PointerUp(new PointerInfo(screen.x + 60, screen.y + 40, false, false, false, false));
Check(lamp.Position.DistanceTo(moved) > 1, "dragging the lamp moves it in the scene");
var newScreen = designed.Camera.Project(lamp.Position)!.Value;
Check(Math.Abs(newScreen.x - (screen.x + 60)) < 2 && Math.Abs(newScreen.y - (screen.y + 40)) < 2, "the lamp follows the pointer on screen");
Check(Math.Abs(panel.Params.OfType<NumberParam>().Single(p => p.Label == "Position X").Value - Math.Round(lamp.Position.X, 2)) < 1e-9, "the panel shows the dragged position");
var sunScreen = designed.Camera.Project(sun.Position)!.Value; double sunReach = (sun.Position - sun.Aim).Length; var sunWas = sun.TowardLight;
designed.PointerDown(new PointerInfo(sunScreen.x, sunScreen.y, true, false, false, false));
designed.PointerMove(new PointerInfo(sunScreen.x + 50, sunScreen.y + 30, true, false, false, false));
designed.PointerUp(new PointerInfo(0, 0, false, false, false, false));
Check(Math.Abs((sun.Position - sun.Aim).Length - sunReach) < 1e-6 && sun.TowardLight.DistanceTo(sunWas) > .05, "dragging the sun turns its direction without changing its distance");
int held = designed.Lights.Count;
panel.Params.OfType<ActionParam>().Single(p => p.Label == "Delete this light").Run();
Check(designed.Lights.Count == held - 1, "a light can be deleted");
designed.CancelCommand();
int lightsBefore = designed.Lights.Count; designed.StartCommand(new LightsCommand(LightKind.Sun));
Check(designed.Lights.Count == lightsBefore + 1 && designed.SelectedLight!.Kind == LightKind.Sun, "the ribbon Sun button adds a sun and opens the editor");
designed.CancelCommand();
var lightRender = designed.StartRender(new RenderSettings { Width = 32, Height = 32, Samples = 2, Bounces = 1 });
var lightUntil = DateTime.UtcNow.AddSeconds(15);
while (lightRender.Running && DateTime.UtcNow < lightUntil) await Task.Delay(10);
Check(lightRender.Finished && lightRender.Error == null, "a render using the document lights completes");

Console.WriteLine($"SUCCESS: {checks} checks; artifacts: {folder}");

delegate Vector3 EvaluateBsdf(Vector3 normal,Vector3 view,Vector3 light,Vector3 albedo,float metallic,float roughness,out float pdf);

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
