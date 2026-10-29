using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prism.Kernel;

namespace Prism.Core.Export;

/// <summary>
/// Everything an AR viewer needs, with no extra libraries: a binary glTF (GLB) for Android and the web, and a USDZ for iPhone / iPad AR Quick Look.
/// Units: the model is in millimetres; the files are in metres, Y-up, standing on the floor with the model's footprint centred.
/// </summary>
public static class ArExport
{
    sealed record Part(string Name, float[] P, float[] N, int[] I, (float r, float g, float b) Color, float Metal, float Rough);

    static (float r, float g, float b) Lin(uint c) { var (r, g, b) = Palette.Unpack(c); return ((float)Math.Pow(r, 2.2), (float)Math.Pow(g, 2.2), (float)Math.Pow(b, 2.2)); }

    /// <summary>Collect the visible geometry, converted to metres, Y-up and sitting on the floor. fitMetres > 0 scales the largest side to that many metres.</summary>
    static List<Part> Collect(CadSession s, double fitMetres, out double scaleUsed)
    {
        var objs = s.Objects.Where(o => o.Visible && !o.IsAnnotation && !s.HiddenIds.Contains(o.Id)).ToList();
        var mn = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var mx = -mn;
        var meshes = new List<(SceneObject o, MeshData m)>();
        foreach (var o in objs)
        {
            MeshData m; try { m = o.Mesh; } catch { continue; }
            if (m.Indices.Length == 0) continue;
            meshes.Add((o, m));
            mn = new(Math.Min(mn.X, m.Min.X), Math.Min(mn.Y, m.Min.Y), Math.Min(mn.Z, m.Min.Z)); mx = new(Math.Max(mx.X, m.Max.X), Math.Max(mx.Y, m.Max.Y), Math.Max(mx.Z, m.Max.Z));
        }
        if (meshes.Count == 0) throw new InvalidOperationException("There is nothing visible to export.");
        double biggest = Math.Max(mx.X - mn.X, Math.Max(mx.Y - mn.Y, mx.Z - mn.Z));
        scaleUsed = fitMetres > 0 ? fitMetres / biggest : 0.001;
        var cx = (mn.X + mx.X) / 2; var cy = (mn.Y + mx.Y) / 2; var zmin = mn.Z;
        var res = new List<Part>();
        foreach (var (o, m) in meshes)
        {
            int nv = m.Positions.Length / 3;
            var P = new float[nv * 3]; var N = new float[nv * 3];
            for (int i = 0; i < nv; i++)
            {
                // CAD (x right, y back, z up) to glTF/USD (x right, y up, z toward the viewer): (x, y, z) -> (x, z, -y)
                double x = (m.Positions[3 * i] - cx) * scaleUsed, y = (m.Positions[3 * i + 1] - cy) * scaleUsed, z = (m.Positions[3 * i + 2] - zmin) * scaleUsed;
                P[3 * i] = (float)x; P[3 * i + 1] = (float)z; P[3 * i + 2] = (float)-y;
                N[3 * i] = m.Normals[3 * i]; N[3 * i + 1] = m.Normals[3 * i + 2]; N[3 * i + 2] = -m.Normals[3 * i + 1];
            }
            res.Add(new Part(o.Name, P, N, m.Indices, Lin(o.Color), Math.Clamp(o.Look.Metal, 0f, 1f), Math.Clamp(1f - o.Look.Gloss, 0.05f, 1f)));
        }
        return res;
    }

    // ------------------------------------------------------------------ GLB
    public static string ExportGlb(CadSession s, string path, double fitMetres = 0)
    {
        var parts = Collect(s, fitMetres, out _);
        using var bin = new MemoryStream(); var bw = new BinaryWriter(bin);
        var views = new List<object>(); var accessors = new List<object>(); var meshes = new List<object>(); var materials = new List<object>(); var nodes = new List<object>();
        void Align() { while (bin.Length % 4 != 0) bw.Write((byte)0); }
        int View(long off, long len, int target) { views.Add(new { buffer = 0, byteOffset = off, byteLength = len, target }); return views.Count - 1; }
        for (int k = 0; k < parts.Count; k++)
        {
            var p = parts[k];
            Align(); long o0 = bin.Length; foreach (var f in p.P) bw.Write(f); int vPos = View(o0, bin.Length - o0, 34962);
            Align(); o0 = bin.Length; foreach (var f in p.N) bw.Write(f); int vNor = View(o0, bin.Length - o0, 34962);
            Align(); o0 = bin.Length; foreach (var i in p.I) bw.Write((uint)i); int vIdx = View(o0, bin.Length - o0, 34963);
            float[] min = [float.MaxValue, float.MaxValue, float.MaxValue], max = [float.MinValue, float.MinValue, float.MinValue];
            for (int i = 0; i < p.P.Length; i++) { min[i % 3] = Math.Min(min[i % 3], p.P[i]); max[i % 3] = Math.Max(max[i % 3], p.P[i]); }
            int aPos = accessors.Count; accessors.Add(new { bufferView = vPos, componentType = 5126, count = p.P.Length / 3, type = "VEC3", min, max });
            int aNor = accessors.Count; accessors.Add(new { bufferView = vNor, componentType = 5126, count = p.N.Length / 3, type = "VEC3" });
            int aIdx = accessors.Count; accessors.Add(new { bufferView = vIdx, componentType = 5125, count = p.I.Length, type = "SCALAR" });
            materials.Add(new { name = p.Name, doubleSided = true, pbrMetallicRoughness = new { baseColorFactor = new[] { p.Color.r, p.Color.g, p.Color.b, 1f }, metallicFactor = p.Metal, roughnessFactor = p.Rough } });
            meshes.Add(new { name = p.Name, primitives = new[] { new { attributes = new { POSITION = aPos, NORMAL = aNor }, indices = aIdx, material = k, mode = 4 } } });
            nodes.Add(new { name = p.Name, mesh = k });
        }
        Align();
        var json = JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0", generator = "Prism" },
            scene = 0, scenes = new[] { new { nodes = Enumerable.Range(0, nodes.Count).ToArray() } },
            nodes, meshes, materials, accessors, bufferViews = views, buffers = new[] { new { byteLength = bin.Length } },
        });
        var jb = Encoding.UTF8.GetBytes(json); int pad = (4 - jb.Length % 4) % 4; Array.Resize(ref jb, jb.Length + pad); for (int i = jb.Length - pad; i < jb.Length; i++) jb[i] = 0x20;
        var bb = bin.ToArray();
        using var fs = File.Create(path); using var w = new BinaryWriter(fs);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + jb.Length + 8 + bb.Length));
        w.Write((uint)jb.Length); w.Write(0x4E4F534Au); w.Write(jb);
        w.Write((uint)bb.Length); w.Write(0x004E4942u); w.Write(bb);
        return path;
    }

    // ------------------------------------------------------------------ USDZ
    public static string ExportUsdz(CadSession s, string path, double fitMetres = 0)
    {
        var parts = Collect(s, fitMetres, out _);
        var sb = new StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(float v, string fmt) => v.ToString(fmt, ci);
        sb.AppendLine("#usda 1.0\n(\n    defaultPrim = \"Model\"\n    metersPerUnit = 1\n    upAxis = \"Y\"\n)\n");
        sb.AppendLine("def Xform \"Model\"\n{");
        for (int k = 0; k < parts.Count; k++)
        {
            var p = parts[k];
            sb.AppendLine($"    def Mesh \"Part{k}\"\n    {{");
            sb.AppendLine("        uniform bool doubleSided = true");
            sb.AppendLine("        int[] faceVertexCounts = [" + string.Join(", ", Enumerable.Repeat("3", p.I.Length / 3)) + "]");
            sb.AppendLine("        int[] faceVertexIndices = [" + string.Join(", ", p.I) + "]");
            sb.AppendLine("        point3f[] points = [" + string.Join(", ", Enumerable.Range(0, p.P.Length / 3).Select(i => $"({F(p.P[3 * i], "0.#####")}, {F(p.P[3 * i + 1], "0.#####")}, {F(p.P[3 * i + 2], "0.#####")})")) + "]");
            sb.AppendLine("        normal3f[] normals = [" + string.Join(", ", Enumerable.Range(0, p.N.Length / 3).Select(i => $"({F(p.N[3 * i], "0.####")}, {F(p.N[3 * i + 1], "0.####")}, {F(p.N[3 * i + 2], "0.####")})")) + "] (\n            interpolation = \"vertex\"\n        )");
            sb.AppendLine($"        rel material:binding = </Model/Materials/Mat{k}>");
            sb.AppendLine("    }");
        }
        sb.AppendLine("    def Scope \"Materials\"\n    {");
        for (int k = 0; k < parts.Count; k++)
        {
            var p = parts[k];
            sb.AppendLine($"        def Material \"Mat{k}\"\n        {{\n            token outputs:surface.connect = </Model/Materials/Mat{k}/PBR.outputs:surface>");
            sb.AppendLine("            def Shader \"PBR\"\n            {\n                uniform token info:id = \"UsdPreviewSurface\"");
            sb.AppendLine($"                color3f inputs:diffuseColor = ({F(p.Color.r, "0.####")}, {F(p.Color.g, "0.####")}, {F(p.Color.b, "0.####")})");
            sb.AppendLine($"                float inputs:metallic = {F(p.Metal, "0.###")}\n                float inputs:roughness = {F(p.Rough, "0.###")}\n                token outputs:surface\n            }}\n        }}");
        }
        sb.AppendLine("    }\n}");
        WriteUsdz(path, "model.usda", Encoding.UTF8.GetBytes(sb.ToString()));
        return path;
    }

    /// <summary>A USDZ is an uncompressed zip whose files start on 64-byte boundaries.</summary>
    static void WriteUsdz(string path, string name, byte[] data)
    {
        using var fs = File.Create(path); using var w = new BinaryWriter(fs);
        var nb = Encoding.ASCII.GetBytes(name);
        int headerLen = 30 + nb.Length;
        int pad = (64 - headerLen % 64) % 64;
        uint crc = Crc32(data);
        w.Write(0x04034B50u); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x21);
        w.Write(crc); w.Write((uint)data.Length); w.Write((uint)data.Length); w.Write((ushort)nb.Length); w.Write((ushort)pad);
        w.Write(nb); for (int i = 0; i < pad; i++) w.Write((byte)0);
        w.Write(data);
        long cd = fs.Position;
        w.Write(0x02014B50u); w.Write((ushort)20); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x21);
        w.Write(crc); w.Write((uint)data.Length); w.Write((uint)data.Length); w.Write((ushort)nb.Length); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write(0u); w.Write(0u);
        w.Write(nb);
        long end = fs.Position;
        w.Write(0x06054B50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1); w.Write((uint)(end - cd)); w.Write((uint)cd); w.Write((ushort)0);
    }

    static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
        return c ^ 0xFFFFFFFF;
    }

    // ------------------------------------------------------------------ package + share
    public static string PackageFolder(string name)
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "AR", string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')));
        Directory.CreateDirectory(d); return d;
    }

    /// <summary>Write model.glb, model.usdz and an index.html (a web viewer with an "AR" button) into a folder.</summary>
    public static string ExportPackage(CadSession s, string folder, string title, double fitMetres = 0)
    {
        Directory.CreateDirectory(folder);
        ExportGlb(s, Path.Combine(folder, "model.glb"), fitMetres);
        ExportUsdz(s, Path.Combine(folder, "model.usdz"), fitMetres);
        File.WriteAllText(Path.Combine(folder, "index.html"), Html(title));
        return folder;
    }

    static string Html(string title)
    {
        string t = WebUtility.HtmlEncode(title);
        return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n"
            + $"<title>{t}</title>\n"
            + "<script type=\"module\" src=\"https://ajax.googleapis.com/ajax/libs/model-viewer/3.5.0/model-viewer.min.js\"></script>\n"
            + "<style>html,body{height:100%;margin:0;background:#12161c;color:#dfe6f2;font-family:system-ui,sans-serif}model-viewer{width:100%;height:100%;--poster-color:#12161c}"
            + ".ar{position:absolute;bottom:24px;left:50%;transform:translateX(-50%);padding:14px 28px;border-radius:30px;background:#2f7bf0;color:#fff;border:0;font-size:17px}"
            + ".hint{position:absolute;top:12px;left:0;right:0;text-align:center;font-size:13px;opacity:.7}</style></head>\n"
            + $"<body><model-viewer src=\"model.glb\" ios-src=\"model.usdz\" alt=\"{t}\" ar ar-modes=\"webxr scene-viewer quick-look\" camera-controls auto-rotate shadow-intensity=\"1\" environment-image=\"neutral\" exposure=\"1.1\">\n"
            + "<button slot=\"ar-button\" class=\"ar\">View in your space</button></model-viewer>\n"
            + $"<div class=\"hint\">{t} · drag to turn · pinch to zoom</div></body></html>";
    }

    // --- a tiny static web server, so a phone on the same Wi-Fi can open the package (no admin rights needed)
    static TcpListener? _listener; static CancellationTokenSource? _cts; static string _root = "";
    public static bool Sharing => _listener != null;
    public static string? ShareUrl { get; private set; }

    public static string LanAddress()
    {
        // prefer an ordinary home / office network address over VPN or virtual adapters
        int Score(IPAddress a) { var b = a.GetAddressBytes(); return b[0] == 192 && b[1] == 168 ? 3 : b[0] == 10 ? 2 : b[0] == 172 && b[1] is >= 16 and <= 31 ? 2 : 0; }
        (int score, string ip) best = (-1, "127.0.0.1");
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                int sc = Score(ua.Address) + (ni.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet ? 1 : 0);
                if (sc > best.score) best = (sc, ua.Address.ToString());
            }
        }
        return best.ip;
    }

    public static string StartShare(string folder)
    {
        StopShare();
        _root = Path.GetFullPath(folder);
        _listener = new TcpListener(IPAddress.Any, 0); _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        var l = _listener; var ct = _cts.Token;
        _ = Task.Run(async () => { while (!ct.IsCancellationRequested) { try { var c = await l.AcceptTcpClientAsync(ct); _ = Task.Run(() => Serve(c)); } catch { break; } } });
        return ShareUrl = $"http://{LanAddress()}:{port}/";
    }

    public static void StopShare() { _cts?.Cancel(); try { _listener?.Stop(); } catch { } _listener = null; ShareUrl = null; }

    static void Serve(TcpClient c)
    {
        using (c)
        try
        {
            var ns = c.GetStream(); ns.ReadTimeout = 5000;
            var buf = new byte[4096]; int n = ns.Read(buf, 0, buf.Length);
            var req = Encoding.ASCII.GetString(buf, 0, n); var line = req.Split('\n')[0].Split(' ');
            string rel = line.Length > 1 ? Uri.UnescapeDataString(line[1].Split('?')[0].TrimStart('/')) : "";
            if (rel == "") rel = "index.html";
            var file = Path.GetFullPath(Path.Combine(_root, rel));
            if (!file.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            { ns.Write(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")); return; }
            var data = File.ReadAllBytes(file);
            string type = Path.GetExtension(file).ToLowerInvariant() switch { ".html" => "text/html; charset=utf-8", ".glb" => "model/gltf-binary", ".usdz" => "model/vnd.usdz+zip", _ => "application/octet-stream" };
            ns.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {data.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n")); ns.Write(data);
        }
        catch { }
    }
}
