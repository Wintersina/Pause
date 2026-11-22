using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// THE ENGINE CLASSES THE GAME MAKES FROM CODE MUST EXIST IN THE PLAYER.
//
// Build 10026 slowed to a crawl and ran out of memory on every phone: HazardVisuals did
// `new GameObject(.., typeof(SpriteMask))`, and the player had no SpriteMask class. Nothing in a scene or prefab uses one, and
// the native SpriteMask module depends on the Tilemap module, which was not in Packages/manifest.json: the build silently
// leaves such a module out ("AddComponent asking for "SpriteMask" which is not a Unity engine type"). The editor always has
// every class, so no editor test could see it.
//
// This suite reads the engine's own module table (PlaybackEngines/<target>/modules.asset: each native module's
// dependencies and whether a built-in package switches it on) and the project's manifest, and requires that every engine
// component type the game's scripts create or look up (typeof(T), AddComponent<T>, GetComponent<T>, ...) sits in a module
// the player can link. It also pins the two things that keep SpriteMask alive: the Tilemap module and the link.xml entry.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod EngineModuleGuardTest.Run
public static class EngineModuleGuardTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[ENGMOD] PASS  " : "[ENGMOD] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public sealed class Module { public string name; public List<string> deps = new List<string>(); public bool byPackage; }

    // The module table as the engine ships it; this copy is the fallback when an editor install has no player support
    // folder to read it from (only the modules the game could plausibly reach are listed).
    static readonly (string name, string[] deps, bool byPackage)[] Fallback =
    {
        ("SharedInternals", new string[0], false), ("Core", new[] { "SharedInternals" }, false),
        ("TextRendering", new[] { "Core" }, false), ("InputLegacy", new[] { "Core" }, false), ("Grid", new[] { "Core" }, false),
        ("SpriteShape", new[] { "Core" }, false),
        ("Audio", new[] { "Core" }, true), ("Animation", new[] { "Core" }, true), ("Physics", new[] { "Core" }, true),
        ("Physics2D", new[] { "Core" }, true), ("IMGUI", new[] { "Core" }, true), ("UI", new[] { "Core" }, true),
        ("UIElements", new[] { "Core" }, true), ("ParticleSystem", new[] { "Core" }, true), ("Video", new[] { "Core" }, true),
        ("Tilemap", new[] { "Core", "Grid", "Physics2D" }, true),
        ("SpriteMask", new[] { "Core", "SpriteShape", "Tilemap" }, false),
    };

    public static Dictionary<string, Module> ReadModules(out string source)
    {
        var table = new Dictionary<string, Module>();
        source = "built-in copy";
        foreach (var target in new[] { BuildTarget.Android, BuildTarget.iOS, BuildTarget.StandaloneOSX, BuildTarget.StandaloneLinux64 })
        {
            string dir = null;
            try { dir = BuildPipeline.GetPlaybackEngineDirectory(target, BuildOptions.None, false); } catch (Exception) { }
            string path = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "modules.asset");
            if (path == null || !File.Exists(path)) continue;
            Module cur = null; bool inDeps = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("- name:", StringComparison.Ordinal))
                {
                    cur = new Module { name = line.Substring(7).Trim() };
                    table[cur.name] = cur; inDeps = false;
                }
                else if (cur == null) continue;
                else if (line.StartsWith("dependencies:", StringComparison.Ordinal)) inDeps = !line.EndsWith("[]", StringComparison.Ordinal);
                else if (inDeps && line.StartsWith("- ", StringComparison.Ordinal)) cur.deps.Add(line.Substring(2).Trim());
                else
                {
                    inDeps = false;
                    if (line.StartsWith("controlledByBuiltinPackage:", StringComparison.Ordinal)) cur.byPackage = line.EndsWith("1", StringComparison.Ordinal);
                }
            }
            if (table.Count > 10) { source = path; return table; }
            table.Clear();
        }
        foreach (var f in Fallback) table[f.name] = new Module { name = f.name, deps = f.deps.ToList(), byPackage = f.byPackage };
        return table;
    }

    public static string PackageOf(string module) { return "com.unity.modules." + module.ToLowerInvariant(); }

    // Can the player link `module` with these packages? Null when it can, else why not.
    public static string WhyNot(string module, Dictionary<string, Module> table, HashSet<string> packages, HashSet<string> seen = null)
    {
        seen = seen ?? new HashSet<string>();
        if (!seen.Add(module)) return null;
        Module m;
        if (!table.TryGetValue(module, out m)) return null;   // not a strippable native module the table knows: nothing to check
        if (m.byPackage && !packages.Contains(PackageOf(module))) return "the " + module + " module is switched off (add \"" + PackageOf(module) + "\" to Packages/manifest.json)";
        foreach (string d in m.deps)
        {
            string why = WhyNot(d, table, packages, seen);
            if (why != null) return module + " needs " + d + ": " + why;
        }
        return null;
    }

    static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }

    public static HashSet<string> ManifestPackages()
    {
        var set = new HashSet<string>();
        string text = File.ReadAllText(Path.Combine(ProjectRoot, "Packages/manifest.json"));
        foreach (Match m in Regex.Matches(text, "\"(com\\.unity\\.modules\\.[a-z0-9]+)\"\\s*:")) set.Add(m.Groups[1].Value);
        return set;
    }

    // Native engine component types by name -> module ("SpriteMask" -> "SpriteMask", "LineRenderer" -> "Core").
    static Dictionary<string, string> EngineComponents()
    {
        var map = new Dictionary<string, string>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string an = asm.GetName().Name;
            if (!an.StartsWith("UnityEngine.", StringComparison.Ordinal) || !an.EndsWith("Module", StringComparison.Ordinal)) continue;
            string module = an.Substring(12, an.Length - 12 - 6);
            Type[] types;
            try { types = asm.GetTypes(); } catch (Exception) { continue; }
            foreach (var t in types)
                if (t.IsPublic && !t.IsAbstract && typeof(Component).IsAssignableFrom(t) && !map.ContainsKey(t.Name)) map[t.Name] = module;
        }
        return map;
    }

    static readonly Regex Use = new Regex(@"typeof\(\s*(?:UnityEngine\.)?([A-Z][A-Za-z0-9_]*)\s*\)|(?:AddComponent|GetComponent|GetComponents|GetComponentInChildren|GetComponentsInChildren|GetComponentInParent|TryGetComponent|FindObjectsByType|FindFirstObjectByType|FindAnyObjectByType|RequireComponent)\s*<\s*(?:UnityEngine\.)?([A-Z][A-Za-z0-9_]*)\s*>");

    // engine component type -> the first script that creates / looks it up
    public static Dictionary<string, string> UsedEngineComponents()
    {
        var engine = EngineComponents();
        var used = new Dictionary<string, string>();
        string scripts = Path.Combine(Application.dataPath, "Scripts");
        foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Use.Matches(text))
            {
                string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (engine.ContainsKey(name) && !used.ContainsKey(name)) used[name] = Path.GetFileName(file);
            }
        }
        return used;
    }

    public static int Execute()
    {
        fails = 0;
        string source;
        var table = ReadModules(out source);
        var packages = ManifestPackages();
        var engine = EngineComponents();
        Debug.Log("[ENGMOD] INFO  module table: " + source + " (" + table.Count + " modules), " + packages.Count + " module packages in the manifest");

        // the meter itself: it knows SpriteMask needs Tilemap, and it sees a missing package
        Check("the module table knows SpriteMask and that it depends on Tilemap", table.ContainsKey("SpriteMask") && table["SpriteMask"].deps.Contains("Tilemap"));
        Check("the module table says a built-in package switches Tilemap on", table.ContainsKey("Tilemap") && table["Tilemap"].byPackage);
        var without = new HashSet<string>(packages); without.Remove(PackageOf("Tilemap"));
        Check("control: without the Tilemap package SpriteMask is reported as unlinkable (the 10026 build)", WhyNot("SpriteMask", table, without) != null,
              WhyNot("SpriteMask", table, without) ?? "linkable");
        Check("control: SpriteMask is an engine component in the SpriteMask module", engine.ContainsKey("SpriteMask") && engine["SpriteMask"] == "SpriteMask");

        // what keeps SpriteMask in the player
        Check("Packages/manifest.json has com.unity.modules.tilemap (the SpriteMask module needs it)", packages.Contains(PackageOf("Tilemap")));
        string link = File.ReadAllText(Path.Combine(Application.dataPath, "link.xml"));
        Check("link.xml preserves UnityEngine.SpriteMaskModule", Regex.IsMatch(link, "<assembly\\s+fullname=\"UnityEngine\\.SpriteMaskModule\"\\s+preserve=\"all\"\\s*/>"));

        // every engine component the scripts create or look up
        var used = UsedEngineComponents();
        Check("the scan finds the engine components the game creates from code (" + used.Count + ")",
              used.ContainsKey("SpriteMask") && used.ContainsKey("LineRenderer") && used.ContainsKey("SpriteRenderer") && used.Count >= 15);
        var bad = new List<string>();
        foreach (var kv in used)
        {
            string why = WhyNot(engine[kv.Key], table, packages);
            if (why != null) bad.Add(kv.Key + " (" + kv.Value + "): " + why);
        }
        Check("every engine component the scripts create or look up is in a module the player links", bad.Count == 0, string.Join(" | ", bad));

        Debug.Log("[ENGMOD] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }
}
