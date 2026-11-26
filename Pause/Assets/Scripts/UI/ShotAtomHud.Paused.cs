using UnityEngine;
using UnityEngine.UI;

public partial class ShotAtomHud
{
    RectTransform pausedRoot;
    readonly Slot[] pausedSlots = new Slot[DroneSquad.MaxSlots * Columns];
    readonly CanvasGroup[] pausedRows = new CanvasGroup[DroneSquad.MaxSlots];
    bool expanded, pausePlaced;
    float expandAge;
    int pauseVersion = -1, pauseSquadVersion = -1, pauseCount;
    Vector2 pauseScreen;
    Rect pauseSafe, pauseHud;
    public bool PauseExpanded => expanded;
    public void AdvancePausedReview(float seconds) { TickPausedRows(seconds); }
    public Rect PausedRowRect(int drone)
    {
        var rt = (RectTransform)pausedRows[drone].transform;
        return new Rect(rt.anchoredPosition, rt.sizeDelta);
    }
    public int ExpandedRowsVisible => expanded ? Mathf.Min(DroneSquad.Slots, 1 + Mathf.FloorToInt(expandAge / .08f)) : 0;
    public string PausedLevelLabel(int drone, ShotAtom atom) => pausedSlots[drone * Columns + SelectedAtomSlot(atom)]?.label.text;

    void BuildPausedRows(Transform parent)
    {
        var root = new GameObject("PausedDroneUpgrades", typeof(RectTransform));
        pausedRoot = root.GetComponent<RectTransform>(); pausedRoot.SetParent(parent, false);
        pausedRoot.anchorMin = pausedRoot.anchorMax = pausedRoot.pivot = Vector2.zero;
        for (int drone = 0; drone < DroneSquad.MaxSlots; drone++)
        {
            var row = new GameObject("Drone" + (drone + 1), typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            row.transform.SetParent(pausedRoot, false);
            var background = row.GetComponent<Image>(); background.color = new Color(.015f, .025f, .05f, .78f); background.raycastTarget = false;
            pausedRows[drone] = row.GetComponent<CanvasGroup>(); pausedRows[drone].blocksRaycasts = false;
            var rowRect = (RectTransform)row.transform; rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = Vector2.zero;
            for (int col = 0; col < Columns; col++)
            {
                var slot = new Slot { drone = drone, glyph = col == 0, atom = col == RapidColumn ? ShotAtom.Rapid : (ShotAtom)Mathf.Max(0, col - 1) };
                var go = new GameObject("Upgrade" + col, typeof(RectTransform)); go.transform.SetParent(row.transform, false);
                slot.root = (RectTransform)go.transform; slot.root.anchorMin = slot.root.anchorMax = slot.root.pivot = Vector2.zero;
                var image = new GameObject("Icon", typeof(RectTransform), typeof(Image)); image.transform.SetParent(go.transform, false);
                slot.icon = image.GetComponent<Image>(); slot.icon.preserveAspect = true; slot.icon.raycastTarget = false;
                slot.icon.rectTransform.anchorMin = slot.icon.rectTransform.anchorMax = Vector2.zero;
                slot.icon.rectTransform.pivot = new Vector2(.5f, 0f);
                var label = new GameObject("Level", typeof(RectTransform), typeof(Text), typeof(Outline)); label.transform.SetParent(go.transform, false);
                slot.label = label.GetComponent<Text>(); slot.label.font = font; slot.label.alignment = TextAnchor.MiddleCenter;
                slot.label.raycastTarget = false; slot.label.horizontalOverflow = HorizontalWrapMode.Overflow;
                slot.label.verticalOverflow = VerticalWrapMode.Overflow; slot.label.color = AkiraPalette.Bone;
                var outline = label.GetComponent<Outline>(); outline.effectColor = AkiraPalette.Ink; outline.effectDistance = Vector2.one;
                slot.label.rectTransform.anchorMin = slot.label.rectTransform.anchorMax = Vector2.zero;
                slot.label.rectTransform.pivot = new Vector2(.5f, 0f);
                pausedSlots[drone * Columns + col] = slot;
            }
        }
        root.SetActive(false);
    }
    void RefreshPausedRows()
    {
        pauseVersion = ShotAtoms.Version; pauseSquadVersion = DroneSquad.Version;
        for (int drone = 0; drone < DroneSquad.MaxSlots; drone++)
        {
            pausedRows[drone].gameObject.SetActive(drone < DroneSquad.Slots);
            for (int col = 0; col < Columns; col++)
            {
                var slot = pausedSlots[drone * Columns + col];
                int level = slot.glyph ? DroneSquad.CoreLevel(drone) : ShotAtoms.Level(drone, slot.atom);
                slot.icon.sprite = slot.glyph ? DronePickupArt.For(DroneSquad.Mode(drone)) : Frame(slot.atom);
                slot.icon.color = level > 0 ? Color.white : new Color(.45f, .5f, .58f, .6f);
                slot.label.text = slot.glyph ? (drone + 1) + (level > 0 ? ":L" + level : ":OFF") : LevelLabel(level);
            }
        }
    }
    void PlacePausedRows()
    {
        var screen = ScreenInfo.Size; Rect safe = ScreenInfo.SafeArea;
        var band = TopBand.FrameFor(safe, screen); Rect hud = LiveHudRect(band, screen);
        if (pausePlaced && pauseScreen == screen && pauseSafe == safe && pauseHud == hud && pauseCount == DroneSquad.Slots) return;
        pausePlaced = true; pauseScreen = screen; pauseSafe = safe; pauseHud = hud; pauseCount = DroneSquad.Slots;
        var g = Compute(band, safe, screen, hud, HudSkin.CurrentWorld, 1);
        float width = Columns * g.pitch - (g.pitch - g.slotWidth), cell = width / Columns;
        float rowHeight = Mathf.Min(Mathf.Clamp(34f * PauseQuickActions.CanvasScaleFor(screen), 32f, 58f),
            Mathf.Max(24f, (g.top - safe.yMin - 34f) / Mathf.Max(1, DroneSquad.Slots)));
        float labelHeight = Mathf.Clamp(rowHeight * .34f, 11f, 20f), icon = Mathf.Min(cell - 6f, rowHeight - labelHeight - 5f);
        for (int drone = 0; drone < DroneSquad.MaxSlots; drone++)
        {
            var row = (RectTransform)pausedRows[drone].transform;
            row.anchoredPosition = new Vector2(g.left, g.top - (drone + 1) * rowHeight);
            row.sizeDelta = new Vector2(width, rowHeight - 2f);
            for (int col = 0; col < Columns; col++)
            {
                var slot = pausedSlots[drone * Columns + col];
                slot.root.anchoredPosition = new Vector2(col * cell, 0f); slot.root.sizeDelta = new Vector2(cell, rowHeight);
                slot.icon.rectTransform.anchoredPosition = new Vector2(cell * .5f, labelHeight);
                slot.icon.rectTransform.sizeDelta = Vector2.one * icon;
                slot.label.fontSize = Mathf.Max(10, Mathf.FloorToInt(labelHeight - 1f));
                slot.label.rectTransform.anchoredPosition = new Vector2(cell * .5f, 0f);
                slot.label.rectTransform.sizeDelta = new Vector2(cell, labelHeight);
            }
        }
    }
    void TickPausedRows(float dt)
    {
        bool want = !WorldManager.Flying && !buttonClicks.playerDied && !WorldEntry.Active && !WorldTransition.InProgress && !BossEncounter.ScriptedEntrance;
        if (want != expanded)
        {
            expanded = want; expandAge = 0f; pausedRoot.gameObject.SetActive(want); pausePlaced = false;
            Refresh();
            if (want) { RefreshPausedRows(); foreach (var slot in slots) slot.root.gameObject.SetActive(false); }
        }
        if (!expanded) return;
        if (pauseVersion != ShotAtoms.Version || pauseSquadVersion != DroneSquad.Version) RefreshPausedRows();
        foreach (var slot in slots) if (slot.root.gameObject.activeSelf) slot.root.gameObject.SetActive(false);
        expandAge += Mathf.Max(0f, dt); PlacePausedRows();
        for (int row = 0; row < DroneSquad.Slots; row++)
        {
            float alpha = Mathf.Clamp01((expandAge - row * .08f) / .12f);
            if (!Mathf.Approximately(pausedRows[row].alpha, alpha)) pausedRows[row].alpha = alpha;
        }
    }
}
