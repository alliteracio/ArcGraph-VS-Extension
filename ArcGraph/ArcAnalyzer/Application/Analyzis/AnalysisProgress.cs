//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcAnalyzer.Application.Analyzis;

/// <summary>
/// Simple progress report used during solution analysis.
/// </summary>
public sealed class AnalysisProgress
{
    public int ProjectsProcessed { get; init; }
    public int TotalProjects { get; init; }
    public string CurrentProject { get; init; } = string.Empty;
    public int NodesFound { get; init; }
    public int EdgesFound { get; init; }
    public override string ToString()
        => $"Projects {ProjectsProcessed}/{TotalProjects}, Project='{CurrentProject}', Nodes={NodesFound}, Edges={EdgesFound}";
}