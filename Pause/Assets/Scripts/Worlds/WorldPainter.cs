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

    // Full-cell wall tiles (Frost, Ember) were tuned under the walls' old lit
    // shader, which showed them at this fraction of their art's colour (the
    // scene's one directional light plus ambient on a camera-facing quad;
    // measured lit / unlit on all three planet worlds). The walls are unlit
    // now (Pause/WorldWall), so this keeps those tiles looking exactly as
    // they did. Rail art (Space's pipes, Verdant's forest rail) shows its
    // colours as drawn.
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
        // Verdant's reinforced forest rail is authored once (as a left rail)
        // and mirrored for the opposite wall, keeping both gameplay-facing
        // edges identical.
        if (theme.displayName == "Verdant")
        {
            var rail = Resources.Load<Texture2D>(root + VerdantRailName);
            Paint(LeftWallName, rail, theme.tint, cachedLeft, false);
            Paint(RightWallName, rail, theme.tint, cachedRight, true);
        }
        else
        {
            Paint(LeftWallName, Resources.Load<Texture2D>(root + "wallLeft"), theme.tint, cachedLeft, false);
            Paint(RightWallName, Resources.Load<Texture2D>(root + "wallRight"), theme.tint, cachedRight, false);
        }
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
        Paint(LeftWallName, null, cachedLeftTint, cachedLeft, false);
        Paint(RightWallName, null, cachedRightTint, cachedRight, false);
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
    // `mirrorX`: the texture is drawn for the other wall and is flipped here.
    static void Paint(string objectName, Texture2D tex, Color tint, Texture fallback, bool mirrorX)
    {
        var mat = MaterialOf(objectName);
        if (mat == null) return;

        Texture selected = tex != null ? tex : fallback;
        // The scrolling material advances its UV offset indefinitely; Clamp
        // would stretch the final edge into a long line, so repeat the tile.
        if (selected != null) selected.wrapMode = TextureWrapMode.Repeat;
        mat.mainTexture = selected;
        RailArt art;
        if (!TryRailArt(selected, out art))
        {
            float a = tint.a;
            tint *= PlanetWallShade;
            tint.a = a;
        }
        if (mat.HasProperty("_Color")) mat.color = tint;
        var wall = GameObject.Find(objectName);
        SetMirrored(wall, mirrorX);
        FitBand(wall);
    }

    // ---------------------------------------------------------- rail art --

    // A rail texture is a band of art inside a wider cell, with transparent
    // margins on both sides (Space's pipe rails Assets/Art/left.png and
    // right.png, Verdant's forest rail). Columns [first, end) hold art;
    // `innerAtEnd`: the gameplay-facing edge of the art is its right one (a
    // rail drawn for the left wall). WorldBackdropTest re-measures the PNGs
    // against this table. Wall tiles that fill their cell (Frost, Ember) are
    // not listed. width / height are the PNG's; the imported texture may be
    // smaller (max-size clamp), which changes nothing: the fit works in
    // fractions of the cell.
    public struct RailArt
    {
        public string name;
        public int width, height, first, end;
        public bool innerAtEnd;
    }

    public const string VerdantRailName = "rail_forest_wide_v1";

    public static readonly RailArt[] Rails =
    {
        new RailArt { name = "left", width = 96, height = 448, first = 6, end = 76, innerAtEnd = true },
        new RailArt { name = "right", width = 96, height = 448, first = 20, end = 90, innerAtEnd = false },
        new RailArt { name = VerdantRailName, width = 725, height = 2169, first = 139, end = 577, innerAtEnd = true },
    };

    public static bool TryRailArt(Texture tex, out RailArt art)
    {
        art = default(RailArt);
        if (tex == null) return false;
        foreach (var r in Rails)
            if (tex.name == r.name) { art = r; return true; }
        return false;
    }

    static readonly int UBand = Shader.PropertyToID("_UBand");
    // Walls whose texture is drawn for the opposite side (see Paint).
    static readonly System.Collections.Generic.HashSet<int> mirrored = new System.Collections.Generic.HashSet<int>();

    static void SetMirrored(GameObject wall, bool on)
    {
        if (wall == null) return;
        if (on) mirrored.Add(wall.GetInstanceID());
        else mirrored.Remove(wall.GetInstanceID());
    }

    // Which slice of its texture a wall quad shows (Pause/WorldWall's _UBand).
    //
    // Only the wall's inner ~0.4 units are on screen on a phone (the view's
    // half-width is 2.85, the wall's inner edge 2.435), i.e. the inner 30% of
    // the quad. Stretching a rail texture's whole cell over the quad would
    // put little but the art's transparent inner margin there. So for a rail
    // texture the art's inner edge is pinned to the wall's inner edge, and
    // the texture is scaled across the quad so its texels come out as wide
    // as RailFit's height makes them tall (square pixels, at any screen
    // shape). The wall quads themselves never change size with the world, so
    // the lane, the wall colliders and everything measured from them
    // (BossRails, rail mines) are the same in every world. Every other wall
    // tile fills its cell and maps 0..1 (1..0 mirrored).
    // Called whenever the wall's texture (Paint) or size (RailFit) changes.
    public static void FitBand(GameObject wall)
    {
        var r = wall != null ? wall.GetComponent<Renderer>() : null;
        if (r == null) return;
        var mat = r.material;     // this wall's own instance, the one moveBackGround scrolls
        if (mat == null || !mat.HasProperty(UBand)) return;
        mat.SetVector(UBand, BandFor(mat.mainTexture, wall.transform.lossyScale, wall.transform.position.x < 0f,
                                     mirrored.Contains(wall.GetInstanceID())));
    }

    // (u at the quad's left edge, u at its right edge). `leftWall`: its inner
    // edge is its right one. `mirrorX`: the texture is shown flipped.
    public static Vector4 BandFor(Texture tex, Vector3 quadSize, bool leftWall, bool mirrorX = false)
    {
        RailArt art;
        if (!TryRailArt(tex, out art) || quadSize.y <= 0f)
            return mirrorX ? new Vector4(1f, 0f, 0f, 0f) : new Vector4(0f, 1f, 0f, 0f);
        // Fraction of the texture's width the quad spans at square texels.
        float across = Mathf.Abs(quadSize.x) * (art.height / quadSize.y) / art.width;
        // u of the art's gameplay-facing edge, and which way u runs from it
        // toward the wall's outer edge.
        float inner = (art.innerAtEnd ? art.end : art.first) / (float)art.width;
        float outer = inner + (art.innerAtEnd ? -across : across);
        return leftWall ? new Vector4(outer, inner, 0f, 0f) : new Vector4(inner, outer, 0f, 0f);
    }
}
