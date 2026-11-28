using System.Collections.Generic;
using UnityEngine;

// The ambient loops of the Desert backdrop and the MEASURED points they sit on,
// as data.
//
// Loops are flipbooks from the run C atlases (256 px cells), declared below as
// DATA -- name, atlas, fps, draw alpha, scale, centred or foot-anchored:
//
//   smoke    smoke_a (dark smoke), smoke_b (a pale puff: the docks' launch mist)
//   dust     dustdevil (a free dust devil wandering the dunes), sandplume (a crest plume),
//            sandfall, grit_gust (a free gust)
//   shimmer  heat_shimmer, mirage_glint (free, salt flats)
// (2026-11-28: the round-1 landmark / pipe loops -- heat vents, leaks, lamps, glass sparkle -- retired with their pieces; the
// living props carry their own lights and smoke in their strips, DesertProps.cs)
//
// A loop whose atlas (or frames) is not installed is simply skipped by
// AmbientEmitters (no frames: nothing shows) and Missing() lists it.
//
// Everything positional comes from points.json (Backdrop3/points.json), which
// Art/Worlds/Desert/backdrop_v3~/src~/measure_points.py measures on the pixels --
// never by eye: every piece's EMITTER points (stack mouths, mast tips, a well's
// hollow, a glass bowl's centre), its opaque box and pipe-end flange faces, and
// each loop's own ANCHOR. Rerun the script after any art change.
public static class DesertAmbientCatalog
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
        ("smoke_a", "smoke", 6f, .66f, 1.3f, false), ("smoke_b", "smoke", 7f, .66f, 1.2f, false),
        ("dustdevil", "dust", 8f, .6f, 1.2f, false), ("sandplume", "dust", 8f, .6f, 1.1f, false),
        ("sandfall", "dust_2", 8f, .68f, 1f, false), ("grit_gust", "dust_2", 10f, .43f, 1f, true),
        ("heat_shimmer", "shimmer", 7f, .2f, 1.2f, false), ("mirage_glint", "shimmer", 6f, .27f, 1f, true),
    };

    // The blinking lamp loops (r1: none -- the props' lamps are baked into their own strips, DesertProps.cs).
    public static bool Blinks(string loop) { return false; }

    // Sparse glints that twinkle by design (r1: none left).
    public static bool Twinkles(string loop) { return false; }

    // Rising plumes: the loops whose base must sit on their emitter point.
    public static bool Plume(string loop)
    {
        return loop == "smoke_a" || loop == "smoke_b" || loop == "sandplume" || loop == "heat_shimmer";
    }

    // Loops that wander the open ground on their own instead of riding a piece
    // (DesertDirector's wanderers): a dust devil on the dunes, a mirage glint over
    // the pans, a gust of grit.
    public static readonly string[] WandererNames = { "dustdevil", "grit_gust", "mirage_glint" };
    public static float FpsOf(string loop) { foreach (var s in Specs) if (s.name == loop) return s.fps; return 8f; }
    public static float AlphaOf(string loop) { foreach (var s in Specs) if (s.name == loop) return s.alpha; return 1f; }
    public static bool Centred(string loop) { foreach (var s in Specs) if (s.name == loop) return s.centred; return true; }

    public static IEnumerable<string> LoopNames { get { foreach (var s in Specs) yield return s.name; } }
    public static string AtlasOf(string loop) { foreach (var s in Specs) if (s.name == loop) return s.atlas; return null; }

    static PFile data;
    static Dictionary<string, PPiece> byName;
    static AmbientTable table;
    static HashSet<string> installed;      // every sprite name in the loop atlases that exist

    static void Load()
    {
        if (data != null) return;
        var t = Resources.Load<TextAsset>(BackdropCatalog.Folder("Desert") + PointsFile);
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
            var t = Resources.Load<TextAsset>(BackdropCatalog.Folder("Desert") + s.atlas);
            var tex = Resources.Load<Texture2D>(BackdropCatalog.Folder("Desert") + s.atlas);
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
                plumeShare = () => DesertTuning.PlumeShare,
                plumeLift = () => DesertTuning.PlumeLift,
                brightness = () => DesertTuning.LoopBrightness,
                lightBoost = () => DesertTuning.LightBoostNow,
            };
            return table;
        }
    }

    public const int MaxPerPiece = 4;

    // Drop the cached data (after re-measuring in the editor, after installing run C; tests).
    public static void Reload() { data = null; byName = null; table = null; installed = null; }
}
