using UnityEngine;

public static class GunBoltArt
{
    static readonly Sprite[] bolts = new Sprite[4];
    public static int Tier(int drone)
    {
        int levels = 0;
        for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) levels += ShotAtoms.Level(drone, (ShotAtom)a);
        return levels >= 25 ? 3 : levels >= 12 ? 2 : levels >= 4 ? 1 : 0;
    }
    public static Sprite For(int tier)
    {
        tier = Mathf.Clamp(tier, 0, 3);
        if (tier == 0) return FloatieBullet.BulletSprite();
        if (bolts[tier] != null) return bolts[tier];
        int width = 5 + tier * 2, height = 28 + tier * 2, middle = width / 2;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int dx = Mathf.Abs(x - middle);
                if (y >= height / 2 && y < height - 1)
                {
                    int breadth = tier == 1 ? 1 : Mathf.Min(tier + 1, (height - 1 - y) / 2 + 1);
                    if (dx <= breadth) pixels[y * width + x] = dx == 0 || (tier == 3 && dx == 2) ? Color.white : new Color(.38f, .45f, .5f, 1f);
                    if (dx == breadth + 1) pixels[y * width + x] = new Color(.03f, .045f, .055f, .9f);
                }
                else if (y < height / 2 && (dx == 0 || (tier >= 2 && dx == 2)))
                    pixels[y * width + x] = new Color(.8f, .85f, 1f, .65f * y / (height / 2f));
            }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "DroneBoltTier" + tier; texture.filterMode = FilterMode.Point; texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixels32(pixels); texture.Apply();
        bolts[tier] = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .75f), 80f, 0, SpriteMeshType.FullRect);
        bolts[tier].hideFlags = HideFlags.HideAndDontSave;
        return bolts[tier];
    }
    public static void Prewarm() { for (int i = 0; i < 4; i++) For(i); }
}
