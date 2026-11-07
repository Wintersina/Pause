using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Leaderboard Settings modal, opened from the Options screen (LeaderboardSettingsOptions).
//
//   SYNC NOW          sends this device's best scores to Play Games / Game Center, and to the shared board while
//                     sharing is on (LeaderboardSync.Now); then says "Synced", "Offline, will retry" or "Sign in to sync".
//   SHARE SCORES      the cross-platform sharing switch (SharedLeaderboardService.SharingOn, on by default, persisted).
//                     Off stops uploads; it deletes nothing.
//   DELETE MY SHARED SCORES   a separate button with an in-game confirm step (no OS dialog); then says what happened.
//   One line on what is shared (display name and scores) and the privacy page link.
//
// Built at runtime on its own overlay canvas like LeaderboardPanel and in the same look (it reuses that panel's layout
// and building blocks). The middle scrolls on a short screen; BACK is fixed at the bottom. All type is 16 units or
// more, every button is a 100-unit target, and the BackNavigator back button closes the modal.
public class LeaderboardSettingsPanel : MonoBehaviour
{
    public enum DeleteStage { Idle, Confirm, Deleting }

    // Texts the tests and the player see.
    public const string Title = "LEADERBOARD SETTINGS";
    public const string WhatIsShared = "Sharing posts your display name and scores (score, star dust, worlds, ship) to a public board shared by Android and iOS players.";
    public const string OffNote = "Turning sharing off stops uploads. It does not delete what you already shared.";
    public const string SyncedText = "Synced";
    public const string OfflineText = "Offline, will retry";
    public const string SignInText = "Sign in to sync";
    public const string SyncingText = "Syncing...";
    public const string ConfirmText = "Delete your shared scores? This can't be undone.";
    public const string DeletedText = "Deleted. Sharing is now off.";
    public const string DeletedSignedOutText = "Deleted. Sharing is now off. You were signed out of the shared board.";
    public const string DeleteFailedText = "Couldn't delete. Sign in, check your connection and try again.";
    public const string UnavailableText = "The shared board isn't available on this device yet. Nothing was shared.";
    public const string PrivacyCaption = "PRIVACY POLICY";

    public static string ShareCaption(bool on) { return on ? "SHARE SCORES: ON" : "SHARE SCORES: OFF"; }

    // How long a sync may stay unconfirmed before it is reported as offline (the queue keeps retrying on its own).
    public const float SyncWaitSeconds = 12f;
    public static Func<float> Clock = () => Time.unscaledTime;

    const float ContentTop = 150f, ContentGap = 12f, ButtonH = 100f, BottomGap = 14f;

    public static LeaderboardSettingsPanel Current { get; private set; }
    public static bool IsOpen { get { return Current != null; } }

    // ---- public surface (tests read these) ----
    public Button SyncButton { get; private set; }
    public Button ShareButton { get; private set; }
    public Button DeleteButton { get; private set; }    // idle stage
    public Button ConfirmButton { get; private set; }   // confirm stage: the destructive one
    public Button CancelButton { get; private set; }
    public Button PrivacyButton { get; private set; }
    public Button CloseButton { get; private set; }
    public string SyncText { get; private set; }
    public string DeleteText { get; private set; }      // the outcome line under the delete block
    public DeleteStage Stage { get; private set; }
    public bool Syncing { get; private set; }
    public bool Scrolls { get { return scroll != null && scroll.enabled; } }
    public LeaderboardPanel.Layout AppliedLayout { get; private set; }
    public RectTransform PanelRoot { get { return panelRoot; } }

    Font font;
    Canvas canvas;
    CanvasScaler scaler;
    RectTransform safeRoot, panelRoot, viewport, content;
    ScrollRect scroll;
    float panelW = LeaderboardPanel.DesignWidth, panelH = LeaderboardPanel.DesignHeight;
    float syncStartedAt;
    bool screenOverridden;
    Vector2 screenSize;
    Rect safeArea;

    float Inner { get { return panelW - (LeaderboardPanel.DesignWidth - 640f); } }
    float Drop { get { return LeaderboardPanel.DesignHeight - panelH; } }

    // ---- open / close ----

    public static LeaderboardSettingsPanel Open(Font font = null)
    {
        return Open(font, new Vector2(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.SafeArea, false);
    }

    // Tests pass an explicit screen and safe area.
    public static LeaderboardSettingsPanel Open(Font font, Vector2 screen, Rect safe, bool overrideScreen = true)
    {
        Close();
        var go = new GameObject("~LeaderboardSettings", typeof(RectTransform));
        var panel = go.AddComponent<LeaderboardSettingsPanel>();
        panel.font = font != null ? font : DockArt.FindSceneFont();
        panel.screenOverridden = overrideScreen;
        panel.screenSize = screen;
        panel.safeArea = safe;
        panel.Build();
        Current = panel;
        BackNavigator.Register(panel, panel.OnBackPressed);
        return panel;
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

    bool OnBackPressed()
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

    // ---- build ----

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        gameObject.AddComponent<GraphicRaycaster>();

        // Full-screen dim; it also swallows taps meant for the Options buttons behind the modal.
        var dim = LeaderboardPanel.Child(transform, "Dim");
        LeaderboardPanel.Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = AkiraPalette.WithAlpha(AkiraPalette.Night0, .88f);

        safeRoot = LeaderboardPanel.Child(transform, "SafeArea");
        panelRoot = LeaderboardPanel.Child(safeRoot, "Panel");
        panelRoot.anchorMin = panelRoot.anchorMax = panelRoot.pivot = new Vector2(.5f, .5f);
        ApplyLayout(false);
        BuildPanel();
    }

    void BuildPanel()
    {
        panelRoot.sizeDelta = new Vector2(panelW, panelH);
        var frame = CelShape.Add(LeaderboardPanel.Fill(panelRoot, "Frame").gameObject, CelShape.Kind.Chamfer,
                                 AkiraPalette.WithAlpha(AkiraPalette.Night1, .97f), 5f);
        frame.cut = 30f;
        frame.raycastTarget = true;
        var line = CelShape.Add(LeaderboardPanel.Place(panelRoot, "PanelLine", 0f, 10f, panelW - 20f, panelH - 20f).gameObject,
                                CelShape.Kind.Chamfer, Color.clear, 1.5f);
        line.cut = 25f;
        line.hollow = true;
        line.ink = AkiraPalette.Hairline;

        // Title slab, as the leaderboard's.
        float inner = Inner;
        var slabRect = LeaderboardPanel.Place(panelRoot, "TitleSlab", -6f, 34f, inner + 30f, 96f);
        var slab = CelShape.Add(slabRect.gameObject, CelShape.Kind.Slab, AkiraPalette.Red, 4f).Shadow(AkiraPalette.Ink, new Vector2(8f, -8f));
        slab.slant = 24f;
        var title = LeaderboardPanel.Child(slabRect, "Title");
        LeaderboardPanel.Stretch(title, 36f);
        LeaderboardPanel.LabelWith(font, title, Title, 44, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3.5f, 24);

        // The scrolling middle.
        float viewH = Mathf.Max(100f, LeaderboardPanel.ButtonsTopUnits - Drop - BottomGap - ContentTop);
        viewport = LeaderboardPanel.Place(panelRoot, "Content", 0f, ContentTop, inner, viewH);
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        content = LeaderboardPanel.Child(viewport, "Rows");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.enabled = false;

        CloseButton = LeaderboardPanel.ButtonWith(font, panelRoot, "Back", 0f, LeaderboardPanel.ButtonsTopUnits - Drop, 420f, ButtonH,
                                                  "BACK", AkiraPalette.Red, Close);
        BuildContent();
    }

    // The controls, top to bottom. Rebuilt whenever a state changes (sync result, toggle, delete stage).
    void BuildContent()
    {
        float keep = content.anchoredPosition.y;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
        float w = Inner;
        float y = 0f;
        bool sharing = SharedLeaderboardService.SharingOn;

        AddText(ref y, "WhatIsShared", WhatIsShared, 20, 112f, AkiraPalette.Muted);

        SyncButton = AddButton(ref y, "SyncNow", "SYNC NOW", AkiraPalette.Cyan, OnSyncPressed, true);
        if (!string.IsNullOrEmpty(SyncText))
        {
            bool good = SyncText == SyncedText;
            AddText(ref y, "SyncResult", SyncText, 24, 40f, good ? AkiraPalette.Cyan : AkiraPalette.Amber, TextAnchor.MiddleCenter);
        }

        y += 8f;
        ShareButton = AddButton(ref y, "Share", ShareCaption(sharing), sharing ? AkiraPalette.Cyan : AkiraPalette.Muted, OnSharePressed, false);
        AddText(ref y, "OffNote", OffNote, 20, 88f, AkiraPalette.Muted);

        y += 8f;
        DeleteButton = ConfirmButton = CancelButton = null;
        if (Stage == DeleteStage.Idle)
        {
            DeleteButton = AddButton(ref y, "Delete", "DELETE MY SHARED SCORES", AkiraPalette.Red, OnDeletePressed, false);
        }
        else if (Stage == DeleteStage.Confirm)
        {
            AddText(ref y, "ConfirmText", ConfirmText, 22, 72f, AkiraPalette.Bone, TextAnchor.MiddleCenter);
            float half = (w - 16f) * .5f;
            CancelButton = AddButton(content, "Cancel", -half * .5f - 8f, y, half, "CANCEL", AkiraPalette.Cyan, OnCancelPressed, false);
            ConfirmButton = AddButton(content, "ConfirmDelete", half * .5f + 8f, y, half, "DELETE", AkiraPalette.Red, OnConfirmPressed, true);
            y += ButtonH + ContentGap;
        }
        else AddText(ref y, "Deleting", "Deleting...", 24, 40f, AkiraPalette.Amber, TextAnchor.MiddleCenter);
        if (!string.IsNullOrEmpty(DeleteText))
            AddText(ref y, "DeleteResult", DeleteText, 20, 72f, AkiraPalette.Amber, TextAnchor.MiddleCenter);

        y += 8f;
        PrivacyButton = AddButton(ref y, "Privacy", PrivacyCaption, AkiraPalette.Muted, OnPrivacyPressed, false);

        content.sizeDelta = new Vector2(0f, y);
        bool scrolls = y > viewport.rect.height + .5f;
        scroll.enabled = scrolls;
        viewport.GetComponent<Image>().raycastTarget = scrolls;
        content.anchoredPosition = new Vector2(0f, scrolls ? Mathf.Clamp(keep, 0f, y - viewport.rect.height) : 0f);
    }

    // A text block at the cursor.
    void AddText(ref float y, string name, string text, int size, float height, Color color, TextAnchor align = TextAnchor.UpperLeft)
    {
        var rt = LeaderboardPanel.Place(content, name, 0f, y, Inner, height);
        var t = LeaderboardPanel.LabelWith(font, rt, text, size, color, align, 2f, 16);
        t.fontStyle = FontStyle.Bold;
        y += height + ContentGap;
    }

    // A full-width 100-unit button at the cursor.
    Button AddButton(ref float y, string name, string caption, Color accent, UnityEngine.Events.UnityAction onClick, bool filled)
    {
        var b = AddButton(content, name, 0f, y, Inner, caption, accent, onClick, filled);
        y += ButtonH + ContentGap;
        return b;
    }

    Button AddButton(RectTransform parent, string name, float x, float top, float width, string caption, Color accent,
                  UnityEngine.Events.UnityAction onClick, bool filled)
    {
        return LeaderboardPanel.ButtonWith(font, parent, name, x, top, width, ButtonH, caption, accent, onClick, filled);
    }

    // ---- actions ----

    void OnSyncPressed()
    {
        if (Syncing) return;
        var native = LeaderboardService.Instance;
        var sharedService = SharedLeaderboardService.Instance;
        bool nativeIn = native != null && native.Platform.IsAvailable && native.SignedIn;
        bool sharedIn = sharedService != null && sharedService.Enabled && sharedService.Backend.IsSignedIn;
        if (!nativeIn && !sharedIn)
        {
            SyncText = SignInText;
            BuildContent();
            return;
        }
        LeaderboardSync.Now();
        Syncing = true;
        syncStartedAt = Clock();
        SyncText = SyncingText;
        CheckSync();
        BuildContent();
    }

    // The queues are empty once everything sent has been confirmed.
    static bool SyncPending()
    {
        var sharedService = SharedLeaderboardService.Instance;
        return LeaderboardService.ScoreTable.Load(LeaderboardService.PendingKey).entries.Count > 0
            || (sharedService != null && sharedService.HasPending);
    }

    void CheckSync()
    {
        if (!Syncing) return;
        if (!SyncPending()) { Syncing = false; SyncText = SyncedText; }
        else if (Clock() - syncStartedAt >= SyncWaitSeconds) { Syncing = false; SyncText = OfflineText; }
    }

    void OnSharePressed()
    {
        SharedLeaderboardService.SharingOn = !SharedLeaderboardService.SharingOn;
        PrefsSaver.SaveNow();
        BuildContent();
    }

    void OnDeletePressed()
    {
        Stage = DeleteStage.Confirm;
        DeleteText = null;
        BuildContent();
    }

    void OnCancelPressed()
    {
        Stage = DeleteStage.Idle;
        BuildContent();
    }

    void OnConfirmPressed()
    {
        if (Stage != DeleteStage.Confirm) return;
        var sharedService = SharedLeaderboardService.Instance;
        Stage = DeleteStage.Deleting;
        BuildContent();
        sharedService.DeleteMyScores(report =>
        {
            if (this == null) return;
            Stage = DeleteStage.Idle;
            DeleteText = !sharedService.Backend.IsAvailable ? UnavailableText
                       : !report.docsDeleted ? DeleteFailedText
                       : report.authDeleted ? DeletedText : DeletedSignedOutText;
            BuildContent();
        });
    }

    void OnPrivacyPressed() { ExternalLinks.Open(ExternalLinks.PrivacyPolicy); }

    // ---- per frame ----

    void Update()
    {
        if (Syncing)
        {
            bool was = Syncing;
            CheckSync();
            if (was && !Syncing) BuildContent();
        }
        if (!screenOverridden && (ScreenInfo.Width != screenSize.x || ScreenInfo.Height != screenSize.y
                                  || ScreenInfo.SafeArea != safeArea))
        {
            screenSize = new Vector2(ScreenInfo.Width, ScreenInfo.Height);
            safeArea = ScreenInfo.SafeArea;
            ApplyLayout(true);
        }
    }

    void ApplyLayout(bool rebuild)
    {
        var layout = LeaderboardPanel.ComputeLayout(screenSize, safeArea);
        AppliedLayout = layout;
        bool resized = layout.panelSize.x != panelW || layout.panelSize.y != panelH;
        panelW = layout.panelSize.x;
        panelH = layout.panelSize.y;
        scaler.scaleFactor = layout.scale;
        Vector2 screen = new Vector2(Mathf.Max(1f, screenSize.x), Mathf.Max(1f, screenSize.y));
        safeRoot.anchorMin = new Vector2(layout.safe.xMin / screen.x, layout.safe.yMin / screen.y);
        safeRoot.anchorMax = new Vector2(layout.safe.xMax / screen.x, layout.safe.yMax / screen.y);
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        if (rebuild && resized)
        {
            for (int i = panelRoot.childCount - 1; i >= 0; i--)
            {
                var child = panelRoot.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            BuildPanel();
        }
    }
}
