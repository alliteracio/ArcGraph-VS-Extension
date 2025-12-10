using ArcAnalyzer.Domain;
using ArcAnalyzer.Domain.GraphModel;
using ArcAnalyzer.Domain.Layering;
using ArcAnalyzer.Domain.Rules;
using ArcAnalyzer.Infrastructure.Vulnerabilities;
using ArcAnalyzer.UI;
using System.Diagnostics;

namespace ArcAnalyzer.Application.Analyzis
{
    public class SolutionAnalysisService : ISolutionAnalyzerService
    {
        public async Task<SolutionAnalysisResult> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken)
        {
            var analyzer = new SolutionDependencyAnalyzer();

            IVulnerabilityChecker? checker = null;
            try
            {
                var ossUser = Environment.GetEnvironmentVariable("OSS_INDEX_USER");
                var ossToken = Environment.GetEnvironmentVariable("OSS_INDEX_TOKEN");
                if (!string.IsNullOrEmpty(ossUser) && !string.IsNullOrEmpty(ossToken))
                {
                    checker = new OssIndexVulnerabilityChecker(ossUser, ossToken);
                }
            }
            catch
            {
                checker = null;
            }

            checker ??= new MockVulnerabilityChecker();

            var graph = await analyzer.AnalyzeSolutionAsync(solutionPath, checker, cancellationToken).ConfigureAwait(false);

            // load layer config
            LayerConfig cfg;
            var configPath = Path.Combine(Path.GetDirectoryName(solutionPath) ?? "", "ArcAnalyzer\\Domain\\Layering\\layer.config.json");
            Debug.WriteLine($"[SolutionAnalyzer] looking for layer config at: {configPath}");
            if (File.Exists(configPath))
                cfg = LayerConfigLoader.LoadFromFile(configPath);
            else
                cfg = new LayerConfig();

            var assigner = new LayerAssigner(cfg);
            assigner.AssignLayers(graph);

            var allowed = new List<(Layer, Layer)>
            {
                (Layer.UI, Layer.Application),
                (Layer.Application, Layer.Domain),
                (Layer.Application, Layer.Infrastructure),
                (Layer.Domain, Layer.Infrastructure),
                (Layer.UI, Layer.UI),
                (Layer.Application, Layer.Application),
                (Layer.Domain, Layer.Domain),
                (Layer.Infrastructure, Layer.Infrastructure)
            };
            var rules = new LayerRules(allowed);
            rules.MarkLayerViolations(graph);
            rules.MarkHighDegreeNodes(graph, inDegreeThreshold: 40, outDegreeThreshold: 40);

            var cycleNodeIds = CycleDetector.FindCycleNodeIds(graph);

            var vulnerablePackages = graph.Nodes.Values
                .Where(n => !string.IsNullOrEmpty(n.PackageId) && n.IsVulnerable)
                .Select(n => $"{n.PackageId} {n.PackageVersion}".Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // clustering (simplified copy from original)
            var nodeList = graph.Nodes.Select(kv => new GraphLayoutHelper.Node { Id = kv.Key }).ToList();
            var edgeList = graph.Edges.Select(e => new GraphLayoutHelper.Edge { Source = e.SourceId, Target = e.TargetId }).ToList();

            GraphLayoutHelper.ComputeLayout(nodeList, edgeList, width: 1200, height: 800, iterations: 400);

            var degree = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in nodeList) degree[n.Id] = 0;
            foreach (var e in edgeList)
            {
                if (degree.ContainsKey(e.Source)) degree[e.Source]++;
                if (degree.ContainsKey(e.Target)) degree[e.Target]++;
            }

            var nodeVectors = nodeList.Select(n =>
            {
                graph.Nodes.TryGetValue(n.Id, out var gn);
                return new Clusterer.NodeVector
                {
                    Id = n.Id,
                    Name = gn?.Name ?? (n.Id.Contains('.') ? n.Id.Split('.').Last() : n.Id),
                    Namespace = gn?.Namespace ?? string.Empty,
                    MethodCount = gn?.MethodCount ?? 0,
                    PropertyCount = gn?.PropertyCount ?? 0,
                    FieldCount = gn?.FieldCount ?? 0,
                    Degree = degree.TryGetValue(n.Id, out var d) ? d : 0,
                    IsExternal = (gn?.IsExternal ?? false) ? 1f : 0f
                };
            }).ToList();

            int recommendedK = Math.Max(2, Math.Min(12, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, nodeVectors.Count)))));
            Dictionary<string, int> clusters = new();
            try
            {
                clusters = Clusterer.AssignClusters(nodeVectors, kClusters: recommendedK);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SolutionAnalyzer] Clustering failed: " + ex);
                clusters = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            return new SolutionAnalysisResult
            {
                Graph = graph,
                VulnerablePackages = vulnerablePackages,
                CycleNodeIds = cycleNodeIds,
                Clusters = clusters,
                StatusMessage = $"Elemzés kész. Nodes: {graph.Nodes.Count}, Edges: {graph.Edges.Count}. Violations: {graph.Edges.Count(e => e.IsViolation)}"
            };
        }
    }
     
}
