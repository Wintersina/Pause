using UnityEditor.SceneManagement;
using UnityEngine;

// The ship popup is drawn at 85% of its original size (DockPopup.PopupScale)
// with its touch targets kept finger-sized. Original sizes are pinned here
// (300 x 126 locked, 300 x 214 owned, canvas units) so a drift shows up.
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
    const float OldWidth = 300f, OldLockedHeight = 126f, OldOwnedHeight = 214f;

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
