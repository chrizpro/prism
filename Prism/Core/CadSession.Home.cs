using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

public sealed partial class CadSession
{
    static readonly double[] IdentityFrame = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    const double HomeYaw = -35, HomePitch = 28;   // the stock home view

    /// <summary>The part's own axes in the world: identity until the user sets a new home. The view cube, the standard views and Home follow it.</summary>
    public double[] HomeFrame { get; private set; } = IdentityFrame;
    public event Action? HomeChanged;

    public bool HomeIsOriginal => IsIdentity(HomeFrame);
    static bool IsIdentity(double[] r) => r.Zip(IdentityFrame, (a, b) => Math.Abs(a - b)).Max() < 1e-9;

    /// <summary>A direction given in the part's axes, as a world direction.</summary>
    public Vec3 FromHome(Vec3 v) => new(
        HomeFrame[0] * v.X + HomeFrame[1] * v.Y + HomeFrame[2] * v.Z,
        HomeFrame[3] * v.X + HomeFrame[4] * v.Y + HomeFrame[5] * v.Z,
        HomeFrame[6] * v.X + HomeFrame[7] * v.Y + HomeFrame[8] * v.Z);

    /// <summary>A world direction as seen in the part's axes.</summary>
    public Vec3 ToHome(Vec3 v) => new(
        HomeFrame[0] * v.X + HomeFrame[3] * v.Y + HomeFrame[6] * v.Z,
        HomeFrame[1] * v.X + HomeFrame[4] * v.Y + HomeFrame[7] * v.Z,
        HomeFrame[2] * v.X + HomeFrame[5] * v.Y + HomeFrame[8] * v.Z);

    /// <summary>The camera angles for a standard view named in the part's axes (yaw and pitch in radians, as for the stock views).</summary>
    public (double yaw, double pitch, double roll) HomeViewAngles(double yaw, double pitch)
    {
        if (HomeIsOriginal) return (yaw, pitch, 0);
        var stock = new Camera { Yaw = yaw, Pitch = pitch };
        var dest = new Camera { Yaw = yaw };
        dest.SetOrientation(FromHome(stock.EyeDirection), FromHome(stock.Up));
        return (dest.Yaw, dest.Pitch, dest.Roll);
    }

    /// <summary>The camera angles for the home view of the current part orientation.</summary>
    public (double yaw, double pitch, double roll) HomeAngles() => HomeViewAngles(HomeYaw * Math.PI / 180, HomePitch * Math.PI / 180);

    /// <summary>Move the main view to the home view.</summary>
    public void GoHome()
    {
        var (y, p, r) = HomeAngles();
        AnimateView(y, p, r);
    }

    /// <summary>Make what the main view shows now the home view, and turn the part's axes (cube, Front, Top...) to match.</summary>
    public void SetHomeToCurrentView()
    {
        var cam = Camera;
        var stock = new Camera { Yaw = HomeYaw * Math.PI / 180, Pitch = HomePitch * Math.PI / 180 };
        // The frame that carries the stock home camera onto the current camera.
        Vec3[] now = [cam.Right, cam.Up, cam.EyeDirection], home = [stock.Right, stock.Up, stock.EyeDirection];
        var r = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double s = 0;
                for (int k = 0; k < 3; k++) s += Comp(now[k], i) * Comp(home[k], j);
                r[3 * i + j] = s;
            }
        ChangeHome(r, "New home orientation set. Home returns to this view.");
    }

    /// <summary>Go back to the stock orientation and look at the model from the stock home view.</summary>
    public void ResetHome()
    {
        ChangeHome(IdentityFrame, "Home is back to the original orientation.");
        GoHome();
    }

    static double Comp(Vec3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    void ChangeHome(double[] frame, string message)
    {
        frame = Snap(frame);
        if (frame.Zip(HomeFrame, (a, b) => Math.Abs(a - b)).Max() < 1e-9) { Say("That is already the home orientation."); return; }
        HomeFrame = frame; Dirty = true;
        HomeChanged?.Invoke(); Invalidate(); Say(message);
    }

    /// <summary>Remove rounding noise so a frame that is really the world axes compares equal to them.</summary>
    static double[] Snap(double[] r) => [.. r.Select(v => Math.Abs(v) < 1e-9 ? 0 : Math.Abs(Math.Abs(v) - 1) < 1e-9 ? Math.Sign(v) : v)];

    void LoadHome(double[]? frame)
    {
        HomeFrame = frame is { Length: 9 } ? frame : IdentityFrame;
        HomeChanged?.Invoke();
    }
}
