using ArcExtension.UI.Controls;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ArcExtension.UI;

[VisualStudioContribution]
internal class ArcToolWindow : ToolWindow
{
    public ArcToolWindow(VisualStudioExtensibility extensibility)
    : base(extensibility)
    {
        Title = "ArcGraph";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
        DockDirection = Dock.Right,
        AllowAutoCreation = true,
    };

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IRemoteUserControl>(new ArcWindowControl());
    }
}
