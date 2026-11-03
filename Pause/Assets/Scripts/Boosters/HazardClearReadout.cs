using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The run's HAZARD CLEAR readout: how many automatic charges are held (HazardClear fires them by itself when a hit would cost a heart).
// NOT a control: no Button, no GraphicRaycaster, every graphic has raycastTarget off, and movePlayer / score never ask about it, so a
// finger on it is an ordinary press (teleport / resume). Built on gameS1 only (never the tutorial) and shown only while a charge is
// held: a pilot who owns none sees nothing. Developer mode shows it only for real held charges too.
//
// PLACEMENT. Pinned to the right edge of the top band, directly UNDER the Replay/Home pair (PauseQuickActions.ScreenRectFor), one
// ButtonSize square, so it follows ScreenFit's tall phones, notches, narrow lanes and tablets. RectFor is pure (BoostersTest).
//
// LOOK. Pixel art drawn in code at 16 px, point filtered: a chamfered dark plate with no border, a thin burst glyph in the world's cue
// colour (pink only in Space), and a small "x3" at its corner.
public class HazardClearReadout : MonoBehaviour
{
    public const float Gap = 6f;                 // canvas units under the quick actions
    public const string HostName = "~HazardClearReadout";
    public const int GlyphPx = 16;

    static HazardClearReadout live;

    // The readout's screen-pixel rect (origin bottom-left) for a top band on `screen`.
    public static Rect RectFor(TopBand.Frame band, Vector2 screen)
    {
        float s = PauseQuickActions.CanvasScaleFor(screen);
        Rect pair = PauseQuickActions.ScreenRectFor(band, screen);
        float side = PauseQuickActions.ButtonSize * s;
        float right = pair.xMax - PauseQuickActions.PanelPad * s;
        float top = pair.yMin - Gap * s;
        return new Rect(right - side, top - side, side, side);
    }

    RectTransform root;
    Image plate, glyph;
    Text count;
    CanvasGroup group;
    Rect screenRect;
    bool shown;
    Vector2Int appliedScreen;
    Rect appliedSafe;
    int skinWorld = -1;

    void Awake() { live = this; }
    void OnDestroy() { if (live == this) live = null; }

    void Build()
    {
        var holder = new GameObject("HazardClearCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        holder.transform.SetParent(transform, false);
        var canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 89;   // under the quick actions (90)
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;   // 1 canvas unit = 1 px: RectFor is in screen pixels
        scaler.scaleFactor = 1f;
        group = holder.GetComponent<CanvasGroup>();

        var go = new GameObject("ClearReadout", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(holder.transform, false);
        root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = Vector2.zero;
        plate = go.GetComponent<Image>();
        plate.sprite = PlateSprite();
        plate.color = new Color(.04f, .05f, .1f, .6f);
        plate.raycastTarget = false;
        group.blocksRaycasts = false;
        group.interactable = false;

        var g = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
        g.transform.SetParent(go.transform, false);
        var gr = (RectTransform)g.transform;
        gr.anchorMin = new Vector2(.12f, .12f); gr.anchorMax = new Vector2(.88f, .88f);
        gr.offsetMin = gr.offsetMax = Vector2.zero;
        glyph = g.GetComponent<Image>();
        glyph.sprite = GlyphSprite();
        glyph.raycastTarget = false;

        var t = new GameObject("Count", typeof(RectTransform), typeof(Text));
        t.transform.SetParent(go.transform, false);
        var tr = (RectTransform)t.transform;
        tr.anchorMin = new Vector2(.4f, 0f); tr.anchorMax = new Vector2(1f, .45f);
        tr.offsetMin = tr.offsetMax = Vector2.zero;
        count = t.GetComponent<Text>();
        count.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        count.alignment = TextAnchor.LowerRight;
        count.raycastTarget = false;
        count.horizontalOverflow = HorizontalWrapMode.Overflow;
        count.verticalOverflow = VerticalWrapMode.Overflow;
    }

    static Sprite plateSprite, glyphSprite;

    static Sprite PlateSprite()
    {
        if (plateSprite != null) return plateSprite;
        var tex = NewTex();
        var px = new Color32[GlyphPx * GlyphPx];
        for (int y = 0; y < GlyphPx; y++)
            for (int x = 0; x < GlyphPx; x++)
            {
                int dx = Mathf.Min(x, GlyphPx - 1 - x), dy = Mathf.Min(y, GlyphPx - 1 - y);
                bool chamfer = dx + dy < 2;   // the four corners are cut
                px[y * GlyphPx + x] = chamfer ? new Color32(0, 0, 0, 0) : new Color32(255, 255, 255, 255);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        plateSprite = Sprite.Create(tex, new Rect(0, 0, GlyphPx, GlyphPx), new Vector2(.5f, .5f), GlyphPx);
        return plateSprite;
    }

    // A small burst: a 2x2 core, four long rays and four short diagonals, one pixel thin.
    static Sprite GlyphSprite()
    {
        if (glyphSprite != null) return glyphSprite;
        var tex = NewTex();
        var px = new Color32[GlyphPx * GlyphPx];
        var on = new Color32(255, 255, 255, 255);
        int c0 = GlyphPx / 2 - 1, c1 = GlyphPx / 2;   // 7 and 8
        for (int a = c0; a <= c1; a++)
            for (int b = c0; b <= c1; b++) px[b * GlyphPx + a] = on;
        for (int k = 3; k <= 7; k++)
        {
            Put(px, c0 - k, c0, on); Put(px, c1 + k, c1, on);   // left / right rays
            Put(px, c0, c0 - k, on); Put(px, c1, c1 + k, on);   // down / up rays
        }
        for (int k = 2; k <= 4; k++)
        {
            Put(px, c0 - k, c0 - k, on); Put(px, c1 + k, c0 - k, on);
            Put(px, c0 - k, c1 + k, on); Put(px, c1 + k, c1 + k, on);
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        glyphSprite = Sprite.Create(tex, new Rect(0, 0, GlyphPx, GlyphPx), new Vector2(.5f, .5f), GlyphPx);
        return glyphSprite;
    }

    static void Put(Color32[] px, int x, int y, Color32 c)
    {
        if (x >= 0 && y >= 0 && x < GlyphPx && y < GlyphPx) px[y * GlyphPx + x] = c;
    }

    static Texture2D NewTex()
    {
        return new Texture2D(GlyphPx, GlyphPx, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "~ClearReadoutArt" };
    }

    void Update()
    {
        var state = HazardClear.BlockedBy();
        bool want = Boosters.ClearAvailable && state != HazardClear.Block.NotInRun && state != HazardClear.Block.Dead;
        if (want && root == null) Build();
        if (root == null) return;
        if (want != shown) { shown = want; root.gameObject.SetActive(want); }
        if (!want) return;

        var screen = new Vector2Int(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        if (screen != appliedScreen || safe != appliedSafe) { appliedScreen = screen; appliedSafe = safe; Place(); }

        int world = Mathf.Max(0, WorldManager.CurrentIndex);
        if (world != skinWorld)
        {
            skinWorld = world;
            glyph.color = WorldCue.Trace(world);
            count.color = WorldCue.Trace(world);
        }
        group.alpha = state == HazardClear.Block.None ? 1f : .45f;
        count.text = "x" + Boosters.ClearCharges;
    }

    void Place()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        var band = TopBand.FrameFor(Screen.safeArea, screen);
        screenRect = RectFor(band, screen);
        root.anchoredPosition = screenRect.position;
        root.sizeDelta = screenRect.size;
        count.fontSize = Mathf.Max(10, Mathf.RoundToInt(screenRect.height * .26f));
    }
}

// Builds the button when the run scene loads.
public static class HazardClearReadoutBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (GameObject.Find(HazardClearReadout.HostName) != null) return;
        new GameObject(HazardClearReadout.HostName).AddComponent<HazardClearReadout>();
    }
}
