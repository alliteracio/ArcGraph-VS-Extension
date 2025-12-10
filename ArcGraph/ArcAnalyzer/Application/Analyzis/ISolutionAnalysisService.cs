using ArcAnalyzer.Domain;

namespace ArcAnalyzer.Application.Analyzis
{
    public interface ISolutionAnalyzerService
    {
        Task<SolutionAnalysisResult> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken);
    }
}
