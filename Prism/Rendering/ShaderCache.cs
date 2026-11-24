using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Silk.NET.OpenGL;

namespace Prism.Rendering;

/// <summary>
/// Compiled shader programs kept as driver binaries (start-up 2026-11-23: on Windows the mesh shader took 2.6 s to compile through ANGLE, once for each
/// of the four viewport panes, so the window stayed frozen about 11 s after it opened). The first pane compiles and saves the binary; the others load it,
/// and later starts load it from %LOCALAPPDATA%\Prism\ShaderCache. A binary the driver refuses (a driver update, say) is compiled again. Graphics that
/// cannot hand out binaries (WebGL) compile every time, as before.
/// </summary>
public static unsafe class ShaderCache
{
    static readonly ConcurrentDictionary<string, (GLEnum format, byte[] data)> _memory = new();

    static string? Folder =>
        OperatingSystem.IsBrowser() ? null : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "ShaderCache");

    /// <summary>Programs loaded from a binary and programs compiled, since start (for the start-up log and tests).</summary>
    public static int Loaded, Compiled;

    static bool Supported(GL gl)
    {
        try { gl.GetInteger(GLEnum.NumProgramBinaryFormats, out int formats); return formats > 0; }
        catch (Exception) { return false; }
    }

    static string Key(GL gl, string vs, string fs)
    {
        string driver = "";
        try { driver = gl.GetStringS(StringName.Renderer) + "|" + gl.GetStringS(StringName.Version); } catch (Exception) { }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(driver + "\0" + vs + "\0" + fs));
        return Convert.ToHexString(hash)[..32];
    }

    /// <summary>A linked program for these sources: from a cached binary when the driver accepts it, otherwise compiled by <paramref name="compile"/>.</summary>
    public static uint Program(GL gl, string vs, string fs, Func<uint> compile)
    {
        if (!Supported(gl)) { Compiled++; return compile(); }
        string key = Key(gl, vs, fs);
        if (TryLoad(gl, key) is uint loaded) { Loaded++; return loaded; }
        uint p = compile();
        Compiled++;
        Save(gl, key, p);
        return p;
    }

    static uint? TryLoad(GL gl, string key)
    {
        if (!_memory.TryGetValue(key, out var bin))
        {
            if (Folder is not { } folder) return null;
            string path = Path.Combine(folder, key + ".bin");
            try
            {
                if (!File.Exists(path)) return null;
                var all = File.ReadAllBytes(path);
                if (all.Length <= 4) return null;
                bin = ((GLEnum)BitConverter.ToInt32(all, 0), all[4..]);
            }
            catch (Exception) { return null; }
        }
        uint p = gl.CreateProgram();
        fixed (byte* data = bin.data) gl.ProgramBinary(p, bin.format, data, (uint)bin.data.Length);
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0) { gl.DeleteProgram(p); _memory.TryRemove(key, out _); return null; }
        _memory[key] = bin;
        return p;
    }

    static void Save(GL gl, string key, uint program)
    {
        try
        {
            gl.GetProgram(program, GLEnum.ProgramBinaryLength, out int length);
            if (length <= 0) return;
            var data = new byte[length];
            GLEnum format;
            uint written;
            fixed (byte* buffer = data) gl.GetProgramBinary(program, (uint)length, &written, &format, buffer);
            if (written == 0) return;
            if (written < length) data = data[..(int)written];
            _memory[key] = (format, data);
            if (Folder is not { } folder) return;
            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, key + ".bin"));
            file.Write(BitConverter.GetBytes((int)format));
            file.Write(data);
        }
        catch (Exception) { /* a binary that cannot be kept only means compiling again next time */ }
    }
}
