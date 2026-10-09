using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Feature: "The bosses should have 5 spinning hearts around them", and
// "make the enemy elite hearts more clear: give them an outline glow".
//
// Every boss wears BossConfig.Hearts (5) hearts spinning round it
// (BossHearts, on the shared HeartOrbit engine): they are its health -- five
// equal shares of the same hit points (HeartWeight each), so the fight is as
// long as before -- and the attack phases follow them. The ring clears the
// body's drawn silhouette, stays inside the rails and under the HUD band on
// every screen shape, re-spaces evenly after each loss, goes with the boss,
// has no colliders, freezes with the world and allocates nothing. The elite
// and boss hearts wear HeartOutline's thin two-tone trace, which reads over
// every world (and over the elite's own art) and is no round halo.
//
// Drives BossEncounter frame by frame (Step(realDt, timeScale)) in edit mode.
public static class BossHeartsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSSHEARTS] PASS  " : "[BOSSHEARTS] FAIL  ") + what);
        if (!ok) fails++;
    }
    static void Info(string what) { Debug.Log("[BOSSHEARTS] INFO  " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EveryBossSpawnsWithFiveHearts();
            HeartsArriveAfterTheEntry();
            HeartsTrackHealth();
            PhasesFollowTheHearts();
            SilhouetteMeasuredFromTheArt();
            RingFitsEveryScreen();
            EvenSpacingAfterEachLoss();
            HeartsVanishWithTheBoss();
            NoCollidersNoAllocationPauseFreezes();
            OutlineIsCrispNotAHalo();
            if (TestHarness.Slow("heart outline contrast renders")) OutlineReadsOverEveryWorld();
            CodexAndDocsMentionTheHearts();
        }
        finally
        {
            ClearScreen();
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            Time.timeScale = 1f;
        }
        Debug.Log("[BOSSHEARTS] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    const float Dt = 1f / 60f;
    static Camera cam;
    static IDisposable screen;

    static void FreshScene(int world = 0)
    {
        ClearScreen();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
    }

    static void ClearScreen()
    {
        if (screen != null) { screen.Dispose(); screen = null; }
        PlayField.Reset();
    }

    // The scene as device `d` shows it: camera fitted, screen and safe area.
    static void UseDevice(FitDevice d)
    {
        ClearScreen();
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        screen = ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.Reset();
    }

    static BossEncounter StartFight(int world = 0)
    {
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        return e;
    }

    static float HueGap(Color a, Color b)
    {
        float ha, hb, s, v;
        Color.RGBToHSV(a, out ha, out s, out v);
        Color.RGBToHSV(b, out hb, out s, out v);
        float d = Mathf.Abs(ha - hb) * 360f;
        return Mathf.Min(d, 360f - d);
    }

    static readonly Color PlayerRed = new Color(254f / 255f, 62f / 255f, 78f / 255f);

    // Phases of the shown hearts round the ring, sorted; the worst gap's
    // distance from an even share, in degrees.
    static float SpacingError(BossHearts h)
    {
        var th = new List<float>();
        for (int i = 0; i < h.Hearts.Length; i++)
            if (h.Hearts[i] != null && h.Hearts[i].gameObject.activeSelf) th.Add(Mathf.Repeat(h.Theta(i), 2f * Mathf.PI));
        if (th.Count < 2) return 0f;
        th.Sort();
        float even = 360f / th.Count, worst = 0f;
        for (int i = 0; i < th.Count; i++)
        {
            float next = i + 1 < th.Count ? th[i + 1] : th[0] + 2f * Mathf.PI;
            worst = Mathf.Max(worst, Mathf.Abs((next - th[i]) * Mathf.Rad2Deg - even));
        }
        return worst;
    }

    // ---- tests ---------------------------------------------------------

    static void EveryBossSpawnsWithFiveHearts()
    {
        Check("a boss has 5 hearts (BossConfig.Hearts), splitting its " + BossConfig.HitPoints + " hit points (" +
              BossConfig.HeartWeight.ToString("F2") + " hits a heart)",
              BossConfig.Hearts == 5 && Mathf.Abs(BossConfig.HeartWeight * BossConfig.Hearts - BossConfig.HitPoints) < 1e-4f);
        for (int w = 0; w < BossCatalog.All.Length; w++)
        {
            FreshScene(w);
            var e = StartFight(w);
            var boss = e.Boss;
            var h = e.Actor != null ? e.Actor.Hearts : null;
            Check(boss.artKey + ": fights with exactly 5 hearts round it", e.State == BossEncounter.Phase.Fight && h != null &&
                  h.Hearts.Length == 5 && h.ShownCount == 5 && e.HeartsLeft == 5 && h is HeartOrbit);
            if (h == null) continue;
            var r = h.Hearts[0].GetComponent<SpriteRenderer>();
            Check(boss.artKey + ": the elite heart drawing in its own colour, never the player's red (hue gap " +
                  HueGap(boss.heartColor, PlayerRed).ToString("F0") + " deg)",
                  r.sprite != null && r.sprite.name.Contains("eliteHeart") && HueGap(boss.heartColor, PlayerRed) > 30f &&
                  HueGap(r.color, PlayerRed) > 30f);
            bool sorted = true;
            foreach (var t in h.Hearts) sorted &= t.GetComponent<SpriteRenderer>().sortingOrder == BossConfig.HeartSortingOrder;
            Check(boss.artKey + ": hearts draw over the boss (-3), its charges (29) and shots (30-32): order " + BossConfig.HeartSortingOrder,
                  sorted && BossConfig.HeartSortingOrder > 32);
            float size = h.heartSize;
            Check(boss.artKey + ": boss-sized hearts, the player's 0.22 x 1.3 (" + size.ToString("F3") + ")",
                  Mathf.Abs(size - .22f * 1.3f) < .01f);
            float rev = 2f * Mathf.PI / h.OrbitParams.speed;
            Check(boss.artKey + ": one revolution every " + rev.ToString("F1") + " s (4-6 s), flat, no warp or flourish",
                  rev >= 4f && rev <= 6f && h.OrbitParams.tilt == 0f && h.OrbitParams.flourishEvery == 0f);
            Check(boss.artKey + ": every heart wears its outline", h.HasOutlines && h.HeartOutlineRenderer(4) != null);
        }
    }

    static void HeartsArriveAfterTheEntry()
    {
        FreshScene(0);
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        bool none = true;
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++)
        {
            none &= e.Actor == null || e.Actor.Hearts == null;
            e.Step(.05f, 1f);
        }
        Check("no hearts while the boss warps in (its intro)", none);
        var h = e.Actor.Hearts;
        Check("... they come with the fight", h != null && h.ShownCount == 5);
        float start = h.Hearts[0].localScale.x;
        e.Step(.02f, 1f);
        float early = h.Hearts[0].localScale.x;
        for (int i = 0; i < 30; i++) e.Step(Dt, 1f);
        float settled = h.Hearts[0].localScale.x;
        Check("... popping in (scale " + early.ToString("F3") + " -> " + settled.ToString("F3") + " within half a second)",
              early < settled * .8f && start <= early + 1e-4f && settled > 0f);
    }

    static void HeartsTrackHealth()
    {
        // one heart's share at a time: a heart each
        FreshScene(1);
        var e = StartFight(1);
        var h = e.Actor.Hearts;
        bool track = true;
        string seen = "";
        for (int k = 1; k <= 5; k++)
        {
            e.OnShipAttackHit(BossConfig.HeartWeight);
            if (k < 5) e.Step(Dt, 1f);
            seen += e.HeartsLeft + (k < 5 ? "," : "");
            track &= e.HeartsLeft == 5 - k && h.Left == 5 - k;
        }
        Check("each heart's share of damage (" + BossConfig.HeartWeight.ToString("F2") + " hit) takes one heart: " + seen, track);
        Check("... and the fifth heart lost destroys it", e.Destroyed && e.HitPointsLeft == 0);
        e.Step(Dt, 1f);
        Check("... which then explodes (DESTROYED)", e.State == BossEncounter.Phase.Outro && e.Actor.State == BossActor.Mode.Dying);

        // full-weight hits: the same three as before end it
        FreshScene(2);
        e = StartFight(2);
        h = e.Actor.Hearts;
        e.Step(Dt, 1f);
        e.OnUltimateHit();
        int a = e.HeartsLeft;
        e.Step(Dt, 1f);
        int shownA = h.ShownCount;
        e.OnUltimateHit();
        int b = e.HeartsLeft;
        e.Step(Dt, 1f);
        int shownB = h.ShownCount;
        bool alive = !e.Destroyed;
        e.OnUltimateHit();
        Check("three full hits, as before: hearts 5 -> " + a + " -> " + b + " -> " + e.HeartsLeft + ", destroyed on the third",
              a == 4 && b == 2 && e.HeartsLeft == 0 && alive && e.Destroyed);
        Check("the ring shows what is left (" + shownA + ", " + shownB + ")", shownA == 4 && shownB == 2);

        // small weighted contacts bank up to a heart, never past the hit points
        FreshScene(3);
        e = StartFight(3);
        for (int i = 0; i < 2; i++) e.OnShipAttackHit(.25f);
        Check("two 0.25 contacts (0.5) take no heart yet", e.HeartsLeft == 5);
        e.OnShipAttackHit(.1f);
        Check("... 0.6 takes the first", e.HeartsLeft == 4);
        bool consistent = true;
        for (float d = 0f; d < 3.2f; d += .01f)
        {
            int hp = BossConfig.HitPoints - Mathf.Min(BossConfig.HitPoints, Mathf.FloorToInt(d + 1e-4f));
            int n = BossEncounter.HeartsFor(d, hp);
            consistent &= (hp <= 0) == (n == 0) && n >= 0 && n <= 5;
        }
        Check("the last heart goes exactly with the last hit point, at any damage", consistent);
        Check("a hit points' edge case: 2.99 hits leaves one heart and one hit point",
              BossEncounter.HeartsFor(2.99f, 1) == 1);
    }

    static void PhasesFollowTheHearts()
    {
        FreshScene(0);
        var e = StartFight(0);
        var boss = e.Boss;
        int[] phase = new int[6], unlocked = new int[6];
        phase[0] = e.FightPhase;
        unlocked[0] = BossCatalog.UnlockedAttacks(boss, e.PhaseProgress01);
        for (int k = 1; k <= 4; k++)
        {
            e.OnShipAttackHit(BossConfig.HeartWeight);
            phase[k] = e.FightPhase;
            unlocked[k] = BossCatalog.UnlockedAttacks(boss, e.PhaseProgress01);
        }
        string s = "";
        for (int k = 0; k <= 4; k++) s += (5 - k) + "h:p" + phase[k] + " ";
        Check("phases by the hearts, at the clock's thirds: 5-4 hearts phase 1, 3-2 phase 2, 1 phase 3 (" + s.Trim() + ")",
              phase[0] == 1 && phase[1] == 1 && phase[2] == 2 && phase[3] == 2 && phase[4] == 3);
        Check("... unlocking one attack, then two, then all three",
              unlocked[0] == 1 && unlocked[1] == 1 && unlocked[2] == 2 && unlocked[3] == 2 && unlocked[4] == 3);
        Check("... with the final phase's faster cooldowns on the last heart", BossCatalog.FinalPhase(e.PhaseProgress01));
        Check("the fight steps the boss on the phase progress (the further on of clock and hearts)",
              File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("actor.StepFight(dt, realDt, PlayerPosition(), PhaseProgress01, pool)"));

        // the clock still escalates an untouched boss
        FreshScene(0);
        e = StartFight(0);
        typeof(BossEncounter).GetField("remaining", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(e, BossConfig.FightSeconds * .3f);
        Check("an untouched boss still reaches phase 3 by the clock (5 hearts, phase " + e.FightPhase + ")",
              e.HeartsLeft == 5 && e.FightPhase == 3);
    }

    // Body(): the ellipse round each boss's idle silhouette, re-measured
    // from the source atlas.
    static void SilhouetteMeasuredFromTheArt()
    {
        foreach (var boss in BossCatalog.All)
        {
            var tex = new Texture2D(2, 2);
            bool loaded = tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Bosses/" + boss.artKey + ".png"));
            if (!loaded) { Check(boss.artKey + ": atlas readable", false); continue; }
            int cell = tex.width / BossArt.BodyColumns;
            int rows = tex.height / cell;
            var px = tex.GetPixels32();
            Rect body = BossHearts.Body(boss);
            float ax = body.width * .5f, ay = body.height * .5f;
            float vh = BossHearts.VisibleHalf(BossConfig.HeartSize);
            // the ring's own radii (unsquashed)
            float rx = ax + BossConfig.HeartClearance + vh, ry = ay + BossConfig.HeartClearance + vh;
            float worst = 0f, worstRing = 0f, s = BossConfig.BossWorldSize / cell;
            int first = BossArt.HasExpandedCombat(boss) ? BossArt.SpaceIdle0 : BossArt.Idle0;
            int count = BossArt.HasExpandedCombat(boss) ? BossArt.SpaceIdleFrames : BossArt.IdleFrames;
            for (int f = first; f < first + count; f++)
            {
                int col = f % BossArt.BodyColumns, row = f / BossArt.BodyColumns;
                int y0 = (rows - 1 - row) * cell, x0 = col * cell;   // texture rows run bottom up
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        if (px[(y0 + y) * tex.width + x0 + x].a < 128) continue;
                        float wx = (x + .5f - cell * .5f) * s - body.center.x;
                        float wy = (y + .5f - cell * .5f) * s - body.center.y;
                        worst = Mathf.Max(worst, (wx / ax) * (wx / ax) + (wy / ay) * (wy / ay));
                        // the heart's near edge on the ring, against this pixel
                        float ex = rx - vh - BossConfig.HeartClearance * .5f, ey = ry - vh - BossConfig.HeartClearance * .5f;
                        worstRing = Mathf.Max(worstRing, (wx / ex) * (wx / ex) + (wy / ey) * (wy / ey));
                    }
            }
            Object.DestroyImmediate(tex);
            Check(boss.artKey + ": its silhouette ellipse (" + ax.ToString("F2") + " x " + ay.ToString("F2") +
                  ") holds every opaque idle pixel (worst " + Mathf.Sqrt(worst).ToString("F3") + " of its radius)",
                  worst <= 1.0001f && worst > .9f);
            Check(boss.artKey + ": the ring (" + rx.ToString("F2") + " x " + ry.ToString("F2") + ") clears the drawn body by more than half the clearance",
                  worstRing < 1f);
        }
    }

    static readonly string[] Screens = { "and-1080x1920", "and-1080x2340-notch", "flip7-1080x2520", "ipad-9" };
    static readonly string[] ScreenNames = { "9:16", "9:19.5", "9:21", "3:4" };

    static void RingFitsEveryScreen()
    {
        for (int sIdx = 0; sIdx < Screens.Length; sIdx++)
        {
            var d = FitDevice.Find(Screens[sIdx]);
            if (d == null) { Check("device " + Screens[sIdx] + " exists", false); continue; }
            foreach (var boss in BossCatalog.All)
            {
                FreshScene(0);
                UseDevice(d);
                var actor = BossActor.Spawn(boss);
                actor.BeginFight();
                var h = actor.Hearts;
                Rect body = BossHearts.Body(boss);
                float vh = BossHearts.VisibleHalf(h.heartSize);
                float edge = Mathf.Min(BossRails.InnerEdge, BossRails.DrawnInnerEdge);
                float band = PlayField.Live.bandBottom;
                Vector3 rest = actor.transform.position;
                bool inside = true, clear = true;
                float minShare = float.MaxValue, maxX = 0f, maxY = float.MinValue;
                for (int f = 0; f < 120; f++)   // 6 s: more than a revolution
                {
                    h.Step(.05f, .05f);
                    for (int i = 0; i < h.Hearts.Length; i++)
                    {
                        Vector3 p = h.Hearts[i].position;
                        inside &= Mathf.Abs(p.x) + vh <= edge + 1e-3f && p.y + vh <= band + 1e-3f;
                        float dx = p.x - (rest.x + body.center.x), dy = p.y - (rest.y + body.center.y);
                        float ax = body.width * .5f + vh, ay = body.height * .5f + vh;
                        float n = (dx / ax) * (dx / ax) + (dy / ay) * (dy / ay);
                        clear &= n >= .999f;
                        float share = Mathf.Sqrt(n);
                        minShare = Mathf.Min(minShare, share);
                        maxX = Mathf.Max(maxX, Mathf.Abs(p.x));
                        maxY = Mathf.Max(maxY, p.y);
                    }
                }
                Info(ScreenNames[sIdx] + " " + boss.artKey + ": ring radii " + h.OrbitRadii.x.ToString("F2") + " x " + h.OrbitRadii.y.ToString("F2") +
                     " round (" + (rest.x + body.center.x).ToString("F2") + ", " + (rest.y + body.center.y).ToString("F2") + "); hearts reach |x| " +
                     maxX.ToString("F2") + " (rail " + edge.ToString("F2") + "), top y " + maxY.ToString("F2") + " (band " + band.ToString("F2") +
                     "), closest to the silhouette " + minShare.ToString("F2") + " of its ellipse+heart");
                Check(ScreenNames[sIdx] + " " + boss.artKey + ": at rest every heart stays inside the rails and under the HUD band", inside);
                Check(ScreenNames[sIdx] + " " + boss.artKey + ": at rest the ring clears the body's silhouette", clear);

                // swayed to either side: never under a rail (the ring flattens against it)
                bool railOk = true;
                foreach (float side in new[] { -1f, 1f })
                {
                    actor.transform.position = rest + new Vector3(side * boss.swayX, boss.swayY, 0f);
                    for (int f = 0; f < 120; f++)
                    {
                        h.Step(.05f, .05f);
                        for (int i = 0; i < h.Hearts.Length; i++)
                        {
                            Vector3 p = h.Hearts[i].position;
                            railOk &= Mathf.Abs(p.x) + vh <= edge + 1e-3f && p.y + vh <= band + 1e-3f;
                        }
                    }
                }
                Check(ScreenNames[sIdx] + " " + boss.artKey + ": swayed to a rail and up, no heart goes under the rail or the band", railOk);
                Object.DestroyImmediate(actor.gameObject);
            }
        }
        ClearScreen();
    }

    static void EvenSpacingAfterEachLoss()
    {
        FreshScene(0);
        var e = StartFight(0);
        var h = e.Actor.Hearts;
        for (int i = 0; i < 30; i++) e.Step(Dt, 1f);
        Check("five hearts start evenly spaced (worst gap off by " + SpacingError(h).ToString("F1") + " deg)", SpacingError(h) < 2f);
        for (int k = 1; k <= 4; k++)
        {
            e.OnShipAttackHit(BossConfig.HeartWeight, e.Actor.transform.position + new Vector3(k % 2 == 0 ? 2f : -2f, -1f, 0f));
            float at30 = 0f;
            for (int i = 0; i < 60; i++)
            {
                e.Step(Dt, 1f);
                if (i == 17) at30 = SpacingError(h);
            }
            float settled = SpacingError(h);
            Check((5 - k) + " hearts: even again after a loss (" + at30.ToString("F1") + " deg off at 0.3 s, " +
                  settled.ToString("F1") + " at 1 s)", settled < 3f && at30 < 25f && h.ShownCount == 5 - k);
        }
    }

    static void HeartsVanishWithTheBoss()
    {
        // destroyed: the last heart crumbles, then everything goes
        FreshScene(0);
        var e = StartFight(0);
        var h = e.Actor.Hearts;
        for (int i = 0; i < 3; i++) e.OnUltimateHit();
        Check("the last hit: no heart left in the ring, its crumble playing", h.ShownCount == 0 && h.ActiveBreaks > 0);
        e.Step(Dt, 1f);
        int guard = 0;
        while (e.State == BossEncounter.Phase.Outro && e.Actor != null && e.Actor.State != BossActor.Mode.Gone && guard++ < 400) e.Step(Dt, 1f);
        Check("when the wreck is gone, so are the hearts", e.Actor == null || h == null || !h.gameObject.activeInHierarchy);
        while (e.State == BossEncounter.Phase.Outro && guard++ < 800) e.Step(Dt, 1f);
        Check("... and with the encounter over nothing of them is left", h == null && e.State == BossEncounter.Phase.Done);

        // survived: it retreats, the hearts go at once
        FreshScene(1);
        e = StartFight(1);
        h = e.Actor.Hearts;
        e.OnUltimateHit();
        typeof(BossEncounter).GetField("remaining", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(e, .01f);
        e.Step(Dt, 1f);
        Check("the clock runs out: it retreats and its hearts are gone at once",
              e.Actor.State == BossActor.Mode.Retreating && !h.gameObject.activeInHierarchy);

        // the player dies: the fight freezes as it is (the death panel takes over)
        FreshScene(2);
        e = StartFight(2);
        h = e.Actor.Hearts;
        buttonClicks.playerDied = true;
        e.Step(Dt, 1f);
        Check("the player dies: the encounter aborts, no heart is lost", e.State == BossEncounter.Phase.Aborted && h.ShownCount == 5);
        buttonClicks.playerDied = false;
    }

    static object sink;

    static void NoCollidersNoAllocationPauseFreezes()
    {
        FreshScene(3);
        var e = StartFight(3);
        var h = e.Actor.Hearts;
        for (int i = 0; i < 20; i++) e.Step(Dt, 1f);
        Check("hearts are drawings only: no collider, no rigidbody",
              h.GetComponentsInChildren<Collider2D>(true).Length == 0 && h.GetComponentsInChildren<Collider>(true).Length == 0 &&
              h.GetComponentsInChildren<Rigidbody2D>(true).Length == 0);
        Check("... and the boss's hitbox is still its own body box", e.Actor.BodyHitbox != null &&
              e.Actor.BodyHitbox.transform.parent == e.Actor.transform);

        // pause: the world frozen (timeScale 0) freezes the spin
        var before = new Vector3[h.Hearts.Length];
        for (int i = 0; i < before.Length; i++) before[i] = h.Hearts[i].position;
        for (int i = 0; i < 60; i++) e.Step(Dt, 0f);
        bool still = true;
        for (int i = 0; i < before.Length; i++) still &= (h.Hearts[i].position - before[i]).sqrMagnitude < 1e-10f;
        Check("pausing (time scale 0) freezes the ring", still);
        e.Step(Dt, 1f);
        e.Step(Dt, 1f);
        bool moved = false;
        for (int i = 0; i < before.Length; i++) moved |= (h.Hearts[i].position - before[i]).sqrMagnitude > 1e-8f;
        Check("... and it turns on when the world does", moved);

        // allocation: the ring's step, with a crumble running
        e.OnShipAttackHit(BossConfig.HeartWeight);
        Action work = () => { for (int i = 0; i < 120; i++) h.Step(Dt, Dt); };
        work();
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long bytes = TestHarness.AllocatedBytes(work);
        Check("the allocation meter sees a deliberate allocation (" + control + " bytes)", meter);
        Check("120 steps of the boss's hearts (a crumble playing) allocate nothing (" + bytes + " bytes)", meter && bytes == 0);
        long seeded = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 30; i++) { h.Step(Dt, Dt); sink = new byte[32]; } });
        Check("... while the same loop with one small allocation reads it (" + seeded + ")", seeded > 0);

        // an elite's outlined hearts
        EliteSystem.Clear();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var def = EliteCatalog.All[0];
        var ship = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
        var eh = ship.GetComponent<EliteHearts>();
        for (int i = 0; i < 5; i++) { eh.Place(Dt, Dt); eh.StepBreaks(Dt); }
        ship.TakeHit(EliteDamage.PlayerWeapon, ship.transform.position + Vector3.left);
        Action eliteWork = () => { for (int i = 0; i < 120; i++) { eh.Place(Dt, Dt); eh.StepBreaks(Dt); } };
        eliteWork();
        long eliteBytes = TestHarness.AllocatedBytes(eliteWork);
        Check("an elite's outlined hearts (a crumble playing) allocate nothing a frame (" + eliteBytes + " bytes)", meter && eliteBytes == 0);
        Check("... and its hearts wear the outline, at most 10% larger than before (" +
              (eh.heartSize / def.heartSize).ToString("F2") + "x), still under the player's 0.22",
              eh.HasOutlines && eh.heartSize <= def.heartSize * 1.15f + 1e-4f && eh.heartSize < .22f);
        EliteSystem.Clear();
    }

    // The outline is a thin trace round the heart, not a round glow.
    static void OutlineIsCrispNotAHalo()
    {
        var heart = Resources.Load<Sprite>(EliteHearts.SpritePath);
        var line = HeartOutline.For(heart);
        Check("the outline is built from the heart drawing", heart != null && line != null);
        if (line == null) return;
        var tex = line.texture;
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height, lit = 0, core = 0, key = 0;
        foreach (var p in px)
        {
            if (p.a > 8) lit++;
            if (p.a > 8 && p.r > 200) core++;
            if (p.a > 8 && p.r < 60) key++;
        }
        bool clearCorners = px[0].a == 0 && px[w - 1].a == 0 && px[(h - 1) * w].a == 0 && px[h * w - 1].a == 0;
        // the heart drawing's own lit share of its cell, for comparison
        int hw, hh;
        var src = ShieldContour.ReadPixels(heart, out hw, out hh);
        int solid = 0;
        if (src != null) foreach (var p in src) if (p.a >= 64) solid++;
        float grow = (float)lit / Mathf.Max(1, solid);
        Check("the outline hugs the shape: corners clear, it lights " + lit + " texels against the heart's " + solid +
              " (x" + grow.ToString("F2") + ", a round halo would be far more), under half its quad",
              clearCorners && lit < px.Length / 2 && grow < 1.9f && grow > 1.05f);
        Check("two tones: a light core line (" + core + " texels) and a dark keyline (" + key + ")", core > 0 && key > 0);
        Check("its reach is a thin trace: " + HeartOutline.ReachShare(64).ToString("F3") + " of the heart's cell, bold style (bright worlds) " +
              HeartOutline.BoldReachShare(64).ToString("F3") + " (<= 0.08)",
              HeartOutline.ReachShare(64) <= .08f && HeartOutline.BoldReachShare(64) <= .08f);
        int savedWorld = PlayerPrefs.GetInt(WorldManager.PrefsCurrentWorld, 0);
        var bold = new bool[WorldManager.Worlds.Length];
        for (int wi = 0; wi < bold.Length; wi++) { PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, wi); bold[wi] = HeartOutline.UseBold; }
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, savedWorld);
        // Verdant's v3 jungle is painted bright (Spec.brightArt), so it wears the bold one too
        Check("brightened Frost and bright-painted Verdant wear the bold outline, Space / Ember the standard one (" + string.Join(",", bold) + ")",
              bold.Length == 4 && !bold[0] && bold[1] && bold[2] && !bold[3]);
        Color c = HeartOutline.Core;
        Check("the light line is not red (it's amber-white: saturation " + Saturation(c).ToString("F2") + ")", Saturation(c) < .25f);
        // no halo objects anywhere on a boss's or an elite's hearts
        FreshScene(0);
        var e = StartFight(0);
        bool noHalo = true;
        foreach (var sr in e.Actor.Hearts.GetComponentsInChildren<SpriteRenderer>(true))
            noHalo &= sr.sprite == null || (sr.sprite != HostileGlow.Halo && !sr.sprite.name.ToLowerInvariant().Contains("halo"));
        Check("no round halo sprite on a boss's hearts", noHalo);
        // a constant scale: the outline is the heart's child at scale 1, only its alpha pulses
        var ol = e.Actor.Hearts.HeartOutlineRenderer(0);
        Check("the outline keeps the heart's scale (only its alpha pulses: " + HeartOutline.PulseMin + "..1)",
              ol != null && ol.transform.localScale == Vector3.one && ol.transform.parent == e.Actor.Hearts.Hearts[0]);
    }

    static float Saturation(Color c)
    {
        float h, s, v;
        Color.RGBToHSV(c, out h, out s, out v);
        return s;
    }

    // ---- outline contrast over every world -----------------------------------------

    const int RW = 1080, RH = 2340;   // a 9:19.5 phone, at its own pixels
    public const float MinContrast = 3f;
    public const int MinStandOut = 10;
    // The dark keyline is there for bright patches; over a dark world only the light line stands out.
    public const float MinShare = .3f;

    static void OutlineReadsOverEveryWorld()
    {
        // Frost twice: under its bright opening cloud ceiling (10 s) and over
        // its brightened ground once the ceiling has cleared.
        string[] worlds = { "Space", "Frost", "Verdant", "Ember", "Frost" };
        int[] index = { 0, 1, 2, 3, 1 };
        float[] seconds = { 10f, 10f, 10f, 10f, FrostTuning.CeilingClearAt + 5f };
        for (int pass = 0; pass < worlds.Length; pass++)
        {
            int w = index[pass];
            string label = pass == 4 ? "Frost ground" : worlds[pass];
            FreshScene(w);
            HeartOutline.Bold = System.Environment.GetEnvironmentVariable("HEARTS_PREVIEW_STANDARD") == "1" ? false : (bool?)null;
            cam.aspect = RW / (float)RH;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, RW, RH);
            var backdrop = new GameObject("~Backdrop");
            var wb = backdrop.AddComponent<WorldBackdrop>();
            wb.Show(worlds[pass], false);
            for (int i = 0; i < (int)(seconds[pass] * 60f); i++) wb.Step(1f / 60f);

            // the world's boss, resting at the top, its ring turned a little
            var boss = BossCatalog.ForWorld(w);
            var actor = BossActor.Spawn(boss);
            actor.BeginFight();
            for (int i = 0; i < 40; i++) actor.Hearts.Step(Dt, Dt);
            // and three elites lower down, spread across the lane
            EliteSystem.Clear();
            var pilot = new GameObject("~Pilot").transform;
            pilot.position = new Vector3(0f, -4f, 0f);
            EliteSystem.PlayerOverride = pilot;
            var elites = new List<EliteHearts>();
            var all = EliteCatalog.All;
            for (int k = 0; k < 3 && k < all.Length; k++)
            {
                var def = all[(w * 3 + k) % all.Length];
                var ship = EliteShip.CreateInPlay(def, new Vector2(-1.5f + 1.5f * k, -1.5f + .8f * (k % 2)));
                ship.AttackCooldown = 99f;
                var eh = ship.GetComponent<EliteHearts>();
                for (int i = 0; i < 30; i++) eh.Place(Dt, Dt);
                elites.Add(eh);
            }

            var rt = new RenderTexture(RW, RH, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(RW, RH, TextureFormat.RGB24, false);
            Color[] with = Grab(rt, tex);
            // Review crops: HEARTS_PREVIEW_DIR=<dir> (HEARTS_PREVIEW_STANDARD=1 renders
            // the standard outline everywhere, for a before / after).
            string previewDir = System.Environment.GetEnvironmentVariable("HEARTS_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(previewDir) && w == 1)
            {
                System.IO.Directory.CreateDirectory(previewDir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(previewDir, label.Replace(' ', '-') + ".png"), tex.EncodeToPNG());
            }
            SetOutlines(actor.Hearts, false);
            foreach (var eh in elites) SetOutlines(eh, false);
            Color[] without = Grab(rt, tex);

            int bossWorst = int.MaxValue, eliteWorst = int.MaxValue;
            float bossShare = 1f, eliteShare = 1f;
            for (int i = 0; i < actor.Hearts.Hearts.Length; i++)
            {
                float share;
                int n = StandOut(with, without, actor.Hearts.Hearts[i].position, actor.Hearts.heartSize, out share);
                bossWorst = Mathf.Min(bossWorst, n);
                bossShare = Mathf.Min(bossShare, share);
            }
            int eliteHearts = 0;
            foreach (var eh in elites)
                for (int i = 0; i < eh.Hearts.Length; i++)
                {
                    // the hearts shown, round the front of the hull (round the
                    // back they draw under it, outline and all)
                    if (!eh.Hearts[i].gameObject.activeInHierarchy || !eh.InFront(i)) continue;
                    eliteHearts++;
                    float share;
                    int n = StandOut(with, without, eh.Hearts[i].position, eh.heartSize, out share);
                    eliteWorst = Mathf.Min(eliteWorst, n);
                    eliteShare = Mathf.Min(eliteShare, share);
                }
            Check(label + ": every boss heart's outline stands " + MinContrast + ":1 clear of what is behind it (at least " +
                  bossWorst + " px a heart, " + (bossShare * 100f).ToString("F0") + "% of its outline pixels; need " + MinStandOut + ")",
                  bossWorst >= MinStandOut && bossShare >= MinShare);
            Check(label + ": every elite heart's outline stands " + MinContrast + ":1 clear of the backdrop and its ship (" + eliteHearts +
                  " hearts in front, at least " + eliteWorst + " px a heart, " + (eliteShare * 100f).ToString("F0") + "% of its outline pixels; need " + MinStandOut + ")",
                  eliteHearts > 0 && eliteWorst >= MinStandOut && eliteShare >= MinShare);

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(backdrop);
            Object.DestroyImmediate(actor.gameObject);
            EliteSystem.Clear();
            HeartOutline.Bold = null;
        }
    }

    static void SetOutlines(HeartOrbit h, bool on)
    {
        for (int i = 0; i < h.Hearts.Length; i++)
        {
            var r = h.HeartOutlineRenderer(i);
            if (r != null) r.enabled = on;
        }
    }

    static Color[] Grab(RenderTexture rt, Texture2D tex)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        return tex.GetPixels();
    }

    // Round one heart: the pixels the outline changed, and how many of them
    // stand MinContrast clear of what was there without it (backdrop or ship).
    static int StandOut(Color[] with, Color[] without, Vector3 at, float size, out float share)
    {
        Vector3 c = cam.WorldToScreenPoint(at);
        float pxPerUnit = RH / (cam.orthographicSize * 2f);
        int r = Mathf.CeilToInt(size * .6f * pxPerUnit);
        int changed = 0, count = 0;
        for (int y = (int)c.y - r; y <= (int)c.y + r; y++)
            for (int x = (int)c.x - r; x <= (int)c.x + r; x++)
            {
                if (x < 0 || y < 0 || x >= RW || y >= RH) continue;
                Color a = with[y * RW + x], b = without[y * RW + x];
                if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < .06f) continue;
                changed++;
                if (Ratio(Luminance(a), Luminance(b)) >= MinContrast) count++;
            }
        share = changed > 0 ? count / (float)changed : 0f;
        return count;
    }

    static float Lin(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
    static float Luminance(Color c) => .2126f * Lin(c.r) + .7152f * Lin(c.g) + .0722f * Lin(c.b);
    static float Ratio(float a, float b) => (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);

    static void CodexAndDocsMentionTheHearts()
    {
        foreach (var boss in BossCatalog.All)
        {
            string lore = CodexCatalogue.BossAttackLore(boss);
            Check("codex " + boss.id + " says it wears " + BossConfig.Hearts + " hearts",
                  lore.Contains("HEARTS  " + BossConfig.Hearts) && lore.Contains(BossConfig.HitPoints + " weapon hits"));
        }
        string docs = File.Exists("../docs/enemy-behaviours.md") ? File.ReadAllText("../docs/enemy-behaviours.md") : "";
        Check("docs/enemy-behaviours.md has the boss hearts section", docs.Contains("## Boss hearts"));
    }
}
