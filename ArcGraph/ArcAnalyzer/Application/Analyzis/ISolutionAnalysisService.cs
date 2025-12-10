//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.Domain.Results;

namespace ArcAnalyzer.Application.Analyzis
{
    public interface ISolutionAnalyzerService
    {
        Task<SolutionAnalysisResult> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken);
    }
}
