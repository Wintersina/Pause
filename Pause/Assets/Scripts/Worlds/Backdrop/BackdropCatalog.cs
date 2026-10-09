using UnityEngine;

// Which layers each world's animated background is built from.
//
// Every world is a stack of depth layers, far to near. Each layer moves at
// `rate` x the foreground scroll velocity (see WorldBackdrop.ScrollVelocity),
// so a higher rate reads as closer. Rates must strictly increase with depth;
// WorldBackdropTest enforces it.
//
// Tile layers are seamless 512x1024 PNGs leap-frogged vertically. Set pieces
// (planets, glaciers, volcanoes...) and particles come from each world's `fx`
// and `anim` atlases and are driven by that world's BackdropDirector.
//
// Art lives at Art/Backgrounds/Resources/Worlds/<World>/Backdrop/ (Frost:
// Backdrop3/, see Spec.folder) and is generated from the
// SVG templates under Assets/Art/Worlds/<World>/src~/ (Space's atlases are
// cut from pixel-art sheets instead: Space/src~/build_atlas.py).
public static class BackdropCatalog
{
    public enum Kind { Tile, Strip, Pieces }

    // What a layer depicts, for the depth model of the planet worlds: the
    // ship flies at atmosphere level, so Ground and Landmark layers are far
    // below (slow, small, hazed) and only Atmosphere / Cloud layers -- air,
    // clouds, haze bands, snow, spores, ash -- may pass close to the ship.
    public enum Role { Sky, Ground, Landmark, Atmosphere, Cloud }

    // Depth limits WorldBackdropTest holds the planet worlds to.
    public const float MaxGroundRate = 0.05f;        // ground & landmarks: far parallax only
    public const float MaxLandmarkSize = 1.8f;       // world units on screen (~30% of the view width)
    // Space: planets are the most distant bodies there are, so even the
    // nearest planet tier stays this slow -- a fraction of any station's rate.
    public const float MaxPlanetRate = 0.02f;

    public struct Layer
    {
        public string name;
        public Kind kind;
        public float rate;          // fraction of foreground scroll velocity
        public Role role;
        public string texture;      // Tile / Strip only
        public float flow;          // Strip: extra units/s on top of the parallax
        public float wobble;        // Strip: heat-shimmer sway, units
        public Color tint;
        public float wrapBlend;     // Tile: fraction of the art cross-faded into its start (seamless whatever the art's wrap)
        public float grade;         // share of the world's brightness lift drawn on this layer (BackdropGrade; 0 = as painted)
        // Pieces pinned to a ground tile layer (GroundPlanner): they move at
        // exactly that layer's rate, on its scroll, so they stay on their
        // spot of the ground. A pinned layer shares its host's rate (the one
        // exception to "rates strictly increase"; it must follow its host).
        public string pinTo;

        public Layer PinnedTo(string host, float hostRate) { var l = this; l.pinTo = host; l.rate = hostRate; return l; }

        public Layer Graded(float share) { var l = this; l.grade = share; return l; }

        public Layer WrapBlended(float fraction) { var l = this; l.wrapBlend = fraction; return l; }
        public Layer WithTexture(string resourceName) { var l = this; l.texture = resourceName; return l; }

        public static Layer Tile(string name, float rate, Color tint, Role role = Role.Ground)
        {
            return new Layer { name = name, kind = Kind.Tile, rate = rate, texture = name, tint = tint, role = role };
        }

        public static Layer Strip(string name, float rate, float flow, float wobble, Color tint)
        {
            return new Layer { name = name, kind = Kind.Strip, rate = rate, texture = name, flow = flow,
                               wobble = wobble, tint = tint, role = Role.Ground };
        }

        public static Layer Pieces(string name, float rate, Role role)
        {
            return new Layer { name = name, kind = Kind.Pieces, rate = rate, tint = Color.white, role = role };
        }
    }

    public class Spec
    {
        public string world;
        public Layer[] layers;      // far -> near
        // Where the art lives under Resources (null: Worlds/<world>/Backdrop/).
        public string folder;
        // The atlas whose presence makes the set complete (null: fx).
        public string keyAtlas;
        // > 0: the tile layers come from one of these many variant folders
        // (<folder>v1/ .. vN/), one picked per entry into the world.
        public int variantSets;
        // Draw-time brightening (BackdropGrade): the world's lift (null or 1
        // = the art as painted) and its saturation nudge; each layer takes
        // Layer.grade of the lift. Read when a set is built.
        public System.Func<float> brightness, saturation;
        // Per variant set (index = variant, 0 = none): a brightness table
        // multiplying the world's lift (1 = the world's, < 1 darker: a night
        // landing) and which variants are the planet's NIGHT side (darker
        // ground, lights and glowing life emphasised by the director).
        // Null: every variant is drawn alike. Frost / Ember v4 can use the
        // same two tables.
        public float[] variantBrightness;
        public bool[] variantNight;
        // The art itself is painted bright (not lifted): what reads over it
        // needs the bold treatment too (HeartOutline's bold outline).
        public bool brightArt;
        // A world's backdrop counts as BRIGHT (drawn lifted by at least
        // BrightLift, or painted bright): what must read over it -- the
        // hearts' outline (HeartOutline), the hostile shots' outline and rim
        // (ShotOutline, BossArt.ShotRim) -- switches to its bold, dark-keyed
        // style. The one predicate for all of them, so a world that is lifted
        // later (Ember) gets every bold treatment at once.
        public const float BrightLift = 1.2f;
        public bool Bright => brightArt || brightness != null && brightness() >= BrightLift;

        // CLOUD COVER (CloudCover): this world's knobs, read when a set is
        // built and every frame. Null: the shared defaults (CloudCover.*), so
        // a world wired later (Ember) opens and clears like Frost / Verdant.
        public System.Func<float> cloudDensity, ceilingHold, ceilingClearSeconds;

        public float CloudDensity() { return cloudDensity != null ? Mathf.Max(0f, cloudDensity()) : CloudCover.Density; }
        public float CeilingHold() { return ceilingHold != null ? ceilingHold() : CloudCover.CeilingHold; }
        public float CeilingClearSeconds()
        {
            return Mathf.Max(CeilingHold() + .5f, ceilingClearSeconds != null ? ceilingClearSeconds() : CloudCover.CeilingClearSeconds);
        }
        // The ceiling's draw density at level second t (CloudCover.Ceiling).
        public float CeilingDensity(float t) { return CloudCover.Ceiling(t, CeilingHold(), CeilingClearSeconds()); }

        public bool Night(int variant) { return variantNight != null && variant >= 0 && variant < variantNight.Length && variantNight[variant]; }
        public float VariantBrightness(int variant)
        {
            return variantBrightness != null && variant >= 0 && variant < variantBrightness.Length ? variantBrightness[variant] : 1f;
        }

        public bool Has(string name)
        {
            foreach (var l in layers) if (l.name == name) return true;
            return false;
        }

        // A layer's tint, the per-layer brightness / colour hook pieces share
        // with tiles.
        public Color Tint(string name) { return Find(name).tint; }

        public Layer Find(string name)
        {
            foreach (var l in layers) if (l.name == name) return l;
            throw new System.ArgumentException(world + " has no backdrop layer " + name);
        }

        public float Rate(string name) { return Find(name).rate; }

        // Sorting order: everything sits far below gameplay (order 0).
        public int Order(string name)
        {
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == name) return BaseOrder + i * 10;
            return BaseOrder;
        }
    }

    public const int BaseOrder = -500;
    public const float SkyWrapBlend = 0.1f;

    public const string AtlasFx = "fx";
    public const string AtlasAnim = "anim";
    // Optional high-resolution re-render of AtlasAnim: same cells, same names,
    // more pixels per cell (docs/art-production-queue.md). Loaded instead of
    // AtlasAnim when present (BackdropSet.LoadAnimAtlas).
    public const string AtlasAnimHires = "anim_hires";

    static readonly Color W = Color.white;

    static readonly Spec[] specs =
    {
        // Space has no ground: everything is sky at its own depth. A body's
        // parallax comes from how far away it is, never from how big it is
        // drawn: a planet that fills a third of the view is a giant thing a
        // very long way off, so every planet tier (planet_deep..planet_near)
        // creeps by just in front of the stars, far slower than the small
        // stations and rocks (deep..near) that really are close to the ship.
        // See SpaceDirector.Tiers; comets pass behind all of them.
        new Spec { world = "Space", layers = new[] {
            // Cross-fade the sky's last rows into its first at render time
            // (BackdropSkyWrap). One of Codex's sky_01..04 per run (SpaceSkySelection).
            Layer.Tile("sky", 0.006f, new Color(0.68f, 0.68f, 0.76f), Role.Sky).WrapBlended(SkyWrapBlend),
            Layer.Pieces("wisps", 0.007f, Role.Sky),
            Layer.Pieces("galaxies", 0.008f, Role.Sky),
            Layer.Pieces("stars", 0.009f, Role.Sky),
            Layer.Pieces("comets", 0.010f, Role.Sky),
            Layer.Pieces("planet_deep", 0.0110f, Role.Sky),
            Layer.Pieces("planet_far", 0.0125f, Role.Sky),
            Layer.Pieces("planet_mid", 0.0140f, Role.Sky),
            Layer.Pieces("planet_near", 0.0160f, Role.Sky),
            Layer.Pieces("deep", 0.050f, Role.Sky),
            Layer.Pieces("far", 0.064f, Role.Sky),
            Layer.Pieces("mid", 0.082f, Role.Sky),
            Layer.Pieces("near", 0.105f, Role.Sky),
            Layer.Pieces("dust", 0.600f, Role.Atmosphere),
        }},
        // Planet worlds, seen from atmosphere level: ground and landmarks
        // creep by far below; clouds, haze and particles pass close.
        // Frost (FrostDirector, docs in FrostBackdrop.cs): flown at atmosphere
        // level just under the cloud ceiling, looking down on a frozen ocean,
        // coast and ice-bound industry. The four ground tiles come from one of
        // up to four variant sets per landing (FrostBackdropSelection,
        // Backdrop3/v1..v4); landmarks, launch sites, weather and the ambient
        // loops are shared atlases. BRIGHTNESS: the art is painted dark and
        // lifted at draw time by FrostTuning.Brightness (BackdropGrade); the
        // .Graded(share) below is how much of that lift each layer takes --
        // the ground and landmarks all of it, the far layers less so the
        // depth haze holds. Tints can only darken (white = as graded).
        new Spec { world = "Frost", folder = "Worlds/Frost/Backdrop3/", keyAtlas = "landmarks",
                   variantSets = BackdropVariants.MaxVariants,
                   brightness = () => FrostTuning.Brightness, saturation = () => FrostTuning.Saturation,
                   cloudDensity = () => FrostTuning.CloudDensity, ceilingHold = () => FrostTuning.CeilingHold,
                   ceilingClearSeconds = () => FrostTuning.CeilingClearSeconds, layers = new[] {
            Layer.Tile("sky", 0.006f, W).Graded(.55f),
            Layer.Tile("far", 0.014f, W).Graded(.8f),
            Layer.Tile("mid", 0.024f, W).Graded(1f),
            Layer.Strip("flow", 0.025f, 0.35f, 0f, W).Graded(1f),
            Layer.Pieces("aurora", 0.027f, Role.Atmosphere).Graded(.3f),
            // Rigs, refineries, icebreakers ... and the elite launch sites
            // (one ground plane: they never slide over each other).
            Layer.Pieces("landmarks", 0.030f, Role.Landmark).Graded(1f),
            Layer.Pieces("mist", 0.060f, Role.Atmosphere).Graded(.5f),
            Layer.Pieces("wisps", 0.090f, Role.Cloud).Graded(.5f),
            // The ceiling's banks are also thickened (FrostTuning.CeilingThicken).
            Layer.Pieces("ceiling", 0.130f, Role.Cloud).Graded(1f),
            Layer.Pieces("snow_far", 0.200f, Role.Atmosphere),
            Layer.Pieces("blizzard", 0.350f, Role.Atmosphere).Graded(.5f),
            Layer.Pieces("snow_mid", 0.450f, Role.Atmosphere),
            Layer.Pieces("snow_near", 0.700f, Role.Atmosphere),
        }},
        // Verdant (VerdantDirector, docs in VerdantBackdrop.cs): flown at
        // atmosphere level over the jungle planet: canopy sea and rivers, a
        // swamp delta, an overgrown ruined city, or the NIGHT side's
        // bioluminescent forest (VerdantTuning.Night; one of four variant
        // sets per landing). Overgrown industry, pipework, smoking stacks
        // and wildfires burning in the forest stand on the ground, PINNED to
        // the mid tile (GroundPlanner: same rate, same scroll) and placed
        // where its affinity mask says they belong. The art is painted
        // brighter than Frost's (value p90 ~.47) and drawn as painted on the
        // day side; the night side is drawn darker (variantBrightness).
        new Spec { world = "Verdant", folder = "Worlds/Verdant/Backdrop3/", keyAtlas = "landmarks",
                   variantSets = BackdropVariants.MaxVariants,
                   brightness = () => VerdantTuning.Brightness, saturation = () => 1f,
                   variantBrightness = VerdantTuning.VariantBrightness, variantNight = VerdantTuning.Night, brightArt = true,
                   cloudDensity = () => VerdantTuning.CloudDensity, ceilingHold = () => VerdantTuning.CeilingHold,
                   ceilingClearSeconds = () => VerdantTuning.CeilingClearSeconds, layers = new[] {
            Layer.Tile("sky", 0.006f, W).Graded(.6f),
            Layer.Tile("far", 0.014f, W).Graded(.8f),
            Layer.Tile("mid", 0.024f, W).Graded(1f),
            // landmarks, pipes, wildfires and the elite sites: one ground
            // plane pinned to the mid tile
            Layer.Pieces("ground", 0.024f, Role.Landmark).PinnedTo("mid", 0.024f).Graded(1f),
            Layer.Strip("flow", 0.025f, 0.30f, 0f, W).Graded(1f),
            Layer.Pieces("palls", 0.040f, Role.Atmosphere).Graded(.6f),
            Layer.Pieces("mist", 0.060f, Role.Atmosphere).Graded(.6f),
            Layer.Pieces("wisps", 0.090f, Role.Cloud).Graded(.6f),
            // the spore-cloud ceiling the planetfall drops through
            Layer.Pieces("ceiling", 0.130f, Role.Cloud),
            Layer.Pieces("pollen", 0.250f, Role.Atmosphere),
            Layer.Pieces("spores", 0.400f, Role.Atmosphere),
            Layer.Pieces("fireflies", 0.550f, Role.Atmosphere),
        }},
        new Spec { world = "Ember", layers = new[] {
            Layer.Tile("sky", 0.006f, W),
            Layer.Tile("far", 0.014f, W),
            Layer.Tile("mid", 0.024f, W),
            Layer.Strip("flow", 0.025f, 0.25f, 0.01f, new Color(1f, 1f, 1f, 0.85f)),
            Layer.Pieces("bursts", 0.026f, Role.Landmark),
            Layer.Pieces("volcanoes", 0.036f, Role.Landmark),
            Layer.Pieces("haze", 0.120f, Role.Cloud),
            Layer.Pieces("clouds", 0.300f, Role.Cloud),
            Layer.Pieces("embers", 0.450f, Role.Atmosphere),
            Layer.Pieces("ash", 0.600f, Role.Atmosphere),
        }},
    };

    public static Spec[] All { get { return specs; } }

    // Unknown worlds (a planet added without art) fall back to Space rather
    // than an empty sky.
    // Is the current world's backdrop bright (Spec.Bright)?
    public static bool CurrentIsBright
    {
        get { var spec = For(WorldManager.Current.displayName); return spec != null && spec.Bright; }
    }

    public static Spec For(string displayName)
    {
        foreach (var s in specs) if (s.world == displayName) return s;
        return specs[0];
    }

    public static string Folder(string world)
    {
        var spec = Exact(world);
        if (spec != null && !string.IsNullOrEmpty(spec.folder)) return spec.folder;
        return "Worlds/" + world + "/Backdrop/";
    }

    // The folder a world's tile layers load from: its variant's sub-folder
    // when it has variant sets (variant 1..N), else its art folder.
    public static string TileFolder(string world, int variant)
    {
        var spec = Exact(world);
        if (spec != null && spec.variantSets > 0 && variant > 0) return Folder(world) + "v" + variant + "/";
        return Folder(world);
    }

    static Spec Exact(string world)
    {
        foreach (var s in specs) if (s.world == world) return s;
        return null;
    }
}

// How much cloud a planet world shows (user, 2026-10-08: "both ice and
// verdant have too many clouds covering the backdrop; lower the amount of
// clouds after the first 10 seconds or so"). Shared by every planet
// director; each world exposes the knobs in its tuning class (FrostTuning /
// VerdantTuning .CloudDensity, .CeilingHold, .CeilingClearSeconds) through
// its Spec, and a world without them gets these defaults.
//
//   THE CEILING  the thick cloud deck the planetfall drops through: held at
//                full thickness for CeilingHold s (the arrival reads as
//                "we came out of the clouds"), then eased out fast -- the
//                banks fade and part from the middle, no new ones roll in
//                below BankSpawnFloor -- <= ~10% of the view covered by
//                ~10 s, gone at CeilingClearSeconds.
//   THE AIR      after it, a light scattering of wisps and low mist drifting
//                with the world's wind, so the ground art stays in view:
//                <= ~12% of the view veiled on average (CloudCoverMeter,
//                FrostBackdropTest / VerdantBackdropTest).
//   CloudDensity scales the air: 1 = that thin look (default), 2 = about
//                twice the pieces, a little stronger; .5 = half; 0 = none.
//                "A bit more / less cloud" = change this one number.
//
// Weather particles (snow, blizzard / pollen gusts, spores) and wildfire
// smoke are not clouds and keep their own tuning.
public static class CloudCover
{
    public const float Density = 1f;
    public const float CeilingHold = 4f;
    public const float CeilingClearSeconds = 14f;
    // new banks keep rolling in at the top only while the ceiling is this thick
    public const float BankSpawnFloor = .6f;
    // how fast the banks part sideways (units/s) once the ceiling has thinned out
    public const float PartSpeed = .5f;

    // 1 through the hold, then eased out fast to 0 at `clear`: (1 - smoothstep)^2
    // (~.12 left 60% of the way through).
    public static float Ceiling(float t, float hold, float clear)
    {
        if (t <= hold) return 1f;
        float x = Mathf.Clamp01((t - hold) / Mathf.Max(.01f, clear - hold));
        float k = 1f - x * x * (3f - 2f * x);
        return k * k;
    }

    // The air at density d: seconds between pieces are divided by Every(d)
    // (more pieces), their alpha multiplied by Alpha(d) (a little stronger).
    public static float Every(float d) { return Mathf.Max(0f, d); }
    public static float Alpha(float d) { return Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, d)), 0f, 1.6f); }

    // The countdown to the next air piece: false while density is 0.
    public static bool Tick(ref float left, float dt, float d, float min, float max, System.Random rng)
    {
        float k = Every(d);
        if (k <= 0f) return false;
        left -= dt;
        if (left > 0f) return false;
        left = (min + (float)rng.NextDouble() * (max - min)) / k;
        return true;
    }
}
