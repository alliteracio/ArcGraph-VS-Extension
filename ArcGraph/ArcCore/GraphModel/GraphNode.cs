//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public sealed class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public override string ToString() => Id;
    public Layer Layer { get; set; } = Layer.Unknown;
}
