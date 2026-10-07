using System.Collections.Generic;
using UnityEngine;

// Frame tables for the pixel-art pickup family (Art/Pickups/src~/pixel_atoms.py
// draws every frame; the tick tables below must match its ATOM_IDLE_TICKS,
// DUST_IDLE, HEAL_IDLE_TICKS and BURST_TICKS). Ticks are 1/24 s, the art
// guide's flipbook clock.
// Serialized by number on the pickup prefabs (PickupFlipbook.kind): new
// kinds go on the end, never in the middle.
public enum PickupKind { Shield = 0, Pause = 1, Dust = 2, DustSmall = 3, Heal = 4, Cooldown = 5 }

public static class PickupArt
{
    public const float Tick = 1f / 24f;
    public const string Root = "Pickups/Atoms/";

    // Star dust in the main game (the small Star Dust, smStar_1, and the
    // large Bright Star, LargeStar_1) is drawn this much bigger than its
    // prefab: the instance's scale, so its trigger box -- what the hull and
    // ShipHitbox's pickup reach touch -- and its SpawnSpace footprint grow
    // with the art. The prefabs themselves (shared with the tutorial, the
    // credits and the codex icons) are untouched.
    public const float StarDustScale = 1.25f;

    public static bool IsStarDust(GameObject go)
    {
        return PrefabName.Is(go, "smStar1") || PrefabName.Is(go, "LargeStar1");
    }

    // StarDustScale for star dust, 1 for anything else.
    public static float InGameScale(GameObject prefab)
    {
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

    public static string IdleName(PickupKind kind)
    {
        switch (kind)
        {
            case PickupKind.Shield: return "shield_idle";
            case PickupKind.Pause: return "pause_idle";
            case PickupKind.Cooldown: return "cooldown_idle";
            case PickupKind.Dust: return "dust_idle";
            case PickupKind.DustSmall: return "dustsm_idle";
            default: return "heal_glint";
        }
    }

    public static string BurstName(PickupKind kind)
    {
        switch (kind)
        {
            case PickupKind.Shield: return "shield_burst";
            case PickupKind.Pause: return "pause_burst";
            case PickupKind.Cooldown: return "cooldown_burst";
            case PickupKind.Heal: return "heal_burst";
            default: return "dust_burst";
        }
    }

    public static int[] IdleTicks(PickupKind kind)
    {
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
        for (int i = 0; i < count; i++) frames[i] = Resources.Load<Sprite>(Root + name + "_" + i);
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
        if (PrefabName.Is(go, "LargeStar1")) { kind = PickupKind.Dust; return true; }
        if (PrefabName.Is(go, "smStar1")) { kind = PickupKind.DustSmall; return true; }
        return false;
    }
}
