using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// The blue-atom pickup for EVERY ship in EVERY skin (15 x 5), on the real
// gameplay ship: gameS1, the prefab spawnShips spawns, the skin equipped
// through ShipSkins, every component started, its engine attached and a few
// frames flown. Then the first blue atom, the frames that follow it (shield
// zip, boost plumes, hull flipbook), the shield running out, and a second
// blue atom.
//
// A pickup must only flip state. For every ship x skin this fails if the
// pickup frame, or any frame while the shield is up:
//   - builds a shield contour or reads pixels back from the GPU
//   - sends a collider path to physics
//   - creates a texture, sprite, material, mesh or GameObject
//   - decodes a skin sheet
//   - plays a boost sting whose audio is not in memory yet (each world has
//     its own; an unloaded clip is read and decompressed inside Play())
// and it prints the table (builds, readbacks, objects made, ms, bytes).
// A few ships are also flown the way a developer build flies a skin
// (developer mode's own equip key).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShieldPickupSkinTest.Run
public static class ShieldPickupSkinTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPS] PASS  " : "[SPS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    const int FramesBefore = 6;
    const int FramesShielded = 45;     // anticipation + zip + idle + a full plume loop

    // What one measured stretch did.
    struct Cost
    {
        public int builds, readbacks, paths, skinSheets;
        public int textures, sprites, materials, meshes, objects;
        public double ms;
        public long bytes;
        public string made;

        public bool Clean
        {
            get
            {
                return builds == 0 && readbacks == 0 && paths == 0 && skinSheets == 0 &&
                       textures == 0 && sprites == 0 && materials == 0 && meshes == 0 && objects == 0;
            }
        }

        public string Cells()
        {
            return builds + " | " + readbacks + " | " + paths + " | " + textures + " | " + sprites + " | " +
                   materials + " | " + meshes + " | " + objects + " | " + ms.ToString("F3") + " | " + bytes;
        }
    }

    sealed class Probe
    {
        int builds, readbacks, paths, skinSheets;
        HashSet<int> textures, sprites, materials, meshes, objects;
        long bytes;
        readonly Stopwatch sw = new Stopwatch();

        static HashSet<int> Ids<T>() where T : Object
        {
            var set = new HashSet<int>();
            foreach (var o in Resources.FindObjectsOfTypeAll<T>()) set.Add(o.GetInstanceID());
            return set;
        }

        static int Fresh<T>(HashSet<int> before, StringBuilder names) where T : Object
        {
            int n = 0;
            foreach (var o in Resources.FindObjectsOfTypeAll<T>())
            {
                if (before.Contains(o.GetInstanceID())) continue;
                n++;
                if (names.Length < 400) names.Append(typeof(T).Name).Append(':').Append(o.name).Append(' ');
            }
            return n;
        }

        public void Begin()
        {
            builds = ShieldContour.BuildCount;
            readbacks = ShieldContour.ReadbackCount;
            paths = ShipHitbox.PathWrites;
            skinSheets = ShipHullArt.LoadedSkinSheets;
            textures = Ids<Texture>();
            sprites = Ids<Sprite>();
            materials = Ids<Material>();
            meshes = Ids<Mesh>();
            objects = Ids<GameObject>();
            bytes = GC.GetTotalMemory(false);
            sw.Restart();
        }

        public Cost End()
        {
            sw.Stop();
            var c = new Cost();
            c.bytes = GC.GetTotalMemory(false) - bytes;
            c.ms = sw.Elapsed.TotalMilliseconds;
            c.builds = ShieldContour.BuildCount - builds;
            c.readbacks = ShieldContour.ReadbackCount - readbacks;
            c.paths = ShipHitbox.PathWrites - paths;
            c.skinSheets = Mathf.Max(0, ShipHullArt.LoadedSkinSheets - skinSheets);
            var names = new StringBuilder();
            c.textures = Fresh<Texture>(textures, names);
            c.sprites = Fresh<Sprite>(sprites, names);
            c.materials = Fresh<Material>(materials, names);
            c.meshes = Fresh<Mesh>(meshes, names);
            c.objects = Fresh<GameObject>(objects, names);
            c.made = names.ToString();
            return c;
        }
    }

    // ------------------------------------------------------------- the rig

    public static void Prepare(int id, int skin, bool developer = false)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        ShipSkins.ClearPreview();
        foreach (int s in ShipId.All)
        {
            PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(s));
            PlayerPrefs.DeleteKey(ShipSkins.DeveloperEquippedKey(s));
            for (int n = 0; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(s, n));
        }
        PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
        // Developer mode owns nothing for real: every skin reads as owned and
        // the equip goes to its own key (ShipSkins), which is how a developer
        // build flies a skin. Written raw, so no progress snapshot is taken.
        if (developer) PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        else if (skin != ShipSkins.Stock) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, skin), 1);
        ShipSkins.Equip(id, skin);
        ShipId.Equip(id);
        // What a scene load does (ShipHullArt.OnSceneLoaded, PickupBurst, Codex).
        ShipHullArt.ReleaseUnusedSkins();
        PickupBurst.Prewarm();
        Codex.Prewarm();
        collisionDetection.lifeCounter = 0;
        collisionDetection.MAXLIFE = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.invTimer = 0f;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        // No stand-in boost holder: collisionDetection.Start must find (and
        // switch off) the ship's own, or the boost plumes burn from the start.
    }

    // The gameplay ship as it is a few frames into a run.
    public static GameObject Spawn(int id, int skin, bool developer = false)
    {
        Prepare(id, skin, developer);
        var spawner = Object.FindFirstObjectByType<spawnShips>();
        if (spawner == null) spawner = new GameObject("~spawner").AddComponent<spawnShips>();
        var go = spawner.Spawn(id);
        if (go == null) return null;
        var cd = go.GetComponent<collisionDetection>();
        TestHarness.Send(cd, "Awake");
        TestHarness.Send(cd, "Start");
        if (cd.boost == null || !cd.boost.transform.IsChildOf(go.transform) || cd.boost.activeSelf)
            Check(go.name + ": the ship's own boost holder is found and off until a blue atom", false);
        // ShipThrusterAttach, a frame or two after the scene loads.
        if (ShipExhaust.UsesSpinDrift(ShipExhaust.IndexFor(go)))
        {
            if (go.GetComponent<ShipSpinDrift>() == null) Call(go.AddComponent<ShipSpinDrift>(), "Start");
        }
        else if (go.GetComponent<ShipThruster>() == null) Call(go.AddComponent<ShipThruster>(), "Start");
        return go;
    }

    static readonly Dictionary<(Type, string), MethodInfo> methods = new Dictionary<(Type, string), MethodInfo>();

    static void Call(MonoBehaviour mb, string method)
    {
        if (mb == null) return;
        var key = (mb.GetType(), method);
        MethodInfo m;
        if (!methods.TryGetValue(key, out m))
        {
            m = mb.GetType().GetMethod(method, Inst, null, Type.EmptyTypes, null);
            methods[key] = m;
        }
        if (m == null) return;
        try { m.Invoke(mb, null); }
        catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, mb); }
    }

    static readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>();

    // One frame of the ship and everything on it: Update, then LateUpdate,
    // with the shield and the plumes stepped a real 1/60 s (edit mode has no
    // frame clock).
    public static void Frame(GameObject go)
    {
        go.GetComponentsInChildren(false, behaviours);
        foreach (var mb in behaviours)
            if (mb != null && mb.enabled && !(mb is movePlayer)) Call(mb, "Update");
        var shield = go.GetComponent<ShipShield>();
        if (shield != null) shield.Tick(Dt);
        var life = go.GetComponent<lifeControler>();
        if (life != null && life.HullAnimator != null) life.HullAnimator.Step(Dt, Dt, collisionDetection.lifeCounter);
        go.GetComponentsInChildren(false, behaviours);
        foreach (var mb in behaviours)
        {
            if (mb == null || !mb.enabled) continue;
            Call(mb, "LateUpdate");
            var book = mb as ShipFlameFlipbook;
            if (book != null) book.Step(Dt);
            var drift = mb as ShipSpinDrift;
            if (drift != null) drift.Step(Dt);
        }
    }

    public static GameObject Atom(Vector3 at)
    {
        var atom = new GameObject("atom3a(Clone)", typeof(CircleCollider2D), typeof(SpriteRenderer));
        atom.tag = "pickUp";
        atom.transform.position = at;
        return atom;
    }

    static void Despawn(GameObject go)
    {
        if (go != null) Object.DestroyImmediate(go);
        foreach (var name in new[] { "atom3a(Clone)", "~ShieldShards" })
            for (var g = GameObject.Find(name); g != null; g = GameObject.Find(name)) Object.DestroyImmediate(g);
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        musicControl.boostMusicChanger = false;
    }

    // ------------------------------------------------------------- the run

    struct Row
    {
        public int id, skin;
        public bool developer;
        public int unbaked, lateBuilds;
        public Cost spawn, first, shielded, second;
        public bool shieldUp, sameContour;
        public string world;
        public bool stingReady;       // the boost sting's audio is in memory (or loading off the main thread)
        public string sting;
    }

    // The blue atom's sound must already be decoded when it plays: a clip
    // still Unloaded is read and decompressed inside AudioSource.Play().
    static bool Ready(AudioClip clip)
    {
        return clip == null || clip.loadState != AudioDataLoadState.Unloaded || clip.loadInBackground;
    }

    static Row Fly(int id, int skin, WorldTheme world, bool developer = false)
    {
        var row = new Row { id = id, skin = skin, world = world.displayName, developer = developer };
        var probe = new Probe();
        int unbaked = ShieldContour.UnbakedCount, late = ShipShield.LateBuilds;

        probe.Begin();
        var go = Spawn(id, skin, developer);
        if (go == null) return row;
        // The run starts in (or has just flown into) this world.
        WorldMusic.Apply(world);
        for (int i = 0; i < FramesBefore; i++) Frame(go);
        row.spawn = probe.End();

        var cd = go.GetComponent<collisionDetection>();
        var clip = cd.boostSound != null ? cd.boostSound.clip : null;
        row.stingReady = Ready(clip);
        row.sting = clip == null ? "none" : clip.name + " " + clip.loadState;
        var trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), cd,
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
        var tick = (Action)Delegate.CreateDelegate(typeof(Action), cd,
            typeof(collisionDetection).GetMethod("turnTextsOff", Inst));

        // The first blue atom.
        var atom = Atom(go.transform.position + Vector3.up * .2f);
        var col = atom.GetComponent<Collider2D>();
        probe.Begin();
        trigger(col);
        row.first = probe.End();
        var shield = go.GetComponent<ShipShield>();
        row.shieldUp = shield != null && shield.IsUp && collisionDetection.atomCheck;
        row.sameContour = shield != null && ReferenceEquals(shield.Contour, ShieldContour.ForShip(id));

        // The frames it is up for.
        probe.Begin();
        for (int i = 0; i < FramesShielded; i++) Frame(go);
        row.shielded = probe.End();
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        Object.DestroyImmediate(atom);

        // It runs out (the shatter), a few frames pass, and a second atom.
        collisionDetection.invTimer = 0f;
        tick();
        for (int i = 0; i < FramesBefore; i++) Frame(go);
        atom = Atom(go.transform.position + Vector3.up * .2f);
        col = atom.GetComponent<Collider2D>();
        probe.Begin();
        trigger(col);
        for (int i = 0; i < FramesShielded; i++) Frame(go);
        row.second = probe.End();
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        Object.DestroyImmediate(atom);
        collisionDetection.invTimer = 0f;
        tick();

        row.unbaked = ShieldContour.UnbakedCount - unbaked;
        row.lateBuilds = ShipShield.LateBuilds - late;
        Despawn(go);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        return row;
    }

    // Belt and braces for the sting: its import settings, per world.
    static void StingsAreImportedPreloaded()
    {
        int bad = 0, seen = 0;
        foreach (var theme in WorldManager.Worlds)
        {
            var clip = Resources.Load<AudioClip>("BoostSounds/" + theme.displayName + "Boost");
            if (clip == null) continue;
            seen++;
            var importer = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(clip)) as UnityEditor.AudioImporter;
            if (importer == null || !importer.defaultSampleSettings.preloadAudioData || !importer.loadInBackground)
            {
                bad++;
                Check(theme.displayName + "'s boost sting is imported Preload Audio Data + Load In Background", false);
            }
        }
        Check("every world's boost sting is imported preloaded, loading off the main thread (" + seen + " stings)",
              seen > 0 && bad == 0);
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        // Every pickup ends in Destroy(pickup), refused in edit mode with a
        // logged error a player never pays for: no stack traces while timing.
        var traces = new Dictionary<LogType, StackTraceLogType>();
        foreach (LogType t in Enum.GetValues(typeof(LogType)))
        {
            traces[t] = Application.GetStackTraceLogType(t);
            Application.SetStackTraceLogType(t, StackTraceLogType.None);
        }
        var rows = new List<Row>();
        int matrix = 0;
        try
        {
            PrefabName.CacheInEditMode = true;
            // The process's very first pickup also pays for the editor's own
            // first times (JIT, the audio system's first Play): flown once,
            // printed, and kept out of the timing bound.
            var cold = Fly(ShipId.Starter, ShipSkins.Stock, WorldManager.Worlds[0]);
            Debug.Log("[SPS] cold process, first pickup ever | " + cold.first.Cells());
            foreach (int id in ShipId.All)
                for (int skin = 0; skin < ShipSkins.CountFor(id); skin++)
                    rows.Add(Fly(id, skin, WorldManager.Worlds[rows.Count % WorldManager.Worlds.Length]));
            matrix = rows.Count;
            // A developer build flies its skins through developer mode.
            for (int skin = 0; skin < ShipSkins.PerShip; skin++)
                rows.Add(Fly(6, skin, WorldManager.Worlds[skin % WorldManager.Worlds.Length], true));
        }
        finally
        {
            foreach (var pair in traces) Application.SetStackTraceLogType(pair.Key, pair.Value);
        }

        var table = new StringBuilder();
        table.AppendLine("[SPS] ship / skin | stretch | builds | readbacks | paths | tex | sprites | mats | meshes | objects | ms | bytes");
        int dirtyFirst = 0, dirtyShielded = 0, dirtySecond = 0, notUp = 0, wrongContour = 0, stingCold = 0;
        var worlds = new HashSet<string>();
        int unbaked = 0, lateBuilds = 0;
        double worstFirst = 0, worstStock = 0, worstSkinned = 0;
        foreach (var r in rows)
        {
            string who = r.id + " " + ShipId.KeyOf(r.id) + " / " + ShipSkins.Get(r.id, r.skin).name + (r.developer ? " (dev mode)" : "");
            unbaked += r.unbaked;
            lateBuilds += r.lateBuilds;
            table.AppendLine("[SPS] " + who + " | spawn+6f | " + r.spawn.Cells() + "  (" + r.world + ", sting " + r.sting + ")");
            if (!r.stingReady) stingCold++;
            worlds.Add(r.world);
            table.AppendLine("[SPS] " + who + " | PICKUP 1 | " + r.first.Cells() + (r.first.Clean ? "" : "  <-- " + r.first.made));
            table.AppendLine("[SPS] " + who + " | 45 frames up | " + r.shielded.Cells() + (r.shielded.Clean ? "" : "  <-- " + r.shielded.made));
            table.AppendLine("[SPS] " + who + " | pickup 2 + 45f | " + r.second.Cells() + (r.second.Clean ? "" : "  <-- " + r.second.made));
            if (!r.first.Clean) dirtyFirst++;
            if (!r.shielded.Clean) dirtyShielded++;
            if (!r.second.Clean) dirtySecond++;
            if (!r.shieldUp) notUp++;
            if (!r.sameContour) wrongContour++;
            worstFirst = Math.Max(worstFirst, r.first.ms);
            if (r.skin == ShipSkins.Stock) worstStock = Math.Max(worstStock, r.first.ms);
            else worstSkinned = Math.Max(worstSkinned, r.first.ms);
        }
        Debug.Log(table.ToString());

        Check("the matrix is every ship x skin (" + matrix + " of " + ShipId.Count * ShipSkins.PerShip +
              "), plus " + (rows.Count - matrix) + " flown in developer mode",
              matrix == ShipId.Count * ShipSkins.PerShip && rows.Count > matrix);
        Check("no shield falls back to cutting its outline from pixels (" + unbaked + " unbaked contours)", unbaked == 0);
        Check("no shield is built by the pickup itself (" + lateBuilds + " late builds)", lateBuilds == 0);
        StingsAreImportedPreloaded();
        Check("the shield comes up on the pickup for every ship x skin (" + notUp + " did not)", notUp == 0);
        Check("every skin wears its ship's one baked contour (" + wrongContour + " do not)", wrongContour == 0);
        Check("the first blue atom builds, reads back and creates nothing, for every ship x skin (" + dirtyFirst +
              " of " + rows.Count + " did)", dirtyFirst == 0);
        Check("the blue atom's sting is already in memory when it plays, in every world (" + stingCold + " of " +
              rows.Count + " pickups had to load it; worlds flown: " + worlds.Count + ")",
              stingCold == 0 && worlds.Count == WorldManager.Worlds.Length);
        Check("the frames the shield is up for create nothing either (" + dirtyShielded + " did)", dirtyShielded == 0);
        Check("nor does the second blue atom (" + dirtySecond + " did)", dirtySecond == 0);
        Debug.Log("[SPS] worst first-pickup call: stock " + worstStock.ToString("F3") + " ms, skinned " +
                  worstSkinned.ToString("F3") + " ms (editor)");
        // One call timed once: a stray collection or the scheduler can land on
        // any single row, so the bound is on the 90th percentile.
        var times = new List<double>();
        foreach (var r in rows) times.Add(r.first.ms);
        times.Sort();
        double p90 = times[(int)(times.Count * .9)], median = times[times.Count / 2];
        Check("the first-pickup call is quick in the editor across the matrix (median " + median.ToString("F3") +
              " ms, 90th percentile " + p90.ToString("F3") + " ms < 2, worst " + worstFirst.ToString("F3") + " ms)",
              p90 < 2.0);

        Debug.Log("[SPS] failures: " + fails);
        return fails;
    }
}
