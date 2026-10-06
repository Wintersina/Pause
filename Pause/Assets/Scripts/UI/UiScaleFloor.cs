using UnityEngine;
using UnityEngine.UI;

// UiScale's minimum scale on one ScaleWithScreenSize canvas.
//
// The canvas keeps its CanvasScaler (and its match mode); this only lowers
// the scaler's reference resolution, by exactly the factor the pixel scale
// falls short of the floor -- every match mode's scale is inversely
// proportional to the reference, so CanvasScaler then lands on the floor
// itself, and anything that reads the scaler to predict the canvas's scale
// (HudStyler.HudCanvasScale, the screen-fit rig) stays right.
//
// Applied when configured and again only when the screen's size or density
// changes (a few compares in LateUpdate, nothing allocated). Code that used
// to write scaler.referenceResolution calls Configure instead, so the
// authored (design) reference is never lost.
[DisallowMultipleComponent]
public class UiScaleFloor : MonoBehaviour
{
    public Vector2 design;
    public float minTapUnits, minTextUnits;

    CanvasScaler scaler;
    int lastW = -1, lastH = -1;
    float lastDpi = -1f;
    bool lastIos;

    // Sets the canvas's design reference and floor (in its own units) and
    // applies them now.
    public static UiScaleFloor Configure(CanvasScaler scaler, Vector2 designReference, float minTapUnits, float minTextUnits)
    {
        if (scaler == null) return null;
        var f = scaler.GetComponent<UiScaleFloor>();
        if (f == null) f = scaler.gameObject.AddComponent<UiScaleFloor>();
        f.scaler = scaler;
        f.design = designReference;
        f.minTapUnits = minTapUnits;
        f.minTextUnits = minTextUnits;
        f.Apply();
        return f;
    }

    // The scale CanvasScaler gives `scaler` with this design reference on the
    // current screen, before the floor.
    public static float PixelScale(CanvasScaler scaler, Vector2 designReference)
    {
        return HudStyler.ScaleWithScreenSize(ScreenInfo.Size, designReference, scaler.screenMatchMode, scaler.matchWidthOrHeight);
    }

    // The reference resolution that makes CanvasScaler land on the floor.
    public static Vector2 ReferenceFor(CanvasScaler scaler, Vector2 designReference, float minTapUnits, float minTextUnits)
    {
        float pixel = PixelScale(scaler, designReference);
        float floor = UiScale.Floor(minTapUnits, minTextUnits);
        return pixel > 0f && floor > pixel ? designReference * (pixel / floor) : designReference;
    }

    public void Apply()
    {
        if (scaler == null) scaler = GetComponent<CanvasScaler>();
        if (scaler == null || design.x <= 0f || design.y <= 0f) return;
        lastW = ScreenInfo.Width;
        lastH = ScreenInfo.Height;
        lastDpi = ScreenInfo.Dpi;
        lastIos = ScreenInfo.IsIos;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceFor(scaler, design, minTapUnits, minTextUnits);
        // CanvasScaler only applies it in its own Update: set the scale now,
        // so layout code that runs this frame measures the canvas it will get.
        var canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
            canvas.scaleFactor = Mathf.Max(PixelScale(scaler, design), UiScale.Floor(minTapUnits, minTextUnits));
    }

    void LateUpdate()
    {
        if (ScreenInfo.Width != lastW || ScreenInfo.Height != lastH || ScreenInfo.Dpi != lastDpi || ScreenInfo.IsIos != lastIos)
            Apply();
    }

    // The scenes' own canvases (all authored 800x600, scaled by width or
    // Expand) lay out touch targets of >= SceneTapUnits (their buttons are
    // 100 units tall) and type of >= SceneTextUnits: a floor for each root
    // ScaleWithScreenSize canvas present when a scene loads that has none
    // yet. Code that restyles one (startMenu, SafeAreaClamp, SpaceDock)
    // calls Configure with its own design, before or after this.
    public const float SceneTapUnits = 96f, SceneTextUnits = 15f;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        AttachScene();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) { AttachScene(); }

    public static void AttachScene()
    {
        foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            if (scaler.GetComponent<UiScaleFloor>() != null) continue;
            Configure(scaler, scaler.referenceResolution, SceneTapUnits, SceneTextUnits);
        }
    }

    // Every floor in the open scenes, for the screen ScreenInfo describes now
    // (the screen-fit rig, after it fakes a device).
    public static void ApplyAll()
    {
        foreach (var f in Object.FindObjectsByType<UiScaleFloor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            f.Apply();
    }
}
