// Hulls drawn as hi-res neon pixel art (Codex-painted, Paragon's style) instead of flat vector cels.
// The vector-art tests (flat fills in the guide palette, exact friendly-red pixels, the 2.5x damage
// ratio) do not describe painted art, so they skip these ids; the painted rules are the sheet contract
// (same alpha across states and skins, damage that clearly progresses) which every test still checks.
// A ship is painted when its own data file says so ("painted": true in ShipArt/<Key>/<Key>_data.json,
// written by the restyle pipeline); there is no list to edit here.
public static class PaintedShips
{
    public static bool Is(int id)
    {
        var d = ShipData.For(id);
        return d != null && d.painted;
    }
}
