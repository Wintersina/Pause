using UnityEngine;

// One planet's look and feel.
//
// A world is a re-theme of gameS1 rather than a separate scene: the backdrop
// and the two side walls swap their textures, everything else keeps running.
// That keeps all the existing wiring intact and means adding a planet is an
// art drop, not scene surgery.
//
// Textures live at Resources/Worlds/<resourceFolder>/ and are loaded by name:
//   backdrop    1024 x 4096, seamless vertical tile, opaque
//   wallLeft      64 x 448,  seamless vertical tile
//   wallRight     64 x 448,  seamless vertical tile
[System.Serializable]
public class WorldTheme
{
    public string displayName = "Space";

    [Tooltip("Folder under Resources/Worlds/. Empty means the scene's own " +
             "authored art is left alone -- that is how the original space " +
             "world stays exactly as it was.")]
    public string resourceFolder = "";

    [Tooltip("Multiplied into the backdrop and walls. Lets one texture set be " +
             "reused at different times of day.")]
    public Color tint = Color.white;

    [Tooltip("Clip under Resources/WorldMusic/. Empty keeps whatever the scene " +
             "already had playing, which is how the space world keeps its " +
             "original track.")]
    public string musicResource = "";

    [Tooltip("Colour of this world's portal.")]
    public Color portalColor = new Color(0.55f, 0.85f, 1f);

    [Tooltip("Speed ramp for this world. Later planets can escalate faster.")]
    public float speedRampPerSecond = 0.002f;

    public float maxSpeed = 0.6f;

    [Tooltip("Multiplies elapsed flight time before enmiesOnBoard checks its " +
             "phase thresholds. 1 is Space's own pace; later worlds set this " +
             "higher so enemy density keeps escalating faster than earlier " +
             "planets, independent of (and continuing past) the speed cap above.")]
    public float enemyRampScale = 1f;
}
