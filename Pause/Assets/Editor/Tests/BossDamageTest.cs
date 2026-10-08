using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: the Space boss shows its battle damage as it loses hearts.
//
// Stage = hearts lost (0 pristine .. 4 one heart left). From stage 1 its idle
// drawing is the matching damaged hull (Space_damage.png, a 2-frame loop);
// tell / fire / hit / death / retreat keep their own drawings. Over every
// pose while it fights: smoke (stage 2 faint, 3 heavier, 4 full) and
// electrical arcs (stage 3 dim intermittent bursts, 4 constant and erratic),
// from Space_damage_fx.png. Gone on death / retreat. Only Space has it.
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
            ArtSlicesIntoItsCells();
            OnlySpaceHasDamage();
            SpaceFightShowsTheDamage();
            OtherBossesUnchangedInAFight();
            OverlayGoesWithTheBoss();
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

    static void ArtSlicesIntoItsCells()
    {
        var space = BossCatalog.ForWorld(0);
        var hull = Resources.Load<Texture2D>(BossArt.Folder + "Space_damage");
        var fx = Resources.Load<Texture2D>(BossArt.Folder + "Space_damage_fx");
        Check("Space_damage imports unscaled at 768x1536 (" + (hull ? hull.width + "x" + hull.height : "missing") + ")",
              hull != null && hull.width == 768 && hull.height == 1536);
        Check("Space_damage_fx imports unscaled at 2304x768 (" + (fx ? fx.width + "x" + fx.height : "missing") + ")",
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
        Check("8 distinct 384px hull cells (" + hullOk + ")", BossArt.DamageCells == 8 && hullOk == 8);
        Check("12 distinct 384px fx cells (" + fxOk + ")", BossArt.DamageFxCells == 12 && fxOk == 12);
        var idle = BossArt.Body(space, BossArt.SpaceIdle0);
        var d0 = BossArt.DamageBody(space, 0);
        Check("hull cells are the size of the body cells (1 world unit at scale 1)",
              idle != null && d0 != null && Mathf.Abs(idle.bounds.size.x - d0.bounds.size.x) < 1e-4f &&
              Mathf.Abs(idle.bounds.size.y - d0.bounds.size.y) < 1e-4f);
        // row 0 of the strip is the top of the texture: stage 1, frame A
        Check("hull cell 0 is the top-left of the strip (stage 1, frame A)",
              d0 != null && d0.rect.x == 0f && d0.rect.y == 1152f);
        var s0 = BossArt.DamageFx(space, BossArt.Smoke0);
        var a0 = BossArt.DamageFx(space, BossArt.Arc0);
        Check("smoke row is the top fx row, arcs the bottom",
              s0 != null && a0 != null && s0.rect.y == 384f && a0.rect.y == 0f);
    }

    static void OnlySpaceHasDamage()
    {
        var all = BossCatalog.All;
        bool ok = true;
        foreach (var b in all)
        {
            bool space = b.artKey == "Space";
            ok &= BossArt.HasDamageArt(b) == space;
            int idle = BossArt.IdleFrame(b, 0f);
            for (int stage = 0; stage <= 4; stage++)
            {
                int cell = BossArt.DamageIdleCell(b, idle, stage, 0f);
                ok &= space ? (stage == 0 ? cell == -1 : cell == (stage - 1) * 2) : cell == -1;
            }
            if (!space) ok &= BossArt.DamageBody(b, 0) == null && BossArt.DamageFx(b, 0) == null;
        }
        Check("damaged idle frames resolve only for Space; Frost / Verdant / Ember get none", ok);

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

    static void SpaceFightShowsTheDamage()
    {
        var e = StartFight(0);
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
            Check(left + " hearts left -> stage " + stage + " (" + a.DamageStage + ")", a.DamageStage == stage);

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
            Check("stage " + stage + ": idle shows its damaged hull (" + idleDamaged + "/" + idleSeen + ")",
                  idleSeen > 0 && idleDamaged == idleSeen);
            Check("stage " + stage + ": other poses keep their drawings (" + posePristine + "/" + poseSeen + ")",
                  posePristine == poseSeen);
            float wantSmoke = BossArt.SmokeAlpha(stage);
            Check("stage " + stage + ": smoke " + (wantSmoke > 0f ? "at " + wantSmoke : "off") + " (" + smokeOn + "/" + frames + ")",
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
        Check("time scale 0 freezes smoke and arcs", frozen);
    }

    static void OtherBossesUnchangedInAFight()
    {
        for (int w = 1; w < BossCatalog.All.Length; w++)
        {
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

    static void OverlayGoesWithTheBoss()
    {
        // destroyed
        var e = StartFight(0);
        var a = e.Actor;
        LoseTo(e, 1);
        e.Step(Dt, 1f);
        Check("one heart left: smoke and arcs on", a.DamageSmoke.enabled && a.DamageArcs.enabled);
        for (int i = 0; i < 20 && e.State == BossEncounter.Phase.Fight; i++) { e.OnShipAttackHit(1f); e.Step(Dt, 1f); }
        bool destroyed = e.Destroyed && e.State == BossEncounter.Phase.Outro;
        bool gone = true;
        int dyingFrames = 0;
        // (the encounter removes the boss once its outro is done)
        for (int i = 0; i < 200 && a != null && e.State == BossEncounter.Phase.Outro; i++)
        {
            gone &= !a.DamageSmoke.enabled && !a.DamageArcs.enabled;
            if (a.State == BossActor.Mode.Dying) { dyingFrames++; gone &= Body(a).sprite == BossArt.Body(a.Boss, a.BodyFrame); }
            e.Step(Dt, 1f);
        }
        Check("destroyed: overlay gone through the death blasts, death frames pristine (" + dyingFrames + " frames)",
              destroyed && gone && dyingFrames > 0);

        // retreat (the clock ran out)
        e = StartFight(0);
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
        Check("retreating: overlay gone, retreat frames pristine, and it still leaves", left && a.State == BossActor.Mode.Gone);
    }
}
