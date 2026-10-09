using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The menus' background: the REAL in-game backdrop (BackdropCatalog tile
// layers, scrolled at the rate a run starts with), not a menu painting.
//
//   WHICH   Arriving at a menu from outside the menus (app start, a run, the
//           tutorial) rolls one backdrop: a random world among those the
//           player has reached (Space always; Frost / Verdant / Ember once
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

    public static void ResetForTests() { Current = new Pick(); Rolls = 0; lastScene = null; }
}

public class MenuBackdrop : MonoBehaviour
{
    // How much darker than a run the menu draws (BackdropGrade lift factor).
    public const float Dim = .8f;
    // Scroll like a run's first seconds (speed 0).
    public static float Velocity { get { return WorldBackdrop.ScrollVelocity(0f); } }

    public static MenuBackdrop Instance { get; private set; }

    public BackdropSet Set { get; private set; }
    public MenuBackdropSelection.Pick Pick { get; private set; }
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
        if (Build(pick)) { if (Application.isPlaying) Resources.UnloadUnusedAssets(); return; }
        // incomplete art: try the other candidates once before giving up
        var list = new List<MenuBackdropSelection.Pick>();
        MenuBackdropSelection.Candidates(MenuBackdropSelection.HighestWorld(), list);
        foreach (var c in list)
            if (!c.Same(pick) && Build(c)) return;
        Debug.LogWarning("[MenuBackdrop] no complete backdrop; keeping the scene background");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Set != null) Set.Destroy();
        Set = null;
    }

    void Update() { Step(Time.unscaledDeltaTime); }

    public void Step(float unscaledDt)
    {
        if (Set == null) return;
        FollowCamera();
        Set.Tick(Mathf.Min(unscaledDt, Time.maximumDeltaTime), Velocity);
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
