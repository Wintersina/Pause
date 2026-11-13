using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Space's drifting asteroid field (SpaceAsteroidDrift): Codex's three
// cracked rocks cross the Space backdrop -- and only Space -- as greyed,
// dimmed, noticeably BIGGER rocks than the hazard field's, moving purely
// horizontally (in from the left edge and across, or from the right), never
// down with the backdrop. Each is its own size (bigger = nearer, slower,
// slower tumble, more and bigger smoke), tumbles either way, its cracks (only
// the cracks) blink in three hard steps on its own pattern and phase, sparks
// pop at crack hot spots and pooled smoke puffs trail behind it, capped per
// rock and overall. All of it is behind gameplay, keeps clear of the
// stations at its depth, freezes while paused and allocates nothing per
// frame. The rocks are pure scenery (nothing can hit or target them),
// and now and then a fated rock collides with another rock or flies into a
// planet (decorative bursts, a crater mark, no score).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod AsteroidDriftTest.Run
public static class AsteroidDriftTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AST] PASS  " : "[AST] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
    const float Dt = 1f / 30f;
    const float PhoneHeightPx = 2532f;

    [System.Serializable] class AtlasRect { public string n; public int x, y, w, h; }
    [System.Serializable] class AtlasManifest { public AtlasRect[] sprites; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        int savedSeed = SpaceDirector.AsteroidSeedOverride;
        float savedPx = SpaceDirector.AsteroidPixelHeightOverride;
        try
        {
            SpaceDirector.AsteroidPixelHeightOverride = PhoneHeightPx;
            Art();
            SpaceOnly();
            LongRun();
            Decorative();
            RockTypesRun();
            Events();
            Determinism();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            SpaceDirector.AsteroidSeedOverride = savedSeed;
            SpaceDirector.AsteroidPixelHeightOverride = savedPx;
            SpaceDirector.RunOnOverride = false;
            SpaceDirector.EventsDisabled = false;
            SpaceAsteroidField.ClearAll();
        }
        Debug.Log("[AST] failures: " + fails);
        return fails;
    }

    // ---- the baked atlas against Codex's art ------------------------------------------

    static void Art()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(Dir + SpaceAsteroidDrift.AtlasName + ".png"));
        var px = tex.GetPixels32();
        int tw = tex.width;
        var m = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText(Dir + SpaceAsteroidDrift.AtlasName + ".json"));
        var rects = new Dictionary<string, AtlasRect>();
        foreach (var r in m.sprites) rects[r.n] = r;

        int offCrack = 0, red = 0, maskPx = 0, rockPx = 0;
        string bad = "";
        for (int i = 0; i < 3; i++)
        {
            AtlasRect crack, rock;
            if (!rects.TryGetValue("crack" + i + "_00", out crack) || !rects.TryGetValue("rock" + i + "_00", out rock))
            {
                bad += "asteroid " + i + " cells missing; ";
                offCrack++;
                continue;
            }
            // every hot spot sits on (within 3 px of) a crack pixel of the 256 mask
            foreach (var f in SpaceAsteroidDrift.HotSpots[i])
            {
                int cx = Mathf.FloorToInt(crack.w * 0.5f + f.x * crack.w), cy = Mathf.FloorToInt(crack.h * 0.5f + f.y * crack.w);
                bool hit = false;
                for (int dy = -3; dy <= 3 && !hit; dy++)
                    for (int dx = -3; dx <= 3 && !hit; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= crack.w || y >= crack.h) continue;
                        hit = px[(crack.y + y) * tw + crack.x + x].a > 128;
                    }
                if (!hit) { offCrack++; bad += "asteroid " + i + " spot (" + f.x + "," + f.y + "); "; }
            }
            // the mask is the cracks only: few pixels, all magenta / pink / violet, never red
            for (int y = 0; y < crack.h; y++)
                for (int x = 0; x < crack.w; x++)
                {
                    var c = px[(crack.y + y) * tw + crack.x + x];
                    var r = px[(rock.y + y) * tw + rock.x + x];
                    if (r.a > 128) rockPx++;
                    if (c.a <= 128) continue;
                    maskPx++;
                    float h, s, v;
                    Color.RGBToHSV(c, out h, out s, out v);
                    if ((h * 360f >= 345f || h * 360f <= 15f) && s > 0.5f) red++;
                }
        }
        Object.DestroyImmediate(tex);
        Check("every crack hot spot sits on a crack pixel of its asteroid's mask " + bad, offCrack == 0);
        float share = rockPx > 0 ? maskPx / (float)rockPx : 1f;
        Check("the blink mask covers only the cracks (" + maskPx + " mask px = " + (share * 100f).ToString("F1") +
              "% of the rock, < 8%), none of it red (" + red + ")", maskPx > 300 && share < 0.08f && red == 0);
        int redTints = 0;
        foreach (var c in SpaceDirector.AsteroidPuffTints) if (Reddish(c)) redTints++;
        if (Reddish(SpaceDirector.CrackTint)) redTints++;
        Check("smoke and crack tints stay in Space's palette (no red)", redTints == 0);
    }

    static bool Reddish(Color c)
    {
        float h, s, v;
        Color.RGBToHSV(c, out h, out s, out v);
        return (h * 360f >= 345f || h * 360f <= 20f) && s > 0.35f;
    }

    // ---- only Space ------------------------------------------------------------------

    static void SpaceOnly()
    {
        var go = new GameObject("~AsteroidDriftOnly");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            Time.timeScale = 1f;
            moveBackGround.speed = 0.2f;
            string where = "";
            foreach (var spec in BackdropCatalog.All)
            {
                wb.Show(spec.world, false);
                for (int i = 0; i < 20 * 30; i++) wb.Step(Dt);
                int rocks = 0;
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!sr.enabled || !sr.gameObject.activeInHierarchy || sr.sprite == null) continue;
                    string n = sr.sprite.name;
                    bool drifting = n.Length > 4 && (n.StartsWith("rock") || n.StartsWith("puff")) && char.IsDigit(n[4]);
                    bool crack = n.Length > 5 && n.StartsWith("crack") && char.IsDigit(n[5]);   // (Space's crackN frames; Desert's crack_scar is a backdrop piece)
                    if (drifting || crack || n.StartsWith("asteroid_0")) rocks++;
                }
                var sd = wb.Current.Director as SpaceDirector;
                if (spec.world == "Space")
                    Check("Space builds the drifting asteroid field and shows it (" + rocks + " renderers)",
                          sd != null && sd.AsteroidDrift != null && sd.AsteroidDrift.Ready && rocks > 0);
                else if (rocks > 0 || sd != null) where += spec.world + " ";
            }
            Check("no other world shows the drifting asteroids " + where, where.Length == 0);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ---- decorative only -------------------------------------------------------------

    static void Decorative()
    {
        Time.timeScale = 1f;
        moveBackGround.speed = 0.2f;
        SpaceDirector.AsteroidSeedOverride = -1;
        SpaceDirector.RunOnOverride = true;         // a run is on: this used to be what armed the rocks
        SpaceDirector.EventsDisabled = true;
        SpaceAsteroidField.ClearAll();
        var go = new GameObject("~AsteroidDecorTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            int liveBefore = ClearTarget.Live.Count;
            wb.Show("Space", false);
            var drift = (wb.Current.Director as SpaceDirector)?.AsteroidDrift;
            if (drift == null || !drift.Ready) { Check("decorative: the drift field is ready", false); return; }
            for (int i = 0; i < 6 * 30; i++) wb.Step(Dt);

            Check("rocks are in flight", drift.ActiveCount > 0);
            bool clean = true;
            foreach (var r in drift.Rocks)
            {
                var root = r.piece.root;
                clean &= root.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                         root.GetComponentsInChildren<Collider>(true).Length == 0 &&
                         root.GetComponentsInChildren<ClearTarget>(true).Length == 0 &&
                         root.GetComponentsInChildren<IShipAttackTarget>(true).Length == 0;
            }
            Check("a drift rock has no collider, no ClearTarget and no attack-target component", clean);
            bool noBody = true;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                noBody &= t.name != "asteroid targets" && t.name != "space_drift_asteroid" && !t.CompareTag("Astr");
            Check("the backdrop holds no hit bodies and no Astr-tagged object", noBody);
            Check("nothing was registered with the weapons' target list (no lock-on, auto-aim, homing or power can find a rock)",
                  ClearTarget.Live.Count == liveBefore && ShipTargets.Collect(new List<ClearTarget>()) == 0);
            Check("the weapons and the ultimate see no threat and no hazard", CountThreats() == 0);

            // the collision / impact events still work and stay decorative (see Events)
            for (int i = 0; i < 20 * 30; i++) wb.Step(Dt);
            Check("after 20 s more still nothing targetable", ClearTarget.Live.Count == liveBefore);
        }
        finally { Object.DestroyImmediate(go); SpaceDirector.RunOnOverride = false; }

        // the dodge-field rocks take every hit in full like the backdrop rocks (uncapped: a spent elite allowance
        // does not shield them), and still need 1 / 2 / 3 hits
        SpaceAsteroidField.ClearAll();
        var a = SpaceAsteroidField.Spawn(2, 0, Vector2.zero, 0f, 0f, 0f);
        float none = 0f;
        ShipAttackHits.Hit(a.gameObject, 0, 1f, ref none);
        Check("the field rock takes a hit outside the firing's allowance and keeps its own hit points",
              Mathf.Approximately(a.Health, SpaceAsteroidField.SizeHits[2] - 1f) && none == 0f && SpaceAsteroidField.SizeHits[0] == 1f &&
              SpaceAsteroidField.SizeHits[1] == 2f && SpaceAsteroidField.SizeHits[2] == 3f);
        SpaceAsteroidField.ClearAll();
    }

    static void RockTypesRun()
    {
        Time.timeScale = 1f;
        moveBackGround.speed = 0.2f;
        SpaceDirector.AsteroidSeedOverride = -1;
        SpaceDirector.RunOnOverride = true;
        SpaceDirector.EventsDisabled = true;
        var go = new GameObject("~AsteroidTypesTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            wb.Show("Space", false);
            var drift = (wb.Current.Director as SpaceDirector)?.AsteroidDrift;
            if (drift == null || !drift.Ready) { Check("rock types: the drift field is ready", false); return; }
            RockTypes(wb, drift);
        }
        finally { Object.DestroyImmediate(go); SpaceDirector.RunOnOverride = false; SpaceDirector.EventsDisabled = false; }
    }

    static int CountThreats()
    {
        int n = 0;
        foreach (var t in ClearTarget.Live)
            if (ShipTargets.IsThreat(t)) n++;
        return n;
    }

    // ---- a long Space run ------------------------------------------------------------

    class Life
    {
        public SpaceAsteroidDrift.Rock rock;
        public float age, size, sizeMul, speed, spin, heading, puffW, puffLife, emitEvery;
        public int tier, level, pattern;
        public float drawnPx;
    }

    static void LongRun()
    {
        Time.timeScale = 1f;
        moveBackGround.speed = 0.2f;
        SpaceDirector.AsteroidSeedOverride = -1;
        var go = new GameObject("~AsteroidDriftTest");
        var wb = go.AddComponent<WorldBackdrop>();
        SpaceDirector.EventsDisabled = true;       // (the collisions and impacts have their own test, Events)
        try
        {
            wb.Show("Space", false);
            var sd = wb.Current.Director as SpaceDirector;
            var drift = sd != null ? sd.AsteroidDrift : null;
            Check("the drift field is ready (atlas, crack material " +
                  (drift != null && drift.CrackMaterial != null ? drift.CrackMaterial.shader.name : "-") + ")",
                  drift != null && drift.Ready && drift.CrackMaterial != null &&
                  drift.CrackMaterial.shader.name == "Pause/BackdropAdditive" &&
                  drift.FxAtlas.texture.filterMode == FilterMode.Point);
            if (drift == null || !drift.Ready) return;
            float halfW = wb.Current.HalfWidth, halfH = wb.Current.HalfHeight;
            var spec = wb.Current.Spec;
            Check("the field opens already populated (" + drift.VisibleCount + " in view)", drift.VisibleCount >= 2);

            var lives = new List<Life>();
            var current = new Dictionary<SpaceAsteroidDrift.Rock, Life>();
            int samples = 0, inBand = 0, maxVisible = 0, sumVisible = 0;
            int quadrants = 0;
            int ltr = 0, rtl = 0, tooSmall = 0, notGrey = 0, verticalBad = 0;
            float minGapAtSpawn = 360f;
            int windows = 0, threeClassWindows = 0;
            bool small = false, medium = false, large = false;
            int perRockOver = 0, totalOver = 0, maxLive = 0;
            int orderBad = 0, sameTierOverlaps = 0, crossTierSamples = 0;
            float trailSum = 0f; int trailN = 0;
            float smallestPuffPx = float.MaxValue;
            float biggestShare = 0f;
            int levelBad = 0, levelsUsed = 0;
            var tiers = new int[SpaceDirector.Tiers.Length];
            var minHeadingGapWorst = 360f;

            const int Seconds = 15 * 60;
            for (int i = 0; i < Seconds * 30; i++)
            {
                moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
                wb.Step(Dt);

                // spawns: a new life when the rock turns active / its age restarts
                for (int k = 0; k < drift.Rocks.Count; k++)
                {
                    var r = drift.Rocks[k];
                    Life l;
                    current.TryGetValue(r, out l);
                    if (!r.active) { current.Remove(r); continue; }
                    if (l != null && r.piece.age >= l.age) { l.age = r.piece.age; continue; }
                    l = new Life
                    {
                        rock = r, age = r.piece.age, size = r.piece.size, sizeMul = r.sizeMul, speed = r.speed,
                        spin = r.spin, heading = r.heading, tier = r.piece.tier, level = r.level, pattern = r.pattern,
                        puffW = Mathf.Max(SpaceDirector.PuffSizeMin, r.piece.size * SpaceDirector.PuffPerSize),
                        puffLife = r.puffLife, emitEvery = r.emitEvery,
                        drawnPx = r.piece.size * PhoneHeightPx / (2f * halfH),
                    };
                    current[r] = l;
                    lives.Add(l);
                    tiers[l.tier]++;
                    // direction: purely horizontal, left -> right or right -> left; bigger than any hazard rock; grey
                    if (l.heading < 90f) ltr++; else rtl++;
                    quadrants |= l.heading < 90f ? 1 : 2;
                    if (r.piece.vy != 0f || r.piece.rate != 0f || r.piece.vx * (l.heading < 90f ? 1f : -1f) <= 0f) verticalBad++;
                    if (l.size < SpaceAsteroidField.Width[2] + 1e-3f) tooSmall++;
                    Color pc = r.piece.color;
                    if (pc.r > 0.7f || pc.g > 0.7f) notGrey++;
                    // largest art vs the screen width
                    biggestShare = Mathf.Max(biggestShare, l.size * SpaceDirector.AsteroidArtFill / (2f * halfW));
                    // the chosen copy is drawn near 1:1
                    float src = l.level < 0 ? 512f : SpaceAsteroidDrift.LevelPx[l.level];
                    bool smallest = l.level == SpaceAsteroidDrift.Levels - 1, original = l.level < 0;
                    if ((!smallest && src / l.drawnPx > SpaceAsteroidDrift.MaxMinify + 1e-3f) ||
                        (!original && l.drawnPx / src > 2f / SpaceAsteroidDrift.MaxMinify + 1e-3f)) levelBad++;
                    levelsUsed |= 1 << (l.level + 1);
                    if (l.sizeMul < 0.6f) small = true; else if (l.sizeMul < 1.0f) medium = true; else large = true;
                }

                // every second: population, caps, sorting, overlaps, trails
                if (i % 30 != 0) continue;
                samples++;
                int vis = drift.VisibleCount;
                sumVisible += vis;
                maxVisible = Mathf.Max(maxVisible, vis);
                if (vis >= 2 && vis <= 5) inBand++;
                var live = new int[drift.Rocks.Count];
                int liveTotal = 0;
                for (int k = 0; k < drift.Puffs.Length; k++)
                {
                    int owner = drift.PuffOwner[k];
                    if (owner < 0) continue;
                    liveTotal++;
                    live[owner]++;
                    var r = drift.Rocks[owner];
                    var sr = drift.Puffs[k];
                    if (sr.sortingOrder >= 0 || sr.sortingOrder != r.order - SpaceDirector.RockOrder + SpaceDirector.PuffOrder ||
                        sr.sortingOrder >= r.crack.sortingOrder) orderBad++;
                    // where is the puff relative to its rock, along the rock's travel?
                    Vector3 d = sr.transform.localPosition - r.piece.root.localPosition;
                    float s = Mathf.Max(1e-4f, r.speed);
                    if (drift.PuffAge[k] > 0.8f) { trailSum += (d.x * r.piece.vx + d.y * r.piece.vy) / s; trailN++; }
                    float puffPx = sr.bounds.size.x * PhoneHeightPx / (2f * halfH);
                    smallestPuffPx = Mathf.Min(smallestPuffPx, puffPx);
                }
                foreach (int n in live) if (n > SpaceDirector.AsteroidPuffsPer) perRockOver++;
                if (liveTotal > SpaceDirector.AsteroidPuffCap) totalOver++;
                maxLive = Mathf.Max(maxLive, liveTotal);
                foreach (var r in drift.Rocks)
                {
                    if (!r.active) continue;
                    string layer = SpaceDirector.LayerOf(r.piece.tier, false);
                    int lo = spec.Order(layer), o = r.piece.sr.sortingOrder;
                    if (o >= 0 || o < lo || o > lo + 9 || r.crack.sortingOrder >= 0 || r.crack.sortingOrder > lo + 9 ||
                        r.sparks[0].sortingOrder > lo + 9) orderBad++;
                    // stations / planetoids at the same depth: (almost) never overlapping (art reach + clearance)
                    foreach (var pool in new[] { sd.Bodies[1], sd.Bodies[2] })
                        foreach (var b in pool.items)
                        {
                            if (!b.active || b.parent != null) continue;
                            float dx = r.piece.x - b.x, dy = r.piece.y - b.y;
                            float contact = SpaceAsteroidDrift.Reach(r) + Reach(b);
                            if (dx * dx + dy * dy >= contact * contact) continue;
                            if (b.tier == r.piece.tier && r.fate == SpaceAsteroidDrift.FateNone) sameTierOverlaps++;
                            else crossTierSamples++;
                        }
                }
                // 10-second windows: small, medium and large all on screen within it
                if (samples % 10 == 0)
                {
                    windows++;
                    if (windowClasses == 7) threeClassWindows++;
                    windowClasses = 0;
                }
                foreach (var r in drift.Rocks)
                    if (r.active && Mathf.Abs(r.piece.x) < halfW && Mathf.Abs(r.piece.y) < halfH)
                        windowClasses |= r.sizeMul < 0.6f ? 1 : r.sizeMul < 1.0f ? 2 : 4;
            }

            float mean = sumVisible / (float)Mathf.Max(1, samples);
            float bandShare = inBand / (float)Mathf.Max(1, samples);
            Debug.Log("[AST] " + Seconds / 60 + " min: " + lives.Count + " rocks (" + drift.Spawned + " spawns, " +
                      drift.SpawnRejects + " deferred), visible mean " + mean.ToString("F2") + ", max " + maxVisible +
                      ", in 3..6 " + (bandShare * 100f).ToString("F0") + "% of seconds; tiers deep..near " +
                      tiers[0] + "/" + tiers[1] + "/" + tiers[2] + "/" + tiers[3]);
            Check("a steady population: mean visible " + mean.ToString("F2") + " in 2..5, " +
                  (bandShare * 100f).ToString("F0") + "% of seconds in band (>= 70%), never more than " +
                  SpaceDirector.AsteroidSlots + " (max " + maxVisible + ")",
                  mean >= 2f && mean <= 5f && bandShare >= 0.7f && maxVisible <= SpaceDirector.AsteroidSlots);
            Check("rocks come at every depth tier (" + tiers[0] + "/" + tiers[1] + "/" + tiers[2] + "/" +
                  tiers[3] + ")", tiers[0] > 0 && tiers[1] > 0 && tiers[2] > 0 && tiers[3] > 0);

            // motion
            float speedMin = float.MaxValue, speedMax = 0f, spinMin = float.MaxValue, spinMax = 0f;
            int cw = 0, ccw = 0;
            foreach (var l in lives)
            {
                speedMin = Mathf.Min(speedMin, l.speed); speedMax = Mathf.Max(speedMax, l.speed);
                spinMin = Mathf.Min(spinMin, Mathf.Abs(l.spin)); spinMax = Mathf.Max(spinMax, Mathf.Abs(l.spin));
                if (l.spin > 0f) ccw++; else cw++;
            }
            Check("rocks cross both ways: " + ltr + " left-to-right, " + rtl + " right-to-left", ltr > 20 && rtl > 20 && quadrants == 3);
            Check("no rock has any vertical velocity or backdrop-scroll rate, and each moves along its heading (" +
                  verticalBad + " bad)", verticalBad == 0);
            Check("every backdrop rock is wider than the largest hazard rock (" + SpaceAsteroidField.Width[2] + " u; " +
                  tooSmall + " smaller)", tooSmall == 0);
            Check("the rocks are tinted grey and dimmer than the hazard rocks (" + notGrey + " too bright); tint " +
                  SpaceDirector.DriftRockTint, notGrey == 0 && SpaceDirector.DriftRockTint.r < 0.75f &&
                  Mathf.Max(SpaceDirector.DriftRockTint.r, SpaceDirector.DriftRockTint.g, SpaceDirector.DriftRockTint.b) -
                  Mathf.Min(SpaceDirector.DriftRockTint.r, SpaceDirector.DriftRockTint.g, SpaceDirector.DriftRockTint.b) < 0.1f);
            Check("own speeds stay in " + SpaceDirector.AsteroidSpeedMin + ".." + SpaceDirector.AsteroidSpeedMax + " u/s (" +
                  speedMin.ToString("F3") + ".." + speedMax.ToString("F3") + ")",
                  speedMin >= SpaceDirector.AsteroidSpeedMin - 1e-4f && speedMax <= SpaceDirector.AsteroidSpeedMax + 1e-4f &&
                  speedMax - speedMin > 0.15f);
            Check("they tumble both ways at " + SpaceDirector.AsteroidSpinMin + ".." + SpaceDirector.AsteroidSpinMax +
                  " deg/s (" + spinMin.ToString("F1") + ".." + spinMax.ToString("F1") + ", " + cw + " cw / " + ccw + " ccw)",
                  spinMin >= SpaceDirector.AsteroidSpinMin - 1e-3f && spinMax <= SpaceDirector.AsteroidSpinMax + 1e-3f &&
                  cw > lives.Count / 5 && ccw > lives.Count / 5);

            // size
            float mulMin = float.MaxValue, mulMax = 0f;
            int nSmall = 0, nMed = 0, nLarge = 0;
            foreach (var l in lives)
            {
                mulMin = Mathf.Min(mulMin, l.sizeMul); mulMax = Mathf.Max(mulMax, l.sizeMul);
                if (l.sizeMul < 0.6f) nSmall++; else if (l.sizeMul < 1.0f) nMed++; else nLarge++;
            }
            Debug.Log("[AST] sizes x" + mulMin.ToString("F2") + "..x" + mulMax.ToString("F2") + " of " +
                      SpaceDirector.AsteroidBaseSize + " u: small/medium/large " + nSmall + "/" + nMed + "/" + nLarge +
                      "; 10 s windows with all three on screen " + threeClassWindows + "/" + windows);
            Check("sizes spread over the band x" + SpaceDirector.AsteroidSizeMin + "..x" + SpaceDirector.AsteroidSizeMax +
                  " (x" + mulMin.ToString("F2") + "..x" + mulMax.ToString("F2") + "), mostly small/medium (" + nSmall + "/" +
                  nMed + "/" + nLarge + ")",
                  mulMin >= SpaceDirector.AsteroidSizeMin - 1e-4f && mulMax <= SpaceDirector.AsteroidSizeMax + 1e-4f &&
                  mulMin < SpaceDirector.AsteroidSizeMin + 0.15f && mulMax > SpaceDirector.AsteroidSizeMin + 0.4f);
            Check("the biggest rock stays under " + SpaceDirector.AsteroidMaxScreenShare * 100f + "% of the screen width (" +
                  (biggestShare * 100f).ToString("F1") + "%)", biggestShare <= SpaceDirector.AsteroidMaxScreenShare + 1e-3f);
            Check("each rock draws a pre-shrunk copy near 1:1 on a 2532 px phone (" + levelBad + " off; copies used mask " +
                  levelsUsed + ")", levelBad == 0 && levelsUsed > 2);
            Check("bigger rocks drift slower (size/speed r = " + Corr(lives, l => l.sizeMul, l => l.speed).ToString("F2") + " < -0.6)",
                  Corr(lives, l => l.sizeMul, l => l.speed) < -0.6f);
            Check("bigger rocks tumble slower (size/|spin| r = " +
                  Corr(lives, l => l.sizeMul, l => Mathf.Abs(l.spin)).ToString("F2") + " < -0.6)",
                  Corr(lives, l => l.sizeMul, l => Mathf.Abs(l.spin)) < -0.6f);
            Check("bigger rocks smoke bigger, longer and more often (size vs puff width r = " +
                  Corr(lives, l => l.sizeMul, l => l.puffW).ToString("F2") + ", life r = " +
                  Corr(lives, l => l.sizeMul, l => l.puffLife).ToString("F2") + ", interval r = " +
                  Corr(lives, l => l.sizeMul, l => l.emitEvery).ToString("F2") + ")",
                  Corr(lives, l => l.sizeMul, l => l.puffW) > 0.6f && Corr(lives, l => l.sizeMul, l => l.puffLife) > 0.9f &&
                  Corr(lives, l => l.sizeMul, l => l.emitEvery) < -0.9f);
            Check("bigger rocks sit nearer (size/tier r = " + Corr(lives, l => l.sizeMul, l => l.tier).ToString("F2") + " > 0.6)",
                  Corr(lives, l => l.sizeMul, l => l.tier) > 0.6f);

            // smoke
            Check("smoke puffs stream off the rocks (" + drift.PuffsEmitted + " puffs, " + drift.SparksPopped + " sparks)",
                  drift.PuffsEmitted > 200 && drift.SparksPopped > 100);
            Check("smoke stays capped: <= " + SpaceDirector.AsteroidPuffsPer + " per rock (" + perRockOver +
                  " samples over), <= " + SpaceDirector.AsteroidPuffCap + " in all (" + totalOver + " over, most " + maxLive + ")",
                  perRockOver == 0 && totalOver == 0 && maxLive > 4);
            float trail = trailN > 0 ? trailSum / trailN : 0f;
            Check("puffs trail behind their rock's travel (mean offset along the heading " + trail.ToString("F3") + " u < 0)",
                  trailN > 50 && trail < -0.02f);
            Check("puffs are big enough to read on a phone (smallest " + smallestPuffPx.ToString("F1") + " px >= 8)",
                  smallestPuffPx >= 8f);
            Check("rocks, cracks, sparks and smoke sort inside their tier's backdrop layer, below gameplay (" + orderBad + " bad)",
                  orderBad == 0);
            Check("rocks keep clear of stations / planetoids at their own depth, bar the odd frame while one is dodging (" +
                  sameTierOverlaps + "/" + samples + " samples; " + crossTierSamples +
                  " samples passing in front of / behind one at another depth)", sameTierOverlaps <= samples / 16 + 2);

            Blink(wb, drift);
            Pause(wb, drift);
            Alloc(wb, drift);
        }
        finally
        {
            SpaceDirector.EventsDisabled = false;
            Time.timeScale = 1f;
            Object.DestroyImmediate(go);
        }
    }

    static int windowClasses;

    static float Reach(BackdropPiece p)
    {
        return p.size * (p.kind == SpaceDirector.Station ? 0.75f : 0.55f);
    }

    static float Corr(List<Life> lives, System.Func<Life, float> fx, System.Func<Life, float> fy)
    {
        int n = lives.Count;
        if (n < 3) return 0f;
        double mx = 0, my = 0;
        foreach (var l in lives) { mx += fx(l); my += fy(l); }
        mx /= n; my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        foreach (var l in lives)
        {
            double dx = fx(l) - mx, dy = fy(l) - my;
            sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
        }
        return (float)(sxy / System.Math.Sqrt(System.Math.Max(1e-12, sxx * syy)));
    }

    // Cracks blink in three hard steps, rock by rock out of step; only the
    // crack layer changes (the rock's own colour holds).
    static void Blink(WorldBackdrop wb, SpaceAsteroidDrift drift)
    {
        bool stepped = true;
        for (int p = 0; p < 3; p++)
            for (float f = 0f; f < 3f; f += 0.01f)
            {
                float l = SpaceDirector.CrackLevel(p, f, 0.37f);
                stepped &= l == 0f || l == SpaceDirector.CrackMid || l == 1f;
            }
        Check("crack levels are hard steps only (off, " + SpaceDirector.CrackMid + ", full)", stepped);

        // A 4 s window over the rocks in flight; the field thins out now and
        // then (rocks leave mid-window), so up to eight windows are tried until
        // one follows at least three rocks all the way through.
        var seq = new Dictionary<SpaceAsteroidDrift.Rock, string>();
        var levels = new Dictionary<SpaceAsteroidDrift.Rock, HashSet<float>>();
        var colour = new Dictionary<SpaceAsteroidDrift.Rock, Color>();
        bool rockSteady = true;
        for (int window = 0; window < 8 && (window == 0 || seq.Count < 2); window++)
        {
            seq.Clear(); levels.Clear(); colour.Clear();
            foreach (var r in drift.Rocks)
                if (r.active && !SpaceAsteroidDrift.IsStripArt(r.art)) { seq[r] = ""; levels[r] = new HashSet<float>(); colour[r] = r.piece.sr.color; }
            for (int i = 0; i < 120; i++)
            {
                wb.Step(Dt);
                foreach (var r in drift.Rocks)
                {
                    if (!seq.ContainsKey(r)) continue;
                    if (!r.active || r.piece.age < Dt * (i + 1) - 1e-3f) { seq.Remove(r); levels.Remove(r); continue; }   // left / a new life
                    seq[r] += r.crackLevel.ToString("F2") + ",";
                    levels[r].Add(r.crackLevel);
                    rockSteady &= r.piece.sr.color == colour[r];
                }
            }
        }
        int blinking = 0, distinct = 0;
        var seen = new HashSet<string>();
        foreach (var kv in levels) if (kv.Value.Contains(0f) && kv.Value.Contains(1f)) blinking++;
        foreach (var kv in seq) if (seen.Add(kv.Value)) distinct++;
        var patterns = new HashSet<int>();
        foreach (var r in drift.Rocks) if (r.active) patterns.Add(r.pattern);
        Check("the cracks of every rock in view blink fully on and off within 4 s (" + blinking + "/" + levels.Count + ")",
              levels.Count >= 2 && blinking == levels.Count);
        Check("rocks blink out of step (" + distinct + " distinct sequences for " + seq.Count + " rocks, " +
              patterns.Count + " patterns in use)", distinct == seq.Count && seq.Count >= 2);
        Check("only the cracks change: the rock's own colour holds while they blink", rockSteady);
    }

    // ---- the four extra rock types: ice, molten, iron, geode (SpaceRockTypes) ----

    static void RockTypes(WorldBackdrop wb, SpaceAsteroidDrift drift)
    {
        Check("the drift pool has seven art kinds (3 cracked + 4 types)", SpaceAsteroidDrift.ArtKinds == 7 && SpaceRockTypes.Count == 4);
        foreach (var t in SpaceRockTypes.All)
        {
            var f = t.Frames;
            Check(t.key + ": its blink strip loads " + SpaceRockTypes.BlinkFrames + " frames, shared",
                  f != null && f.Length == SpaceRockTypes.BlinkFrames && System.Array.TrueForAll(f, x => x != null) && t.Frames == f);
        }
        Check("the blink rate is 6-8 fps", SpaceRockTypes.BlinkFps >= 6f && SpaceRockTypes.BlinkFps <= 8f);
        // 120 s of sky: every art kind turns up, the strip rocks advance frames, and stay decorative
        var arts = new HashSet<int>();
        var frames = new Dictionary<SpaceAsteroidDrift.Rock, HashSet<int>>();
        var phases = new HashSet<int>();
        bool decorative = true, grey = true;
        for (int i = 0; i < 120 * 30; i++)
        {
            wb.Step(Dt);
            foreach (var r in drift.Rocks)
            {
                if (!r.active) continue;
                arts.Add(r.art);
                if (!SpaceAsteroidDrift.IsStripArt(r.art)) continue;
                HashSet<int> set;
                if (!frames.TryGetValue(r, out set)) { set = new HashSet<int>(); frames[r] = set; }
                set.Add(r.frame);
                if (r.piece.age < Dt * 2f) phases.Add(r.frame);
                var root = r.piece.root;
                decorative &= root.GetComponentsInChildren<Collider2D>(true).Length == 0 && root.GetComponentsInChildren<ClearTarget>(true).Length == 0 &&
                              !root.CompareTag("Astr") && !r.piece.sr.gameObject.CompareTag("Astr") && !r.crack.enabled;
                grey &= r.piece.sr.color.r <= 0.8f && r.piece.sr.sprite == r.strip[r.frame];
            }
        }
        Check("all seven art kinds drift through the sky (" + arts.Count + ")", arts.Count == SpaceAsteroidDrift.ArtKinds);
        bool advances = frames.Count > 0;
        foreach (var kv in frames) advances &= kv.Value.Count >= 2;
        Check("a strip rock's blink advances through its frames (" + frames.Count + " rocks followed)", advances);
        Check("strip rocks start at different blink phases", phases.Count > 1);
        Check("strip rocks are decorative (no collider, no ClearTarget, no Astr tag)", decorative);
        Check("strip rocks go through the same grey drift tint and show their current frame", grey);
    }

    static void Pause(WorldBackdrop wb, SpaceAsteroidDrift drift)
    {
        // wait for live puffs and a spark
        for (int i = 0; i < 30 * 30 && !(drift.LivePuffs >= 3 && AnySpark(drift)); i++) wb.Step(Dt);
        Time.timeScale = 0f;
        var before = Snap(drift);
        for (int i = 0; i < 90; i++) wb.Step(Dt);
        var after = Snap(drift);
        bool same = before.Count == after.Count;
        for (int i = 0; same && i < before.Count; i++) same = before[i] == after[i];
        Check("while paused, drift, tumble, crack blink, sparks and smoke all freeze (" + before.Count + " values, " +
              drift.LivePuffs + " live puffs)", same && drift.LivePuffs > 0);
        Time.timeScale = 1f;
        wb.Step(Dt);
        var moved = Snap(drift);
        bool changed = moved.Count != after.Count;
        for (int i = 0; !changed && i < after.Count; i++) changed = moved[i] != after[i];
        Check("... and move on when time resumes", changed);
    }

    static bool AnySpark(SpaceAsteroidDrift drift)
    {
        foreach (var r in drift.Rocks) if (r.active && (r.sparks[0].enabled || r.sparks[1].enabled)) return true;
        return false;
    }

    static List<Vector4> Snap(SpaceAsteroidDrift drift)
    {
        var list = new List<Vector4>();
        foreach (var r in drift.Rocks)
        {
            if (!r.active) continue;
            var t = r.piece.root;
            list.Add(new Vector4(t.localPosition.x, t.localPosition.y, t.localEulerAngles.z, r.crack.color.a));
            foreach (var s in r.sparks)
                list.Add(new Vector4(s.enabled ? 1 : 0, s.color.a, s.transform.localScale.x, r.sparkAge));
        }
        for (int i = 0; i < drift.Puffs.Length; i++)
        {
            if (drift.PuffOwner[i] < 0) continue;
            var t = drift.Puffs[i].transform;
            list.Add(new Vector4(t.localPosition.x, t.localPosition.y, t.localScale.x, drift.Puffs[i].color.a));
        }
        return list;
    }

    // The field alone (its Tick: drift, steering, spawns, blink, sparks,
    // puffs) must allocate nothing; the whole backdrop's figure is logged
    // for reference (the rest of the director allocates a little when a
    // station / planet spawns -- Sprite.name strings -- which predates this).
    static void Alloc(WorldBackdrop wb, SpaceAsteroidDrift drift)
    {
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        for (int i = 0; i < 60; i++) wb.Step(Dt);
        float v = WorldBackdrop.ScrollVelocity(0.3f);
        int spawned = drift.Spawned, puffs = drift.PuffsEmitted;
        SpaceDirector.EventsDisabled = true;       // (the burst effects are pooled elsewhere: this meters the field's own frame)
        long used = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 120 * 30; i++) drift.Tick(Dt, v * (0.5f + (i % 900) / 900f));
        });
        Check("the asteroid field allocates nothing over 2 minutes of frames (" + (drift.Spawned - spawned) + " spawns, " +
              (drift.PuffsEmitted - puffs) + " puffs): " + used + " bytes (meter control " + control + " bytes)",
              meter && used == 0 && drift.Spawned > spawned && drift.PuffsEmitted > puffs);
        long whole = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 120 * 30; i++)
            {
                moveBackGround.speed = Mathf.Repeat(i * 0.0004f, 0.62f);
                wb.Step(Dt);
            }
        });
        Debug.Log("[AST] whole Space backdrop over the same 2 minutes: " + whole + " bytes");
    }

    // ---- decorative events: rock vs rock, rock vs planet ---------------------------------

    static bool TwoRocks(WorldBackdrop wb, SpaceAsteroidDrift drift, out SpaceAsteroidDrift.Rock a, out SpaceAsteroidDrift.Rock b)
    {
        a = b = null;
        for (int i = 0; i < 120 * 30; i++)
        {
            var found = new List<SpaceAsteroidDrift.Rock>();
            foreach (var r in drift.Rocks) if (r.active) found.Add(r);
            if (found.Count >= 3) { a = found[0]; b = found[1]; return true; }
            wb.Step(Dt);
        }
        return false;
    }

    static void Events()
    {
        Time.timeScale = 1f;
        moveBackGround.speed = 0.2f;
        SpaceDirector.AsteroidSeedOverride = -1;
        SpaceDirector.EventsDisabled = false;
        SpaceDirector.RunOnOverride = true;       // (the events need a run on)
        var go = new GameObject("~AsteroidEventTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            wb.Show("Space", false);
            var sd = wb.Current.Director as SpaceDirector;
            var drift = sd != null ? sd.AsteroidDrift : null;
            if (drift == null || !drift.Ready) { Check("events: the drift field is ready", false); return; }
            for (int i = 0; i < 3 * 30; i++) wb.Step(Dt);

            // rock vs rock, unalike: the smaller bursts with debris, the bigger one is shaken and survives
            SpaceAsteroidDrift.Rock a, b;
            Check("events: three rocks are in flight", TwoRocks(wb, drift, out a, out b));
            if (a == null) return;
            a.sizeT = 0.1f; b.sizeT = 0.9f;
            b.maxHealth = 3; b.health = 3f;
            int active = drift.ActiveCount, puffs = drift.PuffsEmitted, hits = drift.RockCollisions;
            drift.Collide(a, b);
            Check("a collision of unalike rocks: the small one bursts, frees its slot, leaves smoke debris; the big one is shaken",
                  drift.RockCollisions == hits + 1 && !a.active && b.active && b.health < 3f && b.flash > 0f &&
                  drift.ActiveCount == active - 1 && drift.PuffsEmitted > puffs);

            // alike: both burst
            SpaceAsteroidDrift.Rock c, d;
            Check("events: two more rocks arrive", TwoRocks(wb, drift, out c, out d));
            if (c == null) return;
            c.sizeT = 0.5f; d.sizeT = 0.55f;
            drift.Collide(c, d);
            Check("a collision of alike rocks bursts both", !c.active && !d.active);
            // the freed slots are reused cleanly
            bool reused = false;
            for (int i = 0; i < 90 * 30 && !reused; i++) { wb.Step(Dt); reused = c.active || d.active; }
            Check("the slots of burst rocks are reused", reused);

            // rock vs planet: impact on the surface, plume, a crater mark that fades, the rock is consumed
            SpaceAsteroidDrift.Rock e, f;
            Check("events: rocks to fire at a planet", TwoRocks(wb, drift, out e, out f));
            if (e == null) return;
            var planet = sd.Bodies[0].items[0];
            bool wasActive = planet.active;
            planet.active = true;
            planet.x = e.piece.x + 1.5f; planet.y = e.piece.y + 0.2f; planet.size = 2.4f;
            int impacts = drift.PlanetImpacts;
            puffs = drift.PuffsEmitted;
            drift.Impact(e, planet);
            Check("a planet impact: the rock is consumed, a plume rises and a crater mark glows on the planet",
                  drift.PlanetImpacts == impacts + 1 && !e.active && drift.PuffsEmitted > puffs && drift.LiveMarks == 1);
            int m = System.Array.FindIndex(drift.MarkPlanet, p => p == planet);
            drift.Tick(Dt, 0f);
            Vector3 mp = m >= 0 ? drift.Marks[m].transform.localPosition : Vector3.zero;
            float onSurface = Mathf.Abs(Vector2.Distance(new Vector2(mp.x, mp.y), new Vector2(planet.x, planet.y)) -
                                        planet.size * SpaceDirector.PlanetRadiusShare);
            Check("the mark sits on the planet's surface at the contact point (" + onSurface.ToString("F3") + " u off)",
                  m >= 0 && drift.Marks[m].enabled && onSurface < 0.05f && drift.Marks[m].sortingOrder > planet.sr.sortingOrder);
            float ageBefore = drift.MarkAge[m];
            drift.Tick(0f, 0f);
            Check("a paused backdrop freezes the mark", drift.MarkAge[m] == ageBefore);
            planet.x += 0.5f;
            drift.Tick(Dt, 0f);
            Check("the mark rides with its planet", Mathf.Abs(drift.Marks[m].transform.localPosition.x - (mp.x + 0.5f)) < 0.05f);
            for (int i = 0; i < (int)(SpaceDirector.MarkLife / Dt) + 5; i++) drift.Tick(Dt, 0f);
            Check("the crater mark fades out and frees its slot", drift.LiveMarks == 0 && !drift.Marks[m].enabled);
            planet.active = wasActive;

            // a fated rock really does fly into its target (scripted head-on course)
            for (int i = 0; i < 30; i++) wb.Step(Dt);
            SpaceAsteroidDrift.Rock g, h;
            Check("events: rocks for a head-on course", TwoRocks(wb, drift, out g, out h));
            if (g == null) return;
            hits = drift.RockCollisions;
            g.baseX = -1f; g.baseY = 0f; g.heading = 0f; g.piece.vx = 0.3f; g.speed = 0.3f;
            h.baseX = 1f; h.baseY = 0.5f; h.heading = 180f; h.piece.vx = -0.3f; h.speed = 0.3f;
            h.piece.tier = g.piece.tier;
            g.fate = SpaceAsteroidDrift.FateRock; g.aimRock = h; g.aimId = h.id;
            for (int i = 0; i < 12 * 30 && drift.RockCollisions == hits; i++) drift.Tick(Dt, 0f);
            Check("a fated rock eases onto its target's line and collides (" + (drift.RockCollisions - hits) + " collision)",
                  drift.RockCollisions == hits + 1);

            // naturally, over ten minutes: events happen, rocks stay horizontal, bookkeeping holds
            int fates = drift.RockFates + drift.PlanetFates, coll = drift.RockCollisions, imp = drift.PlanetImpacts;
            bool horizontal = true, counts = true;
            for (int i = 0; i < 600 * 30; i++)
            {
                moveBackGround.speed = Mathf.Repeat(i * 0.0004f, 0.6f);
                wb.Step(Dt);
                if (i % 30 != 0) continue;
                foreach (var r in drift.Rocks)
                {
                    if (!r.active) continue;
                    horizontal &= r.piece.vy == 0f && r.piece.rate == 0f;
                    int owned = 0;
                    for (int k = 0; k < drift.PuffOwner.Length; k++) if (drift.PuffOwner[k] == drift.Rocks.IndexOf(r)) owned++;
                    counts &= r.livePuffs == owned;
                }
                counts &= drift.LiveMarks <= SpaceDirector.MarkSlots;
            }
            Debug.Log("[AST] ten minutes: " + (drift.RockFates + drift.PlanetFates - fates) + " fated, " +
                      (drift.RockCollisions - coll) + " rock collisions, " + (drift.PlanetImpacts - imp) + " planet impacts");
            Check("some rocks are fated to collide or hit a planet, and the events happen but stay occasional",
                  drift.RockFates + drift.PlanetFates > fates + 5 &&
                  drift.RockCollisions + drift.PlanetImpacts > coll + imp);
            Check("through the events the rocks stay purely horizontal and the smoke bookkeeping holds", horizontal && counts);
        }
        finally
        {
            SpaceDirector.RunOnOverride = false;
            Time.timeScale = 1f;
            Object.DestroyImmediate(go);
        }
    }

    // ---- seeded --------------------------------------------------------------------------

    static void Determinism()
    {
        string a = Trace(1234), b = Trace(1234), c = Trace(99);
        Check("a seeded field repeats exactly, a different seed differs", a == b && a != c && a.Length > 0);
    }

    static string Trace(int seed)
    {
        SpaceDirector.AsteroidSeedOverride = seed;
        moveBackGround.speed = 0.2f;
        Time.timeScale = 1f;
        var go = new GameObject("~AsteroidDriftSeed");
        var wb = go.AddComponent<WorldBackdrop>();
        var sb = new System.Text.StringBuilder();
        try
        {
            wb.Show("Space", false);
            var drift = (wb.Current.Director as SpaceDirector).AsteroidDrift;
            for (int i = 0; i < 60 * 30; i++) wb.Step(Dt);
            foreach (var r in drift.Rocks)
                if (r.active) sb.Append(r.heading.ToString("F2")).Append('/').Append(r.sizeMul.ToString("F3")).Append(';');
        }
        finally
        {
            Object.DestroyImmediate(go);
            SpaceDirector.AsteroidSeedOverride = -1;
        }
        return sb.ToString();
    }
}
