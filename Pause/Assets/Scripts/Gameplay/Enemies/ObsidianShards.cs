using UnityEngine;

// OBSIDIAN SHARD: the Ember rock "ember_rock_obsidian" (a slim violet-black glass blade) was hard to notice -- one near-black
// tone, and the smallest ones drawn only ~0.5 u long. Two rules, both data here:
//
//   COLOUR. The shard comes in five glass colours: the original violet-blue (variant 0, the roster strip itself) and four
//   re-hued copies (Resources/Enemies/Variants/ember_rock_obsidian_<name>.png + Variants/Death/..., built by
//   Art/Enemies/src~/obsidian_variants.py). The lava veins and crack glow stay Ember orange in every colour; no variant is pink.
//   Each spawned shard gets ONE variant when it is built, from a hash of its spawn position and size (Pick): stable for that
//   shard for its whole life (flipbook, hit flash and death burst all use it), the same on a replay of the same seed, and it
//   never touches UnityEngine.Random or HazardSize's size stream, so every seeded board is otherwise unchanged.
//
//   MIN SIZE. A shard is never drawn shorter than MinLongEdge world units along its blade, whatever size HazardSize draws
//   (EnemyBehaviour.MinLongEdge turns that into a scale floor: HazardSize.FloorFor). 0.60 u is ~87 px on a phone (145 px/u) and
//   an iPad alike, since the camera fits the same world width. It is a hair above the shared rock floor
//   (HazardSize.MinDrawnWidth, 0.58 u: a rock is never narrower than that), because a blade is slim, so its length is what
//   the eye reads. The smallest shard used to be 0.49 u long. The collider follows the scale as for every rock
//   (the +23 % at the small end is the only growth): the blade's trigger is already a 0.57 u square around a 0.19 u wide
//   blade, so a shard that is 23 % bigger is exactly as fair as a typical one.
public static class ObsidianShards
{
    public const string Key = "ember_rock_obsidian";

    // Variant 0 is the roster strip; the rest are the Variants/ files.
    public static readonly string[] Names = { "violet", "teal", "jade", "acid", "vermilion" };
    public static int Count => Names.Length;

    // Shortest drawn length of the blade (world units) at any size.
    public const float MinLongEdge = .60f;
    // The blade's silhouette: 164 of the 192 px cell tall (pinned by ObsidianShardTest against the strip).
    public const float SilhouetteLong = 164f / 192f;

    public static bool Applies(EnemyDef def) { return def != null && def.key == Key; }

    static readonly string[] stripPaths = BuildPaths("Enemies/Variants/ember_rock_obsidian_");
    static readonly string[] deathPaths = BuildPaths("Enemies/Variants/Death/ember_rock_obsidian_");

    static string[] BuildPaths(string prefix)
    {
        var paths = new string[Names.Length];
        for (int i = 1; i < paths.Length; i++) paths[i] = prefix + Names[i];
        return paths;
    }

    // The Resources path of a variant's seven-frame strip / three-frame death strip (null for variant 0: the roster's own).
    public static string StripPath(int variant) { return variant > 0 && variant < stripPaths.Length ? stripPaths[variant] : null; }
    public static string DeathPath(int variant) { return variant > 0 && variant < deathPaths.Length ? deathPaths[variant] : null; }

    // The shard's variant for a seed: uniform over 0..Count-1, deterministic.
    public static int Pick(uint seed)
    {
        uint h = seed * 2654435761u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return (int)(h % (uint)Count);
    }

    // The seed of a shard built at `position` with size `size`.
    public static uint SeedFor(Vector3 position, float size)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)Mathf.RoundToInt(position.x * 1000f)) * 16777619u;
        h = (h ^ (uint)Mathf.RoundToInt(position.y * 1000f)) * 16777619u;
        h = (h ^ (uint)Mathf.RoundToInt(size * 100000f)) * 16777619u;
        return h;
    }

    // The scale at which the blade is exactly MinLongEdge long.
    public static float MinScale()
    {
        return MinLongEdge / (SilhouetteLong * EnemyRoster.FrameWorldSize(EnemyRole.Rock));
    }
}
