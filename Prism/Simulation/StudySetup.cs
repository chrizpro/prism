using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Simulation;

public sealed record NamedFaceSet(string Name, Guid BodyId, int[] Faces, string ShapeSignature);
public sealed record SavedCaeCase(string Name, Guid BodyId, CaeSettings Settings, string ShapeSignature);

public static class StudySetup
{
    public static string Signature(SceneObject body)
    {
        // A shape built in this session and the same shape read back from a saved file can differ in unimportant B-rep details (tolerances, cache flags).
        // Hash the form that a save and reopen produces, which is stable, so a study saved with a document still matches its body when the file is reopened.
        string brep;
        using(var settled=Occ.FromBrep(body.Shape.ToBrep()))brep=settled.ToBrep();
        // OCCT's topology section contains Free/Modified/Checked cache flags.
        // Meshing changes Checked without changing geometry. Ignore only those
        // three flags, retaining orientation/closure and all geometric data.
        int topology=brep.IndexOf("\nTShapes ",StringComparison.Ordinal);
        if(topology<0)throw new InvalidOperationException("Unsupported B-rep format for saved-reference validation.");
        brep=brep[..topology]+Regex.Replace(brep[topology..],@"(?m)^([01]{3})([01]{4})(\r?)$","000$2$3");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(brep)));
    }
    public static void Validate(SceneObject body, string? signature, int[] faces)
    {
        if(signature!=null && signature!=Signature(body)) throw new InvalidOperationException("The body geometry changed. Reselect the boundary faces and create a new setup.");
        int count=body.Shape.Count(SubKind.Face);
        if(faces.Any(i=>i<0||i>=count))throw new InvalidOperationException("A boundary face no longer exists. Reselect it.");
    }
}
