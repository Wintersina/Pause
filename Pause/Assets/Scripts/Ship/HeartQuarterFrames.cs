using System.Collections.Generic;
using UnityEngine;

// Exact-pixel quarter states and break-off animation from the existing heart sprite. Built once per
// source sprite, cached, and sampled without allocation. No repaint or rescaling is involved.
// Loss order is clockwise: top-right, bottom-right, bottom-left, top-left (Unity bottom-up pixels).
// Every sprite has the source heart's full cell size, pivot and pixels-per-unit, so the same renderer
// scale and transform work for full hearts, partial hearts, and the detached overlays.
public static class HeartQuarterFrames
{
    public const int Quarters = 4, Frames = 8;
    public const float FrameSeconds = .045f;
    public const float BreakSeconds = FrameSeconds * Frames;
    public const int AtlasColumns = 8, AtlasRows = 10;

    public sealed class Set
    {
        // Indexed by remaining quarters: 0 empty, 1 25%, 2 50%, 3 75%, 4 full.
        public readonly Sprite[] States = new Sprite[Quarters + 1];
        // Exact original quarter, in the original full-size cell, by loss order.
        public readonly Sprite[] Parts = new Sprite[Quarters];
        // Combined preview: fixed lower-health state plus the one detached quarter.
        public readonly Sprite[][] BreakFrames = new Sprite[Quarters][];
        // Detached quarter only. Overlay on States[currentRemaining] for live gameplay so several
        // hits in one frame never redraw an older, healthier state under the break animation.
        public readonly Sprite[][] LossFrames = new Sprite[Quarters][];
        public Texture2D Atlas { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public float PixelsPerUnit { get; internal set; }
        public Vector2 Pivot { get; internal set; }
    }

    static readonly Dictionary<Sprite, Set> cache = new Dictionary<Sprite, Set>();
    public static int BuildCount { get; private set; }

    public static int FrameAt(float seconds)
    {
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, seconds) / FrameSeconds), 0, Frames - 1);
    }

    // Original source pixel's quarter, in loss order. Four pieces partition the source without gaps.
    public static int PartAt(int x, int y, int width, int height)
    {
        bool right = x >= width / 2, top = y >= height / 2;
        return top ? (right ? 0 : 3) : (right ? 1 : 2);
    }

    public static Set For(Sprite heart)
    {
        if (heart == null) return null;
        Set found;
        if (cache.TryGetValue(heart, out found) && found != null && found.Atlas != null) return found;
        int width, height;
        var source = ReadFullCell(heart, out width, out height);
        if (source == null || width < 2 || height < 2 || source.Length < width * height) return null;
        var set = Build(heart, source, width, height);
        cache[heart] = set;
        BuildCount++;
        return set;
    }

    // ReadPixels returns textureRect, which Unity may trim while rect/pivot remain in the
    // authored cell. Restore that cell before partitioning so atlas packing cannot move the heart.
    // Sprite UVs provide the exact affine mapping for packed rotations/flips, including Any.
    public static Color32[] ReadFullCell(Sprite sprite, out int width, out int height)
    {
        width = sprite != null ? Mathf.RoundToInt(sprite.rect.width) : 0;
        height = sprite != null ? Mathf.RoundToInt(sprite.rect.height) : 0;
        if (sprite == null || width <= 0 || height <= 0) return null;
        int trimmedWidth, trimmedHeight;
        var trimmed = ShieldContour.ReadPixels(sprite, out trimmedWidth, out trimmedHeight);
        if (trimmed == null) return null;
        var cell = new Color32[width * height];
        Rect rect = sprite.textureRect;
        if (!sprite.packed || sprite.packingRotation == SpritePackingRotation.None)
        {
            Vector2 offset = sprite.textureRectOffset;
            int ox = Mathf.RoundToInt(offset.x), oy = Mathf.RoundToInt(offset.y);
            for (int y = 0; y < trimmedHeight; y++)
                for (int x = 0; x < trimmedWidth; x++)
                    if (x + ox >= 0 && x + ox < width && y + oy >= 0 && y + oy < height)
                        cell[(y + oy) * width + x + ox] = trimmed[y * trimmedWidth + x];
            return cell;
        }
        var vertices = sprite.vertices;
        var uv = sprite.uv;
        Vector2 origin = vertices[0] * sprite.pixelsPerUnit + sprite.pivot;
        Vector2 u0 = Vector2.Scale(uv[0], new Vector2(sprite.texture.width, sprite.texture.height));
        for (int j = 1; j < vertices.Length; j++)
            for (int k = j + 1; k < vertices.Length; k++)
            {
                Vector2 a = (vertices[j] - vertices[0]) * sprite.pixelsPerUnit;
                Vector2 b = (vertices[k] - vertices[0]) * sprite.pixelsPerUnit;
                float det = a.x * b.y - a.y * b.x;
                if (Mathf.Abs(det) < .001f) continue;
                Vector2 ua = Vector2.Scale(uv[j] - uv[0], new Vector2(sprite.texture.width, sprite.texture.height));
                Vector2 ub = Vector2.Scale(uv[k] - uv[0], new Vector2(sprite.texture.width, sprite.texture.height));
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        Vector2 p = new Vector2(x + .5f, y + .5f) - origin;
                        float ca = (p.x * b.y - p.y * b.x) / det;
                        float cb = (a.x * p.y - a.y * p.x) / det;
                        Vector2 packed = u0 + ua * ca + ub * cb - rect.position;
                        int sx = Mathf.FloorToInt(packed.x), sy = Mathf.FloorToInt(packed.y);
                        if (sx >= 0 && sx < trimmedWidth && sy >= 0 && sy < trimmedHeight)
                            cell[y * width + x] = trimmed[sy * trimmedWidth + sx];
                    }
                return cell;
            }
        throw new System.InvalidOperationException("Packed heart has no usable sprite UV mapping: " + sprite.name);
    }

    static Set Build(Sprite heart, Color32[] source, int width, int height)
    {
        var set = new Set
        {
            Width = width, Height = height, PixelsPerUnit = heart.pixelsPerUnit,
            Pivot = new Vector2(heart.pivot.x / heart.rect.width, heart.pivot.y / heart.rect.height),
        };
        var atlasPixels = new Color32[width * AtlasColumns * height * AtlasRows];
        var states = new Color32[Quarters + 1][];
        var parts = new Color32[Quarters][];
        for (int remain = 0; remain <= Quarters; remain++)
        {
            var pixels = states[remain] = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (PartAt(x, y, width, height) >= Quarters - remain)
                        pixels[y * width + x] = source[y * width + x];
            Put(atlasPixels, width, height, remain, 0, pixels);
        }
        for (int part = 0; part < Quarters; part++)
        {
            var pixels = parts[part] = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (PartAt(x, y, width, height) == part)
                        pixels[y * width + x] = source[y * width + x];
            Put(atlasPixels, width, height, part, 1, pixels);
        }
        for (int loss = 0; loss < Quarters; loss++)
            for (int frame = 0; frame < Frames; frame++)
            {
                var detached = Detached(parts[loss], width, height, loss, frame);
                Put(atlasPixels, width, height, frame, 6 + loss, detached);
                var combined = new Color32[width * height];
                var remaining = states[Quarters - loss - 1];
                for (int i = 0; i < combined.Length; i++)
                {
                    // The detached fragment passes behind the remaining piece. Its stationary pixels
                    // are preserved byte-for-byte, including the source's fine antialiased edge.
                    combined[i] = remaining[i].a > 0 ? remaining[i] : detached[i];
                }
                if (frame == 0) System.Array.Copy(states[Quarters - loss], combined, combined.Length);
                // Imported transparent texels may retain RGB (alpha bleed). Preserve the exact
                // lower state at the endpoint, including those invisible source colors.
                if (frame == Frames - 1) System.Array.Copy(remaining, combined, combined.Length);
                Put(atlasPixels, width, height, frame, 2 + loss, combined);
            }
        var texture = new Texture2D(width * AtlasColumns, height * AtlasRows, TextureFormat.RGBA32, false);
        texture.name = heart.name + "QuarterFrames";
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixels32(atlasPixels);
        texture.Apply(false, false); // tiny, shared, readable for preview export and pixel-invariant tests
        set.Atlas = texture;
        for (int remain = 0; remain <= Quarters; remain++)
            // A healthy heart retains its authored tight mesh, filtering and cached outline.
            set.States[remain] = remain == Quarters ? heart : Cut(set, remain, 0, heart.name + "Quarters" + remain);
        for (int loss = 0; loss < Quarters; loss++)
        {
            set.Parts[loss] = Cut(set, loss, 1, heart.name + "QuarterPart" + loss);
            set.BreakFrames[loss] = new Sprite[Frames];
            set.LossFrames[loss] = new Sprite[Frames];
            for (int frame = 0; frame < Frames; frame++)
            {
                set.BreakFrames[loss][frame] = Cut(set, frame, 2 + loss, heart.name + "QuarterBreak" + loss + "_" + frame);
                set.LossFrames[loss][frame] = Cut(set, frame, 6 + loss, heart.name + "QuarterLoss" + loss + "_" + frame);
            }
        }
        return set;
    }

    static Color32[] Detached(Color32[] part, int width, int height, int index, int frame)
    {
        var result = new Color32[width * height];
        if (frame >= Frames - 1) return result;
        if (frame == 0) { System.Array.Copy(part, result, part.Length); return result; }
        float t = frame / (float)(Frames - 1);
        float side = index < 2 ? 1f : -1f;
        int dx = Mathf.RoundToInt(side * width * .14f * t);
        int dy = Mathf.RoundToInt(height * (.07f * Mathf.Sin(t * Mathf.PI) - .20f * t * t));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, 1f, t));
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int tx = x + dx, ty = y + dy;
                if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;
                var c = part[y * width + x];
                c.a = (byte)Mathf.RoundToInt(c.a * fade);
                result[ty * width + tx] = c;
            }
        return result;
    }

    static void Put(Color32[] atlas, int width, int height, int column, int row, Color32[] pixels)
    {
        int stride = width * AtlasColumns, firstY = (AtlasRows - 1 - row) * height;
        for (int y = 0; y < height; y++)
            System.Array.Copy(pixels, y * width, atlas, (firstY + y) * stride + column * width, width);
    }

    static Sprite Cut(Set set, int column, int row, string name)
    {
        var sprite = Sprite.Create(set.Atlas,
            new Rect(column * set.Width, (AtlasRows - 1 - row) * set.Height, set.Width, set.Height),
            set.Pivot, set.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        // Unity may derive bounds from the cell's alpha even for an empty FullRect cell.
        // Explicit geometry fixes the shared coordinate system for every partial/animation state.
        float left = -set.Pivot.x * set.Width / set.PixelsPerUnit;
        float bottom = -set.Pivot.y * set.Height / set.PixelsPerUnit;
        float right = left + set.Width / set.PixelsPerUnit;
        float top = bottom + set.Height / set.PixelsPerUnit;
        sprite.OverrideGeometry(new[] { new Vector2(left, bottom), new Vector2(left, top),
            new Vector2(right, top), new Vector2(right, bottom) }, new ushort[] { 0, 1, 2, 0, 2, 3 });
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
