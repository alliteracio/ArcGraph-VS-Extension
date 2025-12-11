//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using Microsoft.ML.Data;

namespace ArcAnalyzer.Domain.Results
{
    public class PredictionResult
    {
        public string Id { get; set; } = string.Empty;

        [ColumnName("PredictedLabel")]
        public uint PredictedClusterId { get; set; }

        [ColumnName("Score")]
        public float[]? Distances { get; set; }
    }
}
