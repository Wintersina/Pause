using UnityEngine;

// The in-run read-outs' per-world skins: the score board and the FLIGHT
// COMPLETE panel wear their world's frame, and the home / replay quick actions
// are thin hollow glyphs floating over the backdrop (no panel behind them);
// they swap when the run enters the next world.
//
// Space's death-panel glyphs are the original QuickActions ones; every world has
// a folder, Resources/HudSkins/<WorldTheme.displayName>/, holding:
//   hud_board        702x310 px, the score board's frame: sliced, border 50 px
//                    left/right and 60 px top/bottom, 200 ppu; the dark backing
//                    fill is 70% opaque (Tools/hud_art_pass.py); drawn 351 x 131
//                    plus the per-world BoardOuts growth, outside the layout rect
//   (HudSkins/hud_home and hud_replay, one shared white pair for every world:
//   256 px outline-only glyphs for the quick actions, drawn by Tools/hud_icons.py
//   and tinted per world with IconTint / IconHalo)
//   dp_panel         600x900 px, the FLIGHT COMPLETE panel, border 100 px
//   qa_home_glyph / qa_replay_glyph   the solid glyphs the death panel's buttons use
// The growth along the top and bottom edges is drawn at its real size (not
// stretched), so each row's edge keeps an irregular rhythm.
public static class HudSkin
{
    // Space's frames are skinned too (HudSkins/Space); the originals are the
    // fallback, and Space's glyphs stay the original QuickActions ones.
    public const string SpaceFrame = "Hud/hud_panel";
    public const string SpaceDeathPanel = "DeathPanel/dp_panel";
    public const string SpaceHomeGlyph = PauseQuickActions.HomeIconPath + "_glyph";
    public const string SpaceReplayGlyph = PauseQuickActions.ReplayIconPath + "_glyph";

    public static int WorldCount { get { return WorldManager.LiveWorldCount; } }

    // The world the run is on (the bookkeeping world, so the skin holds still
    // through the space leg between two planets and changes on arrival).
    public static int CurrentWorld { get { return Mathf.Clamp(WorldManager.CurrentIndex, 0, WorldCount - 1); } }

    // null for Space: it keeps the original art.
    public static string Folder(int world)
    {
        if (world <= 0 || world >= WorldCount) return null;
        return "HudSkins/" + WorldManager.Worlds[world].displayName + "/";
    }

    // Frames: every world, Space included, has HudSkins/<World>/{hud_frame,dp_panel}.
    static string Dir(int world) { return "HudSkins/" + WorldManager.Worlds[Mathf.Clamp(world, 0, WorldCount - 1)].displayName + "/"; }
    // The frames' growth past the layout rect, canvas units: { left, bottom,
    // right, top }, order = world index (Space, Frost, Verdant, Ember, Tide,
    // Storm, Desert). TUNED FROM RENDERS (HudVetCapture + the gap measurement): with the worst-case text (1,234,567 / SPEED 999 / PAUSES 99)
    // the nearest icicle, crystal or rail is at least ~5 px (at 1080 wide)
    // from the text on the left / top / bottom, top and bottom equal so the
    // three rows sit centred in the frame's dark inside. Re-render after changing frame art.
    static readonly float[][] BoardOuts =
    {
        new[]{0f, 16.5f, 10f, 10f},       // Space
        new[]{17f, 37f, 14f, 18.5f},    // Frost
        new[]{9f, 31f, 14f, 24f},       // Verdant
        new[]{9.5f, 26.5f, 13.5f, 23f},   // Ember
        new[]{2f, 17f, 7f, 8f},           // Tide
        new[]{7.5f, 35.5f, 14f, 25f},     // Storm
        new[]{17f, 26f, 14f, 36f},       // Desert
    };
    // x = left, y = bottom, z = right, w = top.
    public static Vector4 BoardOut(int world) { return OutFor(BoardOuts, world); }

    static Vector4 OutFor(float[][] table, int world)
    {
        var m = table[Mathf.Clamp(world, 0, table.Length - 1)];
        return new Vector4(m[0], m[1], m[2], m[3]);
    }

    // Extra room the text needs on the left where the art's decoration (Desert's
    // sandworm head) sits inside the frame's left edge, canvas units.
    static readonly float[] TextExtraLeft = { 3f, 12f, 6f, 4f, 8f, 4f, 24f };   // tuned from renders, like the outs
    public static float BoardTextExtraLeft(int world) { return TextExtraLeft[Mathf.Clamp(world, 0, TextExtraLeft.Length - 1)]; }

    public static string FramePath(int world) { return Dir(world) + "hud_board"; }
    // The in-run quick-action glyphs are shared by every world (white; tinted by IconTint).
    public const string SharedHomePath = "HudSkins/hud_home";
    public const string SharedReplayPath = "HudSkins/hud_replay";
    public static string HudHomePath(int world) { return SharedHomePath; }
    public static string HudReplayPath(int world) { return SharedReplayPath; }

    // Per-world glyph colour, picked from each backdrop's measured brightness
    // (Tools/hud_icons.py; HudVetCapture renders): bright backdrops (Frost clouds,
    // Verdant foliage) get a dark saturated tint, the mid/dark ones a light tint.
    // Order = world index (Space, Frost, Verdant, Ember, Tide, Storm, Desert).
    static readonly Color[] IconTints =
    {
        new Color32(190, 240, 255, 255),   // Space
        new Color32(12, 34, 92, 255),      // Frost
        new Color32(8, 54, 22, 255),       // Verdant
        new Color32(255, 232, 196, 255),   // Ember
        new Color32(255, 255, 255, 255),   // Tide
        new Color32(228, 232, 255, 255),   // Storm
        new Color32(255, 238, 176, 255),   // Desert
    };
    // A soft under-halo (UI Outline) where the tinted line alone can't hold 3:1 against the backdrop's brightest
    // or darkest patches; alpha 0 = none. The halo is the opposite brightness of the tint.
    static readonly Color[] IconHalos =
    {
        new Color32(0, 0, 0, 0),           // Space
        new Color32(255, 255, 255, 0),     // Frost
        new Color32(255, 255, 255, 0),     // Verdant
        new Color32(0, 0, 0, 0),           // Ember
        new Color32(6, 28, 30, 150),       // Tide
        new Color32(0, 0, 0, 0),           // Storm
        new Color32(0, 0, 0, 0),           // Desert
    };
    // The holo glass behind each glyph: a backing disc that is NOT see-through (PlateAlpha) so the
    // backdrop cannot eat the strokes, plus a thin rim in the world's tint. Dark glass for the light
    // tints; the two bright-backdrop worlds (dark navy / forest strokes) get frosted light glass.
    // Order = world index (Space, Frost, Verdant, Ember, Tide, Storm, Desert).
    public const float PlateAlpha = .82f;
    static readonly Color[] PlateFills =
    {
        new Color32(4, 16, 30, 255),       // Space
        new Color32(232, 244, 255, 255),   // Frost
        new Color32(232, 248, 226, 255),   // Verdant
        new Color32(30, 10, 4, 255),       // Ember
        new Color32(2, 26, 32, 255),       // Tide
        new Color32(10, 12, 34, 255),      // Storm
        new Color32(32, 20, 4, 255),       // Desert
    };
    public static Color PlateFill(int world) { return PlateFills[Mathf.Clamp(world, 0, PlateFills.Length - 1)]; }
    // The rim is the glyph's own tint, so the plate reads as one holographic part with the line.
    public static Color PlateRim(int world) { return IconTint(world); }

    // One shared texture holds both plate sprites (disc on the left, thin ring on the right), so a
    // button's plate and rim draw in one batch. Built once, never per frame.
    const int PlateTile = 128;
    static Sprite plateDisc, plateRing;
    static void EnsurePlate()
    {
        if (plateDisc != null && plateRing != null) return;
        var tex = new Texture2D(PlateTile * 2, PlateTile, TextureFormat.RGBA32, false);
        tex.name = "hud_plate";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[PlateTile * 2 * PlateTile];
        float c = (PlateTile - 1) * .5f, r = PlateTile * .5f - 1.5f, ring = 5f;
        for (int y = 0; y < PlateTile; y++)
            for (int x = 0; x < PlateTile; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float disc = Mathf.Clamp01(r - d + .5f);
                float rim = Mathf.Clamp01(Mathf.Min(r - d + .5f, d - (r - ring) + .5f));
                px[y * PlateTile * 2 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(disc * 255f));
                px[y * PlateTile * 2 + PlateTile + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(rim * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        plateDisc = Sprite.Create(tex, new Rect(0, 0, PlateTile, PlateTile), new Vector2(.5f, .5f), 100f);
        plateRing = Sprite.Create(tex, new Rect(PlateTile, 0, PlateTile, PlateTile), new Vector2(.5f, .5f), 100f);
    }
    public static Sprite PlateDisc { get { EnsurePlate(); return plateDisc; } }
    public static Sprite PlateRing { get { EnsurePlate(); return plateRing; } }

    public static Color IconTint(int world) { return IconTints[Mathf.Clamp(world, 0, IconTints.Length - 1)]; }
    public static Color IconHalo(int world) { return IconHalos[Mathf.Clamp(world, 0, IconHalos.Length - 1)]; }
    public static string DeathPanelPath(int world) { return Dir(world) + "dp_panel"; }
    public static string HomeGlyphPath(int world) { var f = Folder(world); return f == null ? SpaceHomeGlyph : f + "qa_home_glyph"; }
    public static string ReplayGlyphPath(int world) { var f = Folder(world); return f == null ? SpaceReplayGlyph : f + "qa_replay_glyph"; }

    // A world's sprite, falling back to Space's when a file is missing.
    public static Sprite Load(string path, string spacePath)
    {
        var s = Resources.Load<Sprite>(path);
        return s != null ? s : Resources.Load<Sprite>(spacePath);
    }

    public static Sprite Frame(int world) { return Load(FramePath(world), SpaceFrame); }
    // The in-run quick actions' thin hollow glyphs, shared by every world (the death panel keeps the solid ones above).
    public static Sprite HudHome(int world) { return Load(SharedHomePath, SpaceHomeGlyph); }
    public static Sprite HudReplay(int world) { return Load(SharedReplayPath, SpaceReplayGlyph); }
    public static Sprite DeathPanel(int world) { return Load(DeathPanelPath(world), SpaceDeathPanel); }
    public static Sprite HomeGlyph(int world) { return Load(HomeGlyphPath(world), SpaceHomeGlyph); }
    public static Sprite ReplayGlyph(int world) { return Load(ReplayGlyphPath(world), SpaceReplayGlyph); }
}
