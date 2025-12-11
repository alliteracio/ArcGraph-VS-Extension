//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

namespace ArcExtension.UI.Services;

public interface IUiThreadHelper
{
    Task RunOnUiAsync(Action action, CancellationToken cancellationToken = default);
    Task SetStatusMessageAsync(string message, CancellationToken cancellationToken = default);
}
