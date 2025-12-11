//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcExtension.UI.ViewModels;
using System.Windows;
using System.Windows.Threading;

namespace ArcExtension.UI.Services;

public sealed class UiThreadHelper : IUiThreadHelper
{
    private readonly ArcWorkspaceViewModel _vm;

    public UiThreadHelper(ArcWorkspaceViewModel vm) => _vm = vm;

    public async Task RunOnUiAsync(Action action, CancellationToken cancellationToken = default)
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

            var sc = SynchronizationContext.Current;
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

    public Task SetStatusMessageAsync(string message, CancellationToken cancellationToken = default)
        => RunOnUiAsync(() => _vm.StatusMessage = message, cancellationToken);
}
