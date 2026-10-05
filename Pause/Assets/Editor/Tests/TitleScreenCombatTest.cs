using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Home-screen traffic, the lively parts: ultimates (about one flight in
// seven), shoot-downs that crash into the screen-edge walls, ships diving
// into the PAUSE logo and the menu buttons, the finger pushing ships around,
// and every ship flying all of its skins. All cosmetic: no gameplay state is
// read or written.
//
// Driven through TitleScreenTraffic.Step(dt) / FeedPointer on a fixed step
// and a seeded Random, so the long-run checks are deterministic.
public static class TitleScreenCombatTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TC] PASS  " : "[TC] FAIL  ") + what);
        if (!ok) fails++;
    }

    const float Dt = 1f / 30f;
    static readonly Vector3 LogoPos = new Vector3(-0.04f, 2.72f, 0f);
    static readonly Vector3 LogoScale = new Vector3(0.4176109f, 0.46091294f, 1f);
    const string LogoPath = "Assets/Art/pause_title_2.png";

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        // edit mode never runs the menu's layout pass; do it once, the way
        // the first frame of Play does, so the buttons have their real rects
        var panel = GameObject.Find("UIPanel");
        var scaler = panel != null ? panel.GetComponentInParent<CanvasScaler>() : null;
        if (scaler != null)
            typeof(CanvasScaler).GetMethod("Handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(scaler, null);
        Canvas.ForceUpdateCanvases();
        // The batch editor's screen is 640x480 (landscape); the menu is laid
        // out for a phone, where the panel's -992 inset leaves it ~900 tall.
        // Give it a phone-like height for this run (the scene isn't saved).
        var panelRt = panel != null ? (RectTransform)panel.transform : null;
        if (panelRt != null && panelRt.rect.height <= 0f) panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, -300f);
        if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        Canvas.ForceUpdateCanvases();
        if (panel != null)
        {
            var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
            Debug.Log("[TC] menu canvas " + canvas.name + " " + ((RectTransform)canvas.transform).rect + " scale " + canvas.scaleFactor +
                      " scaler " + (scaler != null ? scaler.uiScaleMode + " ref " + scaler.referenceResolution : "none") +
                      " panel " + ((RectTransform)panel.transform).rect);
        }

        LongRunRatesAndCaps();
        UltimatesUseTheShipsOwnLook();
        ShotsOnlyHitTheirOwnLayerAndCrashIntoWalls();
        LogoCrashLeavesTheLogoAlone();
        ButtonCrashLeavesButtonsTappable();
        FingerPushesShips();
        NoPushWhenTouchStartsOnAButton();
        EverySkinFlies();
        NoGameplaySideEffects();
        NoPerFrameAllocations();

        Debug.Log("[TC] failures: " + fails);
        return fails;
    }

    static TitleScreenTraffic Make(string name, int seed, System.Action<TitleScreenTraffic> configure = null)
    {
        Random.InitState(seed);
        var go = new GameObject(name);
        var t = go.AddComponent<TitleScreenTraffic>();
        configure?.Invoke(t);
        t.Init();
        return t;
    }

    static void Done(TitleScreenTraffic t) { t.Shutdown(); Object.DestroyImmediate(t.gameObject); }

    static void Run(TitleScreenTraffic t, float seconds)
    {
        for (float s = 0f; s < seconds; s += Dt) t.Step(Dt);
    }

    // Launches ship `id` on `layer` at `at`, cruising along `heading`.
    static TitleScreenTraffic.Flyer LaunchId(TitleScreenTraffic t, int id, TitleScreenTraffic.Depth layer, Vector2 at, float heading)
    {
        TitleScreenTraffic.Flyer target = null;
        foreach (var f in t.Pool) if (f.id == id) target = f;
        if (target == null) return null;
        if (target.active) { target.active = false; target.go.SetActive(false); }
        // Launch takes a random free hull; walk until it lands on this one
        TitleScreenTraffic.Flyer g = null;
        for (int guard = 0; guard < 400 && g != target; guard++)
        {
            g = t.Launch(layer, true, false, null);
            if (g != null && g != target) { g.active = false; g.go.SetActive(false); }
        }
        if (g != target) return null;
        g.pos = at;
        g.heading = heading;
        g.waypoint = at + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 3f;
        g.waypointsLeft = 0;
        g.state = TitleScreenTraffic.State.Cruise;
        g.trick = TitleScreenTraffic.Trick.None;
        g.nextBoost = g.nextTrick = 1e9f;
        g.ultAt = float.MaxValue;
        g.wobA = 0f;
        return g;
    }

    static TitleScreenTraffic Quiet(string name, int seed)
    {
        var t = Make(name, seed, x =>
        {
            x.layerTargets = new[] { 0, 0, 0 };
            x.zoomInterval = new Vector2(1e6f, 1e6f);
            x.formationInterval = new Vector2(1e6f, 1e6f);
            x.plungeInterval = new Vector2(1e6f, 1e6f);
        });
        t.NextCrashAt = 1e9f;
        t.NextPlungeAt = 1e9f;
        foreach (var f in t.Pool) if (f.active) { f.active = false; f.go.SetActive(false); }
        return t;
    }

    // ------------------------------------------------------------------

    static void LongRunRatesAndCaps()
    {
        var t = Make("~TC_long", 4242);
        var logo = GameObject.Find("menuTitle").GetComponent<SpriteRenderer>();
        bool ultCap = true, wreckCap = true, boltCap = true, timeScale = true;
        float ts = Time.timeScale;
        const float Seconds = 900f;
        for (float s = 0f; s < Seconds; s += Dt)
        {
            t.Step(Dt);
            ultCap &= t.ActiveUlts <= TitleScreenTraffic.MaxUlts;
            wreckCap &= t.ActiveWrecks <= TitleScreenTraffic.MaxWrecks;
            boltCap &= t.ActiveBolts <= TitleScreenTraffic.BoltPool;
            timeScale &= Time.timeScale == ts;
        }
        float planned = t.UltsPlanned / (float)t.Flights, fired = t.UltsFired / (float)t.Flights;
        Debug.Log("[TC] long run: flights " + t.Flights + ", ults planned " + t.UltsPlanned + " fired " + t.UltsFired +
                  ", shoot-downs " + t.ShootDowns + ", wrecks " + t.Wrecks + ", wall hits " + t.WallImpacts + ", pops " + t.Pops +
                  ", plunges " + t.Plunges + ", logo " + t.LogoCrashes + ", buttons " + t.ButtonCrashes + ", crashes " + t.Crashes +
                  ", skin swaps " + t.SkinSwaps);
        Check("about 15% of flights carry an ultimate (" + (planned * 100f).ToString("0.0") + "%)", planned >= .11f && planned <= .19f);
        Check("and most of those let it go (" + (fired * 100f).ToString("0.0") + "% of flights)", fired >= .09f && fired <= .19f);
        Check("never more than " + TitleScreenTraffic.MaxUlts + " ultimates at once (peak " + t.PeakUlts + ")", ultCap && t.PeakUlts <= TitleScreenTraffic.MaxUlts);
        int kinds = 0;
        for (int i = 0; i < t.AttacksFired.Length; i++) if (t.AttacksFired[i] > 0) kinds++;
        Check("many different weapons go off (" + kinds + " of " + t.AttacksFired.Length + ")", kinds >= 10);
        Check("projectile pool stays capped", boltCap);
        Check("ultimates never touch Time.timeScale (no slow motion on the menu)", timeScale);
        Check("shots bring ships down (" + t.ShootDowns + ")", t.ShootDowns >= 10);
        Check("only ever on the shooter's own layer", t.CrossLayerKills == 0);
        Check("shot ships wreck into the walls (" + t.Wrecks + " wrecks, " + t.WallImpacts + " pieces hit)", t.Wrecks >= 5 && t.WallImpacts >= 15);
        Check("every piece lands on a screen edge", t.WallImpactsOffEdge == 0);
        Check("at most " + TitleScreenTraffic.MaxWrecks + " wrecks fly at once (peak " + t.PeakWrecks + ")", wreckCap && t.PeakWrecks <= TitleScreenTraffic.MaxWrecks);
        Check("ships now and then dive into the logo (" + t.LogoCrashes + ")", t.LogoCrashes >= 3);
        Check("or a menu button (" + t.ButtonCrashes + ")", t.ButtonCrashes >= 1);
        float plungeRate = (t.LogoCrashes + t.ButtonCrashes) / (float)t.Flights;
        Check("logo/button crashes stay rare (" + (plungeRate * 100f).ToString("0.0") + "% of flights)", plungeRate > 0f && plungeRate <= .1f);
        Check("the usual cute crashes keep happening (" + t.Crashes + ")", t.Crashes >= 40);
        Check("logo transform untouched by the long run", logo.transform.position == LogoPos &&
              (logo.transform.localScale - LogoScale).sqrMagnitude < 1e-10f && logo.transform.rotation == Quaternion.identity);
        Done(t);
    }

    static void UltimatesUseTheShipsOwnLook()
    {
        // every ship's ultimate, fired once, draws from that ship's own art
        bool own = true, fires = true;
        string bad = null;
        foreach (int id in ShipId.All)
        {
            var t = Quiet("~TC_look", 100 + id);
            var c = t.Safe.center;
            var f = LaunchId(t, id, TitleScreenTraffic.Depth.Mid, c + new Vector2(-1f, -1.5f), 0f);
            if (f == null) { fires = false; bad = "launch " + id; Done(t); continue; }
            f.ultAt = t.Now;
            int before = t.UltsFired;
            bool sawOwn = false, sawOther = false;
            for (int i = 0; i < 30; i++)
            {
                t.Step(Dt);
                foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sr.enabled || sr.sprite == null || (sr.name != "~bolt" && sr.name != "~ultfx")) continue;
                    if (sr.sprite == WeaponFx.Solid) continue;
                    bool mine = IsOwnArt(sr.sprite, id);
                    if (mine) sawOwn = true; else sawOther = true;
                }
            }
            fires &= t.UltsFired == before + 1;
            if (!sawOwn || sawOther) { own = false; bad = ShipId.KeyOf(id) + (sawOther ? " drew another ship's art" : " drew nothing"); }
            Done(t);
        }
        Check("every ship's ultimate goes off on cue", fires);
        Check("each draws only its own weapon art (WeaponArt / ShipFxArt)" + (bad != null ? " (" + bad + ")" : ""), own);
    }

    static bool IsOwnArt(Sprite s, int id)
    {
        for (int i = 0; i < WeaponArt.ShotFrames; i++) if (WeaponArt.Shot(id, i) == s) return true;
        for (int i = 0; i < 4; i++)
            if (ShipFxArt.AttackLoop(id, i) == s || ShipFxArt.AttackBurst(id, i) == s) return true;
        return false;
    }

    static void ShotsOnlyHitTheirOwnLayerAndCrashIntoWalls()
    {
        var t = Quiet("~TC_rail", 7);
        var c = t.Safe.center;
        Vector2 at = new Vector2(t.View.xMin + 1f, c.y - 1.8f);
        // Crimson Halo's rail gun: an instant line along its heading
        var shooter = LaunchId(t, 4, TitleScreenTraffic.Depth.Mid, at, 0f);
        var same = LaunchId(t, 1, TitleScreenTraffic.Depth.Mid, at + new Vector2(2f, 0f), Mathf.PI * .5f);
        var back = LaunchId(t, 2, TitleScreenTraffic.Depth.Back, at + new Vector2(2.6f, 0f), Mathf.PI * .5f);
        var front = LaunchId(t, 3, TitleScreenTraffic.Depth.Front, at + new Vector2(3.1f, 0f), Mathf.PI * .5f);
        Check("rail test ships placed", shooter != null && same != null && back != null && front != null);
        if (shooter == null || same == null || back == null || front == null) { Done(t); return; }
        shooter.ultAt = t.Now;
        shooter.aim = null;
        int downs = t.ShootDowns;
        for (int i = 0; i < 15; i++)
        {
            // keep everyone on the line while the rail winds up
            same.pos = new Vector2(same.pos.x, shooter.pos.y);
            back.pos = new Vector2(back.pos.x, shooter.pos.y);
            front.pos = new Vector2(front.pos.x, shooter.pos.y);
            t.Step(Dt);
        }
        Check("the rail gun fired (" + t.UltsFired + ")", t.UltsFired == 1 && t.AttacksFired[(int)ShipAttack.RailSlug] == 1);
        Check("it brought down the ship on its own layer", !same.active && t.ShootDowns == downs + 1);
        Check("and passed straight through the back- and front-layer ships", back.active && front.active);
        Check("no cross-layer hit recorded", t.CrossLayerKills == 0);
        Check("the shot ship broke into wreck fragments", t.Wrecks == 1);
        int landed = t.WallImpacts;
        Run(t, 2f);
        Check("its pieces slammed into the screen-edge walls (" + (t.WallImpacts - landed) + ")",
              t.WallImpacts - landed >= DeathCrash.MinFragments && t.WallImpactsOffEdge == 0);
        Run(t, 2f);
        Check("and the wreck cleared away", t.ActiveWrecks == 0);
        Done(t);
    }

    static Rect LogoRectOf(TitleScreenTraffic t) { return t.LogoRect; }

    static void LogoCrashLeavesTheLogoAlone()
    {
        var logo = GameObject.Find("menuTitle").GetComponent<SpriteRenderer>();
        Sprite sprite = logo.sprite;
        Color color = logo.color;
        int order = logo.sortingOrder;
        bool crashed = false, quick = true, intact = true, frontOk = true;
        float longest = 0f;
        for (int seed = 0; seed < 40 && !crashed; seed++)
        {
            var t = Quiet("~TC_logo", 300 + seed);
            if (t.LogoRect.width <= 0f) { Done(t); break; }
            var f = LaunchId(t, 6, TitleScreenTraffic.Depth.Front, t.LogoRect.center + new Vector2(-1.6f, -1.6f), .8f);
            if (f == null || !t.StartPlunge(f, false) || f.plungeInto != -1) { Done(t); continue; }
            Check("a diving ship draws over the logo (sorting " + f.hull.sortingOrder + " > " + order + ")", f.hull.sortingOrder > order && f.hull.sortingOrder < TitleScreenTraffic.Depths[2].sortBase);
            for (int i = 0; i < 150 && t.LogoCrashes == 0; i++)
            {
                t.Step(Dt);
                intact &= logo.transform.position == LogoPos && logo.sprite == sprite && logo.color == color;
            }
            if (t.LogoCrashes == 0) { Done(t); continue; }
            crashed = true;
            float hit = t.LastLogoImpactAt;
            Check("the impact lands on the logo", t.LogoRect.Contains(f.tr.position));
            Check("something is drawn over the logo at the impact", t.ImpactRenderersOn(t.LogoRect) > 0);
            for (int i = 0; i < 60; i++)
            {
                t.Step(Dt);
                if (t.ImpactRenderersOn(t.LogoRect) > 0) longest = t.Now - hit;
                intact &= logo.transform.position == LogoPos && logo.sprite == sprite && logo.color == color &&
                          (logo.transform.localScale - LogoScale).sqrMagnitude < 1e-10f && logo.transform.rotation == Quaternion.identity &&
                          logo.sortingOrder == order && logo.enabled;
            }
            quick = longest <= TitleScreenTraffic.LogoFxMax;
            frontOk = !f.active;
            Done(t);
        }
        Check("a ship dives into the PAUSE logo", crashed);
        Check("the impact clears off the logo within " + TitleScreenTraffic.LogoFxMax + " s (" + longest.ToString("0.00") + " s)", crashed && quick);
        Check("the ship is gone (its pieces fell away)", frontOk);
        Check("logo sprite, colour, sorting and transform never change", intact);
        Check("logo art untouched on disk", File.Exists(LogoPath));
    }

    struct ButtonState
    {
        public Vector2 anchored, size, pivot;
        public Vector3 scale, labelScale;
        public Quaternion rotation;
        public Sprite sprite;
        public bool interactable, raycast, enabled;
    }

    static ButtonState Capture(Button b)
    {
        var rt = (RectTransform)b.transform;
        var img = b.GetComponent<Image>();
        var label = b.GetComponentInChildren<Text>(true);
        return new ButtonState
        {
            anchored = rt.anchoredPosition, size = rt.sizeDelta, pivot = rt.pivot, scale = rt.localScale, rotation = rt.localRotation,
            labelScale = label != null ? label.transform.localScale : Vector3.one,
            sprite = img != null ? img.sprite : null, raycast = img != null && img.raycastTarget,
            interactable = b.interactable, enabled = b.isActiveAndEnabled,
        };
    }

    static bool Same(ButtonState a, ButtonState b, bool label)
    {
        return a.anchored == b.anchored && a.size == b.size && a.pivot == b.pivot && a.scale == b.scale && a.rotation == b.rotation &&
               a.sprite == b.sprite && a.raycast == b.raycast && a.interactable == b.interactable && a.enabled == b.enabled &&
               (!label || a.labelScale == b.labelScale);
    }

    static Vector2 ScreenCentre(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return (c[0] + c[2]) * .5f;
    }

    static void ButtonCrashLeavesButtonsTappable()
    {
        bool crashed = false, frameOk = true, tappable = true, settled = false, wobbled = false;
        string which = "";
        for (int seed = 0; seed < 60 && !crashed; seed++)
        {
            var t = Quiet("~TC_button", 500 + seed);
            if (t.ButtonCount == 0) { Done(t); break; }
            if (seed == 0)
                for (int i = 0; i < t.ButtonCount; i++)
                    Debug.Log("[TC] button " + t.ButtonAt(i).name + " world rect " + t.ButtonRect(i) + " safe " + t.Safe +
                              " screen " + Screen.width + "x" + Screen.height);
            var f = LaunchId(t, 1, TitleScreenTraffic.Depth.Front, t.Safe.center + new Vector2(-2f, -2.5f), .5f);
            if (f == null || !t.StartPlunge(f, false) || f.plungeInto < 0) { Done(t); continue; }
            var rt = t.ButtonAt(f.plungeInto);
            var button = rt.GetComponent<Button>();
            which = button.name;
            var before = Capture(button);
            var all = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            var befores = new ButtonState[all.Length];
            for (int i = 0; i < all.Length; i++) befores[i] = Capture(all[i]);
            for (int i = 0; i < 150 && t.ButtonCrashes == 0; i++) t.Step(Dt);
            if (t.ButtonCrashes == 0) { Done(t); continue; }
            crashed = true;
            for (int i = 0; i < 20; i++)
            {
                // mid-impact: same rect, same image, same raycast target, a
                // touch on it still counts as a tap on UI (never a push)
                frameOk &= Same(before, Capture(button), false);
                tappable &= button.interactable && t.StartsOnUi(0, ScreenCentre(rt));
                wobbled |= t.ButtonWobbling(f.plungeInto);
                t.Step(Dt);
            }
            settled = !t.ButtonWobbling(f.plungeInto) && Same(before, Capture(button), true);
            for (int i = 0; i < all.Length; i++) settled &= Same(befores[i], Capture(all[i]), true);
            bool noUi = true;
            foreach (var tr in t.GetComponentsInChildren<Transform>(true))
                noUi &= tr.gameObject.layer == 2 && tr.GetComponent<Graphic>() == null && tr.GetComponent<Collider2D>() == null;
            Check("impact fx are world sprites on Ignore Raycast, never UI or colliders", noUi);
            Done(t);
        }
        Check("a ship dives into a menu button (" + which + ")", crashed);
        Check("its label gives a cel wobble", wobbled);
        Check("mid-impact the button's rect, image, raycast target and interactable state are unchanged", crashed && frameOk);
        Check("mid-impact it stays tappable", crashed && tappable);
        Check("afterwards every button is exactly as it was (label scale too)", crashed && settled);
    }

    static void FingerPushesShips()
    {
        var t = Quiet("~TC_push", 11);
        var c = t.Safe.center + new Vector2(0f, -2.2f);
        var near = LaunchId(t, 1, TitleScreenTraffic.Depth.Front, c + new Vector2(.4f, 0f), Mathf.PI * .5f);
        var far = LaunchId(t, 2, TitleScreenTraffic.Depth.Front, c + new Vector2(-1.1f, 0f), Mathf.PI * .5f);
        var outOf = LaunchId(t, 3, TitleScreenTraffic.Depth.Front, c + new Vector2(0f, 2.6f), Mathf.PI * .5f);
        Check("push test ships placed", near != null && far != null && outOf != null);
        if (near == null || far == null || outOf == null) { Done(t); return; }
        foreach (var f in new[] { near, far, outOf }) f.baseSpeed = 0f;
        Vector2 screen = WorldToScreen(t, c);
        Check("an empty spot of sky is not UI", !t.StartsOnUi(0, screen));
        var n0 = near.pos; var f0 = far.pos; var o0 = outOf.pos;
        for (int i = 0; i < 10; i++) { t.FeedPointer(0, 0, true, screen, Dt); t.Step(Dt); }
        float dn = Vector2.Dot(near.pos - n0, Vector2.right), df = Vector2.Dot(far.pos - f0, Vector2.left);
        Check("a still finger pushes nearby ships away (" + dn.ToString("0.00") + ", " + df.ToString("0.00") + ")", dn > .02f && df > .005f);
        Check("closer ships are pushed harder", dn > df);
        Check("ships beyond its reach don't move", (outOf.pos - o0).sqrMagnitude < 1e-8f);
        t.FeedPointer(0, 0, false, screen, Dt);

        // same set-up, a fast swipe past the ship shoves it further
        var t2 = Quiet("~TC_push2", 11);
        var s2 = LaunchId(t2, 1, TitleScreenTraffic.Depth.Front, c + new Vector2(.4f, 0f), Mathf.PI * .5f);
        s2.baseSpeed = 0f;
        var start = s2.pos;
        for (int i = 0; i < 10; i++)
        {
            var p = c + new Vector2(0f, -.6f + i * .12f);
            t2.FeedPointer(0, 0, true, WorldToScreen(t2, p), Dt);
            t2.Step(Dt);
        }
        float swipe = (s2.pos - start).magnitude;
        Check("a fast swipe shoves harder than a still finger (" + swipe.ToString("0.00") + " vs " + dn.ToString("0.00") + ")", swipe > dn * 1.15f);
        t2.FeedPointer(0, 0, false, Vector2.zero, Dt);
        Run(t2, 2f);
        Check("let go, the shove dies away (" + s2.push.magnitude.ToString("0.000") + ")", s2.push.magnitude < .02f);
        Done(t2);

        // back ships feel it less than front ones
        var t3 = Quiet("~TC_push3", 11);
        var fr = LaunchId(t3, 1, TitleScreenTraffic.Depth.Front, c + new Vector2(.5f, 0f), Mathf.PI * .5f);
        var bk = LaunchId(t3, 2, TitleScreenTraffic.Depth.Back, c + new Vector2(.5f, 0f), Mathf.PI * .5f);
        fr.baseSpeed = bk.baseSpeed = 0f;
        Vector2 a = fr.pos, b = bk.pos;
        for (int i = 0; i < 10; i++) { t3.FeedPointer(0, 0, true, screen, Dt); t3.Step(Dt); }
        Check("far (back) ships feel the finger less", (bk.pos - b).magnitude < (fr.pos - a).magnitude);

        // two fingers push independently
        var g = LaunchId(t3, 5, TitleScreenTraffic.Depth.Front, c + new Vector2(4.5f, 2.2f), Mathf.PI * .5f);
        g.baseSpeed = 0f;
        var g0 = g.pos;
        for (int i = 0; i < 10; i++)
        {
            t3.FeedPointer(0, 0, true, screen, Dt);
            t3.FeedPointer(1, 1, true, WorldToScreen(t3, c + new Vector2(4f, 2.2f)), Dt);
            t3.Step(Dt);
        }
        Check("a second finger pushes its own ships (multitouch: " + t3.PointersDown + " down, moved " + (g.pos - g0) + ")", t3.PointersDown == 2 && (g.pos - g0).x > .02f);
        Done(t3);
        Done(t);
    }

    static Vector2 WorldToScreen(TitleScreenTraffic t, Vector2 w)
    {
        var v = t.View;
        return new Vector2((w.x - v.x) / v.width * Screen.width, (w.y - v.y) / v.height * Screen.height);
    }

    static void NoPushWhenTouchStartsOnAButton()
    {
        var t = Quiet("~TC_nopush", 12);
        var play = GameObject.Find("PlayButton");
        Check("PLAY is on the home screen", play != null);
        if (play == null) { Done(t); return; }
        var rt = (RectTransform)play.transform;
        Vector2 screen = ScreenCentre(rt);
        Vector2 world = t.View.min + new Vector2(screen.x / Screen.width * t.View.width, screen.y / Screen.height * t.View.height);
        var f = LaunchId(t, 1, TitleScreenTraffic.Depth.Front, world + new Vector2(.4f, 0f), Mathf.PI * .5f);
        f.baseSpeed = 0f;
        Vector2 p0 = f.pos;
        Check("a touch on PLAY counts as UI", t.StartsOnUi(0, screen));
        for (int i = 0; i < 10; i++)
        {
            // starts on the button, then drags off it over open sky
            t.FeedPointer(0, 0, true, screen + new Vector2(0f, i * 25f), Dt);
            t.Step(Dt);
        }
        Check("the touch is marked as on UI for its whole life", t.PointerOnUi(0));
        Check("no ship is pushed by a touch that began on a button", (f.pos - p0).sqrMagnitude < 1e-8f && f.push == Vector2.zero);
        t.FeedPointer(0, 0, false, screen, Dt);
        Done(t);
    }

    static void EverySkinFlies()
    {
        var t = Make("~TC_skins", 99);
        Run(t, 600f);
        int combos = 0, shipsWithMore = 0, ships = 0;
        foreach (int id in ShipId.All)
        {
            ships++;
            int n = 0;
            for (int s = 0; s < ShipSkins.PerShip; s++) if (t.SkinFlown(id, s)) { n++; combos++; }
            if (n > 1) shipsWithMore++;
        }
        var sb = new System.Text.StringBuilder();
        foreach (int id in ShipId.All)
        {
            sb.Append(ShipId.KeyOf(id)).Append(':');
            for (int s = 0; s < ShipSkins.PerShip; s++) if (t.SkinFlown(id, s)) sb.Append(s);
            sb.Append(' ');
        }
        Debug.Log("[TC] skins flown: " + sb + " swaps " + t.SkinSwaps + " work " + t.SkinWork);
        Check("a broad mix of skins flies by (" + combos + " of " + ships * ShipSkins.PerShip + " ship x skin looks)", combos >= ships * 3);
        Check("every ship shows more than one skin (" + shipsWithMore + "/" + ships + ")", shipsWithMore == ships);
        Check("the wardrobe keeps rotating (" + t.SkinSwaps + " swaps)", t.SkinSwaps >= 20);
        int decoded = ShipHullArt.LoadedSkinSheets;
        Check("at most one colourway sheet per ship is held (" + decoded + ")", decoded <= ShipId.Count + 1);

        // what a flight wears, everything wears
        bool hull = true, exhaust = true, wreck = true;
        int skinned = 0;
        foreach (var f in t.Pool)
        {
            if (!f.active) continue;
            if (f.skin != ShipSkins.Stock) skinned++;
            hull &= f.hull.sprite != null && f.hull.sprite.texture == ShipHullArt.SheetFor(f.id, f.skin);
            bool remapped = !ExhaustColors.IsStock(f.id, f.skin);
            foreach (var n in f.nozzles) exhaust &= ExhaustRemap.IsRemapped(n) == remapped;
            if (f.drift != null) exhaust &= ExhaustRemap.IsRemapped(f.drift.Ring) == remapped;
            var frags = f.Frags;
            wreck &= frags != null && frags.count > 0 && frags.sprites[0] != null;
        }
        Check("skinned ships are in the air (" + skinned + ")", skinned > 0);
        Check("each hull is drawn from its flight's skin sheet", hull);
        Check("its exhaust plumes / spin ring wear the same skin", exhaust);
        Check("its crash fragments are cut from that skin's wreck drawing", wreck);
        Done(t);
        Check("leaving the home screen frees the wardrobe (" + ShipHullArt.LoadedSkinSheets + " skin sheets left)",
              ShipHullArt.LoadedSkinSheets == 0);
    }

    static void NoGameplaySideEffects()
    {
        float currency = score.totalCurrency, saved = StarDustLedger.Saved;
        int dust = score.dustPickups, pauses = score.pauseCounter, codex = Codex.DiscoveredCount;
        int launches = AttackProjectile.LaunchCount;
        int discovered = 0;
        System.Action<CodexEntry> onDiscover = e => discovered++;
        Codex.Discovered += onDiscover;
        float ts = Time.timeScale;
        var t = Make("~TC_side", 31);
        Run(t, 240f);
        Codex.Discovered -= onDiscover;
        Check("ultimates went off and ships came down (" + t.UltsFired + " / " + t.ShootDowns + ")", t.UltsFired > 0 && t.ShootDowns > 0);
        Check("no currency or star dust", score.totalCurrency == currency && StarDustLedger.Saved == saved && score.dustPickups == dust);
        Check("no pauses spent or granted", score.pauseCounter == pauses);
        Check("no codex discoveries", Codex.DiscoveredCount == codex && discovered == 0);
        Check("no gameplay attacks (AttackProjectile / ~ShipAttacks)", AttackProjectile.LaunchCount == launches && GameObject.Find("~ShipAttacks") == null);
        Check("no gameplay death sequence (DeathCrash) created", DeathCrash.Instance == null);
        Check("time scale untouched, no hit-stop", Time.timeScale == ts && !WorldTimeFx.HitStopping);
        Done(t);

        // and the code can't: none of the gameplay hooks are referenced
        string[] files =
        {
            "Assets/Scripts/UI/TitleScreenTraffic.cs", "Assets/Scripts/UI/TitleScreenTraffic.Combat.cs",
            "Assets/Scripts/UI/TitleScreenTraffic.Touch.cs", "Assets/Scripts/UI/TitleScreenTraffic.Skins.cs",
        };
        string[] banned =
        {
            "ShipAttackHits", "ShipAttackRunner.Fire", "AttackPool.", "ShipTargets.", "Codex.", "score.", "StarDustLedger",
            "Achievement", "Handheld", "Vibrat", "Haptic", "timeScale =", "WorldTimeFx", "CameraKick", "DeathCrash.Begin",
            "DeathCrash.Ensure", "PlayerPrefs.Set", "ShipSkins.Equip", "SocialBridge",
        };
        string hit = null;
        foreach (var file in files)
        {
            string src = File.ReadAllText(file);
            foreach (var b in banned) if (src.Contains(b)) hit = file + ": " + b;
        }
        Check("the traffic's code calls no scoring, unlock, currency, haptic or time-scale API" + (hit != null ? " (" + hit + ")" : ""), hit == null);
    }

    static void NoPerFrameAllocations()
    {
        var t = Make("~TC_alloc", 77, x =>
        {
            x.maxShips = TitleScreenTraffic.MaxCap;
            x.layerTargets = new[] { 6, 5, 3 };
            x.plungeInterval = new Vector2(8f, 12f);
        });
        // warm-up: the wardrobe decoded, every pool grown, each weapon and
        // effect drawn once (sprites are cut on first use)
        Run(t, 240f);
        // a finger on the sky too
        Vector2 screen = new Vector2(Screen.width * .3f, Screen.height * .35f);
        System.GC.Collect();
        long counted = 0;
        int frames = 0, skipped = 0, ults = t.UltsFired, downs = t.ShootDowns, plunges = t.Plunges;
        for (int i = 0; i < 1800; i++)
        {
            int work = t.SkinWork;
            long before = System.GC.GetTotalMemory(false);
            t.FeedPointer(0, 0, (i / 90) % 2 == 0, screen + new Vector2(Mathf.Sin(i * .05f) * 200f, 0f), Dt);
            t.Step(Dt);
            long after = System.GC.GetTotalMemory(false);
            // a wardrobe swap (one skin sheet decoded, every few seconds) is
            // a scheduled load, not per-frame work: those frames are skipped
            if (t.SkinWork != work) { skipped++; continue; }
            if (after > before) counted += after - before;
            frames++;
        }
        Check("the measured minute saw ultimates, shoot-downs and dives (" + (t.UltsFired - ults) + " / " +
              (t.ShootDowns - downs) + " / " + (t.Plunges - plunges) + ")", t.UltsFired > ults && t.ShootDowns + t.Plunges > downs + plunges);
        Check("Step + touch allocate nothing per frame (" + counted + " bytes over " + frames + " frames; " + skipped +
              " wardrobe frames skipped)", counted <= 0);
        Done(t);
    }
}
