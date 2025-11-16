using Microsoft.VisualStudio.Extensibility.UI;

namespace ArcExtension.UI.Controls;

internal class ArcWindowControl : RemoteUserControl
{
    public ArcWindowControl()
        : base(dataContext: null)
    {
    }
}