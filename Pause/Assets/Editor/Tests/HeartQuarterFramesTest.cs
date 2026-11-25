using UnityEngine;

// Pixel invariants of the quarter-heart art: no repaint, missing/duplicated sections or shifting pivots.
public static class HeartQuarterFramesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HEARTQ] PASS  " : "[HEARTQ] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        var heart = Resources.Load<Sprite>(ShipLivesIndicator.HeartSpritePath);
        Check("the existing heart sprite is installed", heart != null);
        if (heart == null) return fails;
        int width, height;
        var original = HeartQuarterFrames.ReadFullCell(heart, out width, out height);
        Check("source readback restores the authored cell rather than the trimmed atlas rectangle",
              width == Mathf.RoundToInt(heart.rect.width) && height == Mathf.RoundToInt(heart.rect.height));
        CheckSourcePadding(heart, original, width, height);
        var art = HeartQuarterFrames.For(heart);
        Check("five states and four eight-frame quarter breaks are available", art != null && art.States.Length == 5 && art.BreakFrames.Length == 4 && art.LossFrames.Length == 4);
        if (art == null || original == null) return fails + 1;
        Check("the healthy state preserves the exact native sprite and its tight outline geometry", ReferenceEquals(heart, art.States[4]));
        Check("the shared atlas is point filtered with no mipmaps", art.Atlas.filterMode == FilterMode.Point && art.Atlas.mipmapCount == 1);
        bool exact = true, partition = true, pivots = true, partsPresent = true, endpoints = true, remainingFixed = true, moving = true;
        var parts = new Color32[4][];
        for (int q = 0; q < 4; q++)
        {
            parts[q] = Pixels(art.Parts[q]);
            pivots &= SameGeometry(heart, art.Parts[q]);
            int visible = 0; foreach (var c in parts[q]) if (c.a > 0) visible++;
            partsPresent &= visible > 0;
        }
        for (int remain = 0; remain <= 4; remain++)
        {
            var state = Pixels(art.States[remain]);
            pivots &= SameGeometry(heart, art.States[remain]);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x, q = HeartQuarterFrames.PartAt(x, y, width, height);
                    exact &= Same(state[i], q >= 4 - remain ? original[i] : new Color32());
                    if (remain != 4 || original[i].a == 0) continue;
                    int owners = 0;
                    for (int k = 0; k < 4; k++) if (parts[k][i].a > 0) { owners++; partition &= Same(parts[k][i], original[i]); }
                    partition &= owners == 1;
                }
        }
        for (int loss = 0; loss < 4; loss++)
        {
            endpoints &= Equal(Pixels(art.BreakFrames[loss][0]), Pixels(art.States[4 - loss]));
            endpoints &= Equal(Pixels(art.BreakFrames[loss][7]), Pixels(art.States[3 - loss]));
            endpoints &= Equal(Pixels(art.LossFrames[loss][0]), parts[loss]);
            foreach (var c in Pixels(art.LossFrames[loss][7])) endpoints &= c.a == 0;
            var stationary = Pixels(art.States[3 - loss]);
            for (int frame = 0; frame < HeartQuarterFrames.Frames; frame++)
            {
                pivots &= SameGeometry(heart, art.BreakFrames[loss][frame]) && SameGeometry(heart, art.LossFrames[loss][frame]);
                var combined = Pixels(art.BreakFrames[loss][frame]);
                for (int i = 0; i < stationary.Length; i++) if (stationary[i].a > 0) remainingFixed &= Same(combined[i], stationary[i]);
            }
            float before = CentreX(parts[loss], width), after = CentreX(Pixels(art.LossFrames[loss][2]), width);
            moving &= loss < 2 ? after > before + 1f : after < before - 1f;
        }
        Check("all remaining-heart states preserve the original pixels exactly", exact);
        Check("the four quarters partition the visible heart exactly once", partition && partsPresent);
        Check("every frame keeps the original pixel cell, pivot and scale with a full-cell mesh", pivots);
        Check("each break starts at the old state and finishes at the next quarter state", endpoints);
        Check("the remaining heart never moves or changes pixels during a quarter break", remainingFixed);
        Check("each detached quarter moves outward before disappearing", moving);
        int builds = HeartQuarterFrames.BuildCount;
        bool reused = true;
        for (int i = 0; i < 100; i++) reused &= ReferenceEquals(art, HeartQuarterFrames.For(heart));
        Check("subsequent lookups reuse the cached atlas and sprite frames", reused && HeartQuarterFrames.BuildCount == builds);
        Check("the frame clock clamps to the first/last frame", HeartQuarterFrames.FrameAt(-1f) == 0 && HeartQuarterFrames.FrameAt(100f) == 7);
        Debug.Log("[HEARTQ] failures: " + fails);
        return fails;
    }
    static void CheckSourcePadding(Sprite heart, Color32[] source, int width, int height)
    {
        int tw, th;
        var trimmed = ShieldContour.ReadPixels(heart, out tw, out th);
        bool valid = source != null && trimmed != null;
        if (!heart.packed || heart.packingRotation == SpritePackingRotation.None)
        {
            int ox = Mathf.RoundToInt(heart.textureRectOffset.x), oy = Mathf.RoundToInt(heart.textureRectOffset.y);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    bool inside = x >= ox && x < ox + tw && y >= oy && y < oy + th;
                    valid &= Same(source[y * width + x], inside ? trimmed[(y - oy) * tw + x - ox] : new Color32());
                }
        }
        Check("trimmed source pixels retain exact colors at their authored offset and outside padding stays empty", valid);
    }

    static bool SameGeometry(Sprite a, Sprite b)
    {
        if (ReferenceEquals(a, b)) return true; // the healthy state deliberately IS the native sprite
        // The imported heart uses a Tight mesh around its painted silhouette. Animation cells
        // deliberately use FullRect so a detached piece remains drawable beyond that silhouette.
        // Compare the shared pixel coordinate system, then require the FULL cell's exact bounds;
        // the original tight mesh's bounds are not the size of its 64x64 pixel cell.
        Vector2 size = a.rect.size / a.pixelsPerUnit;
        Vector2 centre = (a.rect.size * .5f - a.pivot) / a.pixelsPerUnit;
        bool matches = b != null && a.pixelsPerUnit == b.pixelsPerUnit && a.rect.size == b.rect.size && a.pivot == b.pivot &&
               ((Vector2)b.bounds.size - size).sqrMagnitude < 1e-8f &&
               ((Vector2)b.bounds.center - centre).sqrMagnitude < 1e-8f;
        if (!matches)
            Debug.LogWarning("[HEARTQ] Geometry mismatch " + (b != null ? b.name : "null") +
                " native rect=" + a.rect.ToString("F6") + " pivot=" + a.pivot.ToString("F6") + " PPU=" + a.pixelsPerUnit +
                " expected size=" + size.ToString("F6") + " centre=" + centre.ToString("F6") +
                (b != null ? " actual rect=" + b.rect.ToString("F6") + " pivot=" + b.pivot.ToString("F6") +
                " PPU=" + b.pixelsPerUnit + " size=" + b.bounds.size.ToString("F6") + " centre=" + b.bounds.center.ToString("F6") : ""));
        return matches;
    }
    static bool Same(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }
    static bool Equal(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!Same(a[i], b[i])) return false;
        return true;
    }
    static Color32[] Pixels(Sprite sprite)
    {
        int width, height;
        return HeartQuarterFrames.ReadFullCell(sprite, out width, out height);
    }
    static float CentreX(Color32[] px, int width)
    {
        float sum = 0f, alpha = 0f;
        for (int i = 0; i < px.Length; i++) { sum += (i % width) * px[i].a; alpha += px[i].a; }
        return alpha > 0f ? sum / alpha : 0f;
    }
}
