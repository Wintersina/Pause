using UnityEngine;
using UnityEngine.UI;

// The Account row's small modal: the SIGN OUT confirmation, then what the
// store itself still knows (apps can't sign out of Play Games / Game Center)
// with a button to the store's own settings where one exists.
//
// Its own overlay canvas above the Options screen and the leaderboard panel;
// a dim swallows taps behind it. Back (Android back / Escape) closes it as
// CANCEL (a BackNavigator layer). Look: the leaderboard panel's -- night
// plate, red title slab, chamfered cel buttons.
public class AccountDialog : MonoBehaviour
{
    public enum Kind { ConfirmSignOut, SignedOut }

    public const float Width = 640f, Height = 560f;

    public static AccountDialog Current { get; private set; }

    public Kind Shown { get; private set; }
    public string TitleText { get; private set; }
    public string BodyText { get { return body != null ? body.text : null; } }
    public Button PrimaryButton { get; private set; }    // SIGN OUT / OPEN PLAY GAMES
    public Button SecondaryButton { get; private set; }  // CANCEL / OK

    Font font;
    RectTransform panel;
    Text body;

    public static bool IsOpen { get { return Current != null; } }

    public static AccountDialog ConfirmSignOut()
    {
        var d = Open(Kind.ConfirmSignOut);
        string store = AccountLink.StoreName;
        d.Build("SIGN OUT?",
                "Pause will stop using your " + store + " account on this device: no cloud saves, achievements or "
                + "leaderboard posts until you sign in again.\n\nYour progress stays on this device.",
                "SIGN OUT", d.ConfirmPressed, "CANCEL", Close);
        return d;
    }

    public static AccountDialog SignedOut()
    {
        var d = Open(Kind.SignedOut);
        bool canOpen = AccountSettingsLink.CanOpen || AccountSettingsLink.OpenOverride != null;
        d.Build("SIGNED OUT OF PAUSE",
                "Pause won't use your " + AccountLink.StoreName + " account until you sign in again. "
                + "Your progress stays on this device.\n\n" + AccountSettingsLink.ManageInstructions,
                canOpen ? AccountSettingsLink.OpenLabel : null, canOpen ? (UnityEngine.Events.UnityAction)d.OpenSettings : null,
                "OK", Close);
        return d;
    }

    static AccountDialog Open(Kind kind)
    {
        Close();
        var canvas = CodexUi.NewOverlayCanvas("~AccountDialog", 700, true);
        var d = canvas.gameObject.AddComponent<AccountDialog>();
        d.Shown = kind;
        d.font = DockArt.FindSceneFont();
        Current = d;
        BackNavigator.Register(d, d.OnBack);
        return d;
    }

    public static void Close()
    {
        if (Current == null) return;
        var go = Current.gameObject;
        BackNavigator.Unregister(Current);
        Current = null;
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    bool OnBack()
    {
        if (Current != this) return false;
        Close();
        return true;
    }

    void OnDestroy()
    {
        BackNavigator.Unregister(this);
        if (Current == this) Current = null;
    }

    void ConfirmPressed()
    {
        AccountLink.Disconnect();
        SignedOut();
    }

    void OpenSettings()
    {
        if (AccountSettingsLink.Open()) return;
        // Nothing could be opened: say where to go instead.
        if (body != null)
            body.text = "Couldn't open it from here.\n\n" + AccountSettingsLink.ManageInstructions;
        if (PrimaryButton != null) PrimaryButton.gameObject.SetActive(false);
        CenterSecondary();
    }

    // ---- build ----

    void Build(string title, string text, string primary, UnityEngine.Events.UnityAction onPrimary,
               string secondary, UnityEngine.Events.UnityAction onSecondary)
    {
        TitleText = title;
        var dim = AccountUi.Child(transform, "Dim");
        AccountUi.Stretch(dim);
        dim.offsetMin = dim.offsetMax = Vector2.zero;
        var dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.color = AkiraPalette.WithAlpha(AkiraPalette.Night0, .85f);

        panel = AccountUi.Child(transform, "Panel");
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(Width, Height);
        var frame = CelShape.Add(panel.gameObject, CelShape.Kind.Chamfer,
                                 AkiraPalette.WithAlpha(AkiraPalette.Night1, .98f), 5f)
                            .Shadow(AkiraPalette.Ink, new Vector2(10f, -10f));
        frame.cut = 28f;
        frame.raycastTarget = true;

        var slabRect = AccountUi.Place(panel, "TitleSlab", -10f, 28f, Width - 40f, 86f);
        var slab = CelShape.Add(slabRect.gameObject, CelShape.Kind.Slab, AkiraPalette.Red, 4f)
                           .Shadow(AkiraPalette.Ink, new Vector2(7f, -7f));
        slab.slant = 22f;
        var titleRect = AccountUi.Child(slabRect, "Title");
        AccountUi.Stretch(titleRect, 24f);
        AccountUi.Label(titleRect, font, title, 40, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3.5f);

        var bodyRect = AccountUi.Place(panel, "Body", 40f, 140f, Width - 80f, 270f);
        body = AccountUi.Label(bodyRect, font, text, 24, AkiraPalette.Bone, TextAnchor.UpperLeft, 0f, false);
        body.resizeTextMinSize = 15;
        body.lineSpacing = 1.1f;

        const float top = Height - 128f, h = 92f;
        float half = (Width - 80f - 20f) * .5f;
        SecondaryButton = AccountUi.MakeButton(panel, "Secondary", font, 40f, top, half, h, secondary,
                                               AkiraPalette.Cyan, false, onSecondary, 28);
        if (!string.IsNullOrEmpty(primary))
            PrimaryButton = AccountUi.MakeButton(panel, "Primary", font, 40f + half + 20f, top, half, h, primary,
                                                 AkiraPalette.Red, true, onPrimary, 26);
        else CenterSecondary();
    }

    void CenterSecondary()
    {
        if (SecondaryButton == null) return;
        var rt = (RectTransform)SecondaryButton.transform;
        rt.anchoredPosition = new Vector2((Width - rt.sizeDelta.x) * .5f, rt.anchoredPosition.y);
    }
}
