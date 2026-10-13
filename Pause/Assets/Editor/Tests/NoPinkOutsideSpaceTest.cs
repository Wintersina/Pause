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
        MineChargeUp();
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

    // ---- 4: the rail mine's whole charge-up, as rendered ----
    // (user, 2026-10-13: the Verdant mine "still turns into a pink glow when it is about to shoot": the lit arming atlas was a pink repaint
    // remapped to a washed-out mint with salmon flecks.) Every drawing a non-Space mine wears from dormant to firing -- the Codex loop (idle steps
    // and the arming tell, the drawings the Codex entry cycles through), the in-game flipbook (waking / charging / burst), and the laser's own
    // charge-up and beam art (windup flare, aim line, muzzle, impact, haze) -- has < 1% pink pixels, and so does the tell's glow colour.

    static float SpritePink(Sprite s, out int opaque)
    {
        opaque = 0;
        if (s == null) return 0f;
        var px = ShieldContour.ReadPixels(s, out int w, out int h);
        if (px == null) return 0f;
        int p = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a <= 24) continue;
            opaque++;
            if (PinkPixel(px[i])) p++;
        }
        return opaque == 0 ? 0f : p / (float)opaque;
    }

    // The washed-out "rose / salmon" cast of a green or cyan mine's glow: light, red-dominant, not warm-yellow. (The old Verdant lit atlas's
    // pink-remapped arming frames: 3.3% of the charging frame; the clean drawing: < 1%.) Only for Frost and Verdant (Tide's ivory tusks are warm by design), whose bodies are
    // never red-dominant and light.
    static float SalmonShare(Sprite s)
    {
        var px = ShieldContour.ReadPixels(s, out int w, out int h);
        if (px == null) return 0f;
        int n = 0, p = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a <= 24) continue;
            n++;
            if (px[i].r > px[i].g && px[i].r >= 140 && px[i].b >= 70) p++;
        }
        return n == 0 ? 0f : p / (float)n;
    }
    public const float MaxSalmonShare = .02f;

    static void AddAll(List<KeyValuePair<string, Sprite>> into, string tag, IEnumerable<Sprite> sprites)
    {
        if (sprites == null) return;
        int i = 0;
        foreach (var s in sprites) { if (s != null) into.Add(new KeyValuePair<string, Sprite>(tag + i, s)); i++; }
    }

    static List<KeyValuePair<string, Sprite>> MineChargeSprites(int world)
    {
        var all = new List<KeyValuePair<string, Sprite>>();
        var def = EnemyRoster.One(world, EnemyRole.Mine);
        if (def == null) return all;
        var anim = CodexAnimations.Enemy(def);
        if (anim != null)
        {
            AddAll(all, "codex idle ", anim.idle);
            if (anim.tells != null) for (int t = 0; t < anim.tells.Length; t++) AddAll(all, "codex tell" + t + " ", anim.tells[t]);
        }
        AddAll(all, "flipbook ", EnemyArt.Frames(def));
        MineLaserArt.ResetCache();
        var art = MineLaserArt.For(world);
        if (art != null)
        {
            AddAll(all, "beam sight ", new[] { art.sight }); AddAll(all, "beam0 ", new[] { art.beam0, art.beam1 });
            AddAll(all, "flash ", art.flash); AddAll(all, "spark ", art.spark);
            if (art.flame != null)
            {
                AddAll(all, "pilot ", art.flame.pilot); AddAll(all, "flame flash ", art.flame.flash); AddAll(all, "flame spark ", art.flame.spark);
                AddAll(all, "flame haze ", art.flame.haze); AddAll(all, "flame beam ", art.flame.beam); AddAll(all, "flame sightB ", new[] { art.flame.sightB });
            }
            if (art.kit != null)
            {
                AddAll(all, "kit windup ", art.kit.windup); AddAll(all, "kit muzzle ", art.kit.muzzle); AddAll(all, "kit fade ", art.kit.fade);
                AddAll(all, "kit impact ", art.kit.impact); AddAll(all, "kit body ", art.kit.body); AddAll(all, "kit sight ", art.kit.sight); AddAll(all, "kit lock ", art.kit.lockOn);
            }
        }
        return all;
    }

    static void MineChargeUp()
    {
        for (int w = 0; w < WorldCue.Worlds; w++)
        {
            var sprites = MineChargeSprites(w);
            string worst = ""; float worstShare = 0f; int bad = 0;
            foreach (var kv in sprites)
            {
                float share = SpritePink(kv.Value, out int opaque);
                if (share > worstShare) { worstShare = share; worst = kv.Key; }
                if (share > MaxPaintedPinkShare) bad++;
            }
            if (w == 0)
            {
                Check("control: Space's mine charge-up still carries pink (worst " + (worstShare * 100f).ToString("F0") + "% " + worst + ", " + sprites.Count + " drawings)", sprites.Count > 10 && worstShare > .3f);
                continue;
            }
            Check(Worlds[w] + ": " + sprites.Count + " mine drawings (Codex loop + tell, flipbook, laser charge-up / beam): none over " + (MaxPaintedPinkShare * 100f) + "% pink (worst " + (worstShare * 100f).ToString("F2") + "% " + worst + ")",
                  sprites.Count >= 12 && bad == 0);
            if (w == 1 || w == 2)
            {
                string sw = ""; float ws = 0f;
                foreach (var kv in sprites)
                    if (kv.Key.StartsWith("codex tell") || kv.Key == "flipbook 4" || kv.Key == "flipbook 5")
                    {
                        float sh = SalmonShare(kv.Value);
                        if (sh > ws) { ws = sh; sw = kv.Key; }
                    }
                Check(Worlds[w] + ": no rose / salmon cast in the mine's arming (tell) drawings (worst " + (ws * 100f).ToString("F2") + "% " + sw + ")", ws <= MaxSalmonShare);
            }
            var glow = EnemyBehaviours.ShotColor(w);
            Check(Worlds[w] + ": the mine's charge light (EnemyBehaviours.ShotColor) is not pink", !WorldCue.IsPink(glow));
        }
    }
}
