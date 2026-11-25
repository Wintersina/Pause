using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The menus' background: the REAL in-game backdrop (BackdropCatalog tile
// layers, scrolled at the rate a run starts with), not a menu painting.
//
//   WHICH   Arriving at a menu from outside the menus (app start, a run, the
//           tutorial) rolls one backdrop: a random world among those the
//           player has reached (Space always; Frost / Verdant / Ember / Tide once
//           highestWorld gets there), then a random variant (v1..v4) of it,
//           never the world + variant shown last. Navigating between the
//           menu pages (home, credits, options, shop) keeps that backdrop.
//   WHAT    BackdropSet in its tiles-only mode: only the chosen variant's
//           sky / far / mid / flow textures are loaded (no atlases, no
//           director, so no set pieces or weather), graded like in a run
//           (BackdropGrade, variant brightness) and dimmed a little (Dim) so
//           the logo and buttons read. Nothing is drawn around the UI.
//   HOW     one tile-layer scroll per frame, no allocations; time is
//           unscaled (a run may have left timeScale at 0).
public static class MenuBackdropSelection
{
    public struct Pick
    {
        public string world;
        public int variant;      // 1..N; 0 for a world without variant sets
        public bool halfTurn;    // Space's sky orientation
        public bool Valid { get { return !string.IsNullOrEmpty(world); } }
        public bool Same(Pick o) { return world == o.world && variant == o.variant; }
        public override string ToString() { return world + (variant > 0 ? " v" + variant : ""); }
    }

    public static readonly string[] Scenes = { "startS4", "creditsS7", "leaderboardS3", "shopS6" };

    public static Pick Current { get; private set; }
    public static int Rolls { get; private set; }

    // The Space Dock's backdrop: ALWAYS a Space variant (the real in-game Space tiles), whatever the menu rolled. It
    // never touches Current or Rolls (leaving the dock shows the menu's own pick again, un-rolled). Kept for the
    // session so reopening the dock shows the same Space sky; `last` is the menu's pick (Space already: that very one).
    static Pick dockPick;
    public static Pick DockPick(Pick menu, System.Random rng = null)
    {
        if (menu.world == "Space") return menu;
        if (dockPick.Valid && dockPick.world == "Space") return dockPick;
        var list = new List<Pick>();
        Candidates(0, list);   // Space only
        if (list.Count == 0) return new Pick();
        dockPick = WithTurn(list[(rng ?? new System.Random()).Next(list.Count)], rng ?? new System.Random());
        return dockPick;
    }
    static string lastScene;

    public static bool IsMenuScene(string name) { return System.Array.IndexOf(Scenes, name) >= 0; }

    // The highest world index the player has reached (0 = Space only).
    public static int HighestWorld()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0), 0, WorldManager.Worlds.Length - 1);
    }

    static bool SkyInstalled(BackdropCatalog.Spec spec, int variant)
    {
        if (spec.world == "Space") return Resources.Load<Sprite>(BackdropCatalog.Folder("Space") + SpaceSkySelection.TextureFor(variant)) != null;
        if (spec.variantSets > 0) return BackdropVariants.For(spec.world).Installed(variant);
        return Resources.Load<Sprite>(BackdropCatalog.Folder(spec.world) + "sky") != null;
    }

    // Every backdrop the player may be shown: worlds 0..highest that have a
    // catalog entry, each with the variants whose art is installed.
    public static void Candidates(int highest, List<Pick> into)
    {
        into.Clear();
        highest = Mathf.Clamp(highest, 0, WorldManager.Worlds.Length - 1);
        for (int i = 0; i <= highest; i++)
        {
            string name = WorldManager.Worlds[i].displayName;
            BackdropCatalog.Spec spec = null;
            foreach (var s in BackdropCatalog.All) if (s.world == name) spec = s;
            if (spec == null) continue;
            int n = spec.variantSets > 0 ? spec.variantSets : (spec.world == "Space" ? SpaceSkySelection.VariantCount : 0);
            if (n == 0)
            {
                if (SkyInstalled(spec, 0)) into.Add(new Pick { world = name });
                continue;
            }
            for (int v = 1; v <= n; v++)
                if (SkyInstalled(spec, v)) into.Add(new Pick { world = name, variant = v });
        }
    }

    // One backdrop: a random world among the candidates', then a random
    // variant of it, never `last` while any other candidate exists.
    public static Pick Choose(List<Pick> candidates, Pick last, System.Random rng)
    {
        var worlds = new List<string>();
        foreach (var c in candidates)
            if (!c.Same(last) && !worlds.Contains(c.world)) worlds.Add(c.world);
        if (worlds.Count == 0)
        {
            // only `last` is left (or nothing at all)
            return candidates.Count > 0 ? WithTurn(candidates[0], rng) : new Pick();
        }
        string world = worlds[rng.Next(worlds.Count)];
        var variants = new List<Pick>();
        foreach (var c in candidates) if (c.world == world && !c.Same(last)) variants.Add(c);
        return WithTurn(variants[rng.Next(variants.Count)], rng);
    }

    static Pick WithTurn(Pick p, System.Random rng)
    {
        p.halfTurn = p.world == "Space" && rng.Next(2) == 1;
        return p;
    }

    public static Pick Roll(System.Random rng = null)
    {
        var list = new List<Pick>();
        Candidates(HighestWorld(), list);
        Current = Choose(list, Current, rng ?? new System.Random());
        Rolls++;
        return Current;
    }

    // A menu scene just loaded after `lastScene`: arriving from outside the
    // menus (or the first time) rolls a new backdrop, page-to-page keeps it.
    public static Pick Enter(string scene, System.Random rng = null)
    {
        bool fromMenu = lastScene != null && IsMenuScene(lastScene);
        lastScene = scene;
        if (!fromMenu || !Current.Valid) Roll(rng);
        return Current;
    }

    // A non-menu scene (a run, the tutorial) loaded.
    public static void Leave(string scene) { lastScene = scene; }

    public static void ResetForTests() { Current = new Pick(); Rolls = 0; lastScene = null; dockPick = new Pick(); }
}

public class MenuBackdrop : MonoBehaviour
{
    // How much darker than a run the menu draws (BackdropGrade lift factor).
    public const float Dim = .8f;
    // Scroll like a run's first seconds (speed 0).
    public static float Velocity { get { return WorldBackdrop.ScrollVelocity(0f); } }

    public static MenuBackdrop Instance { get; private set; }

    // The menu's own backdrop (the rolled pick). While the dock is open it stays built, hidden and still, so
    // leaving the dock is a re-show, not a reload.
    public BackdropSet Set { get; private set; }
    public MenuBackdropSelection.Pick Pick { get; private set; }
    // The Space Dock's backdrop (a Space variant) while the dock is open; null otherwise (or when the menu pick IS Space).
    public BackdropSet DockSet { get; private set; }
    public MenuBackdropSelection.Pick DockPick { get; private set; }
    public bool DockActive { get; private set; }
    // What is on screen: the dock's Space while the dock is open, else the menu's pick.
    public BackdropSet ShownSet { get { return DockActive && DockSet != null ? DockSet : Set; } }
    public MenuBackdropSelection.Pick ShownPick { get { return DockActive && DockSet != null ? DockPick : Pick; } }

    // The dock (SpaceDock) asks for / lets go of its Space backdrop. Remembered, so a dock built before the menu
    // backdrop exists (scene load order) is served when it does.
    public static bool DockWanted { get; private set; }
    public static void RequestDock(bool on)
    {
        DockWanted = on && Application.isPlaying;   // edit-mode suites build docks without a scene backdrop
        if (Instance == null) return;
        if (on) Instance.EnterDock(); else Instance.ExitDock();
    }

    // Swaps the screen to a Space backdrop (built now, from preloaded art when it is held, in the same frame as
    // the hide, so nothing flashes). Idempotent.
    public bool EnterDock()
    {
        if (DockActive) return true;
        if (Set == null) return false;   // no menu backdrop here: the scene keeps its own
        var pick = MenuBackdropSelection.DockPick(Pick);
        if (!pick.Valid) return false;
        // Even a Space menu has its own sun. Build a sun-free dock set and park the menu's set so that
        // only the selected destination is visible, and leaving the dock restores the menu exactly.
        float hw, hh;
        ViewExtents(out hw, out hh);
        var set = new BackdropSet(pick.world, transform, hw, hh,
            new BackdropSet.MenuOptions { variant = pick.variant, halfTurn = pick.halfTurn, dim = Dim, noSun = true });
        if (!set.Complete) { set.Destroy(); return false; }
        DockSet = set;
        DockPick = pick;
        Set.Root.gameObject.SetActive(false);
        DockActive = true;
        FollowCamera();
        DockSet.Tick(0f, Velocity);
        return true;
    }

    // Back to the menu's own pick (never re-rolled): the Space set goes, the hidden one is shown again.
    public void ExitDock()
    {
        if (!DockActive) return;
        DockActive = false;
        if (DockSet != null)
        {
            DockSet.Destroy();
            DockSet = null;
            if (Set != null) Set.Root.gameObject.SetActive(true);
        }
        DockPick = new MenuBackdropSelection.Pick();
        FollowCamera();
    }
    Camera cam;
    Renderer[] hidden;

    // Builds the set for `pick` (tests and previews call this; Awake does for
    // the scene). False when the art is missing: the scene keeps its own.
    public bool Build(MenuBackdropSelection.Pick pick)
    {
        Pick = pick;
        if (Set != null) { Set.Destroy(); Set = null; }
        float hw, hh;
        ViewExtents(out hw, out hh);
        var set = new BackdropSet(pick.world, transform, hw, hh,
            new BackdropSet.MenuOptions { variant = pick.variant, halfTurn = pick.halfTurn, dim = Dim });
        if (!set.Complete) { set.Destroy(); return false; }
        Set = set;
        HideSceneBackground();
        FollowCamera();
        Set.Tick(0f, Velocity);
        return true;
    }

    void Awake()
    {
        Instance = this;
        var pick = MenuBackdropSelection.Current;
        if (!pick.Valid) return;
        if (Build(pick)) { if (DockWanted) EnterDock(); SweepIfProbed(); return; }
        // incomplete art: try the other candidates once before giving up
        var list = new List<MenuBackdropSelection.Pick>();
        MenuBackdropSelection.Candidates(MenuBackdropSelection.HighestWorld(), list);
        foreach (var c in list)
            if (!c.Same(pick) && Build(c)) return;
        Debug.LogWarning("[MenuBackdrop] no complete backdrop; keeping the scene background");
    }

    // The full-heap sweep is only worth its stall when this arrival rolled a new backdrop: the roll
    // probed (Resources.Load) every candidate sky and left the unpicked ones resident. Page-to-page
    // (home -> Options -> dock and back) keeps the pick, probes nothing and the scene load has just
    // freed everything unused itself, so a sweep there was a second stall on the destination's first
    // frame.
    static int sweptRolls;
    public static int SweepsRun { get; private set; }
    // True once per roll (the caller then sweeps); tests call it to prove page-to-page does not.
    public static bool SweepDue()
    {
        if (MenuBackdropSelection.Rolls == sweptRolls) return false;
        sweptRolls = MenuBackdropSelection.Rolls;
        return true;
    }
    public static void ResetSweepForTests() { sweptRolls = 0; SweepsRun = 0; }
    static void SweepIfProbed()
    {
        if (!SweepDue() || !Application.isPlaying) return;
        SweepsRun++;
        Resources.UnloadUnusedAssets();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (DockSet != null) DockSet.Destroy();
        DockSet = null;
        if (Set != null) Set.Destroy();
        Set = null;
    }

    void Update() { Step(Time.unscaledDeltaTime); }

    public void Step(float unscaledDt)
    {
        if (Set == null) return;
        FollowCamera();
        // only what is on screen moves (the hidden menu pick is parked)
        ShownSet.Tick(Mathf.Min(unscaledDt, Time.maximumDeltaTime), Velocity);
    }

    void FollowCamera()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            Vector3 p = cam.transform.position;
            transform.position = new Vector3(p.x, p.y, 0f);
        }
        float hw, hh;
        ViewExtents(out hw, out hh);
        if (Set != null) Set.Layout(hw, hh);
        if (DockSet != null) DockSet.Layout(hw, hh);
    }

    void ViewExtents(out float halfW, out float halfH)
    {
        if (cam == null) cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            halfH = cam.orthographicSize;
            halfW = halfH * cam.aspect;
        }
        else
        {
            halfW = CameraFit.GameplayHalfWidth;
            halfH = halfW * 19.5f / 9f;
        }
    }

    // The scene's own full-screen quad (menuBackground / starsBackground)
    // would only sit under the real backdrop: switch its renderer off.
    void HideSceneBackground()
    {
        if (hidden != null) return;
        var list = new List<Renderer>();
        foreach (string n in CameraFit.BackdropNames)
        {
            var go = GameObject.Find(n);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) continue;
            r.enabled = false;
            list.Add(r);
        }
        hidden = list.ToArray();
    }
}

public static class MenuBackdropBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!MenuBackdropSelection.IsMenuScene(scene.name)) { MenuBackdropSelection.Leave(scene.name); return; }
        MenuBackdropSelection.Enter(scene.name);
        if (Object.FindFirstObjectByType<MenuBackdrop>() != null) return;
        new GameObject("~MenuBackdrop").AddComponent<MenuBackdrop>();
    }
}
