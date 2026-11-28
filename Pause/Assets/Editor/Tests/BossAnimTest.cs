using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: every boss animates with many frames (BossAnim; user, 2026-11-28: "every boss to have a lot of frames ...
// for their attacks and movements", "arms moving, jaws moving"). Each boss has its <Key>_anim atlas -- a 12-frame
// hover, a 6-frame turn each way, a 3-frame flinch, and for each of its attack poses (three; the Bloom Queen six) an
// 8-frame wind-up, a 4-frame release and a 4-frame recoil -- and plays it while it fights: every attack's tell is
// spread over the attack's real telegraph, the release starts as the shots leave, the recoil once the attack is over.
// Round 2 (user): the Tempest Engine's eye fires the beam and its coils the lightning; the Bloom Queen's vine attacks
// keep the first poses and its stinger / spore / acid attacks have their own.
//
// Drives BossEncounter frame by frame (Step(realDt, timeScale)) in edit mode.
public static class BossAnimTest
{
    static int fails;
    static void Check(string what, bool ok, string detail = null)
    {
        Debug.Log((ok ? "[BOSSANIM] PASS  " : "[BOSSANIM] FAIL  ") + what + (detail != null ? " -- " + detail : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            FrameTable();
            for (int w = 0; w < BossCatalog.All.Length; w++) AtlasComplete(BossCatalog.All[w]);
            for (int w = 0; w < BossCatalog.All.Length; w++) EveryAttackHasASequence(BossCatalog.All[w], w);
            OwnPoses();
            for (int w = 0; w < BossCatalog.All.Length; w++) NoPinkOutsideSpace(BossCatalog.All[w]);
            for (int w = 0; w < BossCatalog.All.Length; w++) MovesReallyMove(BossCatalog.All[w]);
            StepsAllocateNothing();
            for (int w = 0; w < 7; w++) FightPlaysTheSequences(w);
            for (int w = 0; w < 7; w++) DamageRidesTheAnimation(w);
        }
        finally
        {
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            Time.timeScale = 1f;
        }
        Debug.Log("[BOSSANIM] failures: " + fails);
        return fails;
    }

    const float Dt = 1f / 60f;

    // ---- the table ------------------------------------------------------

    static void FrameTable()
    {
        Check("hover loop of 12+ frames", BossAnim.IdleFrames >= 12 && BossAnim.IdleTicks.Length == BossAnim.IdleFrames);
        Check("turns of 6+ frames each way", BossAnim.TurnFrames >= 6 && BossAnim.TurnTicks.Length == BossAnim.TurnFrames);
        Check("flinch of 3+ frames", BossAnim.HitFrames >= 3 && BossAnim.HitTicks.Length == BossAnim.HitFrames);
        Check("every attack: tell 6+, release 4+, recoil 4+",
              BossAnim.TellFrames >= 6 && BossAnim.FireFrames >= 4 && BossAnim.RecoilFrames >= 4 &&
              BossAnim.FireTicks.Length == BossAnim.FireFrames && BossAnim.RecoilTicks.Length == BossAnim.RecoilFrames);
        Check("75 frames: idle | turnL | turnR | hit | 3 x (tell, fire, recoil)",
              BossAnim.Frames == 75 && BossAnim.Pose0 == 27 && BossAnim.Tell(2, 7) == 27 + 2 * 16 + 7 &&
              BossAnim.Fire(1, 0) == 27 + 16 + 8 && BossAnim.Recoil(0, 3) == 27 + 15);
        // fluid at 24 fps: no drawing held longer than 3 ticks in the fixed sequences, and the loop / turn / release /
        // recoil last what they say
        bool fluid = true;
        foreach (var t in new[] { BossAnim.IdleTicks, BossAnim.TurnTicks, BossAnim.HitTicks, BossAnim.FireTicks, BossAnim.RecoilTicks })
            foreach (int k in t) fluid &= k >= 1 && k <= 3;
        Check("every fixed drawing is held 1..3 ticks at 24 fps", fluid);
        Check("hover 1.5 s, turn .67 s, release .25 s, recoil .42 s",
              Mathf.Abs(BossAnim.IdleSeconds - 1.5f) < 1e-3f && Mathf.Abs(BossAnim.TurnSeconds - 16f / 24f) < 1e-3f &&
              Mathf.Abs(BossAnim.FireSeconds - .25f) < 1e-3f && Mathf.Abs(BossAnim.RecoilSeconds - 10f / 24f) < 1e-3f);
        // the frame-at-time helpers land on the right drawings
        Check("TellAt spreads the 8 wind-up drawings over the telegraph",
              BossAnim.TellAt(1, 0f) == BossAnim.Tell(1, 0) && BossAnim.TellAt(1, .5f) == BossAnim.Tell(1, 4) &&
              BossAnim.TellAt(1, .999f) == BossAnim.Tell(1, 7) && BossAnim.TellAt(1, 1f) == BossAnim.Tell(1, 7));
        Check("FireAt / RecoilAt step on their ticks and hold their last drawing",
              BossAnim.FireAt(0, 0f) == BossAnim.Fire(0, 0) && BossAnim.FireAt(0, 1.5f * BossArt.Tick) == BossAnim.Fire(0, 1) &&
              BossAnim.FireAt(0, 9f) == BossAnim.Fire(0, 3) && BossAnim.RecoilAt(2, 9f) == BossAnim.Recoil(2, 3));
        Check("frame kinds classify", BossAnim.IsIdle(3) && BossAnim.IsTurn(BossAnim.TurnR0 + 5) && BossAnim.IsHit(BossAnim.Hit0 + 2) &&
              BossAnim.IsTell(BossAnim.Tell(2, 7)) && BossAnim.IsFire(BossAnim.Fire(2, 0)) && BossAnim.IsRecoil(BossAnim.Recoil(1, 3)) &&
              BossAnim.PoseOf(BossAnim.Recoil(1, 3)) == 1);
    }

    static void AtlasComplete(BossDef b)
    {
        int frames = BossAnim.FramesFor(b);
        Check(b.artKey + ": has its animation atlas (" + BossAnim.FrameCount(b) + " of " + frames + " frames, " + BossAnim.PosesFor(b) + " poses)",
              BossAnim.Has(b) && BossAnim.FrameCount(b) == frames && frames >= BossAnim.Frames);
        if (!BossAnim.Has(b)) return;
        float cell = BossAnim.Sprite(b, 0).rect.width;
        bool size = true, drawn = true;
        for (int i = 0; i < frames; i++)
        {
            var s = BossAnim.Sprite(b, i);
            size &= s != null && Mathf.Abs(s.rect.width - cell) < .5f && Mathf.Abs(s.rect.height - cell) < .5f &&
                    Mathf.Abs(s.pixelsPerUnit * BossAnim.Margin - s.rect.width) < .01f;
            drawn &= Opaque(s) > cell * cell / 32f;
        }
        Check(b.artKey + ": every frame is one square cell, " + BossAnim.Margin + " units across (the body cell and its margin) and drawn", size && drawn);
        // the same registration as the body: the hover's first frame covers the idle drawing's own box (+-2% of a body
        // cell), the anim frame's box taken back to body-cell shares (its margin off each side)
        var ib = Frac(BossArt.Body(b, BossArt.IdleStart(b)));
        var ab = Frac(BossAnim.Sprite(b, BossAnim.Idle0));
        bool reg = ib != null && ab != null;
        float m = (BossAnim.Margin - 1f) * .5f;
        if (ab != null) for (int k = 0; k < 4; k++) ab[k] = ab[k] * BossAnim.Margin - m;
        for (int k = 0; reg && k < 4; k++) reg &= Mathf.Abs(ib[k] - ab[k]) <= .02f;
        Check(b.artKey + ": registered on the body (idle box " + Str(ib) + ", anim " + Str(ab) + ")", reg);
        if (BossArt.HasDamageArt(b))
        {
            bool decals = true;
            for (int st = 1; st <= BossArt.DamageStages; st++)
                for (int c = 0; c < BossArt.DamageColumns; c++) decals &= BossAnim.Decal(b, st, c) != null;
            Check(b.artKey + ": a damage decal for every stage and idle column", decals && BossAnim.Decal(b, 0, 0) == null);
        }
    }

    static void EveryAttackHasASequence(BossDef b, int w)
    {
        var all = new List<BossAttack>();
        foreach (var list in new[] { b.attacks, b.DefaultAttacks, b.hell, b.signatures, b.themed })
            if (list != null) all.AddRange(list);
        bool mapped = true;
        string bad = "";
        foreach (var a in all)
        {
            int p = BossAnim.PoseFor(b, a);
            bool ok = a.tell >= 0 && a.tell < BossAnim.Poses && p >= 0 && p < BossAnim.PosesFor(b) &&
                      BossAnim.PoseOf(BossAnim.Tell(p, 0)) == p && BossAnim.PoseOf(BossAnim.Recoil(p, 3)) == p;
            if (!ok) { mapped = false; bad += a.name + " "; }
        }
        Check(b.artKey + ": every attack (" + all.Count + ") plays its own tell -> fire -> recoil sequence", mapped, bad);
        // the song's waves name a pose per body part
        bool parts = true;
        int ww = BossEmitters.World(b);
        if (ww >= 0)
            for (int p = 0; p < BossEmitterTable.Parts[ww].Length; p++)
            {
                int pose = BossAnim.TellPose(b, BossWaveSources.PoseOf(b, p));
                parts &= pose >= 0 && pose < BossAnim.PosesFor(b);
            }
        Check(b.artKey + ": every wave source's pose has a sequence", parts);
    }

    // Round 2: the attacks wear the poses drawn for them.
    static void OwnPoses()
    {
        var storm = BossCatalog.Find("boss_storm");
        BossAttack beam = null, bolts = null;
        foreach (var a in storm.DefaultAttacks) { if (a.name == "eye beam") beam = a; if (a.name == "coil lightning") bolts = a; }
        Check("Storm: the eye beam is a beam from the eye (Core) on the eye's pose", beam != null && beam.kind == BossAttackKind.Beam &&
              beam.emitters.Length == 1 && beam.emitters[0] == "Core" && BossAnim.PoseFor(storm, beam) == 0);
        Check("Storm: the coil lightning is bolts from both coils on the coils' pose", bolts != null && bolts.style == BossShotStyle.Bolt &&
              bolts.emitters.Length == 2 && bolts.emitters[0] == "CoilL" && bolts.emitters[1] == "CoilR" && BossAnim.PoseFor(storm, bolts) == 2);
        var v = BossCatalog.Find("boss_verdant");
        var want = new Dictionary<string, int> { { "stinger thorns", 3 }, { "spore bloom", 4 }, { "acid cannons", 5 }, { "vine lash", 1 }, { "trunk toss", 2 } };
        var all = new List<BossAttack>();
        foreach (var list in new[] { v.attacks, v.themed }) if (list != null) all.AddRange(list);
        int seen = 0;
        bool ok = BossAnim.PosesFor(v) == 6;
        foreach (var a in all)
        {
            int p;
            if (!want.TryGetValue(a.name, out p)) continue;
            seen++;
            ok &= BossAnim.PoseFor(v, a) == p;
        }
        Check("Verdant: vine attacks on the vine poses (0-2), stinger / spore / acid on their own (3-5)", ok && seen >= 3);
        Check("Verdant: the song's stinger vines keep pose 0, its bulb rings and cannon hedges wear the spore and acid poses",
              BossAnim.TellPose(v, 0) == 0 && BossAnim.TellPose(v, 1) == 4 && BossAnim.TellPose(v, 2) == 5 && BossAnim.TellPose(storm, 2) == 2);
    }

    static void NoPinkOutsideSpace(BossDef b)
    {
        if (!BossAnim.Has(b) || b.artKey == "Space") return;
        long pink = 0, opaque = 0;
        for (int i = 0, n = BossAnim.FramesFor(b); i < n; i++)
        {
            int w, h;
            var px = ShieldContour.ReadPixels(BossAnim.Sprite(b, i), out w, out h);
            if (px == null) continue;
            foreach (var c in px)
            {
                if (c.a < 128) continue;
                opaque++;
                if (WorldCue.IsPink(c)) pink++;
            }
        }
        // (the GPU texture is block-compressed: a few edge texels may drift a degree or two; the art itself has none)
        Check(b.artKey + ": no pink in any frame (" + pink + " of " + opaque + " texels)", opaque > 0 && pink * 2000 <= opaque);
    }

    // The anatomy really moves: the tell's last drawing differs from its first over a good share of the body, the
    // hover's frames are not all one drawing, and a turn leans.
    static void MovesReallyMove(BossDef b)
    {
        if (!BossAnim.Has(b)) return;
        bool tells = true;
        string why = "";
        for (int p = 0; p < BossAnim.PosesFor(b); p++)
        {
            float d = Diff(BossAnim.Sprite(b, BossAnim.Tell(p, 0)), BossAnim.Sprite(b, BossAnim.Tell(p, 7)));
            float f = Diff(BossAnim.Sprite(b, BossAnim.Tell(p, 7)), BossAnim.Sprite(b, BossAnim.Fire(p, 0)));
            if (d < .02f || f < .02f) { tells = false; why += "pose " + p + " tell " + d.ToString("0.000") + " fire " + f.ToString("0.000") + "; "; }
        }
        Check(b.artKey + ": every wind-up builds and every release snaps (2%+ of the body changes)", tells, why);
        float idle = Diff(BossAnim.Sprite(b, 0), BossAnim.Sprite(b, 6));
        float turn = Diff(BossAnim.Sprite(b, BossAnim.TurnL0 + 2), BossAnim.Sprite(b, BossAnim.TurnR0 + 2));
        Check(b.artKey + ": the hover breathes (" + idle.ToString("0.000") + ") and the turns lean opposite ways (" + turn.ToString("0.000") + ")",
              idle > .005f && turn > .02f);
    }

    static void StepsAllocateNothing()
    {
        var b = BossCatalog.All[0];
        if (!BossAnim.Has(b)) { Check("allocation: no atlas", false); return; }
        long control;
        if (!TestHarness.AllocMeterWorks(out control)) { Check("the allocation meter sees allocations", false, "control " + control); return; }
        long bytes = TestHarness.AllocatedBytes(() =>
        {
            int sum = 0;
            for (int i = 0; i < 600; i++)
            {
                float t = i * Dt;
                sum += BossAnim.IdleAt(t) + BossAnim.TurnAt(i % 2 == 0 ? -1 : 1, t) + BossAnim.HitAt(t) + BossAnim.TellAt(i % 3, t % 1f) +
                       BossAnim.FireAt(i % 3, t) + BossAnim.RecoilAt(i % 3, t);
                if (BossAnim.Sprite(b, sum % BossAnim.Frames) == null) sum++;
                if (BossAnim.Decal(b, 1 + i % 4, i % 2) == null) sum++;
            }
        });
        Check("600 frames of stepping the animation and fetching its sprites allocate nothing", bytes == 0, bytes + " B");
    }

    // ---- in a fight -----------------------------------------------------

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

    // Each attack of the boss's rotation, forced in turn on the timed clock: its tell drawings climb 0 -> 7 over its
    // telegraph (the pose of its tell), the release is up the frame its first shot / beam / hazard leaves, and once
    // it is over the recoil settles into the hover. The body always shows the animation's sprite.
    static void FightPlaysTheSequences(int world)
    {
        var e = StartFight(world);
        var a = e.Actor;
        if (a == null || !BossAnim.Has(a.Boss)) { Check("world " + world + ": a fight with an animated boss", false); return; }
        var body = Body(a);
        string name = a.Boss.artKey;
        for (int k = 0; k < a.Boss.attacks.Length; k++)
        {
            var atk = a.Boss.attacks[k];
            int apose = BossAnim.PoseFor(a.Boss, atk);
            a.ForcedAttack = k;
            // let whatever is running finish, then catch this attack's tell
            int guard = 0;
            while (guard++ < 2400 && (a.CurrentAttack != null || !a.Telegraphing) && e.State == BossEncounter.Phase.Fight)
            {
                e.Step(Dt, 1f);
                if (a.CurrentAttack == atk && a.Telegraphing) break;
            }
            while (guard++ < 2400 && !(a.CurrentAttack == atk && a.Telegraphing) && e.State == BossEncounter.Phase.Fight) e.Step(Dt, 1f);
            if (!(a.CurrentAttack == atk && a.Telegraphing)) { Check(name + " " + atk.name + ": its tell came up", false); continue; }

            int last = -1, firstStep = -1;
            bool tellOk = true, sprites = true;
            float tellTime = 0f;
            while (a.Telegraphing && a.CurrentAttack == atk && tellTime < 5f)
            {
                int f = a.AnimFrame;
                tellOk &= BossAnim.IsTell(f) && BossAnim.PoseOf(f) == apose && BossAnim.StepOf(f) >= last;
                if (firstStep < 0) firstStep = BossAnim.StepOf(f);
                last = BossAnim.StepOf(f);
                sprites &= body.sprite == BossAnim.Sprite(a.Boss, f);
                e.Step(Dt, 1f);
                tellTime += Dt;
            }
            Check(name + " " + atk.name + ": the wind-up (pose " + apose + ") climbs " + firstStep + " -> " + last + " over its " +
                  atk.tellSeconds + " s telegraph (" + tellTime.ToString("0.00") + " s)",
                  tellOk && firstStep <= 1 && last == BossAnim.TellFrames - 1 && Mathf.Abs(tellTime - atk.tellSeconds) < .25f);
            int released = a.AnimFrame;
            Check(name + " " + atk.name + ": the release is up as it fires (" + released + ")",
                  BossAnim.IsFire(released) && BossAnim.PoseOf(released) == apose);
            // through to the end of the attack, then the recoil, then back to the hover / a turn
            bool sawRecoil = false, settled = false;
            for (int i = 0; i < 1200 && e.State == BossEncounter.Phase.Fight; i++)
            {
                e.Step(Dt, 1f);
                sprites &= body.sprite == BossAnim.Sprite(a.Boss, a.AnimFrame);
                if (a.CurrentAttack != null) continue;
                if (BossAnim.IsRecoil(a.AnimFrame) && BossAnim.PoseOf(a.AnimFrame) == apose) sawRecoil = true;
                if (sawRecoil && (BossAnim.IsIdle(a.AnimFrame) || BossAnim.IsTurn(a.AnimFrame))) { settled = true; break; }
            }
            Check(name + " " + atk.name + ": it recoils, then settles into the hover", sawRecoil && settled);
            Check(name + " " + atk.name + ": the body always draws the animation frame", sprites);
        }
        a.ForcedAttack = -1;

        // the sway turns it: a turn drawing shows within a sway period
        bool turned = false;
        for (int i = 0; i < 1500 && !turned && e.State == BossEncounter.Phase.Fight; i++) { e.Step(Dt, 1f); turned = BossAnim.IsTurn(a.AnimFrame); }
        Check(name + ": the sway reverses into a turn", turned);
        // a hit while idle flinches (when no attack owns the pose)
        bool flinch = false;
        for (int tries = 0; tries < 20 && !flinch && e.State == BossEncounter.Phase.Fight; tries++)
        {
            for (int i = 0; i < 400 && a.CurrentAttack != null; i++) e.Step(Dt, 1f);
            for (int i = 0; i < 30; i++) e.Step(Dt, 1f);
            if (a.CurrentAttack != null) continue;
            a.Flash(1f);
            flinch = BossAnim.IsHit(a.AnimFrame);
        }
        Check(name + ": a hit when idle shows the flinch", flinch);
    }

    // Battle damage rides the animation: the decal of the stage over the animated body, never the legacy damaged
    // drawings (the body is always the animation frame); gone on death.
    static void DamageRidesTheAnimation(int world)
    {
        var e = StartFight(world);
        var a = e.Actor;
        if (a == null || !BossArt.HasDamageArt(a.Boss) || !BossAnim.Has(a.Boss)) return;
        var body = Body(a);
        for (int i = 0; i < 10; i++) e.Step(Dt, 1f);
        Check(a.Boss.artKey + ": pristine: no decal", a.DamageDecal != null && !a.DamageDecal.enabled);
        int max = Mathf.Max(1, BossConfig.Hearts);
        for (int i = 0; i < 40 && e.HeartsLeft > 2; i++) e.OnShipAttackHit(BossConfig.HeartWeight * .5f);
        bool ok = true, anim = true;
        for (int i = 0; i < 240 && e.State == BossEncounter.Phase.Fight; i++)
        {
            e.Step(Dt, 1f);
            int stage = a.DamageStage;
            ok &= stage > 0 && a.DamageDecal.enabled &&
                  (a.DamageDecal.sprite == BossAnim.Decal(a.Boss, stage, 0) || a.DamageDecal.sprite == BossAnim.Decal(a.Boss, stage, 1));
            anim &= a.AnimFrame >= 0 && body.sprite == BossAnim.Sprite(a.Boss, a.AnimFrame);
        }
        Check(a.Boss.artKey + ": damaged (stage " + a.DamageStage + "): its decal rides over the animated body", ok && anim);
        Check(a.Boss.artKey + ": the decal sits on the body (same scale, just above it)",
              a.DamageDecal.transform.localScale == body.transform.localScale && a.DamageDecal.sortingOrder > body.sortingOrder);
    }

    // ---- pixels -----------------------------------------------------------

    static int Opaque(Sprite s)
    {
        int w, h;
        var px = ShieldContour.ReadPixels(s, out w, out h);
        if (px == null) return 0;
        int n = 0;
        foreach (var c in px) if (c.a > 24) n++;
        return n;
    }

    // Share of the union of both drawings' opaque texels that differ.
    static float Diff(Sprite x, Sprite y)
    {
        int w1, h1, w2, h2;
        var p = ShieldContour.ReadPixels(x, out w1, out h1);
        var q = ShieldContour.ReadPixels(y, out w2, out h2);
        if (p == null || q == null || w1 != w2 || h1 != h2) return 0f;
        int union = 0, diff = 0;
        for (int i = 0; i < p.Length; i++)
        {
            bool a = p[i].a > 24, b = q[i].a > 24;
            if (!a && !b) continue;
            union++;
            if (a != b || Mathf.Abs(p[i].r - q[i].r) + Mathf.Abs(p[i].g - q[i].g) + Mathf.Abs(p[i].b - q[i].b) > 60) diff++;
        }
        return union == 0 ? 0f : diff / (float)union;
    }

    static int[] Box(Sprite sp)
    {
        int w, h;
        var px = ShieldContour.ReadPixels(sp, out w, out h);
        if (px == null) return null;
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 24) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
        return x1 < 0 ? null : new[] { x0, y0, x1, y1 };
    }

    // The opaque box as shares of the cell (the body and anim sheets may import at different sizes).
    static float[] Frac(Sprite sp)
    {
        var b = Box(sp);
        if (b == null) return null;
        float w = sp.rect.width, h = sp.rect.height;
        return new[] { b[0] / w, b[1] / h, b[2] / w, b[3] / h };
    }

    static string Str(float[] b) => b == null ? "none" : b[0].ToString("0.000") + "," + b[1].ToString("0.000") + ".." + b[2].ToString("0.000") + "," + b[3].ToString("0.000");
}
