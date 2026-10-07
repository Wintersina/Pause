using System.Collections.Generic;
using UnityEngine;

// Space's stations come in two presentations, chosen when a lone station
// spawns (SpaceDirector.PlanStructure):
//
//   edge peekers  (~PeekShare of lone stations) -- centred at or just inside
//                 the left / right screen edge, so the frame cuts away about
//                 half of the station: a distant structure partly hidden.
//                 Upright, still, blinking lamps only (lamps past the edge
//                 are simply off-screen). Never offered as an elite launch
//                 site (SpaceLandingSites: the hub must be on screen).
//   in-frame      everything else (and stations parked by a planet): fully
//                 or mostly on screen, upright and still, with the lamps PLUS
//                 small life -- steam puffs rising from the tower tips (vent
//                 anchors = the lamp table's beacon points) and now and then
//                 a spark popping at a girder end (the double-blink points).
//
// Codex's Space art (fx / anim / neon_frames / asteroid_fx, 4580c462) has no
// per-station light or steam frames -- station_00..03 / ringstation_00..03
// are different stations, station_ring_v2 is a single picture -- so all of
// this is code-driven: no station ever plays a flipbook, turns or tilts.
//
// Cost: per pooled station piece, PuffSlots + 1 SpriteRenderers made once
// (children of its lamp rig, so they share the lamps' cell space and ride /
// scale with the station); the stage sprites are four tiny hard-edged pixel
// puffs and two spark frames cut once from one 64x16 texture built at
// start. Per frame: a countdown per station and a position / colour per live
// puff -- no allocations, and on the scaled clock, so a paused game freezes
// them mid-drift like the rest of the backdrop.
public partial class SpaceDirector
{
    public const float PeekShare = 0.4f;            // lone stations that peek in from the edge
    public const float PeekInsetMax = 0.1f;         // centre at most this share of its width inside the edge
    public const float HubHalfWidth = 0.12f;        // share of a station's width its hangar hub spans each side

    SpaceStationPuffs stationPuffs;

    public SpaceStationPuffs StationPuffs { get { return stationPuffs; } }

    // A peeker: a lone station parked across the screen edge.
    public static bool IsPeeker(BackdropPiece p) { return p != null && p.edge; }

    void BuildStationPuffs()
    {
        stationPuffs = new SpaceStationPuffs(stationLights, stations);
    }

    void TickStationPuffs(float dt)
    {
        if (stationPuffs != null) stationPuffs.Tick(dt, set.Alpha);
    }

    void TeardownStationPuffs()
    {
        if (stationPuffs != null) stationPuffs.Destroy();
        stationPuffs = null;
    }
}

public class SpaceStationPuffs
{
    public const int PuffSlots = 4;                 // live puffs per station at most
    public const int Stages = 4;                    // hard-edged stages a puff grows through
    public const float PuffLife = 2.6f;             // seconds from vent to gone
    public const float EmitMin = 0.8f, EmitMax = 2.0f;      // seconds between puffs of one station
    public const float PuffSizePerUnit = 0.075f, PuffSizeMin = 0.035f, PuffSizeMax = 0.13f;  // world width at birth
    public const float PuffGrow = 1.7f;             // width at death / at birth
    public const float RisePerUnit = 0.10f;         // world units per second per unit of station width
    public const float PuffAlpha = 0.34f;           // peak opacity (x depth-tier light): faint
    public const float SparkMin = 2.5f, SparkMax = 6.5f, SparkLife = 0.22f;
    public const float SparkSizePerUnit = 0.05f, SparkSizeMin = 0.03f, SparkSizeMax = 0.08f;

    // Pale grey-violet steam, now and then magenta-lit by the windows. No red.
    public static readonly Color[] PuffTints =
    {
        new Color(0.74f, 0.70f, 0.86f), new Color(0.66f, 0.64f, 0.80f), new Color(0.86f, 0.60f, 0.84f),
    };
    // Spark pops in the lamps' amber / white-hot / cyan (SpaceStationLights.Colors).
    static readonly int[] SparkColors = { 4, 3, 2 };

    public class Rig
    {
        public BackdropPiece piece;
        public SpaceStationLights.Rig lamps;
        public Transform root;
        public SpriteRenderer[] puffs;
        public float[] age;             // < 0: slot free
        public Vector2[] origin;        // lamp-space birth point
        public float[] driftX;          // lamp-space sideways drift per second
        public Sprite bound;
        public bool boundEdge;
        public bool animated;           // this life: an in-frame station with vents
        public readonly Vector2[] vents = new Vector2[6];
        public int ventCount;
        public readonly Vector2[] ends = new Vector2[4];
        public int endCount, nextVent;
        public float emitIn;
        public SpriteRenderer spark;
        public float sparkIn, sparkAge = -1f;
        public int puffsEmitted, sparksPopped;
    }

    public readonly List<Rig> Rigs = new List<Rig>();
    readonly Sprite[] stageSprites = new Sprite[Stages];
    readonly Sprite[] sparkSprites = new Sprite[2];
    readonly System.Random rng = new System.Random(3301);  // own stream: the director's spawns are untouched
    Texture2D texture;

    public Sprite[] StageSprites { get { return stageSprites; } }

    public SpaceStationPuffs(SpaceStationLights lights, BackdropPool stations)
    {
        BuildSprites();
        if (lights == null || stations == null) return;
        foreach (var lr in lights.Rigs)
        {
            if (!stations.items.Contains(lr.piece)) continue;
            var go = new GameObject("puffs");
            go.transform.SetParent(lr.transform, false);   // lamp space: rides and scales with the station
            var r = new Rig
            {
                piece = lr.piece, lamps = lr, root = go.transform,
                puffs = new SpriteRenderer[PuffSlots], age = new float[PuffSlots],
                origin = new Vector2[PuffSlots], driftX = new float[PuffSlots],
            };
            for (int i = 0; i < PuffSlots; i++)
            {
                var pg = new GameObject("puff");
                pg.transform.SetParent(go.transform, false);
                var sr = pg.AddComponent<SpriteRenderer>();
                sr.sprite = stageSprites[0];
                sr.enabled = false;
                r.puffs[i] = sr;
                r.age[i] = -1f;
            }
            var sg = new GameObject("spark");
            sg.transform.SetParent(go.transform, false);
            r.spark = sg.AddComponent<SpriteRenderer>();
            r.spark.sprite = sparkSprites[0];
            r.spark.enabled = false;
            Rigs.Add(r);
        }
    }

    // Four puff stages (3, 5, 7, 8 px blobs; the last broken up as it thins)
    // and two spark frames (a 5 px cross, then a 3 px dot), cut from one
    // 64x16 point-filtered texture. One texture unit = one sprite unit wide.
    void BuildSprites()
    {
        const int W = 64, H = 16;
        texture = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            name = "SpaceStationPuffs", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[W * H];
        int[] radius = { 2, 3, 4, 5 };              // stage blob radius in px (in a 10 px cell)
        for (int s = 0; s < Stages; s++)
        {
            int ox = s * 10, oy = 3;
            float r = radius[s] - 0.5f;
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                {
                    float dx = x + 0.5f - 5f, dy = y + 0.5f - 5f;
                    bool inside = dx * dx + dy * dy <= r * r;
                    // the last stage thins out: a checker of holes
                    if (s == Stages - 1 && ((x + y) & 1) == 0 && dx * dx + dy * dy > (r - 2f) * (r - 2f)) inside = false;
                    if (inside) px[(oy + y) * W + ox + x] = new Color32(255, 255, 255, 255);
                }
            stageSprites[s] = Sprite.Create(texture, new Rect(ox, oy, 10, 10), new Vector2(0.5f, 0.5f), 10f,
                                            0, SpriteMeshType.FullRect);
            stageSprites[s].name = "station_puff_" + s;
        }
        // spark frames at x 40.. and 48..
        for (int k = -2; k <= 2; k++)
        {
            px[(3 + 2) * W + 40 + 2 + k] = new Color32(255, 255, 255, 255);
            px[(3 + 2 + k) * W + 40 + 2] = new Color32(255, 255, 255, 255);
        }
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                if (x == 1 || y == 1) px[(4 + y) * W + 49 + x] = new Color32(255, 255, 255, 255);
        sparkSprites[0] = Sprite.Create(texture, new Rect(40, 3, 5, 5), new Vector2(0.5f, 0.5f), 5f, 0, SpriteMeshType.FullRect);
        sparkSprites[1] = Sprite.Create(texture, new Rect(48, 3, 5, 5), new Vector2(0.5f, 0.5f), 5f, 0, SpriteMeshType.FullRect);
        sparkSprites[0].name = "station_spark_0";
        sparkSprites[1].name = "station_spark_1";
        texture.SetPixels32(px);
        texture.Apply(false, true);
    }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    public void Tick(float dt, float alpha)
    {
        for (int i = 0; i < Rigs.Count; i++)
        {
            var r = Rigs[i];
            var p = r.piece;
            if (!p.active) { if (r.bound != null) Unbind(r); continue; }
            if (p.sr.sprite != r.bound || p.edge != r.boundEdge) Bind(r);
            if (!r.animated) continue;

            // world units per lamp-space unit: the station's scale x its cell's (a mini is smaller)
            float unit = Mathf.Max(1e-4f, Mathf.Abs(p.root.localScale.x * r.lamps.transform.localScale.x));
            Color c = p.sr.color;
            float light = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            int order = p.sr.sortingOrder + 1;

            r.emitIn -= dt;
            if (r.emitIn <= 0f && r.ventCount > 0)
            {
                r.emitIn = Rand(EmitMin, EmitMax);
                for (int k = 0; k < PuffSlots; k++)
                {
                    if (r.age[k] >= 0f) continue;
                    Vector2 v = r.vents[r.nextVent++ % r.ventCount];
                    r.age[k] = 0f;
                    r.origin[k] = v;
                    r.driftX[k] = Rand(-0.25f, 0.25f) * RisePerUnit * p.size / unit;
                    var t = PuffTints[rng.Next(PuffTints.Length)];
                    r.puffs[k].color = new Color(t.r, t.g, t.b, 0f);
                    r.puffs[k].sortingOrder = order;
                    r.puffs[k].enabled = true;
                    r.puffsEmitted++;
                    break;
                }
            }

            float birth = Mathf.Clamp(p.size * PuffSizePerUnit, PuffSizeMin, PuffSizeMax) / unit;
            float rise = RisePerUnit * p.size / unit;
            for (int k = 0; k < PuffSlots; k++)
            {
                if (r.age[k] < 0f) continue;
                r.age[k] += dt;
                float f = r.age[k] / PuffLife;
                var sr = r.puffs[k];
                if (f >= 1f) { r.age[k] = -1f; sr.enabled = false; continue; }
                int stage = Mathf.Min(Stages - 1, (int)(f * Stages));
                sr.sprite = stageSprites[stage];
                float w = birth * Mathf.Lerp(1f, PuffGrow, f);
                var tr = sr.transform;
                tr.localScale = new Vector3(w, w, 1f);
                tr.localPosition = new Vector3(r.origin[k].x + r.driftX[k] * r.age[k], r.origin[k].y + rise * r.age[k], 0f);
                // fade in over the first stage, hold, fade out over the last
                float a = f < 0.15f ? f / 0.15f : f > 0.6f ? (1f - f) / 0.4f : 1f;
                Color pc = sr.color;
                pc.a = PuffAlpha * a * light * c.a * alpha;
                sr.color = pc;
                sr.sortingOrder = order;
            }

            // an occasional spark at a girder end
            if (r.endCount > 0)
            {
                if (r.sparkAge < 0f)
                {
                    r.sparkIn -= dt;
                    if (r.sparkIn <= 0f)
                    {
                        r.sparkAge = 0f;
                        r.sparkIn = Rand(SparkMin, SparkMax);
                        Vector2 e = r.ends[rng.Next(r.endCount)];
                        r.spark.transform.localPosition = new Vector3(e.x, e.y, 0f);
                        Color sc = SpaceStationLights.Colors[SparkColors[rng.Next(SparkColors.Length)]];
                        r.spark.color = new Color(sc.r, sc.g, sc.b, 0f);
                        r.spark.enabled = true;
                        r.sparksPopped++;
                    }
                }
                else
                {
                    r.sparkAge += dt;
                    if (r.sparkAge >= SparkLife) { r.sparkAge = -1f; r.spark.enabled = false; }
                    else
                    {
                        bool pop = r.sparkAge < SparkLife * 0.45f;
                        r.spark.sprite = sparkSprites[pop ? 0 : 1];
                        float s = Mathf.Clamp(p.size * SparkSizePerUnit, SparkSizeMin, SparkSizeMax) / unit;
                        r.spark.transform.localScale = new Vector3(s, s, 1f);
                        Color sc = r.spark.color;
                        sc.a = (pop ? 1f : 0.55f) * SpaceStationLights.Gain * light * c.a * alpha;
                        r.spark.color = sc;
                        r.spark.sortingOrder = order + 1;
                    }
                }
            }
        }
    }

    // A new life (or a new cell) for this piece: vents and girder ends from
    // the station's lamp table, unless it is an edge peeker.
    void Bind(Rig r)
    {
        Unbind(r);
        var p = r.piece;
        r.bound = p.sr.sprite;
        r.boundEdge = p.edge;
        string cell = r.bound != null ? SpaceStationLights.CellOf(r.bound.name) : null;
        var lamps = cell != null ? SpaceStationLights.LampsOf(cell) : null;
        r.animated = lamps != null && !p.edge && r.lamps.cell != null;
        if (!r.animated) return;
        foreach (var l in lamps)
        {
            var v = new Vector2(l.x / BackdropAtlas.PixelsPerUnit, l.y / BackdropAtlas.PixelsPerUnit);
            if (l.pattern == SpaceStationLights.Beacon && r.ventCount < r.vents.Length) r.vents[r.ventCount++] = v;
            else if (l.pattern == SpaceStationLights.Double && r.endCount < r.ends.Length) r.ends[r.endCount++] = v;
        }
        r.emitIn = Rand(0f, EmitMax);
        r.sparkIn = Rand(0.5f, SparkMax);
    }

    void Unbind(Rig r)
    {
        r.bound = null;
        r.animated = false;
        r.ventCount = r.endCount = r.nextVent = 0;
        for (int k = 0; k < PuffSlots; k++)
        {
            r.age[k] = -1f;
            if (r.puffs[k].enabled) r.puffs[k].enabled = false;
        }
        r.sparkAge = -1f;
        if (r.spark.enabled) r.spark.enabled = false;
    }

    // Live puffs right now (tests, previews).
    public int LivePuffs(Rig r)
    {
        int n = 0;
        for (int k = 0; k < PuffSlots; k++) if (r.age[k] >= 0f) n++;
        return n;
    }

    public void Destroy()
    {
        foreach (var s in stageSprites) BackdropAtlas.Kill(s);
        foreach (var s in sparkSprites) BackdropAtlas.Kill(s);
        BackdropAtlas.Kill(texture);
        texture = null;
        Rigs.Clear();
    }
}
