using UnityEngine;

// Verdant's LIVING PROPS ART (r1, user-approved 2026-11-28): the atlas of every prop, installed per ground set -- Backdrop3/v<n>/props.png + props.json
// (built by ../Art/Worlds/Verdant/backdrop_r1~/build_r1.py, modelled on Desert's / Ember's atlas builders). NOTHING DRAWS IT YET: the placement
// (designed settlements on clearings joined by paths, the big landmarks, the keep-outs) is another agent's work; this file only names the art.
// VerdantTuning.PropsEnabled (false) keeps the director from loading the atlas and WorldArtPreload from queueing it until that lands.
//
//   KINDS      11 approved kinds x 4 variants (liveries) x 8 frames (125 ms: lamps blink, water falls, smoke curls), cut to each kind's union
//              box at its DRAWN size on a 1080 px screen at the .49 altitude (Px, composite.py GROUND) with the cell's centre as the sprite's
//              pivot (props.json px / py), named <kind>_<variant>_<frame:00> ("crashplane_2_05"); the frames that only differ where a lamp
//              blinks share their rect when identical. A kind of mountain scale (ridge, karst) is drawn from frame 0 only.
//   BEASTWALK  six walkers (variants 0-5, 8 frames of 110 ms, "beastwalk_<walker>_<frame>"), cut from the herd, drawn small (the beasts' scale):
//              mirrored to face the heading by the placement (the art walks to the RIGHT). They wander inside the 'beasts' clearing.
//   BLEND      each layout's sprites are tone-matched to that layout's OWN mid tile (a few % pull toward its ground colour, highlights eased
//              over the ground's bright value, lamps untouched; v4 carries the night grade); the strips were already blended once
//              (palette pull, feathered feet, directional contact shadow), so nothing here is a second full blend.
// PLACEMENT HOOK: the partial VerdantDirector at the bottom of this file (BuildProps, called from Build after the fires / sites pools exist;
// StepProps(dt, scrolled), called from Step before the fires and sites are stepped) -- move them into a VerdantProps.cs when the placement
// lands. Props ride the ground plane exactly like Desert's / Ember's: Pool(VerdantPropsArt.Layer, n, false, orderOffset) with orderOffset
// -6 (landforms) / -3 (props), under the fires (-2) and the sites (0); pinned (planner.Y(gy), Place(p)), claimed in `planner` (capacity 96).
public static class VerdantPropsArt
{
    public const string Atlas = "props";             // in each layout folder (v<n>/props.png + .json)
    public const string Layer = "ground";
    public const string Beastwalk = "beastwalk";
    public const int Walkers = 6;
    public const int Frames = 8, Variants = 4, FrameMs = 125, WalkFrameMs = 110;

    public struct Kind
    {
        public string name;
        public float px;          // drawn cell width on a 1080 px screen at the .49 altitude
        public bool landform;     // hard-pinned, mountain-scale (own size cap, own keep-out core)
    }
    public static readonly Kind[] Kinds =
    {
        new Kind { name = "crashplane", px = 180 }, new Kind { name = "outpost", px = 190 }, new Kind { name = "terraces", px = 200 },
        new Kind { name = "flowers", px = 150 }, new Kind { name = "beasts", px = 200 }, new Kind { name = "stilt", px = 190 },
        new Kind { name = "ropebridge", px = 210 },
        new Kind { name = "waterfall", px = 400, landform = true }, new Kind { name = "karst", px = 420, landform = true },
        new Kind { name = "ridge", px = 560, landform = true }, new Kind { name = "starship", px = 430, landform = true },
    };
    public static int Count => Kinds.Length;
    public static int IndexOf(string name) { for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].name == name) return i; return -1; }
    // the sprite of a kind's variant and frame in the layout atlas
    public static string Sprite(string kind, int variant, int frame) { return kind + "_" + variant + "_" + frame.ToString("00"); }
    // the strip name BackdropAtlas.Frames takes (all frames of a variant)
    public static string Strip(int kind, int variant) { return Kinds[kind].name + "_" + variant; }
    public static string WalkStrip(int walker) { return Beastwalk + "_" + walker; }
    public const float ScreenPx = 1080f;
    public static float Units(float px, float halfW) { return px / ScreenPx * 2f * halfW; }
    public static float CellSize(int kind, float halfW) { return Units(Kinds[kind].px, halfW); }
}

public partial class VerdantDirector
{
    BackdropAtlas propArt;
    public BackdropAtlas PropArt => propArt;     // the layout's props atlas (null while VerdantTuning.PropsEnabled is false)

    // Called from Build: loads the layout's props atlas ONLY when the props are enabled (nothing draws them until the placement exists).
    void BuildProps()
    {
        propArt = VerdantTuning.PropsEnabled ? set.VariantAtlas(VerdantPropsArt.Atlas) : null;
    }

    // Called from Step each frame: placement hooks go here (props ride the ground plane: pinned to the mid tile, planner.Y(gy)).
    void StepProps(float dt, float scrolled) { }
}
