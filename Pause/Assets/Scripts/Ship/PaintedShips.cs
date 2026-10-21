// Hulls drawn as hi-res neon pixel art (Codex-painted, Paragon's style) instead of flat vector cels.
// The vector-art tests (flat fills in the guide palette, exact friendly-red pixels, the 2.5x damage
// ratio) do not describe painted art, so they skip these ids; the painted rules are the sheet contract
// (same alpha across states and skins, damage that clearly progresses) which every test still checks.
// Add a ship's id here when its art is restyled (src~/build_restyle.py, the restyle-ship skill).
public static class PaintedShips
{
    // 1 NeonComet. (Paragon, id 16, is painted too: see PremiumShips.IsPremium.)
    static readonly int[] restyled = { 1, 5 };

    public static bool Is(int id)
    {
        if (PremiumShips.IsPremium(id)) return true;
        for (int i = 0; i < restyled.Length; i++) if (restyled[i] == id) return true;
        return false;
    }
}
