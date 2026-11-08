using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.Core.Cam;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Cam;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/cam/README.md">CAM image carving.</seealso>
[Trait("Feature", "CAM")]
[Trait("Layer", "Headless")]
public class ImageReliefTests
{
    static byte[] Png(int w, int h, byte[] gray)
    {
        using var ms = new MemoryStream();
        void Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); ms.Write(len);
            var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); ms.Write(body);
            uint crc = 0xFFFFFFFF; foreach (var b in body) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1)); }
            var c = BitConverter.GetBytes(~crc); Array.Reverse(c); ms.Write(c);
        }
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new List<byte>(); foreach (var v in new[] { w, h }) { var b = BitConverter.GetBytes(v); Array.Reverse(b); ihdr.AddRange(b); }
        ihdr.AddRange([8, 0, 0, 0, 0]); Chunk("IHDR", [.. ihdr]);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true)) for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(gray, y * w, w); }
        Chunk("IDAT", raw.ToArray()); Chunk("IEND", []);
        return ms.ToArray();
    }

    [AvaloniaFact(DisplayName = "CAM-H005 | Image Carve cuts deepest where the picture is dark and stays at the surface where it is light")]
    [Trait("TestId", "CAM-H005")]
    public async Task CarvingDepthFollowsBrightness()
    {
        string file = Path.Combine(Path.GetTempPath(), "prism-carve-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(file, Png(2, 1, [0, 255]));   // left half black, right half white
        try
        {
            var vm = new MainViewModel();
            var api = new AutomationApi(vm, null);
            var args = new JsonObject { ["name"] = "cnc_image_relief", ["params"] = new JsonObject { ["Image file"] = file, ["Width of the carving"] = 10.0, ["Tool diameter"] = 2.0, ["Total cutting depth"] = 2.0, ["Top of the material (Z)"] = 0.0, ["Stepover"] = 50.0 } };
            await api.CallAsync("run_command", args);
            var path = (ToolpathInfo)vm.Session.Objects.Single(o => o.Kind == Prism.Core.ObjKind.Toolpath).Asm!;
            Assert.Equal("Image carve", path.Operation);
            var cuts = Enumerable.Range(0, path.MoveCount).Select(path.Move).Where(m => m.kind == MoveKind.Cut || m.kind == MoveKind.Plunge).ToList();
            Assert.All(cuts.Where(m => m.x < 4.9), m => Assert.InRange(m.z, -2.001, -1.999));
            Assert.All(cuts.Where(m => m.x > 5.1), m => Assert.InRange(m.z, -.001, .001));
            Assert.Contains("ball nose", path.Tool);
        }
        finally { File.Delete(file); }
    }
}
