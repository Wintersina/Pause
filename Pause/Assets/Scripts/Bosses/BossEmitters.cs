using UnityEngine;

// Where on the boss an attack comes from.
//
// Every attack names the body parts it fires from (the Archon's chin cannon,
// the Leviathan's eyes, the Bloom Queen's petal tips, the Drake's jaw ...).
// BossEmitterTable -- generated from the art by
// Art/BossAttacks/src~/measure_emitters.py -- holds each part's muzzle pixel
// in every drawing the boss can tell or fire from, because the poses squash,
// lean and shift the whole body. This turns those into positions relative to
// the boss: the body is one atlas cell of BossConfig.BossWorldSize world
// units, centred on the actor, so a muzzle follows the drift, the bob and
// the current frame exactly.
public static class BossEmitters
{
    // Index of a boss in the generated table (by art key), or -1.
    public static int World(BossDef boss)
    {
        if (boss == null) return -1;
        var w = BossEmitterTable.Worlds;
        for (int i = 0; i < w.Length; i++) if (w[i] == boss.artKey) return i;
        return -1;
    }

    // Index of a named part, or -1.
    public static int Part(BossDef boss, string name)
    {
        int w = World(boss);
        if (w < 0) return -1;
        var parts = BossEmitterTable.Parts[w];
        for (int i = 0; i < parts.Length; i++) if (parts[i] == name) return i;
        return -1;
    }

    public static string PartName(BossDef boss, int part)
    {
        int w = World(boss);
        if (w < 0 || part < 0 || part >= BossEmitterTable.Parts[w].Length) return null;
        return BossEmitterTable.Parts[w][part];
    }

    // The drawings the table covers; any other (death, retreat, portrait)
    // falls back to the first idle drawing -- nothing fires from those.
    public static int TableFrame(int bodyFrame) =>
        bodyFrame >= 0 && bodyFrame < BossEmitterTable.Frames ? bodyFrame : BossArt.Idle0;

    // The muzzle pixel (cell px, x right, y down) of `part` in `bodyFrame`.
    public static Vector2Int Pixel(BossDef boss, int part, int bodyFrame)
    {
        int w = World(boss);
        if (w < 0 || part < 0) return new Vector2Int(BossEmitterTable.CellPixels / 2, BossEmitterTable.CellPixels / 2);
        var pts = BossEmitterTable.Points[w][part];
        int f = TableFrame(bodyFrame);
        return new Vector2Int(pts[f * 2], pts[f * 2 + 1]);
    }

    // The muzzle relative to the boss's centre, in world units at the
    // body's size (BossConfig.BossWorldSize).
    public static Vector2 Local(BossDef boss, int part, int bodyFrame)
    {
        var px = Pixel(boss, part, bodyFrame);
        float k = BossConfig.BossWorldSize / BossEmitterTable.CellPixels;
        float half = BossEmitterTable.CellPixels * .5f;
        return new Vector2((px.x - half) * k, (half - px.y) * k);
    }

    // Names -> indices, once per attack (BossCatalog). Unknown names map to -1.
    public static int[] Resolve(BossDef boss, string[] names)
    {
        if (names == null) return new int[0];
        var result = new int[names.Length];
        for (int i = 0; i < names.Length; i++) result[i] = Part(boss, names[i]);
        return result;
    }
}

// The two side rails: the walls' inner edges, the line boss projectiles
// bounce off or splash against.
//
// The walls (leftPipe / rightPipe in gameS1) are world-fixed quads -- 1.43
// wide at x = -/+3.15, so their inner faces are at -/+2.435 on every screen
// (a wider phone only shows more of the wall, CameraFit never lets the view
// get narrower than +/-2.85). The live walls are measured when a fight
// starts; a scene without them (tests, the tutorial) uses the authored edge.
public static class BossRails
{
    public const float AuthoredInnerEdge = ResumeFx.RailInnerEdge;

    static float measured = -1f;

    public static float InnerEdge => measured > 0f ? measured : AuthoredInnerEdge;

    // From BossEncounter at the start of each intro (and tests).
    public static void Measure()
    {
        measured = -1f;
        var left = GameObject.Find("leftPipe");
        var right = GameObject.Find("rightPipe");
        float edge = float.MaxValue;
        // A reinforced rail is narrower than its quad (the quad carries the
        // texture's transparent canvas), so the edge is the art's, not the
        // renderer's; a plain wall is its renderer.
        float inner, outer;
        if (left != null)
        {
            var r = left.GetComponent<Renderer>();
            if (r != null && r.enabled)
                edge = Mathf.Min(edge, WorldPainter.VisibleRailEdges(left, out inner, out outer) ? inner : -r.bounds.max.x);
        }
        if (right != null)
        {
            var r = right.GetComponent<Renderer>();
            if (r != null && r.enabled)
                edge = Mathf.Min(edge, WorldPainter.VisibleRailEdges(right, out inner, out outer) ? inner : r.bounds.min.x);
        }
        // a sane wall only: the ship's own clamp (2.4) is always inside it
        if (edge != float.MaxValue && edge > 2.3f && edge < 4f) measured = edge;
    }

    public static void Reset() { measured = -1f; }
}
