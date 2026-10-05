namespace ClaudeBridge
{
    // Pagina servita su http://127.0.0.1:<porta>/ per usare il bridge dal browser
    internal static class Dashboard
    {
        public const string Html = @"<!doctype html>
<html lang=""it"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Claude Unity Bridge</title>
<style>
:root { --bg:#f6f6f4; --panel:#fff; --text:#1d1d1b; --muted:#6b6b66; --line:#e2e2dc; --accent:#3b6fd8; --err:#c0392b; --warn:#b7791f; --ok:#2f855a; }
@media (prefers-color-scheme: dark) { :root { --bg:#151514; --panel:#1f1f1d; --text:#ecece8; --muted:#9a9a93; --line:#33332f; --accent:#7aa2f7; --err:#f07167; --warn:#e9b44c; --ok:#68d391; } }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:14px/1.45 system-ui, sans-serif; }
header { display:flex; gap:12px; align-items:center; flex-wrap:wrap; padding:14px 16px; border-bottom:1px solid var(--line); background:var(--panel); }
h1 { font-size:16px; margin:0; }
#status { color:var(--muted); font-size:13px; }
.dot { display:inline-block; width:9px; height:9px; border-radius:50%; background:var(--muted); margin-right:6px; vertical-align:middle; }
main { display:grid; grid-template-columns: minmax(0,1fr) minmax(0,1fr); gap:16px; padding:16px; }
@media (max-width: 900px) { main { grid-template-columns: minmax(0,1fr); } }
section { background:var(--panel); border:1px solid var(--line); border-radius:8px; padding:12px; min-width:0; }
h2 { font-size:13px; text-transform:uppercase; letter-spacing:.04em; color:var(--muted); margin:0 0 8px; }
.row { display:flex; gap:6px; flex-wrap:wrap; margin-bottom:8px; }
button { font:inherit; padding:5px 10px; border:1px solid var(--line); border-radius:6px; background:var(--bg); color:var(--text); cursor:pointer; }
button:hover { border-color:var(--accent); }
input { font:inherit; padding:5px 8px; border:1px solid var(--line); border-radius:6px; background:var(--bg); color:var(--text); min-width:0; flex:1; }
pre { margin:0; white-space:pre-wrap; word-break:break-word; font:12px/1.45 ui-monospace, Consolas, monospace; max-height:60vh; overflow:auto; }
.log-Error,.log-Exception,.log-Assert { color:var(--err); }
.log-Warning { color:var(--warn); }
img { max-width:100%; border-radius:6px; margin-top:8px; }
</style>
</head>
<body>
<header>
  <h1>Claude Unity Bridge</h1>
  <span id=""status""><span class=""dot""></span>connessione…</span>
</header>
<main>
  <section>
    <h2>Comandi</h2>
    <div class=""row"">
      <button onclick=""show('refresh')"">Refresh / compila</button>
      <button onclick=""show('compile')"">Errori compilazione</button>
      <button onclick=""show('play')"">Play</button>
      <button onclick=""show('stop')"">Stop</button>
      <button onclick=""show('scene/save')"">Salva scena</button>
      <button onclick=""show('hierarchy')"">Gerarchia</button>
      <button onclick=""show('scenes')"">Scene</button>
      <button onclick=""shot('camera')"">Screenshot camera</button>
      <button onclick=""shot('scene')"">Screenshot Scene View</button>
      <button onclick=""show('help')"">Aiuto</button>
    </div>
    <div class=""row"">
      <input id=""cmd"" placeholder=""comando, es. inspect"" style=""max-width:140px"">
      <input id=""args"" placeholder=""parametri, es. path=Player&component=PlayerController"">
      <button onclick=""custom()"">Esegui</button>
    </div>
    <pre id=""out""></pre>
    <div id=""img""></div>
  </section>
  <section>
    <h2>Console <button onclick=""clearLogs()"" style=""float:right"">Svuota</button></h2>
    <pre id=""logs""></pre>
  </section>
</main>
<script>
const H = { 'X-Claude-Bridge': 'dashboard', 'Content-Type': 'application/x-www-form-urlencoded' };
async function call(cmd, params) {
  const r = await fetch('/' + cmd, { method: 'POST', headers: H, body: new URLSearchParams(params || {}) });
  const type = r.headers.get('content-type') || '';
  return type.includes('json') ? r.json() : r.blob();
}
function render(d) { return d && d.text !== undefined ? d.text : (d && d.result !== undefined ? String(d.result) : JSON.stringify(d, null, 2)); }
async function show(cmd, params) {
  const out = document.getElementById('out');
  out.textContent = '…';
  try { out.textContent = render(await call(cmd, params)); } catch (e) { out.textContent = 'Errore: ' + e; }
}
function custom() {
  const params = new URLSearchParams(document.getElementById('args').value);
  show(document.getElementById('cmd').value.trim(), Object.fromEntries(params));
}
async function shot(view) {
  const d = await call('screenshot', { view });
  document.getElementById('out').textContent = render(d);
  if (!d.ok) return;
  const blob = await call('screenshot/file');
  document.getElementById('img').innerHTML = '<img alt=""screenshot"" src=""' + URL.createObjectURL(blob) + '"">';
}
let lastId = 0;
async function pollLogs() {
  try {
    const d = await call('logs', { since: lastId, limit: 200 });
    const box = document.getElementById('logs');
    for (const e of d.entries) {
      const line = document.createElement('div');
      line.className = 'log-' + e.type;
      line.textContent = e.time + ' ' + e.type + ': ' + e.message + (e.stack ? '\n' + e.stack : '');
      box.appendChild(line);
    }
    lastId = d.lastId;
    if (d.entries.length) box.scrollTop = box.scrollHeight;
  } catch (e) { /* Unity sta ricaricando */ }
}
async function clearLogs() { await call('logs/clear'); document.getElementById('logs').textContent = ''; }
async function pollStatus() {
  const el = document.getElementById('status');
  try {
    const s = await call('status');
    const state = s.isCompiling ? 'compila…' : s.isPlaying ? (s.isPaused ? 'pausa' : 'play') : 'pronto';
    const color = s.compileErrors ? 'var(--err)' : s.isPlaying ? 'var(--accent)' : 'var(--ok)';
    el.innerHTML = '<span class=""dot"" style=""background:' + color + '""></span>' + state + ' · Unity ' + s.unityVersion + ' · ' + (s.activeScene || 'scena senza nome') + (s.sceneDirty ? ' *' : '') + (s.compileErrors ? ' · ' + s.compileErrors + ' errori di compilazione' : '');
  } catch (e) { el.innerHTML = '<span class=""dot""></span>Unity non raggiungibile (sta ricaricando?)'; }
}
setInterval(pollStatus, 1000); setInterval(pollLogs, 1000); pollStatus(); pollLogs();
</script>
</body>
</html>";
    }
}
