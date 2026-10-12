using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The Codex BACKDROPS tab and the full-screen backdrop viewer: which backdrops exist (a card per world
// variant), when one unlocks (shown in play, persisted in the codex's own PrefsKey, never counted in the
// entry totals / NEW dots / Completionist), locked cards reading "???", tapping an unlocked card opening
// the viewer, swipes and arrows stepping through the unlocked backdrops (wrapping), BACK returning to
// the list, Storm's lightning staying inside its caps in the viewer, and that closing the viewer (or the
// codex) releases the set, the camera, the RenderTexture and the canvas.
public static class CodexBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CBD] PASS  " : "[CBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly (string name, Rect safe)[] Screens =
    {
        ("phone 800x1280", new Rect(-400f, -640f, 800f, 1280f)),
        ("tall phone 800x1733", new Rect(-400f, -866f, 800f, 1733f)),
        ("notch phone (insets)", new Rect(-400f, -800f, 800f, 1560f)),
        ("small phone 360x640 dp", new Rect(-300f, -533f, 600f, 1066f)),
        ("iPad 960x1280", new Rect(-480f, -640f, 960f, 1280f)),
        ("landscape Mac 2276x1280", new Rect(-1138f, -640f, 2276f, 1280f)),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        PlayerPrefs.DeleteKey(Codex.NewKey);
        PlayerPrefs.DeleteKey(Codex.AckKey);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        Codex.Reload();
        bool tide = WorldManager.TideEnabled, storm = WorldManager.StormEnabled, desert = WorldManager.DesertEnabled;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var es = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        try
        {
            Catalogue();
            Listing();
            Unlocking();
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            panel.ApplyLayout(Screens[0].safe);
            Tab(panel);
            Viewer(panel);
            Navigation(panel);
            StormLightning(panel);
            Releasing(panel);
            Layouts(panel);
        }
        finally
        {
            if (CodexBackdropViewer.IsOpen) CodexBackdropViewer.Current.Close();
            if (panel != null) Object.DestroyImmediate(panel.gameObject);
            Object.DestroyImmediate(es);
            WorldManager.TideEnabled = tide;
            WorldManager.StormEnabled = storm;
            WorldManager.DesertEnabled = desert;
            DeveloperUnlocks.SetEnabled(false);
            BackNavigator.ResetHooks();
        }
        Debug.Log("[CBD] failures: " + fails);
        return fails;
    }

    static CodexBackdrop B(string world, int variant) { return CodexBackdrops.Find(world, variant); }

    static void Catalogue()
    {
        var all = CodexBackdrops.All;
        var ids = new HashSet<string>();
        bool unique = true, thumbs = true, titles = true, aligned = true;
        foreach (var b in all)
        {
            unique &= ids.Add(b.id);
            thumbs &= Resources.Load<Texture2D>(b.ThumbResource) != null;
            titles &= !string.IsNullOrEmpty(b.title) && b.title != b.world + " " + b.variant;
            aligned &= WorldManager.Worlds[b.worldIndex].displayName == b.world;
        }
        Check("every world has four backdrops (Space's four skies, each planet's four variant sets): " + all.Count, all.Count == WorldManager.Worlds.Length * 4);
        Check("ids are unique and every card thumbnail loads", unique && thumbs);
        Check("each has its own name and sits under its world, in world order", titles && aligned &&
              all.Select(b => b.worldIndex).SequenceEqual(all.Select(b => b.worldIndex).OrderBy(i => i)));
        Check("names are unique within a world", all.GroupBy(b => b.world).All(g => g.Select(b => b.title).Distinct().Count() == g.Count()));
        Check("ids follow bd_<world>_<variant>", B("Frost", 2).id == "bd_frost_2" && B("Space", 4).id == "bd_space_4");
        Check("Storm and Desert have theirs (Storm's eye, Desert's salt flat)", B("Storm", 3).title == "Eye of the Storm" && B("Desert", 3).title == "Salt Flat");
    }

    static void Listing()
    {
        WorldManager.TideEnabled = WorldManager.StormEnabled = WorldManager.DesertEnabled = false;
        Check("worlds the loop cannot reach are not listed: " + CodexBackdrops.ListedCount, CodexBackdrops.ListedCount == WorldManager.LiveWorldCount * 4 && !CodexBackdrops.IsListed(B("Desert", 1)));
        WorldManager.TideEnabled = WorldManager.StormEnabled = WorldManager.DesertEnabled = true;
        Check("with every world live all are listed", CodexBackdrops.ListedCount == CodexBackdrops.All.Count && CodexBackdrops.IsListed(B("Desert", 4)));
        DeveloperUnlocks.SetEnabled(true);
        WorldManager.TideEnabled = WorldManager.StormEnabled = WorldManager.DesertEnabled = false;
        Check("developer mode lists the dormant worlds too", CodexBackdrops.IsListed(B("Desert", 1)));
        DeveloperUnlocks.SetEnabled(false);
        WorldManager.TideEnabled = WorldManager.StormEnabled = WorldManager.DesertEnabled = true;   // the rest runs with every world live
    }

    static void Unlocking()
    {
        int total = Codex.Total, found = Codex.DiscoveredCount, unacked = Codex.UnackedNewCount, homeBefore = CodexHomeButton.Total();
        Check("a fresh profile has none unlocked, all read ???", CodexBackdrops.UnlockedCount == 0 && CodexBackdrops.DisplayTitle(B("Frost", 2)) == "???");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
        Check("reaching a world unlocks none of its backdrops (only being shown them in play does)", CodexBackdrops.UnlockedCount == 0);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);

        Check("showing Frost's variant 2 in play unlocks it (first time true)", CodexBackdrops.SeenInPlay("Frost", 2));
        Check("... under its name and only it", CodexBackdrops.IsUnlocked(B("Frost", 2)) && CodexBackdrops.DisplayTitle(B("Frost", 2)) == "Coast and Harbour" &&
              !CodexBackdrops.IsUnlocked(B("Frost", 1)) && CodexBackdrops.UnlockedCount == 1);
        Check("... stored in the codex's own pref", PlayerPrefs.GetString(Codex.PrefsKey).Contains("bd_frost_2"));
        string before = PlayerPrefs.GetString(Codex.PrefsKey);
        Check("seeing it again changes nothing", !CodexBackdrops.SeenInPlay("Frost", 2) && PlayerPrefs.GetString(Codex.PrefsKey) == before);
        Codex.Reload();
        Check("... and it survives a reload", CodexBackdrops.IsUnlocked(B("Frost", 2)));
        Check("backdrops never count in the totals, NEW dots or the home count (total " + Codex.Total + "/" + total + ", found " + Codex.DiscoveredCount + "/" + found + ", unacked " + Codex.UnackedNewCount + "/" + unacked + ", home " + CodexHomeButton.Total() + "/" + homeBefore + ")",
              Codex.Total == total && Codex.DiscoveredCount == found && Codex.UnackedNewCount == unacked && !Codex.IsNew("bd_frost_2") && CodexHomeButton.Total() == homeBefore);
        Check("an unknown variant is ignored", !CodexBackdrops.SeenInPlay("Frost", 9) && !CodexBackdrops.SeenInPlay("Nowhere", 1));

        // the game wiring: WorldBackdrop.Show reports the set it built; the viewer's own sets must not
        string src = System.IO.File.ReadAllText("Assets/Scripts/Worlds/Backdrop/WorldBackdrop.cs");
        Check("WorldBackdrop.Show marks the shown backdrop (Space: the run's sky; planets: the picked variant)",
              src.Contains("CodexBackdrops.SeenInPlay(next.Spec.world, next.Spec.world == \"Space\" ? SpaceSkySelection.Variant : next.Variant)"));
        string menu = System.IO.File.ReadAllText("Assets/Scripts/Worlds/Backdrop/MenuBackdrop.cs");
        Check("the menu backdrop records nothing", !menu.Contains("CodexBackdrops") && !menu.Contains("SeeBackdrop"));

        // a cloud restore / reset reaches it through the same key
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.OnExternalChange();
        Check("a reset codex locks them again", CodexBackdrops.UnlockedCount == 0);

        DeveloperUnlocks.SetEnabled(true);
        Check("developer mode shows every backdrop without writing", CodexBackdrops.IsUnlocked(B("Storm", 4)) && PlayerPrefs.GetString(Codex.PrefsKey) == "");
        CodexBackdrops.SeenInPlay("Storm", 4);
        Check("... and records nothing", PlayerPrefs.GetString(Codex.PrefsKey) == "");
        DeveloperUnlocks.SetEnabled(false);
        Check("turning it off brings the real set back", !CodexBackdrops.IsUnlocked(B("Storm", 4)));

        // three seen backdrops for the rest: Space 1, Frost 2, Storm 4 (world order)
        CodexBackdrops.SeenInPlay("Space", 1);
        CodexBackdrops.SeenInPlay("Frost", 2);
        CodexBackdrops.SeenInPlay("Storm", 4);
        Check("the viewer's list is the unlocked ones in world order",
              string.Join(",", CodexBackdrops.UnlockedList().Select(b => b.id)) == "bd_space_1,bd_frost_2,bd_storm_4");
    }

    static void Tab(CodexPanel panel)
    {
        Check("nine tabs: ... ACHIEVEMENTS, MUSIC, then BACKDROPS", CodexPanel.TabCount == 9 && CodexPanel.BackdropsTab == 8 &&
              panel.TabLabel(CodexPanel.BackdropsTab).text == "BACKDROPS");
        panel.ShowTab(CodexPanel.BackdropsTab);
        panel.SkipAnimations();
        var v = panel.Backdrops;
        Check("the tab opens its view and hides the grid", panel.BackdropsOpen && v.Active && !panel.MusicOpen && !panel.AchievementsOpen);
        Check("one card per listed backdrop under a header per world", v.VisibleCards == CodexBackdrops.ListedCount && v.VisibleHeaders == WorldManager.LiveWorldCount);
        int i = v.CardIndexOf("bd_frost_2"), j = v.CardIndexOf("bd_frost_1");
        Check("an unlocked card shows its name, art and can be tapped",
              v.CardName(i).text == "Coast and Harbour" && v.CardButton(i).interactable && v.CardThumb(i).texture != null && !v.CardLock(i).gameObject.activeSelf);
        Check("a locked card shows ??? a lock and an ink silhouette, and cannot be tapped",
              v.CardName(j).text == "???" && !v.CardButton(j).interactable && v.CardThumb(j).texture == null && v.CardLock(j).gameObject.activeSelf &&
              v.CardThumb(j).color == CodexUi.Silhouette);
        Check("only the unlocked cards load their textures", v.LoadedThumbnails == CodexBackdrops.UnlockedCount);
        Check("the header counts seen backdrops", panel.Counter.text.StartsWith(CodexBackdrops.UnlockedCount + " / " + CodexBackdrops.ListedCount) && panel.Counter.text.EndsWith("SEEN"));
        Check("section counters read found/total", v.HeaderCounter(1).text == "1/4" && v.HeaderLabel(1).text == "FROST");
        Check("the tab never shows a NEW dot", !panel.TabNewDot(CodexPanel.BackdropsTab).gameObject.activeSelf);
        Check("the strip invites a tap", v.StripLine.text == CodexBackdropsView.IdleLine);

        panel.ShowTab(CodexPanel.MusicTab);
        panel.SkipAnimations();
        Check("leaving the tab releases the thumbnails and the view", !panel.BackdropsOpen && !v.Active && v.LoadedThumbnails == 0);
        panel.ShowTab(CodexPanel.BackdropsTab);
        panel.SkipAnimations();
        panel.ShowCategory(CodexPanel.Tabs[1]);
        panel.SkipAnimations();
        Check("a category tab takes the grid back from it", !panel.BackdropsOpen && !v.Active && v.LoadedThumbnails == 0 && panel.VisibleCards > 0);
        panel.ShowTab(CodexPanel.BackdropsTab);
        panel.SkipAnimations();
    }

    static int Cameras() { return Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length; }
    static int Targets() { return Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t.name == "CodexBackdropViewer"); }
    static int Roots() { return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(t => t.name.StartsWith("Backdrop_")); }

    static float Spread(RenderTexture rt, out float mean)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = prev;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        double sum = 0, sq = 0; int n = 0;
        for (int k = 0; k < px.Length; k += 7) { float l = (px[k].r + px[k].g + px[k].b) / 765f; sum += l; sq += l * l; n++; }
        mean = (float)(sum / n);
        return Mathf.Sqrt(Mathf.Max(0f, (float)(sq / n - (sum / n) * (sum / n))));
    }

    static void Viewer(CodexPanel panel)
    {
        var v = panel.Backdrops;
        int cams = Cameras(), targets = Targets(), roots = Roots();
        Check("a locked card does nothing", CodexBackdropViewer.Open(null, B("Frost", 1)) == null && !CodexBackdropViewer.IsOpen);
        v.CardButton(v.CardIndexOf("bd_frost_1")).onClick.Invoke();
        Check("... tapping it opens no viewer", !CodexBackdropViewer.IsOpen);

        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        var viewer = CodexBackdropViewer.Current;
        Check("tapping an unlocked card opens the viewer on it", viewer != null && CodexBackdropViewer.IsOpen && viewer.CurrentBackdrop.id == "bd_frost_2");
        Check("it shows the real backdrop: Frost's own set with its director, variant 2",
              viewer.Set != null && viewer.Set.Complete && viewer.Set.Spec.world == "Frost" && viewer.Set.Variant == 2 && viewer.Set.Director is FrostDirector);
        var frost = viewer.Set.Director as FrostDirector;
        Check("the planetfall's cloud ceiling is already gone (the backdrop itself is in view)", frost != null && frost.CeilingCover < .1f);
        Check("the title and position read out", viewer.Title.text == "Coast and Harbour" && viewer.Sub.text == "FROST   2 / 3");
        int lit = viewer.Dots.Count(d => d.gameObject.activeSelf);
        Check("four dots for Frost's variants, the shown one lit and the others dim",
              lit == 4 && viewer.Dots[1].color == CodexUi.Title && viewer.Dots[1].rectTransform.localScale.x > 1.2f && viewer.Dots[0].color != CodexUi.Title);
        Check("arrows and BACK are there", viewer.PrevButton.gameObject.activeSelf && viewer.NextButton.gameObject.activeSelf && viewer.BackButton != null);
        Check("it draws into its own RenderTexture through its own camera (the scene and the codex are untouched)",
              viewer.Target != null && viewer.Target.IsCreated() && viewer.ViewCamera != null && !viewer.ViewCamera.enabled && viewer.ViewCamera.targetTexture == viewer.Target &&
              Cameras() == cams + 1 && Targets() == targets + 1 && Roots() == roots + 1);
        for (int k = 0; k < 20; k++) viewer.Step(1f / 30f);
        float mean, spread = Spread(viewer.Target, out mean);
        Check("the frame shows the backdrop, not a blank (mean " + mean.ToString("0.00") + ", spread " + spread.ToString("0.000") + ")", mean > .03f && spread > .02f);
        Check("it covers the whole screen and takes the touches (the codex underneath cannot be tapped)",
              viewer.ViewImage.raycastTarget && viewer.ViewImage.rectTransform.anchorMin == Vector2.zero && viewer.ViewImage.rectTransform.anchorMax == Vector2.one);
        Check("the viewer is above the codex panel", viewer.GetComponent<Canvas>().sortingOrder > panel.GetComponent<Canvas>().sortingOrder);
        Check("it scrolls slowly (the run's start rate), on unscaled time", WorldBackdrop.ScrollVelocity(0f) <= 8f && viewer.SecondsShown > .5f);
        Check("the Android back / Escape layer is the viewer's", BackNavigator.Top == (object)viewer);

        // BACK goes back to the list
        viewer.BackButton.onClick.Invoke();
        Check("BACK closes it and the Codex list is still there", !CodexBackdropViewer.IsOpen && panel.IsOpen && panel.BackdropsOpen && panel.Backdrops.Active);
        Check("... with everything released (camera, RenderTexture, backdrop objects)", Cameras() == cams && Targets() == targets && Roots() == roots);
        Check("the codex owns Back again", BackNavigator.Top == (object)panel);

        // the system back does the same
        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        Check("it opens again", CodexBackdropViewer.IsOpen);
        BackNavigator.Back();
        Check("the system back closes the viewer first, not the codex", !CodexBackdropViewer.IsOpen && panel.IsOpen && panel.BackdropsOpen);
    }

    static PointerEventData Pointer(Vector2 pos)
    {
        return new PointerEventData(EventSystem.current) { position = pos, pressPosition = pos, button = PointerEventData.InputButton.Left };
    }

    static void Swipe(CodexBackdropViewer viewer, Vector2 from, Vector2 to)
    {
        var go = viewer.ViewImage.gameObject;
        ExecuteEvents.Execute(go, Pointer(from), ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(go, Pointer(to), ExecuteEvents.pointerUpHandler);
    }

    static void Navigation(CodexPanel panel)
    {
        Check("swipe rule: left = next, right = previous, short / vertical / diagonal = nothing",
              CodexBackdropViewer.SwipeDirection(new Vector2(-400f, 20f), 1080f) == 1 && CodexBackdropViewer.SwipeDirection(new Vector2(400f, -20f), 1080f) == -1 &&
              CodexBackdropViewer.SwipeDirection(new Vector2(-60f, 0f), 1080f) == 0 && CodexBackdropViewer.SwipeDirection(new Vector2(-300f, 400f), 1080f) == 0 &&
              CodexBackdropViewer.SwipeDirection(new Vector2(-300f, 250f), 1080f) == 0 && CodexBackdropViewer.SwipeDirection(new Vector2(-70f, 0f), 400f) == 1);

        var v = panel.Backdrops;
        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        var viewer = CodexBackdropViewer.Current;
        Check("three unlocked: Space 1, Frost 2, Storm 4", viewer.Count == 3 && viewer.Index == 1);

        Swipe(viewer, new Vector2(800f, 1200f), new Vector2(200f, 1180f));
        Check("a real swipe left starts the step to the next backdrop", viewer.Dipping);
        viewer.Close();

        // deterministic stepping from here on
        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        viewer = CodexBackdropViewer.Current;
        viewer.MoveNow(1);
        Check("next from Frost 2 is Storm 4: its own set, director and name",
              viewer.CurrentBackdrop.id == "bd_storm_4" && viewer.Set.Spec.world == "Storm" && viewer.Set.Variant == 4 && viewer.Set.Director is StormDirector &&
              viewer.Title.text == "Night Side" && viewer.Sub.text == "STORM   3 / 3");
        Check("only one set is alive after a step", Roots() == 1);
        viewer.MoveNow(1);
        Check("next from the last wraps to the first (Space, its sky variant 1)", viewer.CurrentBackdrop.id == "bd_space_1" && viewer.Set.Spec.world == "Space" && viewer.Set.Director is SpaceDirector && viewer.Index == 0);
        viewer.MoveNow(-1);
        Check("previous from the first wraps to the last", viewer.CurrentBackdrop.id == "bd_storm_4");
        viewer.MoveNow(-1);
        Check("previous steps back", viewer.CurrentBackdrop.id == "bd_frost_2" && viewer.Set.Variant == 2);
        for (int k = 0; k < 10; k++) viewer.Step(1f / 30f);
        float mean, spread = Spread(viewer.Target, out mean);
        Check("each step renders (mean " + mean.ToString("0.00") + ")", mean > .03f && spread > .02f);

        // the on-screen arrows (an invisible hit area each)
        viewer.NextButton.onClick.Invoke();
        Check("the next arrow starts a step", viewer.Dipping);
        viewer.Close();

        // the real thing: the pointer events of an arrow tap
        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        viewer = CodexBackdropViewer.Current;
        ExecuteEvents.Execute(viewer.PrevButton.gameObject, Pointer(Vector2.zero), ExecuteEvents.pointerClickHandler);
        Check("a tap on the previous arrow starts a step", viewer.Dipping);
        viewer.Close();

        // one unlocked backdrop: nothing to step to, no arrows
        PlayerPrefs.SetString(Codex.PrefsKey, "bd_frost_2");
        Codex.Reload();
        panel.Refresh();
        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        viewer = CodexBackdropViewer.Current;
        Check("with a single unlocked backdrop the arrows are hidden and a swipe does nothing",
              viewer.Count == 1 && !viewer.PrevButton.gameObject.activeSelf && !viewer.NextButton.gameObject.activeSelf);
        viewer.MoveNow(1);
        Check("... and stepping stays put", viewer.CurrentBackdrop.id == "bd_frost_2");
        viewer.Close();

        // restore the three
        PlayerPrefs.SetString(Codex.PrefsKey, "bd_space_1,bd_frost_2,bd_storm_4");
        Codex.Reload();
        panel.Refresh();
    }

    // The viewer runs the director's own lightning schedule: its caps hold there too.
    static void StormLightning(CodexPanel panel)
    {
        var v = panel.Backdrops;
        PlayerPrefs.SetString(Codex.PrefsKey, "bd_space_1,bd_frost_2,bd_storm_2,bd_storm_4");
        Codex.Reload();
        panel.Refresh();
        foreach (string id in new[] { "bd_storm_2", "bd_storm_4" })
        {
            v.CardButton(v.CardIndexOf(id)).onClick.Invoke();
            var viewer = CodexBackdropViewer.Current;
            var storm = viewer.Set.Director as StormDirector;
            Check(id + ": the viewer's Storm backdrop is the real StormDirector", storm != null);
            if (storm == null) { viewer.Close(); continue; }
            // 120 s of viewing at 30 fps: no light above the caps, no more than 3 pulses in any second
            int worst = 0, last = storm.Flashes, seconds = 120;
            var perSecond = new Queue<int>();
            float peak = 0f;
            for (int sec = 0; sec < seconds; sec++)
            {
                int before = storm.Flashes;
                for (int f = 0; f < 30; f++)
                {
                    viewer.Set.Tick(1f / 30f, WorldBackdrop.ScrollVelocity(0f));
                    peak = Mathf.Max(peak, storm.FlashLevel);
                }
                int pulses = storm.Flashes - before;
                worst = Mathf.Max(worst, pulses);
                last = storm.Flashes;
            }
            Check(id + ": lightning flashed in 120 s of viewing (" + last + " pulses) yet stayed within its caps (peak " + peak.ToString("0.00") + ", worst second " + worst + " pulses)",
                  last > 0 && peak <= 1.001f && worst <= 3);
            viewer.Close();
        }
        PlayerPrefs.SetString(Codex.PrefsKey, "bd_space_1,bd_frost_2,bd_storm_4");
        Codex.Reload();
        panel.Refresh();
    }

    static void Releasing(CodexPanel panel)
    {
        var v = panel.Backdrops;
        int cams = Cameras(), targets = Targets(), roots = Roots();
        var objs = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        for (int round = 0; round < 3; round++)
        {
            v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
            var viewer = CodexBackdropViewer.Current;
            viewer.MoveNow(1);
            viewer.MoveNow(1);
            for (int k = 0; k < 5; k++) viewer.Step(1f / 30f);
            viewer.Close();
        }
        Check("open / step / close three times leaks nothing (cameras, RenderTextures, backdrop roots, scene objects)",
              Cameras() == cams && Targets() == targets && Roots() == roots &&
              Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == objs);
        Check("close twice is harmless", !CodexBackdropViewer.IsOpen);

        // closing the codex while the viewer is up takes the viewer with it
        v.CardButton(v.CardIndexOf("bd_frost_2")).onClick.Invoke();
        Check("viewer up", CodexBackdropViewer.IsOpen);
        panel.Close();
        panel.SkipAnimations();
        Check("closing the codex closes the viewer and releases the thumbnails", !CodexBackdropViewer.IsOpen && Cameras() == cams && Targets() == targets &&
              Roots() == roots && v.LoadedThumbnails == 0);
        Check("leaving the codex hid the tab: reopening starts on the list again", !panel.BackdropsOpen);
        CodexPanel.Open(CodexUi.FindFont());
        panel.SkipAnimations();
        panel.ApplyLayout(Screens[0].safe);
    }

    static bool Inside(Rect outer, Rect inner) { return inner.xMin >= outer.xMin - .5f && inner.xMax <= outer.xMax + .5f && inner.yMin >= outer.yMin - .5f && inner.yMax <= outer.yMax + .5f; }

    static void Layouts(CodexPanel panel)
    {
        foreach (var (name, safe) in Screens)
        {
            panel.ApplyLayout(safe);
            Canvas.ForceUpdateCanvases();
            var l = panel.CurrentLayout;
            // the tab row, nine tabs in two rows
            bool tabs = true, hit = true, fit = true;
            for (int i = 0; i < CodexPanel.TabCount; i++)
            {
                var t = CodexPanel.TabRect(l, i);
                tabs &= Inside(l.tabs, t);
                for (int j = 0; j < i; j++) tabs &= !CodexPanel.TabRect(l, j).Overlaps(t);
                hit &= t.width + CodexPanel.TabGap >= 96f && t.height + CodexPanel.TabRowGap >= 96f;
            }
            Check(name + ": nine tabs (5 + 4) sit in the tab band, no overlap, each a >= 96-unit touch target", tabs && hit);

            panel.ShowTab(CodexPanel.BackdropsTab);
            panel.SkipAnimations();
            var v = panel.Backdrops;
            panel.ApplyLayout(safe);
            Canvas.ForceUpdateCanvases();
            bool cards = true, names = true;
            for (int i = 0; i < v.VisibleCards; i++)
            {
                var r = v.CardRect(i);
                cards &= r.sizeDelta.x >= 100f && r.sizeDelta.y >= 130f;
                names &= !string.IsNullOrEmpty(v.CardName(i).text);
            }
            Check(name + ": the cards are finger-sized and named, the list scrolls (" + v.ContentHeight.ToString("0") + " tall)", cards && names && v.ContentHeight > 0f);

            // the viewer
            DeveloperUnlocks.SetEnabled(true);
            var b = B("Storm", 2);
            var viewer = CodexBackdropViewer.Open(CodexUi.FindFont(), b);
            viewer.ApplyLayout(safe);
            Canvas.ForceUpdateCanvases();
            var vl = CodexBackdropViewer.ComputeLayout(safe);
            bool inside = Inside(safe, vl.back) && Inside(safe, vl.title) && Inside(safe, vl.sub) && Inside(safe, vl.dots) && Inside(safe, vl.prev) && Inside(safe, vl.next);
            Check(name + ": the viewer's controls sit inside the safe area", inside);
            var parts = new[] { vl.back, vl.title, vl.sub, vl.dots, vl.prev, vl.next };
            bool clear = true;
            for (int i = 0; i < parts.Length; i++) for (int j = 0; j < i; j++) clear &= !parts[i].Overlaps(parts[j]);
            Check(name + ": ... and none overlap each other", clear);
            Check(name + ": BACK is a >= 80-unit touch target and each arrow >= 96", vl.back.height >= 80f && vl.back.width >= 160f && vl.prev.width >= 96f && vl.prev.height >= 96f && vl.next.width >= 96f);
            var br = Drawn(panel, viewer.BackRect, viewer);
            Check(name + ": the laid-out BACK matches", Mathf.Abs(br.center.x - vl.back.center.x) < 1f && Mathf.Abs(br.center.y - vl.back.center.y) < 1f);
            Check(name + ": the title fits on one line or shrinks to fit (never truncated)", FitsAt(viewer.Title, 13) && FitsAt(viewer.Sub, 11));
            Check(name + ": the target matches the screen's aspect", Mathf.Abs((float)viewer.Target.width / viewer.Target.height - (float)ScreenInfo.Width / ScreenInfo.Height) < .02f);
            viewer.Close();
            DeveloperUnlocks.SetEnabled(false);
        }
        panel.ApplyLayout(Screens[0].safe);
    }

    static Rect Drawn(CodexPanel panel, RectTransform rt, CodexBackdropViewer viewer)
    {
        var root = (RectTransform)viewer.transform;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var a = root.InverseTransformPoint(corners[0]);
        var c = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(a.x, a.y, c.x, c.y);
    }

    static bool FitsAt(Text t, int minPx)
    {
        var rect = t.rectTransform.rect.size;
        var settings = t.GetGenerationSettings(rect);
        settings.resizeTextForBestFit = false;
        settings.fontSize = minPx;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        float ppu = Mathf.Max(t.pixelsPerUnit, .0001f);
        return t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, settings) / ppu <= rect.y + .5f;
    }
}
