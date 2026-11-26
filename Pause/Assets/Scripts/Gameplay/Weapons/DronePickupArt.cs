using UnityEngine;

// Small, point-filtered firing-mode badges and pickup icons, drawn once in the game's pixel vocabulary.
public static class DronePickupArt
{
    static readonly Sprite[] icons = new Sprite[5 * 14];
    public static Sprite For(DroneMode mode) => Icon((int)mode);
    public static Sprite Reward => Icon(4);
    public static Sprite Frame(int kind, int frame) => Icon(kind, frame % 14);
    static Sprite Icon(int kind, int frame = 0)
    {
        if (icons[kind * 14 + frame] != null) return icons[kind * 14 + frame];
        const int size = 32;
        var pixels = new Color32[size * size];
        Color edge = kind == 4 ? new Color(1f, .82f, .3f) : DroneSquad.Colour((DroneMode)kind);
        float pulse = 1f - .25f * Mathf.Pow(Mathf.Sin(frame * Mathf.PI / 14f), 2f);
        edge = new Color(edge.r * pulse, edge.g * pulse, edge.b * pulse, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = x - 16, dy = y - 16, d = dx * dx + dy * dy;
                Color c = Color.clear;
                if (d <= 14 * 14) c = new Color(.025f, .04f, .075f, .95f);
                if (d >= 12 * 12 && d <= 14 * 14) c = edge;
                bool mark = false;
                if (kind == 0) mark = (Mathf.Abs(dx) == 4 && Mathf.Abs(dy) <= 4) || (Mathf.Abs(dy) == 4 && Mathf.Abs(dx) <= 4);
                if (kind == 1) mark = (Mathf.Abs(dx) <= 1 && dy >= -6 && dy <= 5) || (dy >= 2 && dy <= 7 && Mathf.Abs(dx) == 7 - dy);
                if (kind == 2) mark = (Mathf.Abs(dy) <= 1 && Mathf.Abs(dx) <= 7) || (Mathf.Abs(dx) >= 4 && Mathf.Abs(dx) <= 8 && Mathf.Abs(dy) == 8 - Mathf.Abs(dx));
                if (kind == 3) mark = (d >= 5 * 5 && d <= 7 * 7) || (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 9) || (Mathf.Abs(dy) <= 1 && Mathf.Abs(dx) <= 9);
                if (kind == 4) mark = (Mathf.Abs(dx) <= 3 && dy >= -5 && dy <= 6) || (Mathf.Abs(dy + 1) <= 1 && Mathf.Abs(dx) <= 9) || (Mathf.Abs(dx) >= 7 && Mathf.Abs(dx) <= 9 && dy >= -4 && dy <= 5);
                if (mark) c = kind == 0 ? edge : Color.Lerp(edge, Color.white, .55f);
                pixels[y * size + x] = c;
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "DroneMode" + kind + "_" + frame; texture.filterMode = FilterMode.Point;
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixels32(pixels); texture.Apply();
        icons[kind * 14 + frame] = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 64f, 0, SpriteMeshType.FullRect);
        icons[kind * 14 + frame].name = texture.name; icons[kind * 14 + frame].hideFlags = HideFlags.HideAndDontSave;
        return icons[kind * 14 + frame];
    }
    public static void Prewarm() { for (int i = 0; i < 5; i++) for (int frame = 0; frame < 14; frame++) Icon(i, frame); }
}
