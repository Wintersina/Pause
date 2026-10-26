using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Codex unlock rewards and their yellow notification bubbles. Discovering an entry (not the Pilot's Log) earns 5
// star dust that must be COLLECTED: exactly one pending reward per discovery, paid exactly once, never automatically.
// The bubbles (a category tab, the ACHIEVEMENTS tab, the home Codex button, an entry card) derive from the saved
// state: opening a tab or an entry never clears them, only collecting does, each tab independently, and they
// survive a reload. Saves that predate the rewards make everything already discovered claimable once.
public static class CodexNewDotsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        UnityEngine.Debug.Log((ok ? "[CND] PASS  " : "[CND] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static float Dust() { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 1; i <= shopingShips.RosterTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var esGo = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        GameObject host = null;
        try
        {
            // candidates: undiscovered, listed, non-log entries in two categories
            var open = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret).ToList();
            var cats = open.Select(e => e.category).Distinct().ToList();
            var catA = cats.First(c => open.Count(e => e.category == c && !IsOwnedKind(e)) >= 2);
            var catB = cats.First(c => c != catA && open.Any(e => e.category == c && !IsOwnedKind(e)));
            var inA = open.Where(e => e.category == catA && !IsOwnedKind(e)).Take(2).ToArray();
            var inB = open.First(e => e.category == catB && !IsOwnedKind(e));
            var log = Codex.Entries.First(e => e.category == CodexCategory.Log);

            Check("the reward is 5 star dust", Codex.UnlockReward == 5);

            // ---- old save: discoveries from before the feature (no PaidKey) are claimable once, never auto-paid ----
            PlayerPrefs.SetString(Codex.PrefsKey, inA[0].id + "," + inB.id + ",music_world_ember,bd_frost_2");
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();
            Check("old save: both old entries are pending (tracks and backdrops are not entries), nothing auto-paid",
                  Codex.IsRewardPending(inA[0]) && Codex.IsRewardPending(inB) && Codex.PendingTotal == 2 && Dust() == 100f && CodexHomeButton.Total() == 2);
            Check("old save: the Pilot's Log never pays", !Codex.IsRewardPending(log) && Codex.CollectReward(log.id) == 0 && Dust() == 100f);
            Check("old save: collect all pays 2 x 5 once, then nothing", Codex.CollectAllRewards() == 10 && Dust() == 110f && Codex.CollectAllRewards() == 0 && Dust() == 110f && Codex.PendingTotal == 0);
            Codex.Reload();
            Check("old save: collected stays collected after a reload", Codex.PendingTotal == 0 && Codex.IsRewardCollected(inA[0].id) && Codex.CollectReward(inB.id) == 0 && Dust() == 110f);
            PlayerPrefs.SetString(Codex.PrefsKey, "");
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
            Codex.Reload();

            // ---- a fresh discovery: exactly one pending 5-dust reward ----
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            panel.ApplyLayout(new Rect(-400f, -866f, 800f, 1733f));
            host = new GameObject("UIPanel", typeof(RectTransform));
            var go = new GameObject("CodexButton", typeof(RectTransform));
            go.transform.SetParent(host.transform, false);
            var home = go.AddComponent<CodexHomeButton>();
            home.Build();
            typeof(CodexHomeButton).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(home, null);
            int tabA = CodexPanel.TabOfCategory(catA), tabB = CodexPanel.TabOfCategory(catB);

            Check("start: no dots, bubble hidden, nothing pending", !home.BadgeVisible && !panel.TabNewDot(tabA).gameObject.activeSelf && Codex.PendingIn(catA) == 0);

            int raised = 0;
            System.Action count = () => raised++;
            Codex.NewChanged += count;
            Codex.Discover(inA[0].id); Codex.Discover(inA[1].id); Codex.Discover(inB.id);
            Codex.Discover(inB.id);   // a repeat contact adds nothing
            Codex.NewChanged -= count;
            Check("discover: exactly one pending reward per entry (3), nothing paid yet",
                  Codex.PendingIn(catA) == 2 && Codex.PendingIn(catB) == 1 && Codex.PendingTotal == 3 && Dust() == 100f && raised >= 3);
            Check("discover: the home bubble shows 3 live (no manual refresh), yellow", home.BadgeVisible && home.BadgeShown == "3" &&
                  SameColor(home.Badge.sprite.texture.GetPixel(6, 6), CodexHomeButton.BubbleYellow));
            panel.Refresh();
            Check("discover: both category tabs carry a dot, ACHIEVEMENTS does not", panel.TabNewDot(tabA).gameObject.activeSelf && panel.TabNewDot(tabB).gameObject.activeSelf &&
                  !panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf);

            // ---- viewing never clears anything ----
            panel.ShowCategory(catA);
            int ia = CardOf(panel, inA[0].id), ib = CardOf(panel, inA[1].id);
            Check("dots carry no Text (pixel art only)", panel.TabNewDot(tabA).GetComponentsInChildren<Text>(true).Length == 0);
            Check("view tab: its dot, the home count and both card dots stay", panel.TabNewDot(tabA).gameObject.activeSelf && home.BadgeShown == "3" &&
                  ia >= 0 && ib >= 0 && panel.CardNewDot(ia).gameObject.activeSelf && panel.CardNewDot(ib).gameObject.activeSelf);
            Check("view tab: COLLECT ALL +10 chip shows", panel.CollectTabFrame.gameObject.activeSelf && panel.CollectTabLabel.text == "COLLECT ALL +10");
            panel.ShowDetail(inA[0]);
            Check("view entry: its dot, the tab dot and the home count stay; its COLLECT +5 chip shows",
                  Codex.IsRewardPending(inA[0]) && panel.TabNewDot(tabA).gameObject.activeSelf && home.BadgeShown == "3" &&
                  panel.CollectEntryFrame.gameObject.activeSelf && panel.CollectEntryLabel.text == "COLLECT +5" && !panel.CollectTabFrame.gameObject.activeSelf);
            panel.Back();
            Check("back to the grid: the card dot is still there", panel.CardNewDot(ia).gameObject.activeSelf && panel.CollectTabFrame.gameObject.activeSelf);

            // ---- reload persistence ----
            Codex.Reload();
            Check("reload: the same state (derived from the save)", Codex.PendingIn(catA) == 2 && Codex.PendingIn(catB) == 1 && TabDot(panel, tabA) && TabDot(panel, tabB));

            // ---- collect one entry through the detail button (a real click) ----
            panel.ShowDetail(inA[0]);
            int saves = PrefsSaver.SaveCount;
            panel.CollectEntryButton.onClick.Invoke();
            Check("collect entry: pays exactly 5, ONE save carrying both the mark and the dust", Dust() == 105f && PrefsSaver.SaveCount == saves + 1 &&
                  PlayerPrefs.GetString(Codex.PaidKey).Contains(inA[0].id));
            panel.CollectEntryButton.onClick.Invoke();
            Codex.CollectReward(inA[0].id);
            Check("collect entry again: pays nothing (idempotent)", Dust() == 105f && !Codex.IsRewardPending(inA[0]) && !panel.CollectEntryFrame.gameObject.activeSelf);
            Check("collect entry: the counter flashes +5 STAR DUST", panel.Counter.text == "+5 STAR DUST");
            panel.Back();
            Check("per-tab independence: tab A keeps its dot (one reward left), tab B untouched, home 2",
                  panel.TabNewDot(tabA).gameObject.activeSelf && panel.TabNewDot(tabB).gameObject.activeSelf && home.BadgeShown == "2" &&
                  !panel.CardNewDot(ia).gameObject.activeSelf && panel.CardNewDot(ib).gameObject.activeSelf);

            // ---- collect the rest of tab A with COLLECT ALL ----
            Check("tab chip now reads COLLECT ALL +5", panel.CollectTabLabel.text == "COLLECT ALL +5");
            panel.CollectTabButton.onClick.Invoke();
            Check("collect all (tab A): +5, tab A's dot and chip clear, tab B's stays, home 1",
                  Dust() == 110f && !panel.TabNewDot(tabA).gameObject.activeSelf && !panel.CollectTabButton.interactable && panel.CollectTabLabel.text == "COLLECT ALL" &&
                  panel.TabNewDot(tabB).gameObject.activeSelf && home.BadgeShown == "1" && Codex.PendingIn(catB) == 1);
            Codex.Reload();
            Check("reload: tab A stays clear, tab B still pending", Codex.PendingIn(catA) == 0 && Codex.PendingIn(catB) == 1 && !TabDot(panel, tabA) && TabDot(panel, tabB));

            panel.ShowCategory(catB);
            Check("view tab B: the bubble stays until collected", home.BadgeVisible && panel.TabNewDot(tabB).gameObject.activeSelf);
            panel.CollectTabButton.onClick.Invoke();
            Check("collect tab B: dot gone, bubble hidden, total paid 15", Dust() == 115f && !panel.TabNewDot(tabB).gameObject.activeSelf && !home.BadgeVisible);

            // ---- developer mode shows and pays nothing ----
            PlayerPrefs.SetString(Codex.PrefsKey, inA[0].id + "," + inB.id + ",music_world_ember");
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();
            DeveloperUnlocks.SetEnabled(true);
            Check("developer mode: nothing pending, nothing collectable", Codex.PendingTotal == 0 && Codex.CollectAllRewards() == 0 && Dust() == 115f);
            DeveloperUnlocks.SetEnabled(false);
            Check("developer mode off: the real pending rewards are back", Codex.PendingTotal == 2);
            Codex.CollectAllRewards();
            PlayerPrefs.SetString(Codex.PrefsKey, "");
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();
            PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);

            // ---- the cloud save carries it, merges by union and never un-collects ----
            Codex.Discover(inA[0].id); Codex.Discover(inB.id);
            Codex.CollectReward(inA[0].id);
            var snap = ProgressSnapshot.Capture(1);
            Check("snapshot: carries the collected id only", snap.codexPaid.Length == 1 && snap.codexPaid[0] == inA[0].id);
            var other = ProgressSnapshot.Capture(2);
            other.codexPaid = new[] { inB.id };
            var merged = ProgressMerge.Merge(snap, other);
            Check("merge: collected rewards union", merged.codexPaid.Contains(inA[0].id) && merged.codexPaid.Contains(inB.id));
            var old = ProgressSnapshot.Capture(3);
            old.codexPaid = new string[0];
            old.Apply();
            Codex.Reload();
            Check("restoring an older save without the list never un-collects (no double pay)", !Codex.IsRewardPending(inA[0]) && Codex.CollectReward(inA[0].id) == 0);
            merged.Apply();
            Codex.Reload();
            Check("restoring a merged save: both collected", Codex.PendingTotal == 0);
            PlayerPrefs.SetString(Codex.PrefsKey, "");
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();

            // ---- ACHIEVEMENTS: dot while claimable, viewing does not clear, claiming does ----
            AchievementStore.ResetAll();
            PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
            var def = AchievementCatalog.All.First(d => AchievementCatalog.IsActive(d));
            AchievementStore.Unlock(def);
            panel.Refresh();
            Check("achievements: a claimable one lights the tab dot and the bubble",
                  panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf && home.BadgeVisible);
            int before = CodexHomeButton.Total();
            panel.ShowAchievements();
            Check("achievements: opening the tab does NOT clear its dot or the count",
                  panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf && CodexHomeButton.Total() == before && before >= 1 && home.BadgeVisible);
            Codex.Reload();
            Check("achievements: still lit after a reload (derives from the save)", CodexPanel.TabHasReward(CodexPanel.AchievementsTab));
            AchievementStore.Claim(def);
            panel.OnAchievementsChanged();   // what the achievements view does after a claim
            panel.Refresh();
            Check("achievements: the dot and the bubble clear when it is claimed", !panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf && CodexHomeButton.Total() == before - 1 && !home.BadgeVisible);
            Check("music tab never carries a dot", !panel.TabNewDot(CodexPanel.MusicTab).gameObject.activeSelf);

            // no touches caught, on every visible card and tab
            panel.ShowCategory(catA);
            bool noRay = true;
            for (int i = 0; i < CodexPanel.TabCount; i++) noRay &= !panel.TabNewDot(i).raycastTarget;
            for (int i = 0; i < panel.VisibleCards; i++) noRay &= !panel.CardNewDot(i).raycastTarget;
            Check("dots take no touches (raycastTarget off, tabs and cards still do)", noRay && panel.CardButton(0).targetGraphic.raycastTarget);

            // dots sit inside their card / tab on every layout
            bool inside = true;
            Codex.Discover(open.First(e => e.category == catA && !Codex.IsDiscovered(e) && !IsOwnedKind(e)).id);
            foreach (var safe in new[] { new Rect(-400f, -640f, 800f, 1280f), new Rect(-400f, -1067f, 800f, 2133f), new Rect(-1138f, -640f, 2276f, 1280f), new Rect(-380f, -700f, 760f, 1500f) })
            {
                panel.ApplyLayout(safe);
                panel.Refresh();
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < panel.VisibleCards; i++)
                    if (panel.CardNewDot(i).gameObject.activeSelf)
                        inside &= InsideWithMargin(panel.CardRect(i), panel.CardNewDot(i).rectTransform, 5f);
                inside &= InsideWithMargin((RectTransform)panel.TabNewDot(tabA).transform.parent, panel.TabNewDot(tabA).rectTransform, 5f);
                Canvas.ForceUpdateCanvases();
            }
            Check("dots sit fully inside their card / tab (>= 5 units from every edge) on 4 layouts", inside);
            var dotSize = panel.TabNewDot(tabA).rectTransform.rect.size;
            float dsf = panel.GetComponent<Canvas>().rootCanvas.scaleFactor, dpx = dotSize.x * dsf / 13f;
            Check("dots are 9.5..22 units (checked at a real canvas scale only), a whole multiple of the 13 px art (" + dotSize.x + " units x" + dsf + " = " + dpx + " art px multiples)",
                  dsf < .7f || (dotSize.x >= 9.5f && dotSize.x <= 22f && Mathf.Abs(dpx - Mathf.Round(dpx)) < .03f));

            // (last: the fit rig swaps scenes) every FitDevice shape: every card and tab dot inside its box with a margin, clear of the tab label;
            // the collect chips inside the panel and clear of the title / counter / pill
            fitBad = 0; fitCells = 0; fitFirst = "";
            var fit = new FitScreen { id = "codex-dots", scene = "startS4", title = "dots", fullBleed = true, stage = DotsStage };
            // gate lane: the shape extremes (smallest, Flip7, widest, tablet); full lane: every FitDevice
            var dotDevices = new[] { "flip7-1080x2520", "and-480x854", "fold-1812x2176", "ipad-pro" };
            int wantCells = TestHarness.FullLane ? 21 : dotDevices.Length;
            foreach (var device in FitDevice.All)
                if (TestHarness.FullLane || System.Array.IndexOf(dotDevices, device.id) >= 0)
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(fit, device, null, 0);
                    fitCells++;
                    if (shot.error != null) { fitBad++; if (fitFirst == "") fitFirst = device.id + ": " + shot.error; }
                }
            Check("dots and collect chips fit on all " + fitCells + " device sizes" + (fitFirst == "" ? "" : " (" + fitFirst + ")"), fitBad == 0 && fitCells >= wantCells);

        }
        finally
        {
            var homeBtn = host != null ? host.GetComponentInChildren<CodexHomeButton>() : null;
            if (homeBtn != null) typeof(CodexHomeButton).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(homeBtn, null);
            if (host != null) Object.DestroyImmediate(host);
            if (panel != null) { panel.Close(); panel.SkipAnimations(); Object.DestroyImmediate(panel.gameObject); }
            Object.DestroyImmediate(esGo);
            AchievementStore.ResetAll();
            AchievementStores.Current = null;
        }
        UnityEngine.Debug.Log("[CND] failures: " + fails);
        return fails;
    }

    static bool SameColor(Color a, Color b) { return Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f; }

    static bool TabDot(CodexPanel panel, int tab) { panel.Refresh(); return panel.TabNewDot(tab).gameObject.activeSelf; }

    static int fitBad, fitCells;
    static string fitFirst = "";

    static void DotsStage(ScreenFitRig rig)
    {
        ScreenFitScreens.HomeBase(rig);
        // every entry discovered for real (not developer mode, so the rewards are pending everywhere): the fullest grids
        DeveloperUnlocks.SetEnabled(false);
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", Codex.Entries.Select(e => e.id)));
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        Codex.Reload();
        var panel = CodexPanel.Open(null);
        rig.Sync();
        panel.Refresh();
        panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
        string why = null;
        foreach (var tab in CodexPanel.Tabs)
        {
            panel.ShowCategory(tab);
            panel.SkipAnimations();
            panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < panel.VisibleCards && why == null; i++)
            {
                var dot = panel.CardNewDot(i);
                dot.gameObject.SetActive(true);
                if (!InsideWithMargin(panel.CardRect(i), dot.rectTransform, 5f)) why = tab + " card " + i + ": dot not inside with margin";
            }
            if (why != null) break;
            // COLLECT ALL: the Achievements-style bar under the tab buttons, clear of the tabs and the list, label fits
            var chip = panel.CollectTabFrame;
            if (!chip.gameObject.activeSelf) { why = tab + ": COLLECT ALL button hidden on a category tab"; break; }
            if (chip.GetComponent<UnityEngine.UI.Button>().interactable != (Codex.PendingIn(tab) > 0)) { why = tab + ": COLLECT ALL enable state does not follow the pending rewards"; break; }
            {
                var panelRt = panel.Panel;
                var lay = panel.CurrentLayout;
                var cr = chip.rectTransform;
                var local = new Rect(cr.anchoredPosition.x - cr.rect.width * .5f, cr.anchoredPosition.y - cr.rect.height * .5f, cr.rect.width, cr.rect.height);
                if (!InsideWithMargin(panelRt, chip.rectTransform, 2f)) why = tab + ": COLLECT ALL button outside the panel";
                else if (local.yMax > lay.collectBar.yMax + .5f || local.yMin < lay.collectBar.yMin - .5f) why = tab + ": COLLECT ALL button not inside the bar under the tabs";
                else if (lay.collectBar.yMax > lay.tabs.yMin + .5f) why = tab + ": COLLECT ALL bar is not under the tabs";
                else if (lay.catBody.yMax > lay.collectBar.yMin + .5f) why = tab + ": the list starts above the COLLECT ALL bar";
                else if (cr.rect.height < 40f || cr.rect.width < 150f) why = tab + ": COLLECT ALL button too small";
            }
        }
        // the entry detail's COLLECT chip: inside the detail, clear of the pill, right of it
        if (why == null)
        {
            panel.ShowCategory(CodexPanel.Tabs[1]);
            panel.SkipAnimations();
            panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
            for (int i = 0; i < panel.VisibleCards && why == null; i += 3)
            {
                panel.ShowDetail(panel.CardEntry(i));
                Canvas.ForceUpdateCanvases();
                var chip = panel.CollectEntryFrame;
                if (!chip.gameObject.activeSelf) why = "detail " + i + ": COLLECT chip hidden with the reward pending";
                else if (!InsideWithMargin(panel.Panel, chip.rectTransform, 2f)) why = "detail " + i + ": COLLECT chip outside the panel";
                else if (WorldRect(chip.rectTransform).Overlaps(WorldRect(panel.DetailPill.rectTransform))) why = "detail " + i + ": COLLECT chip overlaps the category pill";
                else if (WorldRect(chip.rectTransform).Overlaps(WorldRect(panel.DetailLoreCard.rectTransform))) why = "detail " + i + ": COLLECT chip overlaps the lore card";
                panel.Back();
            }
        }
        if (why == null)
        {
            panel.ShowCategory(CodexPanel.Tabs[0]);
            float sf = panel.GetComponent<Canvas>().rootCanvas.scaleFactor, du = panel.TabNewDot(0).rectTransform.rect.width, px = du * sf / 13f;
            if (du < 9.5f || du > 22f || Mathf.Abs(px - Mathf.Round(px)) > .03f) why = "dot size " + du + " units x" + sf + " is not a 9.5..22 unit whole multiple of 13 px";
        }
        for (int t = 0; t < CodexPanel.TabCount && why == null; t++)
        {
            var dot = panel.TabNewDot(t);
            dot.gameObject.SetActive(true);
            var box = (RectTransform)dot.transform.parent;
            if (!InsideWithMargin(box, dot.rectTransform, 5f)) why = "tab " + t + ": dot not inside with margin";
            else if (OverlapsLabel(panel.TabLabel(t), dot.rectTransform)) why = "tab " + t + ": dot covers the label";
        }
        if (why != null) { throw new System.Exception(why); }
    }

    // Glyph extent of the label's rendered text vs the dot, in the label's local space.
    static bool OverlapsLabel(Text label, RectTransform dot)
    {
        var rect = label.rectTransform.rect.size;
        var gen = new TextGenerator();
        gen.Populate(label.text, label.GetGenerationSettings(rect));
        var v = gen.verts;
        if (v.Count < 4) return false;
        float ppu = Mathf.Max(.01f, label.pixelsPerUnit);
        Rect text = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        foreach (var vert in v)
        {
            text.xMin = Mathf.Min(text.xMin, vert.position.x / ppu); text.xMax = Mathf.Max(text.xMax, vert.position.x / ppu);
            text.yMin = Mathf.Min(text.yMin, vert.position.y / ppu); text.yMax = Mathf.Max(text.yMax, vert.position.y / ppu);
        }
        var c = new Vector3[4];
        dot.GetWorldCorners(c);
        Rect d = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        foreach (var w in c)
        {
            var l = label.rectTransform.InverseTransformPoint(w);
            d.xMin = Mathf.Min(d.xMin, l.x); d.xMax = Mathf.Max(d.xMax, l.x);
            d.yMin = Mathf.Min(d.yMin, l.y); d.yMax = Mathf.Max(d.yMax, l.y);
        }
        return d.Overlaps(text);
    }

    // inner fully inside outer, at least `margin` of outer's own units from every edge
    static bool InsideWithMargin(RectTransform outer, RectTransform inner, float margin)
    {
        var c = new Vector3[4];
        inner.GetWorldCorners(c);
        Rect o = outer.rect;
        foreach (var w in c)
        {
            var l = outer.InverseTransformPoint(w);
            if (l.x < o.xMin + margin || l.x > o.xMax - margin || l.y < o.yMin + margin || l.y > o.yMax - margin) return false;
        }
        return true;
    }

    // ships and worlds are discovered by ownership / progress, not by Discover()
    static bool IsOwnedKind(CodexEntry e) { return e.category == CodexCategory.Ships || e.category == CodexCategory.Worlds; }

    static int CardOf(CodexPanel panel, string id)
    {
        for (int i = 0; i < panel.VisibleCards; i++) if (panel.CardEntry(i) != null && panel.CardEntry(i).id == id) return i;
        return -1;
    }

    static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    static bool Within(RectTransform outer, RectTransform inner, float e)
    {
        Rect o = WorldRect(outer), i = WorldRect(inner);
        return i.xMin >= o.xMin - e && i.xMax <= o.xMax + e && i.yMin >= o.yMin - e && i.yMax <= o.yMax + e;
    }
}
