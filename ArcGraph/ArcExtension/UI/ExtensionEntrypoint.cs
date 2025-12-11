//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace ArcExtension.UI;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
                id: "ArcExtension.a13939ae-036a-4290-9efb-89d1d63dde1a",
                version: ExtensionAssemblyVersion,
                publisherName: "Alexandra Apró",
                displayName: "ArcGraph",
                description: "Dependency Graph Analyzer that provides an interactive Tool Window for building, analyzing and visualizing dependency graphs of .NET solutions. ")
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
    }
}
