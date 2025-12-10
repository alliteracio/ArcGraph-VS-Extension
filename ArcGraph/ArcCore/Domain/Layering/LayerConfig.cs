//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using System.Text.Json.Serialization;

namespace ArcCore.Domain.Layering;

public class LayerConfig
{
    [JsonPropertyName("layers")]
    public List<LayerRuleConfig> Layers { get; set; } = new();
}
