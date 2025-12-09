//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.GraphModel;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace ArcCore.Analysis;

public class SolutionDependencyAnalyzer
{
    private static bool _msbuildRegistered;

    public async Task<DependencyGraph> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
            throw new ArgumentException("solutionPath is null or empty.", nameof(solutionPath));

        RegisterMSBuildIfNeeded();

        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);

        var assemblyPackageMap = NuGetAssemblyMapper.BuildMappingForSolution(solution);

        var analyzer = new SolutionAnalyzer(solution);
        return await analyzer.AnalyzeAsync(progress: null, assemblyPackageMap: assemblyPackageMap, cancellationToken: cancellationToken);
    }

    private static void RegisterMSBuildIfNeeded()
    {
        if (_msbuildRegistered)
            return;

        MSBuildLocator.RegisterDefaults();
        _msbuildRegistered = true;
    }
}