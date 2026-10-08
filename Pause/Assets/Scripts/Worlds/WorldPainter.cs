using UnityEngine;

// Swaps the scene's side-wall textures to match a world.
//
// Finds the objects by the names the scene already uses rather than requiring
// new references, and caches the originals so the space world can always be
// restored with the shared Frost rail thickness. The backdrop behind the walls is no longer a
// texture swap: WorldBackdrop builds an animated parallax set per world.
public static class WorldPainter
{
    const string LeftWallName = "leftPipe";
    const string RightWallName = "rightPipe";

    static bool cached;
    static Texture cachedLeft, cachedRight;
    static Color cachedLeftTint = Color.white, cachedRightTint = Color.white;
    static Shader cachedLeftShader, cachedRightShader;
    static float cachedLeftWidth = 1f, cachedRightWidth = 1f;
    static float cachedLeftX, cachedRightX;
    // The world whose rail layout is on the walls (null before the first Apply).
    static string laidOutWorld;
    // RailInset.Shift the walls were last laid out with.
    static float appliedInset;

    // ---- dark inner edge (WorldRailRepeat's _Edge* properties) ----
    // The lane-facing band of every reinforced rail is shaded toward
    // near-black in stepped bands, and its cable gaps plus a thin strip past
    // the silhouette fill with a near-black shadow: the rails read as dark,
    // recessed walls with a dark transition into the starfield (the approved
    // 900x1600 reference). The outer edge (against the screen border) is
    // never touched, and saturated neon lamps keep their light (lampKeep).
    // Tune here; per world in EdgeFor. Costs nothing per frame: a handful of
    // ALU ops in the rail's own fragment shader, no extra draw.
    public struct RailEdge
    {
        public float dark;         // shade at the very inner edge (0..1)
        public float width;        // the band, as a share of the rail's visible width
        public float steps;        // falloff steps across the band (pixel-art bands)
        public float shadow;       // alpha of the near-black fill in the gaps / past the edge
        public float shadowWidth;  // that fill past the silhouette, share of the rail width
        public float lampKeep;     // share of the shade the neon lamps ignore
    }

    public static readonly RailEdge DefaultEdge = new RailEdge
    {
        dark = .55f, width = .16f, steps = 4f, shadow = .6f, shadowWidth = .05f, lampKeep = .85f
    };

    public static RailEdge EdgeFor(string world)
    {
        var e = DefaultEdge;
        switch (world)
        {
            // Frost's pale steel is the brightest metal: a touch more shade
            // for the same read.
            case "Frost": e.dark = .6f; break;
        }
        return e;
    }

    // ---- rail brightness ----
    // The rail art's colour is multiplied by this (rgb only: hue, alpha and
    // the drawn silhouette, which BossRails and the rail mines clamp to, stay
    // as they are). Frost's pale steel and cyan lamps out-shone the lane, so
    // its rails are dimmed to recede behind the playfield; every other world
    // draws its rail as painted.
    public static float FrostRailBrightness = .6f;

    public static float RailBrightness(string world)
    {
        return world == "Frost" ? FrostRailBrightness : 1f;
    }

    // The colour the rail material is tinted with for `theme`.
    public static Color RailTint(WorldTheme theme)
    {
        if (theme == null) return Color.white;
        Color c = theme.tint;
        float k = RailBrightness(theme.displayName);
        return new Color(c.r * k, c.g * k, c.b * k, c.a);
    }

    public static void Apply(WorldTheme theme)
    {
        if (theme == null) return;

        CacheOriginals();

        // Every world flies between reinforced rails. A world without one
        // (none today; the old flat wallLeft / wallRight fallback is gone)
        // keeps the scene's own walls.
        string railName = RailTextureName(theme.displayName);
        if (railName == null)
        {
            Restore();
            return;
        }

        string root = "Worlds/" + (string.IsNullOrEmpty(theme.resourceFolder) ? theme.displayName : theme.resourceFolder) + "/";
        // Reinforced rails share Frost's visible width and are mirrored so
        // their lamp/pipe edges face the playfield on both sides.
        var rail = Resources.Load<Texture2D>(root + railName);
        // Preserve neon brightness and remove residual exterior mattes.
        SetRailShader(Resources.Load<Shader>("WorldRailRepeat"));
        foreach (var wall in new[] { LeftWallName, RightWallName })
        {
            var mat = MaterialOf(wall);
            if (mat == null || !mat.HasProperty("_Overlap")) continue;
            mat.SetFloat("_Overlap", 0.03f);
            mat.SetFloat("_BlackCutout", 1f);
            ApplyEdge(mat, theme.displayName);
        }
        Color railTint = RailTint(theme);
        Paint(LeftWallName, rail, railTint, cachedLeft, false);
        Paint(RightWallName, rail, railTint, cachedRight, true);
        SetRailLayout(theme.displayName);
        // the rails just changed: the edge everything bounces off, crashes
        // into and breaks on (BossRails.InnerEdge; elites, shots) is the
        // drawn one from now on, not only after the first boss intro
        BossRails.Measure();
    }

    public static string RailTextureName(string world)
    {
        switch (world)
        {
            case "Verdant": return "rail_forest_wide_v1";
            case "Frost": return "rail_frost_wide_v1";
            case "Ember": return "rail_ember_wide_v1";
            case "Space": return "rail_space_wide_v1";
            default: return null;
        }
    }

    // Sets `world`'s dark-edge treatment on a WorldRailRepeat material.
    public static void ApplyEdge(Material mat, string world)
    {
        if (mat == null || !mat.HasProperty("_EdgeDark")) return;
        RailBounds(world, out float min, out float max);
        var e = EdgeFor(world);
        mat.SetFloat("_EdgeInnerU", max);
        mat.SetFloat("_EdgeOuterU", min);
        mat.SetFloat("_EdgeDark", e.dark);
        mat.SetFloat("_EdgeWidth", e.width);
        mat.SetFloat("_EdgeSteps", e.steps);
        mat.SetFloat("_EdgeShadow", e.shadow);
        mat.SetFloat("_EdgeShadowWidth", e.shadowWidth);
        mat.SetFloat("_EdgeLampKeep", e.lampKeep);
    }

    public static float RailWidthFactor(string world)
    {
        // Measure visible silhouette boundaries, excluding transparent/black
        // padding. Frost's approved 1.25x presentation is the common standard.
        RailBounds(world, out float min, out float max);
        return 1.25f * (443f / 725f) / (max - min);
    }

    static void RailBounds(string world, out float min, out float max)
    {
        switch (world)
        {
            case "Frost": min = 140f / 725f; max = 583f / 725f; break;
            case "Verdant": min = 139f / 725f; max = 580f / 725f; break;
            case "Ember": min = 157f / 725f; max = 497f / 725f; break;
            default: min = 159f / 725f; max = 499f / 725f; break;
        }
    }

    // Where the rail a wall is showing really is: the world x (as a distance
    // from the centre line) of the inner, gameplay-facing edge of its art
    // and of its outer edge. The wall quad itself is no guide any more: it is
    // padded with the texture's transparent canvas, so its renderer bounds
    // reach well into the lane (to about 2.26 on Frost and Verdant, 1.87 on
    // Space and Ember) while the drawn rail starts near 2.61 in every world.
    // False if the wall is not showing one of the reinforced rails.
    public static bool VisibleRailEdges(GameObject wall, out float inner, out float outer)
    {
        inner = outer = 0f;
        var r = wall != null ? wall.GetComponent<Renderer>() : null;
        var mat = r != null ? r.sharedMaterial : null;
        var tex = mat != null ? mat.mainTexture : null;
        if (tex == null) return false;
        string world = null;
        foreach (string w in new[] { "Space", "Frost", "Verdant", "Ember" })
            if (RailTextureName(w) == tex.name) world = w;
        if (world == null) return false;
        RailBounds(world, out float min, out float max);
        // Both walls show the art's `max` side toward the lane: the left one
        // as drawn, the right one mirrored.
        float width = r.bounds.size.x;
        float quadOuter = Mathf.Abs(r.bounds.center.x) + width * 0.5f;
        inner = quadOuter - max * width;
        outer = quadOuter - min * width;
        return true;
    }

    static void SetRailLayout(string world)
    {
        float factor = RailWidthFactor(world);
        SetRailWidth(factor);
        RailBounds(world, out float min, out float max);
        // Match both visible edges to Frost as well as its thickness; this
        // prevents asymmetric canvas padding from shifting the flight lane.
        float frostCentre = (140f + 583f) / (2f * 725f) - 0.5f;
        float centre = (min + max) * 0.5f - 0.5f;
        float shift = 1.25f * frostCentre - factor * centre;
        // Out toward the screen edges where the screen has room (RailInset).
        float inset = RailInset.Shift;
        laidOutWorld = world;
        appliedInset = inset;
        var left = GameObject.Find(LeftWallName);
        var right = GameObject.Find(RightWallName);
        if (left != null)
        {
            var p = left.transform.localPosition;
            p.x = cachedLeftX + cachedLeftWidth * shift - inset;
            left.transform.localPosition = p;
        }
        if (right != null)
        {
            var p = right.transform.localPosition;
            p.x = cachedRightX - cachedRightWidth * shift + inset;
            right.transform.localPosition = p;
        }
        if (left != null) RailFit.RefreshTextureTiling(left);
        if (right != null) RailFit.RefreshTextureTiling(right);
    }

    // RailFit, when the screen changes (a fold, a rotation): lay the walls
    // out again for the screen's RailInset and re-measure the edge
    // everything bounces off. Nothing to do before the first Apply or while
    // the inset is unchanged.
    public static void RefreshInset()
    {
        if (!cached || laidOutWorld == null) return;
        if (GameObject.Find(LeftWallName) == null && GameObject.Find(RightWallName) == null) return;
        if (Mathf.Approximately(appliedInset, RailInset.Shift)) return;
        SetRailLayout(laidOutWorld);
        BossRails.Measure();
    }

    // The RailInset.Shift the walls are laid out with (0 before any Apply).
    public static float AppliedInset { get { return appliedInset; } }

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
        if (left != null) cachedLeftX = left.transform.localPosition.x;
        if (right != null) cachedRightX = right.transform.localPosition.x;

        var m = MaterialOf(LeftWallName); if (m != null && m.HasProperty("_Color")) cachedLeftTint = m.color;
        m = MaterialOf(RightWallName);    if (m != null && m.HasProperty("_Color")) cachedRightTint = m.color;
        cachedLeftShader = MaterialOf(LeftWallName)?.shader;
        cachedRightShader = MaterialOf(RightWallName)?.shader;
    }

    static void Restore()
    {
        SetRailShader(null);
        Paint(LeftWallName, null, cachedLeftTint, cachedLeft, false);
        Paint(RightWallName, null, cachedRightTint, cachedRight, false);
        SetRailLayout("Space");
    }

    static void SetRailShader(Shader shader)
    {
        var left = MaterialOf(LeftWallName);
        var right = MaterialOf(RightWallName);
        if (left != null && (shader != null || cachedLeftShader != null)) left.shader = shader != null ? shader : cachedLeftShader;
        if (right != null && (shader != null || cachedRightShader != null)) right.shader = shader != null ? shader : cachedRightShader;
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
        // Also apply in editor captures, where WorldBackdrop's play-mode
        // queue fix never runs. Backdrop layers must render below the neon.
        mat.renderQueue = 3000;
        mat.mainTextureScale = new Vector2(mirrorX ? -1f : 1f, 1f);
        if (mat.HasProperty("_Color")) mat.color = tint;
    }
}
