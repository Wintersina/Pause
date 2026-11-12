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
            var open = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret && CodexPayout.For(e) == CodexPayout.Atom).ToList();   // all 5-dust entries, so the amounts below are flat
            var cats = open.Select(e => e.category).Distinct().ToList();
            var catA = cats.First(c => open.Count(e => e.category == c && !IsOwnedKind(e)) >= 2);
            var catB = cats.First(c => c != catA && open.Any(e => e.category == c && !IsOwnedKind(e)));
            var inA = open.Where(e => e.category == catA && !IsOwnedKind(e)).Take(2).ToArray();
            var inB = open.First(e => e.category == catB && !IsOwnedKind(e));
            var log = Codex.Entries.First(e => e.category == CodexCategory.Log);

            Check("the candidates pay 5 star dust each", inA.All(e => CodexPayout.For(e) == 5) && CodexPayout.For(inB) == 5);

            // ---- old save: discoveries from before the feature (no PaidKey) are claimable once, never auto-paid ----
            PlayerPrefs.SetString(Codex.PrefsKey, inA[0].id + "," + inB.id + ",bd_frost_2");
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();
            Check("old save: both old entries are pending (backdrops are not entries; songs pending are covered in CodexPayoutTest), nothing auto-paid",
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
            Inject(inA[0].id); Inject(inA[1].id); Inject(inB.id);   // discoveries no payment reached (a cloud restore from an older build): pending, claimable
            Inject(inB.id);   // a repeat adds nothing
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
            Check("collect all starts a sequence, the first find is already focused or paid", panel.CollectSequenceActive || Dust() >= 110f);
            panel.FinishSequence();
            Check("collect all (tab A): +5, tab A's dot and chip clear, tab B's stays, home 1",
                  Dust() == 110f && !panel.TabNewDot(tabA).gameObject.activeSelf && !panel.CollectTabButton.interactable && panel.CollectTabLabel.text == "COLLECT ALL" &&
                  panel.TabNewDot(tabB).gameObject.activeSelf && home.BadgeShown == "1" && Codex.PendingIn(catB) == 1);
            Codex.Reload();
            Check("reload: tab A stays clear, tab B still pending", Codex.PendingIn(catA) == 0 && Codex.PendingIn(catB) == 1 && !TabDot(panel, tabA) && TabDot(panel, tabB));

            panel.ShowCategory(catB);
            Check("view tab B: the bubble stays until collected", home.BadgeVisible && panel.TabNewDot(tabB).gameObject.activeSelf);
            panel.CollectTabButton.onClick.Invoke();
            panel.FinishSequence();
            Check("collect tab B: dot gone, bubble hidden, total paid 15", Dust() == 115f && !panel.TabNewDot(tabB).gameObject.activeSelf && !home.BadgeVisible);

            // ---- music and combos: dots, COLLECT ALL, and the badge clearing ----
            var songs = CodexMusic.All.Where(t => t.id != CodexMusic.ThemeId && CodexPayout.ForExtra(t.id) > 0).Take(2).ToArray();
            string song1 = songs[0].id, song2 = songs[1].id;
            var bloom = AtomFusionTable.All[(int)FusionId.Bloom];
            int songDust = CodexPayout.ForExtra(song1) + CodexPayout.ForExtra(song2), comboDust = CodexPayout.ForExtra(CodexPayout.FusionPayId(bloom));
            Check("music candidates are real, paying tracks", CodexMusic.Find(song1) != null && CodexMusic.Find(song2) != null && songDust > 0 && comboDust > 0);
            Codex.HearTrack(song1);
            Codex.HearTrack(song2);
            PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
            FusionDiscoveryStore.Reload();
            FusionDiscoveryStore.RecordFiring(FusionId.Bloom, 1, 20261026);
            Codex.NoteFusion(bloom);
            panel.Refresh();
            float dustBefore = Dust();
            Check("music + combo unlocks light both tab dots and the home bubble (3 pending)",
                  panel.TabNewDot(CodexPanel.MusicTab).gameObject.activeSelf && panel.TabNewDot(CodexPanel.CombosTab).gameObject.activeSelf && home.BadgeShown == "3");
            panel.ShowTab(CodexPanel.MusicTab);
            Check("music tab: COLLECT ALL shows with the right amount and 2 TO COLLECT",
                  panel.CollectTabFrame.gameObject.activeSelf && panel.CollectTabButton.interactable && panel.CollectTabLabel.text == "COLLECT ALL +" + songDust);
            panel.CollectTabButton.onClick.Invoke();
            panel.FinishSequence();
            Check("music tab: collecting pays the songs once, clears its dot and the chip, combos stay",
                  Dust() == dustBefore + songDust && !panel.TabNewDot(CodexPanel.MusicTab).gameObject.activeSelf && !panel.CollectTabButton.interactable &&
                  panel.TabNewDot(CodexPanel.CombosTab).gameObject.activeSelf && home.BadgeShown == "1");
            panel.ShowTab(CodexPanel.CombosTab);
            Check("combos tab: COLLECT ALL shows with the combo's amount", panel.CollectTabFrame.gameObject.activeSelf && panel.CollectTabButton.interactable &&
                  panel.CollectTabLabel.text == "COLLECT ALL +" + comboDust);
            panel.CollectTabButton.onClick.Invoke();
            panel.FinishSequence();
            Check("combos tab: collecting pays once, clears the tab dot (opening marked it seen) and the home bubble",
                  Dust() == dustBefore + songDust + comboDust && !panel.TabNewDot(CodexPanel.CombosTab).gameObject.activeSelf && !home.BadgeVisible && Codex.PendingTotal == 0);
            Check("music/combos views sit below the collect bar, never under it", panel.MusicBodyTop <= panel.CollectBarBottom + .01f && panel.CombosBodyTop <= panel.CollectBarBottom + .01f);
            panel.ShowCategory(catA);

            // ---- the COLLECT ALL sequence: one by one, skip credits the rest, the total is the instant total ----
            PlayerPrefs.SetString(Codex.PrefsKey, inA[0].id + "," + inA[1].id + "," + inB.id);
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();
            panel.Refresh();
            panel.ShowCategory(catA);
            float seqBefore = Dust();
            panel.CollectTabButton.onClick.Invoke();
            Check("sequence: starts active, nothing paid before its first step, badges still lit", panel.CollectSequenceActive && Dust() == seqBefore && panel.TabNewDot(tabA).gameObject.activeSelf);
            panel.CollectTabButton.onClick.Invoke();
            Check("sequence: a second tap skips, credits the rest instantly and clears the badge",
                  !panel.CollectSequenceActive && Dust() == seqBefore + 10f && !panel.TabNewDot(tabA).gameObject.activeSelf && Codex.PendingIn(catA) == 0 && Codex.PendingIn(catB) == 1);
            PlayerPrefs.DeleteKey(Codex.PaidKey);
            Codex.Reload();
            panel.Refresh();
            panel.ShowCategory(catA);
            panel.CollectTabButton.onClick.Invoke();
            panel.Close();
            Check("sequence: closing the codex mid-way credits the rest too", !panel.CollectSequenceActive && Codex.PendingIn(catA) == 0);
            PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 115f);
            SequenceChecks();
            // the combo found above must not leak into the sections below (their pending counts are exact)
            PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
            FusionDiscoveryStore.Reload();

            // ---- developer mode shows and pays nothing ----
            PlayerPrefs.SetString(Codex.PrefsKey, inA[0].id + "," + inB.id);
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
            Inject(inA[0].id); Inject(inB.id);
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
            Inject(open.First(e => e.category == catA && !Codex.IsDiscovered(e) && !IsOwnedKind(e)).id);
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


    // The sequencing itself: plain C#, driven by Tick(dt).
    static void SequenceChecks()
    {
        string[] ids = { "a", "b", "c" };
        var amounts = new System.Collections.Generic.Dictionary<string, int> { { "a", 5 }, { "b", 8 }, { "c", 15 } };
        var paid = new System.Collections.Generic.List<string>();
        var focus = new System.Collections.Generic.List<string>();
        System.Func<string, int> pay = id => { paid.Add(id); return amounts[id]; };
        var seq = new CodexCollectSequence(ids, pay);
        seq.Focused = id => focus.Add(id);
        seq.Begin();
        Check("seq: begins on the first id, nothing paid", seq.Active && focus.Count == 1 && focus[0] == "a" && paid.Count == 0);
        seq.Tick(seq.StepSeconds * .5f);
        Check("seq: half a step pays nothing", paid.Count == 0);
        seq.Tick(seq.StepSeconds * .6f);
        Check("seq: step one pays a, then focuses b (in order)", paid.Count == 1 && paid[0] == "a" && focus.Count == 2 && focus[1] == "b" && seq.TotalPaid == 5);
        seq.Tick(seq.StepSeconds * 5f);
        Check("seq: a long frame plays the rest in order, each its own amount, then is done", seq.Done && !seq.Active && string.Join("", paid) == "abc" && seq.TotalPaid == 28);
        Check("seq: Finish afterwards pays nothing more", seq.Finish() == 0 && paid.Count == 3);

        paid.Clear();
        var skip = new CodexCollectSequence(ids, pay);
        skip.Begin();
        skip.Tick(skip.StepSeconds * 1.1f);
        int rest = skip.Finish();
        Check("seq: skip after one credits the others instantly, total equals the instant total", rest == 23 && skip.TotalPaid == 28 && string.Join("", paid) == "abc" && skip.Done);
        skip.Tick(5f);
        Check("seq: a finished sequence ignores Tick", paid.Count == 3);

        paid.Clear();
        var cancel = new CodexCollectSequence(ids, pay);
        Check("seq: cancel before Begin still credits everything exactly once", cancel.Finish() == 28 && paid.Count == 3 && cancel.Done && cancel.Finish() == 0);
        var empty = new CodexCollectSequence(new string[0], pay);
        empty.Begin();
        Check("seq: an empty sequence is done at once", empty.Done && empty.TotalPaid == 0);

        Check("seq: ~0.3-0.5 s per item for a few, faster for many, never below .1 s",
              CodexCollectSequence.StepSecondsFor(1) >= .3f && CodexCollectSequence.StepSecondsFor(1) <= .5f &&
              CodexCollectSequence.StepSecondsFor(40) < CodexCollectSequence.StepSecondsFor(6) && CodexCollectSequence.StepSecondsFor(500) >= .1f);
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
                else if (!lay.wide && lay.collectBar.yMax > lay.tabs.yMin + .5f) why = tab + ": COLLECT ALL bar is not under the tabs";
                else if (lay.wide && lay.collectBar.xMin < lay.tabs.xMax - .5f) why = tab + ": COLLECT ALL bar is not beside the tab rail";   // wide: the tabs are a rail on the left, the bar tops the pane
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
    // Marks an entry discovered in the save WITHOUT paying it (what a cloud restore of an older build's save does).
    static void Inject(string id)
    {
        string cur = PlayerPrefs.GetString(Codex.PrefsKey, "");
        if (!("," + cur + ",").Contains("," + id + ",")) PlayerPrefs.SetString(Codex.PrefsKey, cur.Length == 0 ? id : cur + "," + id);
        Codex.OnExternalChange();
    }

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
