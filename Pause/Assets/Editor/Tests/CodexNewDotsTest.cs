using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// NEW dots in the Codex: a discovery marks its card, its category tab and the home bubble count;
// opening the entry clears its card dot, opening the tab clears the tab dot (and its share of the
// home count); the ACHIEVEMENTS tab dot clears on opening but the claimable count stays until claimed;
// the state survives a reload; an install that already had discoveries lights nothing up.
public static class CodexNewDotsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        UnityEngine.Debug.Log((ok ? "[CND] PASS  " : "[CND] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 1; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        bool tide = WorldManager.TideEnabled;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var esGo = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        GameObject host = null;
        try
        {
            WorldManager.TideEnabled = false;
            // candidates: undiscovered, listed, non-log entries in two categories
            var open = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret && !Codex.InFutureWorld(e)).ToList();
            var cats = open.Select(e => e.category).Distinct().ToList();
            var catA = cats.First(c => open.Count(e => e.category == c && !IsOwnedKind(e)) >= 2);
            var catB = cats.First(c => c != catA && open.Any(e => e.category == c && !IsOwnedKind(e)));
            var inA = open.Where(e => e.category == catA && !IsOwnedKind(e)).Take(2).ToArray();
            var inB = open.First(e => e.category == catB && !IsOwnedKind(e));

            // migration: discoveries that exist before the feature do not light up
            PlayerPrefs.SetString(Codex.PrefsKey, inA[0].id + "," + inB.id);
            PlayerPrefs.DeleteKey(Codex.NewKey); PlayerPrefs.DeleteKey(Codex.AckKey);
            Codex.Reload();
            Check("migration: old discoveries are not NEW and add nothing to the home count",
                  !Codex.IsNew(inA[0].id) && !Codex.IsNew(inB.id) && Codex.UnackedNewCount == 0 && !Codex.TabHasNew(catA) && CodexHomeButton.Total() == 0);
            PlayerPrefs.SetString(Codex.PrefsKey, "");
            Codex.Reload();

            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            panel.ApplyLayout(new Rect(-400f, -866f, 800f, 1733f));
            host = new GameObject("UIPanel", typeof(RectTransform));
            var go = new GameObject("CodexButton", typeof(RectTransform));
            go.transform.SetParent(host.transform, false);
            var home = go.AddComponent<CodexHomeButton>();
            home.Build();
            typeof(CodexHomeButton).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(home, null);
            int tabA = System.Array.IndexOf(CodexPanel.Tabs, catA), tabB = System.Array.IndexOf(CodexPanel.Tabs, catB);

            Check("start: no dots, bubble hidden", !home.BadgeVisible && !panel.TabNewDot(tabA).gameObject.activeSelf && Codex.NewIn(catA) == 0);

            int raised = 0;
            System.Action count = () => raised++;
            Codex.NewChanged += count;
            Codex.Discover(inA[0].id); Codex.Discover(inA[1].id); Codex.Discover(inB.id);
            Codex.NewChanged -= count;
            Check("discover: NEW raised for every discovery and recorded", raised == 3 && Codex.IsNew(inA[0].id) && Codex.IsNew(inB.id) && Codex.NewIn(catA) == 2);
            Check("discover: the home bubble shows 3 live (no manual refresh)", home.BadgeVisible && home.BadgeText.text == "3");
            panel.Refresh();
            Check("discover: both category tabs carry a dot", panel.TabNewDot(tabA).gameObject.activeSelf && panel.TabNewDot(tabB).gameObject.activeSelf &&
                  !panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf);

            // open tab A: its dot goes, the cards keep theirs, home drops by that tab's entries
            panel.ShowCategory(catA);
            int ia = CardOf(panel, inA[0].id), ib = CardOf(panel, inA[1].id);
            Check("open tab: the tab's dot is gone and the other tab's stays", !panel.TabNewDot(tabA).gameObject.activeSelf && panel.TabNewDot(tabB).gameObject.activeSelf);
            Check("open tab: home count dropped by the tab's NEW entries (3 -> 1)", home.BadgeVisible && home.BadgeText.text == "1" && CodexHomeButton.Total() == 1);
            Check("open tab: the entry cards still carry their NEW dot", ia >= 0 && ib >= 0 && panel.CardNewDot(ia).gameObject.activeSelf && panel.CardNewDot(ib).gameObject.activeSelf);

            // open an entry: its card dot clears
            panel.ShowDetail(inA[0]);
            Check("open entry: its card dot clears, the sibling keeps its dot", !Codex.IsNew(inA[0].id) && !panel.CardNewDot(ia).gameObject.activeSelf && panel.CardNewDot(ib).gameObject.activeSelf);
            Check("open entry: home count unchanged (its tab was already opened)", home.BadgeText.text == "1");
            panel.Back();

            // persistence
            Codex.Reload();
            Check("reload: the state persists (entry seen, sibling still NEW, other tab still counted)",
                  !Codex.IsNew(inA[0].id) && Codex.IsNew(inA[1].id) && Codex.IsNew(inB.id) && !Codex.TabHasNew(catA) && Codex.TabHasNew(catB) && Codex.UnackedNewCount == 1);

            // open tab B then its entry
            panel.ShowCategory(catB);
            Check("open tab B: bubble hidden (nothing claimable)", !home.BadgeVisible && Codex.UnackedNewCount == 0);
            var cb = CardOf(panel, inB.id);
            panel.ShowDetail(inB);
            Check("open entry in B clears it", !Codex.IsNew(inB.id) && cb >= 0 && !panel.CardNewDot(cb).gameObject.activeSelf);
            panel.Back();

            // no touches caught, on every visible card and tab
            bool noRay = true;
            for (int i = 0; i < CodexPanel.TabCount; i++) noRay &= !panel.TabNewDot(i).raycastTarget;
            for (int i = 0; i < panel.VisibleCards; i++) noRay &= !panel.CardNewDot(i).raycastTarget;
            Check("dots take no touches (raycastTarget off, tabs and cards still do)", noRay && panel.CardButton(0).targetGraphic.raycastTarget);

            // dots sit inside their card / tab on every layout
            bool inside = true;
            panel.ShowCategory(catA);
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
            Check("dots are about 35% smaller than the first 26-unit ones: 9.5..22 units (checked at a real canvas scale only), a whole multiple of the 13 px art (" + dotSize.x + " units x" + dsf + " = " + dpx + " art px multiples)",
                  dsf < .7f || (dotSize.x >= 9.5f && dotSize.x <= 22f && Mathf.Abs(dpx - Mathf.Round(dpx)) < .03f));

            // ACHIEVEMENTS: dot on unlock, clears on opening, claimable count stays until claimed
            AchievementStore.ResetAll();
            PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
            var def = AchievementCatalog.All.First(d => AchievementCatalog.IsActive(d));
            AchievementStore.Unlock(def);
            panel.Refresh();
            Check("achievements: a claimable one lights the tab dot and the bubble",
                  panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf && home.BadgeVisible);
            int before = CodexHomeButton.Total();
            panel.ShowAchievements();
            Check("achievements: opening the tab clears its dot but the claimable count stays",
                  !panel.TabNewDot(CodexPanel.AchievementsTab).gameObject.activeSelf && CodexHomeButton.Total() == before && before >= 1 && home.BadgeVisible);
            AchievementStore.Claim(def);
            Check("achievements: the bubble drops when it is claimed", CodexHomeButton.Total() == before - 1);

            // (last: the fit rig swaps scenes) every FitDevice shape: every card and tab dot inside its box with a margin, clear of the tab label
            fitBad = 0; fitCells = 0; fitFirst = "";
            var fit = new FitScreen { id = "codex-dots", scene = "startS4", title = "dots", fullBleed = true, stage = DotsStage };
            foreach (var device in FitDevice.All)
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(fit, device, null, 0);
                    fitCells++;
                    if (shot.error != null) { fitBad++; if (fitFirst == "") fitFirst = device.id + ": " + shot.error; }
                }
            Check("dots inside their card / tab with a margin and off the tab label on all " + fitCells + " device sizes" + (fitFirst == "" ? "" : " (" + fitFirst + ")"), fitBad == 0 && fitCells >= 21);

        }
        finally
        {
            var homeBtn = host != null ? host.GetComponentInChildren<CodexHomeButton>() : null;
            if (homeBtn != null) typeof(CodexHomeButton).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(homeBtn, null);
            if (host != null) Object.DestroyImmediate(host);
            if (panel != null) { panel.Close(); panel.SkipAnimations(); Object.DestroyImmediate(panel.gameObject); }
            Object.DestroyImmediate(esGo);
            WorldManager.TideEnabled = tide;
            AchievementStore.ResetAll();
            AchievementStores.Current = null;
        }
        UnityEngine.Debug.Log("[CND] failures: " + fails);
        return fails;
    }

    static int fitBad, fitCells;
    static string fitFirst = "";

    static void DotsStage(ScreenFitRig rig)
    {
        ScreenFitScreens.HomeBase(rig);
        DeveloperUnlocks.SetEnabled(true);   // every entry listed: the fullest grids
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
        }
        float sf = panel.GetComponent<Canvas>().rootCanvas.scaleFactor, du = panel.TabNewDot(0).rectTransform.rect.width, px = du * sf / 13f;
        if (du < 9.5f || du > 22f || Mathf.Abs(px - Mathf.Round(px)) > .03f) why = "dot size " + du + " units x" + sf + " is not a 9.5..22 unit whole multiple of 13 px";
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
