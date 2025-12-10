//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using ArcAnalyzer.UI;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace ArcAnalyzer.Infrastructure.VisualizationServer;

public class LocalGraphServer
{
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private string _graphJson = "{}";
    private bool _usingHttpListener;
    private readonly object _sseLock = new();
    private readonly List<HttpListenerResponse> _sseClients = new();

    public int Start(int? forcePort = null)
    {
        var port = forcePort ?? GetRandomUnusedPort();

        try
        {
            var prefix = $"http://127.0.0.1:{port}/";
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(prefix);

            _httpListener.Start();
            _cts = new CancellationTokenSource();
            Task.Run(() => HttpListenLoop(_cts.Token));
            _usingHttpListener = true;
            Debug.WriteLine($"[LocalGraphServer] HttpListener started on {prefix}");
            return port;
        }
        catch (HttpListenerException hlex)
        {
            Debug.WriteLine($"[LocalGraphServer] HttpListener.Start failed: {hlex}. Falling back to TcpListener.");
            _httpListener = null;
            _usingHttpListener = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalGraphServer] HttpListener.Start exception: {ex}. Falling back to TcpListener.");
            _httpListener = null;
            _usingHttpListener = false;
        }   
        return -1;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            lock (_sseLock)
            {
                foreach (var res in _sseClients)
                {
                    try { res.OutputStream.Close(); } catch { }
                }
                _sseClients.Clear();
            }

            try
            {
                if (_httpListener != null)
                {
                    if (_httpListener.IsListening) _httpListener.Stop();
                    _httpListener.Close();
                    _httpListener = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[LocalGraphServer] Stop httpListener exception: " + ex);
            }           

            Debug.WriteLine("[LocalGraphServer] stopped");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LocalGraphServer] Stop exception: " + ex);
        }
    }

    public void SetGraphJson(string json)
    {
        _graphJson = json ?? "{}";
        Debug.WriteLine("[LocalGraphServer] SetGraphJson length=" + (_graphJson?.Length ?? 0));

        if (_usingHttpListener)
        {
            NotifySseClients();
        }
    }

    private async Task HttpListenLoop(CancellationToken ct)
    {
        if (_httpListener == null) return;

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _httpListener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine("[LocalGraphServer] HttpListener GetContextAsync exception: " + ex);
                break;
            }

            _ = Task.Run(() => HandleHttpListenerRequest(ctx));
        }
    }

    private void HandleHttpListenerRequest(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            Debug.WriteLine($"[LocalGraphServer] HttpListener Request: {req.HttpMethod} {req.RawUrl}");

            if (req.RawUrl.StartsWith("/api/graph", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Encoding.UTF8.GetBytes(_graphJson);
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
                return;
            }

            if (req.RawUrl.StartsWith("/api/stream", StringComparison.OrdinalIgnoreCase))
            {
                res.ContentType = "text/event-stream";
                res.Headers.Add("Cache-Control", "no-cache");
                res.SendChunked = true;

                var init = Encoding.UTF8.GetBytes(": ok\n\n");
                try
                {
                    res.OutputStream.Write(init, 0, init.Length);
                    res.OutputStream.Flush();
                }
                catch
                {
                    try { res.OutputStream.Close(); } catch { }
                    return;
                }

                lock (_sseLock)
                {
                    _sseClients.Add(res);
                }

                return;
            }

            if (req.RawUrl.StartsWith("/api/export", StringComparison.OrdinalIgnoreCase))
            {
                var q = req.Url?.Query ?? "";
                var format = "dot";
                if (!string.IsNullOrEmpty(q))
                {
                    var trimmed = q.TrimStart('?');
                    var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        var kv = p.Split('=', 2);
                        if (kv.Length == 2 && kv[0].Equals("format", StringComparison.OrdinalIgnoreCase))
                        {
                            format = Uri.UnescapeDataString(kv[1]).ToLowerInvariant();
                        }
                    }
                }

                try
                {
                    string outText;
                    string contentType;
                    switch (format)
                    {
                        case "mermaid":
                            outText = GraphExporter.ToMermaid(_graphJson);
                            contentType = "text/plain; charset=utf-8";
                            break;
                        case "dgml":
                            outText = GraphExporter.ToDgml(_graphJson);
                            contentType = "application/xml; charset=utf-8";
                            break;
                        default:
                            outText = GraphExporter.ToDot(_graphJson);
                            contentType = "text/plain; charset=utf-8";
                            break;
                    }

                    var bytes = Encoding.UTF8.GetBytes(outText);
                    res.ContentType = contentType;
                    res.ContentLength64 = bytes.Length;
                    var fname = format == "dgml" ? "graph.dgml" : format == "mermaid" ? "graph.mmd" : "graph.dot";
                    try { res.Headers.Add("Content-Disposition", $"attachment; filename=\"{fname}\""); } catch { }
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.OutputStream.Close();

                    Debug.WriteLine($"[LocalGraphServer] export served: format={format}, size={bytes.Length}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[LocalGraphServer] export error: " + ex);
                    res.StatusCode = 500;
                    var ee = Encoding.UTF8.GetBytes("Export error");
                    res.OutputStream.Write(ee, 0, ee.Length);
                    res.OutputStream.Close();
                }

                return;
            }

            if (req.RawUrl == "/" || req.RawUrl == "/index.html")
            {
                var html = BuildIndexHtml();
                var bytes = Encoding.UTF8.GetBytes(html);
                res.ContentType = "text/html; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
                return;
            }

            res.StatusCode = 404;
            var nf = Encoding.UTF8.GetBytes("Not Found");
            res.OutputStream.Write(nf, 0, nf.Length);
            res.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LocalGraphServer] HandleHttpListenerRequest exception: " + ex);
        }
    }

    private void NotifySseClients()
    {
        List<HttpListenerResponse> toRemove = new();
        lock (_sseLock)
        {
            if (_sseClients.Count == 0) return;

            foreach (var res in _sseClients)
            {
                try
                {
                    var payload = "data: {\"event\":\"update\"}\n\n";
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.OutputStream.Flush();
                }
                catch
                {
                    toRemove.Add(res);
                    try { res.OutputStream.Close(); } catch { }
                }
            }

            foreach (var dead in toRemove)
            {
                _sseClients.Remove(dead);
            }
        }
    }

    private static int GetRandomUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string BuildIndexHtml()
    {
        try
        {
            var info = Assembly.GetExecutingAssembly().GetName();
            var name = info.Name;
            using var stream = Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream($"{name}.www.index.html")!;
            if(stream!= null)
            {
                using var streamReader = new StreamReader(stream, Encoding.UTF8);
                return streamReader.ReadToEnd();
            }
                          
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LocalGraphServer] BuildIndexHtml read exception: " + ex);
        }

        return @"
                    <!doctype html>
                    <html>
                    <head>
                      <meta charset='utf-8'>
                      <title>ArcGraph</title>
                      <meta name='viewport' content='width=device-width,initial-scale=1'/>
                      <style>
                        body { font-family:Segoe UI, Arial; padding:20px; color:#222; }                       
                      </style>
                    </head>
                    <body>
                      <p>Megjelenítési hiba.</p> 
                    </body>
                    </html>";
    }
}