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
                return @"
<!doctype html>
<html>
<head>
  <meta charset='utf-8'>
  <title>ArcGraph Live</title>
  <style>
    html,body { height:100%; width:100%; margin:0; padding:0; font-family:Segoe UI,Arial; }
    #container { display:flex; height:100vh; width:100vw; }
    #cy { flex:1 1 auto; height:100%; width:100%; }
    #sidebar { width:360px; padding:12px; box-sizing:border-box; border-left:1px solid #ddd; overflow:auto; background:#fafafa; }
    #status { padding:8px; font-size:13px; margin-bottom:8px; }
    .controls { margin-bottom:8px; display:flex; gap:8px; align-items:center; flex-wrap:wrap; }
    .control { display:flex; align-items:center; gap:6px; }
    .small { font-size:12px; color:#444; }
    .meta { font-size:12px; color:#333; margin-top:6px; }
    .vuln-list { margin-top:8px; }
    .vuln-item { padding:6px; border:1px solid #f1d0d0; background:#fff6f6; margin-bottom:6px; border-radius:4px; }
    .legend-row { margin-bottom:6px; display:flex; align-items:center; }
    .legend-swatch { width:14px; height:14px; margin-right:6px; border:1px solid #999; }
  </style>
  <script src='https://unpkg.com/cytoscape@3.24.0/dist/cytoscape.min.js'></script>
</head>
<body>
  <div id='container'>
    <div id='cy'></div>
    <div id='sidebar'>
      <div id='status'>Initializing...</div>
      <div class='controls'>
        <label class='control small'><input type='checkbox' id='cb-hide-external'/> Hide external packages</label>
        <label class='control small'><input type='checkbox' id='cb-hide-vulnerable'/> Hide vulnerable nodes</label>
      </div>
      <div id='summary' class='meta'></div>

      <div style='margin-top:8px;'>
        <div class='legend-row'><div class='legend-swatch' style='background:#1f77b4'></div>UI</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#2ca02c'></div>Application</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#ff7f0e'></div>Domain</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#9467bd'></div>Infrastructure</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#d9534f'></div>Vulnerable package</div>
      </div>

      <div id='node-details' style='margin-top:12px;'>
        <h3 id='nd-label' style='margin:6px 0 4px 0;font-size:16px'></h3>
        <div id='nd-meta' class='meta'></div>
        <div id='nd-files' class='meta'></div>
        <div id='nd-package' class='meta'></div>
        <div id='nd-vulns' class='vuln-list'></div>
      </div>
    </div>
  </div>

  <script>
    let cy = null;
    let pollHandle = null;
    let filterHideExternal = false;
    let filterHideVulnerable = false;

    function colorForGroup(g) {
      switch((g||'').toLowerCase()) {
        case 'ui': return '#1f77b4';
        case 'application': return '#2ca02c';
        case 'domain': return '#ff7f0e';
        case 'infrastructure': return '#9467bd';
        default: return '#7f7f7f';
      }
    }

    document.addEventListener('DOMContentLoaded', () => {
      const cb = document.getElementById('cb-hide-external');
      if (cb) cb.addEventListener('change', (e) => {
        filterHideExternal = cb.checked;
        applyFilters();
      });
      const cbv = document.getElementById('cb-hide-vulnerable');
      if (cbv) cbv.addEventListener('change', (e) => {
        filterHideVulnerable = cbv.checked;
        applyFilters();
      });
    });

    async function fetchGraph() {
      try {
        const r = await fetch('/api/graph', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const data = await r.json();

        const nodeCount = data.nodes ? data.nodes.length : 0;
        const edgeCount = data.edges ? data.edges.length : 0;
        const violations = data.edges ? data.edges.filter(e => e.isViolation).length : 0;
        document.getElementById('status').innerText = 'Nodes: ' + nodeCount + ', Edges: ' + edgeCount + ', Violations: ' + violations + ' (updated ' + new Date().toLocaleTimeString() + ')';
        document.getElementById('summary').innerText = '';

        updateGraph(data);
      } catch (e) {
        document.getElementById('status').innerText = 'Failed to fetch graph: ' + e;
        console.error(e);
      }
    }

    function applyFilters() {
      if (!cy) return;
      cy.batch(() => {
        
        cy.nodes().show();
        cy.edges().show();

        if (filterHideExternal) {
          const ext = cy.nodes('.external');
          ext.hide();
         
          ext.connectedEdges().hide();
        }

        if (filterHideVulnerable) {
          const v = cy.nodes('.vuln');
          v.hide();
          v.connectedEdges().hide();
        }
      });
    }

    function updateGraph(data) {
      const elements = [];
      let hasPositions = false;

      for (const n of (data.nodes || [])) {
        const nodeData = {
          id: n.id,
          label: n.label || n.id,
          group: n.group || '',
          isVulnerable: !!n.isVulnerable,
          isExternal: !!n.isExternal,
          packageId: n.packageId || '',
          packageVersion: n.packageVersion || '',
          methodCount: n.methodCount || 0,
          propertyCount: n.propertyCount || 0,
          fieldCount: n.fieldCount || 0,
          sourceFiles: n.sourceFiles || [],
          vulnerabilities: n.vulnerabilities || [],
          degree: n.degree || 0
        };

        const classes = [];
        if (nodeData.group) classes.push('layer-' + nodeData.group.toLowerCase());
        classes.push(nodeData.isExternal ? 'external' : 'internal');
        if (nodeData.isVulnerable) classes.push('vuln');

        const el = { data: nodeData, classes: classes.join(' ') };
        if (typeof n.x === 'number' && typeof n.y === 'number') {
          hasPositions = true;
          el.position = { x: n.x, y: n.y };
        }
        elements.push(el);
      }

      for (const e of (data.edges || [])) {
        elements.push({ data: { id: (e.source + '-' + e.target), source: e.source, target: e.target, weight: e.weight || 1, isViolation: !!e.isViolation, kind: e.kind || '' } });
      }

      if (!cy) {
        cy = cytoscape({
          container: document.getElementById('cy'),
          elements: elements,
          style: [
            { selector: 'node', style: {
                'label': 'data(label)',
                'text-valign':'center',
                'text-halign':'center',
                'background-color': '#67a9cf',
                'width': 'mapData(degree, 0, 50, 24, 80)',
                'height': 'mapData(degree, 0, 50, 24, 80)',
                'border-width': 3,
                'border-color':'#ffffff',
                'font-size': 10,
                'text-wrap': 'wrap',
                'text-max-width': 80
              } },
            { selector: 'node.layer-ui', style: { 'background-color': '#1f77b4' } },
            { selector: 'node.layer-application', style: { 'background-color': '#2ca02c' } },
            { selector: 'node.layer-domain', style: { 'background-color': '#ff7f0e' } },
            { selector: 'node.layer-infrastructure', style: { 'background-color': '#9467bd' } },
            { selector: 'node.external', style: { 'border-style': 'dashed', 'border-color': '#333', 'opacity': 0.95 } },
            { selector: 'node.internal', style: { 'border-style': 'solid' } },
            { selector: 'node.vuln', style: { 'border-color': '#d9534f', 'border-width': 8 } },
            { selector: 'edge', style: { 'width': 'mapData(weight, 1, 10, 1, 6)', 'line-color': '#999', 'target-arrow-shape': 'triangle', 'target-arrow-color': '#999', 'curve-style': 'bezier' } },
            { selector: 'edge[isViolation = true]', style: { 'line-color': '#d9534f', 'target-arrow-color': '#d9534f', 'width': 3 } }
          ],
          layout: hasPositions ? { name: 'preset' } : { name: 'cose', animate: true },
          wheelSensitivity: 0.2
        });

        cy.on('tap', 'node', (evt) => {
          const node = evt.target;
          showNodeDetails(node.data());
        });
        applyFilters();
      } else {
        cy.batch(() => {
          cy.elements().remove();
          cy.add(elements);
        });

        if (hasPositions) {
          cy.layout({ name: 'preset' }).run();
        } else {
          cy.layout({ name: 'cose', animate: true }).run();
        }
        applyFilters();
      }
    }

    function showNodeDetails(d) {
      document.getElementById('nd-label').innerText = d.label || d.id;
      const meta = [];

      if (d.isExternal) {
        meta.push('External package (no source available)');
        document.getElementById('nd-meta').innerText = meta.join(' | ');
        document.getElementById('nd-files').innerText = '';
      } else {
        if (d.group) meta.push('Layer: ' + d.group);
        meta.push('Degree: ' + (d.degree || 0));
        meta.push('Methods: ' + (d.methodCount||0) + ', Props: ' + (d.propertyCount||0) + ', Fields: ' + (d.fieldCount||0));
        document.getElementById('nd-meta').innerText = meta.join(' | ');

        if (d.sourceFiles && d.sourceFiles.length > 0) {
          document.getElementById('nd-files').innerText = 'Sources: ' + d.sourceFiles.slice(0,5).join('; ');
        } else {
          document.getElementById('nd-files').innerText = '';
        }
      }

      if (d.packageId) {
        document.getElementById('nd-package').innerHTML = `Package: <b>${d.packageId}</b> ${d.packageVersion ? ('v' + d.packageVersion) : ''} ${d.isExternal ? '(external)' : '(solution)'}`;
      } else {
        document.getElementById('nd-package').innerText = d.isExternal ? '(external assembly)' : '';
      }

      const vulnsContainer = document.getElementById('nd-vulns');
      vulnsContainer.innerHTML = '';
      if (d.vulnerabilities && d.vulnerabilities.length) {
        for (const v of d.vulnerabilities) {
          const div = document.createElement('div');
          div.className = 'vuln-item';
          div.innerHTML = `<b>${v.id || ''}</b> ${v.title || ''} <span style='color:#a00'>(${v.severity || ''})</span><div style='font-size:12px'>${v.description || ''}</div>`;
          vulnsContainer.appendChild(div);
        }
      } else if (d.isVulnerable) {
        const div = document.createElement('div');
        div.className = 'vuln-item';
        div.innerHTML = '<b>Known vulnerable package</b>';
        vulnsContainer.appendChild(div);
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
          stopPolling();
        };
        es.onmessage = function(e) {
          console.log('SSE message', e.data);
          fetchGraph();
        };
        es.onerror = function(err) {
          console.warn('SSE error, will fallback to polling', err);
          try { es.close(); } catch {}
          startPolling(2000);
        };

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
    fetchGraph();
    setupSse();
  </script>
</body>
</html>";
            }
        }
    }