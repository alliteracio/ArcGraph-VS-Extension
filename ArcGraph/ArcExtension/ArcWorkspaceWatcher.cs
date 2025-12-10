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
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace ArcExtension;

[VisualStudioContribution]
public sealed class ArcWorkspaceWatcher : IObserver<IQueryResults<ISolutionSnapshot>>, IDisposable
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
        if (_started) return;
        _started = true;

        Data.RefreshRequested += OnRefreshRequestedAsync;
        Data.AnalyzeRequested += OnAnalyzeRequestedAsync;

        await SetupSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AnalyzeSolutionAsync(CancellationToken cancellationToken)
    {
        var solutionPath = Data.SolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            await SetStatusMessageAsync("Nincs elérhető solution path az elemzéshez.", cancellationToken).ConfigureAwait(false);
            return;
        }

        await SetStatusMessageAsync($"Solution elemzése folyamatban...\n{solutionPath}", cancellationToken).ConfigureAwait(false);

        try
        {
            var analyzer = new SolutionDependencyAnalyzer();

            IVulnerabilityChecker? checker = null;
            try
            {
                var ossUser = Environment.GetEnvironmentVariable("OSS_INDEX_USER");
                var ossToken = Environment.GetEnvironmentVariable("OSS_INDEX_TOKEN");
                if (!string.IsNullOrEmpty(ossUser) && !string.IsNullOrEmpty(ossToken))
                {
                    checker = new OssIndexVulnerabilityChecker(ossUser, ossToken);
                }
            }
            catch
            {
                checker = null;
            }

            checker ??= new MockVulnerabilityChecker();

            var graph = await analyzer.AnalyzeSolutionAsync(solutionPath, checker, cancellationToken).ConfigureAwait(false);
          
            LayerConfig cfg;

            var configPath = Path.Combine(Path.GetDirectoryName(solutionPath) ?? "", "ArcCore\\Layering\\layer.config.json");
            System.Diagnostics.Debug.WriteLine($"[ArcWorkspaceWatcher] solutionPath={solutionPath}");
            System.Diagnostics.Debug.WriteLine($"[ArcWorkspaceWatcher] looking for layer config at: {configPath}");
            System.Diagnostics.Debug.WriteLine($"[ArcWorkspaceWatcher] layer config exists: {File.Exists(configPath)}");

            if (File.Exists(configPath))
                cfg = LayerConfigLoader.LoadFromFile(configPath);
            else
                cfg = new LayerConfig();
            try
            {
                var cfgText = File.Exists(configPath) ? File.ReadAllText(configPath) : "<none>";
                System.Diagnostics.Debug.WriteLine($"[ArcWorkspaceWatcher] loaded layer.config.json content:\n{cfgText}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArcWorkspaceWatcher] failed to read config content: {ex}");
            }

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

            var cycleNodeIds = CycleDetector.FindCycleNodeIds(graph);

            var vulnerablePackages = graph.Nodes.Values
                .Where(n => !string.IsNullOrEmpty(n.PackageId) && n.IsVulnerable)
                .Select(n => $"{n.PackageId} {n.PackageVersion}".Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await RunOnUiAsync(() =>
            {
                Data.DependencyGraph = graph;
                Data.VulnerablePackages.Clear();
                foreach (var vp in vulnerablePackages)
                    Data.VulnerablePackages.Add(vp);

                Data.StatusMessage = $"Elemzés kész. Nodes: {graph.Nodes.Count}, Edges: {graph.Edges.Count}. Violations: {graph.Edges.Count(e => e.IsViolation)}";
            }, cancellationToken).ConfigureAwait(false);

            var nodeList = graph.Nodes.Select(kv => new GraphLayoutHelper.Node { Id = kv.Key }).ToList();
            var edgeList = graph.Edges.Select(e => new GraphLayoutHelper.Edge { Source = e.SourceId, Target = e.TargetId }).ToList();

            GraphLayoutHelper.ComputeLayout(nodeList, edgeList, width: 1200, height: 800, iterations: 400);

            var degree = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in nodeList) degree[n.Id] = 0;
            foreach (var e in edgeList)
            {
                if (degree.ContainsKey(e.Source)) degree[e.Source]++;
                if (degree.ContainsKey(e.Target)) degree[e.Target]++;
            }

            var nodeVectors = nodeList.Select(n =>
            {
                graph.Nodes.TryGetValue(n.Id, out var gn);
                return new Clusterer.NodeVector
                {
                    Id = n.Id,
                    Name = gn?.Name ?? (n.Id.Contains('.') ? n.Id.Split('.').Last() : n.Id),
                    Namespace = gn?.Namespace ?? string.Empty,
                    MethodCount = gn?.MethodCount ?? 0,
                    PropertyCount = gn?.PropertyCount ?? 0,
                    FieldCount = gn?.FieldCount ?? 0,
                    Degree = degree.TryGetValue(n.Id, out var d) ? d : 0,
                    IsExternal = (gn?.IsExternal ?? false) ? 1f : 0f
                };
            }).ToList();

            int recommendedK = Math.Max(2, Math.Min(12, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, nodeVectors.Count)))));
            Dictionary<string, int> clusters = new();
            try
            {
                clusters = Clusterer.AssignClusters(nodeVectors, kClusters: recommendedK);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ArcWorkspaceWatcher] Clustering failed: " + ex);
                clusters = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            var palette = new[]
            {
                "#8dd3c7","#ffffb3","#bebada","#fb8072","#80b1d3","#fdb462","#b3de69","#fccde5",
                "#d9d9d9","#bc80bd","#ccebc5","#ffed6f"
            };

            string clusterColor(int c)
            {
                if (c <= 0) return colorForGroupString("");
                return palette[(c - 1) % palette.Length];
            }

            string colorForGroupString(string g)
            {
                switch ((g ?? "").ToLowerInvariant())
                {
                    case "ui": return "#1f77b4";
                    case "application": return "#2ca02c";
                    case "domain": return "#ff7f0e";
                    case "infrastructure": return "#9467bd";
                    default: return "#67a9cf";
                }
            }

            var cycleLookup = cycleNodeIds ?? new HashSet<string>(StringComparer.Ordinal);

            var dto = new
            {
                nodes = nodeList.Select(n =>
                {
                    graph.Nodes.TryGetValue(n.Id, out var gn);
                    var vulns = gn?.Vulnerabilities?.Select(v => new
                    {
                        id = v.Id,
                        title = v.Title,
                        description = v.Description,
                        severity = v.Severity,
                        affectedVersions = v.AffectedVersions
                    }).ToArray() ?? Array.Empty<object>();

                    var clusterId = clusters.TryGetValue(n.Id, out var c) ? c : 0;
                    var bgColor = clusterId > 0 ? clusterColor(clusterId) : colorForGroupString(gn?.Layer.ToString());

                    return new
                    {
                        id = n.Id,
                        label = gn?.Name ?? n.Id,
                        group = gn != null ? gn.Layer.ToString() : string.Empty,
                        cluster = clusterId,
                        backgroundColor = bgColor,
                        x = Math.Round(n.X, 2),
                        y = Math.Round(n.Y, 2),
                        isVulnerable = gn?.IsVulnerable ?? false,
                        isExternal = gn?.IsExternal ?? false,
                        isInCycle = cycleLookup.Contains(n.Id),
                        packageId = gn?.PackageId ?? string.Empty,
                        packageVersion = gn?.PackageVersion ?? string.Empty,
                        methodCount = gn?.MethodCount ?? 0,
                        propertyCount = gn?.PropertyCount ?? 0,
                        fieldCount = gn?.FieldCount ?? 0,
                        sourceFiles = gn?.SourceFilePaths ?? new List<string>(),
                        vulnerabilities = vulns,
                        degree = degree.TryGetValue(n.Id, out var d) ? d : 0
                    };
                }).ToArray(),
                edges = edgeList.Select(e =>
                {
                    var ge = graph.Edges.FirstOrDefault(x => x.SourceId == e.Source && x.TargetId == e.Target);
                    var edgeInCycle = cycleLookup.Contains(e.Source) && cycleLookup.Contains(e.Target);
                    return new
                    {
                        source = e.Source,
                        target = e.Target,
                        weight = ge?.Weight ?? 1,
                        isViolation = ge?.IsViolation ?? false,
                        isInCycle = edgeInCycle,
                        kind = ge?.Kind.ToString() ?? string.Empty
                    };
                }).ToArray()
            };

            var graphJson = JsonSerializer.Serialize(dto);

            Data.UpdateGraphJson(graphJson);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await SetStatusMessageAsync($"Elemzési hiba: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
        }
    }

    private async Task OnRefreshRequestedAsync(CancellationToken token)
    {
        await SetupSubscriptionsAsync(token).ConfigureAwait(false);
    }

    private async Task OnAnalyzeRequestedAsync(CancellationToken token)
    {
        await AnalyzeSolutionAsync(token).ConfigureAwait(false);
    }

    private async Task SetupSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await SetStatusMessageAsync("Megnyitott solution keresése...", cancellationToken).ConfigureAwait(false);

        var solutions = await _extensibility.Workspaces()
            .QuerySolutionAsync(s => s.With(s => s.Path), cancellationToken).ConfigureAwait(false);

        var singleSolution = solutions.FirstOrDefault();

        if (singleSolution is null)
        {
            await RunOnUiAsync(() =>
            {
                Data.Files.Clear();
                Data.SolutionPath = null;
                Data.StatusMessage = "Nincs megnyitott solution. Nyiss meg egy solutiont, majd kattints a Refresh gombra.";
            }, cancellationToken).ConfigureAwait(false);

            return;
        }

        await RunOnUiAsync(() =>
        {
            Data.SolutionPath = singleSolution.Path;
        }, cancellationToken).ConfigureAwait(false);

        _solutionSubscription?.Dispose();

        _solutionSubscription = await singleSolution
            .AsQueryable()
            .With(p => p.Projects)
            .SubscribeAsync(this, cancellationToken).ConfigureAwait(false);

        await RefreshFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshFilesAsync(CancellationToken cancellationToken)
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _refreshCts.Token;

        await _refreshSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SetStatusMessageAsync("Fájlok lekérése...", ct).ConfigureAwait(false);

            var workspace = _extensibility.Workspaces();

            var files = await workspace.QueryProjectsAsync(
                project => project
                    .Get(p => p.FilesEndingWith(".cs")
                        .With(f => f.Path)),
                ct).ConfigureAwait(false);

            var filePaths = files
                .Where(f => !string.IsNullOrEmpty(f.Path))
                .Select(f => f.Path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await RunOnUiAsync(() =>
            {
                Data.Files.Clear();
                foreach (var path in filePaths) Data.Files.Add(path);

                Data.StatusMessage = filePaths.Count == 0 ? "Nincsenek .cs fájlok a megnyitott projektekben." : string.Empty;
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await SetStatusMessageAsync($"Hiba a fájlok lekérésekor: {ex.Message}").ConfigureAwait(false);
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
        foreach (var sub in _fileSubscriptions) sub.Dispose();
        _fileSubscriptions.Clear();

        _solutionSubscription?.Dispose();
        _solutionSubscription = null;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;

        _refreshSemaphore.Dispose();

        try { Data.Dispose(); } catch { }
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
                await op.Task.ConfigureAwait(false);
                return;
            }

            var sc = System.Threading.SynchronizationContext.Current;
            if (sc != null)
            {
                var tcs = new TaskCompletionSource<object?>();
                sc.Post(_ =>
                {
                    try { action(); tcs.SetResult(null); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }, null);

                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task.ConfigureAwait(false);
                }

                return;
            }

            action();
        }
        catch (OperationCanceledException) { }
    }

    private async Task SetStatusMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        await RunOnUiAsync(() => Data.StatusMessage = message, cancellationToken).ConfigureAwait(false);
    }
}