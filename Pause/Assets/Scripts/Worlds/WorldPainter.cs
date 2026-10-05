using UnityEngine;

// Swaps the scene's side-wall textures to match a world.
//
// Finds the objects by the names the scene already uses rather than requiring
// new references, and caches the originals so the space world can always be
// restored exactly as authored. The backdrop behind the walls is no longer a
// texture swap: WorldBackdrop builds an animated parallax set per world.
public static class WorldPainter
{
    const string LeftWallName = "leftPipe";
    const string RightWallName = "rightPipe";

    // The planet worlds' wall tiles were tuned under the walls' old lit
    // shader, which showed them at this fraction of their art's colour (the
    // scene's one directional light plus ambient on a camera-facing quad;
    // measured lit / unlit on all three worlds). The walls are unlit now
    // (Pause/WorldWall), so this keeps those worlds looking exactly as they
    // did. Space's rails show their art as drawn.
    public static readonly Color PlanetWallShade = new Color(0.71f, 0.72f, 0.755f, 1f);

    static bool cached;
    static Texture cachedLeft, cachedRight;
    static Color cachedLeftTint = Color.white, cachedRightTint = Color.white;

    public static void Apply(WorldTheme theme)
    {
        if (theme == null) return;

        CacheOriginals();

        if (string.IsNullOrEmpty(theme.resourceFolder))
        {
            Restore();
            return;
        }

        string root = "Worlds/" + theme.resourceFolder + "/";
        Color tint = theme.tint * PlanetWallShade;
        tint.a = theme.tint.a;
        Paint(LeftWallName, Resources.Load<Texture2D>(root + "wallLeft"), tint, cachedLeft);
        Paint(RightWallName, Resources.Load<Texture2D>(root + "wallRight"), tint, cachedRight);
    }

    static void CacheOriginals()
    {
        if (cached) return;
        cached = true;
        cachedLeft = MaterialOf(LeftWallName) != null ? MaterialOf(LeftWallName).mainTexture : null;
        cachedRight = MaterialOf(RightWallName) != null ? MaterialOf(RightWallName).mainTexture : null;

        var m = MaterialOf(LeftWallName); if (m != null && m.HasProperty("_Color")) cachedLeftTint = m.color;
        m = MaterialOf(RightWallName);    if (m != null && m.HasProperty("_Color")) cachedRightTint = m.color;
    }

    static void Restore()
    {
        Paint(LeftWallName, null, cachedLeftTint, cachedLeft);
        Paint(RightWallName, null, cachedRightTint, cachedRight);
    }

    static Material MaterialOf(string objectName)
    {
        var go = GameObject.Find(objectName);
        if (go == null) return null;
        var r = go.GetComponent<Renderer>();
        return r != null ? r.material : null;
    }

    // A missing texture falls back to the cached original rather than painting
    // the world black -- a half-shipped planet should still be playable.
    static void Paint(string objectName, Texture2D tex, Color tint, Texture fallback)
    {
        var mat = MaterialOf(objectName);
        if (mat == null) return;

        Texture selected = tex != null ? tex : fallback;
        // The scrolling material advances its UV offset indefinitely; Clamp
        // would stretch the final edge into a long line, so repeat the tile.
        if (selected != null) selected.wrapMode = TextureWrapMode.Repeat;
        mat.mainTexture = selected;
        if (mat.HasProperty("_Color")) mat.color = tint;
        FitBand(GameObject.Find(objectName));
    }

    // ---------------------------------------------------------- rail art --

    // Space's pipe rails (Assets/Art/left.png, right.png) don't fill their
    // 64-px-wide cell: the art is a 32-px band with transparent margins on
    // both sides. Columns [first, end) that hold art, per texture name;
    // WorldBackdropTest re-measures the PNGs against these.
    public const int RailCellWidth = 64, RailCellHeight = 448;
    public const int LeftRailArtFirst = 12, LeftRailArtEnd = 44;
    public const int RightRailArtFirst = 20, RightRailArtEnd = 52;

    static readonly int UBand = Shader.PropertyToID("_UBand");

    // Which slice of its texture a wall quad shows (Pause/WorldWall's _UBand).
    //
    // Only the wall's inner ~0.35 units are on screen on a phone (the view's
    // half-width is 2.85, the wall's inner edge 2.5), i.e. the inner quarter
    // of the quad. Stretching the whole 64-px cell over the quad would put
    // nothing but the rail art's transparent inner margin there. So for a
    // rail texture the art's inner edge is pinned to the wall's inner edge,
    // and the texture is scaled across the quad so its texels come out as
    // wide as RailFit's height makes them tall (square pixels, at any screen
    // shape). Every other wall tile fills its cell and maps 0..1 as before.
    // Called whenever the wall's texture (Paint) or size (RailFit) changes.
    public static void FitBand(GameObject wall)
    {
        var r = wall != null ? wall.GetComponent<Renderer>() : null;
        if (r == null) return;
        var mat = r.material;     // this wall's own instance, the one moveBackGround scrolls
        if (mat == null || !mat.HasProperty(UBand)) return;
        mat.SetVector(UBand, BandFor(mat.mainTexture, wall.transform.lossyScale, wall.transform.position.x < 0f));
    }

    // (u at the quad's left edge, u at its right edge). `leftWall`: its inner
    // edge is its right one.
    public static Vector4 BandFor(Texture tex, Vector3 quadSize, bool leftWall)
    {
        int first, end;
        if (!RailArt(tex, out first, out end) || quadSize.y <= 0f) return new Vector4(0f, 1f, 0f, 0f);
        // Fraction of the texture's width the quad spans at square texels.
        float across = Mathf.Abs(quadSize.x) * (RailCellHeight / quadSize.y) / RailCellWidth;
        float inner = (leftWall ? end : first) / (float)RailCellWidth;
        return leftWall ? new Vector4(inner - across, inner, 0f, 0f) : new Vector4(inner, inner + across, 0f, 0f);
    }

    static bool RailArt(Texture tex, out int first, out int end)
    {
        first = 0; end = RailCellWidth;
        if (tex == null || tex.width != RailCellWidth) return false;
        if (tex.name == "left") { first = LeftRailArtFirst; end = LeftRailArtEnd; return true; }
        if (tex.name == "right") { first = RightRailArtFirst; end = RightRailArtEnd; return true; }
        return false;
    }
}
