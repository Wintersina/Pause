using System.Collections.Generic;
using System.IO;
using UnityEngine;

// NO PINK OUTSIDE SPACE (user, 2026-10-11): "the only world that is supposed to have pink is Space".
// Every hostile attack of Frost / Verdant / Ember / Tide / Storm / Desert -- shots, beams, hazards, telegraphs, impacts, painted atlases and
// runtime tints -- wears that world's own cue colour (WorldCue). This suite is the guard:
//
//   1. the cue table: Space is pink, every other world's cue hue is clear of the pink band and of the player's red, and HostileShotPalette /
//      EnemyBehaviours.ShotColor / EliteDef.ShotColor all resolve to it
//   2. every painted hostile attack file of a non-Space world (Resources/Attacks/<World>, Bosses/<World>_shots + boss atlases + damage / death,
//      BossAttackFx rows) has < 1% pink-family pixels; Space's art keeps its pink (control)
//   3. every PROCEDURAL hazard / shot sprite (rings, strikes, jets, waves, lashes, motion bodies) of worlds 1..6 has no pink pixel, and carries
//      the world's cue colour (per-world presence); Space's still carries pink (control)
//
// Excluded on purpose: enemy bodies (Resources/Enemies, e.g. the Verdant lit mine atlas's thorn tips) and the boss name cards; both are listed
// in the report as follow-ups.
public static class NoPinkOutsideSpaceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NOPINK] PASS  " : "[NOPINK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    public const float MaxPaintedPinkShare = .01f;

    public static int Execute()
    {
        fails = 0;
        CueTable();
        PaintedArt();
        ProceduralSprites();
        Debug.Log("[NOPINK] failures: " + fails);
        return fails;
    }

    static bool PinkPixel(Color32 c)
    {
        // ANY visibly pinkish pixel: hue 280..352 at a chroma >= 9/255, not dark -- saturated magenta AND pale pinkish-white / lavender-white
        if (c.a <= 24) return false;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float d = h * 360f;
        return v > .25f && s * v >= .035f && d >= 280f && d < 352f;
    }

    // ---- 1 ----

    static void CueTable()
    {
        Check("Space's cue is pink (the one world that keeps it)", WorldCue.IsPink(WorldCue.Body(0)) && HostileShotPalette.InFamily(HostileShotPalette.Body(Color.white, 0)));
        for (int w = 1; w < WorldCue.Worlds; w++)
        {
            var body = WorldCue.Body(w);
            Color.RGBToHSV(body, out float h, out _, out _);
            bool clearOfRed = !HostileGlow.IsPlayerRed(body) && !HostileGlow.IsPlayerRed(WorldCue.Core(w)) && !HostileGlow.IsPlayerRed(WorldCue.Trace(w));
            Check(Worlds[w] + ": cue " + (h * 360f).ToString("F0") + " deg is not pink and not the player's red (body, trace, core)",
                  !WorldCue.IsPink(body) && !WorldCue.IsPink(WorldCue.Trace(w)) && !WorldCue.IsPink(WorldCue.Core(w)) && clearOfRed);
            Check(Worlds[w] + ": HostileShotPalette.Body(any source, world) is the world's cue, EnemyBehaviours.ShotColor too",
                  HostileShotPalette.Body(new Color(1f, .31f, .85f), w) == body && EnemyBehaviours.ShotColor(w) == body && HostileShotPalette.InFamily(body, w));
        }
        int pinkElite = 0; string which = "";
        foreach (var d in EliteCatalog.All)
        {
            if (d.WorldIndex <= 0) continue;
            if (WorldCue.IsPink(d.ShotColor) || WorldCue.IsPink(d.ShotCore)) { pinkElite++; which += d.key + " "; }
        }
        Check("no non-Space elite's shot / telegraph colour is pink (" + pinkElite + " " + which + ")", pinkElite == 0);
        int pinkRoster = 0; which = "";
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.Shoots || def.world <= 0) continue;
            var st = b.ShotStyle;
            if (WorldCue.IsPink(st.ShotColor) || WorldCue.IsPink(st.ShotCore)) { pinkRoster++; which += def.key + " "; }
        }
        Check("no non-Space roster shooter's shot style is pink (" + pinkRoster + " " + which + ")", pinkRoster == 0);
        bool bossOk = true; which = "";
        for (int w = 1; w < BossCatalog.All.Length; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var rim = HostileShotPalette.Trace(HostileShotPalette.Body(boss.flash, boss.WorldIndex));
            if (WorldCue.IsPink(rim) || WorldCue.IsPink(boss.flash)) { bossOk = false; which += boss.id + " "; }
        }
        Check("no non-Space boss's flash or shot rim is pink (" + which + ")", bossOk);
    }

    // ---- 2 ----

    static string Root => Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Art", "Resources");

    static Color32[] Load(string rel, out int w, out int h)
    {
        string path = Path.Combine(Root, rel);
        w = h = 0;
        if (!File.Exists(path)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        ImageConversion.LoadImage(t, File.ReadAllBytes(path));
        w = t.width; h = t.height;
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static float PinkShare(Color32[] px, int y0 = 0, int y1 = int.MaxValue, int width = 0)
    {
        int n = 0, p = 0;
        int from = Mathf.Max(0, y0) * width, to = Mathf.Min(px.Length, y1 == int.MaxValue ? px.Length : y1 * width);
        for (int i = from; i < to; i++)
        {
            if (px[i].a <= 24) continue;
            n++;
            if (PinkPixel(px[i])) p++;
        }
        return n == 0 ? 0f : p / (float)n;
    }

    static void PaintedArt()
    {
        var files = new List<string>();
        for (int w = 1; w < Worlds.Length; w++)
        {
            string n = Worlds[w];
            string dir = Path.Combine(Root, "Attacks", n);
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.png")) files.Add("Attacks/" + n + "/" + Path.GetFileName(f));
            foreach (var suffix in new[] { "", "_shots", "_damage", "_damage_fx", "_death" })
                if (File.Exists(Path.Combine(Root, "Bosses", n + suffix + ".png"))) files.Add("Bosses/" + n + suffix + ".png");
        }
        string worst = ""; float worstShare = 0f; int bad = 0;
        foreach (var rel in files)
        {
            var px = Load(rel, out int w, out int h);
            if (px == null) continue;
            float share = PinkShare(px, 0, int.MaxValue, w);
            if (share > worstShare) { worstShare = share; worst = rel; }
            if (share > MaxPaintedPinkShare) { bad++; Debug.Log("[NOPINK] pink art: " + rel + " " + (share * 100f).ToString("F1") + "%"); }
        }
        Check(files.Count + " painted hostile attack / boss files of Frost..Desert: none over " + (MaxPaintedPinkShare * 100f) + "% pink (worst " + (worstShare * 100f).ToString("F2") + "% " + worst + ")",
              bad == 0 && files.Count >= 30);

        // the shared boss attack fx sheet: 48 px rows, one per world (Space 0 .. Desert 6); only Space's row is pink
        var fx = Load("BossAttackFx/boss_attack_fx.png", out int fw, out int fh);
        if (fx != null)
        {
            int rows = Mathf.Max(1, fh / 48), badRows = 0; string rowShares = "";
            for (int r = 1; r < Mathf.Min(rows, 7); r++)
            {
                // (the sheet is stored bottom-up: row 0 in the file's top is the last in memory)
                int y0 = fh - (r + 1) * 48, y1 = fh - r * 48;
                float s = PinkShare(fx, y0, y1, fw);
                rowShares += " " + Worlds[r] + " " + (s * 100f).ToString("F1") + "%";
                if (s > MaxPaintedPinkShare) badRows++;
            }
            float space = PinkShare(fx, fh - 48, fh, fw);
            Check("boss attack fx sheet: no pink in the Frost..Desert rows (" + rowShares.Trim() + "); Space's row keeps its pink (" + (space * 100f).ToString("F0") + "%)", badRows == 0 && space > .3f);
        }
        else Check("boss attack fx sheet present", false);

        // control: Space's own laser kit and boss shots are pink (the rule is not "no pink anywhere")
        var sp = Load("Attacks/Space/space_attack_laser.png", out int sw, out int sh);
        Check("control: Space's painted laser kit still carries pink", sp != null && PinkShare(sp, 0, int.MaxValue, sw) > .3f);
    }

    // ---- 3 ----

    static IEnumerable<KeyValuePair<string, Sprite>> Procedural(int world)
    {
        AttackHazardArt.Forget();
        for (int bold = 0; bold < 2; bold++)
            for (int f = 0; f < 2; f++)
            {
                bool b = bold == 1;
                string t = " f" + f + (b ? " bold" : "");
                yield return new KeyValuePair<string, Sprite>("ring bar" + t, AttackHazardArt.RingBarProcedural(world, f, b));
                yield return new KeyValuePair<string, Sprite>("ring glyph" + t, AttackHazardArt.RingGlyph(world, f, b));
                yield return new KeyValuePair<string, Sprite>("gap marker" + t, AttackHazardArt.GapMarker(world, f, b));
                foreach (StrikeStyle st in System.Enum.GetValues(typeof(StrikeStyle)))
                {
                    yield return new KeyValuePair<string, Sprite>("column " + st + t, AttackHazardArt.ColumnProcedural(world, st, f, b, false));
                    yield return new KeyValuePair<string, Sprite>("column tip " + st + t, AttackHazardArt.ColumnProcedural(world, st, f, b, true));
                    yield return new KeyValuePair<string, Sprite>("glyph " + st + t, AttackHazardArt.Glyph(st, world, f, b, false));
                    yield return new KeyValuePair<string, Sprite>("burst " + st + t, AttackHazardArt.Burst(world, st, f, b));
                }
                yield return new KeyValuePair<string, Sprite>("lash link" + t, AttackHazardArt.LinkProcedural(world, f, b));
                yield return new KeyValuePair<string, Sprite>("lash tip" + t, AttackHazardArt.LashTip(world, f, b));
                yield return new KeyValuePair<string, Sprite>("lash root" + t, AttackHazardArt.LashRoot(world, f, b));
                yield return new KeyValuePair<string, Sprite>("lash dash" + t, AttackHazardArt.LashDash(world, f, b));
            }
        foreach (ShotMotionArt.Piece piece in System.Enum.GetValues(typeof(ShotMotionArt.Piece)))
            for (int f = 0; f < 2; f++)
                yield return new KeyValuePair<string, Sprite>("motion " + piece + " f" + f, ShotMotionArt.ProceduralSprite(piece, world, f));
    }

    static void ProceduralSprites()
    {
        for (int w = 0; w < WorldCue.Worlds; w++)
        {
            int n = 0, pink = 0, cue = 0;
            string first = "";
            foreach (var kv in Procedural(w))
            {
                if (kv.Value == null) continue;
                var px = ShieldContour.ReadPixels(kv.Value, out int sw, out int sh);
                if (px == null) continue;
                n++;
                int p = 0, c = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a <= 24) continue;
                    if (PinkPixel(px[i])) p++;
                    if (w > 0 && WorldCue.InFamily(px[i], w, 22f)) c++;
                }
                if (p > 0) { pink++; if (first.Length == 0) first = kv.Key; }
                if (c > 0) cue++;
            }
            if (w == 0) Check("control: Space's procedural hazard sprites are pink (" + pink + " of " + n + ")", n > 20 && pink > n / 2);
            else
            {
                Check(Worlds[w] + ": " + n + " procedural hazard / shot sprites, none with a pink pixel" + (first.Length > 0 ? " (first: " + first + ")" : ""), n > 20 && pink == 0);
                Check(Worlds[w] + ": the world's own cue colour is present in its sprites (" + cue + " of " + n + ")", cue >= n * 3 / 4);
            }
        }
    }
}
