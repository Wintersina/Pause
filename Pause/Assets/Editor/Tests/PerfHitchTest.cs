using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// NOTHING IS READ BACK, TRACED OR SLICED FOR THE FIRST TIME IN THE MIDDLE OF A FIGHT.
//
// After the level-long leak (PerfSoakTest) the soak still showed four first-use costs landing in play: ~700 ms when a
// boss arrived and 80-130 ms each time an attack's art was first drawn (Sprite.Create with no mesh type traces a tight
// outline of every cell), 9-12 MB allocated in the frame an elite spawned (its silhouette was measured by reading every
// drawing of its strip back from the GPU), a GPU read-back at most kills (the pieces an enemy breaks into), and every
// world's sliced art kept for the rest of the session. This suite pins the fixes:
//
//   1  every Sprite.Create in the game's scripts asks for SpriteMeshType.FullRect (a source scan: a tight mesh is
//      traced on the spot, milliseconds per cell)
//   2  pixels are read only by the few files allowed to, and the GPU read-backs report to PerfProbe (a source scan)
//   3  every elite's silhouette is baked into its def, the bake matches its strips, and asking for it reads nothing
//   4  after the boss's warm steps its whole fight -- body, shots, both rim styles, card, damage and death art --
//      makes no sprite and reads no pixel; and asking for a pose each frame allocates nothing
//   5  after the ship's warm steps none of its attack strips is sliced on first use
//   6  a shot's outline is built once per drawing and size, and a big one no longer leaves megabytes of garbage
//   7  a world's sliced art is let go when the run moves on and when its scene goes; two "runs" of the same world
//      do not hold more than one
//   8  the asynchronous cut of an enemy's pieces is the very cut the read-back made (same pixels, right way up)
//   9  the per-frame lookups the leaderboard tracker and the boss make allocate nothing
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PerfHitchTest.Run
public static class PerfHitchTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[HITCH] PASS  " : "[HITCH] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }
    static void Info(string what) { Debug.Log("[HITCH] INFO  " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string ScriptsRoot = "Assets/Scripts";

    // The only game scripts that may read a texture's pixels, and why. Everything else bakes or caches.
    static readonly Dictionary<string, string> PixelReaders = new Dictionary<string, string>
    {
        { "ShieldContour.cs", "the one shared read-back (reports to PerfProbe); callers are warmed or baked" },
        { "ShotOutline.cs", "a readable procedural shot drawing's own cell, once per drawing" },
        { "DeathCrash.cs", "the ship's wreck at a death, and the edit-mode cut (reports to PerfProbe)" },
        { "DeathPanelShapes.cs", "the death panel's mirrored card, warmed by FirstUseWarm" },
        { "DeathPanelView.cs", "the death panel's button copy, warmed by FirstUseWarm" },
        { "DockPopup.cs", "the dock (a menu)" },
        { "FrameInsets.cs", "HUD frame measurements, warmed by FirstUseWarm" },
        { "KillCamArchive.cs", "saving a kill-cam replay (after the run)" },
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            EveryRuntimeSpriteIsFullRect();
            PixelsAreReadOnlyWhereAllowed();
            EliteSilhouettesAreBaked();
            QuarterHeartsAreWarmed();
            BossFightIsWarmed();
            ShipStripsAreWarmed();
            ShotOutlineBuildsOnceAndLean();
            WorldArtIsReleased();
            AsyncCutMatchesTheReadback();
            PerFrameLookupsAllocateNothing();
        }
        finally
        {
            PerfProbe.OnPixelRead = null;
            PerfProbe.Reset();
            WorldArtCaches.Release();
            WorldArtCaches.Reset();
            AssetSweep.Reset();
            FirstUseWarm.Clear();
            ShotOutline.Bold = null;
            BossArt.ShotRimBold = null;
        }
        Debug.Log("[HITCH] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- 1 ----
    static void EveryRuntimeSpriteIsFullRect()
    {
        var bad = new List<string>();
        int calls = 0;
        foreach (string path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string src = TestHarness.StripComments(File.ReadAllText(path));
            int at = 0;
            while ((at = src.IndexOf("Sprite.Create(", at, StringComparison.Ordinal)) >= 0)
            {
                int open = at + "Sprite.Create(".Length, depth = 1, i = open;
                for (; i < src.Length && depth > 0; i++)
                {
                    if (src[i] == '(') depth++;
                    else if (src[i] == ')') depth--;
                }
                string args = src.Substring(open, Mathf.Max(0, i - open));
                calls++;
                if (args.IndexOf("SpriteMeshType.FullRect", StringComparison.Ordinal) < 0)
                    bad.Add(Path.GetFileName(path) + ":" + Line(src, at));
                at = open;
            }
        }
        Check("every Sprite.Create in Assets/Scripts passes SpriteMeshType.FullRect (" + calls + " calls): a tight mesh is traced on the spot",
              bad.Count == 0 && calls > 50, string.Join(", ", bad));
    }

    static int Line(string src, int at)
    {
        int n = 1;
        for (int i = 0; i < at && i < src.Length; i++) if (src[i] == '\n') n++;
        return n;
    }

    // ---- 2 ----
    static readonly string[] PixelCalls = { ".GetPixels(", ".GetPixels32(", ".GetPixel(", ".ReadPixels(", ".GetRawTextureData(" };

    static void PixelsAreReadOnlyWhereAllowed()
    {
        var bad = new List<string>();
        foreach (string path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            if (PixelReaders.ContainsKey(name)) continue;
            string src = TestHarness.StripComments(File.ReadAllText(path));
            foreach (string call in PixelCalls)
            {
                int at = src.IndexOf(call, StringComparison.Ordinal);
                // (ShieldContour.ReadPixels(...) is the shared, counted reader: calling it is the allowed way)
                while (at >= 0 && call == ".ReadPixels(" && at >= 13 && src.Substring(at - 13, 13) == "ShieldContour")
                    at = src.IndexOf(call, at + 1, StringComparison.Ordinal);
                if (at >= 0) bad.Add(name + ":" + Line(src, at) + " " + call.Trim('.', '('));
            }
        }
        Check("no game script outside the " + PixelReaders.Count + " listed readers reads a texture's pixels", bad.Count == 0, string.Join(", ", bad));
        foreach (string file in new[] { "Ship/ShieldContour.cs", "Ship/DeathCrash.cs" })
        {
            string src = File.ReadAllText(ScriptsRoot + "/" + file);
            Check(file + " reports its GPU read-backs to PerfProbe", src.Contains("PerfProbe.PixelRead("));
        }
        // the callers of the shared reader that run in play are the baked / warmed ones
        var callers = new List<string>();
        foreach (string path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            if (name == "ShieldContour.cs") continue;
            if (TestHarness.StripComments(File.ReadAllText(path)).Contains("ShieldContour.ReadPixels(")) callers.Add(name);
        }
        callers.Sort();
        string got = string.Join(",", callers);
        Check("ShieldContour.ReadPixels is called only by the known, warmed or baked callers", got == "BossArt.cs,EliteSilhouette.cs,HeartOutline.cs,HeartQuarterFrames.cs,RailMineLaser.cs,ShotOutline.cs", got);
    }

    static void QuarterHeartsAreWarmed()
    {
        FirstUseWarm.Clear();
        FirstUseWarm.QueueSession();
        Check("quarter-heart frames are queued before combat", FirstUseWarm.Asked("heart:quarters"));
        for (int i = 0; i < 500 && !FirstUseWarm.Done; i++) FirstUseWarm.Pump();
        int builds = HeartQuarterFrames.BuildCount;
        var heart = Resources.Load<Sprite>(ShipLivesIndicator.HeartSpritePath);
        var frames = HeartQuarterFrames.For(heart);
        Check("combat reuses the warmed quarter atlas without another build", frames != null && HeartQuarterFrames.BuildCount == builds);
    }

    // ---- 3 ----
    static void EliteSilhouettesAreBaked()
    {
        int n = 0, unbaked = 0, stale = 0;
        string which = "";
        foreach (var def in EliteCatalog.All)
        {
            n++;
            if (def.silhouetteShare <= 0f) { unbaked++; which += def.key + " (no bake) "; continue; }
            if (!TestHarness.FullLane && n % 4 != 0) continue;   // gate lane: every fourth def is re-measured (HeartsInArtTest reads them all)
            float fresh = EliteSilhouette.MeasureShare(def);
            if (fresh > 0f && Mathf.Abs(fresh - def.silhouetteShare) > .003f) { stale++; which += def.key + " (baked " + def.silhouetteShare + ", strips " + fresh.ToString("F4") + ") "; }
        }
        Check("every elite def carries its baked silhouette (" + n + " defs; Pause/Elites/Bake Silhouettes)", n > 20 && unbaked == 0, which);
        Check("the bakes match the strips they were measured from", stale == 0, which);
        PerfProbe.Reset();
        int before = EliteSilhouette.Unbaked;
        float sum = 0f;
        foreach (var def in EliteCatalog.All) sum += EliteSilhouette.Radius(def);
        Check("asking for every elite's silhouette reads no pixel (it read 15-20 drawings back per elite, 9-12 MB)",
              PerfProbe.PixelReads == 0 && EliteSilhouette.Unbaked == before && sum > 0f, PerfProbe.PixelReads + " reads");
    }

    // ---- 4 ----
    static int Sprites() { return Resources.FindObjectsOfTypeAll<Sprite>().Length; }

    // Runs the queued warm steps to the end, one a call as the game does one a frame. (Other suites leave steps of
    // their own queued in this editor process, so each check here starts from an empty queue: FirstUseWarm.Clear.)
    static void PumpWarm()
    {
        for (int i = 0; i < 20000 && !FirstUseWarm.Done; i++) FirstUseWarm.Pump();
    }

    // What was made or read between two points, by name: a failure says WHICH sprite or pixel read was not warmed.
    static HashSet<int> spriteIds;
    static readonly List<string> reads = new List<string>();

    static void Watch()
    {
        spriteIds = new HashSet<int>();
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>()) spriteIds.Add(s.GetInstanceID());
        reads.Clear();
        PerfProbe.Reset();
        PerfProbe.OnPixelRead = (site, what, pixels, gpu) => reads.Add(site + " '" + (what != null ? what.name : "?") + "' " + pixels + " px" + (gpu ? " GPU" : ""));
    }

    static string Seen()
    {
        PerfProbe.OnPixelRead = null;
        var names = new Dictionary<string, int>();
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (spriteIds.Contains(s.GetInstanceID())) continue;
            string n = string.IsNullOrEmpty(s.name) ? "<unnamed of " + (s.texture != null ? s.texture.name : "?") + ">" : s.name + " (" + (s.texture != null ? s.texture.name : "?") + ")";
            int c; names.TryGetValue(n, out c); names[n] = c + 1;
        }
        var parts = new List<string>();
        foreach (var kv in names) parts.Add(kv.Value + " x " + kv.Key);
        return "new sprites: " + (parts.Count == 0 ? "none" : string.Join(", ", parts)) + "; pixel reads: " + (reads.Count == 0 ? "none" : string.Join(", ", reads));
    }

    static void TouchFight(BossDef boss)
    {
        int frames = BossArt.BodyFrameCount(boss);
        for (int i = 0; i < frames; i++) BossArt.Body(boss, i);
        for (int i = 0; i < BossArt.ShotColumns; i++) BossArt.Shot(boss, i);
        for (int c = 0; c < BossArt.ShotRimCells; c++) { BossArt.ShotRim(boss, c, false); BossArt.ShotRim(boss, c, true); }
        BossArt.AttackShot(boss, 1, 0);
        BossArt.Card(boss);
        BossArt.Warning();
        if (BossArt.HasDamageArt(boss))
        {
            for (int i = 0; i < BossArt.DamageCells; i++) BossArt.DamageBody(boss, i);
            for (int i = 0; i < BossArt.DamageFxCells; i++) BossArt.DamageFx(boss, i);
            foreach (int pose in BossArt.AttackDamagePoses(boss)) BossArt.AttackDamageBody(boss, pose, 1);
        }
        if (!string.IsNullOrEmpty(boss.deathKey)) BossArt.DeathStrip(boss, 0);
    }

    // What a frame of the fight asks BossArt for (one rim style, as a fight has).
    static void TouchFrame(BossDef boss)
    {
        int frames = BossArt.BodyFrameCount(boss);
        for (int i = 0; i < frames; i++) BossArt.Body(boss, i);
        for (int i = 0; i < BossArt.ShotColumns; i++) BossArt.Shot(boss, i);
        for (int c = 0; c < BossArt.ShotRimCells; c++) BossArt.ShotRim(boss, c, false);
        BossArt.AttackShot(boss, 1, 0);
        if (!BossArt.HasDamageArt(boss)) return;
        for (int i = 0; i < BossArt.DamageCells; i++) BossArt.DamageBody(boss, i);
        for (int i = 0; i < BossArt.DamageFxCells; i++) BossArt.DamageFx(boss, i);
        foreach (int pose in BossArt.AttackDamagePoses(boss)) BossArt.AttackDamageBody(boss, pose, 1);
    }

    static void BossFightIsWarmed()
    {
        for (int w = 0; w < WorldManager.LiveWorldCount; w++)
        {
            if (!TestHarness.SweepWorld(w)) continue;
            var boss = BossCatalog.ForWorld(w);
            if (boss == null) continue;
            BossArt.Release();
            FirstUseWarm.Clear();
            Check(boss.artKey + ": released, nothing of the boss is sliced", !BossArt.Warmed(boss) && BossArt.CachedSheets == 0);
            BossArt.QueueWarm(boss);
            int queued = FirstUseWarm.Queued;
            PumpWarm();
            Check(boss.artKey + ": the warm steps (" + queued + ", one a frame) make the boss's sprites before the fight",
                  queued >= 7 && queued <= 12 && FirstUseWarm.Done && BossArt.Warmed(boss));
            int sprites = Sprites();
            Watch();
            TouchFight(boss);
            int reads = PerfProbe.PixelReads;
            string seen = Seen();
            Check(boss.artKey + ": the fight then makes no sprite and reads no pixel (the entrance cost ~700 ms of slicing and tracing)",
                  Sprites() == sprites && reads == 0, "sprites " + sprites + " -> " + Sprites() + ", pixel reads " + reads + "; " + seen);
            long control;
            if (TestHarness.AllocMeterWorks(out control))
            {
                long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 60; i++) TouchFrame(boss); });
                Check(boss.artKey + ": 60 frames of asking for every pose, shot, rim and damage drawing allocate nothing", bytes == 0, bytes + " B");
            }
            else Check("the allocation meter sees allocations", false, "control " + control);
            Check(boss.artKey + ": the steps are not queued twice", !FirstUseWarm.Add(BossArt.WarmKey(boss) + "body", null));
        }
        BossArt.Release();
        FirstUseWarm.Release("boss:");
    }

    // ---- 5 ----
    static void ShipStripsAreWarmed()
    {
        int ship = 2;   // Volt Viper: attack, attack_shot, emp, stun
        FirstUseWarm.Clear();
        ShipFxArt.QueueWarm(ship);
        int steps = FirstUseWarm.Queued;
        PumpWarm();
        Check("the ship's warm steps (" + steps + ") run to the end", steps >= 3 + ShipFxArt.ExtraStrips.Length && FirstUseWarm.Done);
        int sprites = Sprites();
        Watch();
        Sprite any = null;
        foreach (string extra in ShipFxArt.ExtraStrips)
        {
            var cells = ShipFxArt.Strip(ship, extra);
            if (cells != null && any == null) any = cells[0];
        }
        ShipFxArt.Strip(ship, "attack");
        Check("after the ship's warm steps no attack strip is sliced on its first use in a fight (each cost 80-130 ms)",
              Sprites() == sprites && any != null && ShipFxArt.StripReady(ship, "attack"), "sprites " + sprites + " -> " + Sprites() + "; " + Seen());
    }

    // ---- 6 ----
    static Sprite Blob(int size, string name)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
        var px = new Color32[size * size];
        float c = size * .5f, r = size * .33f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c * 1.1f;
                bool on = dx * dx + dy * dy < r * r || (Mathf.Abs(dx) < size * .08f && y < c);
                px[y * size + x] = on ? new Color32((byte)(x * 255 / size), (byte)(y * 255 / size), 200, 255) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        made.Add(tex);
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
        s.name = name;
        made.Add(s);
        return s;
    }

    static readonly List<Object> made = new List<Object>();

    static void ShotOutlineBuildsOnceAndLean()
    {
        var art = Blob(192, "~hitchShot");
        var warm = Blob(192, "~hitchShotWarm");
        ShotOutline.For(warm, .9f, false);   // the scratch is sized by the first build
        long control;
        if (!TestHarness.AllocMeterWorks(out control)) { Check("the allocation meter sees allocations", false, "control " + control); return; }
        PerfProbe.Reset();
        Sprite rim = null;
        long first = TestHarness.AllocatedBytes(() => { rim = ShotOutline.For(Blob(192, "~hitchShotN"), .9f, false); });
        Check("a 192 px shot drawing's outline is built with little garbage (it left ~5 MB: two 768 x 768 scratch arrays)", rim != null && first < 512 * 1024, first + " B");
        rim = ShotOutline.For(art, .9f, false);
        long again = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) ShotOutline.For(art, .9f, false); });
        Check("... and asking again for the same drawing and size builds and allocates nothing", again == 0 && ShotOutline.For(art, .9f, false) == rim, again + " B");
        Check("... and a readable drawing is read on the CPU, never through the GPU", PerfProbe.GpuReadbacks == 0, PerfProbe.GpuReadbacks + " read-backs");
        foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        made.Clear();
    }

    // ---- 7 ----
    static void LoadWorld(int w)
    {
        var boss = BossCatalog.ForWorld(w);
        if (boss != null) TouchFight(boss);
        var elites = new List<EliteDef>();
        EliteCatalog.ForWorld(w, elites);
        foreach (var d in elites) { EliteArt.Frames(d); EliteArt.Shards(d); }
        foreach (var def in EnemyRoster.All) if (def.world == w) { EnemyArt.Frames(def); EnemyDeathFlipbook.Frames(def); }
    }

    static void WorldArtIsReleased()
    {
        WorldArtCaches.Release();
        WorldArtCaches.Reset();
        AssetSweep.Reset();
        int swept = 0;
        AssetSweep.Unload = () => swept++;
        int w = WorldManager.LiveWorldCount - 1;
        Check("the first world of a scene releases nothing (the scene's unload already did)", !WorldArtCaches.Enter(w) && WorldArtCaches.Releases == 0);
        LoadWorld(w);
        int strips = EliteArt.CachedStrips, enemies = EnemyArt.CachedStrips, sheets = BossArt.CachedSheets;
        Check("a world's lazy art fills the loaders' tables", strips > 0 && enemies > 0 && sheets > 0, "elite strips " + strips + ", enemy strips " + enemies + ", boss sheets " + sheets);
        Check("the same world again (the planetfall's swap) keeps them", !WorldArtCaches.Enter(w) && EliteArt.CachedStrips == strips && BossArt.CachedSheets == sheets);
        bool released = WorldArtCaches.Enter(0);
        Check("moving to another world empties them and asks for a sweep at the next covered moment",
              released && EliteArt.CachedStrips == 0 && EnemyArt.CachedStrips == 0 && BossArt.CachedSheets == 0 && DeathCrash.CachedTargetSets == 0 && AssetSweep.Pending && swept == 0,
              "elite " + EliteArt.CachedStrips + ", enemy " + EnemyArt.CachedStrips + ", boss " + BossArt.CachedSheets + ", pending " + AssetSweep.Pending);
        AssetSweep.Covered();
        Check("... which runs once the view is covered, not before", swept == 1 && !AssetSweep.Pending);
        var boss = BossCatalog.ForWorld(w);
        Check("... and the boss's warm steps can be asked for again", boss == null || FirstUseWarm.Add(BossArt.WarmKey(boss) + "body", null));
        FirstUseWarm.Release("boss:");
        LoadWorld(0);
        WorldArtCaches.Leave();
        Check("the scene going empties them too", EliteArt.CachedStrips == 0 && EnemyArt.CachedStrips == 0 && BossArt.CachedSheets == 0 && WorldArtCaches.World == -1);
        AssetSweep.Reset();

        // two "runs" of the same world: what is resident after the second is what was after the first
        UnityEditor.EditorUtility.UnloadUnusedAssetsImmediate();
        int[] sprites = new int[3], textures = new int[3];
        for (int run = 0; run < 3; run++)
        {
            WorldArtCaches.Enter(w);
            LoadWorld(w);
            sprites[run] = Sprites();
            textures[run] = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            WorldArtCaches.Leave();
            UnityEditor.EditorUtility.UnloadUnusedAssetsImmediate();
        }
        Info("three runs of one world, at their fullest: sprites " + sprites[0] + ", " + sprites[1] + ", " + sprites[2] + "; textures " + textures[0] + ", " + textures[1] + ", " + textures[2]);
        Check("a second and third run of a world hold no more sprites than the first (nothing is carried from run to run)",
              sprites[1] <= sprites[0] + SpriteSlack && sprites[2] <= sprites[1] + SpriteSlack, sprites[0] + " -> " + sprites[1] + " -> " + sprites[2]);
        Check("... and no more textures", textures[1] <= textures[0] + TextureSlack && textures[2] <= textures[1] + TextureSlack, textures[0] + " -> " + textures[1] + " -> " + textures[2]);
    }

    public const int SpriteSlack = 8, TextureSlack = 4;

    // ---- 8 ----
    static void AsyncCutMatchesTheReadback()
    {
        if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Info("no asynchronous GPU read-back on this device: the game keeps the immediate cut here");
            return;
        }
        DeathCrash.EnsureRowOrderProbed();
        AsyncGPUReadback.WaitAllRequests();
        Check("the read-back's row order was probed", DeathCrash.RowsFlipped == 0 || DeathCrash.RowsFlipped == 1, "" + DeathCrash.RowsFlipped);
        var a = Blob(96, "~hitchCutA");
        var b = Blob(96, "~hitchCutB");
        bool done = false, ok = false;
        bool started = DeathCrash.FragmentsAsync(a, 3, r => { done = true; ok = r; });
        AsyncGPUReadback.WaitAllRequests();
        Sprite[] sa, sb; Vector2[] la, lb; float[] ra, rb;
        bool cached = DeathCrash.FragmentsCached(a, 3, out sa, out la, out ra);
        Check("an asynchronous cut delivers its pieces", started && done && ok && cached && sa != null && sa.Length == 3);
        bool sync = DeathCrash.FragmentsOf(b, 3, out sb, out lb, out rb);
        bool same = cached && sync && sa.Length == sb.Length;
        string why = "";
        for (int k = 0; same && k < sa.Length; k++)
        {
            var ta = sa[k].texture; var tb = sb[k].texture;
            if (ta.width != tb.width || ta.height != tb.height || (la[k] - lb[k]).sqrMagnitude > 1e-8f) { same = false; why = "piece " + k + " size / place"; break; }
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
                if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b || pa[i].a != pb[i].a) { same = false; why = "piece " + k + " pixel " + i; break; }
        }
        Check("... the very pieces the immediate read-back cuts: same sizes, places and pixels, the right way up", same, why);
        DeathCrash.ForgetTargetFragments();
        foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        made.Clear();
    }

    // ---- 9 ----
    static void PerFrameLookupsAllocateNothing()
    {
        long control;
        if (!TestHarness.AllocMeterWorks(out control)) { Check("the allocation meter sees allocations", false, "control " + control); return; }
        string key = ShipId.OwnedKey(2);
        long keys = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 200; i++) key = ShipId.OwnedKey(2 + (i & 7)); });
        Check("a ship's ownership key is built once (the leaderboard tracker built one every frame: 98 B)", keys == 0 && key.EndsWith("9"), keys + " B");
    }
}
