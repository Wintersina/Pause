using System.Collections.Generic;
using UnityEngine;

// Which of a world's ground-tile variant sets a landing flies over.
//
// Every entry into a world with variant sets (Spec.variantSets: Frost,
// Verdant; each new BackdropSet: a planetfall, a portal, a run starting
// there) picks one of up to MaxVariants sets of {sky, far, mid, flow} from
// <folder>v1..v4. A variant whose folder is not installed is skipped, so
// dropping a new v<N>/ folder in is enough to enable it. With more than one
// installed, the same set never comes twice in a row. One instance per
// world, each its own random stream: gameplay's Random is never touched.
public class BackdropVariants
{
    public const int MaxVariants = 4;

    static readonly System.Collections.Generic.Dictionary<string, BackdropVariants> worlds =
        new System.Collections.Generic.Dictionary<string, BackdropVariants>();

    public static BackdropVariants For(string world)
    {
        BackdropVariants v;
        if (!worlds.TryGetValue(world, out v)) worlds[world] = v = new BackdropVariants(world);
        return v;
    }

    public readonly string World;
    // The variant picked last (0: none yet).
    public int Last { get; private set; }
    public int Picks { get; private set; }
    // Test / preview hook: > 0 always picks this variant (when installed).
    public int Force;
    // A pick made ahead of the entry (WorldArtPreload loads that variant's tiles); the next Pick returns it.
    public int Reserved { get; private set; }
    public int Reserve(int count)
    {
        if (Reserved == 0) Reserved = Pick(count);
        return Reserved;
    }

    System.Random rng = new System.Random();
    readonly int[] known = new int[MaxVariants + 1];   // 0 unknown, 1 installed, -1 missing
    readonly int[] order = new int[MaxVariants];

    BackdropVariants(string world) { World = world; }

    public void Seed(int seed) { rng = new System.Random(seed); }

    public void Reset()
    {
        Last = 0;
        Picks = 0;
        Force = 0;
        Reserved = 0;
        rng = new System.Random();
        System.Array.Clear(known, 0, known.Length);
    }

    // The ground sets that SHIP, by world (bit v-1 = variant v): known without touching the disk, so the first Pick /
    // Reserve of a run (WorldManager.OpenPortal, a planetfall) no longer loads up to four sky textures just to learn
    // they exist. A variant not listed here is still probed from disk the old way (so dropping a new v<N>/ folder in
    // works, and a missing one is skipped); BackdropVariantsManifestTest (PerfPassTest) fails when this list and the
    // Resources folders disagree in either direction.
    const int AllFour = 0xF;
    public static readonly string[] ShippedWorlds = { "Frost", "Verdant", "Ember", "Desert", "Tide", "Storm" };
    public static int ShippedMask(string world)
    {
        for (int i = 0; i < ShippedWorlds.Length; i++) if (ShippedWorlds[i] == world) return AllFour;
        return 0;
    }
    // Sky probes that really loaded a texture (tests).
    public static int DiskProbes { get; private set; }
    public static void ResetCounters() { DiskProbes = 0; }

    // Is variant `v` on disk (its sky tile resolves)?
    public bool Installed(int v)
    {
        if (v < 1 || v > MaxVariants) return false;
        if (known[v] == 0 && (ShippedMask(World) & (1 << (v - 1))) != 0) known[v] = 1;
        if (known[v] == 0)
        {
            DiskProbes++;
            known[v] = WorldArtPreload.Load<Sprite>(BackdropCatalog.TileFolder(World, v) + "sky") != null ? 1 : -1;
        }
        return known[v] > 0;
    }

    public int InstalledCount(int count)
    {
        int n = 0;
        for (int v = 1; v <= Mathf.Min(count, MaxVariants); v++) if (Installed(v)) n++;
        return n;
    }

    // One variant for a new entry: random among the installed ones, never
    // the previous one when another is installed. 1 when none is (the set
    // then reports itself incomplete).
    public int Pick(int count)
    {
        if (Reserved > 0) { int r = Reserved; Reserved = 0; return r; }   // the preload already chose (Reserve)
        count = Mathf.Clamp(count, 1, MaxVariants);
        int pick = 0;
        if (Force > 0 && Installed(Force)) pick = Force;
        else
        {
            for (int i = 0; i < count; i++) order[i] = i + 1;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            for (int i = 0; i < count && pick == 0; i++)
                if (order[i] != Last && Installed(order[i])) pick = order[i];
            if (pick == 0 && Installed(Last)) pick = Last;
            if (pick == 0) pick = 1;
        }
        Last = pick;
        Picks++;
        return pick;
    }
}

// Frost's variant selection (BackdropVariants.For("Frost")), kept as the
// static API its tests and preview use.
public static class FrostBackdropSelection
{
    public const int MaxVariants = BackdropVariants.MaxVariants;
    static BackdropVariants F => BackdropVariants.For("Frost");
    public static int Last => F.Last;
    public static int Picks => F.Picks;
    public static int Force { get => F.Force; set => F.Force = value; }
    public static void Seed(int seed) { F.Seed(seed); }
    public static void Reset() { F.Reset(); }
    public static bool Installed(string world, int v) { return BackdropVariants.For(world).Installed(v); }
    public static int InstalledCount(string world, int count) { return BackdropVariants.For(world).InstalledCount(count); }
    public static int Pick(string world, int count) { return BackdropVariants.For(world).Pick(count); }
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
        public float spread;        // per-light fps jitter (0 = the default +-10%); Tide's lamps use a wide one so no two blink in step
        public float tone;          // colour multiplier of the drawn loop (0 = untouched, 1): darkens light plumes (Ember's ash smoke, so shots stay readable over it)
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
        // the redrawn lamps stand on a 44 px post at 1: drawn at .5 they sit on the
        // drawings' 4 px painted lamp dots at the buildings' own scale
        L("beacon_magenta", "beacons", 4f, .70f, .5f),
        L("beacon_cyan", "beacons", 4f, .68f, .5f),
        L("strobe_white", "beacons", 8f, .55f, .5f),
        L("window_lights", "beacons", 5f, .60f, 1f),
        new Loop { name = AuroraLoop, atlas = "aurora", fps = 3f, alpha = .46f, scale = 1f, anchor = new Vector2(128, 128) },
    };

    // Emitter points measured on landmarks.png / sites.png (cell pixels from
    // the top-left of each 256 cell): re-measured on the 2026-11 PixelLab redraw
    // (frost_redraw/build_atlases.py) on the features the new drawings really
    // have, overlaid cell by cell (placement audit, 2026-11-27). The redrawn
    // loops are whole objects standing on their foot (the loop anchor, 128, 236):
    // a lamp on its post, a brazier with its fire, an arched lit doorway, a
    // searchlight beam from its lamp, a puff growing out of its stack. So:
    //   - a beacon / strobe stands on a lamp the drawing paints (its 4 px
    //     amber / cyan / white dot: amber -> beacon_magenta, whose art is
    //     amber), its post's foot on the dot;
    //   - window_lights sits on a painted lit window at .2 (the doorway is
    //     72 px wide at 1, the painted windows 12);
    //   - smoke only from a drawn stack top (refinery_01), at .5-.6;
    //   - a searchlight's beam rises from a painted white lamp;
    //   - a fire (flare: now a brazier, no flare stack) only stands on the
    //     drawing's own snow skirt beside the building, never on a roof.
    // Dropped where the anchor is gone from the new drawing: the rigs' and
    // derrick's roof flares (no flare boom: a bonfire floated over the roof),
    // every steam vent / geyser on the icebreakers, cliffs, convoy, crawler
    // bay and hatch (no funnel, vent or chimney drawn: the puffs hung free),
    // refinery_00's smoke (tanks, no stack), the causeway's beacons (now a rail
    // embankment, no lamps), platform_01's beacon on its hovering drone, the
    // rig bay's strobe (mid-air) and doorway (its pad marker is no window).
    // Re-measured again on the HD rebuild (sources_hd: 128 px cells x2; the
    // drawings moved and changed proportion in their cells, not just sharpened).
    public static readonly Binding[] Bindings =
    {
        B("rig_00", E("beacon_magenta", 147, 50), E("strobe_white", 173, 81), E("flare", 60, 218, .45f)),
        B("rig_01", E("beacon_magenta", 143, 46), E("strobe_white", 193, 67)),
        B("platform_00", E("searchlight", 149, 159), E("beacon_magenta", 199, 62), E("window_lights", 133, 198, .2f)),
        B("platform_01", E("searchlight", 132, 145), E("beacon_cyan", 110, 109), E("window_lights", 130, 184, .2f)),   // the cyan lamp atop the dome
        B("platform_02", E("searchlight", 125, 119), E("beacon_magenta", 79, 47), E("window_lights", 181, 102, .2f)),
        B("refinery_00", E("window_lights", 105, 186, .2f), E("beacon_magenta", 131, 60), E("strobe_white", 178, 104)),   // warning lamps on the tank caps
        B("refinery_01", E("smoke_a", 147, 42, .6f), E("smoke_b", 162, 38, .6f), E("smoke_b", 87, 72, .5f), E("window_lights", 133, 178, .2f)),
        B("relay_00", E("beacon_cyan", 141, 62), E("beacon_magenta", 177, 109), E("strobe_white", 117, 59)),
        B("relay_01", E("beacon_cyan", 117, 38), E("beacon_magenta", 191, 60), E("strobe_white", 141, 22)),
        B("icebreaker_00", E("beacon_magenta", 145, 78), E("flare", 84, 196, .45f)),     // a fire on the ice beside the wreck
        B("icebreaker_01", E("beacon_cyan", 137, 37), E("strobe_white", 206, 72)),   // + the aft mast head
        B("cliff_01", E("beacon_cyan", 175, 34)),
        B("causeway_00", E("window_lights", 181, 116, .2f)),        // the locomotive's lit cab window
        B("convoy_00", E("beacon_magenta", 184, 88), E("window_lights", 90, 72, .2f)),   // the lamp post by the gun, the car's lit windows
        B("derrick_00", E("beacon_magenta", 117, 22), E("beacon_magenta", 89, 102), E("flare", 52, 186, .45f)),
        // launch sites (keyed by their closed drawing)
        B("hangar_closed", E("searchlight", 119, 181), E("beacon_magenta", 87, 34), E("beacon_cyan", 147, 34)),
        B("rigbay_closed", E("beacon_cyan", 88, 33)),               // the cyan lamp on the crane jib
        B("padring_idle", E("beacon_magenta", 69, 72), E("strobe_white", 178, 72), E("beacon_cyan", 70, 162), E("beacon_cyan", 175, 162)),   // + the near rim posts
        B("crawlerbay_closed", E("window_lights", 209, 78, .2f), E("beacon_cyan", 79, 42)),
        B("hatch_closed", E("beacon_cyan", 137, 43)),
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

    public static int LoopIndex(string name) { return Table.LoopIndex(name); }

    // The emitters of a piece drawing (exact name), or none.
    public static Emitter[] For(string piece) { return Table.For(piece); }

    // Frost's loops and bindings as an AmbientTable (AmbientEmitters).
    public static readonly AmbientTable Table = new AmbientTable
    {
        loops = Loops, bindings = Bindings, maxPerPiece = MaxPerPiece,
        lifted = Lifted, plumeShare = () => FrostTuning.PlumeShare, brightness = () => Brightness,
    };
}

// What animates where, for one world: its loops and which piece drawings
// carry which loop at which point (FrostAmbientCatalog.Table,
// VerdantAmbientCatalog.Table). AmbientEmitters plays it.
public class AmbientTable
{
    public FrostAmbientCatalog.Loop[] loops;
    public FrostAmbientCatalog.Binding[] bindings;
    public int maxPerPiece = 4;
    // the atlases drawn lifted by the world's brightness (smoke, steam)
    public System.Func<string, bool> lifted = a => false;
    public System.Func<float> plumeShare = () => 1f, brightness = () => 1f;
    // optional: the plumes' own lift (lighter smoke against a lit forest),
    // instead of their share of the world's brightness
    public System.Func<float> plumeLift;
    // extra opacity for the light loops (beacons, windows): the night side
    public System.Func<float> lightBoost = () => 1f;

    public int LoopIndex(string name)
    {
        for (int i = 0; i < loops.Length; i++) if (loops[i].name == name) return i;
        return -1;
    }

    public FrostAmbientCatalog.Emitter[] For(string piece)
    {
        if (string.IsNullOrEmpty(piece)) return null;
        for (int i = 0; i < bindings.Length; i++) if (bindings[i].piece == piece) return bindings[i].emitters;
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
        public float fps, alpha, phase, boost, tone;
        public bool on;
        public int loop;
        public Vector2 host;          // the host's point, body-local units
    }

    readonly BackdropSet set;
    public readonly AmbientTable Table;
    // +1 / -1: every plume leans that way on screen whatever the mirroring
    // of the piece it rides (the art leans right; -1 mirrors the loops). 0
    // (Frost): loops mirror with their piece.
    public float Wind;
    readonly Sprite[][] frames;
    readonly List<BackdropPool> pools = new List<BackdropPool>();
    readonly List<Slot[][]> slots = new List<Slot[][]>();
    // Smoke and steam draw lifted (BackdropGrade); the lights as painted.
    Material plumeMat, plainMat;
    public Material PlumeMaterial { get { return plumeMat; } }

    public AmbientEmitters(BackdropSet set) : this(set, FrostAmbientCatalog.Table) { }

    public AmbientEmitters(BackdropSet set, AmbientTable table)
    {
        this.set = set;
        Table = table;
        float lift = table.plumeLift != null ? table.plumeLift() : BackdropGrade.Lift(set.Spec, table.plumeShare(), set.Variant);
        plumeMat = BackdropGrade.Create("plumes", lift, BackdropGrade.Saturation(set.Spec, lift));
        var loops = table.loops;
        frames = new Sprite[loops.Length][];
        for (int i = 0; i < loops.Length; i++) frames[i] = set.Atlas(loops[i].atlas).Frames(loops[i].name);
    }

    // The loop's frames, empty when its atlas is not installed.
    public Sprite[] Frames(string loop)
    {
        int i = Table.LoopIndex(loop);
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
            table[i] = new Slot[Table.maxPerPiece];
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
        var list = Table.For(piece);
        float bodyFlip = p.body.localScale.x < 0f ? -1f : 1f;
        int n = 0;
        for (int s = 0; s < mine.Length; s++)
        {
            var slot = mine[s];
            slot.on = false;
            slot.sr.enabled = false;
            if (list == null || s >= list.Length) continue;
            var e = list[s];
            int li = Table.LoopIndex(e.loop);
            if (li < 0 || frames[li].Length == 0) continue;
            var loop = Table.loops[li];
            float scale = loop.scale * e.scale;
            // The loop's anchor lands on the host's point (both in 256 cells
            // at the same pixel size; sprites pivot on their centre). With a
            // level wind the loop is counter-mirrored against its piece so
            // it leans the wind's way on screen (its anchor still on the point).
            float flip = Wind != 0f ? Mathf.Sign(Wind) * bodyFlip : 1f;
            Vector2 cellPx = new Vector2(128f, 128f);
            Sprite f0 = frames[li][0];
            if (f0 != null) cellPx = new Vector2(f0.rect.width * .5f, f0.rect.height * .5f) / Mathf.Max(.01f, f0.pixelsPerUnit / BackdropAtlas.PixelsPerUnit);
            const float ppu = BackdropAtlas.PixelsPerUnit;
            Vector2 host = new Vector2((e.at.x - 128f) / ppu, (128f - e.at.y) / ppu);
            Vector2 anchor = new Vector2((loop.anchor.x - cellPx.x) / ppu * flip, (cellPx.y - loop.anchor.y) / ppu);
            Vector2 pos = host - anchor * scale;
            slot.sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            slot.sr.transform.localScale = new Vector3(scale * flip, scale, 1f);
            slot.host = host;
            slot.loop = li;
            slot.boost = Table.lifted(loop.atlas) ? 1f : -1f;
            slot.frames = frames[li];
            float spr = loop.spread > 0f ? loop.spread : .1f;
            slot.fps = loop.fps * (1f - spr + 2f * spr * (float)rng.NextDouble());
            slot.alpha = loop.alpha * e.alpha;
            slot.tone = loop.tone > 0f ? loop.tone : 1f;
            slot.phase = (float)rng.NextDouble() * slot.frames.Length / Mathf.Max(.01f, slot.fps);
            slot.sr.sprite = slot.frames[0];
            Material m = plumeMat != null && Table.lifted(loop.atlas) ? plumeMat : plainMat;
            if (m != null && slot.sr.sharedMaterial != m) slot.sr.sharedMaterial = m;
            slot.on = true;
            n++;
        }
        return n;
    }

    // Is a rising plume (smoke, steam, geyser) playing on `p`? Its column stands above the piece, so the piece must stay
    // until the plume has left the view (BackdropDirector.GroundDespawnY).
    public bool HasRisingPlume(BackdropPiece p)
    {
        for (int k = 0; k < pools.Count; k++)
        {
            int i = pools[k].items.IndexOf(p);
            if (i < 0) continue;
            foreach (var s in slots[k][i])
            {
                if (!s.on) continue;
                string n = Table.loops[s.loop].name;
                if (n.Contains("smoke") || n.Contains("steam") || n.Contains("geyser")) return true;
            }
            return false;
        }
        return false;
    }

    // Stops every loop riding `p` (a piece being destroyed: its smoke and lamps go with it).
    public void Detach(BackdropPiece p)
    {
        for (int k = 0; k < pools.Count; k++)
        {
            int i = pools[k].items.IndexOf(p);
            if (i < 0) continue;
            foreach (var s in slots[k][i]) { s.on = false; s.sr.enabled = false; }
            return;
        }
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

    // Slot `k` of piece `p` when it shows: the loop's name, its anchor and
    // the host point it should sit on, both in world space (tests).
    public bool Probe(BackdropPiece p, int k, out string loop, out Vector3 anchorWorld, out Vector3 hostWorld)
    {
        loop = null; anchorWorld = hostWorld = Vector3.zero;
        for (int q = 0; q < pools.Count; q++)
        {
            int i = pools[q].items.IndexOf(p);
            if (i < 0) continue;
            var mine = slots[q][i];
            if (k < 0 || k >= mine.Length || !mine[k].on) return false;
            var slot = mine[k];
            var l = Table.loops[slot.loop];
            loop = l.name;
            var sp = slot.frames[0];
            float half = sp.rect.width * .5f / (sp.pixelsPerUnit / BackdropAtlas.PixelsPerUnit);
            float halfH = sp.rect.height * .5f / (sp.pixelsPerUnit / BackdropAtlas.PixelsPerUnit);
            const float ppu = BackdropAtlas.PixelsPerUnit;
            anchorWorld = slot.sr.transform.TransformPoint(new Vector3((l.anchor.x - half) / ppu, (halfH - l.anchor.y) / ppu, 0f));
            hostWorld = p.body.TransformPoint(new Vector3(slot.host.x, slot.host.y, 0f));
            return true;
        }
        return false;
    }

    public void Step(float fade)
    {
        float a = fade * set.Alpha * Table.brightness();
        float boost = Table.lightBoost();
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
                    slot.sr.color = new Color(slot.tone, slot.tone * (slot.tone < 1f ? .94f : 1f), slot.tone * (slot.tone < 1f ? .88f : 1f), Mathf.Min(1f, slot.alpha * a * (slot.boost < 0f ? boost : 1f)));
                    if (!slot.sr.enabled) slot.sr.enabled = true;
                }
            }
        }
    }
}
