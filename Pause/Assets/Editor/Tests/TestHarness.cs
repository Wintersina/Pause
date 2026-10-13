using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Shared plumbing for the batch-mode *Test suites.
//
// Every suite exposes `int Execute()` (returns its failure count) and keeps a
// `Run()` batch entry point that calls Exit() with it, so both of these work:
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShopTest.Run
//   Unity -batchmode -quit -projectPath Pause -executeMethod AllTests.RunAll
//
// Running many suites in one editor process means one suite's leftovers --
// the scene it opened, the PlayerPrefs it wrote, the game's many static
// fields (playerDied, pauseCounter, lifeCounter, ...) -- would otherwise leak
// into the next. Sandbox puts all of that back the way it found it.
public static class TestHarness
{
    // Set by AllTests.RunFast for its run only; always false otherwise.
    public static bool Fast;
    public static int SkippedSlow;

    // Wrap a long simulation / big render check: `if (TestHarness.Slow("...")) LongCheck();`
    // Runs it everywhere except AllTests.RunFast, which logs the skip.
    // Two lanes. Unset = GATE lane (the default run: exhaustive sweeps keep one
    // representative variant / device subset); PAUSE_TEST_FULL=1 = FULL lane
    // (every variant, every device, every seed: what a release gate runs).
    public static bool FullLane { get { return System.Environment.GetEnvironmentVariable("PAUSE_TEST_FULL") == "1"; } }

    // The variants of a world's backdrop an exhaustive sweep visits: 1..n in
    // the full lane, just variant 1 in the gate lane.
    public static int SweepVariants(int n) { return FullLane ? n : 1; }

    // Which worlds of a per-world simulation sweep run: all in the full lane,
    // every third (first, middle, last of seven) in the gate lane.
    public static bool SweepWorld(int w) { return FullLane || w % 3 == 0; }

    public static bool Slow(string what)
    {
        if (!Fast) return true;
        SkippedSlow++;
        Debug.Log("[FAST] skipped slow check: " + what);
        return false;
    }

    // ---- allocation meter -------------------------------------------------
    //
    // Managed bytes allocated while `work` runs, from the profiler's GC.Alloc
    // recorder. (GC.GetAllocatedBytesForCurrentThread always reads 0 under
    // this Unity's Mono, so an "allocates nothing" check built on it can
    // never fail; EnemyBehaviourTest found the recorder to be the meter that
    // works here.) Warm `work` up first: a first call JITs and fills caches.
    static long AllocatedBytesOnce(Action work)
    {
        using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc", 1,
                   Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.StartImmediately))
        {
            if (!rec.Valid) return -1;
            long before = rec.CurrentValue;
            work();
            return rec.CurrentValue - before;
        }
    }

    // The recorder is process-wide: a background thread (profiler, asset import, the other editor
    // services) allocating during `work` adds a stray 80-250 bytes to a reading, which made "allocates
    // nothing" checks flake under load (125 / 208 / 249 B seen for code that allocates 0). So a reading
    // above zero is re-measured, up to NoiseRepeats more times, and the SMALLEST is reported: a real
    // allocation in `work` shows up in every repeat, a stray one does not. Zero readings are returned at
    // once (nothing to filter), so no check gets stricter. `work` must therefore be repeatable (every
    // caller steps a warmed-up sim, which is).
    const int NoiseRepeats = 4;
    public static long AllocatedBytes(Action work)
    {
        long best = AllocatedBytesOnce(work);
        for (int i = 0; i < NoiseRepeats && best > 0; i++)
        {
            long again = AllocatedBytesOnce(work);
            if (again < best) best = again;
        }
        return best;
    }

    // The positive control every allocation check runs first: the meter must
    // see allocations, or it is blind and the check must fail rather than
    // pass on a zero. The recorder's reading is a LOWER bound that wobbles
    // with load (~17 bytes per 32-byte array on a quiet editor, but 584 and
    // 791 for 100 arrays on a busy machine, below the old 800 floor): so the
    // control allocates ControlRepeat x AllocControlCount arrays per attempt,
    // warms up, collects first, and takes the best of several attempts. The
    // floor stays 8 bytes per array of AllocControlCount (i.e. a 4x margin
    // against the bytes actually allocated), and "0" is still the only
    // reading that means "nothing": a real per-frame allocation in the code
    // under test is measured by AllocatedBytes exactly as before (a single,
    // unfiltered reading), so it is still caught.
    public const int AllocControlCount = 100;
    const int ControlRepeat = 4, ControlAttempts = 5;
    static object allocSink;

    public static bool AllocMeterWorks(out long controlBytes)
    {
        Action control = () => { for (int i = 0; i < AllocControlCount * ControlRepeat; i++) allocSink = new byte[32]; };
        control();   // JIT + caches
        GC.Collect();
        controlBytes = 0;
        for (int attempt = 0; attempt < ControlAttempts; attempt++)
        {
            long b = AllocatedBytes(control);
            if (b > controlBytes) controlBytes = b;
            if (controlBytes >= AllocControlCount * 8) break;
        }
        if (controlBytes < AllocControlCount * 8) return false;
        // Unit check that a REAL allocation in code under test is still caught, through the very
        // AllocatedBytes() the checks use (noise filter included): 64 small arrays per call -- a per-frame
        // leak of a few KB, far below any sim's total -- must read > 0 every time, and a call that
        // allocates nothing must read 0. (A single array can read 0: the recorder is a lower bound.)
        Action leak = () => { for (int i = 0; i < 64; i++) allocSink = new byte[64]; };
        Action clean = () => { };
        leak(); clean();
        return AllocatedBytes(leak) > 0 && AllocatedBytes(clean) == 0;
    }

    // ---- GPU memory --------------------------------------------------------
    //
    // A batch -executeMethod never ends an editor frame, and on Metal the
    // GPU side of every texture / buffer the game makes and destroys is only
    // given back once the commands that used it are submitted. Without that,
    // a suite that builds and drops art in a loop grows the editor's
    // IOAccelerator memory without bound (TitleScreenTrafficTest: 6.7 GB by
    // its end, all of it outside Unity's own allocators). GL.Flush() submits
    // them; it is cheap. Call it between cases and every FlushEvery steps of
    // a long simulation; the harness calls it on scene opens and suite ends.
    public const int FlushEvery = 600;
    public static void FlushGpu() { GL.Flush(); }

    // Batch entry: non-zero exit code on any failure so CI/scripts notice.
    public static void Exit(int failures)
    {
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // `using var sandbox = new TestHarness.Sandbox();` at the top of Execute().
    public sealed class Sandbox : IDisposable
    {
        readonly Dictionary<string, object> prefs = new Dictionary<string, object>();
        readonly List<KeyValuePair<FieldInfo, object>> statics = new List<KeyValuePair<FieldInfo, object>>();
        readonly List<KeyValuePair<object, object[]>> collections = new List<KeyValuePair<object, object[]>>();
        readonly float timeScale;

        public Sandbox()
        {
            foreach (string key in PrefsKeys()) prefs[key] = ReadPref(key);
            foreach (var field in GameStaticFields())
            {
                object value;
                try { value = field.GetValue(null); }
                catch (Exception) { continue; /* type initializer threw; nothing to restore */ }

                // A readonly field can't be reassigned, but the collection it
                // holds can be put back. This matters for the static sprite
                // caches (shopingShips.runtimeSprites, ShipHullArt's caches):
                // the editor destroys their Sprite.Create()d entries when the
                // next scene opens, and a stale entry is never reloaded.
                if (field.IsInitOnly)
                {
                    if (value is IDictionary dict)
                    {
                        var entries = new object[dict.Count];
                        dict.CopyTo(entries, 0);
                        collections.Add(new KeyValuePair<object, object[]>(dict, entries));
                    }
                    else if (value is IList list)
                    {
                        var items = new object[list.Count];
                        list.CopyTo(items, 0);
                        collections.Add(new KeyValuePair<object, object[]>(list, items));
                    }
                    continue;
                }
                statics.Add(new KeyValuePair<FieldInfo, object>(field, value));
            }
            timeScale = Time.timeScale;
            KeepRuntimeArt(true);

            // Start every suite with developer mode off. The editor shares
            // PlayerPrefs with the Mac player, where a developer build turns
            // it on by default; suites that test it turn it on themselves,
            // and with it already on, its on -> off restore (the snapshot it
            // takes only on an off -> on change) replays a stale snapshot.
            // Written raw, not via SetEnabled, so nothing is restored here;
            // the key itself is put back on Dispose like every other pref.
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

            // The suites written for the full 16-ship roster run with Paragon's
            // feature flag on (explicitly, whatever its default). The
            // suite that tests the flag itself (ParagonFlagTest) turns it off.
            FeatureFlags.Set(FeatureFlags.ParagonShip, true);

            // Likewise every ship starts in its stock skin: suites that check
            // the hull art read the stock sheet's source pixels, and a skin
            // equipped in the Mac player would otherwise show up here. (Put
            // back on Dispose with the other prefs.)
            for (int id = 0; id <= shopingShips.RosterTotal; id++)
            {
                PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(id));
                PlayerPrefs.DeleteKey(ShipSkins.DeveloperEquippedKey(id));
            }
            ShipSkins.ClearPreview();

            // And with no colours bought, so every ship's weapon starts at
            // level 0 (ShipWeaponUpgrades) whatever the Mac player bought.
            for (int id = 0; id <= shopingShips.RosterTotal; id++)
                for (int n = 1; n < ShipSkins.PerShip; n++)
                    PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }

        public void Dispose()
        {
            // Drop whatever scene the suite opened or dirtied (never saved).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            KeepRuntimeArt(false);

            foreach (var pair in statics)
            {
                try { pair.Key.SetValue(null, pair.Value); }
                catch (Exception) { }
            }
            foreach (var pair in collections)
            {
                try
                {
                    if (pair.Key is IDictionary dict)
                    {
                        dict.Clear();
                        foreach (DictionaryEntry e in pair.Value) dict[e.Key] = e.Value;
                    }
                    else if (pair.Key is IList list && list.IsFixedSize)
                    {
                        for (int i = 0; i < pair.Value.Length; i++) list[i] = pair.Value[i];
                    }
                    else if (pair.Key is IList growable)
                    {
                        growable.Clear();
                        foreach (var item in pair.Value) growable.Add(item);
                    }
                }
                catch (Exception) { }
            }
            Time.timeScale = timeScale;

            foreach (var pair in prefs)
            {
                if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key);
                else if (pair.Value is string s) PlayerPrefs.SetString(pair.Key, s);
                else if (pair.Value is int i) PlayerPrefs.SetInt(pair.Key, i);
                else PlayerPrefs.SetFloat(pair.Key, (float)pair.Value);
            }
            PlayerPrefs.Save();

            // Free what the suite made. Released art is only unloadable, not
            // unloaded: left alive it was re-kept by the next suite's first
            // scene swap and so on to the end of the run (RunAll grew to
            // 5-8 GB, ScreenFitTest alone kept 12k+ textures). Now that the
            // suite's statics are put back, nothing of it is referenced.
            if (keepDepth == 0) EditorUtility.UnloadUnusedAssetsImmediate(true);
            FlushGpu();
        }
    }

    // `target.SendMessage(method)` for per-frame loops. In edit mode every
    // SendMessage logs a "ShouldRunBehaviour()" assertion before it calls the
    // method; at 100k+ frames those log lines (each with a stack trace
    // unless AllTests turned traces off) were most of a suite's time. This calls the very
    // same methods directly. It only takes the fast path when the outcome is
    // unambiguous -- an active object whose receivers are all enabled, with
    // one parameterless, non-coroutine `method` each -- and otherwise is
    // SendMessage itself. As with SendMessage, an exception in a receiver is
    // logged, not thrown.
    public static void Send(Component target, string method)
    {
        if (target == null || !target.gameObject.activeInHierarchy) { target.SendMessage(method); return; }
        var receivers = target.GetComponents<MonoBehaviour>();
        var calls = new List<KeyValuePair<MonoBehaviour, MethodInfo>>(receivers.Length);
        foreach (var mb in receivers)
        {
            if (mb == null) continue;
            if (!Receiver(mb.GetType(), method, out MethodInfo m) || (m != null && !mb.enabled))
            {
                target.SendMessage(method);
                return;
            }
            if (m != null) calls.Add(new KeyValuePair<MonoBehaviour, MethodInfo>(mb, m));
        }
        if (calls.Count == 0) { target.SendMessage(method); return; }
        foreach (var call in calls)
        {
            try { call.Value.Invoke(call.Key, null); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, call.Key); }
        }
    }

    static readonly Dictionary<(Type, string), MethodInfo> receiverCache = new Dictionary<(Type, string), MethodInfo>();
    static readonly HashSet<(Type, string)> ambiguous = new HashSet<(Type, string)>();

    // The method SendMessage would call on a `type` (null if none); false if
    // only SendMessage itself can say (overloads, parameters, coroutines).
    static bool Receiver(Type type, string method, out MethodInfo found)
    {
        var key = (type, method);
        if (ambiguous.Contains(key)) { found = null; return false; }
        if (receiverCache.TryGetValue(key, out found)) return true;
        found = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            var named = Array.FindAll(t.GetMethods(flags), m => m.Name == method);
            if (named.Length == 0) continue;
            var m0 = named[0];
            if (named.Length > 1 || m0.GetParameters().Length != 0 || m0.ReturnType != typeof(void) ||
                m0.IsGenericMethodDefinition)
            {
                ambiguous.Add(key);
                return false;
            }
            found = m0;
            break;
        }
        receiverCache[key] = found;
        return true;
    }

    // Every editor scene swap (NewScene / OpenScene) unloads "unused" assets,
    // and that includes the sprites and textures the game builds at runtime
    // (Sprite.Create / new Texture2D) and caches in statics: the caches then
    // hold dead entries and rebuild them. In a player those caches live for
    // the whole session. Rebuilding is expensive in the editor -- the build
    // target's textures are ETC2, and Sprite.Create's tight outline decodes
    // the whole atlas on the CPU (~0.5-1s for the gun roster) -- and suites
    // that make a fresh scene per case paid it hundreds of times (minutes).
    // So, inside a Sandbox, runtime-made sprites/textures survive scene swaps
    // like they do in the game; when the Sandbox ends they become unloadable
    // again, so suites still don't share them.
    static int keepDepth;
    static readonly List<UnityEngine.Object> kept = new List<UnityEngine.Object>();

    static void KeepRuntimeArt(bool begin)
    {
        if (begin)
        {
            if (keepDepth++ == 0)
            {
                EditorSceneManager.sceneClosing += OnSceneClosing;
                EditorSceneManager.sceneOpening += OnSceneOpening;
                EditorSceneManager.sceneOpened += OnSceneOpened;
                opensSincePrune = 0;
            }
            return;
        }
        if (keepDepth == 0 || --keepDepth > 0) return;
        EditorSceneManager.sceneClosing -= OnSceneClosing;
        EditorSceneManager.sceneOpening -= OnSceneOpening;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        foreach (var o in kept)
            if (o != null) o.hideFlags &= ~HideFlags.DontSave;
        kept.Clear();
    }

    // AllTests: slice the shared FX atlases once for the whole run instead
    // of once per suite. ShipFxArt's sheets are 480 Sprite.Create()s over
    // two ETC2 atlases (~4.4s); a dozen suites that build a ship each paid
    // it. They depend on nothing but the imported textures, so every suite
    // gets the same sprites it would have sliced itself -- each Sandbox
    // snapshots the warm cache and puts it back. Released by EndRun.
    static readonly List<UnityEngine.Object> runKept = new List<UnityEngine.Object>();

    public static void BeginRun()
    {
        ShipFxArt.AttackLoop(ShipId.Starter, 0);
        ShipFxArt.MeterFill(ShipId.Starter, 0);
        foreach (var o in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if ((o.hideFlags & HideFlags.DontSave) != 0 || EditorUtility.IsPersistent(o)) continue;
            o.hideFlags |= HideFlags.DontSave;
            runKept.Add(o);
        }
    }

    public static void EndRun()
    {
        foreach (var o in runKept)
            if (o != null) o.hideFlags &= ~HideFlags.DontSave;
        runKept.Clear();
    }

    static void OnSceneClosing(UnityEngine.SceneManagement.Scene scene, bool removing) { KeepNow(); }
    static void OnSceneOpening(string path, OpenSceneMode mode) { KeepNow(); }

    // KeepNow keeps everything runtime-made, also the art that only the
    // closing scene's objects used (a Portal's ring, a label's texture, ...),
    // so a suite that opens a scene per case piles up a copy per case
    // (ScreenFitTest: ~75 textures, ~40 MB, per scene). Every few opens, once
    // the old scene is gone, let go of all of it and unload what nothing
    // references -- managed references count as roots, so the statics'
    // caches survive -- then keep the survivors again.
    const int PruneEvery = 4;
    static int opensSincePrune;

    static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        FlushGpu();
        if (++opensSincePrune < PruneEvery) return;
        opensSincePrune = 0;
        foreach (var o in kept)
            if (o != null) o.hideFlags &= ~HideFlags.DontSave;
        kept.Clear();
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        KeepNow();
    }

    static void KeepNow()
    {
        Keep(Resources.FindObjectsOfTypeAll<Sprite>());
        Keep(Resources.FindObjectsOfTypeAll<Texture2D>());
    }

    static void Keep(UnityEngine.Object[] objects)
    {
        foreach (var o in objects)
        {
            if ((o.hideFlags & HideFlags.DontSave) != 0) continue;
            if (EditorUtility.IsPersistent(o)) continue;   // an asset: reloads from disk anyway
            o.hideFlags |= HideFlags.DontSave;
            kept.Add(o);
        }
    }

    // Every PlayerPrefs key the game writes that a suite can reach, directly
    // or through component Start()s (shop, WorldManager, DeveloperUnlocks).
    static IEnumerable<string> PrefsKeys()
    {
        var keys = new List<string>
        {
            "spawnShip", "HasDoneTut", "PlayerCurrecny", "HighestSpeed", RunScore.BestScoreKey,
            WorldManager.PrefsCurrentWorld, WorldManager.PrefsHighestWorld,
            DeveloperUnlocks.EnabledKey, DeveloperUnlocks.SelectedWorldKey,
            DeveloperUnlocks.ChoiceBuildKey, PlayerStartWorld.Key, SoundSettings.Key, SoundSettings.SfxOnKey, SoundSettings.MusicOnKey, SoundSettings.SfxVolumeKey, SoundSettings.MusicVolumeKey,
            Codex.PrefsKey, Codex.NewKey, Codex.AckKey, Codex.PaidKey,
        };
        foreach (var flag in FeatureFlags.All) keys.Add(FeatureFlags.KeyOf(flag.name));
        for (int i = 0; i <= shopingShips.RosterTotal; i++) keys.Add("boughtship" + i);

        // Hull skins: equipped index, ownership, and developer-mode equips.
        for (int i = 0; i <= shopingShips.RosterTotal; i++)
        {
            keys.Add(ShipSkins.EquippedKey(i));
            keys.Add(ShipSkins.DeveloperEquippedKey(i));
            for (int n = 0; n < ShipSkins.PerShip; n++) keys.Add(ShipSkins.OwnedKey(i, n));
        }

        // Achievement state (unlocked / claimed / counters / store marks / schema) and the legacy counts it migrates from.
        keys.AddRange(AchievementStore.AllKeys());
        keys.AddRange(AchievementMigration.LegacyKeys());

        // Account cloud-save bookkeeping.
        keys.Add(CloudSync.LastAccountKey);
        keys.Add(CloudSync.LocalSavedAtKey);
        keys.Add(CloudSync.LocalHashKey);
        keys.Add(CloudSync.BackupsKey);
        keys.Add(AccountLink.DisconnectedKey);
        keys.Add(AccountLink.HintShownKey);

        // Leaderboard queue (device-local).
        keys.Add(LeaderboardService.PendingKey);
        keys.Add(LeaderboardService.SubmittedKey);

        // DeveloperUnlocks keeps its own backup copies of the progress keys.
        var backed = new List<string>
        {
            "HasDoneTut", WorldManager.PrefsHighestWorld, WorldManager.PrefsCurrentWorld,
        };
        for (int i = 0; i <= shopingShips.RosterTotal; i++) backed.Add("boughtship" + i);
        foreach (string k in backed)
        {
            keys.Add("developerBackup_" + k);
            keys.Add("developerBackup_" + k + "_exists");
        }
        return keys;
    }

    // PlayerPrefs has no type query; a typed getter returns the default for a
    // key stored as another type, so probe with impossible defaults.
    static object ReadPref(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return null;
        const string probe = "\u0001~unset~";
        string s = PlayerPrefs.GetString(key, probe);
        if (s != probe) return s;
        int i = PlayerPrefs.GetInt(key, int.MinValue);
        if (i != int.MinValue) return i;
        return PlayerPrefs.GetFloat(key);
    }

    // Statics of the game's own scripts (Assembly-CSharp), not Unity's or the
    // editor tests'.
    static IEnumerable<FieldInfo> GameStaticFields()
    {
        var gameAssembly = typeof(shopingShips).Assembly;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in gameAssembly.GetTypes())
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var field in type.GetFields(flags))
            {
                if (field.IsLiteral) continue;
                yield return field;
            }
        }
    }
}
