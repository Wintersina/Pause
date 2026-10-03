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
// Art lives at Resources/Worlds/<World>/Backdrop/ and is generated from the
// SVG templates under Assets/Art/Worlds/<World>/src~/.
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

    public const string AtlasFx = "fx";
    public const string AtlasAnim = "anim";

    static readonly Color W = Color.white;

    static readonly Spec[] specs =
    {
        // Space has no ground: everything is sky at its own depth.
        new Spec { world = "Space", layers = new[] {
            Layer.Tile("sky", 0.010f, W, Role.Sky),
            Layer.Pieces("wisps", 0.018f, Role.Sky),
            Layer.Pieces("galaxies", 0.026f, Role.Sky),
            Layer.Pieces("stars", 0.040f, Role.Sky),
            Layer.Pieces("stations", 0.055f, Role.Sky),
            Layer.Pieces("planets", 0.070f, Role.Sky),
            Layer.Pieces("moons", 0.095f, Role.Sky),
            Layer.Pieces("comets", 0.130f, Role.Sky),
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
            Layer.Tile("mid", 0.024f, W),
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
