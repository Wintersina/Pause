using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// SPACE HAZARD ROCK TYPES (Scripts/Gameplay/Asteroids/SpaceHazardRockTypes.cs): the falling field's own art, apart from the backdrop's.
//
//   ART        four types (shard, ember, ore, crystal), each a 4-frame rock strip and a 4-frame chunk strip + a Codex still
//   TABLE      the measured fill / radius numbers match the PNGs' alpha (re-draw a strip -> update the table)
//   SPAWN      every type turns up; a split's chunks keep their parent's type and use its chunk strip; the field never uses asteroid_00..02
//   BLINK      the accent loop advances, from a random phase
//   COLLIDER   a circle of ColliderFit x the art's measured radius, per type, rock and chunk
//   FEEDBACK   a non-lethal hit: white flash, jolt, accent chips (pooled; nothing allocated); a lethal one is the usual burst
//   BACKDROP   the drift rocks are further away (haze, one mip softer) and stay decorative
//   CODEX      four Hazard entries found on the first break, with the NEW badge, 5 dust, and Collect all
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceHazardRocksTest.Run
public static class SpaceHazardRocksTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HAZROCK] PASS  " : "[HAZROCK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    const string ArtDir = "/Art/Backgrounds/Resources/Worlds/Space/HazardRocks/";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        PlayerPrefs.SetString("HasDoneTut", "true");
        try
        {
            Art();
            Table();
            Spawns();
            Chunks();
            Blink();
            Collider();
            Feedback();
            Backdrop();
            CodexEntries();
            SpaceOnly();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            SpaceAsteroidField.Disabled = false;
            SpaceAsteroidField.SeedOverride = -1;
            SpaceAsteroidFieldDirector.WorldOverride = -1;
            SpaceAsteroidField.ClearAll();
            SpaceAsteroidField.Forget();
        }
        Debug.Log("[HAZROCK] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        SpaceAsteroidField.ClearAll();
        RockHitFx.Reset();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6.6f;
        cam.aspect = 9f / 16f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.lifeCounter = 0;
        BossEncounter.ResetRun();
        BossRails.Reset();
        SpaceAsteroidField.Disabled = false;
        SpaceAsteroidField.SeedOverride = 77;
        SpaceAsteroidFieldDirector.WorldOverride = -1;
        Random.InitState(4242);
    }

    // ---- art ----

    static void Art()
    {
        Fresh();
        Check("four hazard rock types: shard, ember, ore, crystal",
              SpaceHazardRockTypes.Count == 4 && string.Join(",", SpaceHazardRockTypes.All.Select(t => t.key)) == "shard,ember,ore,crystal");
        Check("the blink rate is 6-8 fps", SpaceHazardRockTypes.BlinkFps >= 6f && SpaceHazardRockTypes.BlinkFps <= 8f);
        var driftTextures = new HashSet<Texture>();
        foreach (var d in SpaceRockTypes.All) { var f = d.Frames; if (f != null && f.Length > 0) driftTextures.Add(f[0].texture); }
        foreach (var t in SpaceHazardRockTypes.All)
        {
            var f = t.Frames; var c = t.ChunkFrames;
            Check(t.key + ": the rock strip loads " + SpaceHazardRockTypes.BlinkFrames + " frames of 512x512, shared",
                  f != null && f.Length == SpaceHazardRockTypes.BlinkFrames && f.All(x => x != null && Mathf.Approximately(x.rect.width, 512f) && Mathf.Approximately(x.rect.height, 512f)) && t.Frames == f);
            Check(t.key + ": the chunk strip loads " + SpaceHazardRockTypes.BlinkFrames + " frames of 512x512, shared",
                  c != null && c.Length == SpaceHazardRockTypes.BlinkFrames && c.All(x => x != null && Mathf.Approximately(x.rect.width, 512f)) && t.ChunkFrames == c);
            Check(t.key + ": a Codex still", t.Still != null);
            Check(t.key + ": its art is its own (not asteroid_00..02, not a drift rock)",
                  f != null && c != null && f[0].texture.name.StartsWith("space_hazard_rock_" + t.key) && c[0].texture.name.StartsWith("space_hazard_rock_" + t.key + "_chunk") &&
                  !driftTextures.Contains(f[0].texture) && !driftTextures.Contains(c[0].texture));
            Check(t.key + ": the rock and its chunk are different drawings", f != null && c != null && f[0].texture != c[0].texture);
        }
        Check("the field reports ready", SpaceAsteroidField.ArtReady && SpaceHazardRockTypes.Ready);
    }

    // The table in SpaceHazardRockTypes against the PNGs themselves (alpha >= 128): fill = widest side, radius = area-equivalent.
    static void Table()
    {
        foreach (var t in SpaceHazardRockTypes.All)
            foreach (bool chunk in new[] { false, true })
            {
                string path = Application.dataPath + ArtDir + "space_hazard_rock_" + t.key + (chunk ? "_chunk" : "") + "_blink.png";
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool loaded = File.Exists(path) && ImageConversion.LoadImage(tex, File.ReadAllBytes(path));
                if (!loaded) { Check(t.key + (chunk ? " chunk" : "") + ": PNG readable for the footprint check", false); continue; }
                int w = tex.width / 4, h = tex.height;
                var px = tex.GetPixels32();
                float maxFill = 0f, sumRadius = 0f;
                for (int f = 0; f < 4; f++)
                {
                    int minX = w, maxX = -1, minY = h, maxY = -1; long area = 0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            if (px[y * tex.width + f * w + x].a >= 128) { area++; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                    maxFill = Mathf.Max(maxFill, Mathf.Max(maxX - minX + 1, maxY - minY + 1) / (float)w);
                    sumRadius += Mathf.Sqrt(area / Mathf.PI) / w;
                }
                Object.DestroyImmediate(tex);
                float wantFill = chunk ? t.chunkFill : t.fill, wantRadius = chunk ? t.chunkRadius : t.radius;
                Check(t.key + (chunk ? " chunk" : " rock") + ": table matches the art (fill " + maxFill.ToString("F3") + " vs " + wantFill.ToString("F3") +
                      ", radius " + (sumRadius / 4f).ToString("F3") + " vs " + wantRadius.ToString("F3") + ")",
                      Mathf.Abs(maxFill - wantFill) < .012f && Mathf.Abs(sumRadius / 4f - wantRadius) < .012f);
            }
    }

    // ---- spawns ----

    static void Spawns()
    {
        Fresh();
        var seen = new HashSet<int>();
        for (int i = 0; i < 4; i++)
        {
            var a = SpaceAsteroidField.Spawn(i % 3, i, new Vector2(0f, 2f), 0f, 0f, 20f);
            seen.Add(a.TypeIndex);
            var go = a.gameObject;
            Check("art index " + i + " -> " + a.Kind.key + ": named for the Codex (" + go.name + ")", go.name == a.Kind.ObjectName && go.CompareTag("Astr") && !a.IsChunk);
            Check("... showing a frame of its own rock strip", System.Array.IndexOf(a.Kind.Frames, go.GetComponent<SpriteRenderer>().sprite) >= 0);
            SpaceAsteroidField.ClearAll();
        }
        Check("the four art indexes give the four types", seen.Count == 4);

        // the director: every type turns up
        var dirGo = new GameObject("~dir");
        var dir = dirGo.AddComponent<SpaceAsteroidFieldDirector>();
        dir.Begin();
        var types = new HashSet<int>(); var tiers = new HashSet<int>();
        bool allHazard = true;
        for (int i = 0; i < 300; i++)
        {
            var a = dir.SpawnOne();
            if (a != null)
            {
                types.Add(a.TypeIndex); tiers.Add(a.Tier);
                allHazard &= a.GetComponent<SpriteRenderer>().sprite.texture.name.StartsWith("space_hazard_rock_");
            }
            SpaceAsteroidField.ClearAll();
        }
        Check("the director's rocks cover all four types (" + types.Count + ") and all three tiers (" + tiers.Count + ")", types.Count == 4 && tiers.Count == 3);
        Check("the director's rocks are all hazard-strip art, none of asteroid_00..02", allHazard);
        Object.DestroyImmediate(dirGo);
        SpaceAsteroidField.ClearAll();
    }

    // ---- splits ----

    static void Chunks()
    {
        foreach (var kind in SpaceHazardRockTypes.All)
        {
            Fresh();
            int type = System.Array.IndexOf(SpaceHazardRockTypes.All, kind);
            var big = SpaceAsteroidField.Spawn(2, type, new Vector2(0f, 2f), 0f, 0f, 10f);
            big.TakeShipAttack(0, 3f, big.transform.position);
            var pieces = SpaceAsteroidField.Live.Where(x => x != null).ToArray();
            bool ok = pieces.Length == 2;
            foreach (var p in pieces)
            {
                var sprite = p.GetComponent<SpriteRenderer>().sprite;
                ok &= p.TypeIndex == type && p.IsChunk && p.Tier == 1 && System.Array.IndexOf(kind.ChunkFrames, sprite) >= 0 && p.name == kind.ObjectName;
            }
            Check(kind.key + ": a split gives two medium chunks of the same type, from its chunk strip", ok);
            if (pieces.Length == 2)
            {
                var m = pieces[0];
                m.TakeShipAttack(0, 2f, m.transform.position);
                var small = SpaceAsteroidField.Live.Where(x => x != null && x.Tier == 0).ToArray();
                Check(kind.key + ": a chunk's chunks keep the type too", small.Length == 2 && small.All(x => x.TypeIndex == type && x.IsChunk));
                // a chunk is drawn as wide (art-wise) as a whole rock of its tier
                float artWhole = SpaceAsteroidField.Width[0] * kind.fill;
                var s0 = small[0];
                var spr = s0.GetComponent<SpriteRenderer>();
                float artChunk = spr.sprite.bounds.size.x * s0.transform.localScale.x * kind.chunkFill;
                Check(kind.key + ": a chunk's art is as wide as a whole rock's of its tier (" + artChunk.ToString("F2") + " vs " + artWhole.ToString("F2") + ")",
                      Mathf.Abs(artChunk - artWhole) < .08f * artWhole + .01f || kind.ChunkBoost >= 2.19f);
            }
            SpaceAsteroidField.ClearAll();
        }
    }

    // ---- blink ----

    static void Blink()
    {
        Fresh();
        var a = SpaceAsteroidField.Spawn(1, 0, new Vector2(0f, 6f), 0f, 0f, 0f);
        var sr = a.GetComponent<SpriteRenderer>();
        var shown = new HashSet<Sprite> { sr.sprite };
        for (int i = 0; i < 60; i++) { a.Step(Dt, 0f, -50f); shown.Add(sr.sprite); }
        Check("the blink loop advances through all four frames (" + shown.Count + ")", shown.Count == 4 && shown.All(s => System.Array.IndexOf(a.Kind.Frames, s) >= 0));
        // 7 fps: one second is about seven frame changes
        int changes = 0; Sprite last = sr.sprite;
        for (int i = 0; i < 30; i++) { a.Step(Dt, 0f, -50f); if (sr.sprite != last) { changes++; last = sr.sprite; } }
        Check("... at about 7 frames per second (" + changes + " in 1 s)", changes >= 6 && changes <= 8);

        var phases = new HashSet<int>();
        for (int i = 0; i < 40; i++) { phases.Add(SpaceAsteroidField.Spawn(0, 0, new Vector2(0f, 6f), 0f, 0f, 0f).Frame); }
        Check("rocks start at random blink phases (" + phases.Count + " of 4 seen)", phases.Count >= 3);
        SpaceAsteroidField.ClearAll();
    }

    // ---- colliders ----

    static void Collider()
    {
        Fresh();
        bool ok = true; string bad = "";
        foreach (var kind in SpaceHazardRockTypes.All)
        {
            int type = System.Array.IndexOf(SpaceHazardRockTypes.All, kind);
            for (int tier = 0; tier < 3; tier++)
            {
                var whole = SpaceAsteroidField.Spawn(tier, type, new Vector2(0f, 2f), 0f, 0f, 0f);
                var col = whole.GetComponent<CircleCollider2D>();
                float world = col.radius * whole.transform.localScale.x;
                float want = SpaceHazardRockTypes.ColliderFit * kind.radius * SpaceAsteroidField.Width[tier];
                float art = kind.fill * SpaceAsteroidField.Width[tier] * .5f;
                if (Mathf.Abs(world - want) > .003f || world >= art) { ok = false; bad += kind.key + "/" + tier + " "; }
                SpaceAsteroidField.ClearAll();
            }
        }
        Check("each rock's collider is ColliderFit x its art's measured radius, inside the drawing " + bad, ok);
        var shard = SpaceAsteroidField.Spawn(2, 0, new Vector2(0f, 2f), 0f, 0f, 0f);
        var ore = SpaceAsteroidField.Spawn(2, 2, new Vector2(2f, 2f), 0f, 0f, 0f);
        Check("the colliders differ per type (a thin ore lump is smaller than an ember boulder)",
              ore.GetComponent<CircleCollider2D>().radius < SpaceAsteroidField.Spawn(2, 1, new Vector2(-2f, 2f), 0f, 0f, 0f).GetComponent<CircleCollider2D>().radius);
        SpaceAsteroidField.ClearAll();
    }

    // ---- hit feedback ----

    static void Feedback()
    {
        Fresh();
        var a = SpaceAsteroidField.Spawn(2, 1, new Vector2(0f, 5f), 0f, 0f, 0f);
        Vector3 pos = a.transform.position;
        Check("before a hit: no flash, no jolt, no chips", !a.FlashShowing && !a.JoltActive && RockHitFx.ActiveChips == 0);
        a.TakeShipAttack(0, 1f, pos + new Vector3(0f, 1f, 0f));
        var flashChild = a.transform.Find("flash");
        Check("a non-lethal hit flashes the rock (white overlay up)", !a.Broken && a.FlashShowing && (RockHitFx.Flash == null || (flashChild != null && flashChild.GetComponent<SpriteRenderer>().enabled)));
        Check("... jolts it", a.JoltActive);
        Check("... and throws accent chips (" + RockHitFx.ActiveChips + ")", RockHitFx.ActiveChips >= SpaceAsteroidField.HitChips);
        Check("the flash lasts long enough to see (>= .1 s)", SpaceAsteroidField.HitFlashSeconds >= .1f);
        // the jolt is a hair, away from the blow (the blow came from above: the rock is nudged down)
        a.Step(Dt, 0f, -50f);
        Check("the jolt is tiny (under .1 u) and away from the blow", a.transform.position.y < pos.y && pos.y - a.transform.position.y < .1f);
        for (int i = 0; i < 12; i++) { a.Step(Dt, 0f, -50f); RockHitFx.Step(Dt); }
        Check("the flash and jolt settle", !a.FlashShowing && !a.JoltActive && (flashChild == null || !flashChild.GetComponent<SpriteRenderer>().enabled));
        Check("... the rock is back on its line (no drift left over)", Mathf.Abs(a.transform.position.x - pos.x) < 1e-3f);
        for (int i = 0; i < 20; i++) RockHitFx.Step(Dt);
        Check("the chips fade and go back to the pool", RockHitFx.ActiveChips == 0);

        // nothing allocated by a hit once the pool exists
        a.TakeShipAttack(0, .1f, pos);          // warm-up (builds the pool)
        for (int i = 0; i < 6; i++) { a.Step(Dt, 0f, -50f); RockHitFx.Step(Dt); }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        a.TakeShipAttack(0, .1f, pos + Vector3.up);
        a.Step(Dt, 0f, -50f);
        RockHitFx.Step(Dt);
        long grew = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("a hit allocates nothing (" + grew + " bytes)", grew <= 32);

        // a lethal hit is the ordinary burst: no feedback state on a dead rock
        var small = SpaceAsteroidField.Spawn(0, 3, new Vector2(1.5f, 5f), 0f, 0f, 0f);
        small.TakeShipAttack(0, 1f, small.transform.position);
        Check("a lethal hit just bursts", small.Broken);
        // rapid hits do not flood the pool
        RockHitFx.Reset();
        var tough = SpaceAsteroidField.Spawn(2, 2, new Vector2(-1.5f, 5f), 0f, 0f, 0f);
        for (int i = 0; i < 20; i++) tough.TakeShipAttack(0, .01f, tough.transform.position + Vector3.up);
        Check("a flurry of tiny hits throws one burst of chips, not twenty (" + RockHitFx.ActiveChips + ")", RockHitFx.ActiveChips <= SpaceAsteroidField.HitChips + 1);
        SpaceAsteroidField.ClearAll();
        RockHitFx.Reset();
    }

    // ---- the backdrop rocks stay a further, decorative layer ----

    static void Backdrop()
    {
        Check("drift rocks are see-through (haze < 1) but still clearly visible", SpaceDirector.DriftHaze < 1f && SpaceDirector.DriftHaze >= .7f);
        foreach (var d in SpaceRockTypes.All)
        {
            var imp = AssetImporter.GetAtPath("Assets/Art/Backgrounds/Resources/Worlds/Space/DriftRocks/space_hazard_rock_" + d.key + "_blink.png") as TextureImporter;
            Check(d.key + ": the drift strip is sampled one mip softer (mip bias +1)", imp != null && imp.mipmapEnabled && Mathf.Approximately(imp.mipMapBias, 1f));
        }
        foreach (var t in SpaceHazardRockTypes.All)
        {
            var imp = AssetImporter.GetAtPath("Assets/Art/Backgrounds/Resources/Worlds/Space/HazardRocks/space_hazard_rock_" + t.key + "_blink.png") as TextureImporter;
            Check(t.key + ": the hazard strip stays crisp (no mip bias)", imp != null && Mathf.Approximately(imp.mipMapBias, 0f));
        }
    }

    // ---- codex ----

    static void CodexEntries()
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        PlayerPrefs.DeleteKey(Codex.BackPaidKey);
        PlayerPrefs.DeleteKey(Codex.NewKey);
        PlayerPrefs.DeleteKey(Codex.AckKey);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        Codex.Reload();
        Fresh();
        var names = new HashSet<string>();
        foreach (var t in SpaceHazardRockTypes.All)
        {
            var e = Codex.Find(t.CodexId);
            Check(t.key + ": a Hazards entry " + t.CodexId, e != null && e.category == CodexCategory.Hazards && t.CodexId.StartsWith("hazard_space_rock_"));
            if (e == null) continue;
            Check(t.key + ": a name, a short text and the frame-0 icon", !string.IsNullOrEmpty(e.name) && names.Add(e.name) && e.lore.Length >= 20 && e.Sprite == t.Still && e.Sprite != null);
            Check(t.key + ": pays the common rock payout (" + CodexPayout.Rock + ")", CodexPayout.For(e) == CodexPayout.Rock);
            Check(t.key + ": the spawned rock's name maps to it", Codex.IdForName(t.ObjectName) == t.CodexId && Codex.IdForName(t.ObjectName + "(Clone)") == t.CodexId);
            Check(t.key + ": undiscovered (" + Codex.IsDiscovered(e) + "), not new (" + Codex.IsNew(e.id) + "), nothing pending (" + Codex.IsRewardPending(e) + ")",
                  !Codex.IsDiscovered(e) && !Codex.IsNew(e.id) && !Codex.IsRewardPending(e));
        }
        Check("the old drift asteroid still has no entry", Codex.Find("hazard_space_drift_asteroid") == null);

        // a hit that does not break it discovers nothing
        Check("a hit that does not break it discovers nothing", ForgetAndHit());

        // first break discovers it, type by type: NEW badge, 5 dust paid on the spot
        float dust0 = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        for (int i = 0; i < SpaceHazardRockTypes.Count; i++)
        {
            var rock = SpaceAsteroidField.Spawn(0, i, new Vector2(0f, 2f), 0f, 0f, 0f);
            string id = SpaceHazardRockTypes.All[i].CodexId;
            rock.TakeShipAttack(0, 1f, rock.transform.position);
            Check(SpaceHazardRockTypes.All[i].key + ": found on its first break, with the NEW badge",
                  rock.Broken && Codex.IsDiscovered(id) && Codex.IsNew(id) && Codex.IsRewardCollected(id));
        }
        Check("each first find paid " + CodexPayout.Rock + " star dust once (" + (PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) - dust0) + ")",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) - dust0, 4 * CodexPayout.Rock));
        Check("they badge the Hazards tab", Codex.NewIn(CodexCategory.Hazards) >= 4);
        SpaceAsteroidField.ClearAll();

        // a restored older save lists the finds without payment: they wait for Collect all, which pays each once
        string found = string.Join(",", SpaceHazardRockTypes.All.Select(t => t.CodexId));
        PlayerPrefs.SetString(Codex.PrefsKey, found);
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        Codex.Reload();
        var ids = Codex.PendingIds();
        Check("restored finds wait for COLLECT (" + ids.Count + " pending)", SpaceHazardRockTypes.All.All(t => ids.Contains(t.CodexId)) && Codex.PendingIn(CodexCategory.Hazards) >= 4);
        var paid = new List<string>();
        var seq = new CodexCollectSequence(ids, Codex.CollectReward);
        seq.Paid += (id, dust) => paid.Add(id);
        seq.Finish();
        Check("Collect all pays the four rocks " + CodexPayout.Rock + " each and clears their badges",
              SpaceHazardRockTypes.All.All(t => paid.Contains(t.CodexId) && Codex.IsRewardCollected(t.CodexId) && !Codex.IsRewardPending(t.CodexId)) && Codex.PendingTotal == 0 &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 100f + 4 * CodexPayout.Rock));
        Check("... paid once: collecting again adds nothing", Codex.CollectAllRewards() == 0);
        // a stale save that holds an id for a retired entry stays harmless
        PlayerPrefs.SetString(Codex.PrefsKey, "hazard_space_drift_asteroid," + SpaceHazardRockTypes.All[0].CodexId);
        Codex.Reload();
        Check("an old save with the retired drift asteroid id keeps its real finds valid", Codex.IsDiscovered(SpaceHazardRockTypes.All[0].CodexId) && !Codex.IsDiscovered("hazard_space_drift_asteroid"));
        SpaceAsteroidField.ClearAll();
    }

    static bool ForgetAndHit()
    {
        var rock = SpaceAsteroidField.Spawn(2, 2, new Vector2(0f, 3f), 0f, 0f, 0f);
        string id = SpaceHazardRockTypes.All[2].CodexId;
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
        rock.TakeShipAttack(0, 1f, rock.transform.position);
        bool ok = !rock.Broken && !Codex.IsDiscovered(id);
        SpaceAsteroidField.ClearAll();
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
        return ok;
    }

    // ---- rules ----

    static void SpaceOnly()
    {
        Fresh();
        Check("the hazard field is Space only", SpaceAsteroidFieldDirector.Blocked(0, 120f) == null &&
              Enumerable.Range(1, 6).All(w => SpaceAsteroidFieldDirector.Blocked(w, 120f) == "not space"));
        Check("the spawn rules are unchanged (25 s opening, 3 alive, 6 total, tiers 1/2/3 hits)",
              SpaceAsteroidField.FirstSeconds == 25f && SpaceAsteroidField.MaxAlive == 3 && SpaceAsteroidField.MaxTotal == 6 &&
              SpaceAsteroidField.SizeHits[0] == 1f && SpaceAsteroidField.SizeHits[1] == 2f && SpaceAsteroidField.SizeHits[2] == 3f);
    }
}
