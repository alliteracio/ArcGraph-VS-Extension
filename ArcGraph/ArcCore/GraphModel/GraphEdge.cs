//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public class GraphEdge
{
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public int Weight { get; set; }
    public bool IsViolation { get; set; }
}
