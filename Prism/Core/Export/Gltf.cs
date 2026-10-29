using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Prism.Meshing;

namespace Prism.Core.Export;

/// <summary>Writes a binary glTF 2.0 (.glb): one node per body with a flat-colour material. Millimetres become metres and Z-up becomes Y-up, as glTF expects.</summary>
public static class Gltf
{
    public static void SaveGlb(string path, IReadOnlyList<(string name, TriMesh mesh, uint argb)> parts)
    {
        var bin = new MemoryStream();
        var accessors = new List<object>(); var views = new List<object>(); var meshes = new List<object>(); var nodes = new List<object>(); var materials = new List<object>();
        void Pad() { while (bin.Length % 4 != 0) bin.WriteByte(0); }
        int View(byte[] data, int target)
        {
            Pad(); int offset = (int)bin.Length; bin.Write(data); views.Add(new { buffer = 0, byteOffset = offset, byteLength = data.Length, target });
            return views.Count - 1;
        }
        foreach (var (name, mesh, argb) in parts)
        {
            if (mesh.TriCount == 0) continue;
            int vn = mesh.VertexCount; var pos = new float[vn * 3]; var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue }; var max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < vn; i++)
            {
                // (x, y, z) mm, Z up  ->  (x, z, -y) m, Y up
                float x = mesh.V[i * 3] * 0.001f, y = mesh.V[i * 3 + 2] * 0.001f, z = -mesh.V[i * 3 + 1] * 0.001f;
                pos[i * 3] = x; pos[i * 3 + 1] = y; pos[i * 3 + 2] = z;
                min[0] = Math.Min(min[0], x); min[1] = Math.Min(min[1], y); min[2] = Math.Min(min[2], z);
                max[0] = Math.Max(max[0], x); max[1] = Math.Max(max[1], y); max[2] = Math.Max(max[2], z);
            }
            var posBytes = new byte[pos.Length * 4]; Buffer.BlockCopy(pos, 0, posBytes, 0, posBytes.Length);
            // the Y/Z swap mirrors the model, so reverse each triangle to keep the faces pointing outwards
            var idx = new uint[mesh.T.Length];
            for (int t = 0; t < mesh.TriCount; t++) { idx[t * 3] = (uint)mesh.T[t * 3]; idx[t * 3 + 1] = (uint)mesh.T[t * 3 + 2]; idx[t * 3 + 2] = (uint)mesh.T[t * 3 + 1]; }
            var idxBytes = new byte[idx.Length * 4]; Buffer.BlockCopy(idx, 0, idxBytes, 0, idxBytes.Length);
            int posView = View(posBytes, 34962), idxView = View(idxBytes, 34963);
            accessors.Add(new { bufferView = posView, componentType = 5126, count = vn, type = "VEC3", min, max });
            accessors.Add(new { bufferView = idxView, componentType = 5125, count = idx.Length, type = "SCALAR" });
            materials.Add(new
            {
                name,
                pbrMetallicRoughness = new { baseColorFactor = new[] { (argb >> 16 & 255) / 255.0, (argb >> 8 & 255) / 255.0, (argb & 255) / 255.0, 1.0 }, metallicFactor = 0.1, roughnessFactor = 0.6 },
                doubleSided = true,
            });
            meshes.Add(new { name, primitives = new[] { new { attributes = new { POSITION = accessors.Count - 2 }, indices = accessors.Count - 1, material = materials.Count - 1, mode = 4 } } });
            nodes.Add(new { name, mesh = meshes.Count - 1 });
        }
        if (nodes.Count == 0) throw new InvalidOperationException("Nothing to export.");
        Pad();
        var doc = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "Prism" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = Enumerable.Range(0, nodes.Count).ToArray() } },
            ["nodes"] = nodes, ["meshes"] = meshes, ["materials"] = materials, ["accessors"] = accessors, ["bufferViews"] = views,
            ["buffers"] = new[] { new { byteLength = (int)bin.Length } },
        };
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc));
        int jsonPad = (4 - json.Length % 4) % 4;
        int total = 12 + 8 + json.Length + jsonPad + 8 + (int)bin.Length;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
        w.Write((uint)(json.Length + jsonPad)); w.Write(0x4E4F534Au); w.Write(json); for (int i = 0; i < jsonPad; i++) w.Write((byte)' ');
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin.ToArray());
    }
}
