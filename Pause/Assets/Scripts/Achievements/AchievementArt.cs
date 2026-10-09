using System.Collections.Generic;
using UnityEngine;

// The badge of an achievement: Resources/Achievements/<id> (128 px, painted by
// Codex) when it exists, otherwise a procedural placeholder medal -- a
// tier-coloured riveted ring around a dark glyph-free disc -- so the Codex
// tab never shows a hole while art is still landing. Locked badges are drawn
// by the UI with the same sprite, darkened.
public static class AchievementArt
{
    public const string Folder = "Achievements/";
    public const int Size = 128;

    static readonly Dictionary<string, Sprite> loaded = new Dictionary<string, Sprite>();
    static readonly HashSet<string> missing = new HashSet<string>();
    static readonly Sprite[] placeholders = new Sprite[4];

    // Whether painted art exists for the id.
    public static bool HasArt(string id)
    {
        return id != null && Resources.Load<Sprite>(Folder + id) != null;
    }

    public static Sprite For(AchievementDef def)
    {
        return def == null ? null : For(def.id, def.tier);
    }

    public static Sprite For(string id)
    {
        var def = AchievementCatalog.Find(id);
        return For(id, def != null ? def.tier : AchievementTier.Bronze);
    }

    static Sprite For(string id, AchievementTier tier)
    {
        Sprite s;
        if (loaded.TryGetValue(id, out s))
        {
            if (s != null) return s;
            if (missing.Contains(id)) return Placeholder(tier);   // looked once: no art yet, do not hit Resources again
        }
        s = Resources.Load<Sprite>(Folder + id);
        if (s != null) { loaded[id] = s; return s; }
        loaded[id] = null;
        missing.Add(id);
        return Placeholder(tier);
    }

    // Forget cached sprites (tests, after art changes).
    public static void Reload()
    {
        loaded.Clear();
        missing.Clear();
        for (int i = 0; i < placeholders.Length; i++) placeholders[i] = null;
    }

    public static bool IsPlaceholder(Sprite s)
    {
        if (s == null) return false;
        for (int i = 0; i < placeholders.Length; i++) if (placeholders[i] == s) return true;
        return false;
    }

    // The ring metal per tier: copper, steel, brass-gold, white-gold.
    public static Color Metal(AchievementTier tier)
    {
        switch (tier)
        {
            case AchievementTier.Bronze: return new Color(.80f, .45f, .22f);
            case AchievementTier.Silver: return new Color(.64f, .71f, .80f);
            case AchievementTier.Gold: return new Color(.96f, .72f, .24f);
            default: return new Color(.95f, .96f, .88f);
        }
    }

    public static Sprite Placeholder(AchievementTier tier)
    {
        int i = (int)tier;
        if (placeholders[i] != null) return placeholders[i];
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "AchievementPlaceholder_" + tier,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        tex.SetPixels32(Paint(tier));
        tex.Apply(false, false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100f);
        sprite.name = tex.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return placeholders[i] = sprite;
    }

    // Hard-edged pixel art: ink outline, shaded metal ring with rivets, dark disc with an inner glow line.
    static Color32[] Paint(AchievementTier tier)
    {
        var px = new Color32[Size * Size];
        Color metal = Metal(tier);
        Color metalDark = metal * .55f; metalDark.a = 1f;
        Color metalLight = Color.Lerp(metal, Color.white, .45f);
        Color ink = CodexPalette.Ink;
        Color disc = CodexPalette.Night1;
        Color glow = Color.Lerp(CodexPalette.Indigo1, metal, .35f);
        float c = (Size - 1) * .5f, R = Size * .5f - 1f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                Color col = Color.clear;
                if (r <= R)
                {
                    if (r > R - 3f) col = ink;                                  // outer outline
                    else if (r > R * .80f)                                       // the ring
                    {
                        // light from the upper left
                        float lit = Mathf.Clamp01((-dx + dy) / (2f * R) + .5f);
                        col = lit > .62f ? metalLight : lit < .30f ? metalDark : metal;
                        // rivets: eight, on the ring's middle
                        float ang = Mathf.Atan2(dy, dx);
                        float sector = Mathf.Repeat(ang, Mathf.PI * .25f);
                        float rm = R * .90f;
                        float ax = Mathf.Abs(r - rm), arc = Mathf.Abs(sector - Mathf.PI * .125f) * rm;
                        if (ax < 2.6f && arc < 2.6f) col = ax + arc < 3.2f ? metalDark : col;
                    }
                    else if (r > R * .77f) col = ink;                             // inner outline
                    else if (r > R * .70f) col = glow;                            // inner glow line
                    else if (r > R * .67f) col = ink;
                    else col = Color.Lerp(disc, CodexPalette.Indigo0, Mathf.Clamp01(1f - r / (R * .67f)) * .5f);
                }
                px[y * Size + x] = col;
            }
        }
        return px;
    }
}
