using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace ClaudeBridge
{
    /// <summary>
    /// Raccoglie i messaggi della console e gli errori di compilazione.
    /// Lo stato viene salvato in SessionState così sopravvive al ricaricamento degli script.
    /// </summary>
    [InitializeOnLoad]
    internal static class BridgeLog
    {
        const string StoreKey = "ClaudeBridge.LogStore";
        const int MaxEntries = 1000;

        [Serializable]
        class LogEntry
        {
            public int id;
            public string type;
            public string time;
            public string message;
            public string stack;
        }

        [Serializable]
        class CompileMessage
        {
            public string type;
            public string message;
            public string file;
            public int line;
            public int column;
        }

        [Serializable]
        class Store
        {
            public int lastId;
            public int compileId;
            public bool compiling;
            public string compileFinished = "";
            public List<LogEntry> entries = new List<LogEntry>();
            public List<CompileMessage> compile = new List<CompileMessage>();
        }

        static readonly object gate = new object();
        static readonly Store store;

        public static int LastId { get { lock (gate) return store.lastId; } }
        public static int CompileId { get { lock (gate) return store.compileId; } }
        public static int CompileErrorCount { get { lock (gate) return CountErrors(); } }

        static BridgeLog()
        {
            var saved = SessionState.GetString(StoreKey, "");
            store = string.IsNullOrEmpty(saved) ? new Store() : JsonUtility.FromJson<Store>(saved) ?? new Store();
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;

            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.compilationStarted += _ =>
            {
                lock (gate)
                {
                    store.compiling = true;
                    store.compile.Clear();
                }
            };
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += _ =>
            {
                lock (gate)
                {
                    store.compiling = false;
                    store.compileId++;
                    store.compileFinished = DateTime.Now.ToString("HH:mm:ss");
                }
                Save();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Save;
        }

        static void Save()
        {
            string json;
            lock (gate) json = JsonUtility.ToJson(store);
            SessionState.SetString(StoreKey, json);
        }

        static void OnLog(string message, string stack, LogType type)
        {
            bool withStack = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            lock (gate)
            {
                store.entries.Add(new LogEntry
                {
                    id = ++store.lastId,
                    type = type.ToString(),
                    time = DateTime.Now.ToString("HH:mm:ss"),
                    message = Truncate(message, 2000),
                    stack = withStack ? Truncate(stack, 1500) : ""
                });
                if (store.entries.Count > MaxEntries)
                    store.entries.RemoveRange(0, store.entries.Count - MaxEntries);
            }
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            lock (gate)
            {
                foreach (var m in messages)
                {
                    if (m.type == CompilerMessageType.Info) continue;
                    store.compile.Add(new CompileMessage
                    {
                        type = m.type == CompilerMessageType.Error ? "error" : "warning",
                        message = m.message,
                        file = m.file,
                        line = m.line,
                        column = m.column
                    });
                }
            }
        }

        static int CountErrors()
        {
            int n = 0;
            foreach (var m in store.compile)
                if (m.type == "error") n++;
            return n;
        }

        static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max) + "…";

        static bool Matches(string entryType, string filter)
        {
            switch (filter)
            {
                case "error": return entryType == "Error" || entryType == "Exception" || entryType == "Assert";
                case "warning": return entryType == "Warning";
                case "log": return entryType == "Log";
                case "problems": return entryType != "Log";
                default: return true;
            }
        }

        public static object GetLogs(int since, string filter, int limit)
        {
            var list = new List<object>();
            int lastId;
            lock (gate)
            {
                lastId = store.lastId;
                for (int i = store.entries.Count - 1; i >= 0 && list.Count < limit; i--)
                {
                    var e = store.entries[i];
                    if (e.id <= since) break;
                    if (!Matches(e.type, filter)) continue;
                    list.Add(Json.Obj("id", e.id, "type", e.type, "time", e.time, "message", e.message, "stack", e.stack));
                }
            }
            list.Reverse();
            return Json.Obj("lastId", lastId, "entries", list);
        }

        public static object Clear()
        {
            lock (gate) store.entries.Clear();
            return Json.Obj("text", "log del bridge svuotati");
        }

        public static object GetCompileReport()
        {
            var errors = new List<object>();
            var warnings = new List<object>();
            lock (gate)
            {
                foreach (var m in store.compile)
                {
                    var item = Json.Obj("message", m.message, "file", m.file, "line", m.line, "column", m.column);
                    if (m.type == "error") errors.Add(item);
                    else if (warnings.Count < 30) warnings.Add(item);
                }
                return Json.Obj(
                    "compiling", store.compiling,
                    "compileId", store.compileId,
                    "finishedAt", store.compileFinished,
                    "errors", errors,
                    "warnings", warnings);
            }
        }
    }
}
