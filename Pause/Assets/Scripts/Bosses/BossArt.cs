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
    public const int BodyColumns = 5, BodyRows = 7, BodyFrames = BodyColumns * BodyRows;
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
    public static bool HasExpandedCombat(BossDef boss) => boss != null && boss.artKey == "Space";
    public static int IdleFrame(BossDef boss, float seconds)
    {
        return HasExpandedCombat(boss)
            ? SpaceIdle0 + FrameAt(SpaceIdleTicks, seconds, true)
            : Idle0 + FrameAt(IdleTicks, seconds, true);
    }
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

    static Sprite[] Slice(Texture2D tex, int cols, int rows)
    {
        var result = new Sprite[cols * rows];
        if (tex == null) return result;
        float w = tex.width / (float)cols, h = tex.height / (float)rows;
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

    public static Sprite Body(BossDef boss, int frame)
    {
        if (boss == null) return null;
        var sheet = Sheet(bodies, Folder + boss.artKey, BodyColumns, BodyRows);
        return sheet[Mathf.Clamp(frame, 0, BodyFrames - 1)];
    }

    public static Sprite Shot(BossDef boss, int cell)
    {
        if (boss == null) return null;
        var sheet = Sheet(shots, Folder + boss.artKey + "_shots", ShotColumns, 1);
        return sheet[Mathf.Clamp(cell, 0, ShotColumns - 1)];
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
    public const float ShotRimReach = .17f;       // how far past the silhouette it fades to nothing, as a share of the cell
    public const float ShotRimAlpha = .7f;        // its opacity right at the silhouette
    public const float ShotRimFalloff = 1.8f;     // > 1: most of the light sits tight against the art
    public const float ShotRimCoverage = .25f;    // alpha a texel needs to count as the drawing (soft painted edges count)
    public const float ShotRimPulseScale = .03f;  // its breathing (HostileGlow's wrapper: .07); alpha pulses as the wrapper's

    sealed class RimSet { public Sprite[] sprites; public Texture2D source; public bool built; }
    static readonly Dictionary<string, RimSet> rims = new Dictionary<string, RimSet>();

    // The rim for a shot cell (Bolt0 .. Shard0 + 1), drawn at the art's own
    // scale: its sprite spans the cell plus the pad, one cell = one unit.
    // Null when the art is missing or could not be read (the caller falls
    // back to the round wrapper).
    public static Sprite ShotRim(BossDef boss, int cell)
    {
        if (boss == null || cell < 0 || cell >= ShotRimCells) return null;
        string path = Folder + boss.artKey + "_shots";
        var first = Shot(boss, 0);
        var source = first != null ? first.texture : null;
        RimSet set;
        bool fresh = rims.TryGetValue(path, out set) && set.source == source &&
                     (!set.built || (set.sprites[0] != null && set.sprites[0].texture != null));
        if (!fresh)
        {
            set = new RimSet { source = source, sprites = new Sprite[ShotRimCells] };
            set.built = source != null && BuildShotRims(boss, set.sprites);
            rims[path] = set;
        }
        return set.built ? set.sprites[cell] : null;
    }

    static bool BuildShotRims(BossDef boss, Sprite[] into)
    {
        int n = ShotRimTexels, pad = ShotRimPad, side = n + 2 * pad;
        var px = new Color32[side * ShotRimCells * side];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
        var dist = new int[side * side];
        const int Far = 1 << 20;
        float reach = Mathf.Max(1f, ShotRimReach * n);   // texels

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
                    float k = Mathf.Clamp01(1f - dist[y * side + x] / 3f / reach);
                    if (k <= 0f) continue;
                    px[y * side * ShotRimCells + cell * side + x].a =
                        (byte)Mathf.RoundToInt(255f * ShotRimAlpha * Mathf.Pow(k, ShotRimFalloff));
                }
        }

        var tex = new Texture2D(side * ShotRimCells, side, TextureFormat.RGBA32, false);
        tex.name = boss.artKey + "ShotRim";
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
        for (int i = 0; i < BodyFrames; i++) yield return Body(boss, i);
        for (int i = 0; i < ShotColumns; i++) yield return Shot(boss, i);
        yield return Card(boss);
    }
}
