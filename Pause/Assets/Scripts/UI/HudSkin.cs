using UnityEngine;

// The in-run read-outs' per-world skins: the score board, the home / replay
// panel beside it and the FLIGHT COMPLETE panel each wear their world's frame
// and glyphs, and swap when the run enters the next world.
//
// Space's glyphs are the original QuickActions ones; every world has a folder,
// Resources/HudSkins/<WorldTheme.displayName>/, holding the same four sprites:
//   hud_board        702x310 px, the score board's frame: sliced, border 50 px
//                    left/right and 60 px top/bottom, 200 ppu; drawn 371 x 163 units: the
//                    351 x 131 score board plus HudStyler.FrameOutX / FrameOutY
//                    on each side, outside the layout rect
//   hud_buttons      340x310 px, the home / replay panel's frame (drawn
//                    174 x 163 units around the 154 x 131 panel), the board's motif, drawn as its mirror image
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
    // Where the visible inside of the home / replay panel's frame sits, as the
    // distance (canvas units, + is down) from the frame's centre to the centre
    // of its dark inside. Measured from hud_buttons (sprite px, 200 ppu drawn
    // at half scale): the top margin to the inside (icicles / crystals) minus
    // the bottom margin, halved. The glyph pair is centred there, not on the
    // rect, so the gaps above and below the icons read equal. Order = world index.
    static readonly float[] ButtonInnerTopPx = { 38f, 39f, 49f, 53f, 42f, 63f, 59f };
    static readonly float[] ButtonInnerBottomPx = { 32f, 60f, 47f, 51f, 43f, 71f, 48f };
    public static float ButtonInnerOffsetY(int world)
    {
        int w = Mathf.Clamp(world, 0, ButtonInnerTopPx.Length - 1);
        return (ButtonInnerTopPx[w] - ButtonInnerBottomPx[w]) * .25f;
    }

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
