using System.Collections.Generic;
using UnityEngine;

// The ambient loops of the Ember backdrop and the MEASURED points they sit
// on, as data.
//
// Loops are flipbooks from the run C atlases (smoke, lavafire, eruption,
// leaks, lights: 256 px cells). fps / draw alpha start from the run C
// manifest (Art/Worlds/Ember/backdrop_v3~/src~/manifest.json); the dark
// smokes are drawn stronger than recommended so they read over the dark
// ground. Everything positional comes from points.json (Backdrop3/points.json),
// which Art/Worlds/Ember/backdrop_v3~/src~/measure_points.py measures on the
// pixels -- never by eye (the run B manifest's emitter points are nominal):
//   * every loop's ANCHOR, the point of its cell put on the emitter: the
//     plume / flame / lantern foot (bottom-centre of the opaque art, which
//     the run C art aligned to (128, 236); the script finds them within a
//     few px);
//   * every piece's EMITTER points: stack mouths (column-top profile of the
//     opaque art: the centre of the dark mouth under a chimney's rim), the
//     lava-lit rim of a cooling tower, the crater centre of a lava fountain
//     or eruption scar, the hottest blobs of pools, coal beds and lava
//     channels, mast lamp knobs, the mouth of a venting or spilling pipe;
//     and its opaque box and pipe-end flange faces.
// Rerun the script after any art change; the tests hold the drawn plumes to
// these points (EmberBackdropTest).
public static class EmberAmbientCatalog
{
    [System.Serializable] public class PEmit { public string loop; public float x, y, scale; }
    [System.Serializable] public class PEnd { public float x, y; }
    [System.Serializable] public class PPiece { public string name, atlas; public int[] box; public PEmit[] emit; public PEnd[] ends; }
    [System.Serializable] public class PLoop { public string name, kind; public float x, y, spread; }
    [System.Serializable] class PFile { public PPiece[] pieces; public PLoop[] loops; }

    public const string PointsFile = "points";

    // name, atlas, fps, draw alpha and the loop's scale on its piece (the
    // plumes fill only ~a third of their cell: drawn up to 1.6x so a stack's
    // column reads on a phone; the anchor stays on the measured point
    // whatever the scale)
    static readonly (string name, string atlas, float fps, float alpha, float scale)[] Specs =
    {
        ("smoke_a", "smoke", 8f, .85f, 1.6f), ("smoke_b", "smoke", 9f, .8f, 1.5f),
        ("flare", "lavafire", 12f, .85f, 1f), ("fountain", "lavafire", 11f, .8f, 1f),
        ("eruptsmoke_a", "eruption", 7f, .55f, 1f), ("eruptsmoke_b", "eruption", 8f, .65f, 1.3f),
        ("steam_vent", "leaks", 10f, .75f, 1.3f), ("pipe_drip", "leaks", 10f, .85f, 1f),
        ("ember_rain", "leaks", 9f, .85f, 1.2f), ("lava_bubble", "leaks", 8f, .8f, 1f),
        ("beacon_amber", "lights", 4f, .9f, 1.4f), ("beacon_magenta", "lights", 4f, .9f, 1.4f),
        ("strobe_white", "lights", 4f, .8f, 1.2f),
    };

    // Rising plumes: the loops whose base must sit on their emitter point.
    public static bool Plume(string loop)
    {
        return loop == "smoke_a" || loop == "smoke_b" || loop == "eruptsmoke_a" || loop == "eruptsmoke_b" || loop == "steam_vent";
    }

    static PFile data;
    static Dictionary<string, PPiece> byName;
    static AmbientTable table;

    static void Load()
    {
        if (data != null) return;
        var t = Resources.Load<TextAsset>(BackdropCatalog.Folder("Ember") + PointsFile);
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
                Vector2 anchor = new Vector2(128f, 236f);
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
                lifted = a => false,          // the dark ground needs no lifted smoke
                plumeShare = () => EmberTuning.PlumeShare,
                plumeLift = () => EmberTuning.PlumeLift,
                brightness = () => EmberTuning.LoopBrightness,
                lightBoost = () => EmberTuning.LightBoostNow,
            };
            return table;
        }
    }

    public const int MaxPerPiece = 4;

    // Drop the cached data (after re-measuring in the editor; tests).
    public static void Reload() { data = null; byName = null; table = null; }
}
