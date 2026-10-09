using System.Collections.Generic;
using UnityEngine;

// The ambient loops of the Tide backdrop and the MEASURED points they sit on,
// as data.
//
// Loops are flipbooks from the run C atlases (256 px cells), declared below as
// DATA -- name, atlas, fps, draw alpha, scale, centred or foot-anchored -- so
// run C can be dropped in afterwards with no code change:
//
//   smoke   smoke_a (thick stack smoke), smoke_b (lighter column)
//   flames  flare (open flare-stack flame)
//   surf    wave_crest_a / wave_crest_b (free ocean crests), foam_ring (a ring
//           of foam round a pile), ripple, wake (behind a vessel), whirlpool
//           (a vortex's eye), spray, caustic (light dapple over shallows)
//   leaks   steam_vent, pipe_bubbles, bubble_stream (vents, crawler),
//           vent_gas, oil_drip
//   lights  beacon_mint, beacon_amber, strobe_white, window_lights
//
// A loop whose atlas (or frames) is not installed is simply skipped by
// AmbientEmitters (no frames: nothing shows), and Missing() lists what is
// still to be painted (TideBackdropTest prints it): Present(loop) is the check.
//
// Everything positional comes from points.json (Backdrop3/points.json), which
// Art/Worlds/Tide/backdrop_v3~/src~/measure_points.py measures on the pixels --
// never by eye (the run B manifest's emitter points are nominal): every
// piece's EMITTER points (derrick tips and column tops for smoke, flare
// mouths, vent craters, the break of a leaking pipe, lamp knobs, a whirlpool's
// eye), its opaque box and pipe-end flange faces; and, once run C is in, each
// loop's ANCHOR (the point of its cell put on the emitter). Rerun the script
// after any art change; the tests hold the drawn plumes to these points.
public static class TideAmbientCatalog
{
    [System.Serializable] public class PEmit { public string loop; public float x, y, scale; }
    [System.Serializable] public class PEnd { public float x, y; }
    [System.Serializable] public class PPiece { public string name, atlas; public int[] box; public PEmit[] emit; public PEnd[] ends; }
    [System.Serializable] public class PLoop { public string name, kind; public float x, y, spread; }
    [System.Serializable] class PFile { public PPiece[] pieces; public PLoop[] loops; }
    [System.Serializable] class JRect { public string n; }
    [System.Serializable] class JAtlas { public JRect[] sprites; }

    public const string PointsFile = "points";

    // name, atlas, fps, draw alpha, scale on its piece, centred (anchor at the
    // cell's middle: vortex eye, rings, crests) or a foot (bottom-centre,
    // (128, 236): plumes, flames, lamps)
    static readonly (string name, string atlas, float fps, float alpha, float scale, bool centred)[] Specs =
    {
        ("smoke_a", "smoke", 8f, .85f, 1.5f, false), ("smoke_b", "smoke", 9f, .8f, 1.4f, false),
        ("flare", "flames", 12f, .9f, 1f, false),
        ("steam_vent", "leaks", 10f, .75f, 1.3f, false), ("pipe_bubbles", "leaks", 9f, .8f, 1f, false),
        ("bubble_stream", "leaks", 9f, .85f, 1.2f, false), ("vent_gas", "leaks", 8f, .6f, 1.2f, false),
        ("oil_drip", "leaks", 9f, .8f, 1f, false),
        ("beacon_mint", "lights", 4f, .9f, 1.4f, false), ("beacon_amber", "lights", 4f, .9f, 1.4f, false),
        ("strobe_white", "lights", 4f, .8f, 1.2f, false), ("window_lights", "lights", 5f, .6f, 1f, true),
        ("wave_crest_a", "surf", 8f, .55f, 1f, true), ("wave_crest_b", "surf", 8f, .55f, 1f, true),
        ("foam_ring", "surf", 8f, .6f, 1f, true), ("ripple", "surf", 8f, .5f, 1f, true),
        ("wake", "surf", 8f, .7f, 1f, true), ("whirlpool", "surf", 10f, .8f, 1f, true),
        ("spray", "surf", 10f, .7f, 1f, false), ("caustic", "surf", 6f, .5f, 1f, true),
    };

    // Rising plumes: the loops whose base must sit on their emitter point.
    public static bool Plume(string loop)
    {
        return loop == "smoke_a" || loop == "smoke_b" || loop == "steam_vent" || loop == "vent_gas" || loop == "bubble_stream";
    }

    // Loops that wander the open water on their own instead of riding a piece
    // (TideDirector's crests).
    public static readonly string[] Crests = { "wave_crest_a", "wave_crest_b" };

    public static IEnumerable<string> LoopNames { get { foreach (var s in Specs) yield return s.name; } }
    public static string AtlasOf(string loop) { foreach (var s in Specs) if (s.name == loop) return s.atlas; return null; }

    static PFile data;
    static Dictionary<string, PPiece> byName;
    static AmbientTable table;
    static HashSet<string> installed;      // every sprite name in the loop atlases that exist

    static void Load()
    {
        if (data != null) return;
        var t = Resources.Load<TextAsset>(BackdropCatalog.Folder("Tide") + PointsFile);
        data = t != null ? JsonUtility.FromJson<PFile>(t.text) : null;
        if (data == null) data = new PFile { pieces = new PPiece[0], loops = new PLoop[0] };
        byName = new Dictionary<string, PPiece>();
        foreach (var p in data.pieces) byName[p.name] = p;
    }

    public static PPiece Piece(string name)
    {
        Load();
        PPiece p;
        return name != null && byName.TryGetValue(name, out p) ? p : null;
    }

    public static PLoop[] MeasuredLoops { get { Load(); return data.loops; } }
    public static PPiece[] Pieces { get { Load(); return data.pieces; } }

    // The run C atlases that are installed and the sprite names in them.
    static void LoadInstalled()
    {
        if (installed != null) return;
        installed = new HashSet<string>();
        var seen = new HashSet<string>();
        foreach (var s in Specs)
        {
            if (!seen.Add(s.atlas)) continue;
            var t = Resources.Load<TextAsset>(BackdropCatalog.Folder("Tide") + s.atlas);
            var tex = Resources.Load<Texture2D>(BackdropCatalog.Folder("Tide") + s.atlas);
            if (t == null || tex == null) continue;
            var a = JsonUtility.FromJson<JAtlas>(t.text);
            if (a != null && a.sprites != null) foreach (var r in a.sprites) installed.Add(s.atlas + "/" + r.n);
        }
    }

    // Is the loop's first frame (<name>_00) in its installed atlas?
    public static bool Present(string loop)
    {
        LoadInstalled();
        string atlas = AtlasOf(loop);
        return atlas != null && installed.Contains(atlas + "/" + loop + "_00");
    }

    // The loops still waiting for run C art (empty once it has landed).
    public static List<string> Missing()
    {
        var list = new List<string>();
        foreach (var s in Specs) if (!Present(s.name)) list.Add(s.name);
        return list;
    }

    public static AmbientTable Table
    {
        get
        {
            if (table != null) return table;
            Load();
            var loops = new FrostAmbientCatalog.Loop[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                var s = Specs[i];
                Vector2 anchor = s.centred ? new Vector2(128f, 128f) : new Vector2(128f, 236f);
                foreach (var m in data.loops) if (m.name == s.name) anchor = new Vector2(m.x, m.y);
                loops[i] = new FrostAmbientCatalog.Loop { name = s.name, atlas = s.atlas, fps = s.fps, alpha = s.alpha, scale = s.scale, anchor = anchor };
            }
            var bindings = new List<FrostAmbientCatalog.Binding>();
            foreach (var p in data.pieces)
            {
                if (p.emit == null || p.emit.Length == 0) continue;
                var e = new FrostAmbientCatalog.Emitter[Mathf.Min(p.emit.Length, MaxPerPiece)];
                for (int k = 0; k < e.Length; k++)
                    e[k] = new FrostAmbientCatalog.Emitter { loop = p.emit[k].loop, at = new Vector2(p.emit[k].x, p.emit[k].y), scale = p.emit[k].scale, alpha = 1f };
                bindings.Add(new FrostAmbientCatalog.Binding { piece = p.name, emitters = e });
            }
            table = new AmbientTable
            {
                loops = loops, bindings = bindings.ToArray(), maxPerPiece = MaxPerPiece,
                lifted = a => false,
                plumeShare = () => TideTuning.PlumeShare,
                plumeLift = () => TideTuning.PlumeLift,
                brightness = () => TideTuning.LoopBrightness,
                lightBoost = () => TideTuning.LightBoostNow,
            };
            return table;
        }
    }

    public const int MaxPerPiece = 4;

    // Drop the cached data (after re-measuring in the editor, after installing run C; tests).
    public static void Reload() { data = null; byName = null; table = null; installed = null; }
}
