using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Draw-cost hygiene for combat effects: pooled FX never instance a material
// per object (that breaks batching), a parked pooled effect draws nothing,
// and gameplay scripts do not read Renderer.material on spawned objects.
public static class RenderCostTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RC] PASS  " : "[RC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PooledFxShareMaterials();
        ParkedFxDrawNothing();
        GameplayDoesNotInstanceMaterials();
        return fails;
    }

    static void PooledFxShareMaterials()
    {
        for (int i = 0; i < 24; i++)
            WeaponFx.Impact(1 + (i % 15), new Vector3(i * .1f, 0f, 0f), .8f);
        var root = GameObject.Find("~WeaponFx");
        Check("the weapon FX pool exists after spawning impacts", root != null);
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        Material first = null;
        bool shared = true, noInstances = true;
        foreach (var r in renderers)
        {
            var m = r.sharedMaterial;
            if (m == null) continue;
            if (first == null) first = m;
            if (m != first) shared = false;
            if (m.name.EndsWith("(Instance)")) noInstances = false;
        }
        Check("pooled weapon FX draw with one shared material", shared);
        Check("pooled weapon FX hold no per-object material instance", noInstances);
    }

    static void ParkedFxDrawNothing()
    {
        var root = GameObject.Find("~WeaponFx");
        if (root == null) return;
        foreach (var fx in root.GetComponentsInChildren<FlipbookFx>(true)) fx.Stop();
        bool allOff = true;
        foreach (var r in root.GetComponentsInChildren<SpriteRenderer>(false))
            if (r.enabled) allOff = false;
        Check("a finished pooled effect is deactivated, not drawn at alpha 0", allOff);
    }

    // Reading Renderer.material clones the material for that renderer. The
    // scene wall scrollers do it once per wall; nothing else under Gameplay
    // may, least of all pooled or spawned objects.
    static void GameplayDoesNotInstanceMaterials()
    {
        string dir = Path.Combine(Application.dataPath, "Scripts", "Gameplay");
        var rx = new Regex(@"\b(?:renderer|Renderer|sr|r|body|glow|overlay|rim|white)\.material\b");
        string[] allowed = { "moveBackGround.cs", "slowMoveBackground.cs", "RailFit.cs" };
        string offender = null;
        foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (System.Array.IndexOf(allowed, name) >= 0) continue;
            foreach (string line in File.ReadAllLines(file))
            {
                string code = line;
                int c = code.IndexOf("//");
                if (c >= 0) code = code.Substring(0, c);
                if (rx.IsMatch(code)) { offender = name + ": " + line.Trim(); break; }
            }
            if (offender != null) break;
        }
        Check("no Gameplay script instances a material per renderer" + (offender != null ? " (" + offender + ")" : ""), offender == null);
    }
}
