using System;
using UnityEngine;

// HOW A HOSTILE SHOT LOOKS AND MOVES IN ITS OWN WORLD (docs/world-attacks-design.md 0.4).
//
// A skin is DATA, looked up by the shot's own world and kind -- never by the
// code that fired it -- so the whole roster and elite set is re-themed by art
// alone (a Frost icicle, a Verdant thorn, an Ember pellet) with no behaviour
// edit. EliteShot.Launch asks ShotSkins.For(world, kind); the answer says:
//
//   frames       the sprite pair (a, b) swapped at FrameFps, stepped like the
//                other hostile things (design PC5: never an atom's smooth
//                breath). b == a: one still frame.
//   drawScale    drawn size over the shot's nominal size (themed art is drawn
//                2x; the HIT RADIUS never changes -- the keyline hugs the
//                drawing, the hitbox stays where it was)
//   motion       flags for the projectile behaviours of plan phase 1f (Streak,
//                Shatter, Flutter, Slash, Roll, Burst); None today
//   trail/impact ids into the world's fx atlas (AttackArt), "" = none
//   sound ids    AttackAudio cue keys (plan phase 1h), "" = silent
//
// THE DEFAULT IS TODAY'S SHOT. Every (world, kind) resolves to the procedural
// skin built from EliteFxArt (the pink bolt / shard / shell / slag / pool /
// slab / orb as they are), frames a == b, drawScale 1, no motion flags. A
// world's themed skins only take over after its phase calls
// ShotSkins.Enable(world) -- and then only the kinds the world's art covers
// (AttackArt supplies cells; the rest keep the procedural skin). So code lands
// and passes its tests before Codex's art does, and the plumbing is inert
// until a phase switches it on.
[Flags]
public enum ShotMotion
{
    None = 0,
    Streak = 1,    // very fast slug with a long trail
    Shatter = 2,   // splits into chips at a fuse or on a rail
    Flutter = 4,   // sinusoidal sideways flutter + spin
    Slash = 8,     // thin crescent crossing the lane on its row
    Roll = 16,     // a lobbed heavy log: lands, rolls, bounces off the rails
    Burst = 32,    // a lobbed pod that opens into a lingering cloud
}

public sealed class ShotSkin
{
    public int world;
    public EliteShots.Kind kind;
    public Sprite a, b;                 // never null once built (b == a for a still skin)
    public float drawScale = 1f;
    public float frameFps = 10f;        // EliteArt.Tick-ish: 8..12 fps for a two-frame skin
    public ShotMotion motion = ShotMotion.None;
    public string trail = "", impact = "", special = "";    // AttackArt fx cells ("" none)
    public string soundFire = "", soundImpact = "";         // AttackAudio cue keys ("" silent)
    public bool procedural = true;      // true: the EliteFxArt sprite, tinted by the game as today

    public bool TwoFrames => b != null && b != a;

    // The frame to show `age` seconds after launch.
    public Sprite FrameAt(float age)
    {
        if (!TwoFrames) return a;
        return (Mathf.FloorToInt(age * frameFps) & 1) == 0 ? a : b;
    }
}

public static class ShotSkins
{
    public const int Worlds = 5;   // space, frost, verdant, ember, tide
    public static readonly string[] WorldKeys = { "space", "frost", "verdant", "ember", "tide" };

    static readonly ShotSkin[] procedural = new ShotSkin[Worlds * 16];
    static readonly ShotSkin[] themed = new ShotSkin[Worlds * 16];
    static readonly bool[] enabled = new bool[Worlds];
    // tests: skins that replace whatever the table says for (world, kind)
    static readonly ShotSkin[] overrides = new ShotSkin[Worlds * 16];

    public static bool Enabled(int world) => world >= 0 && world < Worlds && enabled[world];

    // A phase's wiring commit calls this once the world's art is in Resources/Attacks/<World>/.
    public static void Enable(int world, bool on = true)
    {
        if (world < 0 || world >= Worlds) return;
        enabled[world] = on;
    }

    public static int WorldOf(EliteDef d)
    {
        if (d == null) return 0;
        return Mathf.Clamp(d.WorldIndex, 0, Worlds - 1);
    }

    public static ShotSkin For(EliteDef d, EliteShots.Kind kind) => For(WorldOf(d), kind);

    // Never null. The procedural skin unless the world is enabled and its art covers `kind`.
    public static ShotSkin For(int world, EliteShots.Kind kind)
    {
        world = Mathf.Clamp(world, 0, Worlds - 1);
        int k = (int)kind;
        var o = overrides[(world) * 16 + (k)];
        if (o != null) return o;
        if (enabled[world])
        {
            var t = themed[(world) * 16 + (k)];
            // (runtime-made sprites can be unloaded with a scene: a stale skin is built again)
            if (t == null || t.a == null || t.b == null)
            {
                // (a kind the art does not cover is not cached: the procedural skin answers, and art that arrives later is found)
                t = BuildThemed(world, kind);
                if (t == null) return Procedural(world, kind);
                themed[(world) * 16 + (k)] = t;
            }
            return t;
        }
        return Procedural(world, kind);
    }

    // Today's shot for (world, kind): the EliteFxArt drawing, one frame, drawn at its nominal size.
    public static ShotSkin Procedural(int world, EliteShots.Kind kind)
    {
        world = Mathf.Clamp(world, 0, Worlds - 1);
        int k = (int)kind;
        var s = procedural[(world) * 16 + (k)];
        if (s == null) s = procedural[(world) * 16 + (k)] = new ShotSkin { world = world, kind = kind, drawScale = 1f, procedural = true };
        // EliteFxArt rebuilds a sprite the editor or a scene change unloaded; the skin follows it
        if (s.a == null || s.b == null) s.a = s.b = FallbackSprite(kind);
        return s;
    }

    // The EliteFxArt drawing a kind has always used (a landed glob swaps to Pool in EliteShot.Land).
    public static Sprite FallbackSprite(EliteShots.Kind kind)
    {
        switch (kind)
        {
            case EliteShots.Kind.Shard: return EliteFxArt.Shard;
            case EliteShots.Kind.Slag: return EliteFxArt.Slag;
            case EliteShots.Kind.Shell: return EliteFxArt.Shell;
            case EliteShots.Kind.Glob: return EliteFxArt.Slag;
            case EliteShots.Kind.Slab: return EliteFxArt.Slab;
            case EliteShots.Kind.Orb: return EliteFxArt.Orb;
            default: return EliteFxArt.Bolt;
        }
    }

    // The sprite a landed pool wears: the world's pool cell when themed, else today's puddle.
    public static Sprite PoolSprite(int world, out Sprite b)
    {
        world = Mathf.Clamp(world, 0, Worlds - 1);
        b = null;
        if (enabled[world])
        {
            var art = AttackArt.ShotCell(world, AttackArt.ShotCellName.PoolA);
            if (art != null)
            {
                b = AttackArt.ShotCell(world, AttackArt.ShotCellName.PoolB);
                return art;
            }
        }
        return EliteFxArt.Pool;
    }

    // Themed skin from the world's shots atlas (AttackArt.ShotCell), or null when its art does not cover the kind.
    static ShotSkin BuildThemed(int world, EliteShots.Kind kind)
    {
        AttackArt.ShotCellName a, b;
        switch (kind)
        {
            case EliteShots.Kind.Bolt: a = AttackArt.ShotCellName.BoltA; b = AttackArt.ShotCellName.BoltB; break;
            case EliteShots.Kind.Shard: a = AttackArt.ShotCellName.ShardA; b = AttackArt.ShotCellName.ShardB; break;
            case EliteShots.Kind.Shell: a = AttackArt.ShotCellName.ShellA; b = AttackArt.ShotCellName.ShellB; break;
            case EliteShots.Kind.Slag: a = AttackArt.ShotCellName.SlagA; b = AttackArt.ShotCellName.SlagB; break;
            case EliteShots.Kind.Glob: a = AttackArt.ShotCellName.SlagA; b = AttackArt.ShotCellName.SlagB; break;   // (the glob in the air; the landed pool is PoolSprite)
            default: return null;   // slabs and orbs are the world's own ice already
        }
        var sa = AttackArt.ShotCell(world, a);
        if (sa == null) return null;
        var sb = AttackArt.ShotCell(world, b) ?? sa;
        return new ShotSkin { world = world, kind = kind, a = sa, b = sb, drawScale = AttackArt.ThemedDrawScale, procedural = false };
    }

    // ---- tests -----------------------------------------------------------------

    public static void Override(int world, EliteShots.Kind kind, ShotSkin skin) { overrides[Mathf.Clamp(world, 0, Worlds - 1) * 16 + (int)kind] = skin; }

    public static void ResetForTests()
    {
        for (int w = 0; w < Worlds; w++)
        {
            enabled[w] = false;
            for (int k = 0; k < 16; k++) { overrides[(w) * 16 + (k)] = null; themed[(w) * 16 + (k)] = null; }
        }
    }
}
