using System.Collections.Generic;
using UnityEngine;

// Which of Frost's ground-tile variant sets a landing flies over.
//
// Every entry into Frost (each new Frost BackdropSet: a planetfall, a
// portal, a run starting there) picks one of up to MaxVariants sets of
// {sky, far, mid, flow} from Backdrop3/v1..v4 -- frozen ocean, coast and
// harbour, inland tundra, glacier night. A variant whose folder is not
// installed is skipped, so dropping a new v<N>/ folder in is enough to
// enable it. With more than one installed, the same set never comes twice
// in a row. Its own random stream: gameplay's Random is never touched.
public static class FrostBackdropSelection
{
    public const int MaxVariants = 4;

    // The variant picked last (0: none yet).
    public static int Last { get; private set; }
    public static int Picks { get; private set; }

    // Test / preview hook: > 0 always picks this variant (when installed).
    public static int Force;

    static System.Random rng = new System.Random();
    static readonly int[] known = new int[MaxVariants + 1];   // 0 unknown, 1 installed, -1 missing
    static readonly int[] order = new int[MaxVariants];

    public static void Seed(int seed) { rng = new System.Random(seed); }

    public static void Reset()
    {
        Last = 0;
        Picks = 0;
        Force = 0;
        rng = new System.Random();
        System.Array.Clear(known, 0, known.Length);
    }

    // Is variant `v` of `world` on disk (its sky tile resolves)?
    public static bool Installed(string world, int v)
    {
        if (v < 1 || v > MaxVariants) return false;
        if (known[v] == 0)
            known[v] = Resources.Load<Sprite>(BackdropCatalog.TileFolder(world, v) + "sky") != null ? 1 : -1;
        return known[v] > 0;
    }

    public static int InstalledCount(string world, int count)
    {
        int n = 0;
        for (int v = 1; v <= Mathf.Min(count, MaxVariants); v++) if (Installed(world, v)) n++;
        return n;
    }

    // One variant for a new entry: random among the installed ones, never
    // the previous one when another is installed. 1 when none is (the set
    // then reports itself incomplete).
    public static int Pick(string world, int count)
    {
        count = Mathf.Clamp(count, 1, MaxVariants);
        int pick = 0;
        if (Force > 0 && Installed(world, Force)) pick = Force;
        else
        {
            for (int i = 0; i < count; i++) order[i] = i + 1;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            for (int i = 0; i < count && pick == 0; i++)
                if (order[i] != Last && Installed(world, order[i])) pick = order[i];
            if (pick == 0 && Installed(world, Last)) pick = Last;
            if (pick == 0) pick = 1;
        }
        Last = pick;
        Picks++;
        return pick;
    }
}

// The ambient loops of the Frost backdrop, as data: what animates where.
//
// Loops are flipbooks cut from the run C atlases (smoke, steam, fire_lights,
// beacons, aurora: 4x4 cells of 256 px, Unity-rect JSON, frames
// <name>_00..). Each loop's anchor is a point of its 256 cell (pixels from
// the cell's top-left; the plume's / light's base, bottom-centre for all but
// the aurora). An emitter pins a loop's anchor to a point of a landmark's
// (or launch site's) 256 cell, so a refinery's stack smokes from its stack,
// a rig's flare burns on its flare boom. Both cells share a pixel scale, so
// `scale` 1 draws the loop at the landmark's own pixel size.
//
// Missing atlases are fine: a loop whose frames are not found is skipped and
// its emitters never show (AmbientEmitters).
public static class FrostAmbientCatalog
{
    public struct Loop
    {
        public string name, atlas;
        public float fps, alpha, scale;
        public Vector2 anchor;      // cell pixels from the top-left
    }

    public struct Emitter
    {
        public string loop;
        public Vector2 at;          // the host's cell pixels from the top-left
        public float scale;         // x the loop's own scale
        public float alpha;         // x the loop's alpha
    }

    public struct Binding
    {
        public string piece;        // landmark / site sprite name
        public Emitter[] emitters;
    }

    // Every loop's opacity is multiplied by this. (The world's brightness
    // knob is FrostTuning.Brightness; of the loops only the smoke and steam
    // plumes are lifted by it, FrostTuning.PlumeShare: the lights, flames
    // and beacons are bright already.)
    public static float Brightness = 1f;
    public static bool Lifted(string atlas) { return atlas == "smoke" || atlas == "steam"; }
    // Emitters a single landmark may carry (pooled renderers per piece).
    public const int MaxPerPiece = 4;

    public const string AuroraLoop = "aurora";
    // The faint world aurora per variant (index = variant; 0 = no variant):
    // strong only over the glacier night (v4).
    public static readonly float[] AuroraAlpha = { .3f, .3f, .2f, .3f, 1f };

    // fps / anchor / alpha from the run C manifest (backdrop_v3/src~/manifest.json).
    public static readonly Loop[] Loops =
    {
        L("smoke_a", "smoke", 8f, .68f, 1.3f),
        L("smoke_b", "smoke", 8f, .72f, 1.3f),
        L("steam_vent", "steam", 10f, .75f, 1f),
        L("geyser", "steam", 8f, .70f, 1f),
        L("flare", "fire_lights", 12f, .78f, 1f),
        L("searchlight", "fire_lights", 8f, .30f, 1f),   // manifest .52; the re-drawn beam is far stronger
        L("beacon_magenta", "beacons", 4f, .70f, 1f),
        L("beacon_cyan", "beacons", 4f, .68f, 1f),
        L("strobe_white", "beacons", 8f, .55f, 1f),
        L("window_lights", "beacons", 5f, .60f, 1f),
        new Loop { name = AuroraLoop, atlas = "aurora", fps = 3f, alpha = .46f, scale = 1f, anchor = new Vector2(128, 128) },
    };

    // Emitter points measured on landmarks.png / sites.png (cell pixels from
    // the top-left of each 256 cell).
    public static readonly Binding[] Bindings =
    {
        B("rig_00", E("flare", 102, 36), E("beacon_magenta", 108, 104), E("strobe_white", 172, 128)),
        B("rig_01", E("flare", 127, 36), E("beacon_magenta", 104, 104), E("strobe_white", 184, 142)),
        B("platform_00", E("searchlight", 128, 150), E("beacon_magenta", 148, 70), E("window_lights", 112, 160)),
        B("platform_01", E("searchlight", 130, 150), E("beacon_cyan", 72, 48), E("beacon_magenta", 152, 48), E("window_lights", 100, 172)),
        B("platform_02", E("searchlight", 110, 150), E("beacon_magenta", 142, 40), E("window_lights", 96, 160)),
        B("refinery_00", E("smoke_a", 55, 72), E("smoke_b", 100, 66), E("window_lights", 100, 170)),
        B("refinery_01", E("smoke_a", 130, 30), E("smoke_b", 74, 90), E("smoke_b", 184, 96, .8f), E("window_lights", 110, 190)),
        B("relay_00", E("beacon_cyan", 98, 84), E("beacon_magenta", 193, 96), E("strobe_white", 160, 28)),
        B("relay_01", E("beacon_cyan", 72, 82), E("beacon_magenta", 137, 60), E("strobe_white", 120, 28)),
        B("causeway_00", E("beacon_cyan", 55, 38), E("beacon_magenta", 195, 94)),
        B("icebreaker_00", E("steam_vent", 134, 48, .7f), E("beacon_magenta", 144, 73)),
        B("icebreaker_01", E("steam_vent", 128, 50, .7f), E("beacon_cyan", 147, 61)),
        B("cliff_00", E("geyser", 70, 182), E("steam_vent", 190, 200, .8f)),
        B("cliff_01", E("steam_vent", 60, 190, .8f), E("geyser", 200, 205, .8f), E("beacon_cyan", 139, 30)),
        B("derrick_00", E("flare", 129, 36), E("beacon_magenta", 77, 106)),
        B("convoy_00", E("steam_vent", 193, 62, .55f), E("steam_vent", 73, 137, .55f)),
        // launch sites (keyed by their closed drawing)
        B("hangar_closed", E("searchlight", 128, 196), E("beacon_magenta", 75, 42), E("beacon_cyan", 190, 52)),
        B("rigbay_closed", E("beacon_cyan", 62, 42), E("strobe_white", 214, 58), E("window_lights", 128, 112)),
        B("padring_idle", E("beacon_magenta", 128, 44), E("strobe_white", 128, 214)),
        B("crawlerbay_closed", E("steam_vent", 90, 52, .6f), E("window_lights", 200, 70), E("beacon_cyan", 128, 60)),
        B("hatch_closed", E("steam_vent", 60, 70, .6f), E("beacon_cyan", 128, 40)),
    };

    static Loop L(string name, string atlas, float fps, float alpha, float scale)
    {
        return new Loop { name = name, atlas = atlas, fps = fps, alpha = alpha, scale = scale, anchor = new Vector2(128, 236) };
    }

    static Emitter E(string loop, float x, float y, float scale = 1f, float alpha = 1f)
    {
        return new Emitter { loop = loop, at = new Vector2(x, y), scale = scale, alpha = alpha };
    }

    static Binding B(string piece, params Emitter[] emitters) { return new Binding { piece = piece, emitters = emitters }; }

    public static int LoopIndex(string name)
    {
        for (int i = 0; i < Loops.Length; i++) if (Loops[i].name == name) return i;
        return -1;
    }

    // The emitters of a piece drawing (exact name), or none.
    public static Emitter[] For(string piece)
    {
        if (string.IsNullOrEmpty(piece)) return null;
        for (int i = 0; i < Bindings.Length; i++) if (Bindings[i].piece == piece) return Bindings[i].emitters;
        return null;
    }
}

// Ambient loops riding pooled backdrop pieces (FrostAmbientCatalog): every
// piece of a rigged pool carries MaxPerPiece child renderers under its body
// (so they scroll, scale and mirror with it), configured when the piece
// spawns and animated from the piece's own age -- scaled time only, so a
// paused game is a still frame. Nothing is created or allocated after Rig.
public class AmbientEmitters
{
    class Slot
    {
        public SpriteRenderer sr;
        public Sprite[] frames;
        public float fps, alpha, phase;
        public bool on;
    }

    readonly BackdropSet set;
    readonly Sprite[][] frames;
    readonly List<BackdropPool> pools = new List<BackdropPool>();
    readonly List<Slot[][]> slots = new List<Slot[][]>();
    // Smoke and steam draw lifted (BackdropGrade); the lights as painted.
    Material plumeMat, plainMat;
    public Material PlumeMaterial { get { return plumeMat; } }

    public AmbientEmitters(BackdropSet set)
    {
        this.set = set;
        float lift = BackdropGrade.Lift(set.Spec, FrostTuning.PlumeShare);
        plumeMat = BackdropGrade.Create("plumes", lift, BackdropGrade.Saturation(set.Spec, lift));
        var loops = FrostAmbientCatalog.Loops;
        frames = new Sprite[loops.Length][];
        for (int i = 0; i < loops.Length; i++) frames[i] = set.Atlas(loops[i].atlas).Frames(loops[i].name);
    }

    // The loop's frames, empty when its atlas is not installed.
    public Sprite[] Frames(string loop)
    {
        int i = FrostAmbientCatalog.LoopIndex(loop);
        return i >= 0 ? frames[i] : new Sprite[0];
    }

    public bool Has(string loop) { return Frames(loop).Length > 0; }

    // Gives every piece of `pool` its emitter renderers, drawn `orderOffset`
    // above the piece.
    public void Rig(BackdropPool pool, int orderOffset = 1)
    {
        var table = new Slot[pool.items.Count][];
        for (int i = 0; i < pool.items.Count; i++)
        {
            var p = pool.items[i];
            table[i] = new Slot[FrostAmbientCatalog.MaxPerPiece];
            for (int k = 0; k < table[i].Length; k++)
            {
                var go = new GameObject("ambient" + k);
                go.transform.SetParent(p.body, false);
                var sr = go.AddComponent<SpriteRenderer>();
                if (plainMat == null) plainMat = sr.sharedMaterial;
                sr.sortingOrder = p.sr.sortingOrder + orderOffset;
                sr.enabled = false;
                table[i][k] = new Slot { sr = sr };
            }
        }
        pools.Add(pool);
        slots.Add(table);
    }

    // Configures `p`'s emitters for its drawing `piece` (one of the pools
    // passed to Rig). Returns how many show.
    public int Attach(BackdropPiece p, string piece, System.Random rng)
    {
        Slot[] mine = null;
        for (int k = 0; k < pools.Count && mine == null; k++)
        {
            int i = pools[k].items.IndexOf(p);
            if (i >= 0) mine = slots[k][i];
        }
        if (mine == null) return 0;
        var list = FrostAmbientCatalog.For(piece);
        int n = 0;
        for (int s = 0; s < mine.Length; s++)
        {
            var slot = mine[s];
            slot.on = false;
            slot.sr.enabled = false;
            if (list == null || s >= list.Length) continue;
            var e = list[s];
            int li = FrostAmbientCatalog.LoopIndex(e.loop);
            if (li < 0 || frames[li].Length == 0) continue;
            var loop = FrostAmbientCatalog.Loops[li];
            float scale = loop.scale * e.scale;
            // The loop's anchor lands on the host's point (both in 256 cells
            // at the same pixel size; sprites pivot on their centre).
            const float ppu = BackdropAtlas.PixelsPerUnit;
            Vector2 host = new Vector2((e.at.x - 128f) / ppu, (128f - e.at.y) / ppu);
            Vector2 anchor = new Vector2((loop.anchor.x - 128f) / ppu, (128f - loop.anchor.y) / ppu);
            Vector2 pos = host - anchor * scale;
            slot.sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            slot.sr.transform.localScale = new Vector3(scale, scale, 1f);
            slot.frames = frames[li];
            slot.fps = loop.fps * (.9f + .2f * (float)rng.NextDouble());
            slot.alpha = loop.alpha * e.alpha;
            slot.phase = (float)rng.NextDouble() * slot.frames.Length / Mathf.Max(.01f, slot.fps);
            slot.sr.sprite = slot.frames[0];
            Material m = plumeMat != null && FrostAmbientCatalog.Lifted(loop.atlas) ? plumeMat : plainMat;
            if (m != null && slot.sr.sharedMaterial != m) slot.sr.sharedMaterial = m;
            slot.on = true;
            n++;
        }
        return n;
    }

    // Emitters showing right now (tests).
    public int LiveCount
    {
        get
        {
            int n = 0;
            for (int k = 0; k < pools.Count; k++)
                for (int i = 0; i < pools[k].items.Count; i++)
                {
                    if (!pools[k].items[i].active) continue;
                    foreach (var s in slots[k][i]) if (s.on && s.sr.enabled) n++;
                }
            return n;
        }
    }

    public void Destroy() { BackdropAtlas.Kill(plumeMat); plumeMat = null; }

    public void Step(float fade)
    {
        float a = fade * set.Alpha * FrostAmbientCatalog.Brightness;
        for (int k = 0; k < pools.Count; k++)
        {
            var items = pools[k].items;
            var table = slots[k];
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                if (!p.active) continue;
                var mine = table[i];
                for (int s = 0; s < mine.Length; s++)
                {
                    var slot = mine[s];
                    if (!slot.on) continue;
                    int n = slot.frames.Length;
                    int f = (int)((p.age + slot.phase) * slot.fps) % n;
                    slot.sr.sprite = slot.frames[f];
                    slot.sr.color = new Color(1f, 1f, 1f, slot.alpha * a);
                    if (!slot.sr.enabled) slot.sr.enabled = true;
                }
            }
        }
    }
}
