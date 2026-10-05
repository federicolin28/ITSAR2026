using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClaudeBridge
{
    internal sealed class BridgeException : Exception
    {
        public BridgeException(string message) : base(message) { }
    }

    // Risposta non JSON (pagina HTML, immagine, JSON già serializzato)
    internal sealed class RawResponse
    {
        public byte[] Body;
        public string ContentType;

        public static RawResponse Text(string text, string contentType) =>
            new RawResponse { Body = Encoding.UTF8.GetBytes(text), ContentType = contentType };
    }

    internal sealed class Req
    {
        public string Path;
        public NameValueCollection Query;

        public string Get(string key, string fallback = null)
        {
            var v = Query[key];
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        public string Need(string key) =>
            Get(key) ?? throw new BridgeException($"parametro mancante: {key}");

        public int GetInt(string key, int fallback) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public bool GetBool(string key, bool fallback = false)
        {
            var v = Get(key);
            if (v == null) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public string[] GetAll(string key) => Query.GetValues(key) ?? new string[0];
    }

    /// <summary>
    /// Server HTTP su 127.0.0.1 che vive dentro l'Editor. Le richieste arrivano su un thread
    /// secondario e vengono eseguite sul main thread di Unity tramite EditorApplication.update.
    /// </summary>
    [InitializeOnLoad]
    internal static class BridgeServer
    {
        public const int DefaultPort = 7777;
        const string PortKey = "ClaudeBridge.Port";
        const string ReloadKey = "ClaudeBridge.ReloadId";
        const string AnnouncedKey = "ClaudeBridge.Announced";
        const string HeaderName = "X-Claude-Bridge";

        sealed class Job
        {
            public Func<object> Fn;
            public object Result;
            public Exception Error;
            public volatile bool Cancelled;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        static HttpListener listener;
        static readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
        static int inFlight;
        static double nextStatusTime;
        static volatile string statusJson = "{}";

        public static int Port { get; private set; }
        public static int ReloadId { get; private set; }
        public static string ProjectRoot { get; private set; }
        public static string BridgeDir => Path.Combine(ProjectRoot, "Library", "ClaudeBridge");
        static long lastPumpTicks = DateTime.UtcNow.Ticks;

        // Millisecondi dall'ultimo aggiornamento del main thread: valori alti = Editor occupato o bloccato
        public static long MainThreadIdleMs =>
            (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastPumpTicks)) / TimeSpan.TicksPerMillisecond;

        public static string StatusJson
        {
            get
            {
                var s = statusJson;
                return s.Length > 2 ? s.Substring(0, s.Length - 1) + ",\"mainThreadIdleMs\":" + MainThreadIdleMs + "}" : s;
            }
        }

        static BridgeServer()
        {
            ProjectRoot = Directory.GetParent(Application.dataPath).FullName;

            // Unity 6 importa gli asset in processi secondari che caricano anche gli script Editor:
            // il server deve girare solo nell'Editor principale
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;

            ReloadId = SessionState.GetInt(ReloadKey, 0) + 1;
            SessionState.SetInt(ReloadKey, ReloadId);

            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += OnQuit;
            Start();
        }

        static void Start()
        {
            if (listener != null) return;
            int preferred = SessionState.GetInt(PortKey, EditorPrefs.GetInt(PortKey, DefaultPort));
            for (int p = preferred; p < preferred + 20; p++)
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{p}/");
                l.Prefixes.Add($"http://localhost:{p}/");
                try
                {
                    l.Start();
                }
                catch (Exception)
                {
                    try { l.Close(); } catch { /* porta occupata */ }
                    continue;
                }
                listener = l;
                Port = p;
                break;
            }

            if (listener == null)
            {
                Debug.LogWarning($"[ClaudeBridge] Nessuna porta libera tra {preferred} e {preferred + 19}.");
                return;
            }

            SessionState.SetInt(PortKey, Port);
            WriteServerFile();
            UpdateStatus();

            var thread = new Thread(ListenLoop) { IsBackground = true, Name = "ClaudeBridge" };
            thread.Start(listener);

            if (!SessionState.GetBool(AnnouncedKey, false))
            {
                SessionState.SetBool(AnnouncedKey, true);
                Debug.Log($"[ClaudeBridge] In ascolto su http://127.0.0.1:{Port}/  (menu Tools > Claude Bridge)");
            }
        }

        static void Stop()
        {
            var l = listener;
            listener = null;
            if (l == null) return;

            // Sblocca le richieste in coda: il main thread sta per ricaricare il dominio
            while (jobs.TryDequeue(out var job))
            {
                job.Error = new BridgeException("Unity sta ricaricando gli script: riprova tra poco.");
                job.Done.Set();
            }

            // Lascia il tempo alle risposte già pronte di essere spedite
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (Volatile.Read(ref inFlight) > 0 && DateTime.UtcNow < until) Thread.Sleep(10);

            try { l.Stop(); l.Close(); } catch { /* già chiuso */ }
        }

        static void OnQuit()
        {
            Stop();
            try { File.Delete(Path.Combine(BridgeDir, "server.json")); } catch { /* ignorato */ }
        }

        [MenuItem("Tools/Claude Bridge/Apri dashboard nel browser")]
        static void OpenDashboard() => Application.OpenURL($"http://127.0.0.1:{Port}/");

        [MenuItem("Tools/Claude Bridge/Riavvia server")]
        static void Restart()
        {
            Stop();
            Start();
            Debug.Log($"[ClaudeBridge] Server riavviato su http://127.0.0.1:{Port}/");
        }

        static void WriteServerFile()
        {
            Directory.CreateDirectory(BridgeDir);
            var json = Json.Serialize(Json.Obj(
                "port", Port,
                "url", $"http://127.0.0.1:{Port}/",
                "pid", System.Diagnostics.Process.GetCurrentProcess().Id,
                "project", ProjectRoot,
                "unityVersion", Application.unityVersion));
            File.WriteAllText(Path.Combine(BridgeDir, "server.json"), json);
        }

        // ---------- main thread ----------

        static void Pump()
        {
            Interlocked.Exchange(ref lastPumpTicks, DateTime.UtcNow.Ticks);
            if (EditorApplication.timeSinceStartup >= nextStatusTime)
            {
                nextStatusTime = EditorApplication.timeSinceStartup + 0.2;
                UpdateStatus();
            }

            // Limite per frame per non bloccare l'Editor
            for (int i = 0; i < 20 && jobs.TryDequeue(out var job); i++)
            {
                if (job.Cancelled) continue;
                try { job.Result = job.Fn(); }
                catch (Exception e) { job.Error = e; }
                finally { job.Done.Set(); }
            }
        }

        static void UpdateStatus()
        {
            var scene = SceneManager.GetActiveScene();
            statusJson = Json.Serialize(Json.Obj(
                "ok", true,
                "port", Port,
                "project", ProjectRoot,
                "unityVersion", Application.unityVersion,
                "isCompiling", EditorApplication.isCompiling,
                "isUpdating", EditorApplication.isUpdating,
                "isPlaying", EditorApplication.isPlaying,
                "isPaused", EditorApplication.isPaused,
                "activeScene", scene.path,
                "sceneDirty", scene.isDirty,
                "compileId", BridgeLog.CompileId,
                "compileErrors", BridgeLog.CompileErrorCount,
                "reloadId", ReloadId,
                "lastLogId", BridgeLog.LastId));
        }

        static object RunOnMainThread(Func<object> fn, int timeoutMs)
        {
            var job = new Job { Fn = fn };
            jobs.Enqueue(job);
            if (!job.Done.Wait(timeoutMs))
            {
                job.Cancelled = true;
                throw new BridgeException($"Timeout: il main thread di Unity è fermo da {MainThreadIdleMs / 1000} s. C'è una finestra modale aperta, l'Editor sta importando o è bloccato?");
            }
            if (job.Error != null) ExceptionDispatchInfo.Capture(job.Error).Throw();
            return job.Result;
        }

        // ---------- thread di ascolto ----------

        static void ListenLoop(object state)
        {
            var l = (HttpListener)state;
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = l.GetContext(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            Interlocked.Increment(ref inFlight);
            try
            {
                var rq = ctx.Request;

                // Protezione da DNS rebinding: accetta solo host locali
                string host = rq.Url.Host;
                if (host != "127.0.0.1" && host != "localhost")
                {
                    SendJson(ctx, 403, Json.Obj("ok", false, "error", "host non permesso"));
                    return;
                }

                string raw = rq.RawUrl ?? "/";
                int qi = raw.IndexOf('?');
                string path = (qi >= 0 ? raw.Substring(0, qi) : raw).Trim('/').ToLowerInvariant();
                var query = new NameValueCollection();
                if (qi >= 0) ParseForm(raw.Substring(qi + 1), query);
                if (rq.HasEntityBody)
                {
                    string body;
                    using (var sr = new StreamReader(rq.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
                    if (rq.ContentType != null && rq.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                        ParseForm(body, query);
                }

                if (path.Length == 0 || path == "index.html")
                {
                    Send(ctx, 200, RawResponse.Text(Dashboard.Html, "text/html; charset=utf-8"));
                    return;
                }

                // Un header personalizzato blocca le richieste cross-site fatte da altre pagine web
                if (rq.Headers[HeaderName] == null)
                {
                    SendJson(ctx, 403, Json.Obj("ok", false, "error", $"header {HeaderName} mancante"));
                    return;
                }

                if (!BridgeCommands.TryGet(path, out var cmd))
                {
                    SendJson(ctx, 404, Json.Obj("ok", false, "error", $"comando sconosciuto: {path}. Usa 'help' per l'elenco."));
                    return;
                }

                var req = new Req { Path = path, Query = query };
                object result = cmd.AnyThread
                    ? cmd.Fn(req)
                    : RunOnMainThread(() => cmd.Fn(req), cmd.TimeoutMs);

                if (result is RawResponse rr) Send(ctx, 200, rr);
                else SendJson(ctx, 200, result);
            }
            catch (BridgeException e)
            {
                SendJson(ctx, 400, Json.Obj("ok", false, "error", e.Message));
            }
            catch (Exception e)
            {
                SendJson(ctx, 500, Json.Obj("ok", false, "error", $"{e.GetType().Name}: {e.Message}", "stack", e.StackTrace));
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }

        static void ParseForm(string s, NameValueCollection into)
        {
            foreach (var pair in s.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = eq >= 0 ? pair.Substring(0, eq) : pair;
                string v = eq >= 0 ? pair.Substring(eq + 1) : "";
                into.Add(Decode(k), Decode(v));
            }
        }

        static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        static void SendJson(HttpListenerContext ctx, int status, object payload)
        {
            if (payload is Dictionary<string, object> d && !d.ContainsKey("ok"))
            {
                var withOk = new Dictionary<string, object> { ["ok"] = true };
                foreach (var kv in d) withOk[kv.Key] = kv.Value;
                payload = withOk;
            }
            Send(ctx, status, RawResponse.Text(Json.Serialize(payload), "application/json; charset=utf-8"));
        }

        static void Send(HttpListenerContext ctx, int status, RawResponse r)
        {
            try
            {
                var resp = ctx.Response;
                resp.StatusCode = status;
                resp.ContentType = r.ContentType;
                resp.Headers["Cache-Control"] = "no-store";
                resp.ContentLength64 = r.Body.Length;
                resp.OutputStream.Write(r.Body, 0, r.Body.Length);
                resp.OutputStream.Close();
            }
            catch
            {
                // Il client ha chiuso la connessione
            }
        }
    }
}
