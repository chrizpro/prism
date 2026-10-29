namespace Prism.Commands;

/// <summary>Compatibility entry point: spatial and planar sketching use the same editable sketch document.</summary>
public sealed class Sketch3DCommand : CreateSketchCommand
{
    public Sketch3DCommand() : base(spatial: true) { }
}
