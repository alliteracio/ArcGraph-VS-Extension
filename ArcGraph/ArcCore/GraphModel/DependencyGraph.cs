//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public class DependencyGraph
{
    public Dictionary<string, GraphNode> Nodes { get; } = new(StringComparer.Ordinal);
    public List<GraphEdge> Edges { get; } = new();
}
