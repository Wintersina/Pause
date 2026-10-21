using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: a boss with damage art (BossDef.damageKey: all five bosses) shows
// its battle damage as it loses hearts. A boss with a
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

    // The boss atlas's own cell size in px (384 for most; the Cinder Drake's art is 576 = 384 x 1.5, same world size).
    static int Cell(BossDef b)
    {
        var s = BossArt.Body(b, 0);
        return s == null ? 384 : Mathf.RoundToInt(s.rect.width);
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
            ArtSlicesIntoItsCells(2);
            ArtSlicesIntoItsCells(3);
            ArtSlicesIntoItsCells(4);
            ArtSlicesIntoItsCells(5);
            OnlyKeyedBossesHaveDamage();
            DamagedAttackPosesAreOneFrame();
            DamageFxCellsAreClean();
            EveryBossOneDrawing();
            SmokeStrengthIsTunable();
            FightShowsTheDamage(0);
            FightShowsTheDamage(1);
            FightShowsTheDamage(2);
            FightShowsTheDamage(3);
            FightShowsTheDamage(4);
            FightShowsTheDamage(5);
            OtherBossesUnchangedInAFight();
            OverlayGoesWithTheBoss(0);
            OverlayGoesWithTheBoss(1);
            OverlayGoesWithTheBoss(2);
            OverlayGoesWithTheBoss(3);
            OverlayGoesWithTheBoss(4);
            OverlayGoesWithTheBoss(5);
            DamageRegistration(2);
            DamageLoopsAnimate(2);
            DeathStripArt(0, "Space");
            DeathStripArt(1, "Frost");
            DeathStripArt(2, "Verdant");
            DeathStripArt(3, "Ember");
            DeathStripArt(4, "Tide");
            StormDeathStrip();
            StormDamageKeepsRepaintColours();
            DesertOwnStrips();
            for (int w = 0; w < 6; w++) DeathStripPlaysOnce(w);
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
        for (int i = 0; i < 40 && e.HeartsLeft > left; i++) e.OnShipAttackHit(BossConfig.HeartWeight * .5f);
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
        Check("BossConfig.Hearts is 5 + " + BossConfig.ExtraHearts + " = " + (BossConfig.BaseHearts + BossConfig.ExtraHearts),
              BossConfig.Hearts == BossConfig.BaseHearts + BossConfig.ExtraHearts);
        // the four looks spread over the longer fight in proportion: 12 hearts -> 1-3 lost stage 1, 4-6 stage 2, 7-9 stage 3, 10+ stage 4
        int n = BossConfig.Hearts;
        int[] seen = new int[BossArt.DamageStages + 1];
        bool mono = BossArt.DamageStage(n, n) == 0, last = BossArt.DamageStage(n, 1) == BossArt.DamageStages;
        for (int left = n - 1; left >= 0; left--)
        {
            int st = BossArt.DamageStage(n, left);
            mono &= st >= BossArt.DamageStage(n, left + 1) && st >= 1;
            seen[st]++;
        }
        bool even = true;
        for (int st = 1; st <= BossArt.DamageStages; st++) even &= seen[st] >= 1 && seen[st] <= Mathf.CeilToInt(n / (float)BossArt.DamageStages) + 1;
        Check("with " + n + " hearts the damage stages spread evenly (counts " + string.Join(",", seen) + "), never pristine once hurt, the last heart is stage 4",
              mono && last && even);

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
        int c = Cell(space);
        var hull = Resources.Load<Texture2D>(BossArt.Folder + key + "_damage");
        var fx = Resources.Load<Texture2D>(BossArt.Folder + key + "_damage_fx");
        Check(key + "_damage imports unscaled at " + 2 * c + "x" + 4 * c + " (" + (hull ? hull.width + "x" + hull.height : "missing") + ")",
              hull != null && hull.width == 2 * c && hull.height == 4 * c);
        Check(key + "_damage_fx imports unscaled at " + 6 * c + "x" + 2 * c + " (" + (fx ? fx.width + "x" + fx.height : "missing") + ")",
              fx != null && fx.width == 6 * c && fx.height == 2 * c);

        var seen = new HashSet<Sprite>();
        int hullOk = 0, fxOk = 0;
        for (int i = 0; i < BossArt.DamageCells; i++)
        {
            var s = BossArt.DamageBody(space, i);
            if (s != null && s.rect.width == c && s.rect.height == c && seen.Add(s)) hullOk++;
        }
        for (int i = 0; i < BossArt.DamageFxCells; i++)
        {
            var s = BossArt.DamageFx(space, i);
            if (s != null && s.rect.width == c && s.rect.height == c && seen.Add(s)) fxOk++;
        }
        Check(tag + "8 distinct " + c + "px hull cells (" + hullOk + ")", BossArt.DamageCells == 8 && hullOk == 8);
        Check(tag + "12 distinct " + c + "px fx cells (" + fxOk + ")", BossArt.DamageFxCells == 12 && fxOk == 12);
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
              d0 != null && d0.rect.x == 0f && d0.rect.y == 3 * c);
        var s0 = BossArt.DamageFx(space, BossArt.Smoke0);
        var a0 = BossArt.DamageFx(space, BossArt.Arc0);
        Check(tag + "smoke row is the top fx row, arcs the bottom",
              s0 != null && a0 != null && s0.rect.y == c && a0.rect.y == 0f);
    }

    static void OnlyKeyedBossesHaveDamage()
    {
        var all = BossCatalog.All;
        bool ok = true;
        foreach (var b in all)
        {
            bool space = b.artKey == "Space" || b.artKey == "Frost" || b.artKey == "Ember" || b.artKey == "Tide" || b.artKey == "Verdant" || b.artKey == "Storm" || b.artKey == "Desert";
            // (Storm wears BossCatalog.StormDamageKey: the Kraken's art until Storm_damage*.png are installed, then "Storm")
            string key = b.artKey == "Storm" ? BossCatalog.StormDamageKey : b.artKey;
            ok &= BossArt.HasDamageArt(b) == space && (space ? b.damageKey == key : string.IsNullOrEmpty(b.damageKey));
            int idle = BossArt.IdleFrame(b, 0f);
            for (int stage = 0; stage <= 4; stage++)
            {
                int cell = BossArt.DamageIdleCell(b, idle, stage, 0f);
                ok &= space ? (stage == 0 ? cell == -1 : cell == (stage - 1) * 2) : cell == -1;
            }
            if (!space) ok &= BossArt.DamageBody(b, 0) == null && BossArt.DamageFx(b, 0) == null;
        }
        Check("damaged idle frames resolve for every boss (by damageKey)", ok);

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

        for (int stage = 1; stage <= BossArt.DamageStages; stage++)
        {
            // the first heart count that shows this stage
            int left = MaxHearts - 1;
            while (left > 1 && BossArt.DamageStage(MaxHearts, left) < stage) left--;
            LoseTo(e, left);
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
                    // a tell / fire pose wears its own damaged drawing (BossArt.AttackDamageBody); others keep the body frame
                    var want = BossArt.AttackDamageBody(a.Boss, a.BodyFrame, stage) ?? BossArt.Body(a.Boss, a.BodyFrame);
                    if (body.sprite == want) posePristine++;
                }
                if (a.DamageSmoke.enabled) { smokeOn++; smokeA = a.DamageSmoke.color.a; }
                if (a.DamageArcs.enabled) { arcOn++; arcA = a.DamageArcs.color.a; }
            }
            Check(a.Boss.artKey + ": stage " + stage + ": idle shows its damaged hull (" + idleDamaged + "/" + idleSeen + ")",
                  idleSeen > 0 && idleDamaged == idleSeen);
            Check(a.Boss.artKey + ": stage " + stage + ": other poses keep their own (damaged, for tell / fire) drawings (" + posePristine + "/" + poseSeen + ")",
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

    // Verdant damage art: every hull cell has art, stays inside the pristine idle cell's box (+6 px),
    // stages differ, and the 2-frame idle loops and fx loops animate.
    static void DamageRegistration(int world)
    {
        var b = BossCatalog.ForWorld(world);
        var ibox = Box(BossArt.Body(b, BossArt.IdleFrame(b, 0f)));
        bool nonEmpty = true, reg = ibox != null;
        for (int i = 0; i < BossArt.DamageCells; i++)
        {
            var bx = Box(BossArt.DamageBody(b, i));
            if (bx == null) { nonEmpty = false; continue; }
            if (ibox != null && (bx[0] < ibox[0] - 6 || bx[1] < ibox[1] - 6 || bx[2] > ibox[2] + 6 || bx[3] > ibox[3] + 6)) reg = false;
        }
        Check(b.artKey + ": all 8 damage hull cells non-empty", nonEmpty);
        Check(b.artKey + ": every damage cell sits within the pristine idle cell's box + 6 px", reg);
        bool fxOk = true;
        for (int i = 0; i < BossArt.DamageFxCells; i++) fxOk &= Box(BossArt.DamageFx(b, i)) != null;
        Check(b.artKey + ": all 12 fx cells non-empty", fxOk);
        bool differ = true;
        for (int st = 1; st < 4; st++) differ &= !SamePixels(BossArt.DamageBody(b, (st - 1) * 2), BossArt.DamageBody(b, st * 2));
        Check(b.artKey + ": damage stages differ from each other", differ);
    }

    static bool SamePixels(Sprite a, Sprite b)
    {
        int w1, h1, w2, h2;
        var p = Pixels(a, out w1, out h1); var q = Pixels(b, out w2, out h2);
        if (p == null || q == null || w1 != w2 || h1 != h2) return false;
        for (int i = 0; i < p.Length; i++) if (p[i].r != q[i].r || p[i].g != q[i].g || p[i].b != q[i].b || p[i].a != q[i].a) return false;
        return true;
    }

    static void DamageLoopsAnimate(int world)
    {
        var b = BossCatalog.ForWorld(world);
        bool frames = true;
        for (int st = 0; st < 4; st++) frames &= !SamePixels(BossArt.DamageBody(b, st * 2), BossArt.DamageBody(b, st * 2 + 1));
        Check(b.artKey + ": each stage's two idle frames differ (loop animates)", frames);
        bool smoke = true, arcs = true;
        for (int i = 0; i < BossArt.DamageFxColumns - 1; i++)
        {
            smoke &= !SamePixels(BossArt.DamageFx(b, BossArt.Smoke0 + i), BossArt.DamageFx(b, BossArt.Smoke0 + i + 1));
            arcs &= !SamePixels(BossArt.DamageFx(b, BossArt.Arc0 + i), BossArt.DamageFx(b, BossArt.Arc0 + i + 1));
        }
        Check(b.artKey + ": smoke and electrical loops change frame to frame", smoke && arcs);
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
            left &= !a.DamageSmoke.enabled && !a.DamageArcs.enabled && (body.sprite == BossArt.Body(a.Boss, a.BodyFrame) || body.sprite == BossArt.AttackDamageBody(a.Boss, a.BodyFrame, a.DamageStage));   // (its first tick is tell0's drawing, damaged)
        }
        Check(a.Boss.artKey + ": retreating: overlay gone, retreat frames pristine, and it still leaves", left && a.State == BossActor.Mode.Gone);
    }

    // ---- smoke / arc cells are single clean drawings (Frost / Verdant low-health "two frames" report) ----
    //
    // <Key>_damage_fx.png: a smoke plume that is clipped to a flat-edged box, two drawings joined by a straight
    // seam, a faint ghost layer of another frame, or an arc fragment cut off by a rectangle all read as a second
    // frame laid over the hull. Every fx cell must fade out INSIDE the cell with a margin (no alpha near the cell
    // border), have no long straight axis-aligned alpha edge, and for the four reworked bosses no faint haze layer
    // and no clipped sliver.
    static int LongestStraight(bool[] m, int size)
    {
        int best = 0;
        for (int x = 0; x < size - 1; x++)           // vertical edges: run down a column of horizontal alpha changes
        {
            int run = 0;
            for (int y = 0; y < size; y++)
            {
                if (m[y * size + x] != m[y * size + x + 1]) { run++; if (run > best) best = run; } else run = 0;
            }
        }
        for (int y = 0; y < size - 1; y++)           // horizontal edges
        {
            int run = 0;
            for (int x = 0; x < size; x++)
            {
                if (m[y * size + x] != m[(y + 1) * size + x]) { run++; if (run > best) best = run; } else run = 0;
            }
        }
        return best;
    }

    // Small connected pieces (<= maxArea px) that carry a flat axis-aligned edge of minRun px: cut-off slivers.
    static int ClippedSlivers(bool[] m, int size, int maxArea, int minRun)
    {
        var seen = new bool[m.Length];
        var stack = new Stack<int>();
        int bad = 0;
        for (int start = 0; start < m.Length; start++)
        {
            if (!m[start] || seen[start]) continue;
            var comp = new List<int>();
            stack.Push(start); seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop(); comp.Add(i);
                int y = i / size, x = i % size;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int yy = y + dy, xx = x + dx;
                        if (yy < 0 || yy >= size || xx < 0 || xx >= size) continue;
                        int j = yy * size + xx;
                        if (m[j] && !seen[j]) { seen[j] = true; stack.Push(j); }
                    }
            }
            if (comp.Count > maxArea) continue;
            int x0 = size, y0 = size, x1 = 0, y1 = 0;
            foreach (int i in comp) { int y = i / size, x = i % size; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            int w = x1 - x0 + 3, h = y1 - y0 + 3;
            var sub = new bool[Mathf.Max(w, h) * Mathf.Max(w, h)];
            int sz = Mathf.Max(w, h);
            foreach (int i in comp) sub[(i / size - y0 + 1) * sz + (i % size - x0 + 1)] = true;
            if (LongestStraight(sub, sz) >= minRun) bad++;
        }
        return bad;
    }

    static void DamageFxCellsAreClean()
    {
        var reworked = new HashSet<string> { "Frost", "Verdant", "Storm", "Desert" };
        foreach (var boss in BossCatalog.All)
        {
            if (!BossArt.HasDamageArt(boss)) continue;
            int cell = Cell(boss);
            float k = cell / 384f;
            int margin = Mathf.RoundToInt(12 * k), maxStraight = Mathf.RoundToInt(20 * k);
            int onBorder = 0, straightBad = 0, hazeBad = 0, sliverBad = 0, empty = 0, maxRun = 0;
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < BossArt.DamageFxColumns; col++)
                {
                    var sp = BossArt.DamageFx(boss, row == 0 ? BossArt.Smoke0 + col : BossArt.Arc0 + col);
                    var px = sp == null ? null : CellPixels(sp, cell);
                    if (px == null) { empty++; continue; }
                    var m = AlphaMask(px, 0);
                    int opaque = 0, faint = 0; long alphaSum = 0;
                    for (int i = 0; i < px.Length; i++)
                    {
                        int y = i / cell, x = i % cell;
                        if (px[i].a == 0) continue;
                        opaque++; alphaSum += px[i].a;
                        if (px[i].a < 48) faint++;
                        if (x < margin || y < margin || x >= cell - margin || y >= cell - margin) onBorder++;
                    }
                    if (opaque == 0) { if (row == 0) empty++; continue; }
                    int run = LongestStraight(m, cell);
                    maxRun = Mathf.Max(maxRun, run);
                    if (row == 0 && run > maxStraight) straightBad++;
                    if (reworked.Contains(boss.artKey))
                    {
                        if (row == 0 && (faint > opaque * .01f || alphaSum / (float)opaque < 200f)) hazeBad++;   // a ghost / cross-faded layer
                        if (row == 1 && ClippedSlivers(m, cell, Mathf.RoundToInt(300 * k * k), Mathf.Max(5, Mathf.RoundToInt(7 * k))) > 0) sliverBad++;
                    }
                }
            string t = boss.artKey + ": fx cells ";
            Check(t + "fade out inside the cell with a " + margin + " px margin (" + onBorder + " px near the border)", empty == 0 && onBorder == 0);
            Check(t + "smoke has no long straight clipped edge or seam (longest " + maxRun + ", limit " + maxStraight + ", " + straightBad + " cells)", straightBad == 0);
            if (reworked.Contains(boss.artKey))
            {
                Check(t + "smoke has no faint ghost layer / cross-faded second frame (" + hazeBad + " cells)", hazeBad == 0);
                Check(t + "arcs have no clipped boxy slivers (" + sliverBad + " cells)", sliverBad == 0);
            }
        }
    }

    // ---- a damaged attack pose is ONE drawing (the Frost-boss "frames overlaid" bug) --
    //
    // The damaged tell / fire drawings are the pristine pose plus damage transferred from the
    // damaged IDLE hull. Copying idle pixels onto parts that moved in the pose stamped a second,
    // offset hull over the pose (towers doubled), so a damaged boss firing looked like several
    // frames drawn at once. The stage 3 / 4 drawings of Frost / Verdant / Storm / Desert are regenerated whole
    // from the clean pose plus the damaged idle hull (one drawing, the same damage look as the idle hull), so the
    // checks are: stay inside the pose's own silhouette, never copy a textured patch of the wrong-pose idle hull,
    // no translucent second layer, and keep the pose (silhouette overlap).
    const int SameHull = 56;       // BossImpact build.py SAME_HULL: pose pixel == idle pixel within this
    const int Slack = 6;           // rounding between the transfer maths and the compare

    // A sprite's whole square cell (sprites are cut tight, so read the full rect off the source file;
    // row 0 = bottom), null if unreadable.
    static Color32[] CellPixels(Sprite sp, int cell)
    {
        var r = sp.rect;
        if (Mathf.RoundToInt(r.width) != cell || Mathf.RoundToInt(r.height) != cell) return null;
        return ShieldContour.ReadFromSourceFile(sp.texture, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), cell, cell);
    }

    static int Diff(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

    // ---- silhouette / translucency helpers (square cell, row-major) --------------------------------

    static bool[] AlphaMask(Color32[] px, int thr)
    {
        var m = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) m[i] = px[i].a > thr;
        return m;
    }

    // Chebyshev distance of every px to the nearest `true` px of src (two-pass chamfer; far = no true px at all).
    static int[] ChebDist(bool[] src, int size)
    {
        const int Far = 1 << 20;
        var d = new int[src.Length];
        for (int i = 0; i < d.Length; i++) d[i] = src[i] ? 0 : Far;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x, v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + 1);
                if (y > 0) { v = Mathf.Min(v, d[i - size] + 1); if (x > 0) v = Mathf.Min(v, d[i - size - 1] + 1); if (x < size - 1) v = Mathf.Min(v, d[i - size + 1] + 1); }
                d[i] = v;
            }
        for (int y = size - 1; y >= 0; y--)
            for (int x = size - 1; x >= 0; x--)
            {
                int i = y * size + x, v = d[i];
                if (x < size - 1) v = Mathf.Min(v, d[i + 1] + 1);
                if (y < size - 1) { v = Mathf.Min(v, d[i + size] + 1); if (x < size - 1) v = Mathf.Min(v, d[i + size + 1] + 1); if (x > 0) v = Mathf.Min(v, d[i + size - 1] + 1); }
                d[i] = v;
            }
        return d;
    }

    // Opaque px of the damaged drawing farther than 14 px (cell 384) outside its own pose's silhouette.
    static int OutsidePose(Color32[] dmg, Color32[] pose, int cell)
    {
        // 48 px (cell 384): loose enough for floating shards / vapour puffs the redraw moves a little, tight enough
        // that a second, offset hull would show up
        var near = ChebDist(AlphaMask(pose, 40), cell);
        int reach = Mathf.Max(1, Mathf.RoundToInt(48f * cell / 384f)), n = 0;
        for (int i = 0; i < dmg.Length; i++) if (dmg[i].a > 40 && near[i] > reach) n++;
        return n;
    }

    // Semi-transparent px (alpha 8..200) deep inside the drawing: a second, faded hull layered over the first.
    static int InnerTranslucent(Color32[] px, int cell)
    {
        var solid = AlphaMask(px, 40);
        var hole = new bool[px.Length];
        for (int i = 0; i < hole.Length; i++) hole[i] = !solid[i];
        var edge = ChebDist(hole, cell);          // distance to the nearest transparent px
        int deep = Mathf.Max(1, Mathf.RoundToInt(6f * cell / 384f)), n = 0;
        for (int i = 0; i < px.Length; i++) if (solid[i] && edge[i] > deep && px[i].a > 8 && px[i].a < 200) n++;
        return n;
    }

    // A textured 5 x 5 patch of the (wrong-pose) idle hull copied verbatim over a part this pose moved: the
    // second, offset hull of the Frost-boss bug. Flat colours (outline black, a plain fill) match by chance and
    // are ignored; a regenerated drawing never reproduces a textured patch pixel for pixel.
    static int GhostCopy(Color32[] dmg, Color32[] pose, Color32[] idle, int cell)
    {
        var hit = new bool[dmg.Length];
        for (int i = 0; i < dmg.Length; i++)
            hit[i] = dmg[i].a > 80 && idle[i].a > 80 && Diff(dmg[i], idle[i]) <= Slack && Diff(pose[i], idle[i]) > SameHull + Slack && Diff(dmg[i], pose[i]) > Slack;
        int n = 0;
        for (int y = 2; y < cell - 2; y++)
            for (int x = 2; x < cell - 2; x++)
            {
                int c = y * cell + x;
                if (!hit[c]) continue;
                bool all = true; int lo = 255, hi = 0;
                for (int dy = -2; dy <= 2 && all; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int j = c + dy * cell + dx;
                        if (!hit[j] && !(dmg[j].a > 80 && idle[j].a > 80 && Diff(dmg[j], idle[j]) <= Slack)) { all = false; break; }
                        int v = Mathf.Max(idle[j].r, Mathf.Max(idle[j].g, idle[j].b));
                        lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                    }
                if (all && hi - lo > 80) n++;
            }
        return n;
    }

    // Intersection over union of the two drawings' opaque silhouettes.
    static float SilhouetteOverlap(Color32[] a, Color32[] b)
    {
        int inter = 0, union = 0;
        for (int i = 0; i < a.Length; i++)
        {
            bool x = a[i].a > 40, y = b[i].a > 40;
            if (x && y) inter++;
            if (x || y) union++;
        }
        return union == 0 ? 1f : inter / (float)union;
    }

    static void DamagedAttackPosesAreOneFrame()
    {
        foreach (var boss in BossCatalog.All)
        {
            if (!BossArt.HasDamageArt(boss)) continue;
            var poses = BossArt.AttackDamagePoses(boss);
            int cell = Cell(boss);
            var idle = CellPixels(BossArt.Body(boss, BossArt.IdleStart(boss)), cell);
            int ghost = 0, alphaOff = 0, checkedPoses = 0, flat = 0, soft = 0, drift = 0;
            for (int p = 0; p < poses.Length && idle != null; p++)
            {
                var pose = CellPixels(BossArt.Body(boss, poses[p]), cell);
                if (pose == null) continue;
                var seen = new HashSet<Sprite>();
                for (int stage = 1; stage <= BossArt.DamageStages; stage++)
                {
                    var sp = BossArt.AttackDamageBody(boss, poses[p], stage);
                    if (sp == null) { flat++; continue; }
                    // one drawing per (pose, stage): never the same sprite for two stages, never an atlas strip
                    if (!seen.Add(sp) || sp.rect.width != cell || sp.rect.height != cell) flat++;
                    var dmg = CellPixels(sp, cell);
                    if (dmg == null) continue;
                    checkedPoses++;
                    ghost += GhostCopy(dmg, pose, idle, cell);
                    alphaOff += OutsidePose(dmg, pose, cell);
                    if (SilhouetteOverlap(dmg, pose) < .78f) drift++;
                    if (InnerTranslucent(dmg, cell) > InnerTranslucent(pose, cell) * 1.6f + 300f * (cell / 384f) * (cell / 384f)) soft++;
                }
            }
            Check(boss.artKey + ": every damaged attack pose is one " + cell + "px drawing per stage (" + flat + " bad)", flat == 0);
            Check(boss.artKey + ": damaged attack poses stay inside their pose's own silhouette, no stray ghost hull outside it (" + alphaOff + " px outside)", checkedPoses > 0 && alphaOff == 0);
            Check(boss.artKey + ": damaged attack poses carry no translucent second layer inside the hull (" + soft + " drawings)", checkedPoses > 0 && soft == 0);
            Check(boss.artKey + ": every damaged attack drawing keeps its pose (silhouette overlap >= 78%, " + drift + " drifted)", checkedPoses > 0 && drift == 0);
            Check(boss.artKey + ": damage never stamps idle-hull pixels over parts the pose moved (" + ghost + " ghost px)", checkedPoses > 0 && ghost == 0);
        }
    }

    // ---- one boss drawing, every boss x every heart count x every pose --------
    //
    // A live BossActor is stepped through its states (idle, every tell / fire drawing of the song's per-wave
    // cadence, every attack of its rotation, hit flash, entrance, death, retreat) at each heart count from full
    // down to one. Every step is a census of the actor's renderers: exactly ONE boss drawing (the Body renderer)
    // is up with alpha, the only others are the hit-flash silhouette OF that same sprite and the smoke / arcs fx
    // sheets; the Body sprite is the drawing for its (pose, damage stage) -- a damaged boss never shows a
    // pristine pose or hull (an "old pose lingering"). Pixel level: no damaged attack drawing carries idle-hull
    // pixels over parts its pose moved, nor a silhouette other than its pose's
    // (0 ghost px). BOSSAUDIT_DOC=<path> also writes the boss x hearts x state table as markdown.
    sealed class Manual : IMusicBeatSource
    {
        public bool Active { get; set; } = true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment Moment = new MusicMoment { energy = .7f, bass = .7f, accent = .7f, kind = "drive" };
        public MusicMoment MusicNow => Moment;
        public MusicMoment MusicAhead(double beats) => Moment;
    }

    sealed class AuditRow
    {
        public int stage, steps, maxBodies, maxExtras, ghost, stale, badOverlay;
        public readonly SortedSet<string> states = new SortedSet<string>();
        public bool Pass => maxBodies == 1 && ghost == 0 && stale == 0 && badOverlay == 0;
    }

    static readonly Dictionary<Sprite, Color32[]> cellCache = new Dictionary<Sprite, Color32[]>();
    static Color32[] CachedCell(Sprite sp, int cell)
    {
        if (sp == null) return null;
        Color32[] px;
        if (!cellCache.TryGetValue(sp, out px)) cellCache[sp] = px = CellPixels(sp, cell);
        return px;
    }

    static readonly Dictionary<string, int> ghostCache = new Dictionary<string, int>();
    static int GhostPixels(BossDef boss, int frame, int stage)
    {
        if (stage <= 0 || !BossArt.HasDamageArt(boss)) return 0;
        string key = boss.artKey + "/" + frame + "/" + stage;
        int n;
        if (ghostCache.TryGetValue(key, out n)) return n;
        var sp = BossArt.AttackDamageBody(boss, frame, stage);
        int cell = Cell(boss);
        var dmg = CachedCell(sp, cell);
        var pose = CachedCell(BossArt.Body(boss, frame), cell);
        if (sp == null || dmg == null || pose == null) return ghostCache[key] = 0;
        var idleHull = CachedCell(BossArt.Body(boss, BossArt.IdleStart(boss)), cell);   // the pristine idle hull the old damage art was transferred from
        n = idleHull == null ? 0 : GhostCopy(dmg, pose, idleHull, cell);
        n += OutsidePose(dmg, pose, cell);   // opaque art well outside the pose's own silhouette = a second drawing
        if (InnerTranslucent(dmg, cell) > InnerTranslucent(pose, cell) * 1.6f + 300f * (cell / 384f) * (cell / 384f)) n++;
        return ghostCache[key] = n;
    }

    static string StateName(BossDef boss, int frame, bool flash)
    {
        string s;
        int col = BossArt.AttackDamageColumn(boss, frame);
        if (col >= 0)
        {
            var names = BossArt.HasExpandedCombat(boss)
                ? new[] { "tell0_0", "tell0_1", "tell0_2", "tell1_0", "tell1_1", "tell1_2", "tell2_0", "tell2_1", "tell2_2" }
                : new[] { "tell0_0", "tell0_1", "tell1_0", "tell1_1", "tell2_0", "tell2_1", "fire" };
            s = names[col];
        }
        else if (BossArt.IsIdleFrame(frame)) s = "idle";
        else if (frame == BossArt.Hit) s = "hit";
        else if (frame >= BossArt.Death0 && frame < BossArt.Death0 + BossArt.DeathFrames) s = "death";
        else if (frame >= BossArt.Retreat0 && frame < BossArt.Retreat0 + BossArt.RetreatFrames) s = "retreat";
        else s = "frame" + frame;
        return flash ? s + "+flash" : s;
    }

    // One census of the actor, recorded into `row`.
    static void Census(BossActor a, BossDef boss, AuditRow row, string label, ref string firstFail)
    {
        var body = Body(a);
        int bodies = 0, extras = 0;
        string why = null;
        foreach (var sr in a.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!sr.enabled || sr.sprite == null || sr.color.a <= 0f) continue;
            if (sr.transform.parent != a.transform) continue;   // the hearts ring and its outlines are not the boss's drawing
            string n = sr.name;
            if (n == "Body") bodies++;
            else if (n == "HitFlash")
            {
                extras++;
                if (sr.sprite != body.sprite) { row.badOverlay++; why = "flash silhouette is not the body sprite"; }
            }
            else if (n == "DamageSmoke" || n == "DamageArcs")
            {
                extras++;
                if (sr.sprite.texture != BossArt.DamageFx(boss, 0).texture) { row.badOverlay++; why = n + " is not from the damage fx sheet"; }
            }
            else if (n.StartsWith("Charge") || n.StartsWith("Ring")) { }
            else { row.badOverlay++; why = "unexpected renderer " + n; }
        }
        row.maxBodies = Mathf.Max(row.maxBodies, bodies);
        row.maxExtras = Mathf.Max(row.maxExtras, extras);
        bool visible = body.enabled && body.sprite != null;
        if (!visible) { if (why != null && firstFail == null) firstFail = label + ": " + why; return; }
        if (bodies != 1) why = "body renderers up: " + bodies;
        int frame = a.BodyFrame, stage = a.DamageStage;
        row.states.Add(StateName(boss, frame, a.WhiteAlpha > 0f));
        row.steps++;
        // the drawing is the one for (pose, stage): a damaged boss never shows a pristine pose / hull
        if (stage > 0 && BossArt.HasDamageArt(boss) && a.State == BossActor.Mode.Fighting)
        {
            if (BossArt.IsIdleFrame(frame))
            {
                int c0 = (Mathf.Min(stage, BossArt.DamageStages) - 1) * BossArt.DamageColumns;
                if (body.sprite != BossArt.DamageBody(boss, c0) && body.sprite != BossArt.DamageBody(boss, c0 + 1)) { row.stale++; why = "idle at stage " + stage + " is not the damaged hull"; }
            }
            else if (BossArt.AttackDamageColumn(boss, frame) >= 0)
            {
                var want = BossArt.AttackDamageBody(boss, frame, stage);
                if (want == null || body.sprite != want) { row.stale++; why = "pose " + frame + " at stage " + stage + " is not its damaged drawing"; }
                row.ghost = Mathf.Max(row.ghost, GhostPixels(boss, frame, stage));
            }
            else { row.stale++; why = "frame " + frame + " (not idle, not an attack pose) while damaged"; }
        }
        if (why != null && firstFail == null) firstFail = label + ": " + why;
    }

    static string Cells(AuditRow r) => r.maxBodies + " | " + r.maxExtras + " | " + r.ghost + " | " + r.stale + " | " + (r.Pass ? "pass" : "FAIL");

    static void EveryBossOneDrawing()
    {
        string doc = System.Environment.GetEnvironmentVariable("BOSSAUDIT_DOC");
        var md = new System.Text.StringBuilder();
        md.AppendLine("| boss | cell px | layout | hearts left | stage | states seen | max body drawings | max overlays | ghost px | stale poses | result |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        string firstFail = null;
        for (int w = 0; w < BossCatalog.All.Length; w++)
        {
            var boss = BossCatalog.All[w];
            FreshScene(w);
            var actor = BossActor.Spawn(boss);
            var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
            var music = new Manual();
            var player = new Vector3(0f, -3.2f, 0f);
            var all = new Dictionary<int, AuditRow>();
            var entrance = new AuditRow();
            actor.BeginArrival();
            for (int f = 0; f < 60 * 4; f++) { actor.StepArrival(Dt); Census(actor, boss, entrance, boss.artKey + " entrance", ref firstFail); }
            actor.BeginFight();
            int max = MaxHearts;
            double beat = 0;
            for (int left = max; left >= 1; left--)
            {
                var row = new AuditRow();
                all[left] = row;
                actor.SetHearts(left, actor.transform.position);
                row.stage = actor.DamageStage;
                string label = boss.artKey + " hearts " + left;
                // the song's per-wave tell / fire cadence, with hits landing
                actor.BeatOverride = music;
                actor.ForcedAttack = -1;
                for (int f = 0; f < 60 * 7; f++)
                {
                    beat += Dt / .4; music.BeatNow = beat;
                    actor.StepFight(Dt, Dt, player, .8f, pool);
                    pool.Step(Dt);
                    if (f % 150 == 40) actor.Flash(1f);
                    Census(actor, boss, row, label + " (music)", ref firstFail);
                }
                // every attack of its rotation on its own tell / fire cadence
                actor.BeatOverride = null;
                for (int at = 0; at < boss.attacks.Length; at++)
                {
                    actor.ForcedAttack = at;
                    for (int f = 0; f < 60 * 3; f++)
                    {
                        actor.StepFight(Dt, Dt, player, .8f, pool);
                        pool.Step(Dt);
                        if (f == 70) actor.Flash(1f);
                        Census(actor, boss, row, label + " (attack " + at + ")", ref firstFail);
                    }
                }
                actor.ForcedAttack = -1;
                pool.RecycleAll();
            }
            // death handoff at the last heart; a damaged boss retreating (a second actor)
            var death = new AuditRow { stage = actor.DamageStage };
            actor.BeginOutro(true);
            for (int f = 0; f < 60 * 3; f++) { actor.StepOutro(Dt, 0); Census(actor, boss, death, boss.artKey + " death", ref firstFail); }
            var actor2 = BossActor.Spawn(boss);
            actor2.BeginFight();
            actor2.SetHearts(2, actor2.transform.position);
            var retreat = new AuditRow { stage = actor2.DamageStage };
            actor2.BeginOutro(false);
            for (int f = 0; f < 60 * 2; f++) { actor2.StepOutro(Dt, 0); Census(actor2, boss, retreat, boss.artKey + " retreat", ref firstFail); }

            string head = "| " + boss.artKey + " | " + Cell(boss) + " | " + (BossArt.HasExpandedCombat(boss) ? "35-cell" : "20-cell") + " | ";
            bool ok = entrance.Pass && death.maxBodies <= 1 && death.badOverlay == 0 && retreat.maxBodies <= 1 && retreat.badOverlay == 0;
            md.AppendLine(head + "entrance | 0 | " + string.Join(", ", entrance.states) + " | " + Cells(entrance) + " |");
            for (int left = max; left >= 1; left--)
            {
                var r = all[left];
                ok &= r.Pass;
                md.AppendLine(head + left + " | " + r.stage + " | " + string.Join(", ", r.states) + " | " + Cells(r) + " |");
            }
            md.AppendLine(head + "death handoff | " + death.stage + " | " + string.Join(", ", death.states) + " | " + Cells(death) + " |");
            md.AppendLine(head + "retreat (2 left) | " + retreat.stage + " | " + string.Join(", ", retreat.states) + " | " + Cells(retreat) + " |");
            Object.DestroyImmediate(actor.gameObject);
            Object.DestroyImmediate(actor2.gameObject);
            Check(boss.artKey + ": exactly one boss drawing, no ghost px, no stale pose at every heart count 1.." + max + ", entrance, death, retreat", ok);
        }
        Check("every state of every boss is one drawing" + (firstFail != null ? " (first failure: " + firstFail + ")" : ""), firstFail == null);
        if (!string.IsNullOrEmpty(doc)) System.IO.File.WriteAllText(doc, md.ToString());
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
        int c = Cell(em);
        float rk = c / 384f;
        var tex = Resources.Load<Texture2D>(BossArt.Folder + name + "_death");
        Check(name + ": deathKey set and " + name + "_death imports unscaled at " + 6 * c + "x" + c + " (" + (tex ? tex.width + "x" + tex.height : "missing") + ")",
              em.deathKey == name && BossArt.HasDeathArt(em) && tex != null && tex.width == 6 * c && tex.height == c);
        var idle = BossArt.Body(em, BossArt.IdleFrame(em, 0f));
        var seen = new HashSet<Sprite>();
        bool same = true, inside = true;
        var ibox = Box(idle);
        for (int i = 0; i < BossArt.DeathStripCells; i++)
        {
            var s = BossArt.DeathStrip(em, i);
            if (s == null || !seen.Add(s)) { same = false; continue; }
            // same scale, same anchor as the idle cell
            same &= s.rect.width == c && s.rect.height == c && Mathf.Abs(s.bounds.size.x - idle.bounds.size.x) < 1e-4f &&
                    Mathf.Abs(s.bounds.size.y - idle.bounds.size.y) < 1e-4f && s.pivot == idle.pivot && s.rect.x == i * c;
            var box = Box(s);
            // content is never empty and stays inside its own 384 cell (the idle art itself spans 7..382 of it)
            inside &= box != null && box[0] >= 0 && box[1] >= 0 && box[2] < c && box[3] < c &&
                      box[2] - box[0] <= (ibox[2] - ibox[0]) + Mathf.RoundToInt(16 * rk);
        }
        Check(name + " death strip:"+" 6 distinct 384px cells, same scale / pivot as the idle cell", same && seen.Count == 6);
        Check(name + " death strip:"+" every cell is drawn and sits inside its cell, no wider than the idle art", inside);
        // registration: the first cell is the idle pose bursting, so its body centre matches the idle cell's
        var b0 = Box(BossArt.DeathStrip(em, 0));
        float cx0 = (b0[0] + b0[2]) / 2f, cx1 = (ibox[0] + ibox[2]) / 2f;
        float cy0 = (b0[1] + b0[3]) / 2f, cy1 = (ibox[1] + ibox[3]) / 2f;
        Check(name + " death cell 0 is registered to the idle cell (centre off by " + Mathf.Abs(cx0 - cx1).ToString("0") + ", " +
              Mathf.Abs(cy0 - cy1).ToString("0") + " px of 384)", Mathf.Abs(cx0 - cx1) <= 40f * rk && Mathf.Abs(cy0 - cy1) <= 40f * rk);
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

    // The Tempest Engine's death strip: the stand-in key (BossCatalog.StormDeathKey, the Kraken's) until Storm_death.png exists, then its own.
    // Either way the strip is 6 cells of 384, distinct, and its first cell is registered to Storm's idle cell.
    static void StormDeathStrip()
    {
        var em = BossCatalog.ForWorld(5);
        bool own = System.IO.File.Exists("Assets/Art/Resources/Bosses/Storm_death.png");
        Check("Storm: deathKey is " + (own ? "Storm (its own strip is installed)" : "the stand-in " + BossCatalog.StormDeathKey) + ", damage key follows Storm_damage.png",
              em.deathKey == (own ? "Storm" : BossCatalog.StormDeathKey) && BossArt.HasDeathArt(em) &&
              em.damageKey == (System.IO.File.Exists("Assets/Art/Resources/Bosses/Storm_damage.png") && System.IO.File.Exists("Assets/Art/Resources/Bosses/Storm_damage_fx.png") ? "Storm" : BossCatalog.StormDamageKey));
        var seen = new HashSet<Sprite>();
        bool six = true;
        for (int i = 0; i < BossArt.DeathStripCells; i++)
        {
            var s = BossArt.DeathStrip(em, i);
            six &= s != null && seen.Add(s) && s.rect.width == 384f && s.rect.height == 384f;
        }
        Check("Storm death strip: 6 distinct 384 px cells", six && seen.Count == 6);
        if (own)
        {
            var b0 = Box(BossArt.DeathStrip(em, 0));
            var ibox = Box(BossArt.Body(em, BossArt.IdleFrame(em, 0f)));
            Check("Storm death cell 0 is registered to its idle cell", b0 != null && ibox != null &&
                  Mathf.Abs((b0[0] + b0[2]) / 2f - (ibox[0] + ibox[2]) / 2f) <= 40f && Mathf.Abs((b0[1] + b0[3]) / 2f - (ibox[1] + ibox[3]) / 2f) <= 40f);
        }
    }

    // The Violet Squall repaint is teal-slate / blue (mean blue clearly above mean red); the pre-repaint gunmetal was
    // near neutral (blue - red ~ 5). Every damaged Storm drawing (hull stages + each damaged attack pose) must keep the repaint.
    static void StormDamageKeepsRepaintColours()
    {
        var em = BossCatalog.ForWorld(5);
        int cell = Cell(em), bad = 0, n = 0;
        var sprites = new List<Sprite>();
        for (int stage = 1; stage <= BossArt.DamageStages; stage++)
        {
            var hull = BossArt.DamageIdleCell(em, BossArt.IdleStart(em), stage, 0f);
            foreach (int frame in BossArt.AttackDamagePoses(em)) sprites.Add(BossArt.AttackDamageBody(em, frame, stage));
            if (hull >= 0) sprites.Add(BossArt.DamageBody(em, hull));
        }
        foreach (var sp in sprites)
        {
            if (sp == null) { bad++; continue; }
            var px = CellPixels(sp, cell);
            if (px == null) { bad++; continue; }
            double r = 0, b = 0; int k = 0;
            foreach (var c in px) if (c.a > 200) { r += c.r; b += c.b; k++; }
            n++;
            if (k == 0 || (b - r) / k < 9.0) bad++;
        }
        Check("Storm: every damaged drawing keeps the Violet Squall repaint (" + n + " checked, " + bad + " grey / missing)", n > 0 && bad == 0);
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

    // The Dune-Eater (Desert): its own damage stages, damage fx and 6-cell death strip (Codex, thin pale border added by add_border_big.py).
    static void DesertOwnStrips()
    {
        var d = BossCatalog.ForWorld(6);
        Check("Desert: damage key and death key are Desert's own, both installed under Resources/Bosses",
              d.artKey == "Desert" && d.damageKey == "Desert" && d.deathKey == "Desert" && BossArt.HasDamageArt(d) && BossArt.HasDeathArt(d) &&
              System.IO.File.Exists("Assets/Art/Resources/Bosses/Desert_damage.png") && System.IO.File.Exists("Assets/Art/Resources/Bosses/Desert_damage_fx.png") &&
              System.IO.File.Exists("Assets/Art/Resources/Bosses/Desert_death.png"));
        var seen = new HashSet<Sprite>();
        bool six = true;
        for (int i = 0; i < BossArt.DeathStripCells; i++)
        {
            var s = BossArt.DeathStrip(d, i);
            six &= s != null && seen.Add(s) && s.rect.width == Cell(d) && s.rect.height == Cell(d);
        }
        Check("Desert death strip: 6 distinct cells of the atlas's own size (" + Cell(d) + " px)", six && seen.Count == 6);
        var b0 = Box(BossArt.DeathStrip(d, 0));
        var ibox = Box(BossArt.Body(d, BossArt.IdleFrame(d, 0f)));
        Check("Desert death cell 0 is registered to its idle cell", b0 != null && ibox != null &&
              Mathf.Abs((b0[0] + b0[2]) / 2f - (ibox[0] + ibox[2]) / 2f) <= 40f && Mathf.Abs((b0[1] + b0[3]) / 2f - (ibox[1] + ibox[3]) / 2f) <= 40f);
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
        Check("boss death sound hook: boss_<world> keys; every boss uses the shared roar",
              keys && EnemyDeathAudio.CategoryOf("boss_frost") == EnemyDeathAudio.Roar && EnemyDeathAudio.CategoryOf("boss_space") == EnemyDeathAudio.Roar);

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
