//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.GraphModel;

namespace ArcCore.Rules;

public class LayerRules
{
    private readonly HashSet<(Layer From, Layer To)> _allowed;

    public LayerRules(IEnumerable<(Layer From, Layer To)> allowedPairs)
    {
        _allowed = new HashSet<(Layer, Layer)>(allowedPairs);
    }

    public void MarkLayerViolations(DependencyGraph graph)
    {
        foreach (var edge in graph.Edges)
        {
            if (!graph.Nodes.TryGetValue(edge.FromNodeId, out var fromNode) ||
                !graph.Nodes.TryGetValue(edge.ToNodeId, out var toNode))
                continue;

            var from = fromNode.Layer;
            var to = toNode.Layer;

            if (from == Layer.Unknown || to == Layer.Unknown)
                continue;

            if (!_allowed.Contains((from, to)))
            {
                edge.IsViolation = true;
            }
        }
    }

    public void MarkHighDegreeNodes(DependencyGraph graph, int inDegreeThreshold = 30, int outDegreeThreshold = 30)
    {
        var inDegrees = new Dictionary<string, int>();
        var outDegrees = new Dictionary<string, int>();

        foreach (var n in graph.Nodes.Keys)
        {
            inDegrees[n] = 0;
            outDegrees[n] = 0;
        }

        foreach (var e in graph.Edges)
        {
            if (outDegrees.ContainsKey(e.FromNodeId)) outDegrees[e.FromNodeId]++;
            if (inDegrees.ContainsKey(e.ToNodeId)) inDegrees[e.ToNodeId]++;
        }

        foreach (var kv in inDegrees)
        {
            if (kv.Value >= inDegreeThreshold)
            {
                foreach (var e in graph.Edges.Where(x => x.ToNodeId == kv.Key))
                    e.IsViolation = true;
            }
        }

        foreach (var kv in outDegrees)
        {
            if (kv.Value >= outDegreeThreshold)
            {
                foreach (var e in graph.Edges.Where(x => x.FromNodeId == kv.Key))
                    e.IsViolation = true;
            }
        }
    }
}
