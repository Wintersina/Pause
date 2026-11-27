using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The medium star dust, the drone-core flipbooks and the redrawn magnet atom (Art/Pickups/src~/generate_dust_medium.py,
// generate_cores_magnet.py):
//   1  points: small 1, medium 3, Bright Star 5 (ScoreRules / RunScore); dust payout 0.5 / 0.75 / 1 and the secret-meter fill
//   2  the medium dust prefab (medStar_1): kind, tag, sizes, frames, spawn mix, magnet / codex / scale handling
//   3  every dust FX site can load all three sprites (Shop and Codex bursts, death panel, splash gate, credits)
//   4  each drone-core mode (and the recruit) maps to its own flipbook: 14 idle + 6 burst frames, in its colour
//   5  the magnet's 14 idle + 6 burst frames, import settings of every new frame
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PickupArtTest.Run
public static class PickupArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PICKART] PASS  " : "[PICKART] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Points();
        MediumPrefab();
        SpawnMix();
        DustSites();
        Cores();
        Magnet();
        Imports();
        Debug.Log("[PICKART] failures: " + fails);
        return fails;
    }

    static void Points()
    {
        Check("small dust is worth 1 point (it was 2)", ScoreRules.SmallDust == 1 && RunScore.DustPoints(DustSize.Small) == 1);
        Check("medium dust is worth 3 points", ScoreRules.MediumDust == 3 && RunScore.DustPoints(DustSize.Medium) == 3);
        Check("the Bright Star keeps its 5 points", ScoreRules.LargeDust == 5 && RunScore.DustPoints(DustSize.Large) == 5);
        Check("points rise small < medium < large", RunScore.DustPoints(DustSize.Small) < RunScore.DustPoints(DustSize.Medium) &&
              RunScore.DustPoints(DustSize.Medium) < RunScore.DustPoints(DustSize.Large));
        string col = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("dust payout: small 0.5, medium 0.75, large 1 (medium between), medium pays through awardDust",
              col.Contains("smallStarValue = 0.5f") && col.Contains("mediumStarValue = 0.75f") && col.Contains("largeStarValue = 1f") &&
              col.Contains("awardDust(mediumStarValue)") && col.Contains("RunScore.OnDust(DustSize.Medium") && col.Contains("SecretPowerController.OnDust(DustSize.Medium)"));
        string powers = File.ReadAllText("Assets/Scripts/Gameplay/Weapons/SecretPowers.cs");
        Check("secret-meter fill: small 3 < medium 4.5 < large 6", powers.Contains("perSmallDust = 3f") && powers.Contains("perMediumDust = 4.5f") && powers.Contains("perLargeDust = 6f"));
    }

    // The codex lists the medium dust between Star Dust and the Bright Star, quotes its points, has art, an animation and a payout.
    static void CodexMediumEntry()
    {
        var entry = Codex.Find("atom_medstar");
        Check("the codex has a Medium Star entry in Atoms", entry != null && entry.category == CodexCategory.Atoms && entry.name == "Medium Star");
        if (entry == null) return;
        int small = System.Array.FindIndex(Codex.Entries, e => e.id == "atom_stardust");
        int med = System.Array.FindIndex(Codex.Entries, e => e.id == "atom_medstar");
        int big = System.Array.FindIndex(Codex.Entries, e => e.id == "atom_bigstar");
        Check("codex order is small < medium < Bright Star", small >= 0 && small < med && med < big);
        Check("its lore states +3 points; neighbours state theirs", entry.lore.Contains("+" + ScoreRules.MediumDust + " points") &&
              Codex.Find("atom_stardust").lore.Contains("+" + ScoreRules.SmallDust + " point") &&
              Codex.Find("atom_bigstar").lore.Contains("+" + ScoreRules.LargeDust + " points"));
        Check("its art is the medium idle frame", entry.Sprite != null);
        PickupKind kind;
        Check("its codex animation is the medium flipbook", CodexAnimations.TryPickupKind("atom_medstar", out kind) && kind == PickupKind.DustMedium &&
              CodexAnimations.Atom(entry) != null);
        Check("it pays the plain atom payout", CodexPayout.For(entry) == CodexPayout.Atom);
        Check("a Star Dust discovery no longer claims the medium prefab", !System.Array.Exists(Codex.Find("atom_stardust").matches, m => m == "medstar"));
    }

    // Nothing playable may lack a codex entry: every pickup kind, roster enemy / hazard, elite, boss, hazard rock, ship and world.
    static void CodexCoverage()
    {
        var kinds = new HashSet<PickupKind>();
        foreach (var e in Codex.Entries)
        {
            PickupKind k;
            if (e.category == CodexCategory.Atoms && CodexAnimations.TryPickupKind(e.id, out k)) kinds.Add(k);
        }
        foreach (PickupKind kind in System.Enum.GetValues(typeof(PickupKind)))
            Check("codex has an entry for pickup kind " + kind, kinds.Contains(kind));
        int missing = 0;
        foreach (var def in EnemyRoster.All) if (Codex.Find(def.codexId) == null) { missing++; Debug.Log("[PICKART] no codex entry for roster " + def.key); }
        foreach (var d in EliteCatalog.All) if (Codex.Find(d.codexId) == null) { missing++; Debug.Log("[PICKART] no codex entry for elite " + d.key); }
        foreach (var b in BossCatalog.All) if (Codex.Find(b.id) == null) { missing++; Debug.Log("[PICKART] no codex entry for boss " + b.id); }
        foreach (var r in SpaceHazardRockTypes.All) if (Codex.Find(r.CodexId) == null) { missing++; Debug.Log("[PICKART] no codex entry for rock " + r.key); }
        foreach (int ship in ShipId.All) if (Codex.Find(CodexCatalogue.ShipPrefix + ShipId.KeyOf(ship)) == null) { missing++; Debug.Log("[PICKART] no codex entry for ship " + ship); }
        for (int w = 0; w < WorldManager.Worlds.Length; w++) if (Codex.Find(Codex.WorldId(w)) == null) { missing++; Debug.Log("[PICKART] no codex entry for world " + w); }
        Check("every roster enemy / hazard, elite, boss, hazard rock, ship and world has a codex entry", missing == 0);
    }

    static void MediumPrefab()
    {
        var prefab = Resources.Load<GameObject>(PickupArt.MediumStarPrefab);
        Check("prefabs/medStar_1 loads", prefab != null);
        if (prefab == null) return;
        Check("it is a pickUp and is recognised as star dust named medStar1", prefab.CompareTag("pickUp") && PickupArt.IsStarDust(prefab) &&
              PrefabName.Is(prefab, PickupArt.MediumStar));
        PickupKind kind;
        Check("TryKindOf says DustMedium", PickupArt.TryKindOf(prefab, out kind) && kind == PickupKind.DustMedium);
        Check("kind numbers only grew (DustMedium on the end, 17)", (int)PickupKind.DustMedium == 17 && (int)PickupKind.DroneRecruit == 16 &&
              (int)PickupKind.DustSmall == 3 && (int)PickupKind.Dust == 2);
        var book = prefab.GetComponent<PickupFlipbook>();
        Check("the prefab carries the medium flipbook", book != null && book.kind == PickupKind.DustMedium);
        var sr = prefab.GetComponent<SpriteRenderer>();
        var idle = PickupArt.Frames(PickupArt.IdleName(PickupKind.DustMedium), PickupArt.IdleTicks(PickupKind.DustMedium).Length);
        bool all = idle.Length == 10;
        foreach (var f in idle) all &= f != null && f.rect.width == 108f && f.rect.height == 108f;
        Check("10 medium idle frames (108 px) load", all);
        float world = sr != null && sr.sprite != null ? sr.sprite.bounds.size.x * prefab.transform.localScale.x : 0f;
        float small = PickupArt.Frames("dustsm_idle", 10)[0].bounds.size.x * .05f, large = PickupArt.Frames("dust_idle", 10)[0].bounds.size.x * .1f;
        Check("the prefab draws between the small (" + small.ToString("F3") + ") and the Bright Star (" + large.ToString("F3") + "): " + world.ToString("F3"),
              world > small && world < large);
        bool frames = true;
        foreach (var f in idle) frames &= Mathf.Abs(f.bounds.size.x * prefab.transform.localScale.x - world) < .0005f;
        Check("every idle frame draws the prefab's size", frames);
        var burst = PickupArt.Frames(PickupArt.BurstName(PickupKind.DustMedium), PickupArt.BurstTicks.Length);
        bool burstOk = burst.Length == 6;
        foreach (var f in burst) burstOk &= f != null && f.rect.width == 212f;
        Check("6 medium burst frames (212 px) load", burstOk);
        Check("the medium burst is its own art", PickupArt.BurstName(PickupKind.DustMedium) == "dustmd_burst" && PickupArt.IdleName(PickupKind.DustMedium) == "dustmd_idle");
        Check("it flies bigger like the other star dust (StarDustScale)", Mathf.Approximately(PickupArt.InGameScale(prefab), PickupArt.StarDustScale));
        Check("no atom ring / spin for it", !PickupGlow.IsAtom(PickupKind.DustMedium));
        string codex = File.ReadAllText("Assets/Scripts/Codex/CodexCatalogue.cs");
        Check("catching one discovers its own Medium Star codex entry (not Star Dust)", Codex.IdForName("medStar 1(Clone)") == "atom_medstar" &&
              Codex.IdForName("smStar 1(Clone)") == "atom_stardust" && codex.Contains("\"medstar\"") && !codex.Contains("\"smstar\", \"medstar\""));
        CodexMediumEntry();
        CodexCoverage();
        string pull = File.ReadAllText("Assets/Scripts/Gameplay/Pickups/MagnetPull.cs");
        Check("the magnet pulls it (IsStarDust)", pull.Contains("PickupArt.IsStarDust(go)"));
        var go = Object.Instantiate(prefab);
        Check("a spawned one is a magnet's dust", MagnetPull.IsDust(go));
        Object.DestroyImmediate(go);
    }

    static void SpawnMix()
    {
        var spawner = new GameObject("~spawner").AddComponent<spawnGoodStuff>();
        var small = new GameObject("smStar 1"); var med = Resources.Load<GameObject>(PickupArt.MediumStarPrefab);
        spawner.smStar = small; spawner.medStar = med;
        Check("a trail star rolling under MediumShare is medium, over it small",
              spawner.TrailStar(0f) == med && spawner.TrailStar(spawnGoodStuff.MediumShare - .001f) == med &&
              spawner.TrailStar(spawnGoodStuff.MediumShare + .001f) == small && spawner.TrailStar(.99f) == small);
        int mediums = 0, n = 10000;
        var rng = new System.Random(5);
        for (int i = 0; i < n; i++) if (spawner.TrailStar((float)rng.NextDouble()) == med) mediums++;
        Check("about " + (int)(spawnGoodStuff.MediumShare * 100) + "% of trail stars are medium (" + mediums * 100f / n + "%)",
              Mathf.Abs(mediums / (float)n - spawnGoodStuff.MediumShare) < .02f);
        spawner.medStar = null;
        Check("without the prefab every star is small", spawner.TrailStar(0f) == small);
        string src = File.ReadAllText("Assets/Scripts/Gameplay/Pickups/spawnGoodStuff.cs");
        Check("the spawner loads, prewarms and places the medium dust", src.Contains("Resources.Load<GameObject>(PickupArt.MediumStarPrefab)") &&
              src.Contains("PrefabPool.Prewarm(medStar") && src.Contains("Place(TrailStar(Random.value), spawner)"));
        Object.DestroyImmediate(small); Object.DestroyImmediate(spawner.gameObject);
    }

    static void DustSites()
    {
        Check("StarDustArt loads the small, medium and large dust sprites", StarDustArt.AllLoad());
        var seen = new HashSet<Sprite>();
        for (int i = 0; i < 8; i++) seen.Add(StarDustArt.Mixed(i));
        Check("a burst of 8 pieces mixes all three sizes", seen.Count == 3);
        Check("relative sizes: small < medium < large", StarDustArt.Relative(0) < StarDustArt.Relative(1) && StarDustArt.Relative(1) < StarDustArt.Relative(2));
        Check("the three are the pickups' own frames", StarDustArt.SpriteName(DustSize.Small) == "dustsm_idle_0" &&
              StarDustArt.SpriteName(DustSize.Medium) == "dustmd_idle_0" && StarDustArt.SpriteName(DustSize.Large) == "dust_idle_0");

        // Shop burst
        var canvas = new GameObject("~dustCanvas", typeof(RectTransform)).GetComponent<RectTransform>();
        var layer = CodexUi.NewRect("ShopLayer", canvas);
        var shop = new ShopDustFx(layer);
        shop.Warm(null);
        Check("the Shop's payout burst uses all three sizes", SizesIn(layer) == 3);
        // Codex COLLECT ALL burst
        var codexFx = new CodexDustFx(CodexUi.NewRect("CodexLayer", canvas), null);
        bool slots = true;
        for (int i = 0; i < CodexDustFx.Slots; i++) slots &= codexFx.Slot(i).LayerRect != null && SizesIn(codexFx.Slot(i).LayerRect) >= 2;
        Check("COLLECT ALL's dust bursts mix the sizes in every slot", slots);
        Object.DestroyImmediate(canvas.gameObject);

        // Sources of the sites that need a scene to run
        foreach (var site in new[] { "Assets/Scripts/UI/GateView.cs", "Assets/Scripts/UI/DeathPanelView.cs", "Assets/Scripts/Shop/ShopDustFx.cs" })
            Check(Path.GetFileName(site) + " draws StarDustArt.Mixed", File.ReadAllText(site).Contains("StarDustArt.Mixed("));
        string credits = File.ReadAllText("Assets/Scripts/Gameplay/spawnWithoutTouchNewBehaviourScript.cs");
        var creditMed = Resources.Load<GameObject>(spawnWithoutTouchNewBehaviourScript.MediumPrefab);
        var sm = Resources.Load<GameObject>("prefabs/CreditStartSm"); var lg = Resources.Load<GameObject>("prefabs/CreditStarLg");
        Check("the credits have small, medium and large star prefabs", creditMed != null && sm != null && lg != null);
        if (creditMed != null && sm != null && lg != null)
        {
            float w(GameObject g) { var r = g.GetComponent<SpriteRenderer>(); return r.sprite.bounds.size.x * g.transform.localScale.x; }
            Check("the credits' medium star is between their small and large (" + w(sm).ToString("F2") + " < " + w(creditMed).ToString("F2") + " < " + w(lg).ToString("F2") + ")",
                  w(creditMed) > w(sm) && w(creditMed) < w(lg));
            Check("the credits' medium star is the medium art", creditMed.GetComponent<SpriteRenderer>().sprite.rect.width == 108f);
        }
        Check("the credits spawner spawns it", credits.Contains("medStar") && credits.Contains("Instantiate(medStar"));
    }

    static int SizesIn(RectTransform layer)
    {
        var set = new HashSet<Sprite>();
        foreach (var img in layer.GetComponentsInChildren<Image>(true))
            if (img.name == "Dust" && img.sprite != null) set.Add(img.sprite);
        return set.Count;
    }

    static readonly string[] CoreNames = { null, "straight", "sweep", "aimed", "reward" };

    static void Cores()
    {
        var kinds = new[] { PickupKind.CoreStraight, PickupKind.CoreAlternating, PickupKind.CoreAimed, PickupKind.DroneRecruit };
        var hue = new Dictionary<string, float>();
        for (int k = 0; k < kinds.Length; k++)
        {
            string name = CoreNames[k + 1];
            var kind = kinds[k];
            Check(kind + " names its idle and burst flipbooks drone_" + (k + 1) + "_*", PickupArt.IdleName(kind) == "drone_" + (k + 1) + "_idle" && PickupArt.BurstName(kind) == "drone_" + (k + 1) + "_burst");
            Check(kind + " maps to core_" + name, DronePickupArt.Names[k + 1] == name);
            var idle = PickupArt.Frames(PickupArt.IdleName(kind), PickupArt.IdleTicks(kind).Length);
            var burst = PickupArt.Frames(PickupArt.BurstName(kind), PickupArt.BurstTicks.Length);
            bool ok = idle.Length == 14 && burst.Length == 6;
            for (int i = 0; i < idle.Length; i++)
                ok &= idle[i] != null && idle[i].rect.width == 196f && ReferenceEquals(idle[i], Resources.Load<Sprite>("Pickups/Cores/core_" + name + "_idle_" + i));
            for (int i = 0; i < burst.Length; i++)
                ok &= burst[i] != null && burst[i].rect.width == 196f && ReferenceEquals(burst[i], Resources.Load<Sprite>("Pickups/Cores/core_" + name + "_burst_" + i));
            Check(name + ": 14 idle and 6 burst frames, the hand-drawn art (not the procedural disc)", ok);
            Check(name + ": 196 px at 392 ppu = .5 u, the old disc's size (48 px legibility on a 1080 px screen: ~" +
                  Mathf.RoundToInt(.5f * .8f * 1080f / 5.4f) + " px)", idle[0] != null && Mathf.Abs(idle[0].bounds.size.x - .5f) < .001f);
            hue[name] = MeanHue("Assets/Art/Resources/Pickups/Cores/core_" + name + "_idle_0.png");
        }
        Check("colours kept: straight cyan (" + hue["straight"].ToString("F0") + "), sweep amber (" + hue["sweep"].ToString("F0") + "), aimed green (" +
              hue["aimed"].ToString("F0") + "), reward gold (" + hue["reward"].ToString("F0") + ")",
              hue["straight"] > 170f && hue["straight"] < 210f && hue["sweep"] > 25f && hue["sweep"] < 45f &&
              hue["aimed"] > 85f && hue["aimed"] < 130f && hue["reward"] > hue["sweep"] && hue["reward"] < 55f);
        // the pickups themselves
        foreach (var mode in new[] { DroneMode.Straight, DroneMode.Alternating, DroneMode.Aimed })
        {
            var core = DroneCorePickup.Build(mode, Vector3.zero);
            var sr = core.GetComponent<SpriteRenderer>();
            var book = core.GetComponent<PickupFlipbook>();
            PickupKind k;
            Check(mode + " core shows its flipbook (kind " + (PickupKind)(12 + (int)mode) + ")", book != null && book.kind == (PickupKind)(12 + (int)mode) &&
                  ReferenceEquals(sr.sprite, Resources.Load<Sprite>("Pickups/Cores/core_" + CoreNames[(int)mode] + "_idle_0")) &&
                  PickupArt.TryKindOf(core, out k) && k == book.kind);
            Check(mode + " core's burst on catch is its own 6 frames", ReferenceEquals(PickupArt.Frames(PickupArt.BurstName(book.kind), 6)[0],
                  Resources.Load<Sprite>("Pickups/Cores/core_" + CoreNames[(int)mode] + "_burst_0")));
            book.Advance(.2f);
            Check(mode + " core's idle loop advances", book.Frame > 0);
            Object.DestroyImmediate(core);
        }
        var recruit = DroneRewardPickup.Spawn(Vector3.zero);
        Check("the recruit coin is the gold reward flipbook", ReferenceEquals(recruit.GetComponent<SpriteRenderer>().sprite, Resources.Load<Sprite>("Pickups/Cores/core_reward_idle_0")));
        Object.DestroyImmediate(recruit);
        Check("the HUD / codex icons use the same art", ReferenceEquals(DronePickupArt.For(DroneMode.Aimed), Resources.Load<Sprite>("Pickups/Cores/core_aimed_idle_0")) &&
              ReferenceEquals(DronePickupArt.Reward, Resources.Load<Sprite>("Pickups/Cores/core_reward_idle_0")));
    }

    static float MeanHue(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        float sum = 0f; int n = 0;
        foreach (var c in tex.GetPixels32())
        {
            if (c.a < 255) continue;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s > .5f && v > .5f) { sum += h * 360f; n++; }
        }
        Object.DestroyImmediate(tex);
        return n == 0 ? -1f : sum / n;
    }

    static void Magnet()
    {
        var idle = PickupArt.Frames(PickupArt.IdleName(PickupKind.Magnet), PickupArt.IdleTicks(PickupKind.Magnet).Length);
        var burst = PickupArt.Frames(PickupArt.BurstName(PickupKind.Magnet), PickupArt.BurstTicks.Length);
        Check("the magnet has 14 idle frames and 6 burst frames", idle.Length == 14 && burst.Length == 6);
        bool ok = true;
        for (int i = 0; i < idle.Length; i++) ok &= Resources.Load<Sprite>("Pickups/Atoms/magnet_idle_" + i) != null && File.Exists("Assets/Art/Resources/Pickups/Atoms/magnet_idle_" + i + ".png");
        for (int i = 0; i < burst.Length; i++) ok &= Resources.Load<Sprite>("Pickups/Atoms/magnet_burst_" + i) != null;
        Check("magnet_idle_0..13 and magnet_burst_0..5 all load by the same names", ok);
        Check("idle frames are 196 px (.28 u at 700 ppu, the other atoms' size), bursts 388 px", idle[0].rect.width == 196f && Mathf.Abs(idle[0].bounds.size.x - .28f) < .001f && burst[0].rect.width == 388f);
        int files = Directory.GetFiles("Assets/Art/Resources/Pickups/Atoms", "magnet_*.png").Length;
        Check("exactly 20 magnet frames on disk (" + files + ")", files == 20);
    }

    static void Imports()
    {
        var paths = new List<string>();
        paths.AddRange(Directory.GetFiles("Assets/Art/Resources/Pickups/Cores", "*.png"));
        paths.AddRange(Directory.GetFiles("Assets/Art/Resources/Pickups/Atoms", "dustmd_*.png"));
        paths.AddRange(Directory.GetFiles("Assets/Art/Resources/Pickups/Atoms", "magnet_*.png"));
        Check("80 core + 16 medium dust + 20 magnet frames found (" + paths.Count + ")", paths.Count == 116);
        int bad = 0;
        foreach (var p in paths)
        {
            var imp = AssetImporter.GetAtPath(p.Replace('\\', '/')) as TextureImporter;
            bool ok = imp != null && imp.textureType == TextureImporterType.Sprite && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled &&
                      imp.textureCompression == TextureImporterCompression.Uncompressed && imp.alphaIsTransparency;
            if (!ok) { bad++; Debug.Log("[PICKART] bad import " + p); }
        }
        Check("every new frame imports as a Point-filtered, mip-free, uncompressed sprite with alpha transparency (" + bad + " bad)", bad == 0);
    }
}
