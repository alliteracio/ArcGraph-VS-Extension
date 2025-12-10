//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.Domain.GraphModel;
using ArcAnalyzer.UI;
using System.Text.Json;

namespace ArcExtension.UI.Services;

public sealed class GraphDtoBuilder : IGraphDtoBuilder
{
    public string BuildGraphJson(DependencyGraph graph, HashSet<string>? cycleNodeIds, Dictionary<string, int> clusters)
    {
        var nodeList = graph.Nodes.Select(kv => new GraphNode { Id = kv.Key }).ToList();
        var edgeList = graph.Edges.Select(e => new GraphEdge { SourceId = e.SourceId, TargetId = e.TargetId }).ToList();

        
        GraphLayoutHelper.ComputeLayout(nodeList, edgeList, width: 1400, height: 1000, iterations: 600);

        
        SpreadNodes(nodeList, minDistance: 40.0, maxIterations: 200);

        var palette = new[]
        {
            "#8dd3c7","#ffffb3","#bebada","#fb8072","#80b1d3","#fdb462","#b3de69","#fccde5",
            "#d9d9d9","#bc80bd","#ccebc5","#ffed6f"
        };

        string ClusterColor(int c)
        {
            if (c <= 0) return ColorForGroupString("");
            return palette[(c - 1) % palette.Length];
        }

        string ColorForGroupString(string g)
        {
            switch ((g ?? "").ToLowerInvariant())
            {
                case "ui": return "#1f77b4";
                case "application": return "#2ca02c";
                case "domain": return "#ff7f0e";
                case "infrastructure": return "#9467bd";
                default: return "#67a9cf";
            }
        }

        var cycleLookup = cycleNodeIds ?? new HashSet<string>(StringComparer.Ordinal);
      
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodeList) degree[node.Id] = 0;
        foreach (var edge in edgeList)
        {
            if (degree.ContainsKey(edge.SourceId)) degree[edge.SourceId]++;
            if (degree.ContainsKey(edge.TargetId)) degree[edge.TargetId]++;
        }

        var dto = new
        {
            nodes = nodeList.Select(n =>
            {
                graph.Nodes.TryGetValue(n.Id, out var gn);
                var vulns = gn?.Vulnerabilities?.Select(v => new
                {
                    id = v.Id,
                    title = v.Title,
                    description = v.Description,
                    severity = v.Severity,
                    affectedVersions = v.AffectedVersions
                }).ToArray() ?? Array.Empty<object>();

                var clusterId = clusters.TryGetValue(n.Id, out var c) ? c : 0;
                var bgColor = clusterId > 0 ? ClusterColor(clusterId) : ColorForGroupString(gn?.Layer.ToString());

                return new
                {
                    id = n.Id,
                    label = gn?.Name ?? n.Id,
                    group = gn != null ? gn.Layer.ToString() : string.Empty,
                    cluster = clusterId,
                    backgroundColor = bgColor,
                    x = Math.Round(n.X, 2),
                    y = Math.Round(n.Y, 2),
                    isVulnerable = gn?.IsVulnerable ?? false,
                    isExternal = gn?.IsExternal ?? false,
                    isInCycle = cycleLookup.Contains(n.Id),
                    packageId = gn?.PackageId ?? string.Empty,
                    packageVersion = gn?.PackageVersion ?? string.Empty,
                    methodCount = gn?.MethodCount ?? 0,
                    propertyCount = gn?.PropertyCount ?? 0,
                    fieldCount = gn?.FieldCount ?? 0,
                    sourceFiles = gn?.SourceFilePaths ?? new List<string>(),
                    vulnerabilities = vulns,
                    degree = degree.TryGetValue(n.Id, out var d) ? d : 0
                };
            }).ToArray(),
            edges = edgeList.Select(e =>
            {
                var ge = graph.Edges.FirstOrDefault(x => x.SourceId == e.SourceId && x.TargetId == e.TargetId);
                var edgeInCycle = cycleLookup.Contains(e.SourceId) && cycleLookup.Contains(e.TargetId);
                return new
                {
                    source = e.SourceId,
                    target = e.TargetId,
                    weight = ge?.Weight ?? 1,
                    isViolation = ge?.IsViolation ?? false,
                    isInCycle = edgeInCycle,
                    kind = ge?.Kind.ToString() ?? string.Empty
                };
            }).ToArray()
        };

        return JsonSerializer.Serialize(dto);
    }

    private static void SpreadNodes(List<GraphNode> nodes, double minDistance = 30.0, int maxIterations = 100)
    {
        if (nodes.Count < 2) return;

        var n = nodes.Count;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool movedAny = false;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var a = nodes[i];
                    var b = nodes[j];

                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double distSq = dx * dx + dy * dy;
                    double minDistSq = minDistance * minDistance;

                    if (distSq < 0.0001)
                    {
                        var angle = (i + j + iter) * 0.6180339887498948;
                        double jitter = minDistance * 0.1;
                        a.X += Math.Cos(angle) * jitter;
                        a.Y += Math.Sin(angle) * jitter;
                        b.X -= Math.Cos(angle) * jitter;
                        b.Y -= Math.Sin(angle) * jitter;
                        movedAny = true;
                        continue;
                    }

                    if (distSq < minDistSq)
                    {
                        double dist = Math.Sqrt(distSq);                      
                        double ux = dx / dist;
                        double uy = dy / dist;
                        double overlap = minDistance - dist;
                        double shift = overlap * 0.5;
                        a.X -= ux * shift;
                        a.Y -= uy * shift;
                        b.X += ux * shift;
                        b.Y += uy * shift;
                        movedAny = true;
                    }
                }
            }

            if (!movedAny) break;
        }
    }
}