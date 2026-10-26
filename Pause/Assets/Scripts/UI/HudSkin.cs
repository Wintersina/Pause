using UnityEngine;

// The in-run read-outs' per-world skins: the score board, the home / replay
// panel beside it and the FLIGHT COMPLETE panel each wear their world's frame
// and glyphs, and swap when the run enters the next world.
//
// Space's glyphs are the original QuickActions ones; every world has a folder,
// Resources/HudSkins/<WorldTheme.displayName>/, holding the same four sprites:
//   hud_board        702x310 px, the score board's frame: sliced, border 50 px
//                    left/right and 60 px top/bottom, 200 ppu; drawn 363 x 151 units: the
//                    351 x 131 score board plus HudStyler.FrameOutX / FrameOutY
//                    on each side, outside the layout rect
//   hud_buttons      340x310 px, the home / replay panel's frame (drawn
//                    166 x 151 units around the 154 x 131 panel), the board's motif, drawn as its mirror image
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
