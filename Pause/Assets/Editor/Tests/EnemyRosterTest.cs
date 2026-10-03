using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The per-world enemy rosters (EnemyRoster): every world fills every role
// with its own cast, the art exists and animates, sizes and colliders stay
// within 15% of what each role measured before the redraw, every enemy is a
// ClearTarget with the tags and name keys gameplay matches on, the spawner
// picks from the current world, the retired Kenney meteors are gone, and the
// art keeps to the enemy palette (no player red).
public static class EnemyRosterTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ER] PASS  " : "[ER] FAIL  ") + what);
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

        EveryWorldFillsEveryRole();
        OnlySpaceHasSpaceRocks();
        SizesStayWithinFifteenPercent();
        EveryEnemyIsAClearTarget();
        FlipbooksLoadAndAnimate();
        NameKeysStillMatch();
        ExplosionVariantsMatchTheCast();
        SpawnerPicksFromTheCurrentWorld();
        SceneSlotsAreNeverNull();
        RetiredMeteorsAreGone();
        PaletteCompliance();

        Debug.Log("[ER] failures: " + fails);
        return fails;
    }

    static readonly EnemyRole[] Roles = (EnemyRole[])Enum.GetValues(typeof(EnemyRole));
    static int Worlds => WorldManager.Worlds.Length;
    static string W(int world) => WorldManager.Worlds[world].displayName;

    // ---- 1: rosters ------------------------------------------------------------

    static void EveryWorldFillsEveryRole()
    {
        Check("the roster has a key per world", EnemyRoster.WorldKeys.Length == Worlds);
        var keys = new HashSet<string>();
        var codex = new HashSet<string>();
        foreach (var d in EnemyRoster.All)
        {
            Check(d.key + " key is unique", keys.Add(d.key));
            Check(d.key + " codex id is unique", codex.Add(d.codexId));
            Check(d.key + " has a display name and lore", !string.IsNullOrEmpty(d.displayName) && !string.IsNullOrEmpty(d.lore));
            Check(d.key + " key starts with its world", d.key.StartsWith(EnemyRoster.WorldKeys[d.world] + "_", StringComparison.Ordinal));
            Check(d.key + " resolves through Find", EnemyRoster.Find(d.key) == d);
        }
        for (int w = 0; w < Worlds; w++)
        {
            foreach (var role in Roles)
                Check(W(w) + " fills the " + role + " role", EnemyRoster.For(w, role).Count > 0);
            for (int tier = 1; tier <= 4; tier++)
                Check(W(w) + " has a tier " + tier + " fighter", EnemyRoster.Fighter(w, tier) != null);
            Check(W(w) + " has three rocks", EnemyRoster.For(w, EnemyRole.Rock).Count == 3);
        }
        foreach (var d in EnemyRoster.All)
        {
            var tex = Resources.Load<Texture2D>(d.StripPath);
            Check(d.key + " strip resolves (Resources/" + d.StripPath + ")", tex != null);
            if (tex != null)
                Check(d.key + " strip holds " + EnemyRoster.FrameCount + " square frames",
                      tex.width == tex.height * EnemyRoster.FrameCount);
        }
    }

    static void OnlySpaceHasSpaceRocks()
    {
        for (int w = 1; w < Worlds; w++)
            foreach (var role in Roles)
                foreach (var d in EnemyRoster.For(w, role))
                {
                    bool spacey = d.key.Contains("space") || d.key.Contains("crater") || d.key.Contains("aestroid") ||
                                  d.key.Contains("meteor") || (d.legacyNames != null && d.legacyNames.Length > 0);
                    Check(W(w) + " " + d.key + " is its own art, not a Space rock or meteor", !spacey);
                    var go = EnemyFactory.Create(d, Vector3.zero, Quaternion.identity);
                    var sr = go.GetComponent<SpriteRenderer>();
                    string path = sr.sprite != null ? AssetDatabase.GetAssetPath(sr.sprite.texture) : "";
                    Check(W(w) + " " + d.key + " draws from its own strip", path.EndsWith("/Enemies/" + d.key + ".png"));
                    UnityEngine.Object.DestroyImmediate(go);
                }
        foreach (var d in EnemyRoster.For(0, EnemyRole.Rock))
            Check("Space keeps the crater rocks: " + d.key, d.key.StartsWith("space_rock_"));
        Check("Space keeps the rail mine", EnemyRoster.One(0, EnemyRole.Mine).codexId == "hazard_mine");
    }

    // ---- 2: sizes ----------------------------------------------------------------

    static bool Within(float value, float reference, float tolerance = .15f)
    {
        return Mathf.Abs(value - reference) <= reference * tolerance + 1e-4f;
    }

    static void SizesStayWithinFifteenPercent()
    {
        foreach (var d in EnemyRoster.All)
        {
            Vector2 now = d.ColliderSize, before = EnemyRoster.LegacyCollider(d.role);
            Check(string.Format("{0} collider {1:F2}x{2:F2} within 15% of {3}'s {4:F2}x{5:F2}",
                                d.key, now.x, now.y, d.role, before.x, before.y),
                  Within(now.x, before.x) && Within(now.y, before.y));

            // The drawn silhouette, measured from frame 0's opaque pixels.
            float edge = SilhouetteEdge(d);
            float world = edge * d.FrameWorldSize;
            Check(string.Format("{0} silhouette {1:F2} u within 15% of {2}'s {3:F2} u", d.key, world, d.role,
                                EnemyRoster.LegacyWidth(d.role)),
                  edge > 0f && Within(world, EnemyRoster.LegacyWidth(d.role)));
        }
    }

    // Longest edge of frame 0's alpha > 50% bounds, as a fraction of the frame.
    static float SilhouetteEdge(EnemyDef d)
    {
        string path = "Assets/Art/Resources/" + d.StripPath + ".png";
        if (!File.Exists(path)) return 0f;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        int h = tex.height;
        var px = tex.GetPixels32();
        int minX = h, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < h; x++)
                if (px[y * tex.width + x].a > 128)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        UnityEngine.Object.DestroyImmediate(tex);
        if (maxX < 0) return 0f;
        return Mathf.Max(maxX - minX + 1, maxY - minY + 1) / (float)h;
    }

    // ---- 3: gameplay contract ------------------------------------------------------

    static void EveryEnemyIsAClearTarget()
    {
        foreach (var d in EnemyRoster.All)
        {
            var go = EnemyFactory.Create(d, Vector3.zero, Quaternion.identity);
            Check(d.key + " is a ClearTarget", go.GetComponent<ClearTarget>() != null);
            Check(d.key + " is tagged " + d.Tag, go.CompareTag(d.Tag) && ClearTarget.IsHazard(go));
            Check(d.key + " rocks are Astr, everything else Enimey",
                  go.CompareTag(d.role == EnemyRole.Rock ? "Astr" : "Enimey"));
            bool mover = go.GetComponent<moveEnimes>() != null || go.GetComponent<moveItemEnmInStrightLine>() != null;
            Check(d.key + (d.role == EnemyRole.Chaser ? " steers itself (ChaserEnemy, no scroller)" : " keeps its scroller"),
                  d.role == EnemyRole.Chaser ? go.GetComponent<ChaserEnemy>() != null && !mover : mover);
            var col = go.GetComponent<BoxCollider2D>();
            Check(d.key + " has a trigger collider of its roster size",
                  col != null && col.isTrigger && col.size == d.ColliderSize && go.transform.localScale == Vector3.one);
            Check(d.key + " carries its identity", EnemyIdentity.Of(go) == d);
            if (d.role == EnemyRole.Rock) Check(d.key + " tumbles (AsteroidSpin)", go.GetComponent<AsteroidSpin>() != null);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void FlipbooksLoadAndAnimate()
    {
        foreach (var d in EnemyRoster.All)
        {
            var frames = EnemyArt.Frames(d);
            bool all = frames != null && frames.Length == EnemyRoster.FrameCount;
            if (all) foreach (var f in frames) all &= f != null;
            Check(d.key + " flipbook loads all " + EnemyRoster.FrameCount + " frames", all);
            if (!all) continue;
            Check(d.key + " frames are " + d.FrameWorldSize + " u", Mathf.Abs(frames[0].bounds.size.x - d.FrameWorldSize) < .01f);
            Check(d.key + " idle/tell timing is on 2s-6s",
                  Array.TrueForAll(EnemyRoster.IdleTicks(d.role), t => t >= 2 && t <= 6) &&
                  Array.TrueForAll(EnemyRoster.TellTicks(d.role), t => t >= 2 && t <= 6));
        }

        // Step one of each role through idle, tell and the hit flash.
        foreach (var role in Roles)
        {
            var d = EnemyRoster.One(0, role);
            var go = EnemyFactory.Create(d, new Vector3(0f, 50f, 0f), Quaternion.identity);
            var fb = go.GetComponent<EnemyFlipbook>();
            Check(role + " has a flipbook" + (role == EnemyRole.Mine ? " (RailBombAnimator)" : ""),
                  fb != null && (role != EnemyRole.Mine || fb is RailBombAnimator));
            if (fb == null) { UnityEngine.Object.DestroyImmediate(go); continue; }
            Check(role + " tell mode " + fb.tellMode, fb.tellMode == EnemyFlipbook.ModeFor(role));
            var chaser = go.GetComponent<ChaserEnemy>();
            if (chaser != null)
            {
                Check("a fresh chaser is hunting (it loops its lunge)", chaser.IsChasing);
                typeof(ChaserEnemy).GetField("wandering", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(chaser, true);
                Check("a wandering chaser has stopped hunting", !chaser.IsChasing);
            }
            var seen = new HashSet<int>();
            for (int i = 0; i < 60; i++) { fb.Advance(EnemyFlipbook.TickSeconds); seen.Add(fb.CurrentFrame); }
            Check(role + " idle cycles all four drawings", seen.Contains(0) && seen.Contains(1) && seen.Contains(2) && seen.Contains(3));
            fb.Tell();
            int a = fb.CurrentFrame;
            for (int i = 0; i < 3; i++) fb.Advance(EnemyFlipbook.TickSeconds);
            fb.Advance(EnemyFlipbook.TickSeconds * .5f);   // 3.5 ticks: inside the release for every role
            Check(role + " tell plays anticipation then release", a == 4 && fb.CurrentFrame == 5);
            fb.Flash();
            Check(role + " hit flash shows frame 6", fb.CurrentFrame == EnemyRoster.HitFrame);
            for (int i = 0; i < 3; i++) fb.Advance(EnemyFlipbook.TickSeconds);
            Check(role + " returns to idle after the flash", fb.CurrentFrame < 4);
            UnityEngine.Object.DestroyImmediate(go);
        }

        // The mine arms (and loops its burst) only while the ship is close.
        var playerGo = new GameObject("~ErPlayer");
        playerGo.AddComponent<movePlayer>().enabled = false;
        var mine = EnemyFactory.Create(EnemyRoster.One(1, EnemyRole.Mine), new Vector3(0f, 1f, 0f), Quaternion.identity);
        var mfb = mine.GetComponent<EnemyFlipbook>();
        typeof(EnemyFlipbook).GetField("player", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, playerGo.transform);
        bool armed = false;
        for (int i = 0; i < 40; i++) { mfb.Advance(EnemyFlipbook.TickSeconds); armed |= mfb.Telling; }
        Check("a mine near the ship arms", armed);
        playerGo.transform.position = new Vector3(0f, 40f, 0f);
        for (int i = 0; i < 40; i++) mfb.Advance(EnemyFlipbook.TickSeconds);
        Check("a mine far from the ship settles back to idle", !mfb.Telling);
        typeof(EnemyFlipbook).GetField("player", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
        UnityEngine.Object.DestroyImmediate(mine);
        UnityEngine.Object.DestroyImmediate(playerGo);
    }

    static void NameKeysStillMatch()
    {
        for (int w = 0; w < Worlds; w++)
        {
            var alien = EnemyFactory.Create(EnemyRoster.One(w, EnemyRole.Alien), Vector3.zero, Quaternion.identity);
            alien.name += "(Clone)";
            Check(W(w) + " alien still matches collisionDetection's \"alien1\" key", PrefabName.Is(alien, "alien1"));
            Check(W(w) + " alien is the Alien role", EnemyIdentity.IsRole(alien, EnemyRole.Alien));
            var mine = EnemyFactory.Create(EnemyRoster.One(w, EnemyRole.Mine), Vector3.zero, Quaternion.identity);
            Check(W(w) + " mine still matches collisionDetection's \"mine\" key", PrefabName.Is(mine, "mine"));
            Check(W(w) + " mine still blows up as a mine", TargetExplosion.KindFor(mine) == TargetExplosion.Kind.Mine);
            UnityEngine.Object.DestroyImmediate(alien);
            UnityEngine.Object.DestroyImmediate(mine);
        }
        string collision = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("collisionDetection still keys the alien achievement on \"alien1\"", collision.Contains("PrefabName.Is(hit.gameObject, \"alien1\")"));
        Check("collisionDetection still keys the mine explosion on \"mine\"", collision.Contains("PrefabName.Is(hit.gameObject, \"mine\")"));
    }

    static void ExplosionVariantsMatchTheCast()
    {
        foreach (var d in EnemyRoster.All)
        {
            var go = EnemyFactory.Create(d, Vector3.zero, Quaternion.identity);
            Check(d.key + " explodes as " + d.explosion + " (" + d.explosionSize + ")",
                  TargetExplosion.KindFor(go) == d.explosion && TargetExplosion.SizeFor(go) == d.explosionSize &&
                  WeaponArt.Explosion(d.explosion, 0) != null);
            UnityEngine.Object.DestroyImmediate(go);
        }
        foreach (var d in EnemyRoster.For(1, EnemyRole.Rock)) Check(d.key + " shatters as ice", d.explosion == TargetExplosion.Kind.Ice);
        foreach (var d in EnemyRoster.For(2, EnemyRole.Rock)) Check(d.key + " bursts as spore", d.explosion == TargetExplosion.Kind.Spore);
        foreach (var d in EnemyRoster.For(3, EnemyRole.Rock)) Check(d.key + " bursts as magma", d.explosion == TargetExplosion.Kind.Magma);
        foreach (var d in EnemyRoster.For(0, EnemyRole.Rock)) Check(d.key + " breaks as rock", d.explosion == TargetExplosion.Kind.Rock);
        foreach (TargetExplosion.Kind kind in Enum.GetValues(typeof(TargetExplosion.Kind)))
            for (int i = 0; i < WeaponArt.ExplosionFrames; i++)
                if (WeaponArt.Explosion(kind, i) == null) { Check(kind + " explosion frame " + i + " loads", false); break; }
    }

    // ---- 4: spawner --------------------------------------------------------------

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static void SpawnerPicksFromTheCurrentWorld()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        Check("no WorldManager (tutorial, menus) means Space", EnemyRoster.CurrentWorld == 0);

        var board = new GameObject("~ErBoard").AddComponent<enmiesOnBoard>();
        board.SendMessage("Start");
        var wmGo = new GameObject("~ErWorlds");
        var wm = wmGo.AddComponent<WorldManager>();
        SetWorldManager(wm);
        try
        {
            string[] slots = { "spawnAstroid1", "spawnAstroid2", "spawnSmallAstroid", "spawnMidAstroid", "spawnLargeAstroid",
                               "spawnAnimatedEnimeOne", "spawnExtraEnemy", "spawnChaser", "spawnMine" };
            var phaseField = typeof(enmiesOnBoard).GetField("astroidSelector", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int w = 0; w < Worlds; w++)
            {
                PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);   // what a portal does (WorldManager.Advance)
                Check("current world is " + W(w), EnemyRoster.CurrentWorld == w);
                phaseField.SetValue(board, 4);
                var before = new HashSet<EnemyIdentity>(UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None));
                foreach (string slot in slots)
                    for (int k = 0; k < 6; k++)
                        typeof(enmiesOnBoard).GetMethod(slot, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(board, null);
                var roles = new HashSet<EnemyRole>();
                bool allHere = true;
                int n = 0;
                foreach (var id in UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                {
                    if (before.Contains(id)) continue;
                    n++;
                    allHere &= id.Def != null && id.Def.world == w;
                    if (id.Def != null) roles.Add(id.Def.role);
                }
                Check(W(w) + ": every spawn slot draws from " + W(w) + "'s roster (" + n + " spawned)", allHere && n > 20);
                Check(W(w) + ": the slots field every role (" + roles.Count + ")", roles.Count == Roles.Length);
            }
            foreach (var id in UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(id.gameObject);

            // Mines mirror to grip a right-hand wall.
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
            var spawnRail = typeof(enmiesOnBoard).GetMethod("SpawnRail", BindingFlags.NonPublic | BindingFlags.Instance);
            var liveMines = (System.Collections.IList)typeof(enmiesOnBoard).GetField("liveMines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(board);
            bool flips = true;
            for (int k = 0; k < 8; k++)
            {
                typeof(enmiesOnBoard).GetMethod("spawnMine", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(board, null);
                var t = (Transform)liveMines[liveMines.Count - 1];
                flips &= t.GetComponent<SpriteRenderer>().flipX == (t.position.x > 0f);
            }
            Check("rail mines face their own wall (flipped on right-hand rails)", flips);
        }
        finally
        {
            SetWorldManager(null);
            UnityEngine.Object.DestroyImmediate(wmGo);
            UnityEngine.Object.DestroyImmediate(board.gameObject);
        }

        // Tier window: phases 2/3/4 field tiers 1-2 / 1-3 / 2-4.
        for (int phase = 2; phase <= 4; phase++)
        {
            int lo = 9, hi = 0;
            for (int k = 0; k < 400; k++)
            {
                var d = enmiesOnBoard.ChooseExtraDef(0, phase);
                if (d == null || d.role != EnemyRole.Fighter) continue;
                lo = Mathf.Min(lo, d.tier);
                hi = Mathf.Max(hi, d.tier);
            }
            int wantHi = Mathf.Clamp(phase, 1, 4), wantLo = Mathf.Max(1, wantHi - 2);
            Check("phase " + phase + " fields fighter tiers " + wantLo + "-" + wantHi + " (" + lo + "-" + hi + ")", lo == wantLo && hi == wantHi);
        }
    }

    static void SceneSlotsAreNeverNull()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var spawner = UnityEngine.Object.FindFirstObjectByType<enmiesOnBoard>();
        Check("gameS1 has the spawner", spawner != null);
        if (spawner == null) return;
        int missing = 0;
        foreach (var array in new[] { spawner.astroid1, spawner.astroid2, spawner.astroid3, spawner.astroid4, spawner.astroid5 })
            foreach (var prefab in array) if (prefab == null) missing++;
        Check("no gameS1 asteroid slot is a missing prefab (" + missing + " null)", missing == 0);
        Check("gameS1's alien fallback is set", spawner.alien1 != null);
        Check("the deleted prefab guid is gone from gameS1",
              !File.ReadAllText("Assets/Scenes/gameS1.unity").Contains("1390ffc126996fb4388474c4cdff00ac"));
    }

    // ---- 5: deletions --------------------------------------------------------------

    static readonly string[] Deleted =
    {
        "meteorBrown_tiny1", "meteorBrown_tiny2", "meteorBrown_small1", "meteorBrown_small2",
        "meteorGrey_tiny1", "meteorGrey_tiny2", "meteorGrey_small1", "meteorGrey_small2",
        "meteorBrown_med1", "meteorBrown_med3", "meteorGrey_med1", "meteorGrey_med2",
    };

    static readonly string[] DeletedGuids =
    {
        "d5c7b4a330b114d77ae96278c45cbf7a", "58867f1cc39e64b98ae43da5ec1c16c7", "932802f7ce2994e2fb54f220630bff8e",
        "a896dcbe965cd45239f3761ea2f08f3f", "28e7680064c824094908f351dd958267", "0b783274d9c3b43a380a9c8964f534d3",
        "7d3d760f2b2c547f39fc0cf09d81a931", "614a919134e574fd8a31782ec419c00b", "622d3e6c89005423d918ae152884635f",
        "8f09b91325999428cb8bd4683d4de486", "2d5e14837371f483083407ed4d7c61c9", "e07340c1496a147f7a3848f3875cc1bb",
        "e59280c0adaf74a66979796d5c5e73d1", "ae5b4ea45197a4c16aabc97d7044708f", "7df6c38a7ee9b4967be83fc024ecec9a",
        "b007291d6adf14ec788a3cf39865bdba", "22b8843d9b1b845e9b756d5ceb6cf23d", "f40087321030444e28ab65172fadbb10",
        "e5d81032a3fce4ac489fac3f31237a1c", "429a3d996ab5b4a0990bcf3d25c7d8e0", "2dbc28baa3e9e427d9e9f3e3a654868d",
        "b4a450bcc65d04fddabbe519aa541500", "31186ef8db1fe410fbdd1be70bbbae5c", "c35d2af0e366d4d6a9403d8799826368",
    };

    static void RetiredMeteorsAreGone()
    {
        foreach (string name in Deleted)
        {
            Check("kn_" + name + " prefab is deleted",
                  AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Enemies/kn_" + name + ".prefab") == null &&
                  !File.Exists("Assets/Resources/Prefabs/Enemies/kn_" + name + ".prefab.meta"));
            Check(name + ".png is deleted",
                  !File.Exists("Assets/Art/Resources/Prefabs/Enemies/Kenney/" + name + ".png") &&
                  !File.Exists("Assets/Art/Resources/Prefabs/Enemies/Kenney/" + name + ".png.meta"));
        }
        var guids = new HashSet<string>(DeletedGuids);
        var names = new Regex("kn_(" + string.Join("|", Deleted) + ")\\b");
        int dangling = 0;
        foreach (string ext in new[] { "*.cs", "*.unity", "*.prefab", "*.asset", "*.controller", "*.anim" })
            foreach (string path in Directory.GetFiles("Assets", ext, SearchOption.AllDirectories))
            {
                if (path.Replace('\\', '/').EndsWith("Editor/Tests/EnemyRosterTest.cs")) continue;
                string text = File.ReadAllText(path);
                bool hit = names.IsMatch(text);
                foreach (Match m in Regex.Matches(text, "guid: ([0-9a-f]{32})")) hit |= guids.Contains(m.Groups[1].Value);
                if (hit) { dangling++; Debug.Log("[ER] dangling reference to a deleted meteor in " + path); }
            }
        Check("nothing references a deleted meteor (" + dangling + " files)", dangling == 0);
        Check("the big meteors are kept (ambiguous, out of the spawn pool)",
              AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Enemies/kn_meteorBrown_big1.prefab") != null);
    }

    // ---- 6: palette --------------------------------------------------------------

    const string ArtSrc = "Assets/Art/Enemies/src~/";
    static readonly string[] PlayerReds = { "#D8232C", "#86121F", "#FF5B45" };

    static void PaletteCompliance()
    {
        var env = new Dictionary<string, string>();
        foreach (var envLine in File.ReadAllLines(ArtSrc + "palette.env"))
        {
            var m = Regex.Match(envLine.Trim(), @"^([A-Z_]+)=(#[0-9A-Fa-f]{6})$");
            if (m.Success) env[m.Groups[1].Value] = m.Groups[2].Value.ToUpperInvariant();
        }
        Check("palette.env defines the enemy palette (" + env.Count + " colours)", env.Count >= 20);
        foreach (var pair in env)
        {
            Check(pair.Key + " is not the player's red (red means friendly)",
                  !pair.Key.StartsWith("RED") && Array.IndexOf(PlayerReds, pair.Value) < 0);
            Color c;
            ColorUtility.TryParseHtmlString(pair.Value, out c);
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            float deg = h * 360f;
            Check(pair.Key + " " + pair.Value + " is not a saturated red", !(s > .55f && v > .45f && (deg < 15f || deg > 345f)));
        }
        foreach (var f in typeof(EnemyPalette).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType != typeof(Color)) continue;
            string key = Regex.Replace(f.Name, "(?<!^)([A-Z])", "_$1").ToUpperInvariant();
            string want;
            Check("EnemyPalette." + f.Name + " matches palette.env " + key,
                  env.TryGetValue(key, out want) && EnemyPalette.Html((Color)f.GetValue(null)) == want);
        }

        var svgs = Directory.GetFiles(ArtSrc + "svg", "*.svg");
        Check("enemy SVG sources exist (" + svgs.Length + ")", svgs.Length == EnemyRoster.All.Length * EnemyRoster.FrameCount);
        int raw = 0, unknown = 0, rasters = 0, blurOutsideGlow = 0;
        foreach (var path in svgs)
        {
            string src = File.ReadAllText(path);
            string body = Regex.Replace(src, "<filter[^>]*>.*?</filter>", "", RegexOptions.Singleline);
            if (Regex.IsMatch(body, "#[0-9A-Fa-f]{6}")) raw++;
            foreach (Match c in Regex.Matches(src, "@([A-Z_]+)@")) if (!env.ContainsKey(c.Groups[1].Value)) unknown++;
            if (src.Contains("<image") || src.Contains("Gradient")) rasters++;
            // blur is for lights only: never inside the base/shadow/highlight/ink/detail cels
            foreach (string layer in new[] { "base", "shadow", "highlight", "ink", "detail" })
            {
                var g = Regex.Match(src, "<g id=\"" + layer + "\">(.*?)</g>\\s*(<g id=|</svg>)", RegexOptions.Singleline);
                if (g.Success && g.Groups[1].Value.Contains("filter=")) blurOutsideGlow++;
            }
        }
        Check("every SVG colour is a palette.env token (" + raw + " raw hex)", raw == 0);
        Check("every token exists in palette.env (" + unknown + " unknown)", unknown == 0);
        Check("no gradients or raster images in the sources", rasters == 0);
        Check("glow blur only on lights, never on cels (" + blurOutsideGlow + ")", blurOutsideGlow == 0);
        foreach (var d in EnemyRoster.All)
            Check(d.key + " sources exist", File.Exists(ArtSrc + "svg/" + d.key + "_0.svg") &&
                                            File.Exists(ArtSrc + "svg/" + d.key + "_" + EnemyRoster.HitFrame + ".svg"));
    }
}
