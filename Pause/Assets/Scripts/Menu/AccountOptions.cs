using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Account row of the Options screen (leaderboardS3): who is signed in to
// the store account (Google Play Games / Game Center), and the one obvious
// place to sign in -- or out of Pause (see AccountLink for what SIGN OUT can
// and can't do).
//
//   signed out    [?]  NOT SIGNED IN / progress is on this device only
//                 [ SIGN IN WITH GOOGLE PLAY GAMES ]   (SIGN IN TO GAME CENTER)
//   signing in    [?]  SIGNING IN...                   button disabled
//   signed in     [S]  SINA / SIGNED IN               [ SIGN OUT ]
//   failed        [!]  SIGN-IN FAILED / short reason, plus a one-line hint
//                      for set-up problems ("Play Games isn't set up for
//                      this build yet") so the button doesn't look broken
//   signed out    [?]  SIGNED OUT / progress is on this device only
//   in Pause
//   developer mode adds a details line: the last sign-in status code (and on
//   Android the APK's signing-certificate SHA-1).
//
// Built at runtime on the scene's own canvas, in the free space above the
// button stack (and the developer rows); it scales down if a short screen
// leaves less room, and never leaves the safe area. Only shown where a store
// account exists (Android, iOS).
public class AccountOptions : MonoBehaviour
{
    public const float CardWidth = 640f;
    public const float BaseHeight = 300f, DevDetailsHeight = 50f;
    public const float CanvasUnitsWide = 800f;   // the Options canvas matches width at 800
    public const float TopMargin = 24f, Gap = 28f, Lift = 40f, MinScale = .55f;

    public static AccountOptions Current { get; private set; }

    // ---- tests read these ----

    public AccountLink.Status ShownStatus { get; private set; }
    public string NameText { get { return nameText != null ? nameText.text : null; } }
    public string StatusText { get { return statusText != null ? statusText.text : null; } }
    public string HintText { get { return hintText != null && hintText.gameObject.activeSelf ? hintText.text : null; } }
    public string DetailsText { get { return detailsText != null && detailsText.gameObject.activeSelf ? detailsText.text : null; } }
    public string AvatarText { get { return avatarText != null ? avatarText.text : null; } }
    public Button ActionButton { get { return action; } }
    public string ActionCaption { get { return AccountUi.Caption(action); } }
    public RectTransform Card { get { return card; } }
    public CardLayout AppliedLayout { get; private set; }

    Font font;
    RectTransform canvasRect, card, detailsRow;
    CelShape avatarPlate;
    Text platformText, nameText, statusText, hintText, detailsText, avatarText;
    Button action;
    bool devShown;
    bool overridden;
    Vector2 screenSize;
    Rect safeArea;
    float rowsTop = float.NaN;

    // ---- bootstrap ----

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && AccountLink.Available && FindFirstObjectByType<AccountOptions>() == null)
            new GameObject("~AccountOptions").AddComponent<AccountOptions>();
    }

    // The Options canvas: the one holding the LeaderBoard button.
    public static RectTransform FindOptionsCanvas()
    {
        var anchor = SceneUtil.FindAny("pullUpLeaderBoard");
        return anchor != null ? anchor.transform.parent as RectTransform : null;
    }

    void Start()
    {
        if (card == null) Build(new Vector2(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.SafeArea, false);
    }

    // Tests pass an explicit screen and safe area.
    public void Build(Vector2 screen, Rect safe, bool overrideScreen = true)
    {
        canvasRect = FindOptionsCanvas();
        if (canvasRect == null) return;
        Current = this;
        overridden = overrideScreen;
        screenSize = screen;
        safeArea = safe;
        font = DockArt.FindSceneFont();
        BuildCard();
        AccountLink.Changed -= Refresh;
        AccountLink.Changed += Refresh;
        Refresh();
        Relayout();
    }

    void OnDestroy()
    {
        AccountLink.Changed -= Refresh;
        if (Current == this) Current = null;
        if (card != null)
        {
            if (Application.isPlaying) Destroy(card.gameObject);
            else DestroyImmediate(card.gameObject);
        }
    }

    // ---- build ----

    void BuildCard()
    {
        card = AccountUi.Child(canvasRect, "AccountRow");
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.sizeDelta = new Vector2(CardWidth, BaseHeight);

        var plate = CelShape.Add(card.gameObject, CelShape.Kind.Chamfer,
                                 AkiraPalette.WithAlpha(AkiraPalette.Night1, .96f), 4.5f)
                            .Shadow(AkiraPalette.Ink, new Vector2(8f, -8f));
        plate.cut = 24f;
        plate.raycastTarget = true;   // taps on the card don't reach the stars behind it

        // Tag slab: ACCOUNT, Kaneda red, with the platform name beside it.
        var tag = AccountUi.Place(card, "Tag", -8f, -14f, 230f, 50f);
        var slab = CelShape.Add(tag.gameObject, CelShape.Kind.Slab, AkiraPalette.Red, 3.5f)
                           .Shadow(AkiraPalette.Ink, new Vector2(5f, -5f));
        slab.slant = 16f;
        var tagLabel = AccountUi.Child(tag, "Label");
        AccountUi.Stretch(tagLabel, 16f);
        AccountUi.Label(tagLabel, font, "ACCOUNT", 26, AkiraPalette.Bone, TextAnchor.MiddleCenter, 2.5f);

        platformText = AccountUi.Label(AccountUi.Place(card, "Platform", 240f, 10f, CardWidth - 264f, 32f), font,
                                       AccountLink.StoreName.ToUpperInvariant(), 18, AkiraPalette.Muted,
                                       TextAnchor.MiddleRight, 1.5f);

        // Avatar: a chamfered square with the player's initial.
        var avatar = AccountUi.Place(card, "Avatar", 24f, 62f, 96f, 96f);
        avatarPlate = CelShape.Add(avatar.gameObject, CelShape.Kind.Chamfer, AkiraPalette.Indigo0, 4f)
                              .Shadow(AkiraPalette.Ink, new Vector2(5f, -5f));
        avatarPlate.cut = 14f;
        var initial = AccountUi.Child(avatar, "Initial");
        AccountUi.Stretch(initial);
        avatarText = AccountUi.Label(initial, font, "?", 54, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3f, false);

        nameText = AccountUi.Label(AccountUi.Place(card, "Name", 140f, 62f, CardWidth - 164f, 46f), font,
                                   "", 32, AkiraPalette.Bone, TextAnchor.MiddleLeft, 3f);
        statusText = AccountUi.Label(AccountUi.Place(card, "Status", 140f, 108f, CardWidth - 164f, 30f), font,
                                     "", 20, AkiraPalette.Cyan, TextAnchor.MiddleLeft, 1.5f);
        hintText = AccountUi.Label(AccountUi.Place(card, "Hint", 140f, 138f, CardWidth - 164f, 26f), font,
                                   "", 17, AkiraPalette.Amber, TextAnchor.MiddleLeft, 1.5f, false);

        action = AccountUi.MakeButton(card, "Action", font, 24f, 182f, CardWidth - 48f, 88f, "",
                                      AkiraPalette.Red, true, OnAction, 26);
        // a finger-sized hit area (the 88-unit button is ~35 dp on a phone):
        // up over the status line, down to the developer details row
        if (action.targetGraphic != null) action.targetGraphic.raycastPadding = new Vector4(0f, -12f, 0f, -16f);

        detailsRow = AccountUi.Place(card, "Details", 24f, 282f, CardWidth - 48f, DevDetailsHeight - 6f);
        detailsText = AccountUi.Label(detailsRow, font, "", 14, AkiraPalette.Amber, TextAnchor.UpperLeft, 0f, false);
        detailsText.resizeTextMinSize = 9;
        detailsRow.gameObject.SetActive(false);
    }

    // ---- state ----

    public void Refresh()
    {
        if (card == null) return;
        var status = AccountLink.Current;
        ShownStatus = status;
        var report = AccountLink.LastReport;
        string store = AccountLink.StoreName;
        platformText.text = store.ToUpperInvariant();
        string hint = null;
        bool signedIn = status == AccountLink.Status.SignedIn;

        switch (status)
        {
            case AccountLink.Status.SignedIn:
                string name = AccountLink.DisplayName;
                nameText.text = name.ToUpperInvariant();
                statusText.text = "SIGNED IN";
                statusText.color = AkiraPalette.Cyan;
                avatarText.text = AccountLink.Initial(name);
                AccountUi.SetButton(action, "SIGN OUT", AkiraPalette.Red, false, true);
                break;
            case AccountLink.Status.SigningIn:
                nameText.text = "SIGNING IN...";
                statusText.text = "Waiting for " + store;
                statusText.color = AkiraPalette.Muted;
                avatarText.text = "?";
                AccountUi.SetButton(action, "SIGNING IN...", AkiraPalette.Indigo1, true, false);
                break;
            case AccountLink.Status.Failed:
                nameText.text = "SIGN-IN FAILED";
                statusText.text = AccountLink.FailureReason(report);
                statusText.color = AkiraPalette.RedHi;
                avatarText.text = "!";
                hint = AccountLink.FailureHint(report);
                AccountUi.SetButton(action, AccountLink.SignInLabel, AkiraPalette.Red, true, true);
                break;
            case AccountLink.Status.Disconnected:
                nameText.text = "SIGNED OUT";
                statusText.text = "Progress is saved on this device only.";
                statusText.color = AkiraPalette.Muted;
                avatarText.text = "?";
                AccountUi.SetButton(action, AccountLink.SignInLabel, AkiraPalette.Red, true, true);
                break;
            default:
                nameText.text = "NOT SIGNED IN";
                statusText.text = "Progress is saved on this device only.";
                statusText.color = AkiraPalette.Muted;
                avatarText.text = "?";
                AccountUi.SetButton(action, AccountLink.SignInLabel, AkiraPalette.Red, true, true);
                break;
        }
        avatarPlate.SetFill(signedIn ? AkiraPalette.Red : status == AccountLink.Status.Failed
                            ? AkiraPalette.RedShadow : AkiraPalette.Indigo0);
        avatarText.color = signedIn ? AkiraPalette.Bone : AkiraPalette.Muted;

        hintText.text = hint ?? "";
        hintText.gameObject.SetActive(!string.IsNullOrEmpty(hint));

        devShown = DeveloperUnlocks.Enabled;
        detailsRow.gameObject.SetActive(devShown);
        if (devShown) detailsText.text = AccountLink.DetailsLine;
        card.sizeDelta = new Vector2(CardWidth, Height(devShown));
        Relayout();
    }

    public static float Height(bool dev) { return BaseHeight + (dev ? DevDetailsHeight : 0f); }

    void OnAction()
    {
        switch (AccountLink.Current)
        {
            case AccountLink.Status.SigningIn:
                return;
            case AccountLink.Status.SignedIn:
                AccountDialog.ConfirmSignOut();
                return;
            default:
                AccountLink.SignIn();
                return;
        }
    }

    // ---- layout ----

    public struct CardLayout
    {
        public float centerY;      // canvas units, from the canvas centre
        public float scale;
        public float canvasHeight; // canvas units
        public float safeTop;      // highest y the card may reach
        public float safeBottom;
        public float rowsTop;      // top of the highest button/row under the card
    }

    // The card sits just above the button stack (rowsTop) and below the
    // safe area's top edge; it shrinks when that gap is too small.
    // `unitsPerPixel`: the Options canvas's units per screen pixel (0: taken
    // as CanvasUnitsWide across the screen, as the scene authors it).
    public static CardLayout ComputeLayout(Vector2 screen, Rect safe, float rowsTop, float cardHeight, float unitsPerPixel = 0f)
    {
        if (screen.x <= 0f || screen.y <= 0f) screen = new Vector2(1080f, 1920f);
        if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, screen.x, screen.y);
        float units = unitsPerPixel > 0f ? unitsPerPixel : CanvasUnitsWide / screen.x;
        float height = screen.y * units;
        float safeTop = height * .5f - (screen.y - safe.yMax) * units - TopMargin;
        float safeBottom = -height * .5f + safe.yMin * units;
        float safeWidth = safe.width * units;
        float bottom = rowsTop + Gap;
        float room = safeTop - bottom;
        float scale = Mathf.Min(1f, room / cardHeight, safeWidth * .94f / (CardWidth + 8f));
        scale = Mathf.Max(MinScale, scale);
        float h = cardHeight * scale;
        float center = Mathf.Min(bottom + Lift * scale + h * .5f, safeTop - h * .5f);
        center = Mathf.Max(center, bottom + h * .5f);
        return new CardLayout
        {
            centerY = center, scale = scale, canvasHeight = height,
            safeTop = safeTop + TopMargin, safeBottom = safeBottom, rowsTop = rowsTop,
        };
    }

    // Top of the highest other control on the Options canvas (the LeaderBoard
    // / Tutorial / Back stack and DeveloperOptions' rows, shown or not, so the
    // card doesn't jump when developer mode is switched).
    public float RowsTop()
    {
        float top = float.NegativeInfinity;
        if (canvasRect == null) return 0f;
        for (int i = 0; i < canvasRect.childCount; i++)
        {
            var rt = canvasRect.GetChild(i) as RectTransform;
            if (rt == null || rt == card) continue;
            if (rt.GetComponentInChildren<Button>(true) == null) continue;
            if (rt.anchorMin != rt.anchorMax || rt.anchorMin != new Vector2(.5f, .5f)) continue;
            top = Mathf.Max(top, rt.anchoredPosition.y + rt.sizeDelta.y * (1f - rt.pivot.y));
        }
        return float.IsNegativeInfinity(top) ? 0f : top;
    }

    // Canvas units per screen pixel on `screen`, from the Options canvas's
    // scaler (UiScale's floor included): not always CanvasUnitsWide across
    // -- fewer on a phone small in points / dp, more on a tablet (Expand).
    public float UnitsPerPixel(Vector2 screen)
    {
        var root = canvasRect != null ? canvasRect.GetComponentInParent<Canvas>() : null;
        if (root == null || screen.x <= 0f) return 0f;
        root = root.rootCanvas;
        var scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 0f;
        // a scaler switched off (the screen-fit rig sets the scale itself)
        float s = scaler.enabled ? HudStyler.HudCanvasScale(root, scaler, screen) : root.scaleFactor;
        return s > 0f ? 1f / s : 0f;
    }

    public void Relayout()
    {
        if (card == null) return;
        rowsTop = RowsTop();
        var layout = ComputeLayout(screenSize, safeArea, rowsTop, Height(devShown), UnitsPerPixel(screenSize));
        AppliedLayout = layout;
        card.anchoredPosition = new Vector2(0f, layout.centerY);
        card.localScale = new Vector3(layout.scale, layout.scale, 1f);
    }

    void Update()
    {
        bool changed = false;
        if (!overridden && (ScreenInfo.Width != screenSize.x || ScreenInfo.Height != screenSize.y || ScreenInfo.SafeArea != safeArea))
        {
            screenSize = new Vector2(ScreenInfo.Width, ScreenInfo.Height);
            safeArea = ScreenInfo.SafeArea;
            changed = true;
        }
        if (DeveloperUnlocks.Enabled != devShown) { Refresh(); return; }
        if (changed || !Mathf.Approximately(RowsTop(), rowsTop)) Relayout();
    }
}
