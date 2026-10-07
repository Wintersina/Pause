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
// art keeps to the enemy palette (no player red). The rail mines are the
// exception to the flat-ink art checks: they are neon pixel art, a row each
// of the original atlas (RailMineArtTest holds them to it).
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
        NoArtIsSharedBetweenWorlds();
        SizesStayWithinFifteenPercent();
        EveryEnemyIsAClearTarget();
        FlipbooksLoadAndAnimate();
        NameKeysStillMatch();
        ExplosionVariantsMatchTheCast();
        SpawnerPicksFromTheCurrentWorld();
        if (TestHarness.Slow("EnemyRoster: 240s spawner run per world")) HeaviesKeepEveryRowPassable();
        SceneSlotsAreNeverNull();
        RetiredMeteorsAreGone();
        PaletteCompliance();
        FloatingRocks();
        DetailFloor();

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
            int rocks = EnemyRoster.For(w, EnemyRole.Rock).Count;
            Check(W(w) + " has three or four rocks (" + rocks + ")", rocks >= 3 && rocks <= 4);
        }
        foreach (var d in EnemyRoster.All)
        {
            var tex = Resources.Load<Texture2D>(d.StripPath);
            Check(d.key + " strip resolves (Resources/" + d.StripPath + ")", tex != null);
            if (d.role == EnemyRole.Mine)
                Check(d.key + " reads the neon rail-mine atlas", d.StripPath == RailMineArt.AtlasPath);
            else if (tex != null)
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
                    if (d.role == EnemyRole.Mine)
                        Check(W(w) + " " + d.key + " draws from its own row of the neon mine atlas",
                              path.EndsWith("/" + RailMineArt.AtlasPath + ".png") &&
                              Array.IndexOf(EnemyArt.Frames(d), sr.sprite) >= 0 && EnemyArt.Frames(d)[0] == RailMineArt.Frame(w, RailMineArt.Dormant));
                    else
                        Check(W(w) + " " + d.key + " draws from its own strip", path.EndsWith("/Enemies/" + d.key + ".png"));
                    UnityEngine.Object.DestroyImmediate(go);
                }
        foreach (var d in EnemyRoster.For(0, EnemyRole.Rock))
            Check("Space keeps the crater rocks: " + d.key, d.key.StartsWith("space_rock_"));
        Check("Space keeps the rail mine", EnemyRoster.One(0, EnemyRole.Mine).codexId == "hazard_mine");
    }

    // No sprite, texture or drawing is shared between worlds: every texture
    // GUID belongs to one world, and for every role the silhouettes of any
    // two worlds differ (not a palette swap of one shape).
    static void NoArtIsSharedBetweenWorlds()
    {
        var owner = new Dictionary<string, int>();
        foreach (var d in EnemyRoster.All)
        {
            // the mines share the one neon atlas, a row each (checked below)
            if (d.role == EnemyRole.Mine) continue;
            var go = EnemyFactory.Create(d, Vector3.zero, Quaternion.identity);
            var sr = go.GetComponent<SpriteRenderer>();
            string guid = sr.sprite != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sr.sprite.texture)) : "";
            UnityEngine.Object.DestroyImmediate(go);
            Check(d.key + " has its own texture asset", !string.IsNullOrEmpty(guid));
            int w;
            if (owner.TryGetValue(guid, out w))
                Check(d.key + " texture " + guid + " is not also used by " + W(w), w == d.world);
            else owner[guid] = d.world;
        }
        var guidWorlds = new Dictionary<string, HashSet<int>>();
        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Mine) continue;
            string guid = AssetDatabase.AssetPathToGUID("Assets/Art/Resources/" + d.StripPath + ".png");
            if (!guidWorlds.ContainsKey(guid)) guidWorlds[guid] = new HashSet<int>();
            guidWorlds[guid].Add(d.world);
        }
        int shared = 0;
        foreach (var pair in guidWorlds) if (pair.Value.Count > 1) shared++;
        Check("no texture GUID appears in more than one world's roster (" + shared + " shared)", shared == 0);

        var mineRects = new HashSet<Rect>();
        for (int w = 0; w < Worlds; w++)
        {
            var dormant = EnemyArt.Frame(EnemyRoster.One(w, EnemyRole.Mine), 0);
            Check(W(w) + " mine has its own atlas row", dormant != null && mineRects.Add(dormant.rect));
        }

        var masks = new Dictionary<string, bool[]>();
        foreach (var d in EnemyRoster.All) if (d.role != EnemyRole.Mine) masks[d.key] = Mask(d);
        foreach (var role in Roles)
            if (role != EnemyRole.Mine)
            for (int a = 0; a < Worlds; a++)
                for (int b = a + 1; b < Worlds; b++)
                    foreach (var da in EnemyRoster.For(a, role))
                        foreach (var db in EnemyRoster.For(b, role))
                        {
                            if (role == EnemyRole.Fighter && da.tier != db.tier) continue;
                            float iou = IoU(masks[da.key], masks[db.key]);
                            Check(string.Format("{0} and {1} are different drawings (silhouette overlap {2:P0} < 80%)",
                                                da.key, db.key, iou), iou < .8f);
                        }
    }

    static bool[] Mask(EnemyDef d)
    {
        return Mask("Assets/Art/Resources/" + d.StripPath + ".png");
    }

    // One frame's alpha > 50% silhouette from any flipbook strip on disk.
    static bool[] Mask(string path, int frame = 0)
    {
        if (!File.Exists(path)) return new bool[0];
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        int h = tex.height;
        var px = tex.GetPixels32();
        var mask = new bool[h * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < h; x++)
                mask[y * h + x] = px[y * tex.width + frame * h + x].a > 128;
        UnityEngine.Object.DestroyImmediate(tex);
        return mask;
    }

    static float IoU(bool[] a, bool[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 1f;
        int both = 0, either = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] && b[i]) both++;
            if (a[i] || b[i]) either++;
        }
        return either == 0 ? 1f : both / (float)either;
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
            Vector2 now = d.ColliderSize, before = EnemyRoster.TargetCollider(d.role);
            Check(string.Format("{0} collider {1:F2}x{2:F2} within 15% of {3}'s {4:F2}x{5:F2}",
                                d.key, now.x, now.y, d.role, before.x, before.y),
                  Within(now.x, before.x) && Within(now.y, before.y));

            // The drawn silhouette, measured from frame 0's opaque pixels.
            float edge = d.role == EnemyRole.Mine ? 1f : SilhouetteEdge(d);
            float world = d.role == EnemyRole.Mine ? MineSilhouette(d) : edge * d.FrameWorldSize;
            Check(string.Format("{0} silhouette {1:F2} u within 15% of {2}'s {3:F2} u", d.key, world, d.role,
                                EnemyRoster.TargetWidth(d.role)),
                  edge > 0f && Within(world, EnemyRoster.TargetWidth(d.role)));
        }

        // The heavies are the one role resized on purpose: properly big
        // (1.0-1.2 u drawn), bigger than every fighter, with a collider that
        // scales with the art but stays inset inside the drawing.
        Check("the heavies target 1.0-1.2 u", EnemyRoster.BigWidth >= 1f && EnemyRoster.BigWidth <= 1.2f);
        Check("the heavies out-size the fighters", EnemyRoster.BigWidth > EnemyRoster.LegacyWidth(EnemyRole.Fighter));
        foreach (var d in EnemyRoster.All)
        {
            if (d.role != EnemyRole.Big) continue;
            float drawn = SilhouetteEdge(d) * d.FrameWorldSize;
            Vector2 col = d.ColliderSize;
            Check(string.Format("{0} collider {1:F2} is inset inside its {2:F2} u drawing (65-92%)", d.key, col.x, drawn),
                  col.x >= drawn * .65f && col.x <= drawn * .92f && col.y >= drawn * .65f && col.y <= drawn * .92f);
        }
        foreach (var d in EnemyRoster.All)
            if (d.role == EnemyRole.Big)
                Check(d.key + " is a large explosion", d.explosionSize == TargetExplosion.Size.Large);
    }

    // The mine's dormant drawing in the neon atlas: longest edge of its
    // alpha > 50% bounds, in world units at RailMineArt.PixelsPerUnit.
    static float MineSilhouette(EnemyDef d)
    {
        string path = "Assets/Art/Resources/" + RailMineArt.AtlasPath + ".png";
        if (!File.Exists(path)) return 0f;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32();
        var r = RailMineArt.PixelRect(d.world, RailMineArt.Dormant);
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (int y = r.y; y < r.yMax; y++)
            for (int x = r.x; x < r.xMax; x++)
                if (px[(tex.height - 1 - y) * tex.width + x].a > 128)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
        UnityEngine.Object.DestroyImmediate(tex);
        if (maxX < 0) return 0f;
        return Mathf.Max(maxX - minX + 1, maxY - minY + 1) / RailMineArt.PixelsPerUnit;
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
            if (d.role == EnemyRole.Mine)
                Check(d.key + " frames are neon atlas cells at " + RailMineArt.PixelsPerUnit + " PPU",
                      frames[0].texture == RailMineArt.Atlas && Mathf.Approximately(frames[0].pixelsPerUnit, RailMineArt.PixelsPerUnit));
            else
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
            var expectedMode = d.key == "space_chaser" ? EnemyFlipbook.TellMode.IdleOnly : EnemyFlipbook.ModeFor(role);
            Check(role + " tell mode " + fb.tellMode, fb.tellMode == expectedMode);
            var chaser = go.GetComponent<ChaserEnemy>();
            if (chaser != null)
            {
                Check("a fresh chaser is hunting", chaser.IsChasing);
                typeof(ChaserEnemy).GetField("wandering", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(chaser, true);
                Check("a wandering chaser has stopped hunting", !chaser.IsChasing);
            }
            var seen = new HashSet<int>();
            for (int i = 0; i < 60; i++) { fb.Advance(EnemyFlipbook.TickSeconds); seen.Add(fb.CurrentFrame); }
            Check(role + " idle cycles its intended drawings",
                  d.key == "space_chaser"
                      ? seen.SetEquals(new[] { 0, 1 })
                      : seen.Contains(0) && seen.Contains(1) && seen.Contains(2) && seen.Contains(3));
            if (d.key == "space_chaser")
            {
                typeof(ChaserEnemy).GetField("wandering", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(chaser, false);
                bool steadyChase = true;
                for (int i = 0; i < 120; i++)
                {
                    fb.Advance(EnemyFlipbook.TickSeconds);
                    steadyChase &= fb.CurrentFrame == 0 || fb.CurrentFrame == 1;
                }
                Check("Steel Hound keeps its hover poses throughout the chase", steadyChase);
            }
            fb.Tell();
            int a = fb.CurrentFrame;
            // half a tick into the release (the anticipation holds TellTicks[0])
            int anticipation = EnemyRoster.TellTicks(role)[0];
            for (int i = 0; i < Mathf.Max(3, anticipation); i++) fb.Advance(EnemyFlipbook.TickSeconds);
            fb.Advance(EnemyFlipbook.TickSeconds * .5f);
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
        Check("collisionDetection still keys the alien achievement on \"alien1\"", collision.Contains("PrefabName.Is(target, \"alien1\")"));
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
                // a portal clears the board: the last world's enemies (the
                // chasers never move here) don't crowd this world's spawns
                // (SpawnSpace keeps every spawn clear of them)
                foreach (var old in UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                var before = new HashSet<EnemyIdentity>(UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None));
                foreach (string slot in slots)
                    for (int k = 0; k < 6; k++)
                    {
                        typeof(enmiesOnBoard).GetMethod(slot, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(board, null);
                        Scroll(1f, false);   // the board moves on between spawns (SpawnLane keeps each row open)
                    }
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
                Check(W(w) + ": the slots field every role (" + string.Join(", ", roles) + ")", roles.Count == Roles.Length);
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

    // Every live hazard (chasers aside: they steer) moves down the board.
    static void Scroll(float dy, bool despawn = true)
    {
        foreach (var id in UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id.Def != null && id.Def.role == EnemyRole.Chaser) continue;
            id.transform.position += Vector3.down * dy;
            if (despawn && id.transform.position.y < -14f) UnityEngine.Object.DestroyImmediate(id.gameObject);
        }
    }

    // The heavies are ~1.1 u now. Over a simulated run in every world -- the
    // real spawner's timers, phases and density ramp, stepped headless with
    // the board scrolling at a steady speed -- no row of the board is ever
    // closed: every window a ship has to pass through keeps a ship-width gap
    // (SpawnLane.ShipGap) across the lane, heavies stay clear of the walls
    // and rail mines, and they stay an occasional threat, not a wall.
    static void HeaviesKeepEveryRowPassable()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var wmGo = new GameObject("~ErRunWorlds");
        var wm = wmGo.AddComponent<WorldManager>();
        SetWorldManager(wm);
        var spawn = typeof(enmiesOnBoard).GetMethod("spawn", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(float) }, null);
        var select = typeof(enmiesOnBoard).GetMethod("SelectPhase", BindingFlags.NonPublic | BindingFlags.Instance);
        var elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
        Check("the spawner steps with an explicit dt (spawn(float))", spawn != null && spawn.GetParameters().Length == 1);
        if (spawn == null || select == null || elapsed == null) { SetWorldManager(null); UnityEngine.Object.DestroyImmediate(wmGo); return; }
        const float dt = .1f, scrollSpeed = 3f, runSeconds = 240f;
        float gap = SpawnLane.ShipGap;
        try
        {
            for (int w = 0; w < Worlds; w++)
            {
                PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
                UnityEngine.Random.InitState(9100 + w);
                var board = new GameObject("~ErRunBoard").AddComponent<enmiesOnBoard>();
                board.SendMessage("Start");
                var seen = new HashSet<EnemyIdentity>();
                int heavies = 0, total = 0, closedRows = 0, heavyOutOfLane = 0;
                float tightest = 99f;
                for (float t = 0f; t < runSeconds; t += dt)
                {
                    elapsed.SetValue(board, t);
                    select.Invoke(board, null);
                    spawn.Invoke(board, new object[] { dt });
                    foreach (var id in UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                    {
                        if (!seen.Add(id) || id.Def == null) continue;
                        total++;
                        if (id.Def.role != EnemyRole.Big) continue;
                        heavies++;
                        float half = id.Def.FrameWorldSize * .5f * .85f;
                        if (Mathf.Abs(id.transform.position.x) + half > SpawnLane.LaneHalf - .3f) heavyOutOfLane++;
                    }
                    // every window of the board near the spawn line, one ship gap tall
                    for (float y = -3f; y <= 1.5f; y += .25f)
                    {
                        float widest = SpawnLane.WidestGap(SpawnLane.RowSpans(y, y + gap));
                        tightest = Mathf.Min(tightest, widest);
                        if (widest < gap - 1e-3f) closedRows++;
                    }
                    Scroll(scrollSpeed * dt);
                }
                Check(string.Format("{0}: {1:F0}s run, every row keeps a ship-width gap ({2} closed windows, tightest {3:F2} u >= {4:F2} u)",
                                    W(w), runSeconds, closedRows, tightest, gap), closedRows == 0);
                Check(string.Format("{0}: heavies spawn ({1} of {2} hazards)", W(w), heavies, total), heavies >= 5);
                Check(string.Format("{0}: heavies stay an occasional threat ({1:P0} of spawns <= 25%)", W(w), heavies / (float)Mathf.Max(1, total)),
                      heavies <= total * .25f);
                Check(string.Format("{0}: the lane guard doesn't starve the board ({1} hazards in {2:F0}s)", W(w), total, runSeconds), total >= 200);
                Check(W(w) + ": every heavy spawns clear of the walls and rail mines (" + heavyOutOfLane + " out)", heavyOutOfLane == 0);
                foreach (var id in UnityEngine.Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(id.gameObject);
                UnityEngine.Object.DestroyImmediate(board.gameObject);
            }
        }
        finally
        {
            SetWorldManager(null);
            UnityEngine.Object.DestroyImmediate(wmGo);
        }
    }

    static void SceneSlotsAreNeverNull()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var spawner = UnityEngine.Object.FindFirstObjectByType<enmiesOnBoard>();
        Check("gameS1 has the spawner", spawner != null);
        if (spawner == null) return;
        // The astroid1-5 prefab arrays were retired with the aestroid_* art;
        // every rock and heavy comes from the roster.
        string scene = File.ReadAllText("Assets/Scenes/gameS1.unity");
        Check("gameS1 no longer serializes the retired astroid1-5 arrays",
              !Regex.IsMatch(scene, @"^  astroid[1-5]:", RegexOptions.Multiline));
        Check("enmiesOnBoard no longer has the astroid1-5 fields",
              typeof(enmiesOnBoard).GetField("astroid1") == null && typeof(enmiesOnBoard).GetField("rails") == null);
        // The alien1.prefab fallback was retired with its invader art: every
        // alien comes from the roster, in every world.
        Check("enmiesOnBoard no longer has the alien1 prefab fallback", typeof(enmiesOnBoard).GetField("alien1") == null);
        Check("gameS1 no longer serializes the alien1 fallback",
              !Regex.IsMatch(scene, @"^  alien1:", RegexOptions.Multiline));
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
        // The big meteors and the Kenney fighters followed them out (the
        // whole Prefabs/Enemies folders); UnusedAssetGuardTest guards that.
        Check("the Kenney prefab folder is gone", !AssetDatabase.IsValidFolder("Assets/Resources/prefabs/Enemies"));
        Check("the Kenney art folder is gone", !AssetDatabase.IsValidFolder("Assets/Art/Resources/Prefabs/Enemies"));
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

        // The shipped strips (the per-frame SVGs build.py writes are untracked
        // intermediates): one per drawn enemy, FrameCount square frames butted
        // left to right, and the player's reds never more than a trace of a
        // drawing (red means friendly; a few anti-aliased texels may land on it).
        Check("the enemy generator exists", File.Exists(ArtSrc + "build.py") && File.Exists(ArtSrc + "render.sh"));
        Color32[] reds = new Color32[PlayerReds.Length];
        for (int i = 0; i < reds.Length; i++)
        {
            Color c;
            ColorUtility.TryParseHtmlString(PlayerReds[i], out c);
            reds[i] = c;
        }
        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Mine) continue;   // the mines are the neon atlas (RailMineArtTest)
            var tex = LoadStrip(d);
            Check(d.key + " strip exists", tex != null);
            if (tex == null) continue;
            Check(string.Format("{0} strip is {1} square frames ({2}x{3})", d.key, EnemyRoster.FrameCount, tex.width, tex.height),
                  tex.width == tex.height * EnemyRoster.FrameCount);
            int opaque = 0, red = 0;
            foreach (var p in tex.GetPixels32())
            {
                if (p.a <= 128) continue;
                opaque++;
                foreach (var r in reds)
                    if (Mathf.Abs(p.r - r.r) + Mathf.Abs(p.g - r.g) + Mathf.Abs(p.b - r.b) <= 24) { red++; break; }
            }
            Check(string.Format("{0} keeps off the player's reds ({1} of {2} texels)", d.key, red, opaque),
                  opaque > 0 && red < opaque * .02f);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    static Texture2D LoadStrip(EnemyDef d)
    {
        string path = "Assets/Art/Resources/" + d.StripPath + ".png";
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        return tex;
    }

    // ---- 7: detail floor ---------------------------------------------------------

    // The art can't slide back to blobby simple shapes: every enemy's key
    // pose (frame 0, read from the shipped strip) is built from many tones
    // and many colour boundaries (panel lines, rivets, plates, sockets,
    // teeth...). The heavies, drawn twice as big, carry more. Floors sit
    // well under the 2026-10 art (fewest: 374 tones, 4362 boundaries).
    public const int MinTones = 300, MinBoundaries = 3000, MinBoundariesBig = 6000;

    static void DetailFloor()
    {
        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Mine) continue;   // the neon atlas, held by RailMineArtTest
            var tex = LoadStrip(d);
            if (tex == null) { Check(d.key + " key pose strip exists", false); continue; }
            int h = tex.height;
            var px = tex.GetPixels32();
            var tones = new HashSet<int>();
            int boundaries = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                {
                    var a = px[y * tex.width + x];
                    if (a.a <= 128) continue;
                    tones.Add(a.r << 16 | a.g << 8 | a.b);
                    bool edge = false;
                    if (x + 1 < h) { var b = px[y * tex.width + x + 1]; edge |= b.a > 128 && (b.r != a.r || b.g != a.g || b.b != a.b); }
                    if (y + 1 < h) { var b = px[(y + 1) * tex.width + x]; edge |= b.a > 128 && (b.r != a.r || b.g != a.g || b.b != a.b); }
                    if (edge) boundaries++;
                }
            UnityEngine.Object.DestroyImmediate(tex);
            int need = d.role == EnemyRole.Big ? MinBoundariesBig : MinBoundaries;
            Check(string.Format("{0} key pose is detailed ({1} tones >= {2}, {3} colour boundaries >= {4})",
                                d.key, tones.Count, MinTones, boundaries, need),
                  tones.Count >= MinTones && boundaries >= need);
        }
    }

    // ---- 8: floating rocks -------------------------------------------------------

    // The first-pass grass-capped Spore Rock (commit 18b5e5f) the restored
    // Verdant rock is held to.
    const string FirstPassSpore = ArtSrc + "reference/verdant_rock_spore_firstpass.png";

    static void FloatingRocks()
    {
        var floatingKeys = new HashSet<string>();
        var py = Regex.Match(File.ReadAllText(ArtSrc + "rocks.py"), @"FLOATING\s*=\s*\(([^)]*)\)");
        Check("rocks.py lists its floating rocks", py.Success);
        if (py.Success)
            foreach (Match m in Regex.Matches(py.Groups[1].Value, "\"([a-z_0-9]+)\""))
                floatingKeys.Add(m.Groups[1].Value);

        for (int w = 0; w < Worlds; w++)
        {
            int n = 0;
            foreach (var d in EnemyRoster.For(w, EnemyRole.Rock)) if (d.floating) n++;
            Check(W(w) + " has a floating world rock (" + n + ")", n >= 1);
        }
        foreach (var d in EnemyRoster.All)
        {
            if (d.floating) Check(d.key + " (floating) is a rock", d.role == EnemyRole.Rock);
            Check(d.key + (d.floating ? " is" : " is not") + " drawn as a floating rock in rocks.py",
                  floatingKeys.Contains(d.key) == d.floating);
            if (d.role != EnemyRole.Rock) continue;
            var go = EnemyFactory.Create(d, Vector3.zero, Quaternion.identity);
            var spin = go.GetComponent<AsteroidSpin>();
            Check(d.key + (d.floating ? " sways upright (" + EnemyRoster.FloatSwayDegrees + " deg)" : " tumbles"),
                  spin != null && spin.Sways == d.floating &&
                  (!d.floating || spin.swayDegrees > 0f && spin.swayDegrees <= 15f));
            UnityEngine.Object.DestroyImmediate(go);
            if (!d.floating) continue;
            // the drawn bob: idle frames 1-3 move against the key pose
            string strip = "Assets/Art/Resources/" + d.StripPath + ".png";
            var keyPose = Mask(strip, 0);
            bool bobs = false;
            foreach (int k in new[] { 1, 2, 3 }) bobs |= keyPose.Length > 0 && IoU(keyPose, Mask(strip, k)) < .97f;
            Check(d.key + " draws its float bob into the idle frames", bobs);
        }

        // The restored Verdant Spore Rock keeps the first-pass grass-cap
        // silhouette the player liked.
        var spore = EnemyRoster.Find("verdant_rock_spore");
        Check("the Verdant spore rock exists and floats", spore != null && spore.floating && spore.world == 2);
        Check("the first-pass spore rock reference is kept (" + FirstPassSpore + ")", File.Exists(FirstPassSpore));
        if (spore != null && File.Exists(FirstPassSpore))
        {
            float iou = IoU(Mask(spore), Mask(FirstPassSpore));
            Check(string.Format("verdant_rock_spore keeps the first-pass grass-cap silhouette (IoU {0:F3} >= 0.85)", iou),
                  iou >= .85f);
        }
    }
}
