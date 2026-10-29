using System;
using System.IO;
using System.IO.Compression;

namespace Prism.Rendering;

/// <summary>A minimal PNG encoder (8-bit RGBA) so rendered images can be saved without any imaging library.</summary>
public static class PngWriter
{
    static readonly uint[] Table = MakeTable();
    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }
    static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length); s.Write(len);
        var tb = System.Text.Encoding.ASCII.GetBytes(type); s.Write(tb); s.Write(data);
        Span<byte> crc = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(tb, data)); s.Write(crc);
    }

    /// <summary>bgraPremul: 4 bytes per pixel in B,G,R,A order with colour premultiplied by alpha (as the renderer produces it).</summary>
    public static void Save(string path, byte[] bgraPremul, int width, int height)
        => File.WriteAllBytes(path, Encode(bgraPremul, width, height));

    /// <summary>Encode BGRA pixels as a PNG in memory.</summary>
    public static byte[] Encode(byte[] bgraPremul, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int o = y * (width * 4 + 1); raw[o] = 0;
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4; int a = bgraPremul[i + 3];
                // undo the premultiplication for the file
                int r = a == 0 ? 0 : Math.Min(255, bgraPremul[i + 2] * 255 / a), g = a == 0 ? 0 : Math.Min(255, bgraPremul[i + 1] * 255 / a), b = a == 0 ? 0 : Math.Min(255, bgraPremul[i] * 255 / a);
                raw[o + 1 + x * 4] = (byte)r; raw[o + 2 + x * 4] = (byte)g; raw[o + 3 + x * 4] = (byte)b; raw[o + 4 + x * 4] = (byte)a;
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        using var fs = new MemoryStream();
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(fs, "IHDR", ihdr); Chunk(fs, "IDAT", ms.ToArray()); Chunk(fs, "IEND", []);
        return fs.ToArray();
    }
}
