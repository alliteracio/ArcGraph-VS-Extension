using ArcAnalyzer.Domain.GraphModel;

namespace ArcAnalyzer.Domain
{
    public sealed class SolutionAnalysisResult
    {
        public DependencyGraph Graph { get; init; } = new DependencyGraph();
        public IReadOnlyCollection<string> VulnerablePackages { get; init; } = Array.Empty<string>();
        public HashSet<string>? CycleNodeIds { get; init; }
        public Dictionary<string, int> Clusters { get; init; } = new();
        public string StatusMessage { get; init; } = string.Empty;
    }
}
