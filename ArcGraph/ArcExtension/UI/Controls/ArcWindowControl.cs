//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcExtension.UI.ViewModels;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ArcExtension.UI.Controls;

internal class ArcWindowControl : RemoteUserControl
{
    public ArcWindowControl(ArcWorkspaceViewModel vm)
        : base(dataContext: vm)
    {
    }
}