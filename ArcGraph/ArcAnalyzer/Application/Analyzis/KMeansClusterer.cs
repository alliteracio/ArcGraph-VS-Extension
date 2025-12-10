//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.Domain.GraphModel;
using ArcAnalyzer.Domain.Results;
using Microsoft.ML;

namespace ArcAnalyzer.Application.Analyzis;

public static class KMeansClusterer
{
    public static Dictionary<string, int> AssignClusters(IEnumerable<GraphNode> nodes, int? kClusters = null)
    {
        var nodeList = nodes.ToList();
        var n = Math.Max(1, nodeList.Count);

        if (n == 0) return new Dictionary<string, int>(StringComparer.Ordinal);

        var k = kClusters ?? Math.Max(2, Math.Min(20, (int)Math.Ceiling(Math.Sqrt(n))));
        var ml = new MLContext(seed: 0);

        IDataView data = ml.Data.LoadFromEnumerable(nodeList);

        var pipeline = ml.Transforms.Text.FeaturizeText("NameFeats", nameof(GraphNode.Name))
            .Append(ml.Transforms.Text.FeaturizeText("NsFeats", nameof(GraphNode.Namespace)))
            .Append(ml.Transforms.NormalizeMeanVariance(nameof(GraphNode.MethodCount)))
            .Append(ml.Transforms.NormalizeMeanVariance(nameof(GraphNode.PropertyCount)))
            .Append(ml.Transforms.NormalizeMeanVariance(nameof(GraphNode.FieldCount)))
            .Append(ml.Transforms.NormalizeMeanVariance(nameof(GraphNode.Degree)))
            .Append(ml.Transforms.Concatenate("Features",
                "NameFeats", "NsFeats",
                nameof(GraphNode.MethodCount),
                nameof(GraphNode.PropertyCount),
                nameof(GraphNode.FieldCount),
                nameof(GraphNode.Degree),
                nameof(GraphNode.IsExternal)))
            .Append(ml.Clustering.Trainers.KMeans(featureColumnName: "Features", numberOfClusters: k));

        var model = pipeline.Fit(data);
        var transformed = model.Transform(data);

        var preds = ml.Data.CreateEnumerable<PredictionResult>(transformed, reuseRowObject: false).ToList();

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in preds)
        {
            map[p.Id] = p.PredictedClusterId == 0 ? 0 : (int)p.PredictedClusterId;
        }
        return map;
    }
}