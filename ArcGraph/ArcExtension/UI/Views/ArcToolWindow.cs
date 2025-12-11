//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcExtension.UI.Controls;
using ArcExtension.UI.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ArcExtension.UI.Views;

[VisualStudioContribution]
internal class ArcToolWindow : ToolWindow
{
    private ArcWorkspaceWatcher _workspaceWatcher;
    public ArcToolWindow(VisualStudioExtensibility extensibility)
    : base(extensibility)
    {
        Title = "ArcGraph";
        _workspaceWatcher = new ArcWorkspaceWatcher(extensibility);
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
        DockDirection = Dock.Right,
        AllowAutoCreation = false,
    };

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _workspaceWatcher.StartAsync(cancellationToken);
    }

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IRemoteUserControl>(new ArcWindowControl(_workspaceWatcher.Data));
    }
}
