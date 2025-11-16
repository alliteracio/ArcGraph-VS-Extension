//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ArcExtension.UI.Commands;

[VisualStudioContribution]
public class ArcToolWindowCommand : Command
{
    public ArcToolWindowCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    public override CommandConfiguration CommandConfiguration => new("ArcGraph")
    {
        Placements = new[] { CommandPlacement.KnownPlacements.ViewOtherWindowsMenu },
        Icon = new(ImageMoniker.KnownValues.ToolWindow, IconSettings.IconAndText),
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await Extensibility.Shell().ShowToolWindowAsync<ArcToolWindow>(activate: true, cancellationToken);
    }
}
