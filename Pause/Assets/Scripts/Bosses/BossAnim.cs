using System.Collections.Generic;
using UnityEngine;

// The many-frame boss animation (user, 2026-11-28: "every boss to have a lot of frames ... for their attacks and
// movements"; "arms moving, jaws moving").
//
// Each boss's painted idle drawing is part-rigged by Art/Bosses/src~/anim/render_anim.py (limbs, jaws, wings,
// tentacles, turrets cut into layers and pivoted; the world's material FX painted on), every frame at the body
// atlas's cell size and registration. Packed, in this order, into
//
//   Resources/Bosses/<Key>_anim0.png, <Key>_anim1.png (no texture past 4096 px: Columns cells across, as many rows
//   as fit, the rest in the next sheet), cells left to right, top to bottom:
//
//     idle 0..11 | turnL 0..5 | turnR 0..5 | hit 0..2 | for each attack pose p = 0..PosesFor-1: tell 0..7, fire 0..3, recoil 0..3
//
// Every frame is Margin (1.25) cells square at the body's own pixel scale, centred on the body's cell, so a limb
// flung wide is never cut at the cell's edge (the sprite is Margin world units across where the body is 1).
// The 384-px bosses pack 7 frames across, the 576-px ones (Ember, Desert: 720-px frames) 5 (ColumnsFor).
//
// The attack poses: 0..2 are the drawn tells (BossAttack.tell); a boss may have more (the Bloom Queen: 0..2 play its
// vine attacks, 3..5 its stinger thorns, spore bloom and acid cannons), picked by BossAttack.anim (PoseFor), and
// the song's waves (which only know a tell) by TellPose.
//
// BossActor plays them while it fights: the hover loop; a turn each time its sway reverses (the limbs trail and
// swing through); every attack as tell (wound up over the attack's real telegraph) -> fire (from the moment the
// shots leave) -> recoil (settling back once the attack is over); a flinch when hit while idle. The legacy body
// frame (BossActor.BodyFrame: emitters, the codex, the arrival, death and retreat) is unchanged.
//
// Battle damage over the animation: Resources/Bosses/<Key>_anim_damage.png, laid out like <Key>_damage.png
// (2 x 4: column = the damaged idle loop's A / B, row = stage 1..4), holds only the damage itself (the damaged
// hull's pixels that differ from the pristine idle) over the rig's static core, so it stays registered whatever
// the limbs do.
public static class BossAnim
{
    public const int IdleFrames = 12, TurnFrames = 6, HitFrames = 3, TellFrames = 8, FireFrames = 4, RecoilFrames = 4;
    public const int Poses = 3, MaxPoses = 6;
    public const int PoseFrames = TellFrames + FireFrames + RecoilFrames;
    public const int Idle0 = 0, TurnL0 = Idle0 + IdleFrames, TurnR0 = TurnL0 + TurnFrames, Hit0 = TurnR0 + TurnFrames;
    public const int Pose0 = Hit0 + HitFrames;
    public const int Frames = Pose0 + Poses * PoseFrames;   // 75 (three poses)
    public const float Margin = 1.25f;

    // ---- per boss: the poses it has, the attack -> pose map, the packing -----------------------------------

    public static int PosesFor(BossDef boss) => boss != null && boss.artKey == "Verdant" ? 6 : Poses;
    public static int FramesFor(BossDef boss) => Pose0 + PosesFor(boss) * PoseFrames;
    // the painted body cell (px): the Cinder Drake and the Dune-Eater are drawn at 576, the rest at 384
    public static int CellPx(BossDef boss) => boss != null && (boss.artKey == "Ember" || boss.artKey == "Desert") ? 576 : 384;
    public static int FramePx(BossDef boss) => Mathf.RoundToInt(CellPx(boss) * Margin);
    public static int ColumnsFor(BossDef boss) => FramePx(boss) * Columns <= MaxTexture ? Columns : 5;
    public static int SheetsFor(BossDef boss)
    {
        int per = ColumnsFor(boss) * (MaxTexture / FramePx(boss));
        return (FramesFor(boss) + per - 1) / per;
    }
    public const int MaxTexture = 4096;

    // The pose an attack plays: its own (anim) when it has one, else its tell's.
    public static int PoseFor(BossDef boss, BossAttack attack)
    {
        if (attack == null) return 0;
        int n = PosesFor(boss);
        return attack.anim >= 0 && attack.anim < n ? attack.anim : TellPose(boss, attack.tell);
    }

    // A bare tell (the song's waves) -> its pose. The Bloom Queen's bulb rings and cannon hedges wear the new spore and
    // acid poses; the stinger's vine weaves keep the vine pose 0.
    static readonly int[] VerdantTells = { 0, 4, 5 };
    public static int TellPose(BossDef boss, int tell)
    {
        tell = Mathf.Clamp(tell, 0, Poses - 1);
        return boss != null && boss.artKey == "Verdant" ? VerdantTells[tell] : tell;
    }

    // Hold ticks at 24 fps (render_anim.py's moves.py writes the same numbers into its previews).
    public static readonly int[] IdleTicks = { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 };   // a 1.5 s hover
    public static readonly int[] TurnTicks = { 2, 2, 3, 3, 3, 3 };
    public static readonly int[] HitTicks = { 1, 2, 2 };
    public static readonly int[] FireTicks = { 1, 1, 2, 2 };
    public static readonly int[] RecoilTicks = { 2, 2, 3, 3 };
    // The tell is not ticked: its eight drawings are spread over the attack's own telegraph (tellSeconds, or the
    // song's beat-before), so the wind-up always lands exactly as the shots leave.

    public static float IdleSeconds => BossArt.Seconds(IdleTicks);
    public static float TurnSeconds => BossArt.Seconds(TurnTicks);
    public static float HitSeconds => BossArt.Seconds(HitTicks);
    public static float FireSeconds => BossArt.Seconds(FireTicks);
    public static float RecoilSeconds => BossArt.Seconds(RecoilTicks);
    // A volley within this of the last restarts nothing (the song's sixteenths would only ever show the first drawing).
    public static float FireRestartSeconds => (FireTicks[0] + FireTicks[1]) * BossArt.Tick;

    public static int Tell(int pose, int i) => Pose0 + Mathf.Clamp(pose, 0, MaxPoses - 1) * PoseFrames + Mathf.Clamp(i, 0, TellFrames - 1);
    public static int Fire(int pose, int i) => Pose0 + Mathf.Clamp(pose, 0, MaxPoses - 1) * PoseFrames + TellFrames + Mathf.Clamp(i, 0, FireFrames - 1);
    public static int Recoil(int pose, int i) => Pose0 + Mathf.Clamp(pose, 0, MaxPoses - 1) * PoseFrames + TellFrames + FireFrames + Mathf.Clamp(i, 0, RecoilFrames - 1);

    // The tell drawing `progress` (0..1) through the telegraph.
    public static int TellAt(int pose, float progress) =>
        Tell(pose, Mathf.Min(TellFrames - 1, Mathf.FloorToInt(Mathf.Clamp01(progress) * TellFrames)));
    public static int IdleAt(float seconds) => Idle0 + BossArt.FrameAt(IdleTicks, seconds, true);
    public static int TurnAt(int dir, float seconds) => (dir < 0 ? TurnL0 : TurnR0) + Mathf.Min(TurnFrames - 1, BossArt.FrameAt(TurnTicks, seconds, false));
    public static int HitAt(float seconds) => Hit0 + Mathf.Min(HitFrames - 1, BossArt.FrameAt(HitTicks, seconds, false));
    public static int FireAt(int pose, float seconds) => Fire(pose, Mathf.Min(FireFrames - 1, BossArt.FrameAt(FireTicks, seconds, false)));
    public static int RecoilAt(int pose, float seconds) => Recoil(pose, Mathf.Min(RecoilFrames - 1, BossArt.FrameAt(RecoilTicks, seconds, false)));

    // What a frame index is (tests, previews).
    public static bool IsIdle(int f) => f >= Idle0 && f < Idle0 + IdleFrames;
    public static bool IsTurn(int f) => f >= TurnL0 && f < Hit0;
    public static bool IsHit(int f) => f >= Hit0 && f < Pose0;
    public static int PoseOf(int f) => f < Pose0 ? -1 : (f - Pose0) / PoseFrames;
    public static int StepOf(int f) => f < Pose0 ? -1 : (f - Pose0) % PoseFrames;
    public static bool IsTell(int f) => f >= Pose0 && StepOf(f) < TellFrames;
    public static bool IsFire(int f) => f >= Pose0 && StepOf(f) >= TellFrames && StepOf(f) < TellFrames + FireFrames;
    public static bool IsRecoil(int f) => f >= Pose0 && StepOf(f) >= TellFrames + FireFrames;

    public const string Suffix = "_anim", DamageSuffix = "_anim_damage";
    // Every anim sheet is Columns cells across, so a cell is the texture's width / Columns whatever size the import
    // gave it (a platform's max-size override scales the sheet, never the layout).
    public const int Columns = 7;

    // ---- the sheets ---------------------------------------------------------------------------------------

    static readonly Dictionary<string, Sprite[]> sheets = new Dictionary<string, Sprite[]>();
    static readonly Dictionary<string, Sprite[]> decals = new Dictionary<string, Sprite[]>();
    static BossDef lastBoss, lastDecalBoss;
    static Sprite[] lastSheet, lastDecals;

    static Sprite[] Sheet(BossDef boss)
    {
        if (boss == null || string.IsNullOrEmpty(boss.artKey)) return null;
        if (ReferenceEquals(boss, lastBoss) && lastSheet != null && (lastSheet.Length == 0 || lastSheet[0] != null)) return lastSheet;
        string key = BossArt.Folder + boss.artKey + Suffix;
        Sprite[] sheet;
        if (!sheets.TryGetValue(key, out sheet) || sheet == null || (sheet.Length > 0 && sheet[0] == null))
        {
            sheet = Build(boss, key);
            sheets[key] = sheet;
        }
        lastBoss = boss; lastSheet = sheet;
        return sheet;
    }

    static Sprite[] Build(BossDef boss, string key)
    {
        int frames = FramesFor(boss), cols = ColumnsFor(boss);
        var list = new List<Sprite>(frames);
        for (int t = 0; t < 4 && list.Count < frames; t++)
        {
            var tex = Resources.Load<Texture2D>(key + t);
            if (tex == null) break;
            float cell = tex.width / (float)cols;
            int rows = Mathf.RoundToInt(tex.height / cell);
            // pixels per unit: a frame is Margin units across (the body's cell is 1), whatever size the import gave it
            for (int r = 0; r < rows && list.Count < frames; r++)
                for (int c = 0; c < cols && list.Count < frames; c++)
                    list.Add(UnityEngine.Sprite.Create(tex, new Rect(c * cell, tex.height - (r + 1) * cell, cell, cell),
                                           new Vector2(.5f, .5f), cell / Margin, 0, SpriteMeshType.FullRect));
        }
        return list.Count == frames ? list.ToArray() : new Sprite[0];
    }

    // Has this boss its animation atlas (all its frames)?
    public static bool Has(BossDef boss) { var s = Sheet(boss); return s != null && s.Length > 0 && s.Length == FramesFor(boss); }
    public static int FrameCount(BossDef boss) { var s = Sheet(boss); return s == null ? 0 : s.Length; }

    public static Sprite Sprite(BossDef boss, int frame)
    {
        var s = Sheet(boss);
        if (s == null || s.Length == 0) return null;
        return s[Mathf.Clamp(frame, 0, s.Length - 1)];
    }

    // The damage decal for `stage` (1..4) and the damaged idle loop's column (0, 1); null when pristine / not painted.
    public static Sprite Decal(BossDef boss, int stage, int column)
    {
        if (boss == null || stage <= 0 || !BossArt.HasDamageArt(boss)) return null;
        if (!ReferenceEquals(boss, lastDecalBoss) || lastDecals == null || (lastDecals.Length > 0 && lastDecals[0] == null))
        {
            string key = BossArt.Folder + boss.artKey + DamageSuffix;
            Sprite[] d;
            if (!decals.TryGetValue(key, out d) || d == null || (d.Length > 0 && d[0] == null))
            {
                var tex = Resources.Load<Texture2D>(key);
                float cell = tex != null ? tex.width / (float)BossArt.DamageColumns : 0f;
                d = tex == null || Mathf.Abs(tex.height - BossArt.DamageStages * cell) > 1f
                    ? new Sprite[0] : BuildDecals(tex, cell);
                decals[key] = d;
            }
            lastDecalBoss = boss; lastDecals = d;
        }
        int i = (Mathf.Min(stage, BossArt.DamageStages) - 1) * BossArt.DamageColumns + Mathf.Clamp(column, 0, BossArt.DamageColumns - 1);
        return i < lastDecals.Length ? lastDecals[i] : null;
    }

    static Sprite[] BuildDecals(Texture2D tex, float cell)
    {
        var d = new Sprite[BossArt.DamageCells];
        for (int r = 0; r < BossArt.DamageStages; r++)
            for (int c = 0; c < BossArt.DamageColumns; c++)
                d[r * BossArt.DamageColumns + c] = UnityEngine.Sprite.Create(tex, new Rect(c * cell, tex.height - (r + 1) * cell, cell, cell),
                                                                              new Vector2(.5f, .5f), cell, 0, SpriteMeshType.FullRect);
        return d;
    }

    // Resources the fight reads (BossArt.WarmPaths / QueueWarm).
    public static void WarmPaths(BossDef boss, List<string> into)
    {
        if (boss == null || string.IsNullOrEmpty(boss.artKey)) return;
        int n = SheetsFor(boss);
        for (int t = 0; t < n; t++) into.Add(BossArt.Folder + boss.artKey + Suffix + t);
        if (BossArt.HasDamageArt(boss)) into.Add(BossArt.Folder + boss.artKey + DamageSuffix);
    }

    public static void QueueWarm(BossDef boss, string k)
    {
        FirstUseWarm.Add(k + "anim", BossArt.Folder + boss.artKey + Suffix + "0", typeof(Texture2D), () => Sheet(boss));
        if (BossArt.HasDamageArt(boss))
            FirstUseWarm.Add(k + "animdamage", BossArt.Folder + boss.artKey + DamageSuffix, typeof(Texture2D), () => Decal(boss, 1, 0));
    }

    public static void Release()
    {
        sheets.Clear(); decals.Clear();
        lastBoss = null; lastSheet = null; lastDecalBoss = null; lastDecals = null;
    }

    public static int CachedSheets => sheets.Count + decals.Count;
}
