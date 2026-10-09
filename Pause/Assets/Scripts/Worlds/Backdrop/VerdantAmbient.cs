using System.Collections.Generic;
using UnityEngine;

// The ambient loops of the Verdant backdrop and the MEASURED points they sit
// on, as data.
//
// Loops are flipbooks from the run C atlases (smoke, wildfire, firesmoke,
// leaks, lights: 256 px cells; the lights' strobe and fireflies in 128 px
// half cells). fps / draw alpha come from the run C manifest
// (Art/Worlds/Verdant/backdrop_v3/manifest.json). Everything positional
// comes from points.json (Backdrop3/points.json), which
// Art/Worlds/Verdant/backdrop_v3/src~/measure_points.py measures on the
// pixels -- never by eye:
//   * every loop's ANCHOR, the point of its cell put on the emitter: the
//     plume / flame base for the rising loops (the smoke plumes' bases were
//     re-aligned to within 0.5 px of (128, 236) in the art; the script finds
//     them at (128 +-1, 234)), the lamp head of a beacon;
//   * every piece's EMITTER points: chimney mouths (column-top profile of
//     the opaque art, a rust-coloured spire with a wide mouth), mast-top lamp
//     knobs, the hottest pixel of a fire's blobs / three points spread along
//     a burn front, the rupture and the landing of a pipe leak's sap, dome
//     apexes; and its opaque box and pipe-end flange faces.
// Rerun the script after any art change; the tests hold the drawn plumes to
// these points (VerdantBackdropTest).
public static class VerdantAmbientCatalog
{
    [System.Serializable] public class PEmit { public string loop; public float x, y, scale; }
    [System.Serializable] public class PEnd { public float x, y; }
    [System.Serializable] public class PPiece { public string name, atlas; public int[] box; public PEmit[] emit; public PEnd[] ends; }
    [System.Serializable] public class PLoop { public string name, kind; public float x, y, spread; }
    [System.Serializable] class PFile { public PPiece[] pieces; public PLoop[] loops; }

    public const string PointsFile = "points";

    // name, atlas, fps, draw alpha (run C manifest; ember rain and fireflies
    // are painted very faint, so they are drawn a little stronger) and the
    // loop's scale on its piece (the plumes fill only ~a third of their
    // cell: drawn up to 1.8x so a stack's column reads on a phone; the
    // anchor stays on the measured point whatever the scale)
    static readonly (string name, string atlas, float fps, float alpha, float scale)[] Specs =
    {
        ("smoke_a", "smoke", 8f, .8f, 1.8f), ("smoke_b", "smoke", 9f, .8f, 1.6f),
        ("flame_front", "wildfire", 12f, .8f, 1f), ("flame_patch", "wildfire", 12f, .8f, 1.1f),
        ("wildsmoke_a", "firesmoke", 7f, .62f, 1.3f), ("wildsmoke_b", "firesmoke", 8f, .66f, 1.5f),
        ("steam_vent", "leaks", 10f, .75f, 1.3f), ("leak_sap", "leaks", 9f, .8f, 1f),
        ("ember_rain", "leaks", 8f, .85f, 1.2f), ("spore_burst", "leaks", 8f, .7f, 1f),
        ("beacon_lime", "lights", 4f, .85f, 1.4f), ("beacon_magenta", "lights", 4f, .85f, 1.4f),
        ("window_lights", "lights", 5f, .71f, 1f), ("strobe_white", "lights", 4f, .68f, 1f),
        ("fireflies", "lights", 6f, .9f, 1f),
    };

    // Rising plumes: the loops whose base must sit on their emitter point.
    public static bool Plume(string loop)
    {
        return loop == "smoke_a" || loop == "smoke_b" || loop == "wildsmoke_a" || loop == "wildsmoke_b" || loop == "steam_vent";
    }

    static PFile data;
    static Dictionary<string, PPiece> byName;
    static AmbientTable table;

    static void Load()
    {
        if (data != null) return;
        var t = Resources.Load<TextAsset>(BackdropCatalog.Folder("Verdant") + PointsFile);
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
                Vector2 anchor = s.atlas == "lights" && (s.name == "strobe_white" || s.name == "fireflies")
                    ? new Vector2(64f, 121f) : new Vector2(128f, 236f);
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
                lifted = a => a == "smoke" || a == "firesmoke",
                plumeShare = () => VerdantTuning.PlumeShare,
                plumeLift = () => VerdantTuning.PlumeLift,
                brightness = () => VerdantTuning.LoopBrightness,
                lightBoost = () => VerdantTuning.LightBoostNow,
            };
            return table;
        }
    }

    public const int MaxPerPiece = 4;

    // Drop the cached data (after re-measuring in the editor; tests).
    public static void Reload() { data = null; byName = null; table = null; }
}
