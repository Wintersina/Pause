// Hulls drawn as hi-res neon pixel art (Codex-painted, Paragon's style) instead of flat vector cels.
// The vector-art tests (flat fills in the guide palette, exact friendly-red pixels, the 2.5x damage
// ratio) do not describe painted art, so they skip these ids; the painted rules are the sheet contract
// (same alpha across states and skins, damage that clearly progresses) which every test still checks.
// A ship is painted when its own data file says so ("painted": true in ShipArt/<Key>/<Key>_data.json,
// written by the restyle pipeline); there is no list to edit here.
public static class PaintedShips
{
    // LEGACY-COMPAT: 1 NeonComet, 2 VoltViper, 3 SolarFang, 4 CrimsonHalo, 5 IonLancer, 6 JadePhantom.
    static readonly int[] restyled = { 1, 2, 3, 4, 5, 6 };

    public static bool Is(int id)
    {
        var d = ShipData.For(id);
        if (d != null) return d.painted;
        if (PremiumShips.IsPremium(id)) return true;
        for (int i = 0; i < restyled.Length; i++) if (restyled[i] == id) return true;
        return false;
    }
}
