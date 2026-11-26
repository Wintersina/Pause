using System;
using UnityEditor;
using UnityEngine;

// The safe performance wins (perf/safe-wins): the deferred asset sweep runs only under cover, the score
// label's digits are written without an intermediate string and read exactly as before, and the menu
// music streams off disk instead of sitting decompressed in memory.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PerfSafeWinsTest
public static class PerfSafeWinsTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[PERF] PASS  " : "[PERF] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        try
        {
            Sweep();
            ScoreLabel();
            MenuMusic();
        }
        finally { AssetSweep.Reset(); }
        Debug.Log("[PERF] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- AssetSweep: a deferred Resources.UnloadUnusedAssets that only runs under cover ----
    static void Sweep()
    {
        AssetSweep.Reset();
        int runs = 0;
        AssetSweep.Unload = () => runs++;

        AssetSweep.Covered();
        Check("nothing pending: a covered frame sweeps nothing", runs == 0 && AssetSweep.Runs == 0 && !AssetSweep.Pending);

        AssetSweep.Defer();
        Check("a freed world asks for a sweep (pending, not run)", AssetSweep.Pending && runs == 0);
        AssetSweep.Defer();
        AssetSweep.Covered();
        Check("the next covered frame runs it once, however many asked", runs == 1 && AssetSweep.Runs == 1 && !AssetSweep.Pending);
        AssetSweep.Covered();
        AssetSweep.Covered();
        Check("later covered frames (the clouds hide the view for a while) sweep nothing more", runs == 1);

        AssetSweep.Defer();
        AssetSweep.Drop();
        AssetSweep.Covered();
        Check("a scene load drops the request (the load swept already)", runs == 1 && !AssetSweep.Pending);

        AssetSweep.Defer();
        AssetSweep.Now();
        Check("a caller under cover sweeps at once and clears the request", runs == 2 && !AssetSweep.Pending);
        AssetSweep.Now();
        Check("... pending or not", runs == 3);

        // The lift-off and the planetfall hide the whole view between their swap and their clouds parting:
        // the windows the sweep is allowed in.
        Check("planetfall: the world switches under the clouds and they part later (a covered window exists)",
              PlanetfallTimeline.SwitchAt < PlanetfallTimeline.CloudsOutFrom, PlanetfallTimeline.SwitchAt + " < " + PlanetfallTimeline.CloudsOutFrom);
        Check("planetfall: the backdrop crossfade that frees the old world ends inside that window",
              PlanetfallTimeline.SwitchAt + WorldBackdrop.CrossfadeSeconds <= PlanetfallTimeline.CloudsOutFrom,
              (PlanetfallTimeline.SwitchAt + WorldBackdrop.CrossfadeSeconds).ToString("F2"));
        Check("lift-off: the swap happens before the clouds break", LiftoffTimeline.SwapAt < LiftoffTimeline.BreakAt);

        // The in-play call sites: the backdrop crossfade defers, the lift-off swap sweeps under its clouds, the
        // planetfall's covered frames run a pending sweep, a single scene load drops it. Nothing in play calls
        // Resources.UnloadUnusedAssets directly any more.
        string backdrop = Source("Assets/Scripts/Worlds/Backdrop/WorldBackdrop.cs");
        string liftoff = Source("Assets/Scripts/Worlds/Planetfall/Liftoff.cs");
        string planetfall = Source("Assets/Scripts/Worlds/Planetfall/Planetfall.cs");
        string reset = Source("Assets/Scripts/Core/GameStateReset.cs");
        Check("WorldBackdrop defers the sweep when the outgoing set is destroyed", backdrop.Contains("AssetSweep.Defer()") && !backdrop.Contains("Resources.UnloadUnusedAssets"));
        Check("Liftoff.Swap sweeps under its clouds", liftoff.Contains("AssetSweep.Now()") && !liftoff.Contains("Resources.UnloadUnusedAssets"));
        Check("Planetfall runs a pending sweep on its covered frames", planetfall.Contains("AssetSweep.Covered()") && planetfall.Contains("CloudsOutFrom"));
        Check("a scene load drops a pending sweep", reset.Contains("AssetSweep.Drop()"));
        AssetSweep.Reset();
    }

    static string Source(string path)
    {
        try { return TestHarness.StripComments(System.IO.File.ReadAllText(path)); }
        catch (Exception) { return string.Empty; }
    }

    // ---- the score label: digits straight into a buffer, read exactly as N0 ----
    static void ScoreLabel()
    {
        long[] values =
        {
            0, 1, 7, 9, 10, 99, 100, 999, 1000, 1001, 9999, 12345, 99999, 100000, 999999, 1000000, 1234567, 7654321,
            99999999, 123456789, 999999999999L, 1234567890123L, long.MaxValue, long.MaxValue - 1,
            -1, -9, -10, -999, -1000, -1234567, long.MinValue + 1, long.MinValue,
        };
        var buf = new char[40];
        bool all = true;
        foreach (long v in values)
        {
            int n = RunScore.FormatInto(buf, 3, v);
            string got = new string(buf, 3, n - 3);
            string want = RunScore.Format(v);
            if (got != want) { all = false; Check("FormatInto(" + v + ") == N0", false, got + " vs " + want); }
            string label = ScoreHud.Label(v);
            if (label != ScoreHud.LabelPrefix + want) { all = false; Check("ScoreHud.Label(" + v + ") reads as before", false, label); }
        }
        Check("FormatInto writes every value exactly as N0 (invariant), ScoreHud.Label reads exactly as before (" + values.Length + " values)", all);
        Check("the HUD label still starts with SCORE and two spaces", ScoreHud.LabelPrefix == "SCORE  " && ScoreHud.Label(42) == "SCORE  42");
        Check("a run's biggest figures fit the HUD's buffer", ScoreHud.Label(long.MaxValue).Length == ScoreHud.LabelPrefix.Length + 25 && ScoreHud.Label(long.MinValue).Length == ScoreHud.LabelPrefix.Length + 26);

        // 1,234,567 (steady) and a rolling figure: the writer itself allocates nothing.
        long control;
        if (!TestHarness.AllocMeterWorks(out control, currentThreadOnly: true)) { Check("allocation meter works (control " + control + " B)", false); return; }
        long rolling = 1234567;
        Action write = () => { for (int i = 0; i < 64; i++) RunScore.FormatInto(buf, 0, rolling + i); };
        write();
        long bytes = TestHarness.AllocatedBytes(write, currentThreadOnly: true);
        Check("FormatInto allocates nothing (64 figures)", bytes == 0, bytes + " B");
        BackgroundAllocations(write);
        for (int i = 0; i < 8; i++) ScoreHud.Label(rolling + i);   // JIT and the buffer
        string made = null;
        long one = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 64; i++) made = ScoreHud.Label(rolling + i); }, currentThreadOnly: true);
        long two = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 64; i++) made = ScoreHud.LabelPrefix + RunScore.Format(rolling + i); }, currentThreadOnly: true);
        Check("ScoreHud.Label allocates no more than the one string it returns (" + one + " B for 64 labels; prefix + N0 " + two + " B)", one > 0 && one <= two);
    }

    // A worker allocating inside the measurement must be visible to the default meter, while a
    // synchronous score formatter still measures zero on its own thread. The meter's positive control
    // above independently checks that real allocations on the calling thread are caught.
    static void BackgroundAllocations(Action write)
    {
        using var start = new System.Threading.AutoResetEvent(false);
        using var done = new System.Threading.AutoResetEvent(false);
        bool stop = false;
        object sink = null;
        var worker = new System.Threading.Thread(() =>
        {
            while (true)
            {
                start.WaitOne();
                if (stop) break;
                for (int i = 0; i < 256; i++) sink = new byte[64];
                done.Set();
            }
        });
        worker.Start();
        try
        {
            Action concurrent = () => { start.Set(); done.WaitOne(); write(); };
            concurrent(); // warm the event waits and the worker before recording
            long allThreads = TestHarness.AllocatedBytes(concurrent, currentThreadOnly: false);
            long formatterThread = TestHarness.AllocatedBytes(concurrent);
            Check("the explicit process-wide meter sees concurrent worker allocations", allThreads > 0, allThreads + " B");
            long control;
            Check("the default meter still catches calling-thread allocations", TestHarness.AllocMeterWorks(out control), control + " B");
            long tiny = TestHarness.AllocatedBytes(() => { sink = new byte[1]; });
            Check("one tiny allocation is visible even when marker timing rounds to zero", tiny > 0, tiny + " B");
            long globalControl = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 256; i++) sink = new byte[64]; }, currentThreadOnly: false);
            Check("the explicit process-wide meter still catches calling-thread allocations", globalControl > 0, globalControl + " B");
            Check("the default meter excludes a concurrent worker while score formatting stays allocation-free", formatterThread == 0, formatterThread + " B");
        }
        finally
        {
            stop = true;
            start.Set();
            worker.Join();
            GC.KeepAlive(sink);
        }
    }

    // ---- the menu's music streams: no decompressed copy of a song in memory ----
    static void MenuMusic()
    {
        foreach (var name in new[] { "MovingMusic", "StoreMusic", "Tutorial", "MainMenuMusic" })
        {
            string path = "Assets/Audio/Music/" + name + ".wav";
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) { Check(name + ".wav imports", false); continue; }
            var s = imp.defaultSampleSettings;
            Check(name + " streams (no decompressed copy in memory)", s.loadType == AudioClipLoadType.Streaming, s.loadType.ToString());
            Check(name + " stays Vorbis at its quality (the sound is unchanged)", s.compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Abs(s.quality - .5f) < .011f);
            Check(name + " has no platform override undoing it", !imp.ContainsSampleSettingsOverride("Android") && !imp.ContainsSampleSettingsOverride("iOS"));
        }
    }
}
