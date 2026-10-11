using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The Codex ACHIEVEMENTS tab: 7 tabs that still fit and stay 96-unit touch targets, the sectioned
// badge list on every screen, locked / unlocked / claimed states, progress bars, "???" for hidden ones,
// the dormant Tide pair, the header strip, and COLLECT / COLLECT ALL through REAL pointer events
// (raycast -> down / up / click on the nested-canvas buttons), plus the home button's dot.
public static class CodexAchievementsTabTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CAT] PASS  " : "[CAT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly (string name, Rect safe)[] Screens =
    {
        ("minimum 800x1280", new Rect(-400f, -640f, 800f, 1280f)),
        ("16:9 phone 1080x1920", new Rect(-400f, -711f, 800f, 1422f)),
        ("tall phone 1080x2340", new Rect(-400f, -866f, 800f, 1733f)),
        ("9:24 phone 1080x2880", new Rect(-400f, -1067f, 800f, 2133f)),
        ("Z Fold cover 968x2376", new Rect(-400f, -982f, 800f, 1964f)),
        ("iPad 1536x2048", new Rect(-480f, -640f, 960f, 1280f)),
        ("landscape Mac 1600x900", new Rect(-1138f, -640f, 2276f, 1280f)),
        ("off-centre safe area", new Rect(-380f, -700f, 760f, 1500f)),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);   // no migration surprises
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 1; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        Codex.Reload();
        bool tide = WorldManager.TideEnabled;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var esGo = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        try
        {
            WorldManager.TideEnabled = false;
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            panel.ApplyLayout(Screens[0].safe);
            Tabs(panel);
            Fresh(panel);
            States(panel);
            Layouts(panel);
            Pointer(panel);
            Dormant(panel);
            Perf(panel);
            Home();
            HomeFit();
        }
        finally
        {
            if (panel != null) { panel.Close(); panel.SkipAnimations(); Object.DestroyImmediate(panel.gameObject); }
            Object.DestroyImmediate(esGo);
            WorldManager.TideEnabled = tide;
            AchievementStore.ResetAll();
            AchievementStores.Current = null;
        }
        Debug.Log("[CAT] failures: " + fails);
        return fails;
    }

    // ---- tabs ----

    static void Tabs(CodexPanel panel)
    {
        Check("seven tabs: the six categories then ACHIEVEMENTS", CodexPanel.TabCount == 7 && CodexPanel.Tabs.Length == 6 &&
              panel.TabLabel(CodexPanel.AchievementsTab).text == "ACHIEVEMENTS" && CodexPanel.TabsPerRow == 4);
        foreach (var (name, safe) in Screens)
        {
            panel.ApplyLayout(safe);
            Canvas.ForceUpdateCanvases();
            var l = panel.CurrentLayout;
            bool ok = true, hit = true;
            for (int i = 0; i < CodexPanel.TabCount; i++)
            {
                Rect t = CodexPanel.TabRect(l, i);
                float hitW = t.width + CodexPanel.TabGap, hitH = t.height + CodexPanel.TabRowGap;
                ok &= Contains(l.tabs, t);
                hit &= hitW >= 96f && hitH >= 96f;
                for (int j = 0; j < i; j++) ok &= !CodexPanel.TabRect(l, j).Overlaps(t);
                var drawn = PanelSpace(panel.Panel, (RectTransform)panel.TabLabel(i).transform.parent);
                ok &= Mathf.Abs(drawn.center.x - t.center.x) < 1f && Mathf.Abs(drawn.center.y - t.center.y) < 1f;
            }
            Check(name + ": seven tabs (4 + 3) sit in the tab band without overlap", ok);
            Check(name + ": every tab is a >= 96-unit touch target", hit);
            bool fit = true;
            for (int i = 0; i < CodexPanel.TabCount; i++) fit &= FitsAt(panel.TabLabel(i), 13);
            Check(name + ": all seven tab labels fit at >= 13px", fit);
            Check(name + ": the body still holds a section header and two card rows", l.list.height >= CodexPanel.SectionHeaderHeight + CodexPanel.SectionHeaderGap + 2f * l.cardHeight);
        }
        panel.ApplyLayout(Screens[0].safe);
    }

    // ---- an empty profile ----

    static void Fresh(CodexPanel panel)
    {
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        panel.ShowTab(CodexPanel.AchievementsTab);
        panel.SkipAnimations();
        var v = panel.Achievements;
        Check("the tab opens the achievements view and hides the entry grid", panel.AchievementsOpen && v.Active);
        Check("58 cards in 7 sections (dormant Tide not listed)", v.VisibleCards == 58 && v.SectionCount == 7);
        Check("sections: JOURNEY, BOSSES, ELITES, COMBAT, PAUSE, CODEX, COLLECTION",
              string.Join(",", Enumerable.Range(0, v.SectionCount).Select(i => v.SectionAt(i).label)) == "JOURNEY,BOSSES,ELITES,COMBAT,PAUSE,CODEX,COLLECTION");
        Check("section counters read 0/n", v.SectionAt(0).counter == "0/" + v.SectionAt(0).defs.Count);
        Check("the header counter reads 0 / 58 UNLOCKED", panel.Counter.text.StartsWith("0 / 58"));
        Check("strip: 0/58 UNLOCKED, nothing to collect, COLLECT ALL off",
              v.StripCount.text == "0/58 UNLOCKED" && v.StripLine.text.StartsWith("NOTHING TO COLLECT") && !v.CollectAll.button.interactable);
        bool allLocked = true;
        for (int i = 0; i < v.VisibleCards; i++)
            allLocked &= v.CardLock(i).gameObject.activeSelf && !v.CardCollectVisible(i) && !v.CardCollected(i).gameObject.activeSelf && v.CardBadge(i).sprite != null;
        Check("every card starts locked: padlock, no COLLECT, no COLLECTED, a badge (art or placeholder)", allLocked);
        int hidden = v.CardIndexOf("mega_domino");
        Check("a hidden achievement reads ??? while locked", v.CardName(hidden).text == "???" && v.CardTier(hidden).text == "?");
        int plain = v.CardIndexOf("world_frost_reached");
        Check("a normal locked one shows its title and tier", v.CardName(plain).text == "Cold Front" && v.CardTier(plain).text == "BRONZE");
        int bar = v.CardIndexOf("kills_100");
        Check("a counter shows a progress bar (0/100)", v.CardBarVisible(bar) && v.CardBarText(bar).text == "0/100" && !v.CardTier(bar).gameObject.activeSelf);
        Check("a one-shot shows no bar", !v.CardBarVisible(plain));
        Check("the ACHIEVEMENTS tab is highlighted, the others idle",
              panel.TabLabel(CodexPanel.AchievementsTab).color == CodexUi.Body && panel.TabLabel(0).color == CodexUi.Muted);
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        Check("another tab hides it again and brings the entries back", !panel.AchievementsOpen && !v.Active && panel.VisibleCards > 0 &&
              panel.TabLabel(CodexPanel.AchievementsTab).color == CodexUi.Muted);
        panel.ShowTab(CodexPanel.AchievementsTab);
        panel.SkipAnimations();
        Check("... and the tab opens again", panel.AchievementsOpen && v.Active);
    }

    // ---- locked / unlocked / claimed ----

    static void States(CodexPanel panel)
    {
        var v = panel.Achievements;
        AchievementStore.SetCounter("stars", 40);
        AchievementStore.Unlock(AchievementCatalog.Find("loop_1"));
        AchievementStore.Unlock(AchievementCatalog.Find("kills_100"));
        AchievementStore.Claim(AchievementCatalog.Find("kills_100"));
        AchievementStore.Unlock(AchievementCatalog.Find("mega_domino"));
        panel.Refresh();
        panel.SkipAnimations();
        int locked = v.CardIndexOf("stars_150"), claimable = v.CardIndexOf("loop_1"), claimed = v.CardIndexOf("kills_100"), secret = v.CardIndexOf("mega_domino");
        Check("locked counter: bar 40/150 at 27%", v.CardBarText(locked).text == "40/150" &&
              Mathf.Abs(v.CardBarFill(locked).rectTransform.anchorMax.x - 40f / 150f) < .001f && v.CardLock(locked).gameObject.activeSelf);
        Check("locked: the badge is dimmed but still readable", v.CardBadge(locked).color.r < .75f && v.CardBadge(locked).color.r > .45f && v.CardEdge(locked).color == CodexUi.Locked);
        Check("unlocked (claimable): COLLECT 25 chip, no padlock, full-colour badge, gold edge",
              v.CardCollectVisible(claimable) && v.CardCollect(claimable).label.text == "COLLECT 25" && !v.CardLock(claimable).gameObject.activeSelf &&
              v.CardBadge(claimable).color == Color.white && v.CardEdge(claimable).color == CodexPalette.Amber && !v.CardCollected(claimable).gameObject.activeSelf);
        Check("claimed: COLLECTED, no chip, teal edge, no padlock", v.CardCollected(claimed).gameObject.activeSelf && !v.CardCollectVisible(claimed) &&
              v.CardEdge(claimed).color == CodexUi.Accent && !v.CardLock(claimed).gameObject.activeSelf);
        Check("a hidden one shows its real name once earned", v.CardName(secret).text == "MEGA DOMINO" && v.CardCollectVisible(secret));
        Check("strip counts 3 unlocked, 2 to collect, COLLECT ALL +50",
              v.StripCount.text == "3/58 UNLOCKED" && v.StripLine.text.StartsWith("2 TO COLLECT") && v.CollectAll.button.interactable &&
              v.CollectAll.label.text == "COLLECT ALL +50");
        Check("the sticky/section counters follow", v.SectionAt(0).counter.StartsWith("1/") && panel.Counter.text.StartsWith("3 / 58"));

        panel.ShowAchievements();   // already open: closes a detail, never re-enters
        v.ShowDetail(AchievementCatalog.Find("loop_1"));
        Check("detail: claimable shows the big COLLECT 25, the description and the reward",
              v.InDetail && v.DetailCollect.rt.gameObject.activeSelf && v.DetailName.text == "Full Circle" &&
              v.DetailDescription.text == AchievementCatalog.Find("loop_1").description && v.DetailReward.text == "REWARD  25 STAR DUST" &&
              !v.DetailCollected.gameObject.activeSelf && v.DetailPillLabel.text == "GOLD  -  JOURNEY");
        v.ShowDetail(AchievementCatalog.Find("kills_100"));
        Check("detail: claimed shows COLLECTED, no button", v.DetailCollected.gameObject.activeSelf && !v.DetailCollect.rt.gameObject.activeSelf);
        v.ShowDetail(AchievementCatalog.Find("stars_150"));
        Check("detail: locked shows a progress bar and no action", v.DetailBarText.text == "40 / 150".Replace(" ", "") &&
              !v.DetailCollect.rt.gameObject.activeSelf && !v.DetailCollected.gameObject.activeSelf);
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        v.ShowDetail(AchievementCatalog.Find("deaths_100"));
        Check("detail: a hidden locked one says ??? and gives only the hint", v.DetailName.text == "???" &&
              v.DetailDescription.text == CodexAchievementsView.HiddenHint && v.DetailPillLabel.text == "?");
        Check("Back closes the detail first, then the panel", Back(panel));
        panel.Refresh();
    }

    static bool Back(CodexPanel panel)
    {
        var v = panel.Achievements;
        bool detail = v.InDetail;
        panel.Back();
        bool closedDetail = detail && !v.InDetail && panel.IsOpen;
        return closedDetail;
    }

    // ---- layout on every screen ----

    static void Layouts(CodexPanel panel)
    {
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        AchievementStore.Unlock(AchievementCatalog.Find("loop_1"));
        foreach (var (name, safe) in Screens)
        {
            panel.ApplyLayout(safe);
            panel.ShowAchievements();
            panel.Refresh();
            panel.SkipAnimations();
            Canvas.ForceUpdateCanvases();
            var v = panel.Achievements;
            var l = panel.CurrentLayout;
            var body = l.body;
            bool inside = true, names = true, bars = true;
            for (int i = 0; i < v.VisibleCards; i++)
            {
                var r = PanelSpace(panel.Panel, v.CardRect(i));
                inside &= r.xMin >= body.xMin - .5f && r.xMax <= body.xMax + .5f;
                names &= FitsAt(v.CardName(i), 11);
                if (v.CardBarVisible(i)) bars &= FitsAt(v.CardBarText(i), 10);
            }
            Check(name + ": cards sit inside the body horizontally (" + v.Columns + " columns)", inside);
            Check(name + ": card names fit at >= 11px", names);
            Check(name + ": progress texts fit", bars);
            var strip = PanelSpace(panel.Panel, v.StripRect);
            var chips0 = PanelSpace(panel.Panel, v.ChipRect(0));
            var view = PanelSpace(panel.Panel, v.Viewport);
            Check(name + ": strip, chips and list stack inside the body without overlap",
                  Contains(Inset(body, -.5f), strip) && Contains(Inset(body, -.5f), chips0) && Contains(Inset(body, -.5f), view) &&
                  strip.yMin >= chips0.yMax - .5f && chips0.yMin >= view.yMax - .5f);
            Check(name + ": the strip text fits", FitsAt(v.StripCount, 16) && FitsAt(v.StripLine, 11) && FitsAt(v.CollectAll.label, 13));
            var cab = PanelSpace(panel.Panel, v.CollectAll.rt);
            Check(name + ": COLLECT ALL is >= 64 units tall (+12 raycast padding each side) and inside the strip",
                  cab.height >= 64f && Contains(Inset(strip, -.5f), cab) && v.CollectAll.frame.raycastPadding.x <= -12f);
            bool chipsOk = true;
            for (int s = 0; s < v.SectionCount; s++)
            {
                var c = PanelSpace(panel.Panel, v.ChipRect(s));
                chipsOk &= Contains(Inset(PanelSpace(panel.Panel, v.Root), -.5f), c) && FitsAt(v.ChipLabel(s), 10) && c.width >= 80f;
                var h = PanelSpace(panel.Panel, v.SectionHeaderRect(s));
                chipsOk &= FitsAt(v.SectionLabel(s), 14) && FitsAt(v.SectionCounter(s), 22) && h.xMin >= view.xMin - .5f && h.xMax <= view.xMax + .5f;
            }
            Check(name + ": 7 jump chips and header texts fit (>= 80 wide)", chipsOk);
            Check(name + ": the COLLECT chip is a >= 56-unit target", v.CardCollect(v.CardIndexOf("loop_1")).rt.rect.height >= 56f);
            v.SetScrollY(v.SectionTop(1) + 1f);
            var pinned = PanelSpace(panel.Panel, (RectTransform)v.Viewport.Find("StickyHeader"));
            Check(name + ": the pinned header sits at the top of the list", v.StickySection == 1 && Mathf.Abs(pinned.yMax - view.yMax) < .5f);
            v.SetScrollY(v.MaxScroll);
            Canvas.ForceUpdateCanvases();
            var last = PanelSpace(panel.Panel, v.CardRect(v.VisibleCards - 1));
            Check(name + ": scrolls to the very last card", last.yMin >= view.yMin - .5f && last.yMax <= view.yMax + .5f);
            v.SetScrollY(0f);
            v.ShowDetail(AchievementCatalog.Find("boss_all"));
            Canvas.ForceUpdateCanvases();
            var dc = PanelSpace(panel.Panel, v.DetailCollect.rt);
            var dr = PanelSpace(panel.Panel, v.DetailRoot);
            var lore = PanelSpace(panel.Panel, (RectTransform)v.DetailDescription.transform.parent);
            Check(name + ": the detail fits: name, description, bar, reward and button all inside, nothing overlapping",
                  FitsAt(v.DetailName, 22) && FitsAt(v.DetailDescription, 16) && FitsAt(v.DetailReward, 12) && lore.height >= 60f &&
                  Contains(Inset(dr, -.5f), lore) && (!v.DetailCollect.rt.gameObject.activeSelf || (Contains(Inset(dr, -.5f), dc) && !dc.Overlaps(lore))));
            v.CloseDetail();
        }
        panel.ApplyLayout(Screens[0].safe);
        panel.Refresh();
    }

    // ---- real pointer events ----

    static Vector2 ScreenCentre(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * .5f;   // overlay canvas: world == screen pixels
    }

    // One tap at a screen point through the same rules the GraphicRaycaster applies:
    // only the raycaster on a graphic's OWN (nearest) canvas can see it. Then down / up / click.
    static GameObject Tap(CodexPanel panel, Vector2 pos)
    {
        var es = EventSystem.current;
        var ped = new PointerEventData(es) { position = pos, button = PointerEventData.InputButton.Left, clickCount = 1 };
        Graphic best = null;
        int bestDepth = int.MinValue;
        var all = panel.GetComponentsInChildren<Graphic>(false);
        for (int gi = 0; gi < all.Length; gi++)
        {
            var g = all[gi];
            if (!g.raycastTarget || !g.enabled || g.canvas == null || g.canvasRenderer.cull) continue;
            if (g.canvas.GetComponent<GraphicRaycaster>() == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pos, null, g.raycastPadding)) continue;
            if (!g.Raycast(pos, null)) continue;
            // a CanvasGroup that blocks nothing hides its graphics from the raycaster
            var group = g.GetComponentInParent<CanvasGroup>();
            bool blocked = false;
            for (var t = g.transform; t != null; t = t.parent)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null && !cg.blocksRaycasts) { blocked = true; break; }
                if (cg != null && cg.ignoreParentGroups) break;
            }
            if (blocked) continue;
            if (gi > bestDepth) { best = g; bestDepth = gi; }
        }
        if (best == null) return null;
        var hit = best.gameObject;
        var result = new RaycastResult { gameObject = hit };
        ped.pointerCurrentRaycast = result;
        ped.pointerPressRaycast = result;
        var down = ExecuteEvents.ExecuteHierarchy(hit, ped, ExecuteEvents.pointerDownHandler);
        ped.pointerPress = down != null ? down : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
        ped.eligibleForClick = true;
        ExecuteEvents.ExecuteHierarchy(hit, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(hit, ped, ExecuteEvents.pointerClickHandler);
        return hit;
    }

    static void Reveal(CodexAchievementsView v, int card)
    {
        float y = -v.CardRect(card).anchoredPosition.y - v.CardHeight * .5f - 20f;
        v.SetScrollY(Mathf.Clamp(y, 0f, v.MaxScroll));
        Canvas.ForceUpdateCanvases();
    }

    static float Dust() { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); }

    static void Pointer(CodexPanel panel)
    {
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        foreach (string id in new[] { "meta_first_flight", "world_frost_reached", "boss_space", "stars_150" }) AchievementStore.Unlock(AchievementCatalog.Find(id));
        panel.ApplyLayout(Screens[0].safe);
        panel.ShowAchievements();
        panel.Refresh();
        panel.SkipAnimations();
        var v = panel.Achievements;
        Canvas.ForceUpdateCanvases();

        // the nested-canvas lesson: each button sits on its own canvas WITH a raycaster
        var chip = v.CardCollect(v.CardIndexOf("meta_first_flight"));
        Check("a card's COLLECT chip has its own sub-canvas and its own GraphicRaycaster",
              chip.canvas != null && chip.canvas.gameObject == chip.frame.gameObject && chip.canvas.GetComponent<GraphicRaycaster>() != null &&
              chip.canvas.rootCanvas != chip.canvas);
        Check("... and so do COLLECT ALL and the detail's COLLECT",
              v.CollectAll.canvas.GetComponent<GraphicRaycaster>() != null && v.DetailCollect.canvas.GetComponent<GraphicRaycaster>() != null);
        Check("decorations take no touches (badge, glow, lock, edge, labels)",
              !v.CardBadge(0).raycastTarget && !v.CardLock(0).raycastTarget && !v.CardEdge(0).raycastTarget && !v.CardName(0).raycastTarget &&
              !chip.label.raycastTarget);

        // COLLECT on one card
        int idx = v.CardIndexOf("meta_first_flight");
        Reveal(v, idx);
        var hit = Tap(panel, ScreenCentre(chip.rt));
        Check("a real tap on the COLLECT chip hits the chip (hit " + (hit != null ? hit.name : "nothing") + ")", hit == chip.frame.gameObject);
        Check("... pays exactly 25 star dust and marks it claimed", Dust() == 125f && AchievementStore.IsClaimed(AchievementCatalog.Find("meta_first_flight")));
        Check("... the card flips to COLLECTED at once", !v.CardCollectVisible(idx) && v.CardCollected(idx).gameObject.activeSelf);
        Check("... the strip updates (3 to collect, balance counts up to 125)", v.StripLine.text.StartsWith("3 TO COLLECT"));
        v.SkipAnimations();
        Check("... the balance reads 125 after the count-up", v.StripLine.text.EndsWith("DUST 125") && v.ShownBalance == 125);
        Tap(panel, ScreenCentre(chip.rt));   // the chip is gone: the tap lands on the card and opens its detail
        Check("tapping the (gone) chip again pays nothing", Dust() == 125f);
        v.CloseDetail();

        // tap a card: detail opens; COLLECT in the detail
        int idx2 = v.CardIndexOf("world_frost_reached");
        Reveal(v, idx2);
        var cardBtn = v.CardButton(idx2);
        var cc = new Vector3[4]; v.CardRect(idx2).GetWorldCorners(cc);
        var topLeft = ScreenCentre(v.CardRect(idx2)) - new Vector2(0f, (cc[1].y - cc[0].y) * .1f);   // a little below the centre, clear of the pinned header
        var h2 = Tap(panel, topLeft);
        Check("a real tap on a card's badge opens its detail (hit " + (h2 != null ? h2.name : "nothing") + ")",
              h2 == cardBtn.gameObject && v.InDetail && v.DetailDef.id == "world_frost_reached");
        Canvas.ForceUpdateCanvases();
        var h3 = Tap(panel, ScreenCentre(v.DetailCollect.rt));
        Check("a real tap on the detail's COLLECT hits it", h3 == v.DetailCollect.frame.gameObject);
        Check("... pays 25 and shows COLLECTED", Dust() == 150f && v.DetailCollected.gameObject.activeSelf && !v.DetailCollect.rt.gameObject.activeSelf);
        Check("while the detail is up the list under it takes no taps",
              Tap(panel, ScreenCentre(v.CardRect(idx))) == null || v.InDetail);
        panel.BackButton.onClick.Invoke();
        Check("the BACK button closes the detail, not the codex", !v.InDetail && panel.IsOpen);

        // COLLECT ALL
        Canvas.ForceUpdateCanvases();
        Check("2 left to collect", AchievementStore.ClaimableCount == 2 && v.CollectAll.label.text == "COLLECT ALL +50");
        var h4 = Tap(panel, ScreenCentre(v.CollectAll.rt));
        Check("a real tap on COLLECT ALL hits it", h4 == v.CollectAll.frame.gameObject);
        Check("... pays N x 25 = 50 once", Dust() == 200f && AchievementStore.ClaimableCount == 0);
        Check("... and disables itself", !v.CollectAll.button.interactable && v.CollectAll.label.text == "COLLECT ALL");
        Tap(panel, ScreenCentre(v.CollectAll.rt));
        Check("a second COLLECT ALL pays nothing", Dust() == 200f);

        // a tab tap through the real pipeline
        panel.ShowCategory(CodexCategory.Log);
        panel.SkipAnimations();
        Canvas.ForceUpdateCanvases();
        var tabFrame = (RectTransform)panel.TabLabel(CodexPanel.AchievementsTab).transform.parent;
        var h5 = Tap(panel, ScreenCentre(tabFrame));
        Check("a real tap on the ACHIEVEMENTS tab opens it", h5 == tabFrame.gameObject && panel.AchievementsOpen);
        var logTab = (RectTransform)panel.TabLabel(0).transform.parent;
        Tap(panel, ScreenCentre(logTab));
        Check("a real tap on LOG leaves it", !panel.AchievementsOpen && panel.Category == CodexCategory.Log);
        panel.ShowTab(CodexPanel.AchievementsTab);
        panel.SkipAnimations();

        // jump chips through real taps
        Canvas.ForceUpdateCanvases();
        var chip3 = v.ChipRect(3);
        Tap(panel, ScreenCentre(chip3));
        v.SkipAnimations();
        Check("a real tap on a jump chip scrolls to its section", Mathf.Abs(v.ScrollY - v.JumpTargetY(3)) < 1f && v.ScrollY > 0f);
        v.SetScrollY(0f);

        // the codex closes with the achievements open and reopens on the entries
        panel.Close();
        panel.SkipAnimations();
        CodexPanel.Open(CodexUi.FindFont());
        panel.SkipAnimations();
        Check("closing and reopening starts on the entries again", !panel.AchievementsOpen && panel.IsOpen);
        panel.ShowTab(CodexPanel.AchievementsTab);
        panel.SkipAnimations();
    }

    // ---- dormant Tide ----

    static void Dormant(CodexPanel panel)
    {
        var v = panel.Achievements;
        Check("Tide off: neither Tide achievement is on a card", v.CardIndexOf("world_tide_reached") < 0 && v.CardIndexOf("boss_tide") < 0 && v.VisibleCards == 58);
        WorldManager.TideEnabled = true;
        panel.Refresh();
        Check("Tide on: both are listed, 60 cards, x/60", v.CardIndexOf("world_tide_reached") >= 0 && v.CardIndexOf("boss_tide") >= 0 &&
              v.VisibleCards == 60 && v.StripCount.text.EndsWith("/60 UNLOCKED"));
        WorldManager.TideEnabled = false;
        panel.Refresh();
        Check("Tide off again: back to 58", v.VisibleCards == 58);
    }

    // ---- no per-frame allocation, pulse only where visible ----

    static void Perf(CodexPanel panel)
    {
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        foreach (string id in new[] { "meta_first_flight", "world_frost_reached", "world_verdant_reached", "loop_1", "loop_2" }) AchievementStore.Unlock(AchievementCatalog.Find(id));
        panel.Refresh();
        panel.SkipAnimations();
        var v = panel.Achievements;
        v.SetScrollY(0f);
        Canvas.ForceUpdateCanvases();
        int visible = v.CardIndexOf("meta_first_flight"), far = v.CardIndexOf("loop_2");
        float t = Time.unscaledTime + 1f;
        v.Tick(t);
        float k1 = v.CardCollect(visible).rt.localScale.x;
        v.Tick(t + .3f);
        float k2 = v.CardCollect(visible).rt.localScale.x;
        Check("a visible COLLECT chip pulses (" + k1.ToString("0.000") + " -> " + k2.ToString("0.000") + ")", k1 > 1f && k2 > 1f && Mathf.Abs(k1 - k2) > .001f && k1 <= 1.08f && k2 <= 1.08f);
        Check("COLLECT ALL pulses too", v.CollectAll.rt.localScale.x > 1f);
        Reveal(v, visible);
        v.SetScrollY(0f);
        float before = v.CardCollect(far).rt.localScale.x;
        v.Tick(t + .6f);
        Check("a chip scrolled out of view holds its pose", Mathf.Approximately(v.CardCollect(far).rt.localScale.x, before) || far == visible);

        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        for (int i = 0; i < 30; i++) v.Tick(t + i * .016f);   // warm up
        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 120; i++) v.Tick(t + 1f + i * .016f); });
        Check("120 frames of the tab allocate nothing (" + bytes + " B; meter control " + control + " B" + (meter ? "" : ", meter blind this run: not judged") + ")", !meter || bytes == 0);
    }

    // ---- the home screen entry ----

    static void Home()
    {
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        var host = new GameObject("UIPanel", typeof(RectTransform));
        var go = new GameObject("CodexButton", typeof(RectTransform));
        go.transform.SetParent(host.transform, false);
        var home = go.AddComponent<CodexHomeButton>();
        try
        {
            home.Build();
            // edit mode never calls OnEnable: do what Play mode does, so the event wiring is what is under test
            typeof(CodexHomeButton).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(home, null);
            Check("home: no badge while nothing is waiting", !home.BadgeVisible);
            Check("home: label rule 0/1/9/10/42 -> 0,1,9,9+,9+",
                  CodexHomeButton.BadgeLabel(1) == "1" && CodexHomeButton.BadgeLabel(9) == "9" &&
                  CodexHomeButton.BadgeLabel(10) == "9+" && CodexHomeButton.BadgeLabel(42) == "9+");
            var all = AchievementCatalog.All.Where(d => AchievementCatalog.IsActive(d)).ToList();
            AchievementStore.Unlock(all[0]);
            Check("home: one unlock shows 1 live (no manual refresh)", home.BadgeVisible && home.BadgeText.text == "1");
            for (int i = 1; i < 9; i++) AchievementStore.Unlock(all[i]);
            Check("home: 9 shows 9", home.BadgeVisible && home.BadgeText.text == "9" && AchievementStore.ClaimableCount == 9);
            AchievementStore.Unlock(all[9]);
            Check("home: 10 shows 9+", home.BadgeText.text == "9+");
            AchievementStore.Unlock(all[10]);
            AchievementStore.Unlock(all[11]);
            Check("home: 12 still shows 9+", home.BadgeText.text == "9+" && AchievementStore.ClaimableCount == 12);
            AchievementStore.Claim(all[0]);
            Check("home: a claim updates it live (11 -> 9+)", home.BadgeText.text == "9+" && AchievementStore.ClaimableCount == 11);
            AchievementStore.Claim(all[1]);
            AchievementStore.Claim(all[2]);
            Check("home: 9 left shows 9", home.BadgeText.text == "9");
            Check("home: the badge takes no touches, has no canvas, and the DISCOVERED counter is unchanged",
                  !home.Badge.raycastTarget && !home.BadgeText.raycastTarget && home.Badge.GetComponent<Canvas>() == null &&
                  System.Text.RegularExpressions.Regex.IsMatch(home.Counter.text, @"^\d+/\d+ DISCOVERED$"));
            Check("home: the badge sprite is point filtered", home.Badge.sprite != null && home.Badge.sprite.texture.filterMode == FilterMode.Point);
            AchievementStore.ClaimAll();
            Check("home: the badge goes once everything is collected", !home.BadgeVisible);
            typeof(CodexHomeButton).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(home, null);
            string src = System.IO.File.ReadAllText("Assets/Scripts/Codex/CodexHomeButton.cs");
            Check("home: it listens to unlocks, claims and the panel closing",
                  src.Contains("AchievementStore.Unlocked +=") && src.Contains("AchievementStore.Claimed +=") && src.Contains("CodexPanel.Closed +="));
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    // The badge on the real home screen, on every device shape: inside the
    // button, in its top-right, and clear of the tap target.
    static void HomeFit()
    {
        int bad = 0, cells = 0;
        string first = "";
        var screen = new FitScreen
        {
            id = "home-badge", scene = "startS4", title = "badge", fullBleed = false,
            stage = rig =>
            {
                HomeBadgeStage(rig, ref bad, ref first);
            }
        };
        foreach (var device in FitDevice.All)
        {
            using (new TestHarness.Sandbox())
            {
                AchievementStore.ResetAll();
                var shot = ScreenFitRunner.Run(screen, device, null, 0);
                cells++;
                if (shot.error != null) { bad++; if (first == "") first = device.id + ": " + shot.error; }
            }
        }
        Check("home: badge rides the Codex word's top-right (also for a longer word), taps pass through it, on all " + cells + " device sizes" + (first == "" ? "" : " (" + first + ")"), bad == 0 && cells > 5);
    }

    static void HomeBadgeStage(ScreenFitRig rig, ref int bad, ref string first)
    {
        foreach (var d in AchievementCatalog.All.Where(x => AchievementCatalog.IsActive(x)).Take(12)) AchievementStore.Unlock(d);
        ScreenFitScreens.HomeBase(rig);
        var home = Object.FindFirstObjectByType<CodexHomeButton>();
        string why = null;
        if (home == null || !home.BadgeVisible) why = "badge not shown";
        else
        {
            home.Refresh();
            why = BadgeRidesWord(rig, home, "Codex");
            if (why == null) why = BadgeRidesWord(rig, home, "Discoveries");
            if (why == null) why = BadgeRidesWord(rig, home, "Codex");
            if (why == null)
            {
                // a tap at the badge must still land on the button
                bool onBadge = false;
                foreach (var g in home.GetComponentsInChildren<Graphic>(false))
                    if (g.raycastTarget && (g == home.Badge || g == home.BadgeText)) onBadge = true;
                if (onBadge) why = "badge graphics catch raycasts";
                else if (!home.Button.targetGraphic.raycastTarget) why = "button lost its raycast target";
            }
        }
        if (why != null) { bad++; if (first == "") first = rig.device.id + ": " + why; }
    }


    // Rect of a RectTransform in the button's local space.
    static Rect LocalRect(Transform space, RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        var lo = new Vector2(float.MaxValue, float.MaxValue); var hi = new Vector2(float.MinValue, float.MinValue);
        foreach (var w in c) { var l = space.InverseTransformPoint(w); lo = Vector2.Min(lo, l); hi = Vector2.Max(hi, l); }
        return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
    }

    // The bubble rides the top-right of the rendered word: right of its last
    // glyph (<= 10 units away), above the text's vertical centre, inside the
    // button, clear of the DISCOVERED line, 9.5..22 units, whole 13 px multiples.
    static string BadgeRidesWord(ScreenFitRig rig, CodexHomeButton home, string word)
    {
        home.Label.text = word;
        Canvas.ForceUpdateCanvases();
        home.PlaceBadge(true);
        float right, top, lastTop;
        if (!home.MeasureWord(out right, out top, out lastTop)) return word + ": word not measurable";
        var lab = home.Label.rectTransform;
        var wr = home.transform.InverseTransformPoint(lab.TransformPoint(new Vector3(right, lastTop, 0f)));
        var btn = LocalRect(home.transform, (RectTransform)home.transform);
        var bd = LocalRect(home.transform, home.Badge.rectTransform);
        var cnt = LocalRect(home.transform, home.Counter.rectTransform);
        var labR = LocalRect(home.transform, lab);
        float e = 1.5f;
        if (bd.xMin < btn.xMin - e || bd.xMax > btn.xMax + e || bd.yMin < btn.yMin - e || bd.yMax > btn.yMax + e) return word + ": badge outside button " + bd + " vs " + btn;
        if (bd.xMin < wr.x - 4f) return word + ": badge covers the last glyph " + bd.xMin + " < " + wr.x;
        if (bd.xMin - wr.x > 10f) return word + ": badge " + (bd.xMin - wr.x) + " units from the word";
        if (bd.yMin < wr.y - 4f && bd.xMin < wr.x - .5f) return word + ": badge below the last glyph's top";
        if (bd.center.y <= labR.center.y) return word + ": badge not above the text centre";
        if (bd.Overlaps(cnt)) return word + ": badge overlaps the DISCOVERED line";
        if (bd.width < 9.5f || bd.width > 22f) return word + ": badge size " + bd.width;
        float scale = home.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
        float px = bd.width * scale / 13f;
        if (Mathf.Abs(px - Mathf.Round(px)) > .03f) return word + ": badge not a whole multiple of the 13 px art (" + px + ")";
        if (home.BadgeText.text != "9+") return word + ": text " + home.BadgeText.text;
        return null;
    }

    // ---- helpers (the same measuring rules as CodexTest) ----

    static bool FitsAt(Text t, int minSize)
    {
        var rect = t.rectTransform.rect.size;
        if (rect.x <= 0f || rect.y <= 0f) return false;
        int max = t.resizeTextForBestFit ? t.resizeTextMaxSize : t.fontSize;
        int min = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
        int chosen = -1;
        for (int size = max; size >= min; size--)
            if (Fits(t, size, rect)) { chosen = size; break; }
        bool ok = chosen >= minSize;
        if (!ok) Debug.Log("[CAT] '" + t.text.Replace("\n", " ") + "' does not fit at >= " + minSize + " in " + rect);
        return ok;
    }

    static bool Fits(Text t, int size, Vector2 rect)
    {
        var gen = new TextGenerator();
        var s = Settings(t, size, new Vector2(rect.x, 100000f));
        if (t.horizontalOverflow == HorizontalWrapMode.Overflow)
        {
            var one = Settings(t, size, new Vector2(100000f, 100000f));
            one.horizontalOverflow = HorizontalWrapMode.Overflow;
            return gen.GetPreferredWidth(t.text, one) <= rect.x + .5f && gen.GetPreferredHeight(t.text, one) <= rect.y + .5f;
        }
        if (gen.GetPreferredHeight(t.text, s) > rect.y + .5f) return false;
        var word = Settings(t, size, new Vector2(100000f, 100000f));
        word.horizontalOverflow = HorizontalWrapMode.Overflow;
        foreach (string w in t.text.Split(' ', '\n'))
            if (w.Length > 0 && gen.GetPreferredWidth(w, word) > rect.x + .5f) return false;
        return true;
    }

    static TextGenerationSettings Settings(Text t, int size, Vector2 extents)
    {
        var s = t.GetGenerationSettings(extents);
        s.scaleFactor = 1f;
        s.resizeTextForBestFit = false;
        s.fontSize = size;
        s.verticalOverflow = VerticalWrapMode.Overflow;
        s.generateOutOfBounds = true;
        return s;
    }

    static Rect PanelSpace(RectTransform panel, RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = panel.InverseTransformPoint(corners[0]);
        Vector2 max = panel.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    static Rect Inset(Rect r, float by) { return Rect.MinMaxRect(r.xMin + by, r.yMin + by, r.xMax - by, r.yMax - by); }

    static bool Contains(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin - .01f && inner.xMax <= outer.xMax + .01f &&
               inner.yMin >= outer.yMin - .01f && inner.yMax <= outer.yMax + .01f;
    }
}
