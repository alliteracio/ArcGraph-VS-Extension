//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using System.Text.Json.Serialization;

namespace ArcCore.Domain.Layering;

public class LayerRuleConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("namespacePattern")]
    public string NamespacePattern { get; set; } = ".*";
}
