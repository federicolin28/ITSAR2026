using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ClaudeBridge
{
    internal sealed class Command
    {
        public Func<Req, object> Fn;
        public bool AnyThread;
        public int TimeoutMs;
        public string Help;
    }

    /// <summary>
    /// Elenco dei comandi esposti dal bridge. Tutti girano sul main thread salvo quelli marcati AnyThread.
    /// </summary>
    internal static class BridgeCommands
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Dictionary<string, Command> map = new Dictionary<string, Command>();

        static void Add(string name, string help, Func<Req, object> fn, bool anyThread = false, int timeoutMs = 30000) =>
            map[name] = new Command { Fn = fn, AnyThread = anyThread, TimeoutMs = timeoutMs, Help = help };

        public static bool TryGet(string name, out Command cmd) => map.TryGetValue(name, out cmd);

        static BridgeCommands()
        {
            // Lettura stato (rispondono anche mentre Unity compila)
            Add("help", "elenco dei comandi", Help, anyThread: true);
            Add("status", "stato dell'Editor (compilazione, play mode, scena)",
                _ => RawResponse.Text(BridgeServer.StatusJson, "application/json; charset=utf-8"), anyThread: true);
            Add("logs", "console: since=<id> type=all|error|warning|log|problems limit=<n>",
                r => BridgeLog.GetLogs(r.GetInt("since", 0), r.Get("type", "all"), r.GetInt("limit", 100)), anyThread: true);
            Add("logs/clear", "svuota i log raccolti dal bridge e la Console", ClearLogs);
            Add("compile", "errori e warning dell'ultima compilazione", _ => BridgeLog.GetCompileReport(), anyThread: true);

            // Asset e compilazione
            Add("refresh", "AssetDatabase.Refresh (importa i file modificati e ricompila). force=1 forza la ricompilazione", Refresh, timeoutMs: 300000);
            Add("find-assets", "cerca asset: filter='t:Prefab nome' folder=Assets", FindAssets);

            // Play mode
            Add("play", "entra in play mode", _ => { EditorApplication.isPlaying = true; return Json.Obj("text", "play mode richiesto"); });
            Add("stop", "esce dal play mode", _ => { EditorApplication.isPlaying = false; return Json.Obj("text", "uscita dal play mode richiesta"); });
            Add("pause", "pausa/riprendi: value=1|0 (senza value alterna)", Pause);

            // Scene
            Add("scenes", "scene del progetto e Build Settings", Scenes);
            Add("scene/open", "apre una scena: path=Assets/... save=1|discard=1 additive=1", OpenScene);
            Add("scene/save", "salva le scene aperte e gli asset modificati; path=Assets/... per salvare con nome la scena attiva", SaveScenes);
            Add("hierarchy", "albero della scena: path=<radice> depth=<n> components=0|1", Hierarchy);
            Add("inspect", "proprietà serializzate di un GameObject: path=A/B component=<tipo> depth=<n>", Inspect);
            Add("set", "imposta una proprietà: path= component= property= value=", SetProperty);
            Add("create", "crea un GameObject: name= parent= primitive=Cube|Sphere|... position=x,y,z rotation=x,y,z scale=x,y,z", Create);
            Add("instantiate", "istanzia un prefab: asset=Assets/....prefab parent= name= position= rotation= scale=", Instantiate);
            Add("add-component", "aggiunge un componente: path= type=", AddComponent);
            Add("remove-component", "rimuove un componente: path= type=", RemoveComponent);
            Add("delete", "elimina un GameObject: path=", Delete);
            Add("select", "seleziona un GameObject o un asset: path=", Select);
            Add("undo", "annulla l'ultima operazione", _ => { Undo.PerformUndo(); return Json.Obj("text", "undo eseguito"); });

            // Esecuzione
            Add("menu", "esegue una voce di menu: path='File/Save'", Menu);
            Add("exec", "invoca un metodo statico: type=Classe method=Nome arg=... (ripetibile)", Exec, timeoutMs: 300000);
            Add("screenshot", "salva un PNG: view=camera|scene camera=<path> width= height= out=<file>", Screenshot);
            Add("screenshot/file", "restituisce l'ultimo screenshot come PNG", ScreenshotFile, anyThread: true);
            Add("tests/run", "avvia i test: mode=EditMode|PlayMode filter=<regex sul nome>", BridgeTests.Run);
            Add("tests/result", "risultato dell'ultima esecuzione dei test", BridgeTests.Result, anyThread: true);
        }

        static object Help(Req r)
        {
            var sb = new StringBuilder();
            foreach (var kv in map.OrderBy(k => k.Key))
                sb.Append(kv.Key.PadRight(18)).AppendLine(kv.Value.Help);
            return Json.Obj("text", sb.ToString());
        }

        // ---------- console e compilazione ----------

        static object ClearLogs(Req r)
        {
            var logEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor");
            logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
            return BridgeLog.Clear();
        }

        static object Refresh(Req r)
        {
            AssetDatabase.Refresh();
            if (r.GetBool("force")) UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            return Json.Obj("compiling", EditorApplication.isCompiling, "compileId", BridgeLog.CompileId);
        }

        static object FindAssets(Req r)
        {
            var folder = r.Get("folder");
            var guids = folder != null
                ? AssetDatabase.FindAssets(r.Get("filter", ""), new[] { folder })
                : AssetDatabase.FindAssets(r.Get("filter", ""));
            int limit = r.GetInt("limit", 200);
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().Take(limit).ToList();
            var text = string.Join("\n", paths);
            if (guids.Length > limit) text += $"\n… altri {guids.Length - limit} risultati (usa limit=)";
            return Json.Obj("text", text, "count", guids.Length);
        }

        static object Pause(Req r)
        {
            EditorApplication.isPaused = r.Get("value") != null ? r.GetBool("value") : !EditorApplication.isPaused;
            return Json.Obj("text", EditorApplication.isPaused ? "in pausa" : "in esecuzione");
        }

        // ---------- scene ----------

        static IEnumerable<Scene> LoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) yield return s;
            }
        }

        static object Scenes(Req r)
        {
            var sb = new StringBuilder("Aperte:\n");
            foreach (var s in LoadedScenes())
                sb.AppendLine($"  {s.path}{(s.isDirty ? " *modificata*" : "")}{(s == SceneManager.GetActiveScene() ? " (attiva)" : "")}");
            sb.AppendLine("Build Settings:");
            foreach (var s in EditorBuildSettings.scenes)
                sb.AppendLine($"  {(s.enabled ? "[x]" : "[ ]")} {s.path}");
            sb.AppendLine("Tutte le scene in Assets:");
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
                sb.AppendLine("  " + AssetDatabase.GUIDToAssetPath(guid));
            return Json.Obj("text", sb.ToString());
        }

        static object OpenScene(Req r)
        {
            if (EditorApplication.isPlaying) throw new BridgeException("Esci dal play mode prima di aprire una scena.");
            var path = r.Need("path");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new BridgeException($"scena non trovata: {path}");

            if (LoadedScenes().Any(s => s.isDirty))
            {
                if (r.GetBool("save")) SaveOpenScenesOrThrow();
                else if (!r.GetBool("discard"))
                    throw new BridgeException("Ci sono scene con modifiche non salvate: aggiungi save=1 per salvarle o discard=1 per scartarle.");
            }
            var mode = r.GetBool("additive") ? OpenSceneMode.Additive : OpenSceneMode.Single;
            EditorSceneManager.OpenScene(path, mode);
            return Json.Obj("text", $"aperta {path}");
        }

        static object SaveScenes(Req r)
        {
            if (EditorApplication.isPlaying) throw new BridgeException("In play mode le modifiche alla scena non si possono salvare.");
            var newPath = r.Get("path");
            if (newPath != null)
            {
                // "Salva con nome" della scena attiva, utile per le scene mai salvate
                if (!newPath.EndsWith(".unity")) newPath += ".unity";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(BridgeServer.ProjectRoot, newPath)));
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), newPath))
                    throw new BridgeException($"impossibile salvare in {newPath}");
            }
            SaveOpenScenesOrThrow();
            AssetDatabase.SaveAssets();
            return Json.Obj("text", "scene e asset salvati");
        }

        static void SaveOpenScenesOrThrow()
        {
            // Una scena mai salvata aprirebbe la finestra "Salva con nome" e bloccherebbe l'Editor
            if (LoadedScenes().Any(s => s.isDirty && string.IsNullOrEmpty(s.path)))
                throw new BridgeException("La scena non ha ancora un file: usa 'save path=Assets/Scenes/Nome.unity'.");
            EditorSceneManager.SaveOpenScenes();
        }

        static GameObject FindGameObject(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new BridgeException("parametro mancante: path");
            var parts = path.Trim('/').Split('/');

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var t = Descend(stage.prefabContentsRoot.transform, parts, 0);
                if (t != null) return t.gameObject;
            }

            foreach (var scene in LoadedScenes())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var t = Descend(root.transform, parts, 0);
                    if (t != null) return t.gameObject;
                }
            }
            throw new BridgeException($"GameObject non trovato: '{path}'. Usa 'hierarchy' per vedere i percorsi (es. Environment/Ground).");
        }

        static Transform Descend(Transform t, string[] parts, int i)
        {
            if (t.name != parts[i]) return null;
            if (i == parts.Length - 1) return t;
            foreach (Transform child in t)
            {
                var found = Descend(child, parts, i + 1);
                if (found != null) return found;
            }
            return null;
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static object Hierarchy(Req r)
        {
            int maxDepth = r.GetInt("depth", 50);
            bool components = r.GetBool("components", true);
            var sb = new StringBuilder();
            var rootPath = r.Get("path");
            if (rootPath != null)
            {
                Walk(FindGameObject(rootPath).transform, 0, maxDepth, components, sb);
            }
            else
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                {
                    sb.AppendLine($"# Prefab in modifica: {stage.assetPath}");
                    Walk(stage.prefabContentsRoot.transform, 0, maxDepth, components, sb);
                }
                foreach (var s in LoadedScenes())
                {
                    sb.AppendLine($"# Scena: {s.name} ({s.path}){(s.isDirty ? " *modificata*" : "")}");
                    foreach (var root in s.GetRootGameObjects()) Walk(root.transform, 0, maxDepth, components, sb);
                }
            }
            return Json.Obj("text", sb.ToString());
        }

        static void Walk(Transform t, int depth, int maxDepth, bool components, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" (inattivo)");
            if (components)
            {
                var names = t.GetComponents<Component>()
                    .Where(c => !(c is Transform))
                    .Select(c => c == null ? "MissingScript" : c.GetType().Name)
                    .ToList();
                if (names.Count > 0) sb.Append(" [").Append(string.Join(", ", names)).Append(']');
            }
            if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                sb.Append(" {prefab: ").Append(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)).Append('}');
            sb.AppendLine();

            if (t.childCount == 0) return;
            if (depth + 1 > maxDepth)
            {
                sb.Append(' ', (depth + 1) * 2).AppendLine($"… {t.childCount} figli");
                return;
            }
            foreach (Transform child in t) Walk(child, depth + 1, maxDepth, components, sb);
        }

        // ---------- ispezione e modifica ----------

        static bool TypeMatches(Type t, string name) =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            (t.FullName != null && t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));

        static bool IsOfTypeName(Type t, string name)
        {
            for (var x = t; x != null; x = x.BaseType)
                if (TypeMatches(x, name)) return true;
            return false;
        }

        static Component FindComponent(GameObject go, string typeName)
        {
            var c = go.GetComponents<Component>().FirstOrDefault(x => x != null && TypeMatches(x.GetType(), typeName))
                    ?? go.GetComponents<Component>().FirstOrDefault(x => x != null && IsOfTypeName(x.GetType(), typeName));
            if (c == null)
            {
                var have = string.Join(", ", go.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name));
                throw new BridgeException($"componente '{typeName}' non presente su {go.name}. Presenti: {have}");
            }
            return c;
        }

        static object Inspect(Req r)
        {
            var go = FindGameObject(r.Need("path"));
            var filter = r.Get("component");
            int maxDepth = r.GetInt("depth", 3);
            var sb = new StringBuilder();
            sb.AppendLine($"{PathOf(go.transform)}  active={go.activeSelf} tag={go.tag} layer={LayerMask.LayerToName(go.layer)} static={go.isStatic}");

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    sb.AppendLine("- (Missing Script)");
                    continue;
                }
                if (filter != null && !IsOfTypeName(c.GetType(), filter)) continue;
                sb.Append("- ").Append(c.GetType().Name);
                if (c is Behaviour b) sb.Append(b.enabled ? "" : " (disabilitato)");
                sb.AppendLine();
                DumpProperties(new SerializedObject(c), sb, maxDepth);
            }
            return Json.Obj("text", sb.ToString());
        }

        static void DumpProperties(SerializedObject so, StringBuilder sb, int maxDepth)
        {
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                sb.Append(' ', 4 + it.depth * 2)
                  .Append(it.propertyPath).Append(" (").Append(it.propertyType == SerializedPropertyType.Generic ? it.type : it.propertyType.ToString())
                  .Append(") = ").AppendLine(ValueString(it));
                if (it.propertyType == SerializedPropertyType.Generic && it.hasVisibleChildren && it.depth < maxDepth)
                    enter = !(it.isArray && it.arraySize > 30);
            }
        }

        static string F(float f) => f.ToString("0.####", Inv);

        static string ValueString(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    return p.longValue.ToString(Inv);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return F(p.floatValue);
                case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
                case SerializedPropertyType.Character: return "'" + (char)p.intValue + "'";
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : p.intValue.ToString(Inv);
                case SerializedPropertyType.Color:
                    var c = p.colorValue;
                    return $"{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}";
                case SerializedPropertyType.Vector2: return $"{F(p.vector2Value.x)},{F(p.vector2Value.y)}";
                case SerializedPropertyType.Vector3: return $"{F(p.vector3Value.x)},{F(p.vector3Value.y)},{F(p.vector3Value.z)}";
                case SerializedPropertyType.Vector4: return $"{F(p.vector4Value.x)},{F(p.vector4Value.y)},{F(p.vector4Value.z)},{F(p.vector4Value.w)}";
                case SerializedPropertyType.Vector2Int: return $"{p.vector2IntValue.x},{p.vector2IntValue.y}";
                case SerializedPropertyType.Vector3Int: return $"{p.vector3IntValue.x},{p.vector3IntValue.y},{p.vector3IntValue.z}";
                case SerializedPropertyType.Quaternion:
                    var e = p.quaternionValue.eulerAngles;
                    return $"euler {F(e.x)},{F(e.y)},{F(e.z)}";
                case SerializedPropertyType.Rect:
                    var rc = p.rectValue;
                    return $"x={F(rc.x)} y={F(rc.y)} w={F(rc.width)} h={F(rc.height)}";
                case SerializedPropertyType.Bounds:
                    return $"center {p.boundsValue.center} size {p.boundsValue.size}";
                case SerializedPropertyType.ObjectReference: return Describe(p.objectReferenceValue);
                case SerializedPropertyType.AnimationCurve: return $"curva ({p.animationCurveValue?.length ?? 0} chiavi)";
                case SerializedPropertyType.Generic: return p.isArray ? $"array[{p.arraySize}]" : "{…}";
                default: return p.propertyType.ToString();
            }
        }

        static string Describe(Object o)
        {
            if (o == null) return "null";
            var type = o.GetType().Name;
            if (AssetDatabase.Contains(o))
            {
                var path = AssetDatabase.GetAssetPath(o);
                return AssetDatabase.IsMainAsset(o) ? $"{path} ({type})" : $"{path}::{o.name} ({type})";
            }
            if (o is Component c) return $"scena:{PathOf(c.transform)} ({type})";
            if (o is GameObject g) return $"scena:{PathOf(g.transform)} (GameObject)";
            return $"{o.name} ({type})";
        }

        static object SetProperty(Req r)
        {
            var go = FindGameObject(r.Need("path"));
            var compName = r.Need("component");
            var propPath = r.Need("property");
            var value = r.Query["value"] ?? "";

            Object target = compName.Equals("GameObject", StringComparison.OrdinalIgnoreCase) ? (Object)go : FindComponent(go, compName);
            var so = new SerializedObject(target);
            var p = so.FindProperty(propPath)
                    ?? so.FindProperty("m_" + char.ToUpperInvariant(propPath[0]) + propPath.Substring(1));
            if (p == null)
                throw new BridgeException($"proprietà '{propPath}' non trovata su {target.GetType().Name}. Usa 'inspect' per vedere i nomi esatti.");

            Assign(p, value);
            so.ApplyModifiedProperties();

            if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null && value != "null" && value != "")
                throw new BridgeException($"'{value}' non è compatibile con il tipo del campo ({p.type}).");

            return Json.Obj("text", $"{PathOf(go.transform)} {target.GetType().Name}.{p.propertyPath} = {ValueString(p)}");
        }

        static float[] Floats(string s, int min)
        {
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < min) throw new BridgeException($"servono almeno {min} numeri separati da virgola: '{s}'");
            return parts.Select(x => float.Parse(x, NumberStyles.Float, Inv)).ToArray();
        }

        static void Assign(SerializedProperty p, string v)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    p.longValue = long.Parse(v, Inv);
                    break;
                case SerializedPropertyType.Boolean:
                    p.boolValue = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case SerializedPropertyType.Float:
                    p.floatValue = float.Parse(v, NumberStyles.Float, Inv);
                    break;
                case SerializedPropertyType.String:
                    p.stringValue = v;
                    break;
                case SerializedPropertyType.Character:
                    p.intValue = v.Length > 0 ? v[0] : 0;
                    break;
                case SerializedPropertyType.Enum:
                {
                    int idx = Array.FindIndex(p.enumNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = Array.FindIndex(p.enumDisplayNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) p.enumValueIndex = idx;
                    else if (int.TryParse(v, out var n)) p.intValue = n;
                    else throw new BridgeException($"valore enum non valido '{v}'. Ammessi: {string.Join(", ", p.enumNames)}");
                    break;
                }
                case SerializedPropertyType.Color:
                    if (v.StartsWith("#"))
                    {
                        if (!ColorUtility.TryParseHtmlString(v, out var col)) throw new BridgeException($"colore non valido: {v}");
                        p.colorValue = col;
                    }
                    else
                    {
                        var c = Floats(v, 3);
                        p.colorValue = new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);
                    }
                    break;
                case SerializedPropertyType.Vector2:
                {
                    var f = Floats(v, 2);
                    p.vector2Value = new Vector2(f[0], f[1]);
                    break;
                }
                case SerializedPropertyType.Vector3:
                {
                    var f = Floats(v, 3);
                    p.vector3Value = new Vector3(f[0], f[1], f[2]);
                    break;
                }
                case SerializedPropertyType.Vector4:
                {
                    var f = Floats(v, 4);
                    p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]);
                    break;
                }
                case SerializedPropertyType.Vector2Int:
                {
                    var f = Floats(v, 2);
                    p.vector2IntValue = new Vector2Int((int)f[0], (int)f[1]);
                    break;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    var f = Floats(v, 3);
                    p.vector3IntValue = new Vector3Int((int)f[0], (int)f[1], (int)f[2]);
                    break;
                }
                case SerializedPropertyType.Quaternion:
                {
                    // Tre valori = angoli di Eulero, quattro = x,y,z,w
                    var f = Floats(v, 3);
                    p.quaternionValue = f.Length >= 4 ? new Quaternion(f[0], f[1], f[2], f[3]) : Quaternion.Euler(f[0], f[1], f[2]);
                    break;
                }
                case SerializedPropertyType.Rect:
                {
                    var f = Floats(v, 4);
                    p.rectValue = new Rect(f[0], f[1], f[2], f[3]);
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                    p.objectReferenceValue = ResolveReference(v, p.type);
                    break;
                default:
                    throw new BridgeException($"tipo {p.propertyType} non supportato da 'set': scrivi uno script Editor e usa 'exec'.");
            }
        }

        // v: "null", "Assets/....", "Assets/x.png::NomeSprite" oppure "scena:Percorso/Oggetto"
        static Object ResolveReference(string v, string fieldType)
        {
            if (v == "" || v == "null") return null;

            // p.type ha la forma "PPtr<$Material>"
            string wanted = fieldType.StartsWith("PPtr<") ? fieldType.Substring(5).TrimEnd('>').TrimStart('$') : "Object";

            var candidates = new List<Object>();
            if (v.StartsWith("scena:") || v.StartsWith("scene:"))
            {
                var go = FindGameObject(v.Substring(6));
                candidates.Add(go);
                candidates.AddRange(go.GetComponents<Component>());
            }
            else
            {
                string path = v, sub = null;
                int sep = v.IndexOf("::", StringComparison.Ordinal);
                if (sep >= 0)
                {
                    path = v.Substring(0, sep);
                    sub = v.Substring(sep + 2);
                }
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                if (all == null || all.Length == 0)
                {
                    // Non è un asset: prova come percorso in scena
                    var go = FindGameObject(v);
                    candidates.Add(go);
                    candidates.AddRange(go.GetComponents<Component>());
                }
                else
                {
                    var main = AssetDatabase.LoadMainAssetAtPath(path);
                    candidates.Add(main);
                    candidates.AddRange(all.Where(a => a != main && (sub == null || a.name == sub)));
                    if (sub != null) candidates.RemoveAt(0);
                }
            }

            var match = candidates.FirstOrDefault(c => c != null && (wanted == "Object" || IsOfTypeName(c.GetType(), wanted)));
            if (match == null)
                throw new BridgeException($"nessun oggetto di tipo {wanted} trovato in '{v}'");
            return match;
        }

        static void ApplyTransform(Transform t, Req r)
        {
            if (r.Get("position") != null) { var f = Floats(r.Get("position"), 3); t.localPosition = new Vector3(f[0], f[1], f[2]); }
            if (r.Get("rotation") != null) { var f = Floats(r.Get("rotation"), 3); t.localEulerAngles = new Vector3(f[0], f[1], f[2]); }
            if (r.Get("scale") != null) { var f = Floats(r.Get("scale"), 3); t.localScale = new Vector3(f[0], f[1], f[2]); }
        }

        static object Create(Req r)
        {
            GameObject go;
            var prim = r.Get("primitive");
            if (prim != null)
            {
                if (!Enum.TryParse(prim, true, out PrimitiveType pt))
                    throw new BridgeException($"primitive non valida: {prim}. Ammesse: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}");
                go = GameObject.CreatePrimitive(pt);
                if (r.Get("name") != null) go.name = r.Get("name");
            }
            else
            {
                go = new GameObject(r.Get("name", "GameObject"));
            }
            Undo.RegisterCreatedObjectUndo(go, "Claude: crea " + go.name);
            if (r.Get("parent") != null) Undo.SetTransformParent(go.transform, FindGameObject(r.Get("parent")).transform, "Claude: parent");
            ApplyTransform(go.transform, r);
            return Json.Obj("text", "creato " + PathOf(go.transform), "path", PathOf(go.transform));
        }

        static object Instantiate(Req r)
        {
            var assetPath = r.Need("asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
                         ?? throw new BridgeException($"prefab non trovato: {assetPath}");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(go, "Claude: istanzia " + prefab.name);
            if (r.Get("name") != null) go.name = r.Get("name");
            if (r.Get("parent") != null) Undo.SetTransformParent(go.transform, FindGameObject(r.Get("parent")).transform, "Claude: parent");
            ApplyTransform(go.transform, r);
            return Json.Obj("text", "istanziato " + PathOf(go.transform), "path", PathOf(go.transform));
        }

        static Type ResolveComponentType(string name)
        {
            var matches = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && TypeMatches(t, name))
                .ToList();
            if (matches.Count == 0) throw new BridgeException($"tipo di componente sconosciuto: {name}");
            if (matches.Count == 1) return matches[0];
            // Più tipi con lo stesso nome: preferisci quello senza namespace (script del progetto), poi UnityEngine
            return matches.FirstOrDefault(t => string.IsNullOrEmpty(t.Namespace))
                   ?? matches.FirstOrDefault(t => t.Namespace == "UnityEngine")
                   ?? throw new BridgeException($"nome ambiguo, usa il nome completo: {string.Join(", ", matches.Select(t => t.FullName))}");
        }

        static object AddComponent(Req r)
        {
            var go = FindGameObject(r.Need("path"));
            var type = ResolveComponentType(r.Need("type"));
            var c = Undo.AddComponent(go, type);
            if (c == null) throw new BridgeException($"impossibile aggiungere {type.Name} a {go.name} (già presente o incompatibile?)");
            return Json.Obj("text", $"aggiunto {type.Name} a {PathOf(go.transform)}");
        }

        static object RemoveComponent(Req r)
        {
            var go = FindGameObject(r.Need("path"));
            var c = FindComponent(go, r.Need("type"));
            Undo.DestroyObjectImmediate(c);
            return Json.Obj("text", $"rimosso {r.Get("type")} da {PathOf(go.transform)}");
        }

        static object Delete(Req r)
        {
            var go = FindGameObject(r.Need("path"));
            var path = PathOf(go.transform);
            Undo.DestroyObjectImmediate(go);
            return Json.Obj("text", "eliminato " + path);
        }

        static object Select(Req r)
        {
            var path = r.Need("path");
            Object o = AssetDatabase.LoadMainAssetAtPath(path);
            if (o == null) o = FindGameObject(path);
            Selection.activeObject = o;
            EditorGUIUtility.PingObject(o);
            return Json.Obj("text", "selezionato " + Describe(o));
        }

        // ---------- esecuzione ----------

        static object Menu(Req r)
        {
            var path = r.Need("path");
            if (!EditorApplication.ExecuteMenuItem(path)) throw new BridgeException($"voce di menu non trovata o disabilitata: {path}");
            return Json.Obj("text", "eseguito " + path);
        }

        static Type ResolveType(string name)
        {
            // Serve cercare in tutti gli assembly; quelli già scaricati danno eccezione e vengono saltati
#pragma warning disable UAC0005
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
#pragma warning restore UAC0005
            foreach (var a in assemblies)
            {
                var t = a.GetType(name, false, true);
                if (t != null) return t;
            }
            foreach (var a in assemblies)
            {
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                var t = types.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (t != null) return t;
            }
            return null;
        }

        static object ConvertArg(string s, Type t)
        {
            if (t == typeof(string)) return s;
            if (t.IsEnum) return Enum.Parse(t, s, true);
            if (t == typeof(bool)) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            return Convert.ChangeType(s, t, Inv);
        }

        static object Exec(Req r)
        {
            var typeName = r.Need("type");
            var methodName = r.Need("method");
            var args = r.GetAll("arg");
            var type = ResolveType(typeName) ?? throw new BridgeException($"classe non trovata: {typeName} (lo script è compilato? prova 'refresh')");

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var method = type.GetMethods(flags).FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
            if (method == null)
            {
                var available = string.Join(", ", type.GetMethods(flags).Where(m => !m.IsSpecialName).Select(m => $"{m.Name}({m.GetParameters().Length})").Distinct());
                throw new BridgeException($"metodo statico {methodName} con {args.Length} argomenti non trovato in {type.FullName}. Disponibili: {available}");
            }

            var ps = method.GetParameters();
            var values = new object[args.Length];
            for (int i = 0; i < args.Length; i++) values[i] = ConvertArg(args[i], ps[i].ParameterType);

            object ret;
            try { ret = method.Invoke(null, values); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
            return Json.Obj("method", $"{type.FullName}.{method.Name}", "result", ret?.ToString());
        }

        static string ScreenshotPath => Path.Combine(BridgeServer.BridgeDir, "screenshot.png");

        static object Screenshot(Req r)
        {
            int w = r.GetInt("width", 1280), h = r.GetInt("height", 720);
            Camera cam;
            if (r.Get("view", "camera") == "scene")
            {
                var sv = SceneView.lastActiveSceneView ?? throw new BridgeException("nessuna Scene View aperta");
                cam = sv.camera;
            }
            else if (r.Get("camera") != null)
            {
                // GetComponent nell'Editor restituisce un "finto null": serve il confronto esplicito
                cam = FindGameObject(r.Get("camera")).GetComponent<Camera>();
                if (cam == null) throw new BridgeException("l'oggetto non ha una Camera");
            }
            else
            {
                cam = Camera.main ?? (Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null)
                      ?? throw new BridgeException("nessuna camera attiva in scena: usa view=scene");
            }

            var outPath = r.Get("out") != null ? Path.GetFullPath(Path.Combine(BridgeServer.ProjectRoot, r.Get("out"))) : ScreenshotPath;
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
            }
            return Json.Obj("text", outPath, "path", outPath, "camera", cam.name);
        }

        static object ScreenshotFile(Req r)
        {
            if (!File.Exists(ScreenshotPath)) throw new BridgeException("nessuno screenshot: usa prima 'screenshot'");
            return new RawResponse { Body = File.ReadAllBytes(ScreenshotPath), ContentType = "image/png" };
        }
    }
}
