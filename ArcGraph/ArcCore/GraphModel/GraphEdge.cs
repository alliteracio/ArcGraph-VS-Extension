//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public class GraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Weight { get; set; } = 0;
    public DependencyKind Kind { get; set; } = DependencyKind.Unknown;
    public bool IsViolation { get; set; } = false;
    public override string ToString() => $"{SourceId} -> {TargetId} ({Kind}, w={Weight})";
}
