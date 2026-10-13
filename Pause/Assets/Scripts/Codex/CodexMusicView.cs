using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The MUSIC tab of the codex: a header strip (what is playing, a STOP button)
// above one scrolling list of track cards. A card shows the track's name and
// kind; tapping an unlocked card plays it (tap again to stop), and the card
// that is playing reads NOW PLAYING with a red edge and a STOP chip. A track
// the player has not heard in a run yet reads "???" and does nothing.
// Same cel cards, ink outlines and palette as the other tabs.
//
// Playback and the menu-music ducking live in CodexMusicPlayer; the panel
// stops it whenever the tab or the codex is left.
public sealed class CodexMusicView
{
    // The cx_card 9-slice border is 28 canvas units (56 px at 200 ppu under a 100 ppu canvas):
    // text must start past it, plus a breathing gap, or it sits on the card's outline.
    public const float CardBorder = 28f;
    public const float PadX = 12f, PadY = 8f;
    public const float InsetX = CardBorder + PadX, InsetY = CardBorder + PadY;
    public const float StripHeight = 128f, StripGap = 12f;
    public const float RowHeight = 132f, RowGap = 12f;
    public const float StopWidth = 200f;
    public const string LockedHint = "Not heard yet.  Keep flying.";
    public const string IdleLine = "Tap a track to listen.";
    public const string SoundOffLine = "Sound is off in Options.";

    sealed class Row
    {
        public RectTransform rt;
        public Image frame, edge, chip;
        public Text title, sub, chipLabel;
        public Button button;
        public CodexTrack track;
    }

    readonly CodexPanel panel;
    readonly Font font;
    RectTransform root;
    CanvasGroup rootGroup;
    RectTransform strip;
    Text stripCount, stripLine;
    Image stopFrame;
    Text stopLabel;
    Button stopButton;
    RectTransform grid, viewport, content;
    ScrollRect scroll;
    Row[] rows;
    int shownCount;
    Rect body, stripRect, listRect;

    public CodexMusicView(CodexPanel panel, RectTransform parent, Font font)
    {
        this.panel = panel;
        this.font = font;
        Build(parent);
    }

    // ---- accessors for the tests ----
    public bool Active { get { return root.gameObject.activeSelf; } }
    public int VisibleRows { get { return shownCount; } }
    public CodexTrack RowTrack(int i) { return rows[i].track; }
    public Button RowButton(int i) { return rows[i].button; }
    public Text RowTitle(int i) { return rows[i].title; }
    public Text RowSub(int i) { return rows[i].sub; }
    public Text RowChip(int i) { return rows[i].chipLabel; }
    public Image RowEdge(int i) { return rows[i].edge; }
    public RectTransform RowRect(int i) { return rows[i].rt; }
    public int RowIndexOf(string id)
    {
        for (int i = 0; i < shownCount; i++) if (rows[i].track.id == id) return i;
        return -1;
    }
    public Button StopButton { get { return stopButton; } }
    public Text StripCount { get { return stripCount; } }
    public Text StripLine { get { return stripLine; } }
    public RectTransform StripRect { get { return strip; } }
    public Rect ListRect { get { return listRect; } }
    public Rect StripRectLocal { get { return stripRect; } }
    public float ContentHeight { get { return content.sizeDelta.y; } }
    public RectTransform Viewport { get { return viewport; } }

    void Build(RectTransform parent)
    {
        root = CodexUi.NewRect("Music", parent);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();

        strip = CodexUi.NewRect("Strip", root);
        var frame = CodexUi.NewImage("Frame", strip, CodexUi.CodexSprite("cx_card"), Color.white, true);
        CodexUi.Stretch(frame.rectTransform);
        var edge = CodexUi.NewImage("Edge", strip, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(edge.rectTransform);
        stripCount = Label("Count", strip, 30, 16, CodexUi.Title, TextAnchor.MiddleLeft, 2f);
        stripLine = Label("Line", strip, 18, 11, CodexUi.Body, TextAnchor.MiddleLeft, 1.5f);

        stopFrame = CodexUi.NewImage("Stop", strip, CodexUi.CodexSprite("cx_tab"), CodexUi.Select, true);
        stopFrame.raycastTarget = true;
        stopFrame.raycastPadding = new Vector4(-12f, -12f, -12f, -12f);
        stopButton = stopFrame.gameObject.AddComponent<Button>();
        stopButton.transition = Selectable.Transition.None;
        stopButton.targetGraphic = stopFrame;
        stopButton.onClick.AddListener(CodexMusicPlayer.Stop);
        stopLabel = Label("Label", stopFrame.rectTransform, 24, 13, CodexUi.Body, TextAnchor.MiddleCenter, 2f);
        CodexUi.Stretch(stopLabel.rectTransform);
        stopLabel.text = "STOP";

        var go = new GameObject("Grid", typeof(RectTransform), typeof(ScrollRect));
        go.transform.SetParent(root, false);
        grid = (RectTransform)go.transform;
        viewport = CodexUi.NewRect("Viewport", grid);
        CodexUi.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        content = CodexUi.NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        scroll = go.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.inertia = true;
        scroll.scrollSensitivity = 30f;

        rows = new Row[CodexMusic.All.Count];
        for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(i);
        root.gameObject.SetActive(false);
    }

    Text Label(string name, Transform parent, int size, int min, Color color, TextAnchor anchor, float outline)
    {
        var t = CodexUi.NewText(name, parent, font, "", size, color, anchor);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = min;
        t.resizeTextMaxSize = size;
        CodexUi.AddOutline(t.gameObject, CodexUi.Ink, outline);
        return t;
    }

    Row BuildRow(int index)
    {
        var r = new Row();
        r.frame = CodexUi.NewImage("Row" + index, content, CodexUi.CodexSprite("cx_card"), Color.white, true);
        r.frame.raycastTarget = true;
        r.rt = r.frame.rectTransform;
        r.button = r.frame.gameObject.AddComponent<Button>();
        r.button.targetGraphic = r.frame;
        var colors = r.button.colors;
        colors.pressedColor = CodexUi.Select;
        colors.highlightedColor = Color.white;
        colors.fadeDuration = .08f;
        r.button.colors = colors;
        r.button.onClick.AddListener(() => OnRowClicked(index));
        r.edge = CodexUi.NewImage("Edge", r.rt, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Locked, true);
        CodexUi.Stretch(r.edge.rectTransform);
        r.title = Label("Title", r.rt, 28, 14, CodexUi.Body, TextAnchor.MiddleLeft, 2f);
        r.sub = Label("Sub", r.rt, 18, 10, CodexUi.Muted, TextAnchor.MiddleLeft, 1.5f);
        r.chip = CodexUi.NewImage("Chip", r.rt, CodexUi.CodexSprite("cx_tab"), CodexUi.Accent, true);
        r.chipLabel = Label("Label", r.chip.rectTransform, 22, 12, CodexUi.Ink, TextAnchor.MiddleCenter, 0f);
        CodexUi.Stretch(r.chipLabel.rectTransform);
        var o = r.chipLabel.GetComponent<Outline>(); if (o != null) Object.Destroy(o);
        return r;
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    public void ApplyLayout(Rect bodyRect, CodexPanel.Layout l)
    {
        body = bodyRect;
        CodexUi.Place(root, bodyRect);
        float w = bodyRect.width, h = bodyRect.height;
        stripRect = new Rect(-w * .5f, h * .5f - StripHeight, w, StripHeight);
        listRect = Rect.MinMaxRect(-w * .5f, -h * .5f, w * .5f, stripRect.yMin - StripGap);
        CodexUi.Place(strip, stripRect);
        float btnW = Mathf.Min(StopWidth, w * .34f), btnH = StripHeight - 2f * InsetY;
        CodexUi.Place(stopFrame.rectTransform, CodexUi.Centered(w * .5f - InsetX - btnW * .5f, 0f, btnW, btnH));
        float textW = w - btnW - InsetX * 2f - 12f;
        float lineH = StripHeight * .5f - InsetY - 2f;
        CodexUi.Place(stripCount.rectTransform, new Rect(-w * .5f + InsetX, 2f, textW, lineH));
        CodexUi.Place(stripLine.rectTransform, new Rect(-w * .5f + InsetX, -2f - lineH, textW, lineH));
        CodexUi.Place(grid, listRect);
        LayoutRows();
    }

    void LayoutRows()
    {
        if (rows == null) return;
        float w = listRect.width - 2f * CodexPanel.GridInset;
        float y = 0f;
        for (int i = 0; i < shownCount; i++)
        {
            var r = rows[i];
            r.rt.anchorMin = r.rt.anchorMax = new Vector2(.5f, 1f);
            r.rt.pivot = new Vector2(.5f, 1f);
            r.rt.anchoredPosition = new Vector2(0f, -y);
            r.rt.sizeDelta = new Vector2(w, RowHeight);
            float chipW = Mathf.Min(170f, w * .26f), chipH = RowHeight - 2f * InsetY;
            CodexUi.Place(r.chip.rectTransform, CodexUi.Centered(w * .5f - InsetX - chipW * .5f, 0f, chipW, chipH));
            float textW = w - chipW - InsetX * 2f - 12f;
            float left = -w * .5f + InsetX;
            float lineH = RowHeight * .5f - InsetY - 2f;
            CodexUi.Place(r.title.rectTransform, new Rect(left, 2f, textW, lineH));
            CodexUi.Place(r.sub.rectTransform, new Rect(left, -2f - lineH, textW, lineH));
            y += RowHeight + RowGap;
        }
        float total = Mathf.Max(0f, y - RowGap) + 8f;
        content.sizeDelta = new Vector2(0f, total);
    }

    // ---------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------

    public void Show()
    {
        root.gameObject.SetActive(true);
        shownCount = 0;
        foreach (var t in CodexMusic.All)
        {
            if (!CodexMusic.IsListed(t) || shownCount >= rows.Length) continue;
            rows[shownCount++].track = t;
        }
        for (int i = 0; i < rows.Length; i++) rows[i].rt.gameObject.SetActive(i < shownCount);
        LayoutRows();
        scroll.velocity = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        Paint();
        CodexMusicPlayer.Changed -= Paint;
        CodexMusicPlayer.Changed += Paint;
    }

    public void Hide()
    {
        CodexMusicPlayer.Changed -= Paint;
        CodexMusicPlayer.Stop();
        root.gameObject.SetActive(false);
    }

    public void SetFade(float alpha) { rootGroup.alpha = alpha; }

    public void Tick()
    {
        CodexMusicPlayer.Tick();
    }

    // Repaints every row, the strip and the STOP button from the unlock state and the player.
    public void Paint()
    {
        if (rows == null || !root.gameObject.activeSelf) return;
        string now = CodexMusicPlayer.NowPlaying;
        for (int i = 0; i < shownCount; i++)
        {
            var r = rows[i];
            bool unlocked = CodexMusic.IsUnlocked(r.track);
            bool playing = unlocked && r.track.id == now;
            r.title.text = CodexMusic.DisplayTitle(r.track);
            r.title.color = unlocked ? CodexUi.Body : CodexUi.Muted;
            r.sub.text = !unlocked ? LockedHint : playing ? "NOW PLAYING" : r.track.kind;
            r.sub.color = playing ? CodexUi.Title : CodexUi.Muted;
            r.edge.color = playing ? CodexUi.Select : unlocked ? CodexUi.Accent : CodexUi.Locked;
            r.frame.color = unlocked ? Color.white : new Color(.7f, .7f, .8f, 1f);
            r.chip.gameObject.SetActive(unlocked);
            r.chip.color = playing ? CodexUi.Select : CodexUi.Accent;
            r.chipLabel.text = playing ? "STOP" : "PLAY";
            r.chipLabel.color = playing ? CodexUi.Body : CodexUi.Ink;
            r.button.interactable = unlocked;
        }
        stripCount.text = now != null
            ? "NOW PLAYING"
            : CodexMusic.UnlockedCount + " / " + CodexMusic.ListedCount + "  HEARD";
        var t = now != null ? CodexMusic.Find(now) : null;
        stripLine.text = t != null ? t.title + "  -  " + t.kind
                       : SoundSettings.Muted ? SoundOffLine : IdleLine;
        stopButton.interactable = now != null;
        stopFrame.color = now != null ? CodexUi.Select : CodexUi.Locked;
        stopLabel.color = now != null ? CodexUi.Body : CodexUi.Muted;
    }

    void OnRowClicked(int index)
    {
        var t = rows[index].track;
        if (t == null || !CodexMusic.IsUnlocked(t)) return;
        if (CodexMusicPlayer.NowPlaying == t.id) CodexMusicPlayer.Stop();
        else CodexMusicPlayer.Play(t.id);
    }
}
