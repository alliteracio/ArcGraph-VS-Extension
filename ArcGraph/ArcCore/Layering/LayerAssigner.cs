//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.GraphModel;
using System.Text.RegularExpressions;

namespace ArcCore.Layering;

public class LayerAssigner
{
    private readonly List<(Layer Layer, Regex Pattern)> _rules = new();

    public LayerAssigner(LayerConfig config)
    {
        foreach (var r in config.Layers)
        {
            var layer = MapNameToLayer(r.Name);
            var regex = new Regex(r.NamespacePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _rules.Add((layer, regex));
        }
    }

    private Layer MapNameToLayer(string name) => name?.ToLowerInvariant() switch
    {
        "ui" => Layer.UI,
        "application" => Layer.Application,
        "domain" => Layer.Domain,
        "infrastructure" => Layer.Infrastructure,
        _ => Layer.Unknown
    };

    public void AssignLayers(DependencyGraph graph)
    {
        foreach (var node in graph.Nodes.Values)
        {
            foreach (var (layer, regex) in _rules)
            {
                if (!string.IsNullOrEmpty(node.Namespace) && regex.IsMatch(node.Namespace))
                {
                    node.Layer = layer;
                    break;
                }
            }
        }
    }
}
