//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using System.Text.Json;

namespace ArcCore.Domain.Layering;

public static class LayerConfigLoader
{
    public static LayerConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Layer config not found", path);

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<LayerConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? new LayerConfig();
    }
}
