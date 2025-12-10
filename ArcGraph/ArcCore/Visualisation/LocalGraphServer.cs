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

                    if (path.StartsWith("/api/export", StringComparison.OrdinalIgnoreCase))
                    {
                        var format = "dot";
                        var qIdx = path.IndexOf('?');
                        if (qIdx >= 0 && qIdx < path.Length - 1)
                        {
                            var q = path.Substring(qIdx + 1);
                            var pairs = q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in pairs)
                            {
                                var kv = p.Split(new[] { '=' }, 2);
                                if (kv.Length == 2 && kv[0].Equals("format", StringComparison.OrdinalIgnoreCase))
                                    format = Uri.UnescapeDataString(kv[1]).ToLowerInvariant();
                            }
                        }

                        string outText;
                        string contentType;
                        switch (format)
                        {
                            case "mermaid": outText = GraphExporter.ToMermaid(_graphJson); contentType = "text/plain; charset=utf-8"; break;
                            case "dgml": outText = GraphExporter.ToDgml(_graphJson); contentType = "application/xml; charset=utf-8"; break;
                            default: outText = GraphExporter.ToDot(_graphJson); contentType = "text/plain; charset=utf-8"; break;
                        }

                        var payload = Encoding.UTF8.GetBytes(outText);
                        var header2 = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nContent-Disposition: attachment; filename=\"graph.{(format == "dgml" ? "dgml" : format == "mermaid" ? "mmd" : "dot")}\"\r\n\r\n";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(header2)).ConfigureAwait(false);
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
  <meta name='viewport' content='width=device-width,initial-scale=1'/>
  <style>
    html,body { height:100%; width:100%; margin:0; padding:0; font-family:Segoe UI,Arial; }
    #container { display:flex; height:100vh; width:100vw; }
    #cy { flex:1 1 auto; height:100%; width:100%; background:#fff; }
    #sidebar { width:360px; padding:12px; box-sizing:border-box; border-left:1px solid #ddd; overflow:auto; background:#fafafa; }
    #status { padding:8px; font-size:13px; margin-bottom:8px; white-space:pre-wrap; }
    .meta { font-size:12px; color:#333; margin-top:6px; }
    .vuln-list { margin-top:8px; }
    .vuln-item { padding:6px; border:1px solid #f1d0d0; background:#fff6f6; margin-bottom:6px; border-radius:4px; }
    .legend-row { margin-bottom:6px; display:flex; align-items:center; }
    .legend-swatch { width:14px; height:14px; margin-right:6px; border:1px solid #999; }
    .controls { margin-bottom:8px; display:flex; gap:8px; align-items:center; flex-wrap:wrap; }
    .control { display:flex; align-items:center; gap:6px; }
    .small { font-size:12px; color:#444; }
    #export-controls { margin-top:6px; }
    #export-controls button { margin-right:6px; }
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

      <!-- static export controls: these call the server's /api/export which uses your separate exporter class -->
      <div id='export-controls'>
        <button id='export-dot' title='Export DOT'>Export DOT</button>
        <button id='export-mermaid' title='Export Mermaid'>Export Mermaid</button>
        <button id='export-dgml' title='Export DGML'>Export DGML</button>
      </div>

      <div id='summary' class='meta'><div id='summary-data'></div></div>

      <div style='margin-top:8px;'>
        <div class='legend-row'><div class='legend-swatch' style='background:#1f77b4'></div>UI</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#2ca02c'></div>Application</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#ff7f0e'></div>Domain</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#9467bd'></div>Infrastructure</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#d9534f'></div>Vulnerable package</div>
        <div class='legend-row'><div class='legend-swatch' style='background:#ffcc00'></div>In-cycle</div>
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

    function colorForGroup(g) {
      switch ((g||'').toLowerCase()) {
        case 'ui': return '#1f77b4';
        case 'application': return '#2ca02c';
        case 'domain': return '#ff7f0e';
        case 'infrastructure': return '#9467bd';
        default: return '#67a9cf';
      }
    }

    const clusterPalette = ['#8dd3c7','#ffffb3','#bebada','#fb8072','#80b1d3','#fdb462','#b3de69','#fccde5','#d9d9d9','#bc80bd','#ccebc5','#ffed6f'];
    function clusterColor(c) { if (!c || c <= 0) return '#ffffff'; return clusterPalette[(c - 1) % clusterPalette.length]; }
    function setStatus(t) { const s = document.getElementById('status'); if (s) s.innerText = t; console.debug('[arcgraph] status:', t); }
    function setSummary(t) { const s = document.getElementById('summary-data'); if (s) s.innerText = t; }

    function applyFilters() {
      if (!cy) return;
      cy.batch(() => {
        cy.nodes().show();
        cy.edges().show();
        const hideExternal = !!document.getElementById('cb-hide-external') && document.getElementById('cb-hide-external').checked;
        const hideVuln = !!document.getElementById('cb-hide-vulnerable') && document.getElementById('cb-hide-vulnerable').checked;
        if (hideExternal) { const ext = cy.nodes('.external'); ext.hide(); ext.connectedEdges().hide(); }
        if (hideVuln) { const v = cy.nodes('.vuln'); v.hide(); v.connectedEdges().hide(); }
      });
    }

    function showNodeDetails(d) {
      try {
        document.getElementById('nd-label').innerText = d.label || d.id;
        const meta = [];
        if (d.isExternal) meta.push('External');
        if (d.isInCycle) meta.push('In cycle');
        if (d.group) meta.push('Layer: ' + d.group);
        meta.push('Degree: ' + (d.degree || 0));
        document.getElementById('nd-meta').innerText = meta.join(' | ');
        document.getElementById('nd-files').innerText = d.sourceFiles && d.sourceFiles.length ? 'Sources: ' + d.sourceFiles.slice(0,5).join('; ') : '';
        if (d.packageId) {
          document.getElementById('nd-package').innerHTML = '<b>Package:</b> ' + d.packageId + (d.packageVersion ? (' v' + d.packageVersion) : '') + (d.isExternal ? ' (external)' : ' (solution)');
        } else {
          document.getElementById('nd-package').innerText = d.isExternal ? '(external assembly)' : '';
        }
        const vulnsContainer = document.getElementById('nd-vulns');
        vulnsContainer.innerHTML = '';
        if (d.vulnerabilities && d.vulnerabilities.length) {
          d.vulnerabilities.forEach(v => {
            const div = document.createElement('div');
            div.className = 'vuln-item';
            div.innerHTML = '<b>' + (v.id || '') + '</b> ' + (v.title || '') + ' <span style=\'color:#a00\'>' + (v.severity || '') + '</span><div style=\'font-size:12px\'>' + (v.description || '') + '</div>';
            vulnsContainer.appendChild(div);
          });
        } else if (d.isVulnerable) {
          const div = document.createElement('div');
          div.className = 'vuln-item';
          div.innerHTML = '<b>Known vulnerable package</b>';
          vulnsContainer.appendChild(div);
        }
      } catch (e) { console.error('showNodeDetails error', e); }
    }

    async function fetchGraph() {
      try {
        setStatus('Fetching graph...');
        const r = await fetch('/api/graph', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const data = await r.json();
        const nodeCount = data.nodes ? data.nodes.length : 0;
        const edgeCount = data.edges ? data.edges.length : 0;
        const violations = data.edges ? data.edges.filter(e => e.isViolation).length : 0;
        setStatus('Nodes: ' + nodeCount + ', Edges: ' + edgeCount + ', Violations: ' + violations + ' (updated ' + new Date().toLocaleTimeString() + ')');
        setSummary('Nodes: ' + nodeCount + ', Edges: ' + edgeCount + ', Violations: ' + violations);
        updateGraph(data);
      } catch (e) {
        setStatus('Failed to fetch graph: ' + e);
        console.error(e);
      }
    }

    function downloadFromResponse(resp, defaultName) {
      return resp.text().then(text => {
        const blob = new Blob([text], { type: resp.headers.get('Content-Type') || 'text/plain' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = defaultName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 5000);
      });
    }

    async function exportGraph(format) {
      try {
        const url = '/api/export?format=' + encodeURIComponent(format);
        const r = await fetch(url, { cache: 'no-store' });
        if (!r.ok) { const t = await r.text(); alert('Export failed: ' + r.status + ' ' + t); return; }
        const suggested = format === 'dgml' ? 'graph.dgml' : format === 'mermaid' ? 'graph.mmd' : 'graph.dot';
        await downloadFromResponse(r, suggested);
      } catch (e) { console.error('Export error', e); alert('Export error: ' + (e && e.message ? e.message : e)); }
    }

    
    const ebDot = document.getElementById('export-dot');
    const ebMer = document.getElementById('export-mermaid');
    const ebDg = document.getElementById('export-dgml');
    if (ebDot) ebDot.addEventListener('click', () => exportGraph('dot'));
    if (ebMer) ebMer.addEventListener('click', () => exportGraph('mermaid'));
    if (ebDg) ebDg.addEventListener('click', () => exportGraph('dgml'));

    function initOrUpdateCytoscape(elements, hasPositions) {
      if (typeof cytoscape === 'undefined') {
        setStatus('Cytoscape is not loaded. Check network or CDN.');
        console.error('[arcgraph] cytoscape missing');
        return;
      }

      if (!cy) {
        cy = cytoscape({
          container: document.getElementById('cy'),
          elements: elements,
          style: [
            { selector: 'node', style: {
                'label': 'data(label)',
                'text-valign':'center','text-halign':'center',
                'background-color': 'data(fillColor)',
                'width': 'mapData(degree, 0, 50, 24, 80)',
                'height': 'mapData(degree, 0, 50, 24, 80)',
                'border-width': 4, 'border-color': 'data(borderColor)',
                'font-size': 10,'text-wrap': 'wrap','text-max-width': 80
              } },
            { selector: 'node.external', style: { 'opacity': 0.95 } },
            { selector: 'node.vuln', style: { 'border-color': '#d9534f', 'border-width': 8 } },
            { selector: 'node.in-cycle', style: { 'border-color': '#ffcc00', 'border-width': 8 } },
            { selector: 'edge', style: { 'width': 'mapData(weight, 1, 10, 1, 6)', 'line-color': '#999', 'target-arrow-shape': 'triangle', 'target-arrow-color': '#999', 'curve-style': 'bezier' } },
            { selector: 'edge.in-cycle', style: { 'line-color': '#ffcc00', 'target-arrow-color': '#ffcc00', 'width': 4 } },
            { selector: 'edge[isViolation = true]', style: { 'line-color': '#d9534f', 'target-arrow-color': '#d9534f', 'width': 3 } }
          ],
          layout: hasPositions ? { name: 'preset' } : { name: 'cose', animate: true },
          wheelSensitivity: 0.2
        });

        cy.on('tap', 'node', evt => {
          try { showNodeDetails(evt.target.data()); } catch (e) { console.error(e); }
        });

        const cbE = document.getElementById('cb-hide-external');
        const cbV = document.getElementById('cb-hide-vulnerable');
        if (cbE) cbE.addEventListener('change', applyFilters);
        if (cbV) cbV.addEventListener('change', applyFilters);
      } else {
        cy.batch(() => { cy.elements().remove(); cy.add(elements); });
        try {
          if (hasPositions) cy.layout({ name: 'preset' }).run();
          else cy.layout({ name: 'cose', animate: true }).run();
        } catch (e) { console.warn(e); }
        applyFilters();
      }
    }

    function updateGraph(data) {
      try {
        if (!data) { setStatus('No graph data'); return; }
        const elements = [];
        let hasPositions = false;
        (data.nodes || []).forEach(n => {
          const nd = {
            id: n.id,
            label: n.label || n.id,
            group: n.group || '',
            isVulnerable: !!n.isVulnerable,
            isExternal: !!n.isExternal,
            isInCycle: !!n.isInCycle,
            packageId: n.packageId || '',
            packageVersion: n.packageVersion || '',
            methodCount: n.methodCount || 0,
            propertyCount: n.propertyCount || 0,
            fieldCount: n.fieldCount || 0,
            sourceFiles: n.sourceFiles || [],
            vulnerabilities: n.vulnerabilities || [],
            degree: n.degree || 0,
            cluster: n.cluster || 0
          };
          nd.fillColor = colorForGroup(nd.group);
          nd.borderColor = clusterColor(nd.cluster);
          const classes = [];
          if (nd.group) classes.push('layer-' + nd.group.toLowerCase());
          classes.push(nd.isExternal ? 'external' : 'internal');
          if (nd.isVulnerable) classes.push('vuln');
          if (nd.isInCycle) classes.push('in-cycle');
          const el = { data: nd, classes: classes.join(' ') };
          if (typeof n.x === 'number' && typeof n.y === 'number') { hasPositions = true; el.position = { x: n.x, y: n.y }; }
          elements.push(el);
        });

        (data.edges || []).forEach(e => {
          const edgeClasses = [];
          if (e.isInCycle) edgeClasses.push('in-cycle');
          elements.push({ data: { id: (e.source + '-' + e.target), source: e.source, target: e.target, weight: e.weight || 1, isViolation: !!e.isViolation, kind: e.kind || '' }, classes: edgeClasses.join(' ') });
        });

        initOrUpdateCytoscape(elements, hasPositions);
      } catch (err) {
        console.error('[arcgraph] updateGraph error:', err);
        setStatus('Error updating graph: ' + (err && err.message ? err.message : err));
      }
    }

    function startPolling(intervalMs = 2000) { if (pollHandle) return; pollHandle = setInterval(fetchGraph, intervalMs); }
    function stopPolling() { if (pollHandle) { clearInterval(pollHandle); pollHandle = null; } }

    function setupSse() {
      try {
        const es = new EventSource('/api/stream');
        let connected = false;
        es.onopen = function() { connected = true; stopPolling(); };
        es.onmessage = function(e) { fetchGraph(); };
        es.onerror = function(err) { try { es.close(); } catch {} startPolling(2000); };
        setTimeout(() => { if (!connected) startPolling(2000); }, 800);
      } catch (ex) { startPolling(2000); }
    }

  
    fetchGraph();
    setupSse();
  </script>
</body>
</html>";
        }
    }
}