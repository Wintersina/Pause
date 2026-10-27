using UnityEngine;

// The in-run read-outs' per-world skins: the score board, the home / replay
// panel beside it and the FLIGHT COMPLETE panel each wear their world's frame
// and glyphs, and swap when the run enters the next world.
//
// Space's glyphs are the original QuickActions ones; every world has a folder,
// Resources/HudSkins/<WorldTheme.displayName>/, holding the same four sprites:
//   hud_board        702x310 px, the score board's frame: sliced, border 50 px
//                    left/right and 60 px top/bottom, 200 ppu; drawn about 351 x 131 plus BoardOut units: the
//                    351 x 131 score board plus the per-world BoardOut growth
//                    on each side, outside the layout rect
//   hud_buttons      340x310 px, the home / replay panel's frame (drawn
//                    ButtonOut units around the 154 x 131 panel), the board's motif, drawn as its mirror image
//   dp_panel         600x900 px, the FLIGHT COMPLETE panel, border 100 px
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
    // Storm, Desert). TUNED FROM RENDERS (HudVetCapture + the gap measurement
    // in the PR): with the worst-case text (1,234,567 / SPEED 999 / PAUSES 99)
    // the nearest icicle, crystal or rail is at least ~5 px (at 1080 wide)
    // from the text on the left / top / bottom, top and bottom equal so the
    // three rows sit centred in the frame's dark inside; the button panel's
    // icons are equal left / right and top / bottom. Re-render after changing
    // frame art.
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
    static readonly float[][] ButtonOuts =
    {
        new[]{3f, 9f, 7f, 10.5f},         // Space
        new[]{11f, 22.5f, 6.5f, 18f},     // Frost
        new[]{12.5f, 19f, 14f, 20f},      // Verdant
        new[]{11f, 18f, 10f, 22.5f},      // Ember
        new[]{9f, 16f, 8f, 12f},          // Tide
        new[]{14f, 27.5f, 13.5f, 27.5f},  // Storm
        new[]{11.5f, 19.5f, 14f, 26f},    // Desert
    };

    // x = left, y = bottom, z = right, w = top.
    public static Vector4 BoardOut(int world) { return OutFor(BoardOuts, world); }
    public static Vector4 ButtonOut(int world) { return OutFor(ButtonOuts, world); }

    static Vector4 OutFor(float[][] table, int world)
    {
        var m = table[Mathf.Clamp(world, 0, table.Length - 1)];
        return new Vector4(m[0], m[1], m[2], m[3]);
    }

    // The home / replay glyphs sit this far (canvas units, + is down) from the
    // button rect's centre; the outs above were tuned with this shift in place.
    public static float ButtonInnerOffsetY(int world) { return -1.5f; }

    // Verdant's home art is drawn smaller than its restart arrow: scaled up to match.
    public static float HomeGlyphScale(int world) { return world == 2 ? 1.2f : 1f; }

    // Extra room the text needs on the left where the art's decoration (Desert's
    // sandworm head) sits inside the frame's left edge, canvas units.
    static readonly float[] TextExtraLeft = { 3f, 12f, 6f, 4f, 8f, 4f, 24f };   // tuned from renders, like the outs
    public static float BoardTextExtraLeft(int world) { return TextExtraLeft[Mathf.Clamp(world, 0, TextExtraLeft.Length - 1)]; }

    public static string FramePath(int world) { return Dir(world) + "hud_board"; }
    public static string ButtonPanelPath(int world) { return Dir(world) + "hud_buttons"; }
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
    public static Sprite ButtonPanel(int world) { return Load(ButtonPanelPath(world), SpaceFrame); }
    public static Sprite DeathPanel(int world) { return Load(DeathPanelPath(world), SpaceDeathPanel); }
    public static Sprite HomeGlyph(int world) { return Load(HomeGlyphPath(world), SpaceHomeGlyph); }
    public static Sprite ReplayGlyph(int world) { return Load(ReplayGlyphPath(world), SpaceReplayGlyph); }
}
