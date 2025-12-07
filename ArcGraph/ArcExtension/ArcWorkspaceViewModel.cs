//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Extensibility.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.Serialization;

namespace ArcExtension;

[DataContract]
public sealed class ArcWorkspaceViewModel : ObservableObject
{
    [DataMember]
    public ObservableCollection<string> Files { get; } = new();

    private string? _solutionPath;
    public string? SolutionPath
    {
        get => _solutionPath;
        set
        {
            if (_solutionPath != value)
            {
                _solutionPath = value;
                OnPropertyChanged();
            }
        }
    }

    private string _statusMessage = string.Empty;

    [DataMember]
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    [DataMember]
    public IAsyncCommand RefreshCommand { get; }

    public event Func<CancellationToken, Task>? RefreshRequested;

    public ArcWorkspaceViewModel()
    {
        RefreshCommand = new AsyncCommand(ExecuteRefreshAsync);

        Files.CollectionChanged += OnFilesCollectionChanged;
    }

    private Task ExecuteRefreshAsync(object? parameter, CancellationToken ct)
    {
        if (RefreshRequested is not null)
            return RefreshRequested.Invoke(ct);

        return Task.CompletedTask;
    }

    private void OnFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
   
        if (Files.Count > 0 && !string.IsNullOrEmpty(StatusMessage))
        {
            StatusMessage = string.Empty;
        }
    }
}