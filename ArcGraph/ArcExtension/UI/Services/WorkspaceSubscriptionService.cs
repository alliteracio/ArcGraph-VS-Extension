//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace ArcExtension.UI.Services;

public sealed class WorkspaceSubscriptionService : IWorkspaceSubscriptionService
{
    private readonly List<IDisposable> _fileSubscriptions = new();
    private IDisposable? _solutionSubscription;
    private VisualStudioExtensibility? _extensibility;

    public event EventHandler<IReadOnlyList<string>>? FilesChanged;
    public event Func<string, CancellationToken, Task>? StatusMessageRequested;

    public async Task SetupSubscriptionsAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {  
        _extensibility = extensibility;
        await SetStatusMessageAsync("Megnyitott solution keresése...", cancellationToken).ConfigureAwait(false);

        var solutions = await extensibility.Workspaces()
            .QuerySolutionAsync(s => s.With(s => s.Path), cancellationToken).ConfigureAwait(false);

        var singleSolution = solutions.FirstOrDefault();

        if (singleSolution is null)
        {
            FilesChanged?.Invoke(this, Array.Empty<string>());
            await SetStatusMessageAsync("Nincs megnyitott solution. Nyiss meg egy solutiont, majd kattints a Refresh gombra.", cancellationToken).ConfigureAwait(false);
            return;
        }

        _solutionSubscription?.Dispose();

        _solutionSubscription = await singleSolution
            .AsQueryable()
            .With(p => p.Projects)
            .SubscribeAsync(new WorkspaceSolutionObserver(this), cancellationToken).ConfigureAwait(false);

        await RefreshFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshFilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SetStatusMessageAsync("Fájlok lekérése...", cancellationToken).ConfigureAwait(false);

            if (_extensibility is null)
            {
                FilesChanged?.Invoke(this, Array.Empty<string>());
                return;
            }

            var workspace = _extensibility.Workspaces();

            var files = await workspace.QueryProjectsAsync(
                project => project
                    .Get(p => p.FilesEndingWith(".cs")
                        .With(f => f.Path)),
                cancellationToken).ConfigureAwait(false);

            var filePaths = files
                .Where(f => !string.IsNullOrEmpty(f.Path))
                .Select(f => f.Path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FilesChanged?.Invoke(this, filePaths);
            if (filePaths.Count == 0)
                await SetStatusMessageAsync("Nincsenek .cs fájlok a megnyitott projektekben.", cancellationToken).ConfigureAwait(false);
            else
                await SetStatusMessageAsync("Sikeres file search. Kezdőthet az analízis.", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await SetStatusMessageAsync($"Hiba a fájlok lekérésekor: {ex.Message}", cancellationToken).ConfigureAwait(false);
        }
    }

    private Task SetStatusMessageAsync(string m, CancellationToken ct) => StatusMessageRequested?.Invoke(m, ct) ?? Task.CompletedTask;

    public void Dispose()
    {
        foreach (var sub in _fileSubscriptions) sub.Dispose();
        _fileSubscriptions.Clear();

        _solutionSubscription?.Dispose();
        _solutionSubscription = null;
    }

    private sealed class WorkspaceSolutionObserver : IObserver<IQueryResults<ISolutionSnapshot>>
    {
        private readonly WorkspaceSubscriptionService _parent;
        public WorkspaceSolutionObserver(WorkspaceSubscriptionService parent) => _parent = parent;
        public void OnNext(IQueryResults<ISolutionSnapshot> value) => _ = _parent.RefreshFilesAsync(CancellationToken.None);
        public void OnError(Exception error) => _ = _parent.SetStatusMessageAsync($"Feliratkozás error: {error.Message}", CancellationToken.None);
        public void OnCompleted() => _ = _parent.SetupSubscriptionsAsync(_parent._extensibility!, CancellationToken.None);
    }
}
