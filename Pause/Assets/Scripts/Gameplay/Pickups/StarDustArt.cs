using UnityEngine;

public enum DustSize { Small = 0, Medium = 1, Large = 2 }

// The three star-dust drawings (small dustsm, medium dustmd, large dust: frame 0 of each idle flipbook in
// Resources/Pickups/Atoms) for everything that shows star dust OUTSIDE the in-run pickups: the Shop's and Codex's
// payout bursts, the death panel's dust sparkles, the splash gate's dust burst, the credits' falling stars.
// A burst mixes all three sizes: Mixed(i) cycles small, medium, large, medium so the sizes interleave.
public static class StarDustArt
{
    public const int Sizes = 3;
    static readonly string[] names = { "dustsm_idle_0", "dustmd_idle_0", "dust_idle_0" };
    // How big each reads next to the others in a burst, as a multiple of the burst's base size (small .6, medium 1, large 1.5).
    static readonly float[] relative = { .6f, 1f, 1.5f };
    // small, medium, large, medium: medium is the most common so the middle size reads as the "normal" dust.
    static readonly DustSize[] cycle = { DustSize.Small, DustSize.Medium, DustSize.Large, DustSize.Medium };

    static readonly Sprite[] cache = new Sprite[Sizes];

    public static string SpriteName(DustSize size) { return names[(int)size]; }

    public static Sprite Get(DustSize size)
    {
        int i = (int)size;
        if (cache[i] == null) cache[i] = Resources.Load<Sprite>(PickupArt.Root + names[i]);
        return cache[i];
    }

    public static DustSize SizeAt(int index) { return cycle[((index % cycle.Length) + cycle.Length) % cycle.Length]; }

    // The sprite for the index-th piece of a burst (never null while the art is imported).
    public static Sprite Mixed(int index) { return Get(SizeAt(index)); }

    // The index-th piece's size relative to the burst's base size.
    public static float Relative(int index) { return relative[(int)SizeAt(index)]; }

    // Whether `sprite` is one of the three dust drawings, and how big it reads in a burst (for a pool that mixes them).
    public static bool IsDust(Sprite sprite) { return sprite != null && System.Array.IndexOf(cache, sprite) >= 0; }
    public static float RelativeOf(Sprite sprite) { int i = System.Array.IndexOf(cache, sprite); return i < 0 ? 1f : relative[i]; }

    // Every size loads, in the order small, medium, large.
    public static bool AllLoad()
    {
        for (int i = 0; i < Sizes; i++) if (Get((DustSize)i) == null) return false;
        return true;
    }
}
