using UnityEngine;

// The one breakpoint every screen shares for "this screen is wide / near-square": width / height from WideAspect up
// (the Z Flip cover screen, a foldable's inner screen, a tablet). The Codex (tab rail beside the body) and the Space Dock
// (rack beside the ship card) already split into two panes there; screens that cannot split (the Options column) use
// it too, to know they are on a short canvas and must scroll rather than stack.
public static class ScreenLayout
{
    public const float WideAspect = .8f;

    public static bool IsWide(float width, float height)
    {
        return height > 0f && width / height >= WideAspect;
    }

    public static bool IsWide(Rect area) { return IsWide(area.width, area.height); }
}
