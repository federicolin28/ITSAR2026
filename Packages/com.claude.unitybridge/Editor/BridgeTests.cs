using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
#if CLAUDE_BRIDGE_TESTS
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
#endif

namespace ClaudeBridge
{
    /// <summary>
    /// Avvia i test del Test Framework e scrive il risultato in Library/ClaudeBridge/test-results.json,
    /// così il risultato sopravvive al ricaricamento degli script dei test PlayMode.
    /// </summary>
    [InitializeOnLoad]
    internal static class BridgeTests
    {
        static string ResultPath => Path.Combine(BridgeServer.BridgeDir, "test-results.json");

        public static object Result(Req r)
        {
            if (!File.Exists(ResultPath)) return Json.Obj("status", "none", "text", "nessun test eseguito");
            return RawResponse.Text(File.ReadAllText(ResultPath), "application/json; charset=utf-8");
        }

        static void WriteResult(Dictionary<string, object> data)
        {
            Directory.CreateDirectory(BridgeServer.BridgeDir);
            File.WriteAllText(ResultPath, Json.Serialize(data));
        }

        static string CurrentRunId()
        {
            if (!File.Exists(ResultPath)) return "";
            // Il runId è scritto da Run() nella forma "runId":"<guid>"
            var text = File.ReadAllText(ResultPath);
            int i = text.IndexOf("\"runId\":\"", StringComparison.Ordinal);
            if (i < 0) return "";
            i += 9;
            int end = text.IndexOf('"', i);
            return end > i ? text.Substring(i, end - i) : "";
        }

#if CLAUDE_BRIDGE_TESTS
        static readonly TestRunnerApi api;

        static BridgeTests()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;

            // Le callback vanno registrate a ogni caricamento del dominio per i test PlayMode
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.hideFlags = HideFlags.HideAndDontSave;
            api.RegisterCallbacks(new Callbacks());
        }

        public static object Run(Req r)
        {
            if (EditorApplication.isPlaying) throw new BridgeException("Esci dal play mode prima di lanciare i test.");

            // Con scene modificate il Test Runner apre una finestra "Vuoi salvare?" che blocca l'Editor
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                if (!r.GetBool("save"))
                    throw new BridgeException("Ci sono scene con modifiche non salvate: aggiungi save=1 per salvarle prima dei test.");
                if (string.IsNullOrEmpty(scene.path))
                    throw new BridgeException("La scena non ha ancora un file: usa prima 'save path=Assets/Scenes/Nome.unity'.");
                EditorSceneManager.SaveScene(scene);
            }
            var mode = string.Equals(r.Get("mode", "EditMode"), "PlayMode", StringComparison.OrdinalIgnoreCase)
                ? TestMode.PlayMode
                : TestMode.EditMode;
            var filter = new Filter { testMode = mode };
            var pattern = r.Get("filter");
            if (pattern != null) filter.groupNames = new[] { pattern };

            var runId = Guid.NewGuid().ToString("N");
            WriteResult(Json.Obj("runId", runId, "status", "running", "mode", mode.ToString()));
            api.Execute(new ExecutionSettings(filter));
            return Json.Obj("runId", runId, "mode", mode.ToString(), "text", $"test {mode} avviati");
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var failures = new List<object>();
                CollectFailures(result, failures);
                WriteResult(Json.Obj(
                    "runId", CurrentRunId(),
                    "status", "finished",
                    "result", result.TestStatus.ToString(),
                    "passed", result.PassCount,
                    "failed", result.FailCount,
                    "skipped", result.SkipCount,
                    "inconclusive", result.InconclusiveCount,
                    "duration", result.Duration,
                    "failures", failures));
            }

            static void CollectFailures(ITestResultAdaptor r, List<object> into)
            {
                if (r.HasChildren)
                {
                    foreach (var child in r.Children) CollectFailures(child, into);
                    return;
                }
                if (r.TestStatus == TestStatus.Failed)
                    into.Add(Json.Obj("test", r.Test.FullName, "message", r.Message, "stack", r.StackTrace));
            }
        }
#else
        public static object Run(Req r) =>
            throw new BridgeException("Il pacchetto com.unity.test-framework non è installato nel progetto.");
#endif
    }
}
