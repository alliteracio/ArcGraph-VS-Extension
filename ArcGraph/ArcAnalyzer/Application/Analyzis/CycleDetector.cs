using ArcAnalyzer.Domain.GraphModel;

namespace ArcAnalyzer.Application.Analyzis;

public static class CycleDetector
{
    public static HashSet<string> FindCycleNodeIds(DependencyGraph graph)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));

        var nodes = graph.Nodes.Keys.ToList();
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in nodes) adj[id] = new List<string>();

        foreach (var e in graph.Edges)
        {
            if (string.IsNullOrEmpty(e.SourceId) || string.IsNullOrEmpty(e.TargetId)) continue;
            if (!adj.ContainsKey(e.SourceId)) adj[e.SourceId] = new List<string>();
            adj[e.SourceId].Add(e.TargetId);
        }

        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowlink = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new HashSet<string>(StringComparer.Ordinal);

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowlink[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            foreach (var w in adj.TryGetValue(v, out var list) ? list : new List<string>())
            {
                if (!indices.ContainsKey(w))
                {
                    StrongConnect(w);
                    lowlink[v] = Math.Min(lowlink[v], lowlink[w]);
                }
                else if (onStack.Contains(w))
                {
                    lowlink[v] = Math.Min(lowlink[v], indices[w]);
                }
            }

            if (lowlink[v] == indices[v])
            {
                var scc = new List<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    scc.Add(w);
                } while (!string.Equals(w, v, StringComparison.Ordinal));

                if (scc.Count > 1)
                {
                    foreach (var id in scc) result.Add(id);
                }
                else
                {
                    var single = scc[0];
                    if (adj.TryGetValue(single, out var outs) && outs.Contains(single))
                    {
                        result.Add(single);
                    }
                }
            }
        }

        foreach (var n in adj.Keys)
        {
            if (!indices.ContainsKey(n))
                StrongConnect(n);
        }

        return result;
    }
}