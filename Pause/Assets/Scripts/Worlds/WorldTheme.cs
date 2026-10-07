using UnityEngine;

// One planet's look and feel.
//
// A world is a re-theme of gameS1 rather than a separate scene: the two side
// walls swap their textures and WorldBackdrop swaps in the world's animated
// parallax background; everything else keeps running. That keeps all the
// existing wiring intact and means adding a planet is an art drop, not scene
// surgery.
//
// Wall textures live at Resources/Worlds/<resourceFolder>/:
//   rail_<theme>_wide_v1   reinforced industrial tile, mirrored on the right
//   wallLeft / wallRight  legacy fallback for worlds without a reinforced rail
// Backgrounds live at Backgrounds/Resources/Worlds/<displayName>/Backdrop/ (see
// BackdropCatalog), generated from Assets/Art/Worlds/<displayName>/src~/.
[System.Serializable]
public class WorldTheme
{
    public string displayName = "Space";

    [Tooltip("Folder under Resources/Worlds/. Reinforced rails use the world " +
             "display name when empty; worlds without one keep scene art.")]
    public string resourceFolder = "";

    [Tooltip("Multiplied into the walls. Lets one texture set be " +
             "reused at different times of day.")]
    public Color tint = Color.white;

    [Tooltip("Clip under Resources/WorldMusic/. Empty keeps whatever the scene " +
             "already had playing, which is how the space world keeps its " +
             "original track.")]
    public string musicResource = "";

    [Tooltip("True plays the six 30-second WorldMusic/<displayName>StageNN " +
             "arrangements, escalating on the level clock. False plays " +
             "musicResource as one song for the whole level, looped.")]
    public bool progressiveMusic = true;

    [Tooltip("Colour of this world's portal.")]
    public Color portalColor = new Color(0.55f, 0.85f, 1f);

    [Tooltip("Speed ramp for this world. Later planets can escalate faster. " +
             "Every world shares the one cap (SpeedRamp.Cap).")]
    public float speedRampPerSecond = 0.002f;

    [Tooltip("Multiplies elapsed flight time before enmiesOnBoard checks its " +
             "phase thresholds. 1 is Space's own pace; later worlds set this " +
             "higher so enemy density keeps escalating faster than earlier " +
             "planets, independent of (and continuing past) the speed cap.")]
    public float enemyRampScale = 1f;
}
