using UnityEngine;

// Swaps the scene's backdrop and side-wall textures to match a world.
//
// Finds the objects by the names the scene already uses rather than requiring
// new references, and caches the originals so the space world can always be
// restored exactly as authored.
public static class WorldPainter
{
    const string BackdropName = "starsBackground0";
    const string LeftWallName = "leftPipe";
    const string RightWallName = "rightPipe";

    static bool cached;
    static Texture cachedBackdrop, cachedLeft, cachedRight;
    static Color cachedBackdropTint = Color.white, cachedLeftTint = Color.white, cachedRightTint = Color.white;

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
        Paint(BackdropName, Resources.Load<Texture2D>(root + "backdrop"), theme.tint, cachedBackdrop);
        Paint(LeftWallName, Resources.Load<Texture2D>(root + "wallLeft"), theme.tint, cachedLeft);
        Paint(RightWallName, Resources.Load<Texture2D>(root + "wallRight"), theme.tint, cachedRight);
    }

    static void CacheOriginals()
    {
        if (cached) return;
        cached = true;
        cachedBackdrop = MaterialOf(BackdropName) != null ? MaterialOf(BackdropName).mainTexture : null;
        cachedLeft = MaterialOf(LeftWallName) != null ? MaterialOf(LeftWallName).mainTexture : null;
        cachedRight = MaterialOf(RightWallName) != null ? MaterialOf(RightWallName).mainTexture : null;

        var m = MaterialOf(BackdropName); if (m != null && m.HasProperty("_Color")) cachedBackdropTint = m.color;
        m = MaterialOf(LeftWallName);     if (m != null && m.HasProperty("_Color")) cachedLeftTint = m.color;
        m = MaterialOf(RightWallName);    if (m != null && m.HasProperty("_Color")) cachedRightTint = m.color;
    }

    static void Restore()
    {
        Paint(BackdropName, null, cachedBackdropTint, cachedBackdrop);
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

        mat.mainTexture = tex != null ? tex : fallback;
        if (mat.HasProperty("_Color")) mat.color = tint;
    }
}
