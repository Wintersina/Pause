using UnityEngine;
using UnityEngine.UI;

// The START WORLD picker of the hangar (shopS6): one thin line under the
// "SPACE DOCK" instruction, "<   START  <WORLD>   >". Reads and writes the same
// saved choice as before (PlayerStartWorld), with the same rules: nothing
// shows until a level is finished, only reached worlds are offered, the
// furthest reached follows future progress, and developer mode (whose own
// picker wins) hides it.
//
// No plate or border: cyan text with finger-sized invisible tap targets. The
// width is capped by the canvas, so it stays centred and inside any screen;
// SpaceDock reserves its height in the header like the instruction line.
public class DockStartWorld : MonoBehaviour
{
    public const string Name = "~DockStartWorld";
    public const string RowName = "Row", PrevName = "Prev", NextName = "Next", LabelName = "World";
    public const float Y = -98f, Height = 40f, MaxWidth = 460f, ArrowW = 76f;

    GameObject row;
    Text label;
    bool shown;
    int shownWorld = -2, shownHighest = -2;

    // Whether the picker is on offer (pure, for tests).
    public static bool Visible(bool unlocked, bool developerMode) { return unlocked && !developerMode; }
    static bool Dev { get { return DeveloperUnlocks.Available && DeveloperUnlocks.Enabled; } }

    public GameObject Row { get { return row; } }

    public static DockStartWorld Build(Transform canvas)
    {
        var existing = SceneUtil.FindAny(Name);
        if (existing != null) return existing.GetComponent<DockStartWorld>();
        var go = new GameObject(Name, typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
        rt.pivot = new Vector2(.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, Y);
        rt.sizeDelta = new Vector2(MaxWidth, 0f);
        var comp = go.AddComponent<DockStartWorld>();
        comp.Construct();
        comp.Refresh();
        return comp;
    }

    void Construct()
    {
        var font = DockArt.FindSceneFont();
        row = new GameObject(RowName, typeof(RectTransform));
        var rr = (RectTransform)row.transform;
        rr.SetParent(transform, false);
        rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(.5f, .5f);
        rr.anchoredPosition = Vector2.zero;
        rr.sizeDelta = new Vector2(MaxWidth, Height);
        Piece(PrevName, font, "<", new Vector2(-(MaxWidth - ArrowW) * .5f, 0f), ArrowW, 26, () => Step(-1));
        Piece(NextName, font, ">", new Vector2((MaxWidth - ArrowW) * .5f, 0f), ArrowW, 26, () => Step(1));
        label = Piece(LabelName, font, "", Vector2.zero, MaxWidth - 2f * ArrowW, 20, () => Step(1));
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 12;
        label.resizeTextMaxSize = 20;
    }

    Text Piece(string name, Font font, string text, Vector2 pos, float width, int size, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(row.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, Height);
        var t = go.GetComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f);
        t.text = text;
        t.raycastPadding = new Vector4(0f, -4f, 0f, -4f);   // a finger-tall target
        var b = go.GetComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = t;
        b.onClick.AddListener(onClick);
        return t;
    }

    void Step(int direction)
    {
        if (!PlayerStartWorld.Unlocked || Dev) return;
        PlayerStartWorld.Step(direction);
        Refresh();
    }

    public void Refresh()
    {
        bool on = Visible(PlayerStartWorld.Unlocked, Dev);
        shownWorld = PlayerStartWorld.Selected;
        shownHighest = PlayerStartWorld.Highest;
        if (label != null) label.text = PlayerStartWorld.Label(shownWorld);
        if (on == shown && row != null && row.activeSelf == on) return;
        shown = on;
        if (row != null) row.SetActive(on);
        if (SpaceDock.Instance != null) SpaceDock.Instance.Relayout();   // the rack refits under the line
    }

    void Update()
    {
        if (Visible(PlayerStartWorld.Unlocked, Dev) != shown || PlayerStartWorld.Selected != shownWorld ||
            PlayerStartWorld.Highest != shownHighest)
            Refresh();
    }
}
