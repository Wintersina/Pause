using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: boss hits are felt (BossImpact) and a damaged boss stays damaged
// while it attacks (BossArt.AttackDamageBody).
//
//  * every boss x every attack pose x every damage stage resolves to a damaged
//    attack drawing (never the pristine body frame), the same size as the body
//    cell, on its own texture;
//  * a tell / fire pose in a fight wears its stage's damaged drawing;
//  * the shake peaks by weight and decays to 0 by 0.5 s; the per-boss impact
//    strips pool (6 renderers), end by 0.4 s, light hits are rate-limited, no
//    screen overlay exists; the seven strips load and differ; no hit-stop
//    is asked for; the boss flashes white.
public static class BossImpactTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSSIMPACT] PASS  " : "[BOSSIMPACT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EveryBossEveryAttackEveryStageIsDamaged();
            FightPosesWearTheirDamage();
            ShakeScalesAndDecays();
            HitsNeverSlowTime();
            BurstsPoolAndFade();
            BurstAndArtLoad();
            HitDrivesEverything();
        }
        finally { BossImpact.Dispose(); BossImpact.ClockOverride = null; }
        Debug.Log("[BOSSIMPACT] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossImpact.Dispose();
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
    }

    // ---- 7 bosses x poses x stages -------------------------------------------

    static void EveryBossEveryAttackEveryStageIsDamaged()
    {
        Check("seven bosses", BossCatalog.All.Length == 7);
        foreach (var b in BossCatalog.All)
        {
            int missing = 0, undamaged = 0, wrongSize = 0, combos = 0;
            var poses = BossArt.AttackDamagePoses(b);
            // every frame BossArt can pick for a tell / fire / attack pose
            var frames = new System.Collections.Generic.List<int>();
            for (int pose = 0; pose < 3; pose++)
            {
                for (int i = 0; i <= 20; i++) frames.Add(BossArt.TellFrame(b, pose, i / 20f));
                frames.Add(BossArt.FireFrame(b, pose));
            }
            foreach (var at in b.attacks) frames.Add(BossArt.AttackFrame(b, at));
            // the landing squash / retreat pose: for the 20-frame bosses it IS tell0's first drawing; a 35-cell boss
            // (Space, Tide) shows the base atlas's own cell 5, which has no damaged drawing (reported gap: only the retreat's first ticks)
            if (!BossArt.HasExpandedCombat(b)) frames.Add(BossArt.Tell(0, 0));
            foreach (int f in poses) frames.Add(f);
            foreach (int f in frames)
            {
                var body = BossArt.Body(b, f);
                for (int stage = 1; stage <= BossArt.DamageStages; stage++)
                {
                    combos++;
                    var s = BossArt.AttackDamageBody(b, f, stage);
                    if (s == null) { missing++; continue; }
                    if (s.texture == body.texture) undamaged++;
                    if (Mathf.RoundToInt(s.rect.width) != Mathf.RoundToInt(body.rect.width) || Mathf.RoundToInt(s.rect.height) != Mathf.RoundToInt(body.rect.height) ||
                        Mathf.Abs(s.pixelsPerUnit - body.pixelsPerUnit) > .01f) wrongSize++;
                }
                Check(b.artKey + ": stage 0 keeps the plain frame " + f, BossArt.AttackDamageBody(b, f, 0) == null);
            }
            Check(b.artKey + ": every attack pose x stage 1..4 has damaged art (" + combos + " combos, " + missing + " missing)", missing == 0 && combos > 0);
            Check(b.artKey + ": none falls back to the undamaged atlas (" + undamaged + ")", undamaged == 0);
            Check(b.artKey + ": damaged frames are the body cell's size and scale (" + wrongSize + " off)", wrongSize == 0);
        }
    }

    static BossEncounter StartFight(int world)
    {
        FreshScene();
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        return e;
    }

    // In a real fight, at stage 1 and 4, every attack-pose frame the actor shows is its damaged drawing.
    static void FightPosesWearTheirDamage()
    {
        foreach (var def in BossCatalog.All)
        {
            int world = System.Array.IndexOf(BossCatalog.All, def);
            var e = StartFight(world);
            var a = e.Actor;
            var body = a.transform.Find("Body").GetComponent<SpriteRenderer>();
            for (int i = 0; i < 10; i++) e.Step(1f / 60f, 1f);
            int hearts = Mathf.Max(1, BossConfig.Hearts);
            int poseFrames = 0, damaged = 0;
            for (int target = hearts - 1; target >= 2; target -= Mathf.Max(1, hearts / 3))
            {
                for (int i = 0; i < 40 && e.HeartsLeft > target; i++) e.OnShipAttackHit(BossConfig.HeartWeight * .5f);
                for (int i = 0; i < 480 && e.State == BossEncounter.Phase.Fight; i++)
                {
                    e.Step(1f / 60f, 1f);
                    if (BossArt.AttackDamageColumn(a.Boss, a.BodyFrame) < 0 || a.DamageStage <= 0) continue;
                    poseFrames++;
                    if (body.sprite == BossArt.AttackDamageBody(a.Boss, a.BodyFrame, a.DamageStage)) damaged++;
                }
            }
            Check(def.artKey + ": tell / fire frames in a fight show the damage stage (" + damaged + "/" + poseFrames + ")", poseFrames > 0 && damaged == poseFrames);
        }
    }

    // ---- shake / hit-stop / cracks --------------------------------------------

    static void ShakeScalesAndDecays()
    {
        float light = BossImpact.ShakeAmplitude(1f / 14f, false, false), heavy = BossImpact.ShakeAmplitude(1f, false, false);
        float heart = BossImpact.ShakeAmplitude(1f, true, false), kill = BossImpact.ShakeAmplitude(1f, true, true);
        Check("shake grows with weight: light " + light + " < heavy " + heavy + " < heart " + heart + " < kill " + kill,
              light > 0f && light < heavy && heavy < heart && heart < kill && kill <= BossImpact.ShakeMax);
        Check("a full hit shakes visibly (>= .25 world units)", heavy >= .25f);
        Check("envelope starts at the amplitude", Mathf.Approximately(BossImpact.ShakeEnvelope(heavy, 0f), heavy));
        Check("envelope is 0 at .5 s and after", BossImpact.ShakeEnvelope(heavy, .5f) == 0f && BossImpact.ShakeEnvelope(heavy, 1f) == 0f);
        bool mono = true;
        float prev = 99f;
        for (int i = 0; i <= 30; i++) { float v = BossImpact.ShakeEnvelope(heavy, i * .5f / 30f); mono &= v <= prev + 1e-6f; prev = v; }
        Check("envelope only falls", mono);

        FreshScene();
        BossImpact.ClockOverride = () => clockNow;
        var fx = BossImpact.Ensure();
        clockNow = 0f;
        fx.Trigger("Frost", Vector3.zero, 1f, false, false);
        Check("trigger starts the shake at the heavy amplitude", Mathf.Abs(fx.ShakeNow - heavy) < 1e-4f);
        float t = 0f;
        for (int i = 0; i < 28; i++) { fx.Step(.0166f); t += .0166f; }
        Check("shake is under a quarter of its peak at ~.46 s", fx.ShakeNow < heavy * .25f);
        for (int i = 0; i < 3; i++) fx.Step(.0166f);
        Check("shake has decayed to 0 by .5 s", fx.ShakeNow == 0f);
        // a second hit during a shake does not stack past the stronger one
        clockNow = 10f;
        fx.Trigger("Frost", Vector3.zero, 1f, false, false);
        fx.Step(.05f);
        fx.Trigger("Frost", Vector3.zero, 1f / 14f, false, false);
        Check("a light hit during a heavy shake does not restart it smaller", fx.ShakeNow > BossImpact.ShakeEnvelope(light, 0f));
        for (int i = 0; i < 40; i++) fx.Step(.0166f);
        // a gatling stream pulses: after the first light shake decays a new one starts
        clockNow = 20f;
        fx.Trigger("Frost", Vector3.zero, 1f / 14f, false, false);
        float first = fx.ShakeNow;
        fx.Step(.01f);
        fx.Trigger("Frost", Vector3.zero, 1f / 14f, false, false);
        Check("an equal light hit right after does not re-peak the shake (no pile-up)", fx.ShakeNow < first);
    }

    static float clockNow;

    // A hit on a boss never touches the time scale: no hit-stop, heavy or light, kill or not.
    static void HitsNeverSlowTime()
    {
        FreshScene();
        clockNow = 100f;
        BossImpact.ClockOverride = () => clockNow;
        var fx = BossImpact.Ensure();
        fx.Trigger("Frost", Vector3.zero, 1f, false, true);
        fx.Trigger("Frost", Vector3.zero, 1f / 14f, false, false);
        Check("boss hits leave the time scale at 1", Mathf.Approximately(Time.timeScale, 1f));
    }

    static void BurstsPoolAndFade()
    {
        FreshScene();
        clockNow = 0f;
        BossImpact.ClockOverride = () => clockNow;
        var fx = BossImpact.Ensure();
        Check("no screen-space overlay canvas is built (the old crack overlay is gone)", fx.GetComponentInChildren<Canvas>(true) == null);
        for (int i = 0; i < 20; i++) { fx.Trigger("Frost", new Vector3(0f, 2f, 0f), 1f, false, false); fx.Step(.01f); }
        Check("twenty heavy hits never show more than the 6 pooled bursts (" + fx.BurstsShown + ")",
              fx.PoolSize == BossImpact.BurstPool && fx.BurstsShown <= BossImpact.BurstPool && fx.BurstsShown == BossImpact.BurstPool);
        for (int i = 0; i < 30; i++) fx.Step(.0166f);
        Check("all bursts are gone by ~.5 s (" + fx.BurstsShown + ")", fx.BurstsShown == 0);

        // size cap: even the biggest burst stays small on screen (1.5 world units of a 10-unit-high view)
        fx.Trigger("Frost", Vector3.zero, 1f, true, true);
        Check("the heaviest burst is capped (" + fx.LastBurstSize + " <= 1.6 units)", fx.LastBurstSize <= BossImpact.BurstBig * 1.08f + 1e-3f && BossImpact.BurstBig <= 1.6f);
        fx.Trigger("Frost", Vector3.zero, .1f, false, false);
        clockNow = 50f;
        fx.Trigger("Frost", Vector3.zero, .1f, false, false);
        Check("a light burst is smaller than a heavy one", fx.LastBurstSize < BossImpact.BurstSmall * 1.1f);

        // gatling: 60 light hits over 2 s
        FreshScene();
        clockNow = 0f;
        fx = BossImpact.Ensure();
        int maxShown = 0;
        for (int i = 0; i < 120; i++)
        {
            clockNow = i / 60f;
            fx.Trigger("Verdant", new Vector3(0f, 2f, 0f), 1f / 14f, false, false);
            fx.Step(1f / 60f);
            maxShown = Mathf.Max(maxShown, fx.BurstsShown);
        }
        Check("a 2 s gatling stream shows <= 3 bursts at once (" + maxShown + ") and bursts only ~13 times (" + fx.BurstsSpawned + ")",
              maxShown <= 3 && fx.BurstsSpawned >= 8 && fx.BurstsSpawned <= 16);
    }

    static void BurstAndArtLoad()
    {
        int n = 0, bad = 0;
        foreach (var s in BossImpact.AllSprites()) { n++; if (s == null) bad++; }
        Check("the seven boss impact strips load (" + n + " sprites, " + bad + " missing)", n == 7 * 12 && bad == 0);
        bool square = true, small = true;
        foreach (var k in BossImpact.Keys)
        {
            var c = BossImpact.Cell(k, 0);
            square &= c != null && Mathf.RoundToInt(c.rect.width) == 256 && Mathf.RoundToInt(c.rect.height) == 256 && Mathf.Abs(c.bounds.size.x - 1f) < .01f;
            var tex = Resources.Load<Texture2D>(BossArt.ImpactFolder + BossImpact.StripName(k));
            small &= tex != null && tex.width == 3072 && tex.height == 256;
        }
        Check("every strip is 3072 x 256: 12 square 256 px cells, 1 world unit at scale 1", square && small);
        Check("each boss has its own distinct strip (7 different textures)",
              BossImpact.Cell("Frost", 4).texture != BossImpact.Cell("Storm", 4).texture &&
              BossImpact.Cell("Ember", 4).texture != BossImpact.Cell("Desert", 4).texture &&
              BossImpact.Cell("Space", 4).texture != BossImpact.Cell("Tide", 4).texture &&
              BossImpact.Cell("Verdant", 4).texture != BossImpact.Cell("Space", 4).texture);
        Check("burst runs its 12 cells over .4 s", BossImpact.BurstCell(0f) == 0 && BossImpact.BurstCell(.39f) == 11 && BossImpact.BurstCell(.2f) == 6);
        // no pink outside Space: the strips' opaque pixels (hue 290-350 deg, sat > .35)
        foreach (var k in BossImpact.Keys)
        {
            if (k == "Space") continue;
            Check(k + " strip has no pink pixels (pink only in Space)", PinkPixels(k) == 0);
        }
    }

    // Opaque pink-ish pixels in a strip, read through a temporary readable copy (editor only).
    static int PinkPixels(string key)
    {
        var src = Resources.Load<Texture2D>(BossArt.ImpactFolder + BossImpact.StripName(key));
        if (src == null) return -1;
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        int n = 0;
        foreach (var c in tex.GetPixels32())
        {
            if (c.a < 128) continue;
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            if (h >= 290f / 360f && h <= 350f / 360f && sat > .35f && v > .3f) n++;
        }
        Object.DestroyImmediate(tex);
        return n;
    }

    // BossEncounter.OnShipAttackHit -> all of it, with a light and a heavy hit.
    static void HitDrivesEverything()
    {
        var e = StartFight(3);
        var a = e.Actor;
        clockNow = 500f;
        BossImpact.ClockOverride = () => clockNow;
        var fx = BossImpact.Ensure();
        e.OnShipAttackHit(1f / 14f);
        float lightShake = fx.ShakeNow;
        float lightWhite = a.WhiteAlpha;
        clockNow = 502f;
        for (int i = 0; i < 60; i++) fx.Step(.0166f);
        e.OnShipAttackHit(1f, a.transform.position);
        Check("a light boss hit shakes, bursts and flashes a little (" + lightShake + ")", lightShake > 0f && fx.BurstsShown >= 1);
        Check("a heavy boss hit shakes harder than a light one", fx.ShakeNow > lightShake * 1.5f);
        Check("a heavy hit shows a burst themed to this boss (" + fx.LastBurstKey + ") and the boss pops white", fx.BurstsShown >= 1 && fx.LastBurstKey == e.Boss.artKey);
        e.Step(1f / 60f, 1f);
        Check("the boss body flashes white after a hit (" + a.WhiteAlpha + " vs light " + lightWhite + ")", a.WhiteAlpha > .5f);
        for (int i = 0; i < 20; i++) e.Step(1f / 60f, 1f);
        Check("... and the flash is gone in ~.12 s", a.WhiteAlpha == 0f);
    }
}
