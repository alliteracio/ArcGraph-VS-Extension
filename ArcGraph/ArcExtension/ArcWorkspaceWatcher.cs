//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.Analysis;
using ArcCore.GraphModel;
using ArcCore.Layering;
using ArcCore.Rules;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;
using System.Windows; 
using System.Windows.Threading; 

namespace ArcExtension;

[VisualStudioContribution]
public sealed class ArcWorkspaceWatcher :
    IObserver<IQueryResults<ISolutionSnapshot>>, IDisposable
{
    private readonly VisualStudioExtensibility _extensibility;
    private readonly List<IDisposable> _fileSubscriptions = new();
    private IDisposable? _solutionSubscription;
    private bool _started;

    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private CancellationTokenSource? _refreshCts;

    public ArcWorkspaceViewModel Data { get; } = new();

    public ArcWorkspaceWatcher(VisualStudioExtensibility extensibility) => _extensibility = extensibility;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
            return;

        _started = true;
        Data.RefreshRequested += OnRefreshRequestedAsync;
        Data.AnalyzeRequested += OnAnalyzeRequestedAsync;
        await SetupSubscriptionsAsync(cancellationToken);
    }

    public async Task AnalyzeSolutionAsync(CancellationToken cancellationToken)
    {
        var solutionPath = Data.SolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            await SetStatusMessageAsync("Nincs elérhető solution path az elemzéshez.", cancellationToken);
            return;
        }

            await SetStatusMessageAsync($"Solution elemzése folyamatban...\n{solutionPath}", cancellationToken);

        try
        {
            var analyzer = new SolutionDependencyAnalyzer();
            var graph = await analyzer.AnalyzeSolutionAsync(solutionPath, cancellationToken);

            LayerConfig cfg;
            var configPath = Path.Combine(Path.GetDirectoryName(solutionPath) ?? "", "layer.config.json");
            if (File.Exists(configPath))
                cfg = LayerConfigLoader.LoadFromFile(configPath);
            else
                cfg = new LayerConfig();

            var assigner = new LayerAssigner(cfg);
            assigner.AssignLayers(graph);

            var allowed = new List<(Layer, Layer)>
        {
            (Layer.UI, Layer.Application),
            (Layer.Application, Layer.Domain),
            (Layer.Application, Layer.Infrastructure),
            (Layer.Domain, Layer.Infrastructure),
            (Layer.UI, Layer.UI),
            (Layer.Application, Layer.Application),
            (Layer.Domain, Layer.Domain),
            (Layer.Infrastructure, Layer.Infrastructure)
        };

            var rules = new LayerRules(allowed);
            rules.MarkLayerViolations(graph);
            rules.MarkHighDegreeNodes(graph, inDegreeThreshold: 40, outDegreeThreshold: 40);

            await RunOnUiAsync(() =>
            {
                Data.StatusMessage = $"Elemzés kész. Nodes: {graph.Nodes.Count}, Edges: {graph.Edges.Count}. Violations: {graph.Edges.Count(e => e.IsViolation)}";
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await SetStatusMessageAsync($"Elemzési hiba: {ex.GetType().Name}: {ex.Message}", cancellationToken);
        }
    }

    private async Task OnRefreshRequestedAsync(CancellationToken token)
    {
        await SetupSubscriptionsAsync(token);
    }
    private async Task OnAnalyzeRequestedAsync(CancellationToken token)
    {
        await AnalyzeSolutionAsync(token);
    }

    private async Task SetupSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await SetStatusMessageAsync("Megnyitott solution keresése...", cancellationToken);

        var solutions = await _extensibility.Workspaces()
            .QuerySolutionAsync(solution => solution
            .With(solution => solution.Path), cancellationToken);

        var singleSolution = solutions.FirstOrDefault();

        if (singleSolution is null)
        {
            await RunOnUiAsync(() =>
            {
                Data.Files.Clear();
                Data.SolutionPath = null;
                Data.StatusMessage = "Nincs megnyitott solution. Nyiss meg egy solutiont, majd kattints a Refresh gombra.";
            }, cancellationToken);

            return;
        }

        await RunOnUiAsync(() =>
        {
            Data.SolutionPath = singleSolution.Path;
        }, cancellationToken);

        _solutionSubscription?.Dispose();

        _solutionSubscription = await singleSolution
            .AsQueryable()
            .With(p => p.Projects)
            .SubscribeAsync(this, cancellationToken);

        await RefreshFilesAsync(cancellationToken);
    }

    private async Task RefreshFilesAsync(CancellationToken cancellationToken)
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _refreshCts.Token;

        await _refreshSemaphore.WaitAsync(ct);
        try
        {
            await SetStatusMessageAsync("Fájlok lekérése...", ct);

            var workspace = _extensibility.Workspaces();

            var files = await workspace.QueryProjectsAsync(
                project => project
                    .Get(p => p.FilesEndingWith(".cs")
                        .With(f => f.Path)),
                ct);

            var filePaths = files
                .Where(f => !string.IsNullOrEmpty(f.Path))
                .Select(f => f.Path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await RunOnUiAsync(() =>
            {
                Data.Files.Clear();
                foreach (var path in filePaths)
                {
                    Data.Files.Add(path);
                }

                if (filePaths.Count == 0)
                {
                    Data.StatusMessage = "Nincsenek .cs fájlok a megnyitott projektekben.";
                }
                else
                {
                    Data.StatusMessage = string.Empty;
                }
            }, ct);
        }
        catch (OperationCanceledException){}
        catch (Exception ex)
        {
            await SetStatusMessageAsync($"Hiba a fájlok lekérésekor: {ex.Message}");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    public void OnNext(IQueryResults<ISolutionSnapshot> value)
    {
        _ = RefreshFilesAsync(CancellationToken.None);
    }

    public void OnError(Exception error)
    {
        _ = SetStatusMessageAsync($"Subscription error: {error.Message}");
    }

    public void OnCompleted()
    {
        _ = SetupSubscriptionsAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        foreach (var sub in _fileSubscriptions)
            sub.Dispose();

        _fileSubscriptions.Clear();

        _solutionSubscription?.Dispose();
        _solutionSubscription = null;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;

        _refreshSemaphore.Dispose();
    }

    private static async Task RunOnUiAsync(Action action, CancellationToken cancellationToken = default)
    {
        try
        {
            var app = Application.Current;
            if (app?.Dispatcher != null)
            {
                if (app.Dispatcher.CheckAccess())
                {
                    action();
                    return;
                }

                var op = app.Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
                await op.Task;
                return;
            }

            var sc = SynchronizationContext.Current;
            if (sc != null)
            {
                var tcs = new TaskCompletionSource<object?>();
                sc.Post(_ =>
                {
                    try
                    {
                        action();
                        tcs.SetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, null);

                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task;
                }

                return;
            }

            action();
        }
        catch (OperationCanceledException) { }
    }

    private async Task SetStatusMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        await RunOnUiAsync(() =>
        {
            Data.StatusMessage = message;
        }, cancellationToken);
    }
}