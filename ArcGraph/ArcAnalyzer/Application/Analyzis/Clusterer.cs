using Microsoft.ML;
using Microsoft.ML.Data;

namespace ArcAnalyzer.Application.Analyzis
{
    public static class Clusterer
    {
        public class NodeVector
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Namespace { get; set; } = string.Empty;
            public float MethodCount { get; set; }
            public float PropertyCount { get; set; }
            public float FieldCount { get; set; }
            public float Degree { get; set; }
            public float IsExternal { get; set; } // 0 or 1
        }

        private class PredictionResult
        {
            public string Id { get; set; } = string.Empty;

            [ColumnName("PredictedLabel")]
            public uint PredictedClusterId { get; set; }

            [ColumnName("Score")]
            public float[]? Distances { get; set; }
        }

        public static Dictionary<string, int> AssignClusters(IEnumerable<NodeVector> nodes, int? kClusters = null)
        {
            var nodeList = nodes.ToList();
            var n = Math.Max(1, nodeList.Count);

            if (n == 0) return new Dictionary<string, int>(StringComparer.Ordinal);

            var k = kClusters ?? Math.Max(2, Math.Min(20, (int)Math.Ceiling(Math.Sqrt(n))));
            var ml = new MLContext(seed: 0);

            IDataView data = ml.Data.LoadFromEnumerable(nodeList);

            var pipeline = ml.Transforms.Text.FeaturizeText("NameFeats", nameof(NodeVector.Name))
                .Append(ml.Transforms.Text.FeaturizeText("NsFeats", nameof(NodeVector.Namespace)))
                .Append(ml.Transforms.NormalizeMeanVariance(nameof(NodeVector.MethodCount)))
                .Append(ml.Transforms.NormalizeMeanVariance(nameof(NodeVector.PropertyCount)))
                .Append(ml.Transforms.NormalizeMeanVariance(nameof(NodeVector.FieldCount)))
                .Append(ml.Transforms.NormalizeMeanVariance(nameof(NodeVector.Degree)))
                .Append(ml.Transforms.Concatenate("Features",
                    "NameFeats", "NsFeats",
                    nameof(NodeVector.MethodCount),
                    nameof(NodeVector.PropertyCount),
                    nameof(NodeVector.FieldCount),
                    nameof(NodeVector.Degree),
                    nameof(NodeVector.IsExternal)))
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
}