//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public class GraphNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public Layer Layer { get; set; } = Layer.Unknown;
    public NodeRole Role { get; set; } = new();
}
