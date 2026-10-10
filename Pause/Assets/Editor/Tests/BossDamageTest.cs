using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: a boss with damage art (BossDef.damageKey: Space, Frost, Ember, Tide) shows
// its battle damage as it loses hearts; Verdant has none yet. A boss with a
// BossDef.deathKey (all five) plays its <Key>_death.png strip over the body as it
// blows up.
//
// Stage = hearts lost (0 pristine .. 4 one heart left). From stage 1 its idle
// drawing is the matching damaged hull (<Key>_damage.png, a 2-frame loop over
// its own idle's period);
// tell / fire / hit / death / retreat keep their own drawings. Over every
// pose while it fights: smoke (stage 2 faint, 3 heavier, 4 full) and
// electrical arcs (stage 3 dim intermittent bursts, 4 constant and erratic),
// from <Key>_damage_fx.png (smoke scaled by BossDef.smokeStrength). Gone on
// death / retreat.
//
// Drives BossEncounter frame by frame (Step(realDt, timeScale)) in edit mode.
public static class BossDamageTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSSDAMAGE] PASS  " : "[BOSSDAMAGE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            StageFollowsHeartsLost();
            ArtSlicesIntoItsCells(0);
            ArtSlicesIntoItsCells(1);
            ArtSlicesIntoItsCells(3);
            ArtSlicesIntoItsCells(4);
            OnlyKeyedBossesHaveDamage();
            SmokeStrengthIsTunable();
            FightShowsTheDamage(0);
            FightShowsTheDamage(1);
            FightShowsTheDamage(3);
            FightShowsTheDamage(4);
            OtherBossesUnchangedInAFight();
            OverlayGoesWithTheBoss(0);
            OverlayGoesWithTheBoss(1);
            OverlayGoesWithTheBoss(3);
            OverlayGoesWithTheBoss(4);
            DeathStripArt(0, "Space");
            DeathStripArt(1, "Frost");
            DeathStripArt(2, "Verdant");
            DeathStripArt(3, "Ember");
            DeathStripArt(4, "Tide");
            for (int w = 0; w < 5; w++) DeathStripPlaysOnce(w);
            EveryBossHasDeathArt();
            BossDeathSoundHook();
        }
        finally
        {
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            Time.timeScale = 1f;
        }
        Debug.Log("[BOSSDAMAGE] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    const float Dt = 1f / 60f;

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
    }

    static BossEncounter StartFight(int world)
    {
        FreshScene(world);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        return e;
    }

    static SpriteRenderer Body(BossActor a) => a.transform.Find("Body").GetComponent<SpriteRenderer>();

    static int MaxHearts => Mathf.Max(1, BossConfig.Hearts);

    // Lose hearts until `left` remain (never the last: that ends the fight).
    static void LoseTo(BossEncounter e, int left)
    {
        for (int i = 0; i < 20 && e.HeartsLeft > left; i++) e.OnShipAttackHit(BossConfig.HeartWeight * .5f);
        for (int i = 0; i < 6; i++) e.Step(Dt, 1f);   // the hit flash over
    }

    // ---- checks --------------------------------------------------------

    static void StageFollowsHeartsLost()
    {
        int[] want = { 4, 4, 3, 2, 1, 0 };   // hearts left 0..5 of 5
        bool ok = true;
        for (int left = 0; left <= 5; left++) ok &= BossArt.DamageStage(5, left) == want[left];
        Check("stage = hearts lost, clamped 0..4 (5 left 0, 4 left 1, 3 left 2, 2 left 3, 1 left 4)", ok);
        Check("... more hearts than the max is pristine", BossArt.DamageStage(5, 7) == 0);
        Check("BossConfig.Hearts is still 5", BossConfig.Hearts == 5);

        // effect strengths per stage
        Check("smoke: none at 0/1, ~35% at 2, ~70% at 3, full at 4",
              BossArt.SmokeAlpha(0) == 0f && BossArt.SmokeAlpha(1) == 0f && Mathf.Abs(BossArt.SmokeAlpha(2) - .35f) < .01f &&
              Mathf.Abs(BossArt.SmokeAlpha(3) - .7f) < .01f && BossArt.SmokeAlpha(4) == 1f);
        int on3 = 0, off3 = 0, on4 = 0, slots = 400;
        var cells4 = new HashSet<int>();
        bool dim = true, below3 = true;
        for (int i = 0; i < slots; i++)
        {
            float t = i * BossArt.ArcBurstTicks * BossArt.Tick;
            float a3 = BossArt.ArcAlpha(3, t);
            if (a3 > 0f) { on3++; dim &= Mathf.Abs(a3 - .4f) < .01f; } else off3++;
            if (BossArt.ArcAlpha(4, t) == 1f) on4++;
            below3 &= BossArt.ArcAlpha(0, t) == 0f && BossArt.ArcAlpha(1, t) == 0f && BossArt.ArcAlpha(2, t) == 0f;
            cells4.Add(BossArt.ArcCell(4, i * BossArt.Tick));
        }
        Check("arcs: none below stage 3", below3);
        Check("arcs at stage 3 flash in bursts at ~40% alpha (" + on3 + " on / " + off3 + " off)",
              dim && on3 > slots / 5 && off3 > slots / 5);
        Check("arcs at stage 4 are always on at full alpha", on4 == slots);
        Check("... and jump between all their frames (" + cells4.Count + " seen)", cells4.Count == BossArt.DamageFxColumns);
    }

    static void ArtSlicesIntoItsCells(int world)
    {
        var space = BossCatalog.ForWorld(world);
        string key = space.damageKey, tag = space.artKey + ": ";
        var hull = Resources.Load<Texture2D>(BossArt.Folder + key + "_damage");
        var fx = Resources.Load<Texture2D>(BossArt.Folder + key + "_damage_fx");
        Check(key + "_damage imports unscaled at 768x1536 (" + (hull ? hull.width + "x" + hull.height : "missing") + ")",
              hull != null && hull.width == 768 && hull.height == 1536);
        Check(key + "_damage_fx imports unscaled at 2304x768 (" + (fx ? fx.width + "x" + fx.height : "missing") + ")",
              fx != null && fx.width == 2304 && fx.height == 768);

        var seen = new HashSet<Sprite>();
        int hullOk = 0, fxOk = 0;
        for (int i = 0; i < BossArt.DamageCells; i++)
        {
            var s = BossArt.DamageBody(space, i);
            if (s != null && s.rect.width == 384f && s.rect.height == 384f && seen.Add(s)) hullOk++;
        }
        for (int i = 0; i < BossArt.DamageFxCells; i++)
        {
            var s = BossArt.DamageFx(space, i);
            if (s != null && s.rect.width == 384f && s.rect.height == 384f && seen.Add(s)) fxOk++;
        }
        Check(tag + "8 distinct 384px hull cells (" + hullOk + ")", BossArt.DamageCells == 8 && hullOk == 8);
        Check(tag + "12 distinct 384px fx cells (" + fxOk + ")", BossArt.DamageFxCells == 12 && fxOk == 12);
        var idle = BossArt.Body(space, BossArt.IdleFrame(space, 0f));
        var d0 = BossArt.DamageBody(space, 0);
        // (Height only for Space: the body slicer cuts 7 rows from every
        // atlas, and Frost's is 4 rows -- BossEncounterTest's known
        // "body cells are square" failure. The damage sheets are cut by
        // their own 2 x 4 / 6 x 2 grids, square either way.)
        Check(tag + "hull cells are 1 world unit across like the body cells" + (world == 0 ? " and as tall" : ", and square"),
              idle != null && d0 != null && Mathf.Abs(idle.bounds.size.x - d0.bounds.size.x) < 1e-4f &&
              (world == 0 ? Mathf.Abs(idle.bounds.size.y - d0.bounds.size.y) < 1e-4f : Mathf.Abs(d0.bounds.size.y - 1f) < 1e-4f));
        // row 0 of the strip is the top of the texture: stage 1, frame A
        Check(tag + "hull cell 0 is the top-left of the strip (stage 1, frame A)",
              d0 != null && d0.rect.x == 0f && d0.rect.y == 1152f);
        var s0 = BossArt.DamageFx(space, BossArt.Smoke0);
        var a0 = BossArt.DamageFx(space, BossArt.Arc0);
        Check(tag + "smoke row is the top fx row, arcs the bottom",
              s0 != null && a0 != null && s0.rect.y == 384f && a0.rect.y == 0f);
    }

    static void OnlyKeyedBossesHaveDamage()
    {
        var all = BossCatalog.All;
        bool ok = true;
        foreach (var b in all)
        {
            bool space = b.artKey == "Space" || b.artKey == "Frost" || b.artKey == "Ember" || b.artKey == "Tide";
            ok &= BossArt.HasDamageArt(b) == space && (space ? b.damageKey == b.artKey : string.IsNullOrEmpty(b.damageKey));
            int idle = BossArt.IdleFrame(b, 0f);
            for (int stage = 0; stage <= 4; stage++)
            {
                int cell = BossArt.DamageIdleCell(b, idle, stage, 0f);
                ok &= space ? (stage == 0 ? cell == -1 : cell == (stage - 1) * 2) : cell == -1;
            }
            if (!space) ok &= BossArt.DamageBody(b, 0) == null && BossArt.DamageFx(b, 0) == null;
        }
        Check("damaged idle frames resolve for Space, Frost, Ember and Tide (by damageKey); Verdant gets none", ok);

        // Frost's idle is the 4-frame Idle0 loop: the damaged loop spans it
        var fr = BossCatalog.ForWorld(1);
        var floop = new HashSet<int>();
        bool fposes = true;
        for (int i = 0; i < 30; i++) floop.Add(BossArt.DamageIdleCell(fr, BossArt.Idle0 + i % BossArt.IdleFrames, 2, i * BossArt.Tick));
        foreach (int f in new[] { BossArt.Hit, BossArt.Fire, BossArt.Death(0), BossArt.Retreat0, BossArt.Tell(1, 1), BossArt.Portrait })
            fposes &= BossArt.DamageIdleCell(fr, f, 4, 0f) == -1;
        Check("Frost: every Idle0 frame shows the damaged loop (A, B) over the idle's 0.625 s; other poses keep theirs",
              floop.Count == 2 && floop.Contains(2) && floop.Contains(3) && fposes &&
              Mathf.Abs(BossArt.Seconds(BossArt.DamageIdleTicksFor(fr)) - BossArt.Seconds(BossArt.IdleTicks)) < 1e-4f);

        var sp = BossCatalog.ForWorld(0);
        bool poses = true;
        int[] notIdle = { BossArt.Hit, BossArt.Fire, BossArt.Death(0), BossArt.Death(4), BossArt.Retreat0,
                          BossArt.TellFrame(sp, 0, .5f), BossArt.TellFrame(sp, 2, 1f), BossArt.FireFrame(sp, 1), BossArt.Tell(0, 0) };
        foreach (int f in notIdle) poses &= BossArt.DamageIdleCell(sp, f, 4, 0f) == -1;
        Check("tell / fire / hit / death / retreat poses keep their own drawings at any stage", poses);
        var loop = new HashSet<int>();
        for (int i = 0; i < 24; i++) loop.Add(BossArt.DamageIdleCell(sp, BossArt.SpaceIdle0, 3, i * BossArt.Tick));
        Check("the damaged idle loops its two frames (A, B) over the idle's 0.5 s period",
              loop.Count == 2 && loop.Contains(4) && loop.Contains(5) &&
              Mathf.Abs(BossArt.Seconds(BossArt.DamageIdleTicks) - BossArt.Seconds(BossArt.SpaceIdleTicks)) < 1e-4f);
    }

    static void SmokeStrengthIsTunable()
    {
        var fr = BossCatalog.ForWorld(1);
        float saved = fr.smokeStrength;
        bool shared = true;
        foreach (var b in BossCatalog.All) shared &= b.smokeStrength == 1f;
        for (int st = 0; st <= 4; st++) shared &= BossArt.SmokeAlpha(fr, st) == BossArt.SmokeAlpha(st);
        fr.smokeStrength = .5f;
        bool half = Mathf.Abs(BossArt.SmokeAlpha(fr, 4) - .5f) < 1e-4f && Mathf.Abs(BossArt.SmokeAlpha(fr, 2) - .175f) < 1e-4f &&
                    BossArt.SmokeAlpha(BossCatalog.ForWorld(0), 4) == 1f;
        fr.smokeStrength = 3f;
        bool capped = BossArt.SmokeAlpha(fr, 4) == 1f;
        fr.smokeStrength = saved;
        Check("smoke: every boss on the shared schedule today; smokeStrength scales one boss's smoke (capped at 1)",
              shared && half && capped);
    }

    static void FightShowsTheDamage(int world)
    {
        var e = StartFight(world);
        var a = e.Actor;
        var body = Body(a);
        for (int i = 0; i < 10; i++) e.Step(Dt, 1f);
        Check("fight starts pristine: stage 0, no smoke, no arcs",
              a.DamageStage == 0 && a.DamageSmoke != null && !a.DamageSmoke.enabled && !a.DamageArcs.enabled);
        Check("... and the body is the plain atlas frame", body.sprite == BossArt.Body(a.Boss, a.BodyFrame));
        Check("overlays sit just above the body at its scale",
              a.DamageSmoke.sortingOrder > body.sortingOrder && a.DamageArcs.sortingOrder > a.DamageSmoke.sortingOrder &&
              a.DamageArcs.sortingOrder < 0 &&
              a.DamageSmoke.transform.localScale == body.transform.localScale &&
              a.DamageSmoke.transform.localPosition == body.transform.localPosition);

        for (int left = 4; left >= 1; left--)
        {
            LoseTo(e, left);
            int stage = MaxHearts - left;
            Check(a.Boss.artKey + ": " + left + " hearts left -> stage " + stage + " (" + a.DamageStage + ")", a.DamageStage == stage);

            int idleSeen = 0, idleDamaged = 0, poseSeen = 0, posePristine = 0, smokeOn = 0, arcOn = 0, frames = 0;
            float smokeA = 0f, arcA = 0f;
            for (int i = 0; i < 360 && e.State == BossEncounter.Phase.Fight; i++)
            {
                e.Step(Dt, 1f);
                frames++;
                if (BossArt.IsIdleFrame(a.BodyFrame))
                {
                    idleSeen++;
                    int row = (stage - 1) * BossArt.DamageColumns;
                    if (body.sprite == BossArt.DamageBody(a.Boss, row) || body.sprite == BossArt.DamageBody(a.Boss, row + 1)) idleDamaged++;
                }
                else
                {
                    poseSeen++;
                    if (body.sprite == BossArt.Body(a.Boss, a.BodyFrame)) posePristine++;
                }
                if (a.DamageSmoke.enabled) { smokeOn++; smokeA = a.DamageSmoke.color.a; }
                if (a.DamageArcs.enabled) { arcOn++; arcA = a.DamageArcs.color.a; }
            }
            Check(a.Boss.artKey + ": stage " + stage + ": idle shows its damaged hull (" + idleDamaged + "/" + idleSeen + ")",
                  idleSeen > 0 && idleDamaged == idleSeen);
            Check(a.Boss.artKey + ": stage " + stage + ": other poses keep their drawings (" + posePristine + "/" + poseSeen + ")",
                  posePristine == poseSeen);
            float wantSmoke = BossArt.SmokeAlpha(a.Boss, stage);
            Check(a.Boss.artKey + ": stage " + stage + ": smoke " + (wantSmoke > 0f ? "at " + wantSmoke : "off") + " (" + smokeOn + "/" + frames + ")",
                  wantSmoke > 0f ? smokeOn == frames && Mathf.Abs(smokeA - wantSmoke) < .01f : smokeOn == 0);
            if (stage < 3) Check("stage " + stage + ": no arcs", arcOn == 0);
            else if (stage == 3) Check("stage 3: arcs flicker, dim (" + arcOn + "/" + frames + ")", arcOn > 0 && arcOn < frames && Mathf.Abs(arcA - .4f) < .01f);
            else Check("stage 4: arcs constant, full (" + arcOn + "/" + frames + ")", arcOn == frames && arcA == 1f);
        }

        // pause freezes the overlay
        var smokeBefore = a.DamageSmoke.sprite;
        var arcBefore = a.DamageArcs.sprite;
        bool frozen = true;
        for (int i = 0; i < 40; i++) { e.Step(Dt, 0f); frozen &= a.DamageSmoke.sprite == smokeBefore && a.DamageArcs.sprite == arcBefore; }
        Check(a.Boss.artKey + ": time scale 0 freezes smoke and arcs", frozen);
    }

    static bool IsDeathStripCell(BossDef b, Sprite sp)
    {
        for (int c = 0; c < BossArt.DeathStripCells; c++) if (sp == BossArt.DeathStrip(b, c)) return true;
        return false;
    }

    static void OtherBossesUnchangedInAFight()
    {
        for (int w = 0; w < BossCatalog.All.Length; w++)
        {
            if (BossArt.HasDamageArt(BossCatalog.All[w])) continue;
            var e = StartFight(w);
            var a = e.Actor;
            var body = Body(a);
            LoseTo(e, 2);
            bool plain = true;
            for (int i = 0; i < 120 && e.State == BossEncounter.Phase.Fight; i++)
            {
                e.Step(Dt, 1f);
                plain &= body.sprite == BossArt.Body(a.Boss, a.BodyFrame);
            }
            Check(a.Boss.artKey + ": 2 hearts left, body frames unchanged and no damage overlay",
                  plain && a.DamageSmoke == null && a.DamageArcs == null &&
                  a.transform.Find("DamageSmoke") == null);
        }
    }

    static void OverlayGoesWithTheBoss(int world)
    {
        // destroyed
        var e = StartFight(world);
        var a = e.Actor;
        LoseTo(e, 1);
        e.Step(Dt, 1f);
        Check(a.Boss.artKey + ": one heart left: smoke and arcs on", a.DamageSmoke.enabled && a.DamageArcs.enabled);
        for (int i = 0; i < 20 && e.State == BossEncounter.Phase.Fight; i++) { e.OnShipAttackHit(1f); e.Step(Dt, 1f); }
        bool destroyed = e.Destroyed && e.State == BossEncounter.Phase.Outro;
        bool gone = true;
        int dyingFrames = 0;
        // (the encounter removes the boss once its outro is done)
        for (int i = 0; i < 200 && a != null && e.State == BossEncounter.Phase.Outro; i++)
        {
            gone &= !a.DamageSmoke.enabled && !a.DamageArcs.enabled;
            if (a.State == BossActor.Mode.Dying) { dyingFrames++; gone &= BossArt.HasDeathArt(a.Boss) ? IsDeathStripCell(a.Boss, Body(a).sprite) : Body(a).sprite == BossArt.Body(a.Boss, a.BodyFrame); }
            e.Step(Dt, 1f);
        }
        Check(a.Boss.artKey + ": destroyed: overlay gone through the death blasts, death frames pristine (" + dyingFrames + " frames)",
              destroyed && gone && dyingFrames > 0);

        // retreat (the clock ran out)
        e = StartFight(world);
        a = e.Actor;
        LoseTo(e, 1);
        e.Step(Dt, 1f);
        a.BeginOutro(false);
        bool left = !a.DamageSmoke.enabled && !a.DamageArcs.enabled;
        var body = Body(a);
        for (int i = 0; i < 120 && a.State == BossActor.Mode.Retreating; i++)
        {
            a.StepOutro(Dt, 0);
            left &= !a.DamageSmoke.enabled && !a.DamageArcs.enabled && body.sprite == BossArt.Body(a.Boss, a.BodyFrame);
        }
        Check(a.Boss.artKey + ": retreating: overlay gone, retreat frames pristine, and it still leaves", left && a.State == BossActor.Mode.Gone);
    }

    // ---- the death strip -----------------------------------------------

    static Color32[] Pixels(Sprite sp, out int w, out int h) => ShieldContour.ReadPixels(sp, out w, out h);

    // opaque bounding box in cell px (null when empty)
    // (sprites are cut tight to their drawing: offset by the trim to get cell px)
    static int[] Box(Sprite sp)
    {
        int w, h;
        var px = Pixels(sp, out w, out h);
        if (px == null) return null;
        var o = sp.textureRectOffset;
        var b = BoxOf(px, w, h);
        if (b == null) return null;
        int ox = Mathf.RoundToInt(o.x), oy = Mathf.RoundToInt(o.y);
        return new[] { b[0] + ox, b[1] + oy, b[2] + ox, b[3] + oy };
    }

    static int[] BoxOf(Color32[] px, int w, int h)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 24) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
        return x1 < 0 ? null : new[] { x0, y0, x1, y1 };
    }

    static void DeathStripArt(int world, string name)
    {
        var em = BossCatalog.ForWorld(world);
        var tex = Resources.Load<Texture2D>(BossArt.Folder + name + "_death");
        Check(name + ": deathKey set and " + name + "_death imports unscaled at 2304x384 (" + (tex ? tex.width + "x" + tex.height : "missing") + ")",
              em.deathKey == name && BossArt.HasDeathArt(em) && tex != null && tex.width == 2304 && tex.height == 384);
        var idle = BossArt.Body(em, BossArt.IdleFrame(em, 0f));
        var seen = new HashSet<Sprite>();
        bool same = true, inside = true;
        var ibox = Box(idle);
        for (int i = 0; i < BossArt.DeathStripCells; i++)
        {
            var s = BossArt.DeathStrip(em, i);
            if (s == null || !seen.Add(s)) { same = false; continue; }
            // same scale, same anchor as the idle cell
            same &= s.rect.width == 384f && s.rect.height == 384f && Mathf.Abs(s.bounds.size.x - idle.bounds.size.x) < 1e-4f &&
                    Mathf.Abs(s.bounds.size.y - idle.bounds.size.y) < 1e-4f && s.pivot == idle.pivot && s.rect.x == i * 384f;
            var box = Box(s);
            // content is never empty and stays inside its own 384 cell (the idle art itself spans 7..382 of it)
            inside &= box != null && box[0] >= 0 && box[1] >= 0 && box[2] < 384 && box[3] < 384 &&
                      box[2] - box[0] <= (ibox[2] - ibox[0]) + 16;
        }
        Check(name + " death strip:"+" 6 distinct 384px cells, same scale / pivot as the idle cell", same && seen.Count == 6);
        Check(name + " death strip:"+" every cell is drawn and sits inside its cell, no wider than the idle art", inside);
        // registration: the first cell is the idle pose bursting, so its body centre matches the idle cell's
        var b0 = Box(BossArt.DeathStrip(em, 0));
        float cx0 = (b0[0] + b0[2]) / 2f, cx1 = (ibox[0] + ibox[2]) / 2f;
        float cy0 = (b0[1] + b0[3]) / 2f, cy1 = (ibox[1] + ibox[3]) / 2f;
        Check(name + " death cell 0 is registered to the idle cell (centre off by " + Mathf.Abs(cx0 - cx1).ToString("0") + ", " +
              Mathf.Abs(cy0 - cy1).ToString("0") + " px of 384)", Mathf.Abs(cx0 - cx1) <= 40f && Mathf.Abs(cy0 - cy1) <= 40f);
        Check("strip timing: 6 cells at 0.12 s, clamped (cell at 0 s, .13 s, .5 s, .71 s, 3 s)",
              BossArt.DeathStripCell(0f) == 0 && BossArt.DeathStripCell(.13f) == 1 && BossArt.DeathStripCell(.5f) == 4 &&
              BossArt.DeathStripCell(.71f) == 5 && BossArt.DeathStripCell(3f) == 5 && BossArt.DeathStripCell(-1f) == 0);
        // picked up purely by key
        var temp = new BossDef { artKey = "Frost", deathKey = name };
        var missing = new BossDef { artKey = "Frost", deathKey = "NoSuchWorld" };
        Check("death art is looked up by deathKey: found for a key with a strip, absent (no throw) for one without or none",
              BossArt.HasDeathArt(temp) && BossArt.DeathStrip(temp, 0) == BossArt.DeathStrip(em, 0) &&
              !BossArt.HasDeathArt(missing) && !BossArt.HasDeathArt(new BossDef { artKey = "Frost" }) && !BossArt.HasDeathArt(null));
    }

    static void DeathStripPlaysOnce(int world)
    {
        FreshScene(world);
        bool done = false;
        BossEncounter.Begin(world, () => done = true);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        var a = e.Actor;
        var body = Body(a);
        for (int i = 0; i < 20 && e.State == BossEncounter.Phase.Fight; i++) { e.OnShipAttackHit(1f); e.Step(Dt, 1f); }
        Check(a.Boss.artKey + ": destroyed", e.Destroyed && e.State == BossEncounter.Phase.Outro);

        int dying = 0, lastCell = -1, back = 0, firstFrameCell = -1, maxCell = -1;
        var cellFrames = new int[BossArt.DeathStripCells];
        bool strip = true;
        long alloc = 0;
        int allocFrames = 0;
        for (int i = 0; i < 300 && a != null && e.State == BossEncounter.Phase.Outro && !done; i++)
        {
            bool dyingNow = a.State == BossActor.Mode.Dying && body.enabled;
            if (dyingNow)
            {
                dying++;
                int cell = -1;
                for (int c = 0; c < BossArt.DeathStripCells; c++) if (body.sprite == BossArt.DeathStrip(a.Boss, c)) cell = c;
                strip &= cell >= 0;
                if (cell >= 0) cellFrames[cell]++;
                if (dying == 1) firstFrameCell = cell;
                if (cell < lastCell) back++;
                lastCell = cell; maxCell = Mathf.Max(maxCell, cell);
            }
            // between the last blast (0.4 s) and the end, nothing else spawns: step must not allocate
            bool quiet = a.State == BossActor.Mode.Dying && dying * Dt > .45f && dying * Dt < .7f;
            long before = quiet ? System.GC.GetAllocatedBytesForCurrentThread() : 0;
            e.Step(Dt, 1f);
            if (quiet) { alloc += System.GC.GetAllocatedBytesForCurrentThread() - before; allocFrames++; }
        }
        Check("death strip: every dying frame shows a strip cell (" + dying + " frames), starting at cell 0", strip && dying > 0 && firstFrameCell == 0);
        Check("death strip: plays once, in order, never backwards, reaching the last cell", back == 0 && maxCell == BossArt.DeathStripCells - 1);
        bool each = true;
        for (int c = 0; c < 5; c++) each &= Mathf.Abs(cellFrames[c] * Dt - BossArt.DeathStripCellSeconds) < 2.5f * Dt;
        Check("each early cell holds ~0.12 s (" + string.Join(",", cellFrames) + " frames at 60 fps)", each);
        Check("the death is not shortened: lasts at least the atlas death's " + BossArt.Seconds(BossArt.DeathTicks).ToString("0.00") +
              " s (" + (dying * Dt).ToString("0.00") + " s)", dying * Dt >= BossArt.Seconds(BossArt.DeathTicks) - 2.5f * Dt);
        Check("no allocation while the strip plays (" + alloc + " B over " + allocFrames + " frames)", allocFrames > 5 && alloc == 0);
        // the flow: it ends, then the encounter finishes and the world transition callback fires
        for (int i = 0; i < 400 && !done; i++) e.Step(Dt, 1f);
        Check("defeat flow completes: the boss is gone and the finished callback (world transition) fires", done);
    }

    static void EveryBossHasDeathArt()
    {
        bool ok = BossCatalog.All.Length >= 5;
        string miss = "";
        foreach (var b in BossCatalog.All)
        {
            bool has = !string.IsNullOrEmpty(b.deathKey) && BossArt.HasDeathArt(b);
            if (!has) miss += b.artKey + " ";
            ok &= has;
        }
        Check("EVERY boss in BossCatalog.All has a death strip (missing: " + miss.Trim() + ")", ok);
        // importer settings of each death strip match Ember's reference
        var refImp = UnityEditor.AssetImporter.GetAtPath("Assets/Art/Resources/Bosses/Ember_death.png") as UnityEditor.TextureImporter;
        bool same = refImp != null;
        foreach (var b in BossCatalog.All)
        {
            var imp = UnityEditor.AssetImporter.GetAtPath("Assets/Art/Resources/Bosses/" + b.deathKey + "_death.png") as UnityEditor.TextureImporter;
            same &= imp != null && refImp != null && imp.textureType == refImp.textureType &&
                    imp.filterMode == refImp.filterMode && imp.mipmapEnabled == refImp.mipmapEnabled && imp.isReadable == refImp.isReadable &&
                    imp.alphaIsTransparency == refImp.alphaIsTransparency && imp.maxTextureSize == refImp.maxTextureSize &&
                    imp.textureCompression == refImp.textureCompression && imp.npotScale == refImp.npotScale &&
                    imp.spritePixelsPerUnit == refImp.spritePixelsPerUnit && imp.sRGBTexture == refImp.sRGBTexture;
        }
        Check("every boss death strip has the same importer settings as Ember_death", same);
    }

    // The sound hook: keys are boss_<world>, silent (no throw) while no clips are authored.
    static void BossDeathSoundHook()
    {
        bool keys = EnemyDeathAudio.BossKey("Space") == "boss_space" && EnemyDeathAudio.BossKey("Verdant") == "boss_verdant" && EnemyDeathAudio.BossKey("") == null;
        Check("boss death sound hook: boss_<world> keys; screams for Frost/Verdant only",
              keys && EnemyDeathAudio.BossScreams("Frost") && EnemyDeathAudio.BossScreams("Verdant") && !EnemyDeathAudio.BossScreams("Space"));

        // the real fight path: BossActor.BeginOutro(true) plays the world's boss cue exactly once
        bool sim = EnemyDeathAudio.Simulate;
        var clock = EnemyDeathAudio.Clock;
        double t = 1000;
        EnemyDeathAudio.Simulate = true;
        EnemyDeathAudio.Clock = () => t;
        try
        {
            for (int w = 0; w < 5; w++)
            {
                EnemyDeathAudio.ResetVoices();
                var e = StartFight(w);
                var a = e.Actor;
                string key = EnemyDeathAudio.BossKey(a.Boss.artKey);
                int before = EnemyDeathAudio.Played;
                t += 5;
                a.BeginOutro(true);
                Check(a.Boss.artKey + ": BeginOutro(true) plays " + key + " once (" + (EnemyDeathAudio.Played - before) + ")",
                      EnemyDeathAudio.Played == before + 1 && EnemyDeathAudio.LastKey == key && EnemyDeathAudio.LastClip != null);
                a.BeginOutro(false);
                Check(a.Boss.artKey + ": a retreat (BeginOutro(false)) is silent", EnemyDeathAudio.Played == before + 1);
            }
        }
        finally { EnemyDeathAudio.Simulate = sim; EnemyDeathAudio.Clock = clock; EnemyDeathAudio.ResetVoices(); }
    }
}
