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
    // How far each world's frame art reaches INTO its drawn rect: the distance
    // from the sprite's edge to the dark inside, in sprite px at 200 ppu (drawn
    // at half size, so /2 = canvas units). Measured from hud_board / hud_buttons
    // (the median over the middle of each edge, so icicles and crystals count).
    // Order = world index: Space, Frost, Verdant, Ember, Tide, Storm, Desert.
    // { top, bottom, left, right }
    static readonly int[][] BoardInset =
    {
        new[]{56,46,25,25}, new[]{42,67,37,36}, new[]{54,47,36,37}, new[]{67,63,33,32},
        new[]{49,44,19,19}, new[]{71,79,38,38}, new[]{75,50,73,43},
    };
    static readonly int[][] ButtonInset =
    {
        new[]{38,32,19,19}, new[]{39,60,27,27}, new[]{49,47,33,33}, new[]{53,51,27,27},
        new[]{42,43,23,22}, new[]{63,71,38,38}, new[]{59,48,37,38},
    };
    // What the frame may still reach into the layout rect (canvas units): the
    // text / icons live in the rest. Space's frame at these values is the
    // reference look.
    const float KeepTop = 4f, KeepBottom = 7f, KeepSide = 2.5f, MinOut = 6f, MaxOutX = 14f;

    // Extra room the text needs on the left where the art's decoration (Desert's
    // sandworm head) sits inside the frame's left edge, canvas units.
    public static float BoardTextExtraLeft(int world) { return world == 6 ? 12f : 0f; }

    // The frame's growth past the layout rect, canvas units: x = left, y = bottom,
    // z = right, w = top. Sized per world so the art's inside matches the rect.
    public static Vector4 BoardOut(int world) { return OutFor(BoardInset, world); }
    public static Vector4 ButtonOut(int world) { return OutFor(ButtonInset, world); }

    static Vector4 OutFor(int[][] table, int world)
    {
        var m = table[Mathf.Clamp(world, 0, table.Length - 1)];
        return new Vector4(
            Mathf.Clamp(m[2] * .5f - KeepSide, MinOut, MaxOutX),
            Mathf.Max(MinOut, m[1] * .5f - KeepBottom),
            Mathf.Clamp(m[3] * .5f - KeepSide, MinOut, MaxOutX),
            Mathf.Max(MinOut, m[0] * .5f - KeepTop));
    }

    // The home / replay pair is centred on the frame's dark inside: its centre
    // sits this far (canvas units, + is down) below the layout rect's centre.
    public static float ButtonInnerOffsetY(int world)
    {
        var m = ButtonInset[Mathf.Clamp(world, 0, ButtonInset.Length - 1)];
        Vector4 o = ButtonOut(world);
        float insetTop = m[0] * .5f - o.w, insetBottom = m[1] * .5f - o.y;
        return (insetTop - insetBottom) * .5f;
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
