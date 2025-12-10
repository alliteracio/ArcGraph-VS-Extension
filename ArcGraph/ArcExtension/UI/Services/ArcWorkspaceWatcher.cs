//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.Application.Analyzis;
using ArcExtension.UI.ViewModels;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace ArcExtension.UI.Services;

[VisualStudioContribution]
public sealed class ArcWorkspaceWatcher : IDisposable
{
    private bool _started;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly IWorkspaceSubscriptionService _subService;
    private readonly ISolutionAnalyzerService _analyzerService;
    private readonly IGraphDtoBuilder _dtoBuilder;
    private readonly IUiThreadHelper _uiHelper;

    public ArcWorkspaceViewModel Data { get; } = new();

    public ArcWorkspaceWatcher(
        VisualStudioExtensibility extensibility)
    {
        _extensibility = extensibility;
        _subService = new WorkspaceSubscriptionService();
        _analyzerService = new SolutionAnalysisService();
        _dtoBuilder = new GraphDtoBuilder();
        _uiHelper = new UiThreadHelper(Data);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started) return;
        _started = true;

        Data.RefreshRequested += OnRefreshRequestedAsync;
        Data.AnalyzeRequested += OnAnalyzeRequestedAsync;

        _subService.FilesChanged += OnFilesChanged;
        _subService.StatusMessageRequested += async (s, ct) => await _uiHelper.SetStatusMessageAsync(s, ct);

        await _subService.SetupSubscriptionsAsync(_extensibility, cancellationToken).ConfigureAwait(false);
    }

    private void OnFilesChanged(object? sender, IReadOnlyList<string> files)
    {
        _ = _uiHelper.RunOnUiAsync(() =>
        {
            Data.Files.Clear();
            foreach (var f in files) Data.Files.Add(f);
        });
    }

    private async Task OnRefreshRequestedAsync(CancellationToken ct)
    {
        await _subService.RefreshFilesAsync(ct).ConfigureAwait(false);
    }

    private async Task OnAnalyzeRequestedAsync(CancellationToken ct)
    {
        var solutions = await _extensibility.Workspaces()
            .QuerySolutionAsync(s => s.With(s => s.Path), ct).ConfigureAwait(false);

        var solutionPath = solutions.FirstOrDefault().Path;
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            await _uiHelper.SetStatusMessageAsync("Nincs elérhető solution path az elemzéshez.", ct).ConfigureAwait(false);
            return;
        }

        await _uiHelper.SetStatusMessageAsync($"Solution elemzése folyamatban...\n{solutionPath}", ct).ConfigureAwait(false);

        try
        {
            var result = await _analyzerService.AnalyzeSolutionAsync(solutionPath, ct).ConfigureAwait(false);
            Data.SolutionPath = solutionPath;

            await _uiHelper.RunOnUiAsync(() =>
            {
                Data.DependencyGraph = result.Graph;
                Data.VulnerablePackages.Clear();
                foreach (var vp in result.VulnerablePackages) Data.VulnerablePackages.Add(vp);
                Data.StatusMessage = result.StatusMessage;
            }, ct).ConfigureAwait(false);

            var graphJson = _dtoBuilder.BuildGraphJson(result.Graph, result.CycleNodeIds, result.Clusters);
            Data.UpdateGraphAsync(graphJson);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _uiHelper.SetStatusMessageAsync($"Elemzési hiba: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        try { Data.Dispose(); } catch { }
        _subService.Dispose();
    }
}