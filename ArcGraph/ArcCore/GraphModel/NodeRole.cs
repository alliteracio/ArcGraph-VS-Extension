//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public class NodeRole
{
    public bool IsController { get; set; }
    public bool IsService { get; set; }
    public bool IsRepository { get; set; }
    public bool IsDto { get; set; }
}
