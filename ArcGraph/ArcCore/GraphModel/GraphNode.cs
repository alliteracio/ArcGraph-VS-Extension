//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcCore.GraphModel;

public sealed class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public bool IsExternal { get; set; } = false;
    public string TypeKind { get; set; } = string.Empty;
    public string Accessibility { get; set; } = string.Empty;
    public int GenericArity { get; set; } = 0;
    public List<string> ImplementedInterfaces { get; set; } = new();
    public List<string> BaseTypes { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<string> SourceFilePaths { get; set; } = new();
    public int MethodCount { get; set; } = 0;
    public int PropertyCount { get; set; } = 0;
    public int FieldCount { get; set; } = 0;
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }

    public override string ToString() => Id;
    public Layer Layer { get; set; } = Layer.Unknown;
}