using UnityEditor.SceneManagement;
using UnityEngine;

// BOSS TELEGRAPHS (BossPatternMarkers / BossTelegraphStyle): light dotted lines that thin out as the song gets busy.
//   1  the style curves fall with intensity (alpha, width, time shown, muzzle glow), the lane edge is the brightest line
//   2  a lane is a dotted PAIR on the gap's edges (tiled dotted sprite, one pooled renderer per edge), a spot a dotted ring
//   3  busy telegraphs are fainter than calm ones and appear later but still end on the same beat
//   4  flooding every slot stays under the weight cap
//   5  no pink outside Space
//   6  nothing allocated per frame (add + step)
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossTelegraphTest.Run
public static class BossTelegraphTest
{
    static int fails;
    static void Check(string what, bool ok) { Debug.Log("[TELE] " + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        Style();
        for (int w = 0; w < 7; w++) Markers(w);
        PreviewStyle();
        for (int w = 0; w < 7; w++) MajorPreviews(w);
        Debug.Log("[TELE] failures: " + fails);
        return fails;
    }

    static void Style()
    {
        bool falls = BossTelegraphStyle.EdgeAlpha(0f) > BossTelegraphStyle.EdgeAlpha(1f) && BossTelegraphStyle.AimAlpha(0f) > BossTelegraphStyle.AimAlpha(1f)
            && BossTelegraphStyle.Width(0f) > BossTelegraphStyle.Width(1f) && BossTelegraphStyle.VisibleSeconds(0f) > BossTelegraphStyle.VisibleSeconds(1f)
            && BossTelegraphStyle.MuzzleAlpha(0f) > BossTelegraphStyle.MuzzleAlpha(1f) && BossTelegraphStyle.MuzzleSize(0f) > BossTelegraphStyle.MuzzleSize(1f);
        Check("alpha, width, time shown and muzzle glow all fall with intensity", falls);
        Check("the gap lane edge is brighter than an aim line at every intensity", BossTelegraphStyle.EdgeAlpha(0f) > BossTelegraphStyle.AimAlpha(0f) && BossTelegraphStyle.EdgeAlpha(1f) > BossTelegraphStyle.AimAlpha(1f));
        Check("edges stay readable at a drop (alpha >= .15, width >= .03)", BossTelegraphStyle.EdgeAlpha(1f) >= .15f && BossTelegraphStyle.Width(1f) >= .03f);
        bool kinds = true;
        foreach (var k in new[] { BossAttackKind.Aimed, BossAttackKind.Fan, BossAttackKind.Lob, BossAttackKind.Pattern }) kinds &= !BossTelegraphStyle.MajorByKind(k);
        foreach (var k in new[] { BossAttackKind.Beam, BossAttackKind.Lash, BossAttackKind.Jet, BossAttackKind.Wave, BossAttackKind.Blast, BossAttackKind.Strike, BossAttackKind.Roll }) kinds &= BossTelegraphStyle.MajorByKind(k);
        Check("major by kind: lasers, vine lash and the area hazards yes; bullet attacks no", kinds);
        Check("the flag overrides the kind both ways", new BossAttack { kind = BossAttackKind.Aimed, telegraph = 1 }.IsMajor && !new BossAttack { kind = BossAttackKind.Beam, telegraph = 0 }.IsMajor);
        Check("the laser's own aim line eases down toward a drop but stays visible", BossTelegraphStyle.SightAlpha(1f) < BossTelegraphStyle.SightAlpha(0f) && BossTelegraphStyle.SightAlpha(1f) >= .5f);
        var tex =BossTelegraphStyle.Dash.texture;
        Check("the dotted texture has dots and gaps", tex.height == BossTelegraphStyle.DashPixels + BossTelegraphStyle.GapPixels
            && tex.GetPixel(0, 0).a > .9f && tex.GetPixel(0, BossTelegraphStyle.DashPixels).a < .1f);
    }

    // Step until something is shown (or the mark ends); returns frames stepped.
    static int Until(BossPatternMarkers mk, float dt, out bool shown)
    {
        int f = 0;
        shown = false;
        while (mk.Live > 0 && f < 600) { mk.Step(dt); f++; if (mk.Shown > 0) { shown = true; return f; } }
        return f;
    }

    static void Markers(int world)
    {
        string tag = "world " + world + ": ";
        using var sandbox = new TestHarness.Sandbox();
        var actor = BossActor.Spawn(BossCatalog.ForWorld(world));
        try
        {
            actor.BeginFight();
            var mk = actor.Markers;
            const float dt = 1f / 60f;
            var top = new Vector2(1f, 5f); var bottom = new Vector2(1f, -5f);
            float half = .9f, seconds = 1.6f;

            mk.Clear(); mk.Intensity = 0f; mk.Add(top, bottom, half, seconds);
            int frames = Until(mk, dt, out bool shown);
            var l = mk.FirstRenderer(0); var r = mk.SecondRenderer(0);
            Check(tag + "a calm lane shows a dotted pair (tiled dotted sprite on both edges)", shown && mk.ShownRenderers == 2 && l.drawMode == SpriteDrawMode.Tiled && r.drawMode == SpriteDrawMode.Tiled && l.sprite == BossTelegraphStyle.Dash && r.sprite == BossTelegraphStyle.Dash);
            Check(tag + "the safe lane stays marked: edges sit the gap's half width either side of its centre",
                  Mathf.Abs(Mathf.Abs(l.transform.position.x - r.transform.position.x) - half * 2f) < .01f && Mathf.Abs((l.transform.position.x + r.transform.position.x) * .5f - 1f) < .01f);
            float calmAlpha = mk.MaxShownAlpha;
            while (mk.Live > 0) mk.Step(dt);

            mk.Intensity = 1f; mk.Add(top, bottom, half, seconds);
            int busyFrames = Until(mk, dt, out bool busyShown);
            float busyAlpha = mk.MaxShownAlpha;
            Check(tag + "a busy lane is fainter (" + busyAlpha.ToString("F2") + " < " + calmAlpha.ToString("F2") + ") and shown later (" + busyFrames + " > " + frames + " frames), still shown",
                  busyShown && busyAlpha < calmAlpha && busyFrames > frames && busyAlpha >= .15f);
            int total = busyFrames;
            while (mk.Live > 0 && total < 1000) { mk.Step(dt); total++; }
            Check(tag + "it still ends on the original beat (" + total + " frames ~ " + (seconds / dt).ToString("F0") + ")", Mathf.Abs(total - seconds / dt) <= 2);

            // a spot is a ring of dots, fainter than a lane edge
            mk.Clear(); mk.Intensity = 0f; mk.Add(Vector2.zero, Vector2.zero, .38f, 1f);
            Until(mk, dt, out bool spot);
            Check(tag + "a spot is a ring of dots, fainter than a lane edge", spot && mk.IsRing(0) && mk.FirstRenderer(0).sprite == BossTelegraphStyle.Ring && mk.MaxShownAlpha < calmAlpha);

            // flood: every slot at a calm section, weight and alpha stay capped
            mk.Clear(); mk.Intensity = 0f;
            for (int i = 0; i < BossPatternMarkers.Max; i++) mk.Add(new Vector2(-4f + i * .5f, 5f), new Vector2(-4f + i * .5f, -5f), half, 1.5f);
            for (int i = 0; i < 30; i++) mk.Step(dt);
            Check(tag + "flooding all " + BossPatternMarkers.Max + " slots stays under the weight cap (" + mk.Weight.ToString("F1") + " <= " + BossTelegraphStyle.MaxTotalWeight + ")", mk.Live > 0 && mk.Weight <= BossTelegraphStyle.MaxTotalWeight + .001f && mk.MaxShownAlpha <= BossTelegraphStyle.EdgeAlphaCalm);

            // colour: never pink outside Space
            mk.Clear(); mk.Intensity = 0f; mk.Add(top, bottom, half, 1f);
            Until(mk, dt, out _);
            var c = mk.FirstRenderer(0).color;
            bool pink = c.g < Mathf.Min(c.r, c.b) - .15f;
            Check(tag + "telegraph colour " + c + (world == 0 ? " (Space: anything goes)" : " is not pink"), world == 0 || !pink);

            // which attacks get an overlay: none of the bullet attacks, only an attack opted in as major
            var cur = typeof(BossActor).GetField("current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var host = (IPatternHost)actor;
            mk.Clear(); mk.Suppressed = 0;
            BossAttack pattern = null, beam = null;
            foreach (var a in BossCatalog.ForWorld(world).attacks) { if (a.kind == BossAttackKind.Pattern && pattern == null) pattern = a; if (a.kind == BossAttackKind.Beam && beam == null) beam = a; }
            if (pattern != null)
            {
                cur.SetValue(actor, pattern);
                host.Mark(top, bottom, half, 1f); host.Mark(Vector2.zero, Vector2.zero, .3f, 1f);
                Check(tag + "an ordinary bullet pattern's lane / spot telegraphs are dropped (" + mk.Suppressed + " dropped, " + mk.Live + " live)", mk.Live == 0 && mk.Suppressed == 2);
                var major = new BossAttack { name = "major pattern", kind = BossAttackKind.Pattern, telegraph = 1 };
                cur.SetValue(actor, major);
                host.Mark(top, bottom, half, 1f);
                Check(tag + "an attack flagged major gets its dotted lane", mk.Live == 1 && major.IsMajor);
                mk.Clear();
            }
            if (beam != null)
            {
                cur.SetValue(actor, beam);
                host.Mark(top, bottom, half, 1f);
                Check(tag + "a laser (own aim line) never gets the bullet lane overlay on top", mk.Live == 0 && beam.IsMajor);
            }
            cur.SetValue(actor, null);
            foreach (var a in BossCatalog.ForWorld(world).attacks)
                Debug.Log("[TELE] TABLE " + BossCatalog.ForWorld(world).name + " | " + a.name + " | " + a.kind + " | major=" + a.IsMajor);

            // allocations: add + step, warmed
            mk.Clear();
            for (int i = 0; i < 30; i++) { mk.Add(top, bottom, half, .3f); mk.Add(Vector2.zero, Vector2.zero, .3f, .3f); mk.Step(dt); }
            mk.Clear();
            long control;
            bool meter = TestHarness.AllocMeterWorks(out control);
            long bytes = TestHarness.AllocatedBytes(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    if (i % 20 == 0) { mk.Add(top, bottom, half, 1f); mk.Add(top, bottom, .08f, 1f); mk.Add(Vector2.one, Vector2.one, .3f, 1f); }
                    mk.Step(dt);
                }
            });
            Check(tag + "adding and stepping telegraphs allocates nothing (" + bytes + " bytes)", meter && bytes == 0);
        }
        finally { Object.DestroyImmediate(actor.gameObject); }
    }

    // The hazard preview (AttackPreview) softened for a boss: fewer, smaller, fainter dots toward a drop; unchanged otherwise.
    static void PreviewStyle()
    {
        var shape = new AttackShape();
        shape.AddQuad(new Vector2(0f, 0f), new Vector2(0f, -3f), 1f);
        var plain = AttackPreview.Show(shape, 1f, Color.white);
        int plainDots = plain.DotCount; float plainAlpha = AttackPreview.LastAlpha, plainSpacing = AttackPreview.LastSpacing; plain.End();
        var calm = AttackPreview.Show(shape, 1f, Color.white, false, 0f);
        int calmDots = calm.DotCount; float calmAlpha = AttackPreview.LastAlpha, calmDot = AttackPreview.LastDotSize; calm.End();
        var busy = AttackPreview.Show(shape, 1f, Color.white, false, 1f);
        int busyDots = busy.DotCount; float busyAlpha = AttackPreview.LastAlpha, busyDot = AttackPreview.LastDotSize, busySpacing = AttackPreview.LastSpacing; busy.End();
        Check("an enemy's preview is unchanged (alpha " + plainAlpha.ToString("F2") + ", spacing " + plainSpacing.ToString("F2") + ")", Mathf.Approximately(plainAlpha, AttackPreview.Alpha) && Mathf.Approximately(plainSpacing, AttackPreview.DotSpacing));
        Check("a boss's preview is lighter than an enemy's, and lighter again at a drop (alpha " + calmAlpha.ToString("F2") + " > " + busyAlpha.ToString("F2") + ", dots " + plainDots + " > " + calmDots + " > " + busyDots + ")",
              calmAlpha < plainAlpha && busyAlpha < calmAlpha && calmDots < plainDots && busyDots < calmDots && busyDot < calmDot && calmDot < AttackPreview.DotSize);
        Check("it stays dotted, never solid (gap between dots " + (busySpacing - busyDot).ToString("F2") + " u) and readable (alpha >= .2)", busySpacing > busyDot * 1.5f && busyAlpha >= .2f && busyDots >= 10);
    }

    // Every MAJOR attack's own preview in a live fight: dotted, light, never solid.
    static void MajorPreviews(int world)
    {
        using var sandbox = new TestHarness.Sandbox();
        var boss = BossCatalog.ForWorld(world);
        for (int a = 0; a < boss.attacks.Length; a++)
        {
            var atk = boss.attacks[a];
            if (atk.kind == BossAttackKind.Pattern || !atk.IsMajor) continue;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BossEncounter.ResetRun();
            var camGo = new GameObject("Main Camera", typeof(Camera)); camGo.tag = "MainCamera";
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
            BossEncounter.Begin(world, null);
            var e = BossEncounter.Instance;
            e.Step(.1f, 1f);
            for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
            e.Actor.ForcedAttack = a;
            AttackPreview.EndAll();
            int dots = 0; float spacing = 0f, alpha = 0f, dotSize = 0f, sightAlpha = -1f; bool dashed = false;
            for (int i = 0; i < 400 && e.Actor.AttacksStarted == 0; i++) e.Step(1f / 30f, 1f);
            for (int f = 0; f < 150; f++)
            {
                e.Step(1f / 30f, 1f);
                if (AttackPreview.ActiveCount > 0 && AttackPreview.DotsInUse > dots) { dots = AttackPreview.DotsInUse; spacing = AttackPreview.LastSpacing; alpha = AttackPreview.LastAlpha; dotSize = AttackPreview.LastDotSize; }
                foreach (var b in e.Pool.Beams) if (b != null && b.SightShown) { sightAlpha = Mathf.Max(sightAlpha, b.SightAlphaNow); dashed = b.SightDotted; }
            }
            string tag = boss.name + " / " + atk.name + " (" + atk.kind + "): ";
            if (atk.kind == BossAttackKind.Beam)
                Check(tag + "the laser's aim line is the painted dashed art, light (alpha " + sightAlpha.ToString("F2") + ")", dashed && sightAlpha > .5f && sightAlpha <= BossTelegraphStyle.SightAlphaCalm + .001f);
            else
                Check(tag + "the preview is dots, lighter than an enemy's (" + dots + " dots, alpha " + alpha.ToString("F2") + ", spacing " + spacing.ToString("F2") + ")",
                      dots >= 5 && alpha <= AttackPreview.Alpha * BossTelegraphStyle.PreviewAlphaCalm + .001f && spacing >= AttackPreview.DotSpacing * BossTelegraphStyle.PreviewSpacingCalm - .001f && spacing > dotSize * 1.5f);
            AttackPreview.EndAll();
            BossEncounter.ResetRun();
        }
    }
}
