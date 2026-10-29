using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prism.Rendering;

/// <summary>A small animated-GIF encoder (median-cut palette, LZW) so animations can be saved without any library.</summary>
public static class GifWriter
{
    /// <summary>frames: BGRA premultiplied, all width x height. delayCs: hundredths of a second per frame.</summary>
    public static void Save(string path, IReadOnlyList<byte[]> frames, int width, int height, int delayCs, bool loop)
    {
        if (frames.Count == 0) throw new InvalidOperationException("No frames.");
        // flatten onto white, collect colour samples
        var rgb = frames.Select(f => Flatten(f, width, height)).ToList();
        var samples = new List<int>();
        foreach (var f in rgb) for (int i = 0; i < f.Length; i += 3 * 5) samples.Add(f[i] << 16 | f[i + 1] << 8 | f[i + 2]);
        var palette = MedianCut(samples, 256);
        // nearest palette entry for every 15-bit colour (worked out when first needed)
        var cache = new byte[32768]; var have = new bool[32768];
        byte Nearest(int r, int g, int b)
        {
            int key = (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);
            if (have[key]) return cache[key];
            int rr = (r >> 3) << 3 | 4, gg = (g >> 3) << 3 | 4, bb = (b >> 3) << 3 | 4, best = 0, bd = int.MaxValue;
            for (int p = 0; p < palette.Length; p++)
            {
                int dr = palette[p] >> 16 & 255, dg = palette[p] >> 8 & 255, db = palette[p] & 255;
                int d = (dr - rr) * (dr - rr) * 3 + (dg - gg) * (dg - gg) * 4 + (db - bb) * (db - bb) * 2;
                if (d < bd) { bd = d; best = p; }
            }
            have[key] = true; return cache[key] = (byte)best;
        }
        using var fs = File.Create(path); using var w = new BinaryWriter(fs);
        w.Write("GIF89a"u8.ToArray()); w.Write((ushort)width); w.Write((ushort)height);
        w.Write((byte)0xF7); w.Write((byte)0); w.Write((byte)0);               // global palette of 256 entries
        for (int i = 0; i < 256; i++) { int c = i < palette.Length ? palette[i] : 0; w.Write((byte)(c >> 16)); w.Write((byte)(c >> 8)); w.Write((byte)c); }
        if (loop) { w.Write((byte)0x21); w.Write((byte)0xFF); w.Write((byte)11); w.Write("NETSCAPE2.0"u8.ToArray()); w.Write((byte)3); w.Write((byte)1); w.Write((ushort)0); w.Write((byte)0); }
        foreach (var f in rgb)
        {
            w.Write((byte)0x21); w.Write((byte)0xF9); w.Write((byte)4); w.Write((byte)0); w.Write((ushort)delayCs); w.Write((byte)0); w.Write((byte)0);
            w.Write((byte)0x2C); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)width); w.Write((ushort)height); w.Write((byte)0);
            var idx = new byte[width * height];
            for (int i = 0; i < idx.Length; i++) idx[i] = Nearest(f[3 * i], f[3 * i + 1], f[3 * i + 2]);
            w.Write((byte)8); Lzw(fs, idx); w.Write((byte)0);
        }
        w.Write((byte)0x3B);
    }

    static byte[] Flatten(byte[] bgra, int w, int h)
    {
        var o = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            int a = bgra[4 * i + 3], inv = 255 - a;
            o[3 * i] = (byte)Math.Min(255, bgra[4 * i + 2] + inv); o[3 * i + 1] = (byte)Math.Min(255, bgra[4 * i + 1] + inv); o[3 * i + 2] = (byte)Math.Min(255, bgra[4 * i] + inv);
        }
        return o;
    }

    static int[] MedianCut(List<int> colors, int n)
    {
        var boxes = new List<List<int>> { colors };
        while (boxes.Count < n)
        {
            var big = boxes.Where(b => b.Count > 1).OrderByDescending(b => b.Count).FirstOrDefault();
            if (big == null) break;
            int rmin = 255, rmax = 0, gmin = 255, gmax = 0, bmin = 255, bmax = 0;
            foreach (var c in big) { int r = c >> 16 & 255, g = c >> 8 & 255, b = c & 255; rmin = Math.Min(rmin, r); rmax = Math.Max(rmax, r); gmin = Math.Min(gmin, g); gmax = Math.Max(gmax, g); bmin = Math.Min(bmin, b); bmax = Math.Max(bmax, b); }
            int axis = rmax - rmin >= gmax - gmin && rmax - rmin >= bmax - bmin ? 16 : gmax - gmin >= bmax - bmin ? 8 : 0;
            big.Sort((a, b) => (a >> axis & 255).CompareTo(b >> axis & 255));
            int mid = big.Count / 2;
            var right = big.GetRange(mid, big.Count - mid); big.RemoveRange(mid, big.Count - mid);
            boxes.Add(right);
        }
        return boxes.Select(b => { long r = 0, g = 0, bl = 0; foreach (var c in b) { r += c >> 16 & 255; g += c >> 8 & 255; bl += c & 255; } int k = Math.Max(b.Count, 1); return (int)(r / k) << 16 | (int)(g / k) << 8 | (int)(bl / k); }).ToArray();
    }

    static void Lzw(Stream s, byte[] data)
    {
        const int min = 8; int clear = 1 << min, eoi = clear + 1, next = eoi + 1, size = min + 1;
        var dict = new Dictionary<int, int>();
        var block = new List<byte>(255);
        uint buf = 0; int bits = 0;
        void Emit(int code)
        {
            buf |= (uint)code << bits; bits += size;
            while (bits >= 8) { block.Add((byte)buf); buf >>= 8; bits -= 8; if (block.Count == 255) Flush(); }
        }
        void Flush() { if (block.Count == 0) return; s.WriteByte((byte)block.Count); s.Write(block.ToArray()); block.Clear(); }
        Emit(clear);
        int prefix = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            int c = data[i], key = prefix << 8 | c;
            if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
            Emit(prefix);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next - 1 == (1 << size) && size < 12) size++;      // the code just added needs one more bit from now on
            }
            else { Emit(clear); dict.Clear(); next = eoi + 1; size = min + 1; }
            prefix = c;
        }
        Emit(prefix); Emit(eoi);
        if (bits > 0) { block.Add((byte)buf); }
        Flush();
    }
}
