//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.Domain.GraphModel;
using ArcAnalyzer.Infrastructure.VisualizationServer;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Extensibility.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace ArcExtension.UI.ViewModels;

[DataContract]
public sealed class ArcWorkspaceViewModel : ObservableObject
{
    private LocalGraphServer? _server;
    private int _serverPort = -1;
    private bool _isServerStarted;

    [DataMember]
    public ObservableCollection<string> Files { get; } = new();

    [DataMember]
    public ObservableCollection<string> VulnerablePackages { get; } = new();

    private string? _solutionPath = string.Empty;

    [DataMember]
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

    private DependencyGraph? _dependencyGraph;
    public DependencyGraph? DependencyGraph
    {
        get => _dependencyGraph;
        set => SetProperty(ref _dependencyGraph, value);
    }

    [DataMember]
    public IAsyncCommand RefreshCommand { get; }

    [DataMember]
    public IAsyncCommand AnalyzeCommand { get; }

    public event Func<CancellationToken, Task>? RefreshRequested;
    public event Func<CancellationToken, Task>? AnalyzeRequested;

    private Task ExecuteRefreshAsync(object? parameter, CancellationToken ct)
    => RefreshRequested is not null ? RefreshRequested.Invoke(ct) : Task.CompletedTask;

    private Task ExecuteAnalyzeAsync(object? parameter, CancellationToken ct)
       => AnalyzeRequested?.Invoke(CancellationToken.None) ?? Task.CompletedTask;

    public event PropertyChangedEventHandler PropertyChanged;

    public ArcWorkspaceViewModel()
    {
        RefreshCommand = new AsyncCommand(ExecuteRefreshAsync);
        AnalyzeCommand = new AsyncCommand(ExecuteAnalyzeAsync);

        _ = EnsureServerStartedAsync();
    }

    private async Task EnsureServerStartedAsync()
    {
        if (_isServerStarted || _server != null) return;
        _isServerStarted = true;
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
            _isServerStarted = false;
            await Task.CompletedTask;
        }
    }

    public async Task UpdateGraphAsync(string graphJson)
    {
        if (_server == null)
        {
            await EnsureServerStartedAsync().ConfigureAwait(false);
        }

        await SetGraphOnServerAsync(graphJson).ConfigureAwait(false);
    }

    private Task SetGraphOnServerAsync(string graphJson)
    {
        try
        {
            _server.SetGraphJson(graphJson);
            StatusMessage = $"Graph frissítve. Web: {WebViewUri}";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            Debug.WriteLine("[ArcWorkspaceViewModel] Graph JSON updated, length=" + (graphJson?.Length ?? 0));
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[ArcWorkspaceViewModel] UpdateGraphJson exception: " + ex);
            StatusMessage = "Hiba a graph frissítésekor: " + ex.Message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            return Task.CompletedTask;
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