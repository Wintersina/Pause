using System.Collections.Generic;
using UnityEngine;

// Frame tables for the pixel-art pickup family (Art/Pickups/src~/pixel_atoms.py
// draws every frame; the tick tables below must match its ATOM_IDLE_TICKS,
// DUST_IDLE, HEAL_IDLE_TICKS and BURST_TICKS). Ticks are 1/24 s, the art
// guide's flipbook clock.
// Serialized by number on the pickup prefabs (PickupFlipbook.kind): new
// kinds go on the end, never in the middle.
public enum PickupKind
{
    Shield = 0, Pause = 1, Dust = 2, DustSmall = 3, Heal = 4, Cooldown = 5, Magnet = 6,
    // the permanent shot atoms (ShotAtoms.cs), in ShotAtom order
    ShotTwin = 7, ShotPierce = 8, ShotSplit = 9, ShotRicochet = 10, ShotArc = 11, ShotRapid = 12,
}

public static class PickupArt
{
    public const float Tick = 1f / 24f;
    public const string Root = "Pickups/Atoms/";
    // The shot atoms' coins (shot_<atom>_idle_0..7, shot_<atom>_burst_0..5) live beside the pixel atoms, not in them: their art has
    // soft edges the pixel-atom palette check (ArtRestyleTest) would refuse.
    public const string ShotRoot = "Pickups/ShotAtoms/";
    public static bool IsShot(PickupKind kind) { return kind >= PickupKind.ShotTwin; }

    // Star dust in the main game (the small Star Dust, smStar_1, and the
    // large Bright Star, LargeStar_1) is drawn this much bigger than its
    // prefab: the instance's scale, so its trigger box -- what the hull and
    // ShipHitbox's pickup reach touch -- and its SpawnSpace footprint grow
    // with the art. The prefabs themselves (shared with the tutorial, the
    // credits and the codex icons) are untouched.
    public const float StarDustScale = 1.25f;

    // The small Star Dust (smStar1) was hard to spot, so it is drawn a further
    // SmallStarExtra bigger than StarDustScale (the large Bright Star is not).
    // Its trigger box grows only SmallStarBoxExtra of that, so collecting it
    // stays about as hard as before; its SpawnSpace footprint follows the art.
    public const float SmallStarExtra = 1.25f;
    public const float SmallStarBoxExtra = 1.1f;

    // The atoms (blue, red, violet, green, magnet) are drawn this much bigger than
    // their 0.28 u art in play, so they read at a glance on a phone (an atom
    // was ~40 px across on a 1080 px screen, a hostile shot ~32). 1.3 x 1.1:
    // the atoms were made a further 10% bigger. EnlargeVisual shrinks the
    // colliders back by the same factor, then AtomHitScale grows them, so the
    // hitbox and the pickup reach are 10% bigger than the original 0.28 u box.
    public const float AtomHitScale = 1.1f;
    public const float AtomVisualScale = 1.3f * AtomHitScale;

    // Scales `go` by k (x and y) and its 2D colliders by 1/k: bigger art,
    // the same world-space hitbox (times hitGrow, when given).
    public static void EnlargeVisual(GameObject go, float k, float hitGrow = 1f)
    {
        if (go == null || k <= 0f || Mathf.Approximately(k, 1f)) return;
        var s = go.transform.localScale;
        go.transform.localScale = new Vector3(s.x * k, s.y * k, s.z);
        float c = hitGrow / k;
        foreach (var box in go.GetComponents<BoxCollider2D>()) { box.size *= c; box.offset *= c; }
        foreach (var circle in go.GetComponents<CircleCollider2D>()) { circle.radius *= c; circle.offset *= c; }
    }

    public static bool IsStarDust(GameObject go)
    {
        return PrefabName.Is(go, "smStar1") || PrefabName.Is(go, "LargeStar1");
    }

    // StarDustScale for star dust, 1 for anything else.
    public static float InGameScale(GameObject prefab)
    {
        if (PrefabName.Is(prefab, "smStar1")) return StarDustScale * SmallStarExtra;
        return IsStarDust(prefab) ? StarDustScale : 1f;
    }

    // Grows a freshly spawned main-game pickup by InGameScale (x and y).
    public static void ApplyInGameScale(GameObject instance, GameObject prefab)
    {
        if (instance == null) return;
        float k = InGameScale(prefab != null ? prefab : instance);
        if (k == 1f) return;
        var s = instance.transform.localScale;
        instance.transform.localScale = new Vector3(s.x * k, s.y * k, s.z);
        if (PrefabName.Is(prefab != null ? prefab : instance, "smStar1"))
        {
            float shrink = SmallStarBoxExtra / SmallStarExtra;
            foreach (var box in instance.GetComponents<BoxCollider2D>()) { box.size *= shrink; box.offset *= shrink; }
            foreach (var circle in instance.GetComponents<CircleCollider2D>()) { circle.radius *= shrink; circle.offset *= shrink; }
        }
    }

    // Atom idle: rest pose, ten travelling drawings on 2s, anticipation, pop, settle.
    public static readonly int[] AtomIdleTicks = { 6, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 };
    // Star dust: rest, squash, stretch, spin through an edge-on smear, rest on the flip side, spin back.
    public static readonly int[] DustIdleTicks = { 8, 2, 2, 1, 1, 1, 4, 1, 1, 1 };
    // Green atom overlay: rest (the untouched original), electron glints in
    // sequence with 1-tick smears between them, nucleus pop, shock ring, rest.
    public static readonly int[] HealIdleTicks = { 8, 2, 1, 2, 1, 2, 1, 2, 2, 2, 3 };
    // Pickup burst: 1-tick white impact, burst star, ring breaking into shards.
    public static readonly int[] BurstTicks = { 1, 2, 2, 2, 2, 3 };
    // Shot atom coin: eight drawings of the spinning border (the symbol stays put), two ticks each.
    // The eight drawings cover 0-45 degrees of the octagon, which repeats every 45: looping them is a continuous spin. At 36 drawings
    // a second a 45 degree step takes 0.22 s, one apparent full turn about every 1.8 s. (The tick count is 1 each: the real clock
    // is ShotFrameSeconds, see HoldSeconds.)
    public static readonly int[] ShotIdleTicks = { 1, 1, 1, 1, 1, 1, 1, 1 };
    public const float ShotFrameSeconds = 1f / 36f;

    // How long drawing `ticks` of `kind`'s idle loop is held.
    public static float HoldSeconds(PickupKind kind, int ticks)
    {
        return IsShot(kind) ? ShotFrameSeconds * ticks : ticks * Tick;
    }

    public static string IdleName(PickupKind kind)
    {
        ShotAtom shot;
        if (ShotAtoms.TryAtomOf(kind, out shot)) return "shot_" + ShotAtoms.Key(shot) + "_idle";
        switch (kind)
        {
            case PickupKind.Shield: return "shield_idle";
            case PickupKind.Pause: return "pause_idle";
            case PickupKind.Cooldown: return "cooldown_idle";
            case PickupKind.Magnet: return "magnet_idle";
            case PickupKind.Dust: return "dust_idle";
            case PickupKind.DustSmall: return "dustsm_idle";
            default: return "heal_glint";
        }
    }

    public static string BurstName(PickupKind kind)
    {
        ShotAtom shot;
        if (ShotAtoms.TryAtomOf(kind, out shot)) return "shot_" + ShotAtoms.Key(shot) + "_burst";
        switch (kind)
        {
            case PickupKind.Shield: return "shield_burst";
            case PickupKind.Pause: return "pause_burst";
            case PickupKind.Cooldown: return "cooldown_burst";
            case PickupKind.Magnet: return "magnet_burst";
            case PickupKind.Heal: return "heal_burst";
            default: return "dust_burst";
        }
    }

    public static int[] IdleTicks(PickupKind kind)
    {
        if (IsShot(kind)) return ShotIdleTicks;
        switch (kind)
        {
            case PickupKind.Dust:
            case PickupKind.DustSmall: return DustIdleTicks;
            case PickupKind.Heal: return HealIdleTicks;
            default: return AtomIdleTicks;
        }
    }

    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();

    // Frames <name>_0 .. <name>_<count-1>, loaded once and shared.
    public static Sprite[] Frames(string name, int count)
    {
        Sprite[] frames;
        if (cache.TryGetValue(name, out frames) && frames != null && frames.Length == count && frames[0] != null)
            return frames;
        frames = new Sprite[count];
        string root = name.StartsWith("shot_", System.StringComparison.Ordinal) ? ShotRoot : Root;
        for (int i = 0; i < count; i++) frames[i] = Resources.Load<Sprite>(root + name + "_" + i);
        cache[name] = frames;
        return frames;
    }

    // Which pickup a spawned object is, from its flipbook or its prefab name.
    public static bool TryKindOf(GameObject go, out PickupKind kind)
    {
        kind = PickupKind.Shield;
        if (go == null) return false;
        PickupFlipbook book;
        if (go.TryGetComponent(out book)) { kind = book.kind; return true; }
        if (PrefabName.Is(go, HealAtom.ObjectName)) { kind = PickupKind.Heal; return true; }
        if (PrefabName.Is(go, "atom3a")) { kind = PickupKind.Shield; return true; }
        if (PrefabName.Is(go, "pauseAtom")) { kind = PickupKind.Pause; return true; }
        if (PrefabName.Is(go, "cooldownAtom")) { kind = PickupKind.Cooldown; return true; }
        if (PrefabName.Is(go, MagnetPull.ObjectName)) { kind = PickupKind.Magnet; return true; }
        if (PrefabName.Is(go, "LargeStar1")) { kind = PickupKind.Dust; return true; }
        if (PrefabName.Is(go, "smStar1")) { kind = PickupKind.DustSmall; return true; }
        return false;
    }
}
