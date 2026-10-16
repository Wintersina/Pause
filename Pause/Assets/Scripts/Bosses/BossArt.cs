using System.Collections.Generic;
using UnityEngine;

// Slices the boss atlases rendered by Art/Bosses/src~/bosses.py.
//
//   Resources/Bosses/<Key>.png        body flipbook, 5 x 4 base cells; Space has 3 added painted rows
//     row 0  idle 0..3 | hit
//     row 1  tell0 a,b | tell1 a,b | fire
//     row 2  tell2 a,b | death 0..2
//     row 3  death 3..4 | retreat 0..1 | portrait (codex)
//   Resources/Bosses/<Key>_shots.png  8 x 1 cells
//     bolt 0..1 | shard 0..1 | lane telegraph | beam 0..1 | muzzle charge
//     (the bolt and shard cells also get a rim built from their alpha: ShotRim)
//   Resources/Bosses/<Key>_card.png   the intro name card
//   Resources/Bosses/warning.png      the intro warning slab
//
// Every cell sprite is 1 world unit across at scale 1 (pixelsPerUnit = cell
// width), so a renderer's localScale is directly its world size. Hold ticks
// at 24 fps, the same tables bosses.py writes into each SVG's header.
public static class BossArt
{
    public const string Folder = "Bosses/";
    // Body atlases are BodyColumns square cells across; the row count is read
    // from each texture (Frost/Verdant/Ember: 4 rows = 20 frames, Space: 7 rows
    // = 35 frames). BodyRows / BodyFrames are the largest sheet (Space's).
    public const int BodyColumns = 5, BodyRows = 7, BodyFrames = BodyColumns * BodyRows;
    public const int BaseBodyFrames = 20;   // frames 0..19, painted on every boss
    public const int ShotColumns = 8;

    // Flat body frame indices.
    public const int Idle0 = 0, IdleFrames = 4, Hit = 4;
    public const int Fire = 9;
    public const int Death0 = 12, DeathFrames = 5;
    public const int Retreat0 = 17, RetreatFrames = 2;
    public const int Portrait = 19;
    // New hand-painted Space rows: six engine-idle cells followed by three
    // cells for each weapon tell (chin, reactor, pod lances).
    public const int SpaceIdle0 = 20, SpaceIdleFrames = 6, SpaceTell0 = 26, SpaceTellFrames = 3;
    public static readonly int[] SpaceIdleTicks = { 2, 2, 2, 2, 2, 2 };
    public static int Tell(int pose, int frame)
    {
        pose = Mathf.Clamp(pose, 0, 2);
        frame = Mathf.Clamp(frame, 0, 1);
        return pose == 0 ? 5 + frame : pose == 1 ? 7 + frame : 10 + frame;
    }
    // Bosses with the 35-cell atlas (BossDef.expandedCombat: Space, Tide): six
    // idle drawings at 20..25 and three drawings per tell pose at 26..34.
    public static bool HasExpandedCombat(BossDef boss) => boss != null && boss.expandedCombat;
    public static int IdleFrame(BossDef boss, float seconds)
    {
        return HasExpandedCombat(boss)
            ? SpaceIdle0 + FrameAt(SpaceIdleTicks, seconds, true)
            : Idle0 + FrameAt(IdleTicks, seconds, true);
    }
    // The idle loop as data (BossActor plays it by FrameAt; the codex steps it):
    // how many drawings it has and the ticks each is held.
    public static int IdleCount(BossDef boss) => HasExpandedCombat(boss) ? SpaceIdleFrames : IdleFrames;
    public static int[] IdleTicksFor(BossDef boss) => HasExpandedCombat(boss) ? SpaceIdleTicks : IdleTicks;
    public static int IdleStart(BossDef boss) => HasExpandedCombat(boss) ? SpaceIdle0 : Idle0;
    public static int TellFrame(BossDef boss, int pose, float progress)
    {
        if (!HasExpandedCombat(boss)) return Tell(pose, progress < .34f ? 0 : 1);
        pose = Mathf.Clamp(pose, 0, 2);
        int stage = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(progress) * SpaceTellFrames), 0, SpaceTellFrames - 1);
        return SpaceTell0 + pose * SpaceTellFrames + stage;
    }
    public static int FireFrame(BossDef boss, int pose) =>
        HasExpandedCombat(boss) ? SpaceTell0 + Mathf.Clamp(pose, 0, 2) * SpaceTellFrames + SpaceTellFrames - 1 : Fire;
    public static int AttackFrame(BossDef boss, BossAttack attack) =>
        attack != null && attack.fireFrame ? FireFrame(boss, attack.tell) : TellFrame(boss, attack != null ? attack.tell : 0, 1f);
    // Death frames 0..2 sit at 12..14 and 3..4 at 15..16 (row-major), so the
    // flat index is simply Death0 + i.
    public static int Death(int i) => Death0 + Mathf.Clamp(i, 0, DeathFrames - 1);

    // ---- battle damage (bosses with a BossDef.damageKey: Space, Frost, Ember, Tide) ---
    //
    //   Resources/Bosses/<Key>_damage.png     2 x 4 cells: row r = damage
    //     stage r + 1 (4 .. 1 hearts left), cols = a 2-frame idle loop A,B;
    //     every cell registered to the pristine idle (Space: SpaceIdle0,
    //     the others: Idle0)
    //   Resources/Bosses/<Key>_damage_fx.png  6 x 2 cells, an overlay drawn
    //     over any body frame at the same registration: row 0 smoke loop,
    //     row 1 electrical arcs loop
    //
    // Stage = hearts lost (0 pristine .. 4 one heart left). The damaged hull
    // stands in for the idle pose only (tell / fire / hit / death / retreat
    // have no damaged drawings); the smoke and arcs go over every pose.
    public const int DamageStages = 4, DamageColumns = 2, DamageCells = DamageStages * DamageColumns;
    public const int DamageFxColumns = 6, DamageFxRows = 2, DamageFxCells = DamageFxColumns * DamageFxRows;
    public const int Smoke0 = 0, Arc0 = DamageFxColumns;
    // A and B each hold half the pristine idle loop, so the damaged loop
    // runs at the same period: Space's 6-frame engine idle is 0.5 s, the
    // 4-frame Idle0 loop (Frost) 0.625 s.
    public static readonly int[] DamageIdleTicks = { 6, 6 };
    public static readonly int[] PlainDamageIdleTicks = { 8, 7 };
    public static int[] DamageIdleTicksFor(BossDef boss) => HasExpandedCombat(boss) ? DamageIdleTicks : PlainDamageIdleTicks;
    public static readonly int[] SmokeTicks = { 3, 3, 3, 3, 3, 3 };
    public static readonly int[] ArcTicks = { 2, 2, 2, 2, 2, 2 };
    public const int ArcBurstTicks = 5;        // stage 3: arcs flicker on / off in slots this long
    public const float ArcBurstChance = .4f;   // ... this share of slots lit

    public static bool HasDamageArt(BossDef boss) => boss != null && !string.IsNullOrEmpty(boss.damageKey);
    // The hearts lost spread over the DamageStages looks: with 5 hearts one lost
    // is one stage (as it always was); with more, the same four looks are
    // spread evenly across the longer fight (12 hearts: 1-3 lost stage 1, 4-6
    // stage 2, 7-9 stage 3, 10+ stage 4). Never pristine once a heart is lost.
    public static int DamageStage(int maxHearts, int heartsLeft)
    {
        int lost = Mathf.Clamp(maxHearts - heartsLeft, 0, Mathf.Max(0, maxHearts));
        int span = Mathf.Max(1, maxHearts - 1);   // lost counts 1..span map to stages 1..4
        return Mathf.Clamp((lost * DamageStages + span - 1) / span, 0, DamageStages);
    }
    public static bool IsIdleFrame(int frame) =>
        (frame >= SpaceIdle0 && frame < SpaceIdle0 + SpaceIdleFrames) || (frame >= Idle0 && frame < Idle0 + IdleFrames);

    // The damaged hull cell standing in for body `frame`, or -1 (keep the
    // body frame): only a boss with damage art, only the idle pose, only
    // once damaged.
    public static int DamageIdleCell(BossDef boss, int frame, int stage, float seconds)
    {
        if (!HasDamageArt(boss) || stage <= 0 || !IsIdleFrame(frame)) return -1;
        return (Mathf.Min(stage, DamageStages) - 1) * DamageColumns + FrameAt(DamageIdleTicksFor(boss), seconds, true);
    }

    // ---- damaged attack poses (Resources/BossImpact/<Key>_attack_damage.png) --
    //
    // The damaged hull above only stands in for the idle pose; every tell /
    // fire pose has its own damaged drawing here, one column per pose (the
    // body-atlas frames of AttackDamagePoses, in that order), one row per
    // damage stage 1..4, cells registered to the body cells. So a
    // damaged boss keeps its damage while it telegraphs and fires.
    public const string ImpactFolder = "BossImpact/";
    static readonly int[] PlainAttackPoses = { 5, 6, 7, 8, 10, 11, 9 };      // tell0 a,b | tell1 a,b | tell2 a,b | fire
    static readonly int[] ExpandedAttackPoses = { 26, 27, 28, 29, 30, 31, 32, 33, 34 };   // three drawings per tell pose
    public static int[] AttackDamagePoses(BossDef boss) => HasExpandedCombat(boss) ? ExpandedAttackPoses : PlainAttackPoses;

    // The column of body frame `frame` in the boss's attack-damage atlas, or -1 (not an attack pose).
    public static int AttackDamageColumn(BossDef boss, int frame)
    {
        var poses = AttackDamagePoses(boss);
        for (int i = 0; i < poses.Length; i++) if (poses[i] == frame) return i;
        return -1;
    }

    // The damaged drawing of attack pose `frame` at damage `stage` (1..4), or
    // null: undamaged, no damage art, or not an attack pose (keep the body frame).
    public static Sprite AttackDamageBody(BossDef boss, int frame, int stage)
    {
        if (!HasDamageArt(boss) || stage <= 0) return null;
        int col = AttackDamageColumn(boss, frame);
        if (col < 0) return null;
        var sheet = AttackDamageSheet(boss, stage);
        int i = (Mathf.Min(stage, DamageStages) - 1) * AttackDamagePoses(boss).Length + col;
        return i < sheet.Length ? sheet[i] : null;
    }

    static readonly string[] PlainPoseNames = { "tell0_0", "tell0_1", "tell1_0", "tell1_1", "tell2_0", "tell2_1", "fire" };
    static readonly string[] ExpandedPoseNames = { "tell0_0", "tell0_1", "tell0_2", "tell1_0", "tell1_1", "tell1_2", "tell2_0", "tell2_1", "tell2_2" };

    // Resources/BossImpact/attack/<key>_<pose>_stage<N>.png: one file per pose and stage (cell-sized: 384 px, the Cinder
    // Drake's 576), loaded and cached stage by stage: only the stage the boss is actually showing is resident
    // (a stage's poses are ~3 MB at the Drake's size, all four ~12 MB).
    static readonly Dictionary<string, bool[]> attackStagesLoaded = new Dictionary<string, bool[]>();

    // How many damage stages of the boss's attack poses are loaded now (tests / diagnostics).
    public static int AttackDamageStagesLoaded(BossDef boss)
    {
        bool[] loaded;
        if (!attackStagesLoaded.TryGetValue(ImpactFolder + "attack/" + boss.damageKey, out loaded)) return 0;
        int n = 0;
        foreach (bool b in loaded) if (b) n++;
        return n;
    }

    // Drops the boss's cached attack-pose stages (tests; the next request reloads just its stage).
    public static void ForgetAttackDamage(BossDef boss)
    {
        string path = ImpactFolder + "attack/" + boss.damageKey;
        bodies.Remove(path);
        attackStagesLoaded.Remove(path);
    }

    static Sprite[] AttackDamageSheet(BossDef boss, int stage)
    {
        string path = ImpactFolder + "attack/" + boss.damageKey;
        var names = HasExpandedCombat(boss) ? ExpandedPoseNames : PlainPoseNames;
        Sprite[] sheet;
        bool[] loaded;
        if (!bodies.TryGetValue(path, out sheet) || sheet == null || sheet.Length != names.Length * DamageStages ||
            !attackStagesLoaded.TryGetValue(path, out loaded) || loaded == null)
        {
            sheet = new Sprite[names.Length * DamageStages];
            loaded = new bool[DamageStages];
            bodies[path] = sheet;
            attackStagesLoaded[path] = loaded;
        }
        int s = Mathf.Clamp(stage, 1, DamageStages) - 1;
        if (loaded[s]) return sheet;
        loaded[s] = true;
        for (int c = 0; c < names.Length; c++)
        {
            var tex = Resources.Load<Texture2D>(ImpactFolder + "attack/" + boss.damageKey.ToLowerInvariant() + "_" + names[c] + "_stage" + (s + 1));
            if (tex != null) sheet[s * names.Length + c] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), tex.width);
        }
        return sheet;
    }

    // The shared smoke schedule; SmokeAlpha(boss, stage) scales it by the
    // boss's smokeStrength.
    public static float SmokeAlpha(int stage) => stage >= 4 ? 1f : stage == 3 ? .7f : stage == 2 ? .35f : 0f;
    public static float SmokeAlpha(BossDef boss, int stage) =>
        Mathf.Clamp01(SmokeAlpha(stage) * (boss != null ? boss.smokeStrength : 1f));
    public static int SmokeCell(float seconds) => Smoke0 + FrameAt(SmokeTicks, seconds, true);

    // Arcs: none before stage 3; at 3 dim, intermittent bursts; at 4 always
    // on, full, jumping between frames at random.
    public static float ArcAlpha(int stage, float seconds)
    {
        if (stage >= 4) return 1f;
        if (stage < 3) return 0f;
        int slot = Mathf.FloorToInt(seconds / (ArcBurstTicks * Tick));
        return Hash01(slot) < ArcBurstChance ? .4f : 0f;
    }

    public static int ArcCell(int stage, float seconds)
    {
        if (stage < 4) return Arc0 + FrameAt(ArcTicks, seconds, true);
        int step = Mathf.FloorToInt(seconds / (2 * Tick));
        return Arc0 + Mathf.FloorToInt(Hash01(step * 7 + 3) * DamageFxColumns) % DamageFxColumns;
    }

    // Repeatable noise in [0, 1) (never disturbs UnityEngine.Random).
    static float Hash01(int n)
    {
        unchecked
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // Shot atlas cells.
    public const int Bolt0 = 0, Shard0 = 2, Telegraph = 4, Beam0 = 5, Charge = 7;

    public const float Tick = 1f / 24f;
    public static readonly int[] IdleTicks = { 6, 3, 3, 3 };
    public const int TellInTicks = 2;
    public const int FireTicks = 3;
    public const int HitTicks = 2;
    public static readonly int[] DeathTicks = { 2, 3, 3, 4, 6 };
    public static readonly int[] RetreatTicks = { 2, 2 };
    public static readonly int[] ShotTicks = { 2, 2 };
    public static readonly int[] BeamTicks = { 2, 2 };
    public const int TelegraphBlinkTicks = 3;

    public static float Seconds(int[] ticks)
    {
        int n = 0;
        foreach (int t in ticks) n += t;
        return n * Tick;
    }

    // The drawing showing `seconds` into a held-tick sequence; one past the
    // end once a non-looping sequence has run out.
    public static int FrameAt(int[] ticks, float seconds, bool loop)
    {
        float total = Seconds(ticks);
        if (loop && total > 0f) seconds = Mathf.Repeat(seconds, total);
        for (int i = 0; i < ticks.Length; i++)
        {
            seconds -= ticks[i] * Tick;
            if (seconds < 0f) return i;
        }
        return ticks.Length;
    }

    static readonly Dictionary<string, Sprite[]> bodies = new Dictionary<string, Sprite[]>();
    static readonly Dictionary<string, Sprite[]> shots = new Dictionary<string, Sprite[]>();
    static readonly Dictionary<string, Sprite> singles = new Dictionary<string, Sprite>();

    // squareCell > 0: cells are squareCell px both ways, rows counted from the
    // top of the texture (any leftover strip at the bottom is ignored).
    static Sprite[] Slice(Texture2D tex, int cols, int rows, int squareCell = 0)
    {
        var result = new Sprite[cols * rows];
        if (tex == null) return result;
        float w = tex.width / (float)cols, h = tex.height / (float)rows;
        if (squareCell > 0)
        {
            w = h = squareCell;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    result[r * cols + c] = Sprite.Create(tex, new Rect(c * w, tex.height - (r + 1) * h, w, h),
                                                         new Vector2(.5f, .5f), w);
            return result;
        }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[r * cols + c] = Sprite.Create(tex, new Rect(c * w, (rows - 1 - r) * h, w, h),
                                                     new Vector2(.5f, .5f), w);
        return result;
    }

    // Sprite.Create()d sprites can be destroyed under a cache when the editor
    // opens a new scene (tests do), so a stale entry is rebuilt.
    static Sprite[] Sheet(Dictionary<string, Sprite[]> cache, string path, int cols, int rows)
    {
        Sprite[] sheet;
        if (cache.TryGetValue(path, out sheet) && sheet != null && sheet.Length > 0 && sheet[0] != null) return sheet;
        sheet = Slice(Resources.Load<Texture2D>(path), cols, rows);
        cache[path] = sheet;
        return sheet;
    }

    // A body sheet: square cells, BodyColumns across, as many rows as fit.
    static BossDef lastBodyBoss;
    static Sprite[] lastBodySheet;

    static Sprite[] BodySheet(BossDef boss)
    {
        // the last boss asked about (a frame asks for its body a few times: no path string built each time)
        if (ReferenceEquals(boss, lastBodyBoss) && lastBodySheet != null && lastBodySheet.Length > 0 && lastBodySheet[0] != null) return lastBodySheet;
        string path = Folder + boss.artKey;
        Sprite[] sheet;
        if (bodies.TryGetValue(path, out sheet) && sheet != null && sheet.Length > 0 && sheet[0] != null) { lastBodyBoss = boss; lastBodySheet = sheet; return sheet; }
        var tex = Resources.Load<Texture2D>(path);
        int rows = BodyRows;
        if (tex != null)
        {
            int cell = Mathf.Max(1, tex.width / BodyColumns);
            rows = Mathf.Clamp(tex.height / cell, 1, BodyRows);
        }
        sheet = Slice(tex, BodyColumns, rows, tex == null ? 0 : tex.width / BodyColumns);
        bodies[path] = sheet;
        lastBodyBoss = boss; lastBodySheet = sheet;
        return sheet;
    }

    // How many body frames the boss's atlas holds (Space 35, the others 20).
    public static int BodyFrameCount(BossDef boss) => boss == null ? 0 : BodySheet(boss).Length;

    public static Sprite Body(BossDef boss, int frame)
    {
        if (boss == null) return null;
        var sheet = BodySheet(boss);
        return sheet[Mathf.Clamp(frame, 0, sheet.Length - 1)];
    }

    public static Sprite DamageBody(BossDef boss, int cell)
    {
        if (!HasDamageArt(boss) || cell < 0) return null;
        var sheet = Sheet(bodies, Folder + boss.damageKey + "_damage", DamageColumns, DamageStages);
        return sheet[Mathf.Clamp(cell, 0, DamageCells - 1)];
    }

    public static Sprite DamageFx(BossDef boss, int cell)
    {
        if (!HasDamageArt(boss) || cell < 0) return null;
        var sheet = Sheet(bodies, Folder + boss.damageKey + "_damage_fx", DamageFxColumns, DamageFxRows);
        return sheet[Mathf.Clamp(cell, 0, DamageFxCells - 1)];
    }

    // ---- death strip (bosses with a BossDef.deathKey: Ember, Tide) --------------
    //
    //   Resources/Bosses/<Key>_death.png   DeathStripCells square cells in one
    //     row, registered to the idle cell (same scale and anchor as the body),
    //     DeathStripCellSeconds each. It replaces the body atlas's death frames;
    //     the blasts, sound and outro length are unchanged (the last cell
    //     holds until the outro's own death time is up).
    public const int DeathStripCells = 6;
    public const float DeathStripCellSeconds = .12f;
    public static bool HasDeathArt(BossDef boss) =>
        boss != null && !string.IsNullOrEmpty(boss.deathKey) && Resources.Load<Texture2D>(Folder + boss.deathKey + "_death") != null;
    public static float DeathStripSeconds => DeathStripCells * DeathStripCellSeconds;
    // The strip's cell `seconds` into the death, held on the last one after.
    public static int DeathStripCell(float seconds) =>
        Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, seconds) / DeathStripCellSeconds), 0, DeathStripCells - 1);
    public static Sprite DeathStrip(BossDef boss, int cell)
    {
        if (boss == null || string.IsNullOrEmpty(boss.deathKey)) return null;
        var sheet = Sheet(bodies, Folder + boss.deathKey + "_death", DeathStripCells, 1);
        return sheet[Mathf.Clamp(cell, 0, DeathStripCells - 1)];
    }

    // The shot sheet of the last boss asked about (a fight asks hundreds of times a second: no path string built per call).
    static BossDef lastShotBoss;
    static Sprite[] lastShotSheet;

    public static Sprite Shot(BossDef boss, int cell)
    {
        if (boss == null) return null;
        if (!ReferenceEquals(boss, lastShotBoss) || lastShotSheet == null || lastShotSheet.Length == 0 || lastShotSheet[0] == null)
        {
            lastShotSheet = Sheet(shots, Folder + boss.artKey + "_shots", ShotColumns, 1);
            lastShotBoss = boss;
        }
        return lastShotSheet[Mathf.Clamp(cell, 0, ShotColumns - 1)];
    }

    // ---- per-attack shot art (PatternStream.art) ------------------------------------------------------
    //
    // Resources/Bosses/<Key>_attackshots.png: 2 columns (frame a, b) x N rows of 128 px cells, one row per attack variant.
    // PatternStream.art = k (1-based) draws row k-1 instead of the boss's shared bolt / shard pair; art 0, a missing
    // file or a row outside it keeps the shared pair (so the game plays before the art lands). The variant is drawn at
    // its stream's style size (Bolt or Shard) and keeps that style's hit radius: the hitbox never depends on the art.
    // The variants carry their own dark keyline, so they wear no extra rim.
    public const string AttackShotsSuffix = "_attackshots";
    public const int AttackShotColumns = 2, AttackShotCell = 128;
    static readonly Dictionary<string, Sprite[]> attackShots = new Dictionary<string, Sprite[]>();
    static BossDef lastAttackBoss;
    static Sprite[] lastAttackSheet;
    static Texture2D injectedAttackShots;   // tests: served in place of the file for every boss

    public static void InjectAttackShots(Texture2D tex) { injectedAttackShots = tex; lastAttackBoss = null; lastAttackSheet = null; attackShots.Clear(); }

    static Sprite[] AttackShotSheet(BossDef boss)
    {
        if (boss == null) return null;
        if (ReferenceEquals(boss, lastAttackBoss) && lastAttackSheet != null && (lastAttackSheet.Length == 0 || lastAttackSheet[0] != null)) return lastAttackSheet;
        string path = Folder + boss.artKey + AttackShotsSuffix;
        Sprite[] sheet;
        if (!attackShots.TryGetValue(path, out sheet) || sheet == null || (sheet.Length > 0 && sheet[0] == null))
        {
            var tex = injectedAttackShots != null ? injectedAttackShots : Resources.Load<Texture2D>(path);
            int rows = tex != null ? tex.height / AttackShotCell : 0;
            sheet = tex == null || tex.width != AttackShotColumns * AttackShotCell || rows <= 0
                ? new Sprite[0] : Slice(tex, AttackShotColumns, rows, AttackShotCell);
            attackShots[path] = sheet;
        }
        lastAttackBoss = boss; lastAttackSheet = sheet;
        return sheet;
    }

    // How many attack variants this boss's file holds (0: none painted yet).
    public static int AttackShotVariants(BossDef boss) { var s = AttackShotSheet(boss); return s == null ? 0 : s.Length / AttackShotColumns; }

    // Frame `frame` (0, 1) of variant `art` (1-based); null when art is 0 or not painted.
    public static Sprite AttackShot(BossDef boss, int art, int frame)
    {
        if (art <= 0) return null;
        var s = AttackShotSheet(boss);
        int i = (art - 1) * AttackShotColumns + Mathf.Clamp(frame, 0, AttackShotColumns - 1);
        return s != null && i >= 0 && i < s.Length ? s[i] : null;
    }

    // ---- the shots' rim ----------------------------------------------------
    //
    // A boss shot's glow: a light "shadow" that hugs the drawing's own
    // silhouette and fades out a little way past it, so the art is framed
    // rather than swamped (it used to wear HostileGlow's round wrapper, far
    // wider than a thin bolt). Built once per boss from the shot cells'
    // alpha -- a distance falloff from the silhouette, at half the art's
    // resolution, all four cells in one small texture so every shot's rim
    // batches -- and cached; nothing is made per shot or per frame.
    //
    // All of its tuning is here:
    public const int ShotRimCells = 4;            // bolt 0..1, shard 0..1
    public const int ShotRimTexels = 64;          // rim texels across one art cell
    public const int ShotRimPad = 12;             // clear texels around the cell (must exceed the reach)
    public const float ShotRimReach = .06f;       // how far past the silhouette the outline reaches, as a share of the cell (a thin trace, not a halo)
    public const float ShotRimAlpha = .8f;        // its opacity right at the silhouette
    public const float ShotRimFalloff = 1f;       // linear: a crisp outline, no soft round bloom
    public const float ShotRimCoverage = .25f;    // alpha a texel needs to count as the drawing (soft painted edges count)
    // BOLD rim, for a bright world (BackdropCatalog.Spec.Bright, as the
    // hearts' and the elite / enemy shots' bold outlines): a solid light rim
    // in the usual hostile-pink trace (not paled: AtomClarityTest reads the
    // boss shots' pink edge as their cue against pickups), then a solid
    // dark keyline and a short dark under-glow, so a boss shot holds 3:1 over
    // pale cloud and mid-dark jungle alike. Shares of the cell, like the reach.
    public const float ShotRimBoldLight = .045f, ShotRimBoldSolid = .085f, ShotRimBoldReach = .1f;
    public static readonly Color32 ShotRimBoldKey = new Color32(10, 8, 26, 255);
    // Bold rims for the current world? (Tests may force it: ShotRimBold.)
    public static bool? ShotRimBold;
    public static bool ShotRimUseBold => ShotRimBold.HasValue ? ShotRimBold.Value : BackdropCatalog.CurrentIsBright;
    public const float ShotRimPulseScale = 0f;    // it never swells (a swelling rim reads as a round glow); only its alpha pulses

    sealed class RimSet { public Sprite[] sprites; public Texture2D source; public bool built; }
    static readonly Dictionary<string, RimSet> rims = new Dictionary<string, RimSet>();

    // The rim for a shot cell (Bolt0 .. Shard0 + 1), drawn at the art's own
    // scale: its sprite spans the cell plus the pad, one cell = one unit.
    // Null when the art is missing or could not be read (the caller falls
    // back to the round wrapper).
    public static Sprite ShotRim(BossDef boss, int cell) { return ShotRim(boss, cell, ShotRimUseBold); }

    // The rim set of the last boss asked about (every shot launch asks twice: no path string built each time).
    static BossDef lastRimBoss;
    static bool lastRimBold;
    static RimSet lastRimSet;

    public static Sprite ShotRim(BossDef boss, int cell, bool bold)
    {
        if (boss == null || cell < 0 || cell >= ShotRimCells) return null;
        var first = Shot(boss, 0);
        var source = first != null ? first.texture : null;
        RimSet set;
        if (ReferenceEquals(boss, lastRimBoss) && bold == lastRimBold && lastRimSet != null && lastRimSet.source == source &&
            (!lastRimSet.built || (lastRimSet.sprites[0] != null && lastRimSet.sprites[0].texture != null)))
            return lastRimSet.built ? lastRimSet.sprites[cell] : null;
        string path = Folder + boss.artKey + "_shots" + (bold ? "#bold" : "");
        bool fresh = rims.TryGetValue(path, out set) && set.source == source &&
                     (!set.built || (set.sprites[0] != null && set.sprites[0].texture != null));
        if (!fresh)
        {
            set = new RimSet { source = source, sprites = new Sprite[ShotRimCells] };
            set.built = source != null && BuildShotRims(boss, set.sprites, bold);
            rims[path] = set;
        }
        lastRimBoss = boss; lastRimBold = bold; lastRimSet = set;
        return set.built ? set.sprites[cell] : null;
    }

    static bool BuildShotRims(BossDef boss, Sprite[] into, bool bold)
    {
        int n = ShotRimTexels, pad = ShotRimPad, side = n + 2 * pad;
        var px = new Color32[side * ShotRimCells * side];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
        var dist = new int[side * side];
        const int Far = 1 << 20;
        float reach = Mathf.Max(1f, (bold ? ShotRimBoldReach : ShotRimReach) * n);   // texels
        float boldLight = ShotRimBoldLight * n, boldSolid = ShotRimBoldSolid * n;

        // The cells' pixels, read back once as one strip. By each cell's own
        // rect, never a shot sprite's textureRect: that is trimmed to the
        // drawing (the sprites have tight meshes), and a rim built from it
        // would be the drawing stretched over the whole cell.
        var first = Shot(boss, 0);
        if (first == null) return false;
        Rect cellRect = first.rect;
        int w = Mathf.RoundToInt(cellRect.width), h = Mathf.RoundToInt(cellRect.height);
        if (w <= 0 || h <= 0 || cellRect.x + w * ShotRimCells > first.texture.width + .5f) return false;
        var strip = Sprite.Create(first.texture, new Rect(cellRect.x, cellRect.y, w * ShotRimCells, h),
                                  new Vector2(.5f, .5f), w, 0, SpriteMeshType.FullRect);
        int stripW, stripH;
        Color32[] art = ShieldContour.ReadPixels(strip, out stripW, out stripH);
        if (Application.isPlaying) Object.Destroy(strip); else Object.DestroyImmediate(strip);
        if (art == null || stripW != w * ShotRimCells || stripH != h || art.Length < stripW * stripH) return false;

        for (int cell = 0; cell < ShotRimCells; cell++)
        {

            // the silhouette, at the rim's resolution
            for (int i = 0; i < dist.Length; i++) dist[i] = Far;
            for (int ty = 0; ty < n; ty++)
            {
                int y0 = ty * h / n, y1 = Mathf.Max(y0 + 1, (ty + 1) * h / n);
                for (int tx = 0; tx < n; tx++)
                {
                    int x0 = tx * w / n, x1 = Mathf.Max(x0 + 1, (tx + 1) * w / n);
                    int sum = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) sum += art[y * stripW + cell * w + x].a;
                    if (sum >= ShotRimCoverage * 255f * (x1 - x0) * (y1 - y0)) dist[(ty + pad) * side + tx + pad] = 0;
                }
            }

            // distance from it: a two-pass 3-4 chamfer (thirds of a texel)
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int i = y * side + x, v = dist[i];
                    if (x > 0) v = Mathf.Min(v, dist[i - 1] + 3);
                    if (y > 0)
                    {
                        v = Mathf.Min(v, dist[i - side] + 3);
                        if (x > 0) v = Mathf.Min(v, dist[i - side - 1] + 4);
                        if (x < side - 1) v = Mathf.Min(v, dist[i - side + 1] + 4);
                    }
                    dist[i] = v;
                }
            for (int y = side - 1; y >= 0; y--)
                for (int x = side - 1; x >= 0; x--)
                {
                    int i = y * side + x, v = dist[i];
                    if (x < side - 1) v = Mathf.Min(v, dist[i + 1] + 3);
                    if (y < side - 1)
                    {
                        v = Mathf.Min(v, dist[i + side] + 3);
                        if (x < side - 1) v = Mathf.Min(v, dist[i + side + 1] + 4);
                        if (x > 0) v = Mathf.Min(v, dist[i + side - 1] + 4);
                    }
                    dist[i] = v;
                }

            // the falloff: full at the silhouette (and under the art), gone at the reach
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    if (bold)
                    {
                        // light rim to boldLight, then a solid dark keyline, then a short dark fade
                        float d = dist[y * side + x] / 3f;
                        if (d > reach) continue;
                        int at = y * side * ShotRimCells + cell * side + x;
                        if (d <= boldLight) { px[at].a = (byte)Mathf.RoundToInt(255f * ShotRimAlpha); continue; }
                        float a = d <= boldSolid ? 1f : 1f - (d - boldSolid) / Mathf.Max(.01f, reach - boldSolid);
                        var key = ShotRimBoldKey;
                        px[at] = new Color32(key.r, key.g, key.b, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a)));
                        continue;
                    }
                    float k = Mathf.Clamp01(1f - dist[y * side + x] / 3f / reach);
                    if (k <= 0f) continue;
                    px[y * side * ShotRimCells + cell * side + x].a =
                        (byte)Mathf.RoundToInt(255f * ShotRimAlpha * Mathf.Pow(k, ShotRimFalloff));
                }
        }

        var tex = new Texture2D(side * ShotRimCells, side, TextureFormat.RGBA32, false);
        tex.name = boss.artKey + (bold ? "ShotRimBold" : "ShotRim");
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);   // kept readable: tiny, and the tests read it back
        for (int cell = 0; cell < ShotRimCells; cell++)
        {
            var s = Sprite.Create(tex, new Rect(cell * side, 0, side, side), new Vector2(.5f, .5f), n, 0, SpriteMeshType.FullRect);
            s.name = tex.name + cell;
            s.hideFlags = HideFlags.HideAndDontSave;
            into[cell] = s;
        }
        return true;
    }

    public static Sprite Card(BossDef boss) => boss == null ? null : Single(Folder + boss.artKey + "_card");
    public static Sprite Warning() => Single(Folder + "warning");

    static Sprite Single(string path)
    {
        Sprite s;
        if (singles.TryGetValue(path, out s) && s != null) return s;
        var tex = Resources.Load<Texture2D>(path);
        s = tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f);
        singles[path] = s;
        return s;
    }

    // Every sprite a boss needs, for the art-completeness test.
    public static IEnumerable<Sprite> AllSprites(BossDef boss)
    {
        int bodyFrames = BodyFrameCount(boss);
        for (int i = 0; i < bodyFrames; i++) yield return Body(boss, i);
        for (int i = 0; i < ShotColumns; i++) yield return Shot(boss, i);
        yield return Card(boss);
        if (HasDamageArt(boss))
        {
            for (int i = 0; i < DamageCells; i++) yield return DamageBody(boss, i);
            for (int i = 0; i < DamageFxCells; i++) yield return DamageFx(boss, i);
            var poses = AttackDamagePoses(boss);
            for (int s = 1; s <= DamageStages; s++)
                for (int p = 0; p < poses.Length; p++) yield return AttackDamageBody(boss, poses[p], s);
        }
        if (HasDeathArt(boss))
            for (int i = 0; i < DeathStripCells; i++) yield return DeathStrip(boss, i);
    }
}
