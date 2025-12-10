//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcCore.GraphModel;
using ArcCore.Visualisation;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Extensibility.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace ArcExtension;

[DataContract]
public sealed class ArcWorkspaceViewModel : ObservableObject
{
    [DataMember]
    public ObservableCollection<string> Files { get; } = new();

    public ObservableCollection<string> VulnerablePackages { get; } = new();

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

    private string _webViewUri = string.Empty;

    [DataMember]
    public string WebViewUri
    {
        get => _webViewUri;
        set
        {
            if (_webViewUri != value)
            {
                _webViewUri = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebViewUri)));
            }
        }
    }

    [DataMember]
    public IAsyncCommand RefreshCommand { get; }
    [DataMember]
    public IAsyncCommand AnalyzeCommand { get; }

    private DependencyGraph? _dependencyGraph;
    public DependencyGraph? DependencyGraph
    {
        get => _dependencyGraph;
        set => SetProperty(ref _dependencyGraph, value);
    }

    public event Func<CancellationToken, Task>? RefreshRequested;
    public event Func<CancellationToken, Task>? AnalyzeRequested;

    private LocalGraphServer? _server;
    private int _serverPort = -1;
    private bool _serverStarting;

    public ArcWorkspaceViewModel()
    {
        RefreshCommand = new AsyncCommand(ExecuteRefreshAsync);
        AnalyzeCommand = new AsyncCommand(ExecuteAnalyzeAsync);
        _ = EnsureServerStartedAsync();
        Files.CollectionChanged += OnFilesCollectionChanged;
    }

    private Task ExecuteRefreshAsync(object? parameter, CancellationToken ct)
        => RefreshRequested is not null ? RefreshRequested.Invoke(ct) : Task.CompletedTask;

    private Task ExecuteAnalyzeAsync(object? parameter, CancellationToken ct)
       => AnalyzeRequested?.Invoke(CancellationToken.None) ?? Task.CompletedTask;

    private void OnFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Files.Count > 0 && !string.IsNullOrEmpty(StatusMessage))
        {
            StatusMessage = string.Empty;
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private async Task EnsureServerStartedAsync()
    {
        if (_serverStarting || _server != null) return;
        _serverStarting = true;
        try
        {
            _server = new LocalGraphServer();
            _serverPort = _server.Start();

            WebViewUri = $"http://127.0.0.1:{_serverPort}/";

            StatusMessage = $"Localhost elinditva: {WebViewUri}";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));

            Debug.WriteLine($"[ArcWorkspaceViewModel] LocalGraphServer started on {WebViewUri}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[ArcWorkspaceViewModel] Failed to start LocalGraphServer: " + ex);
            StatusMessage = "Nem sikerült elindítani a localhostot: " + ex.Message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }
        finally
        {
            _serverStarting = false;
            await Task.CompletedTask;
        }
    }

    public void UpdateGraphJson(string graphJson)
    {
        if (_server == null)
        {
            _ = EnsureServerStartedAsync().ContinueWith(t =>
            {
                try
                {
                    _server?.SetGraphJson(graphJson);
                    StatusMessage = $"Graph frissítve. Web: {WebViewUri}";
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ArcWorkspaceViewModel] UpdateGraphJson error: " + ex);
                }
            });
            return;
        }

        try
        {
            _server.SetGraphJson(graphJson);
            StatusMessage = $"Graph frissítve. Web: {WebViewUri}";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            Debug.WriteLine("[ArcWorkspaceViewModel] Graph JSON updated, length=" + (graphJson?.Length ?? 0));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[ArcWorkspaceViewModel] UpdateGraphJson exception: " + ex);
            StatusMessage = "Hiba a graph frissítésekor: " + ex.Message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }
    }

    public void Dispose()
    {
        try
        {
            _server?.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[ArcWorkspaceViewModel] Dispose error: " + ex);
        }
    }
}