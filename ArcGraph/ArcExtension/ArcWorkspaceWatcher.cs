//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.Analysis;
using ArcCore.GraphModel;
using ArcCore.Layering;
using ArcCore.Rules;
using ArcCore.Visualisation;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;
using System.Text.Json;
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
            
            var ossUser = Environment.GetEnvironmentVariable("OSS_INDEX_USER");
            var ossToken = Environment.GetEnvironmentVariable("OSS_INDEX_TOKEN");

            using var checker = new OssIndexVulnerabilityChecker(username: ossUser, token: ossToken);
            var graph = await analyzer.AnalyzeSolutionAsync(solutionPath, checker, cancellationToken);

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
                Data.DependencyGraph = graph;
                Data.StatusMessage = $"Elemzés kész. Nodes: {graph.Nodes.Count}, Edges: {graph.Edges.Count}. Violations: {graph.Edges.Count(e => e.IsViolation)}";
            }, cancellationToken);

            var vulnerablePackages = graph.Nodes.Values
                .Where(n => !string.IsNullOrEmpty(n.PackageId) && n.IsVulnerable)
                .Select(n => $"{n.PackageId} {n.PackageVersion}".Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await RunOnUiAsync(() =>
            {
                Data.VulnerablePackages.Clear();
                foreach (var vp in vulnerablePackages)
                    Data.VulnerablePackages.Add(vp);
            }, cancellationToken);

            var nodeList = graph.Nodes.Select(n => new GraphLayoutHelper.Node
            {
                Id = n.Value.Id
            }).ToList();

            var edgeList = graph.Edges.Select(e => new GraphLayoutHelper.Edge
            {
                Source = e.SourceId,
                Target = e.TargetId
            }).ToList();

            GraphLayoutHelper.ComputeLayout(nodeList, edgeList, width: 1200, height: 800, iterations: 400);

            var dto = new
            {
                nodes = nodeList.Select(n =>
                {
                    graph.Nodes.TryGetValue(n.Id, out var gn);
                    var vulns = gn?.Vulnerabilities?.Select(v => new {
                        id = v.Id,
                        title = v.Title,
                        description = v.Description,
                        severity = v.Severity,
                        affectedVersions = v.AffectedVersions
                    }).ToArray() ?? Array.Empty<object>();

                    return new
                    {
                        id = n.Id,
                        label = gn?.Name ?? n.Id,
                        group = gn != null ? gn.Layer.ToString() : string.Empty,
                        x = Math.Round(n.X, 2),
                        y = Math.Round(n.Y, 2),
                        isVulnerable = gn?.IsVulnerable ?? false,
                        packageId = gn?.PackageId ?? string.Empty,
                        packageVersion = gn?.PackageVersion ?? string.Empty,
                        methodCount = gn?.MethodCount ?? 0,
                        propertyCount = gn?.PropertyCount ?? 0,
                        fieldCount = gn?.FieldCount ?? 0,
                        sourceFiles = gn?.SourceFilePaths ?? new List<string>(),
                        vulnerabilities = vulns
                    };
                }).ToArray(),
                edges = edgeList.Select(e =>
                {
                    var ge = graph.Edges.FirstOrDefault(x => x.SourceId == e.Source && x.TargetId == e.Target);
                    return new
                    {
                        source = e.Source,
                        target = e.Target,
                        weight = ge?.Weight ?? 1,
                        isViolation = ge?.IsViolation ?? false,
                        kind = ge?.Kind.ToString() ?? string.Empty
                    };
                }).ToArray()
            };

            var graphJson = JsonSerializer.Serialize(dto);

            Data.UpdateGraphJson(graphJson);
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
        catch (OperationCanceledException) { }
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
        _ = SetStatusMessageAsync($"Feliratkozás error: {error.Message}");
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

        try
        {
            Data.Dispose();
        }
        catch { }
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

            var sc = System.Threading.SynchronizationContext.Current;
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