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

    static bool cached;
    static Texture cachedLeft, cachedRight;
    static Color cachedLeftTint = Color.white, cachedRightTint = Color.white;
    static float cachedLeftWidth = 1f, cachedRightWidth = 1f;

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
        // Verdant's reinforced forest rail is authored once and mirrored for
        // the opposite wall, keeping both gameplay-facing edges identical.
        if (theme.displayName == "Verdant")
        {
            var rail = Resources.Load<Texture2D>(root + "rail_forest_wide_v1");
            Paint(LeftWallName, rail, theme.tint, cachedLeft, false);
            Paint(RightWallName, rail, theme.tint, cachedRight, true);
            SetRailWidth(1.25f);
        }
        else
        {
            Paint(LeftWallName, Resources.Load<Texture2D>(root + "wallLeft"), theme.tint, cachedLeft, false);
            Paint(RightWallName, Resources.Load<Texture2D>(root + "wallRight"), theme.tint, cachedRight, false);
            SetRailWidth(1f);
        }
    }

    static void CacheOriginals()
    {
        if (cached) return;
        cached = true;
        cachedLeft = MaterialOf(LeftWallName) != null ? MaterialOf(LeftWallName).mainTexture : null;
        cachedRight = MaterialOf(RightWallName) != null ? MaterialOf(RightWallName).mainTexture : null;
        var left = GameObject.Find(LeftWallName);
        var right = GameObject.Find(RightWallName);
        if (left != null) cachedLeftWidth = left.transform.localScale.x;
        if (right != null) cachedRightWidth = right.transform.localScale.x;

        var m = MaterialOf(LeftWallName); if (m != null && m.HasProperty("_Color")) cachedLeftTint = m.color;
        m = MaterialOf(RightWallName);    if (m != null && m.HasProperty("_Color")) cachedRightTint = m.color;
    }

    static void Restore()
    {
        Paint(LeftWallName, null, cachedLeftTint, cachedLeft, false);
        Paint(RightWallName, null, cachedRightTint, cachedRight, false);
        SetRailWidth(1f);
    }

    static void SetRailWidth(float factor)
    {
        var left = GameObject.Find(LeftWallName);
        var right = GameObject.Find(RightWallName);
        if (left != null)
        {
            var s = left.transform.localScale;
            s.x = cachedLeftWidth * factor;
            left.transform.localScale = s;
        }
        if (right != null)
        {
            var s = right.transform.localScale;
            s.x = cachedRightWidth * factor;
            right.transform.localScale = s;
        }
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
    static void Paint(string objectName, Texture2D tex, Color tint, Texture fallback, bool mirrorX)
    {
        var mat = MaterialOf(objectName);
        if (mat == null) return;

        Texture selected = tex != null ? tex : fallback;
        // The scrolling material advances its UV offset indefinitely; Clamp
        // would stretch the final edge into a long line, so repeat the tile.
        if (selected != null) selected.wrapMode = TextureWrapMode.Repeat;
        mat.mainTexture = selected;
        mat.mainTextureScale = new Vector2(mirrorX ? -1f : 1f, 1f);
        if (mat.HasProperty("_Color")) mat.color = tint;
    }
}
