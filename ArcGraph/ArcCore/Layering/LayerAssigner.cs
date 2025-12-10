//  Diploma Thesis 2025
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
       
            System.Diagnostics.Debug.WriteLine($"[LayerConfig] loaded rule: Layer={layer} Pattern={regex.ToString()}");
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

    private static string NormalizeNamespaceLocal(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        raw = raw.Replace("global::", "");
        raw = raw.Replace('+', '.');
        return raw.Trim();
    }

    public void AssignLayers(DependencyGraph graph)
    {
        foreach (var node in graph.Nodes.Values)
        {
            if (node.IsExternal)
            {
                System.Diagnostics.Debug.WriteLine($"[LayerAssign] Skipping external Node={node.Id} Namespace='{node.Namespace}'");
                continue;
            }

            var ns = NormalizeNamespaceLocal(node.Namespace);
            bool matched = false;

            System.Diagnostics.Debug.WriteLine($"[LayerAssign] Node={node.Id} NamespaceRaw='{node.Namespace}' NamespaceNorm='{ns}'");

            if (!string.IsNullOrEmpty(ns))
            {
                foreach (var (layer, regex) in _rules)
                {
                    bool isMatch = false;
                    try
                    {
                        isMatch = regex.IsMatch(ns);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LayerAssign] Regex error for pattern {regex?.ToString()}: {ex}");
                    }

                    System.Diagnostics.Debug.WriteLine($"[LayerAssign]   Testing rule Layer={layer} Pattern={regex} => match={isMatch}");

                    if (isMatch)
                    {
                        node.Layer = layer;
                        matched = true;
                        System.Diagnostics.Debug.WriteLine($"[LayerAssign]   MATCHED -> Node={node.Id} assigned to Layer={layer}");
                        break;
                    }
                }
            }

            if (!matched && node.SourceFilePaths != null)
            {
                foreach (var p in node.SourceFilePaths)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    var path = p.Replace('\\', '/').ToLowerInvariant();
                    if (path.Contains("/domain/"))
                    {
                        node.Layer = Layer.Domain;
                        matched = true;
                        System.Diagnostics.Debug.WriteLine($"[LayerAssign]   FALLBACK PATH MATCH -> Node={node.Id} path contains /domain/ -> Layer=Domain");
                        break;
                    }
                    if (path.Contains("/ui/"))
                    {
                        node.Layer = Layer.UI;
                        matched = true;
                        System.Diagnostics.Debug.WriteLine($"[LayerAssign]   FALLBACK PATH MATCH -> Node={node.Id} path contains /ui/ -> Layer=UI");
                        break;
                    }
                    if (path.Contains("/application/") || path.Contains("/services/"))
                    {
                        node.Layer = Layer.Application;
                        matched = true;
                        System.Diagnostics.Debug.WriteLine($"[LayerAssign]   FALLBACK PATH MATCH -> Node={node.Id} path contains /application or /services -> Layer=Application");
                        break;
                    }
                    if (path.Contains("/infrastructure/") || path.Contains("/data/"))
                    {
                        node.Layer = Layer.Infrastructure;
                        matched = true;
                        System.Diagnostics.Debug.WriteLine($"[LayerAssign]   FALLBACK PATH MATCH -> Node={node.Id} path contains /infrastructure or /data -> Layer=Infrastructure");
                        break;
                    }
                }
            }

            if (!matched)
            {
                System.Diagnostics.Debug.WriteLine($"[LayerAssign] No rule for Node={node.Id} Namespace='{ns}'");
            }
        }
    }
}