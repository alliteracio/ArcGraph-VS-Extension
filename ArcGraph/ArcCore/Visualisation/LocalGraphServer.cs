//  Diploma Thesis 2025
//  Alexandra Apró
//  University of Szeged

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ArcCore.Visualisation
{
    public class LocalGraphServer
    {
        private HttpListener? _httpListener;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private string _graphJson = "{}";
        private int _listeningPort = -1;
        private bool _usingHttpListener;
        private readonly object _sseLock = new();
        private readonly List<HttpListenerResponse> _sseClients = new();

        public int Start(int? forcePort = null)
        {
            var port = forcePort ?? GetRandomUnusedPort();
            _listeningPort = port;
           
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

            try
            {
                var localAddr = IPAddress.Loopback;
                _tcpListener = new TcpListener(localAddr, port);
                _tcpListener.Start();
                _cts = new CancellationTokenSource();
                Task.Run(() => TcpListenLoop(_cts.Token));
                Debug.WriteLine($"[LocalGraphServer] TcpListener started on http://127.0.0.1:{port}/");
                return port;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalGraphServer] TcpListener.Start exception: {ex}");
                throw;
            }
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

                try
                {
                    if (_tcpListener != null)
                    {
                        _tcpListener.Stop();
                        _tcpListener = null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[LocalGraphServer] Stop tcpListener exception: " + ex);
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

        private async Task TcpListenLoop(CancellationToken ct)
        {
            if (_tcpListener == null) return;

            while (!ct.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine("[LocalGraphServer] TcpListener Accept exception: " + ex);
                    break;
                }

                _ = Task.Run(() => HandleTcpClient(client));
            }
        }

        private async Task HandleTcpClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[8192];
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) return;
                    var reqText = Encoding.UTF8.GetString(buffer, 0, read);
                    var firstLineEnd = reqText.IndexOf("\r\n", StringComparison.Ordinal);
                    if (firstLineEnd <= 0) return;
                    var firstLine = reqText.Substring(0, firstLineEnd);
                    var parts = firstLine.Split(' ');
                    if (parts.Length < 2) return;
                    var method = parts[0];
                    var path = parts[1];
                    Debug.WriteLine($"[LocalGraphServer] Tcp request: {method} {path}");

                    if (path.StartsWith("/api/graph", StringComparison.OrdinalIgnoreCase))
                    {
                        var payload = Encoding.UTF8.GetBytes(_graphJson);
                        var header = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\n\r\n";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(header)).ConfigureAwait(false);
                        await stream.WriteAsync(payload).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                        return;
                    }

                    if (path == "/" || path == "/index.html")
                    {
                        var html = BuildIndexHtml();
                        var payload = Encoding.UTF8.GetBytes(html);
                        var header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\n\r\n";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(header)).ConfigureAwait(false);
                        await stream.WriteAsync(payload).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                        return;
                    }

                    var nf = Encoding.UTF8.GetBytes("404 Not Found");
                    var nfHeader = "HTTP/1.1 404 Not Found\r\nContent-Length: " + nf.Length + "\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(nfHeader)).ConfigureAwait(false);
                    await stream.WriteAsync(nf).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[LocalGraphServer] HandleTcpClient exception: " + ex);
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
            // Full interactive index: Cytoscape + SSE (tries SSE, uses preset positions if provided, otherwise falls back to cose)
            return @"
<!doctype html>
<html>
<head>
  <meta charset='utf-8'>
  <title>ArcGraph Live</title>
  <style>
    html,body,#cy { height:100%; width:100%; margin:0; padding:0; }
    #status { padding:8px; font-family:Segoe UI,Arial; }
  </style>
  <script src='https://unpkg.com/cytoscape@3.24.0/dist/cytoscape.min.js'></script>
</head>
<body>
  <div id='status'>Initializing...</div>
  <div id='cy'></div>

  <script>
    let cy = null;
    let pollHandle = null;

    async function fetchGraph() {
      try {
        const r = await fetch('/api/graph', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const data = await r.json();
        document.getElementById('status').innerText = 'Nodes: ' + (data.nodes?data.nodes.length:0) + ', Edges: ' + (data.edges?data.edges.length:0) + ' (updated ' + new Date().toLocaleTimeString() + ')';
        updateGraph(data);
      } catch (e) {
        document.getElementById('status').innerText = 'Failed to fetch graph: ' + e;
        console.error(e);
      }
    }

    function updateGraph(data) {
      // Build cytoscape elements and collect whether positions exist
      const elements = [];
      let hasPositions = false;

      for (const n of (data.nodes || [])) {
        const nodeData = { id: n.id, label: n.label || n.id, group: n.group };
        const el = { data: nodeData };
        if (typeof n.x === 'number' && typeof n.y === 'number') {
          hasPositions = true;
          el.position = { x: n.x, y: n.y };
        }
        elements.push(el);
      }

      for (const e of (data.edges || [])) {
        elements.push({ data: { id: (e.source+'-'+e.target), source: e.source, target: e.target, weight: e.weight || 1, isViolation: e.isViolation || false } });
      }

      if (!cy) {
        cy = cytoscape({
          container: document.getElementById('cy'),
          elements: elements,
          style: [
            { selector: 'node', style: { 'label': 'data(label)', 'text-valign':'center', 'background-color': '#67a9cf', 'width': 40, 'height': 40 } },
            { selector: 'edge', style: { 'width': 'mapData(weight, 1, 10, 1, 6)', 'line-color': '#999', 'target-arrow-shape': 'triangle', 'target-arrow-color': '#999' } },
            { selector: 'edge[isViolation = true]', style: { 'line-color': '#d9534f', 'target-arrow-color': '#d9534f' } }
          ],
          layout: hasPositions ? { name: 'preset' } : { name: 'cose', animate: true }
        });

        cy.on('tap', 'node', (evt) => {
          const node = evt.target;
          alert(`${node.data('label')}\\nGroup: ${node.data('group')}`);
        });
      } else {
        // If positions available, update with preset positions and don't re-run heavy layout
        if (hasPositions) {
          // keep viewport (pan/zoom) stable: apply positions and refresh
          cy.batch(() => {
            cy.nodes().forEach(n => {
              const incoming = elements.find(e => e.data && e.data.id === n.id());
              if (incoming && incoming.position) {
                n.position(incoming.position);
              }
            });
            // update edges/nodes data if necessary
            cy.elements().remove();
            cy.add(elements);
          });
          // ensure preset layout is applied
          cy.layout({ name: 'preset' }).run();
        } else {
          // replace elements and run dynamic layout
          cy.elements().remove();
          cy.add(elements);
          cy.layout({ name: 'cose', animate: true }).run();
        }
      }
    }

    function startPolling(intervalMs = 2000) {
      if (pollHandle) return;
      pollHandle = setInterval(fetchGraph, intervalMs);
      console.info('Polling started (fallback)');
    }

    function stopPolling() {
      if (pollHandle) {
        clearInterval(pollHandle);
        pollHandle = null;
        console.info('Polling stopped');
      }
    }

    function setupSse() {
      try {
        const es = new EventSource('/api/stream');
        let connected = false;
        es.onopen = function() {
          connected = true;
          console.log('SSE connected');
          // stop any polling if running
          stopPolling();
        };
        es.onmessage = function(e) {
          // server signaled update -> fetch updated graph immediately
          console.log('SSE message', e.data);
          fetchGraph();
        };
        es.onerror = function(err) {
          console.warn('SSE error, will fallback to polling', err);
          try { es.close(); } catch {}
          startPolling(2000);
        };

        // If connection not established quickly, start polling as guard
        setTimeout(() => {
          if (!connected) {
            console.log('SSE not established quickly; starting polling fallback');
            startPolling(2000);
          }
        }, 800);
      } catch (ex) {
        console.warn('SSE not supported, fallback to polling', ex);
        startPolling(2000);
      }
    }

    // initial fetch + SSE attempt (polling only if SSE fails)
    fetchGraph();
    setupSse();
  </script>
</body>
</html>";
        }
    }
}
