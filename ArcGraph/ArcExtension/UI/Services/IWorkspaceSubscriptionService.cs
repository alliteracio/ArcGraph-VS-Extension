//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using Microsoft.VisualStudio.Extensibility;

namespace ArcExtension.UI.Services;

public interface IWorkspaceSubscriptionService : IDisposable
{
    event EventHandler<IReadOnlyList<string>>? FilesChanged;
    event Func<string, CancellationToken, Task>? StatusMessageRequested;

    Task SetupSubscriptionsAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken);
    Task RefreshFilesAsync(CancellationToken cancellationToken);
}
