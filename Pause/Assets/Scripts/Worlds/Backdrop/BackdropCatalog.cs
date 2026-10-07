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
// Art lives at Art/Backgrounds/Resources/Worlds/<World>/Backdrop/ and is generated from the
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
        new Spec { world = "Frost", layers = new[] {
            Layer.Tile("sky", 0.006f, W),
            Layer.Tile("far", 0.014f, W),
            Layer.Tile("mid", 0.024f, W),
            Layer.Strip("flow", 0.025f, 0.35f, 0f, W),
            Layer.Pieces("geysers", 0.026f, Role.Landmark),
            Layer.Pieces("glaciers", 0.034f, Role.Landmark),
            Layer.Pieces("aurora", 0.060f, Role.Atmosphere),
            Layer.Pieces("haze", 0.120f, Role.Cloud),
            Layer.Pieces("clouds", 0.300f, Role.Cloud),
            Layer.Pieces("snow", 0.500f, Role.Atmosphere),
        }},
        new Spec { world = "Verdant", layers = new[] {
            Layer.Tile("sky", 0.006f, W),
            Layer.Tile("far", 0.014f, W),
            // Distant planet-side industry stays beneath the high-contrast
            // rail frame and ship silhouettes.
            Layer.Tile("mid", 0.024f, new Color(.78f, .78f, .78f, 1f)).WithTexture("forest_industrial_center_v1")
                .WrapBlended(0.50f),
            Layer.Strip("flow", 0.025f, 0.30f, 0f, W),
            Layer.Pieces("waterfalls", 0.030f, Role.Landmark),
            Layer.Pieces("ruins", 0.036f, Role.Landmark),
            Layer.Pieces("haze", 0.120f, Role.Cloud),
            Layer.Pieces("clouds", 0.300f, Role.Cloud),
            Layer.Pieces("glowspores", 0.400f, Role.Atmosphere),
            Layer.Pieces("spores", 0.500f, Role.Atmosphere),
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
    public static Spec For(string displayName)
    {
        foreach (var s in specs) if (s.world == displayName) return s;
        return specs[0];
    }

    public static string Folder(string world)
    {
        return "Worlds/" + world + "/Backdrop/";
    }
}
