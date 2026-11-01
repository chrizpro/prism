using System;
using Prism.Kernel;

namespace Prism.Simulation;

/// <summary>A rigid part for motion analysis: mass in kg, centre of mass in the part's own coordinates (mm) and the inertia tensor about that centre in the part's axes (kg·m²).</summary>
public sealed record MotionBody(string Key, string Name, double Mass, Vec3 Com, double[] Inertia);

public static class MotionMass
{
    /// <summary>
    /// Mass, centre of mass and inertia tensor of a closed triangle mesh of uniform density (kg per mm³), summed over signed tetrahedra to the origin.
    /// Positions are mm. A mesh that is not closed gives a meaningless result: check <paramref name="volume"/> before trusting it.
    /// </summary>
    public static MotionBody FromMesh(string key, string name, float[] positions, int[] indices, double densityKgPerMm3, out double volume)
    {
        double v6sum = 0; double sx = 0, sy = 0, sz = 0;
        var c = new double[9];            // ∫ x xᵀ dV (before the 1/120 and sign), summed
        Span<double> p = stackalloc double[9];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            for (int k = 0; k < 3; k++) { int i = indices[t + k] * 3; p[3 * k] = positions[i]; p[3 * k + 1] = positions[i + 1]; p[3 * k + 2] = positions[i + 2]; }
            double det = p[0] * (p[4] * p[8] - p[5] * p[7]) - p[1] * (p[3] * p[8] - p[5] * p[6]) + p[2] * (p[3] * p[7] - p[4] * p[6]);
            v6sum += det;
            sx += det * (p[0] + p[3] + p[6]) / 4; sy += det * (p[1] + p[4] + p[7]) / 4; sz += det * (p[2] + p[5] + p[8]) / 4;
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    double diag = p[a] * p[b] + p[3 + a] * p[3 + b] + p[6 + a] * p[6 + b];
                    double s = p[a] + p[3 + a] + p[6 + a], u = p[b] + p[3 + b] + p[6 + b];
                    c[3 * a + b] += det * (diag + s * u) / 120;
                }
        }
        double sign = v6sum < 0 ? -1 : 1;           // an inside-out mesh still has a positive volume
        volume = sign * v6sum / 6;
        if (volume < 1e-12) return new MotionBody(key, name, 0, Vec3.Zero, new double[9]);
        var com = new Vec3(sign * sx / 6 / volume, sign * sy / 6 / volume, sign * sz / 6 / volume);
        double m = densityKgPerMm3 * volume;
        for (int i = 0; i < 9; i++) c[i] *= sign * densityKgPerMm3;            // second moments about the origin, kg·mm²
        double tr = c[0] + c[4] + c[8];
        var inertia = new double[9];
        for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                inertia[3 * a + b] = (a == b ? tr : 0) - c[3 * a + b];
        // move to the centre of mass (parallel-axis theorem)
        double r2 = com.X * com.X + com.Y * com.Y + com.Z * com.Z; double[] r = [com.X, com.Y, com.Z];
        for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                inertia[3 * a + b] -= m * ((a == b ? r2 : 0) - r[a] * r[b]);
        for (int i = 0; i < 9; i++) inertia[i] *= 1e-6;                          // kg·mm² → kg·m²
        return new MotionBody(key, name, m, com, inertia);
    }
}
