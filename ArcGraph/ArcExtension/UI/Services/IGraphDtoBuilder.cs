//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.Domain.GraphModel;

namespace ArcExtension.UI.Services;

public interface IGraphDtoBuilder
{
    string BuildGraphJson(DependencyGraph graph, HashSet<string>? cycleNodeIds, Dictionary<string, int> clusters);
}
