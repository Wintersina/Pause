using UnityEditor.SceneManagement;
using UnityEngine;

// The ship popup is drawn at 85% of its original size (DockPopup.PopupScale)
// with its touch targets kept finger-sized. Original sizes are pinned here
// (300 x 126 locked, 300 x 214 owned, canvas units, plus the power preview on
// top) so a drift shows up.
public static class DockPopupScaleTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DPS] PASS  " : "[DPS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Master before this change: the card in canvas units.
    const float OldWidth = 300f, OldLockedHeight = 126f + DockPopup.PreviewHeight, OldOwnedHeight = 214f + DockPopup.PreviewHeight;

    // The power preview: every ship has frames, names and lines for both
    // kinds; it alternates on a timer until tapped; the card fits the smallest phone.
    static void PreviewChecks(DockPopup popup, Transform anchor)
    {
        bool frames = true, text = true;
        var cells = new Sprite[PowerPreview.Cells];
        foreach (int ship in ShipId.All)
            foreach (bool power in new[] { false, true })
            {
                frames &= PowerPreview.Fill(ship, power, cells);
                text &= PowerPreview.Name(ship, power).Length > 0 && PowerPreview.Line(ship, power).Length > 0;
            }
        bool plans = true;
        foreach (int ship in ShipId.All)
            foreach (bool power in new[] { false, true })
            {
                bool all = PowerPreview.Fill(ship, power, cells);
                int n = PowerPreview.CellCount(ship, power);
                for (int i = 0; i < n; i++) all &= cells[i] != null;
                foreach (int c in PowerPreview.Sequence(ship, power)) all &= c < n;
                plans &= all && n >= 16 && n <= PowerPreview.Cells;
            }
        Check("every ship's dock preview plan finds all its cells (the nuke's shockwave and mushroom, the sentries' bolt, the nova's ice...)", plans);
        bool flightArt = true;
        foreach (int ship in ShipId.All)
        {
            string strip = ShipFxArt.PowerStripName(ShipLoadoutTable.For(ship).power);
            if (strip == null) continue;
            var own = ShipFxArt.Strip(ship, strip);
            PowerPreview.Fill(ship, true, cells);
            flightArt &= own != null && cells[0] != null && cells[0].texture == own[0].texture;
        }
        Check("each power's dock preview plays the strip the flight draws (Turtle's shell dome, not its old hex strip)", flightArt);
        Check("Paragon's attack preview ends on the shockwave and mushroom", PowerPreview.CellCount(16, false) == 48);
        Check("every ship (" + ShipId.Count + ") resolves non-null attack and power preview frames", frames && ShipId.Count == shopingShips.RosterTotal - 1);
        bool fit = true;
        foreach (int ship in ShipId.All)
            foreach (bool power in new[] { false, true })
            {
                PowerPreview.Fill(ship, power, cells);
                var b = PowerPreview.VisibleBounds(ship, power, cells);
                Vector2 size, at;
                PowerPreview.Fit(ship, power, cells, 62f, 276f, out size, out at);
                fit &= b.width > 0f && b.height > 0f && size.x > 0f && size.x <= 8f * 56f + .01f && b.height * size.x <= 62f * .86f + .01f;
            }
        bool v3 = true;
        foreach (int ship in new[] { 3, 10, 11, 12, 13, 14 })
        {
            v3 &= PowerPreview.Fill(ship, false, cells);
            for (int i = 0; i < PowerPreview.CellCount(ship, false); i++) v3 &= cells[i] != null;
        }
        var eyeBurst = new Sprite[PowerPreview.Cells];
        PowerPreview.Fill(10, false, eyeBurst);
        v3 &= PowerPreview.BurstStrip(10) == "mark" && eyeBurst[ShipFxArt.LoopFrames] == ShipFxArt.Extra(10, "mark", true, 0);
        Check("the six v3 ships' dock previews find all 16 cells (Paranoid's burst is its mark strip)", v3);
        Check("Solar Fang's wave and Dove's gust rise through the frame",
              PowerPreview.Rises(3) && PowerPreview.Rises(14) && !PowerPreview.Rises(12) &&
              PowerPreview.RiseOffset(0, 62f) < 0f && PowerPreview.RiseOffset(16, 62f) > 0f);
        Check("every ship's preview art fits the panel (visible box <= 85% of its height, zoom <= 3x, the ion laser 8x)", fit);
        Check("every ship has a preview name and line for both kinds", text);
        foreach (bool power in new[] { false, true })
        {
            var seq = PowerPreview.Sequence(power);
            bool burst = false, loop = false;
            foreach (int c in seq) { loop |= c >= 0 && c < 8; burst |= c >= 8; }
            Check((power ? "power" : "ultimate") + " sequence plays loop then burst frames", loop && burst && seq[0] == 0);
        }

        popup.safeView = new Rect(-50f, -50f, 100f, 100f);
        popup.SetDensity(.0132f);
        popup.Show(4, anchor, .3f, false, false, 3200f, 0f);
        Check("preview starts on the ultimate with its name and line",
              !popup.PreviewShowsPower && popup.PreviewName == PowerPreview.Name(4, false) && popup.PreviewLine == PowerPreview.Line(4, false));
        Check("the card lists both weapons and the trigger at once",
              popup.AttackNameText == PowerPreview.Name(4, false) && popup.AttackLineText == PowerPreview.Line(4, false) &&
              popup.PowerNameText == PowerPreview.Name(4, true) && popup.PowerLineText == PowerPreview.Line(4, true) &&
              popup.TriggerText.EndsWith(ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(4)).ToUpperInvariant()));
        Check("preview shows a frame while up", popup.PreviewSprite != null);
        popup.TapPreviewChip(true);
        Check("tapping POWER switches to the power and stops auto-alternating",
              popup.PreviewShowsPower && !popup.PreviewAutoAlternates && popup.PreviewName == PowerPreview.Name(4, true));
        popup.Show(5, anchor, .3f, false, false, 3200f, 0f);
        Check("a card kept open on another ship restarts the preview", !popup.PreviewShowsPower && popup.PreviewAutoAlternates);

        // the smallest supported phone: 1080 x 1920 (9:16), 420 dpi -> ~411 x 731 dp
        float wpp = 5.6f / 411f;
        popup.safeView = new Rect(-2.8f, -3.2f, 5.6f, 6.4f);
        popup.SetDensity(wpp);
        popup.Show(2, anchor, .3f, true, false, 0f, 0f);
        popup.ShowSkins(2, 0, 0f);
        Check("the tallest card (owned + preview) fits the safe view of a short phone",
              popup.WorldRect.height + popup.TailLength <= popup.safeView.height + .001f && popup.WorldRect.width <= popup.safeView.width + .001f);
        popup.safeView = new Rect(-50f, -50f, 100f, 100f);
        popup.SetDensity(.0132f);
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var anchor = new GameObject("~Anchor").transform;
        var popup = DockPopup.Create(null, font, null);
        popup.safeView = new Rect(-50f, -50f, 100f, 100f);   // roomy: no fit-shrink
        Check("the one scale constant is 0.85", Mathf.Approximately(DockPopup.PopupScale, .85f));
        Check("layout constants are the originals (width 300, locked 126, owned 214)",
              DockPopup.PanelWidth == OldWidth && DockPopup.BaseHeight == OldLockedHeight && DockPopup.OwnedHeight == OldOwnedHeight);
        Check("the preview is 171 units tall (art frame plus both weapons and the trigger)", DockPopup.PreviewHeight == 171f);
        PreviewChecks(popup, anchor);
        PlayerPrefs.SetString(ShipId.OwnedKey(2), "True");
        foreach (float wpp in new[] { .0066f, .0092f, .0132f, .0200f })   // world units per point: small to large phones
        {
            popup.SetDensity(wpp);
            popup.Show(5, anchor, .3f, false, false, 3200f, 0f);
            float w = popup.WorldWidth, h = popup.CurrentHeight;
            Check("wpp " + wpp + ": locked card is 0.85 x old (" + w.ToString("F3") + " x " + h.ToString("F3") + ")",
                  Mathf.Approximately(w, OldWidth * wpp * .85f) && Mathf.Approximately(h, OldLockedHeight * wpp * .85f));
            popup.Show(2, anchor, .3f, true, false, 0f, 0f);
            popup.ShowSkins(2, 0, 0f);
            Check("wpp " + wpp + ": owned card is 0.85 x old",
                  Mathf.Approximately(popup.WorldWidth, OldWidth * wpp * .85f) && Mathf.Approximately(popup.CurrentHeight, OldOwnedHeight * wpp * .85f));

            // touch targets in points: >= 48
            float unitPt = popup.Unit / wpp;
            var action = (RectTransform)popup.ActionButton.transform;
            float pad = popup.ActionButton.targetGraphic.raycastPadding.y;
            float actionH = (action.rect.height - pad - popup.ActionButton.targetGraphic.raycastPadding.w) * unitPt;
            Check("wpp " + wpp + ": action hit height " + actionH.ToString("F1") + " pt >= 48", actionH >= 47.9f);
            var closeG = popup.CloseButton.targetGraphic;
            float closeH = (((RectTransform)closeG.transform).rect.height - closeG.raycastPadding.y - closeG.raycastPadding.w) * unitPt;
            Check("wpp " + wpp + ": close hit " + closeH.ToString("F1") + " pt >= 48", closeH >= 47.9f);
            bool sw = true;
            foreach (var s in popup.swatches)
            {
                var r = s.hit.rectTransform.rect;
                sw &= r.width * unitPt >= 47.9f && r.height * unitPt >= 47.9f;
            }
            Check("wpp " + wpp + ": every swatch slot >= 48 x 48 pt", sw);
        }
        Object.DestroyImmediate(popup.gameObject);
        Object.DestroyImmediate(anchor.gameObject);
        Debug.Log("[DPS] failures: " + fails);
        return fails;
    }
}
