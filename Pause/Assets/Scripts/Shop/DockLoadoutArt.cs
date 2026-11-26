using UnityEngine;

// Native pixel-art loadout marks, shared by the berth wall and the ship card.
public static class DockLoadoutArt
{
    static Sprite drone;
    public static Sprite Drone
    {
        get
        {
            if (drone != null) return drone;
            const int size = 16;
            var pixels = new Color32[size * size];
            Color ink = AkiraPalette.Ink, metal = AkiraPalette.GunHi, cyan = AkiraPalette.Cyan, light = AkiraPalette.Bone;
            for (int y = 2; y <= 13; y++)
            for (int x = 2; x <= 13; x++)
            {
                bool core = x >= 6 && x <= 9 && y >= 3 && y <= 12;
                bool wing = y >= 5 && y <= 9 && (x >= 2 && x <= 5 || x >= 10 && x <= 13);
                bool tip = x >= 7 && x <= 8 && y >= 12;
                if (!core && !wing && !tip) continue;
                bool edge = core ? x == 6 || x == 9 || y == 3 : x == 2 || x == 13 || y == 5;
                pixels[y * size + x] = tip ? light : edge ? ink : y >= 8 ? cyan : metal;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DockDroneGlyph", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            drone = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, 16f, 0, SpriteMeshType.FullRect);
            drone.hideFlags = HideFlags.HideAndDontSave; return drone;
        }
    }
    public static Sprite Heart => Resources.Load<Sprite>(ShipLivesIndicator.HeartSpritePath);
}
