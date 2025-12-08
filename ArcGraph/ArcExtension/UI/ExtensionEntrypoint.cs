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
                displayName: "ArcExtension",
                description: "Extension description")
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
    }
}
