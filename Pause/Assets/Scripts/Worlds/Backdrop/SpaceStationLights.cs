using System.Collections.Generic;
using UnityEngine;

// Blinking lights on Space's stations: the long stations (station_00..03),
// the ring habitats (ringstation_00..03), their pre-shrunk minis, Codex's
// high-resolution ring station (station_ring_v2, its own sprite), and the
// moon with a station built on it (`moon`). Stations hold still -- no spin,
// no tilt, no flipbook (SpaceDirector.Enter) -- so the life comes from these
// lamps instead.
//
// Each lamp sits on a point of the station's own art, found by
// Art/Worlds/Space/src~/station_lights.py: tower tips and hanging keel tips
// get aviation-style beacons (white / amber; red is the player's colour,
// docs/art-style.md), girder ends a double blink, the magenta window grids
// flicker and pulse, a couple of cyan rim points strobe. All drawn by ONE MeshRenderer per pooled station
// piece (a mesh per atlas cell, built once) with the BackdropStationLights
// shader: per frame the director only feeds each renderer its clock, tint and
// lamp size through a shared MaterialPropertyBlock -- no allocations, no
// per-lamp objects. The renderer is a child of the piece's body, so the lamps
// move, scale (and would mirror) with the station; the clock is integrated
// from the scaled dt, so a paused game freezes them mid-blink.
public partial class SpaceDirector
{
    SpaceStationLights stationLights;

    public SpaceStationLights StationLights { get { return stationLights; } }

    void BuildStationLights()
    {
        stationLights = new SpaceStationLights(fx, detailedStationFrames);
        stationLights.Attach(stations);
        stationLights.Attach(moons);
    }

    void TickStationLights(float dt)
    {
        if (stationLights != null) stationLights.Tick(dt, set.Alpha);
    }

    void TeardownStationLights()
    {
        if (stationLights != null) stationLights.Destroy();
        stationLights = null;
    }

    // Stations are cells of these names (and their mini_ copies); the `moon`
    // cell is a moon with a station on it.
    public static bool IsStationArt(Sprite s)
    {
        return s != null && SpaceStationLights.CellOf(s.name) != null;
    }
}

public class SpaceStationLights
{
    public const string ShaderPath = "BackdropShaders/BackdropStationLights";
    // Blink patterns (BackdropStationLights.shader, Level()).
    public const int Pulse = 0, Double = 1, Strobe = 2, Flicker = 3, Beacon = 4;
    // The blink clock wraps here: a whole number of every pattern's period
    // (pulse, double, strobe, a flicker slot, beacon -- see Level()).
    public const float ClockWrap = 240f;
    public static readonly float[] Periods = { 1.2f, 2.4f, 1.6f, 0.25f, 1.5f };
    // Lamp width in world units: a fraction of the station's drawn width,
    // within these limits (and never under MinPx screen pixels).
    public const float SizePerUnit = 0.016f, SizeMin = 0.012f, SizeMax = 0.035f, MinPx = 2f;
    // Lamps stay under gameplay brightness: depth-tier light times this.
    public const float Gain = 0.78f;

    // docs/art-style.md palette, emissive tones only: Space's neon cyan and
    // white-hot, magenta (Verdant's thorn ramp, the stations' own window
    // colour) and a touch of amber for the beacons. No red: red is the player's.
    public static readonly Color[] Colors =
    {
        new Color32(0xe0, 0x30, 0x9e, 0xff),   // magenta
        new Color32(0xff, 0x9a, 0xd6, 0xff),   // pink
        new Color32(0x0b, 0xd0, 0xf6, 0xff),   // cyan
        new Color32(0xf5, 0xfd, 0xfd, 0xff),   // white-hot
        new Color32(0xfc, 0xb8, 0x09, 0xff),   // amber
    };

    public struct Lamp
    {
        public float x, y;          // atlas pixels from the full-size cell's centre (x right, y up)
        public int pattern, color;
        public float seed;          // 0..1: phase within the pattern / flicker stream
    }

    public struct Station
    {
        public string name;         // the full-size cell; its mini_ copy shares the lamps
        public Lamp[] lamps;
    }

    static Station S(string name, Lamp[] lamps) { return new Station { name = name, lamps = lamps }; }

    static Lamp L(float x, float y, int pattern, int color, float seed)
    {
        return new Lamp { x = x, y = y, pattern = pattern, color = color, seed = seed };
    }

    // Beacons and girder ends are a bit larger than the window lamps.
    public static float SizeFactor(int pattern)
    {
        return pattern == Beacon ? 1.5f : pattern == Double ? 1.25f : 1f;
    }

    // Generated from fx.png by station_lights.py (tower tips, keel tips,
    // girder ends, magenta window clusters, cyan rim; every lamp on an opaque
    // pixel -- WorldBackdropTest re-checks). Re-run it if the cells are re-cut.
    // (An array, not a Dictionary: TestHarness.Sandbox restores static
    // arrays element-wise but empties generic dictionaries on restore.)
    public static readonly Station[] Anchors =
    {
        S("station_00", new[] {
            L(0.5f, 108.5f, 4, 3, 0.812f), L(-11.5f, 93.5f, 4, 4, 0.121f), L(-77.5f, 45.5f, 4, 4, 0.618f), L(-7.5f, -109.5f, 0, 4, 0.075f),
            L(30.5f, -73.5f, 0, 4, 0.497f), L(-113.5f, 1.5f, 1, 3, 0.916f), L(112.5f, -13.5f, 1, 4, 0.627f), L(-3.5f, 8.5f, 3, 0, 0.263f),
            L(61.5f, 13.5f, 3, 0, 0.878f), L(23.5f, 53.5f, 3, 1, 0.382f), L(-62.5f, -2.5f, 0, 0, 0.779f), L(-5.5f, -36.5f, 3, 0, 0.185f),
            L(-26.5f, -14.5f, 3, 0, 0.111f), L(23.5f, -32.5f, 3, 0, 0.424f), L(-42.5f, 21.5f, 2, 2, 0.561f), L(-89.5f, -15.5f, 2, 2, 0.923f),
        }),
        S("station_01", new[] {
            L(-51.5f, 92.5f, 4, 3, 0.897f), L(-102.5f, 91.5f, 4, 4, 0.842f), L(70.5f, 76.5f, 4, 4, 0.761f), L(74.5f, -93.5f, 0, 4, 0.609f),
            L(55.5f, -81.5f, 0, 4, 0.976f), L(-113.5f, 18.5f, 1, 3, 0.181f), L(114.5f, 12.5f, 1, 4, 0.383f), L(-71.5f, -14.5f, 3, 1, 0.555f),
            L(13.5f, 33.5f, 0, 0, 0.972f), L(97.5f, -35.5f, 3, 0, 0.376f), L(43.5f, -34.5f, 3, 1, 0.389f), L(-26.5f, 43.5f, 0, 0, 0.698f),
            L(92.5f, 17.5f, 3, 0, 0.226f), L(-59.5f, -2.5f, 0, 1, 0.163f), L(50.5f, 38.5f, 2, 2, 0.218f), L(-76.5f, 71.5f, 2, 2, 0.181f),
        }),
        S("station_02", new[] {
            L(-35.5f, 111.5f, 4, 3, 0.862f), L(22.5f, 105.5f, 4, 4, 0.157f), L(-55.5f, 87.5f, 4, 4, 0.132f), L(-1.5f, -111.5f, 0, 4, 0.084f),
            L(11.5f, -98.5f, 0, 4, 0.764f), L(-107.5f, -34.5f, 1, 3, 0.877f), L(107.5f, -22.5f, 1, 4, 0.661f), L(-36.5f, 5.5f, 0, 0, 0.755f),
            L(29.5f, -15.5f, 3, 1, 0.555f), L(-80.5f, -1.5f, 3, 0, 0.841f), L(77.5f, -0.5f, 3, 0, 0.980f), L(-6.5f, -20.5f, 0, 0, 0.621f),
            L(74.5f, -30.5f, 3, 0, 0.155f), L(49.5f, 4.5f, 0, 1, 0.189f), L(-2.5f, 57.5f, 2, 2, 0.865f), L(-26.5f, -60.5f, 2, 2, 0.399f),
        }),
        S("station_03", new[] {
            L(59.5f, 98.5f, 4, 3, 0.299f), L(77.5f, 80.5f, 4, 4, 0.189f), L(-62.5f, 62.5f, 4, 4, 0.463f), L(54.5f, -101.5f, 0, 4, 0.255f),
            L(-49.5f, -97.5f, 0, 4, 0.466f), L(-113.5f, 7.5f, 1, 3, 0.703f), L(114.5f, 10.5f, 1, 4, 0.549f), L(26.5f, -1.5f, 3, 1, 0.577f),
            L(-38.5f, 6.5f, 3, 0, 0.331f), L(63.5f, -30.5f, 3, 0, 0.494f), L(-53.5f, -54.5f, 0, 0, 0.504f), L(83.5f, 8.5f, 3, 0, 0.021f),
            L(-84.5f, 11.5f, 0, 0, 0.152f), L(-15.5f, 18.5f, 3, 1, 0.346f), L(27.5f, -49.5f, 2, 2, 0.219f), L(61.5f, 41.5f, 2, 2, 0.745f),
        }),
        S("ringstation_00", new[] {
            L(40.5f, 102.5f, 4, 3, 0.276f), L(28.5f, 81.5f, 4, 4, 0.507f), L(68.5f, 50.5f, 4, 4, 0.125f), L(40.5f, -103.5f, 0, 4, 0.799f),
            L(-61.5f, -91.5f, 0, 4, 0.906f), L(-113.5f, 9.5f, 1, 3, 0.006f), L(115.5f, -39.5f, 1, 4, 0.026f), L(-29.5f, -41.5f, 3, 1, 0.103f),
            L(-43.5f, 42.5f, 3, 0, 0.468f), L(11.5f, 25.5f, 3, 0, 0.910f), L(77.5f, 1.5f, 3, 0, 0.149f), L(13.5f, -62.5f, 3, 0, 0.648f),
            L(69.5f, -70.5f, 3, 0, 0.500f), L(-87.5f, 34.5f, 3, 1, 0.619f), L(-62.5f, -7.5f, 2, 2, 0.260f), L(43.5f, -13.5f, 2, 2, 0.979f),
        }),
        S("ringstation_01", new[] {
            L(74.5f, 67.5f, 4, 3, 0.224f), L(58.5f, 55.5f, 4, 4, 0.737f), L(78.5f, -79.5f, 0, 4, 0.433f), L(-17.5f, -58.5f, 0, 4, 0.770f),
            L(-110.5f, 42.5f, 1, 3, 0.905f), L(110.5f, -29.5f, 1, 4, 0.222f), L(2.5f, -34.5f, 3, 0, 0.146f), L(-16.5f, 53.5f, 0, 1, 0.211f),
            L(-68.5f, 3.5f, 3, 0, 0.255f), L(44.5f, -48.5f, 3, 0, 0.921f), L(-36.5f, -15.5f, 3, 1, 0.157f), L(83.5f, -6.5f, 3, 0, 0.936f),
            L(-94.5f, 24.5f, 3, 0, 0.860f), L(32.5f, 26.5f, 2, 2, 0.006f), L(83.5f, 28.5f, 2, 2, 0.033f),
        }),
        S("ringstation_02", new[] {
            L(-80.5f, 82.5f, 4, 3, 0.449f), L(48.5f, 74.5f, 4, 4, 0.274f), L(63.5f, -86.5f, 0, 4, 0.808f), L(-28.5f, -78.5f, 0, 4, 0.617f),
            L(-111.5f, 28.5f, 1, 3, 0.367f), L(112.5f, -15.5f, 1, 4, 0.669f), L(15.5f, -40.5f, 3, 1, 0.411f), L(-61.5f, -18.5f, 3, 0, 0.464f),
            L(-27.5f, 53.5f, 3, 0, 0.260f), L(68.5f, 19.5f, 3, 0, 0.541f), L(-27.5f, -30.5f, 3, 0, 0.683f), L(-82.5f, 13.5f, 3, 1, 0.145f),
            L(2.5f, 47.5f, 3, 0, 0.410f), L(97.5f, -57.5f, 2, 2, 0.536f), L(65.5f, -16.5f, 2, 2, 0.344f),
        }),
        S("ringstation_03", new[] {
            L(-86.5f, 86.5f, 4, 3, 0.926f), L(14.5f, 84.5f, 4, 4, 0.726f), L(80.5f, 46.5f, 4, 4, 0.284f), L(-25.5f, -85.5f, 0, 4, 0.395f),
            L(19.5f, -82.5f, 0, 4, 0.156f), L(-111.5f, 34.5f, 1, 3, 0.547f), L(112.5f, -19.5f, 1, 4, 0.351f), L(-81.5f, 11.5f, 0, 0, 0.726f),
            L(1.5f, -31.5f, 3, 1, 0.976f), L(-54.5f, 59.5f, 3, 0, 0.985f), L(-50.5f, -16.5f, 0, 1, 0.078f), L(26.5f, -38.5f, 0, 0, 0.418f),
            L(-92.5f, 23.5f, 3, 0, 0.166f), L(-68.5f, 59.5f, 0, 0, 0.498f), L(78.5f, -64.5f, 2, 2, 0.342f), L(36.5f, 46.5f, 2, 2, 0.515f),
        }),
        // Codex's high-resolution ring station (its own 1254 px sprite, not an
        // fx cell): station_lights.py --standalone, in that sprite's pixels.
        S("station_ring_v2", new[] {
            L(7.5f, 582.5f, 4, 3, 0.586f),
            L(-60.5f, 493.5f, 4, 4, 0.362f),
            L(-357.5f, 394.5f, 4, 4, 0.053f),
            L(18.5f, -546.5f, 0, 4, 0.651f),
            L(-232.5f, -410.5f, 0, 4, 0.950f),
            L(-540.5f, 13.5f, 1, 3, 0.593f),
            L(561.5f, -54.5f, 1, 4, 0.569f),
            L(206.5f, 216.5f, 3, 1, 0.746f),
            L(39.5f, -54.5f, 3, 0, 0.620f),
            L(-279.5f, 133.5f, 3, 1, 0.152f),
            L(128.5f, -289.5f, 3, 0, 0.085f),
            L(-86.5f, 269.5f, 3, 0, 0.481f),
            L(-80.5f, -258.5f, 3, 0, 0.376f),
            L(185.5f, -23.5f, 0, 1, 0.381f),
            L(457.5f, -201.5f, 2, 2, 0.040f),
            L(441.5f, -7.5f, 2, 2, 0.442f),
        }),
        S("moon", new[] {
            L(-82.5f, -0.5f, 1, 3, 0.254f), L(83.5f, -0.5f, 1, 4, 0.384f), L(-39.5f, 0.5f, 3, 1, 0.879f), L(28.5f, 72.5f, 0, 0, 0.207f),
            L(42.5f, -72.5f, 3, 0, 0.842f), L(24.5f, 14.5f, 3, 1, 0.152f), L(41.5f, -24.5f, 3, 0, 0.857f), L(57.5f, 30.5f, 3, 0, 0.492f),
            L(-2.5f, 107.5f, 2, 2, 0.142f), L(77.5f, -47.5f, 2, 2, 0.405f),
        }),
    };

    // "mini_station_02" -> "station_02"; null for anything that is not a station cell.
    public static string CellOf(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return null;
        string n = spriteName.StartsWith("mini_", System.StringComparison.Ordinal) ? spriteName.Substring(5) : spriteName;
        return LampsOf(n) != null ? n : null;
    }

    // The lamps of a full-size station cell; null if it is not one.
    public static Lamp[] LampsOf(string cell)
    {
        foreach (var a in Anchors) if (a.name == cell) return a.lamps;
        return null;
    }

    // Does this sprite (a station cell or its mini) carry lamps? Allocation-free.
    public bool Covers(Sprite s) { return s != null && cells.ContainsKey(s); }

    // CPU mirror of the shader's Level() (tests, previews): 0..1 brightness of
    // a lamp of `pattern` with `seed` at blink clock `time`.
    public static float Level(int pattern, float time, float seed)
    {
        float t = time + seed * 10f;
        switch (pattern)
        {
            case Pulse:
            {
                float f = t / 1.2f - Mathf.Floor(t / 1.2f);
                return f < 0.45f ? 1f : (f < 0.6f ? 0.5f : 0.12f);
            }
            case Double:
            {
                float s = t - 2.4f * Mathf.Floor(t / 2.4f);
                return (s < 0.12f || (s >= 0.26f && s < 0.38f)) ? 1f : 0f;
            }
            case Strobe:
            {
                float s = t - 1.6f * Mathf.Floor(t / 1.6f);
                return s < 1.3f ? 1f : 0.1f;
            }
            case Flicker:
            {
                float slot = Mathf.Floor(t * 4f);
                float x = Mathf.Sin((slot % 960f) * 12.9898f + seed * 78.233f) * 43758.5453f;
                float h = x - Mathf.Floor(x);
                return h < 0.2f ? 0.15f : (h < 0.5f ? 0.55f : 1f);
            }
            default:
            {
                float b = t - 1.5f * Mathf.Floor(t / 1.5f);
                return b < 0.15f ? 1f : 0f;
            }
        }
    }

    // One per pooled piece that may show station art.
    public class Rig
    {
        public BackdropPiece piece;
        public MeshRenderer renderer;
        public MeshFilter filter;
        public Transform transform;
        public Sprite bound;            // the sprite the lamps were laid out for
        public string cell;             // its station cell, or null: lamps off
        public float offset;            // this station's own place in the blink cycle
        public float size;              // lamp width this frame, world units
        public int order;
    }

    struct Cell { public Mesh mesh; public Vector3 scale; public string name; }

    static readonly int IdBlinkTime = UnityEngine.Shader.PropertyToID("_BlinkTime");
    static readonly int IdTint = UnityEngine.Shader.PropertyToID("_Tint");
    static readonly int IdSize = UnityEngine.Shader.PropertyToID("_Size");
    static readonly int IdMinPx = UnityEngine.Shader.PropertyToID("_MinPx");

    readonly Dictionary<Sprite, Cell> cells = new Dictionary<Sprite, Cell>();
    readonly List<Mesh> meshes = new List<Mesh>();
    public readonly List<Rig> Rigs = new List<Rig>();
    readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    readonly System.Random rng = new System.Random(2077);   // own stream: the director's spawn sequence is untouched
    Material material;
    float clock;

    public Material Material { get { return material; } }
    public float Clock { get { return clock; } }

    // standalone: stations drawn as their own sprites (station_ring_v2).
    public SpaceStationLights(BackdropAtlas fx, params Sprite[] standalone)
    {
        var shader = Resources.Load<UnityEngine.Shader>(ShaderPath);
        if (shader != null) material = new Material(shader) { name = "SpaceStationLights" };
        if (standalone != null)
            foreach (var sprite in standalone)
            {
                if (sprite == null || cells.ContainsKey(sprite)) continue;
                var lamps = LampsOf(sprite.name);
                if (lamps == null) continue;
                var mesh = BuildMesh(lamps, sprite.name);
                meshes.Add(mesh);
                cells[sprite] = new Cell { mesh = mesh, scale = Vector3.one, name = sprite.name };
            }
        if (fx == null) return;
        foreach (var a in Anchors)
        {
            Sprite full = fx.Get(a.name);
            if (full == null) continue;
            var mesh = BuildMesh(a.lamps, a.name);
            meshes.Add(mesh);
            cells[full] = new Cell { mesh = mesh, scale = Vector3.one, name = a.name };
            Sprite mini = fx.Get("mini_" + a.name);
            if (mini == null) continue;
            // A mini is the same drawing pre-shrunk: the same lamps, scaled to its bounds.
            Vector3 f = full.bounds.size, m = mini.bounds.size;
            cells[mini] = new Cell { mesh = mesh, scale = new Vector3(m.x / f.x, m.y / f.y, 1f), name = a.name };
        }
    }

    static Mesh BuildMesh(Lamp[] lamps, string name)
    {
        int n = lamps.Length;
        var verts = new Vector3[n * 4];
        var corners = new Vector2[n * 4];
        var data = new List<Vector4>(n * 4);
        var colors = new Color[n * 4];
        var tris = new int[n * 6];
        for (int i = 0; i < n; i++)
        {
            var l = lamps[i];
            var c = new Vector3(l.x / BackdropAtlas.PixelsPerUnit, l.y / BackdropAtlas.PixelsPerUnit, 0f);
            for (int k = 0; k < 4; k++)
            {
                verts[i * 4 + k] = c;
                corners[i * 4 + k] = new Vector2(k & 1, k >> 1);
                data.Add(new Vector4(l.pattern, l.seed, SizeFactor(l.pattern), 0f));
                colors[i * 4 + k] = Colors[l.color];
            }
            tris[i * 6 + 0] = i * 4 + 0; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
            tris[i * 6 + 3] = i * 4 + 1; tris[i * 6 + 4] = i * 4 + 2; tris[i * 6 + 5] = i * 4 + 3;
        }
        var mesh = new Mesh { name = "StationLights_" + name };
        mesh.vertices = verts;
        mesh.uv = corners;
        mesh.SetUVs(1, data);
        mesh.colors = colors;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        var b = mesh.bounds;
        b.Expand(0.2f);                 // the squares are grown in the vertex stage
        mesh.bounds = b;
        mesh.UploadMeshData(false);
        return mesh;
    }

    // Gives every piece of the pool its (hidden) lamp renderer, once.
    public void Attach(BackdropPool pool)
    {
        foreach (var p in pool.items)
        {
            var go = new GameObject("lights");
            go.transform.SetParent(p.body, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.enabled = false;
            Rigs.Add(new Rig { piece = p, renderer = mr, filter = mf, transform = go.transform, order = int.MinValue });
        }
    }

    public void Tick(float dt, float alpha)
    {
        clock = Mathf.Repeat(clock + dt, ClockWrap);
        for (int i = 0; i < Rigs.Count; i++)
        {
            var r = Rigs[i];
            var p = r.piece;
            if (!p.active) continue;            // its root is inactive: nothing draws
            Sprite s = p.sr.sprite;
            if (s != r.bound) Bind(r, s);
            if (r.cell == null) continue;
            if (r.order != p.sr.sortingOrder + 1)
            {
                r.order = p.sr.sortingOrder + 1;
                r.renderer.sortingOrder = r.order;
            }
            Color c = p.sr.color;               // depth-tier light, cross-fade alpha
            float k = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * Gain;
            r.size = Mathf.Clamp(p.size * SizePerUnit, SizeMin, SizeMax);
            mpb.Clear();
            mpb.SetFloat(IdBlinkTime, Mathf.Repeat(clock + r.offset, ClockWrap));
            mpb.SetColor(IdTint, new Color(k, k, k, c.a));
            mpb.SetFloat(IdSize, r.size);
            mpb.SetFloat(IdMinPx, MinPx);
            r.renderer.SetPropertyBlock(mpb);
        }
    }

    void Bind(Rig r, Sprite s)
    {
        r.bound = s;
        Cell cell;
        if (material == null || s == null || !cells.TryGetValue(s, out cell))
        {
            r.cell = null;
            r.renderer.enabled = false;
            return;
        }
        r.cell = cell.name;
        r.filter.sharedMesh = cell.mesh;
        r.transform.localScale = cell.scale;
        r.offset = (float)rng.NextDouble() * ClockWrap;
        r.renderer.enabled = true;
    }

    // The blink clock this rig's renderer is fed (tests).
    public float TimeOf(Rig r) { return Mathf.Repeat(clock + r.offset, ClockWrap); }

    public void Destroy()
    {
        foreach (var r in Rigs) if (r.filter != null) r.filter.sharedMesh = null;
        foreach (var m in meshes) BackdropAtlas.Kill(m);
        meshes.Clear();
        cells.Clear();
        BackdropAtlas.Kill(material);
        material = null;
    }
}
