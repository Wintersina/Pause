using UnityEngine;

// Which layers each world's animated background is built from.
//
// Every world is a stack of depth layers, far to near. Each layer moves at
// `rate` x the foreground scroll velocity (see WorldBackdrop.ScrollVelocity),
// so a higher rate reads as closer. Rates must strictly increase with depth;
// WorldBackdropTest enforces it.
//
// Tile layers are seamless 512x1024 PNGs leap-frogged vertically. Set pieces
// (planets, peaks, volcanoes...) and particles come from each world's `fx`
// and `anim` atlases and are driven by that world's BackdropDirector.
//
// Art lives at Resources/Worlds/<World>/Backdrop/ and is generated from the
// SVG templates under Assets/Art/Worlds/<World>/src~/.
public static class BackdropCatalog
{
    public enum Kind { Tile, Strip, Pieces }

    public struct Layer
    {
        public string name;
        public Kind kind;
        public float rate;          // fraction of foreground scroll velocity
        public string texture;      // Tile / Strip only
        public float flow;          // Strip: extra units/s on top of the parallax
        public float wobble;        // Strip: heat-shimmer sway, units
        public Color tint;

        public static Layer Tile(string name, float rate, Color tint)
        {
            return new Layer { name = name, kind = Kind.Tile, rate = rate, texture = name, tint = tint };
        }

        public static Layer Strip(string name, float rate, float flow, float wobble, Color tint)
        {
            return new Layer { name = name, kind = Kind.Strip, rate = rate, texture = name, flow = flow,
                               wobble = wobble, tint = tint };
        }

        public static Layer Pieces(string name, float rate)
        {
            return new Layer { name = name, kind = Kind.Pieces, rate = rate, tint = Color.white };
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
        new Spec { world = "Space", layers = new[] {
            Layer.Tile("sky", 0.010f, W),
            Layer.Pieces("wisps", 0.018f),
            Layer.Pieces("galaxies", 0.026f),
            Layer.Pieces("stars", 0.040f),
            Layer.Pieces("stations", 0.055f),
            Layer.Pieces("planets", 0.070f),
            Layer.Pieces("moons", 0.095f),
            Layer.Pieces("comets", 0.130f),
            Layer.Pieces("dust", 0.600f),
        }},
        new Spec { world = "Frost", layers = new[] {
            Layer.Tile("sky", 0.010f, W),
            Layer.Tile("far", 0.040f, W),
            Layer.Tile("mid", 0.100f, W),
            Layer.Strip("flow", 0.101f, 1.6f, 0f, W),
            Layer.Pieces("geysers", 0.102f),
            Layer.Pieces("peaks", 0.160f),
            Layer.Pieces("aurora", 0.220f),
            Layer.Pieces("snow", 0.500f),
        }},
        new Spec { world = "Verdant", layers = new[] {
            Layer.Tile("sky", 0.010f, W),
            Layer.Tile("far", 0.040f, W),
            Layer.Tile("mid", 0.100f, W),
            Layer.Strip("flow", 0.101f, 1.4f, 0f, W),
            Layer.Pieces("waterfalls", 0.102f),
            Layer.Pieces("ruins", 0.160f),
            Layer.Pieces("fireflies", 0.350f),
            Layer.Pieces("spores", 0.500f),
        }},
        new Spec { world = "Ember", layers = new[] {
            Layer.Tile("sky", 0.010f, W),
            Layer.Tile("far", 0.040f, W),
            Layer.Tile("mid", 0.100f, W),
            Layer.Strip("flow", 0.101f, 0.9f, 0.035f, new Color(1f, 1f, 1f, 0.75f)),
            Layer.Pieces("bubbles", 0.102f),
            Layer.Pieces("volcanoes", 0.160f),
            Layer.Pieces("embers", 0.450f),
            Layer.Pieces("ash", 0.600f),
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
