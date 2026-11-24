using UnityEngine;

// The destination behind the Space Dock: where the start world the pilot has picked in the picker at the top of the
// dock (DockStartWorld / PlayerStartWorld) takes them. Each world shows its own rotating planet (a 16-frame turn in a
// 4x4 atlas, PlanetFlipbooks); plain Space shows an orange sun with several bigger asteroids drifting round it
// (DockBody). Drawn BEHIND the translucent deck (Order), above the menu backdrop's tile layers, below every lot
// piece, dimmed so the deck lines and labels stay readable.
//
// PLACEMENT (owner: "not too close", then "another 15% to the right": 25% off centre): the sphere is whole on screen but for its right edge, its diameter
// DiameterShare of the pane's width (55-65%) and its centre OffsetShare of that width right of the middle; it may crop at the right edge only (Fit, all
// tunable; the height of the lot's space caps it so it always fits on a short screen). SpaceDock gives it the pane.
//
// WARP (a change of the picker's selection): the old destination shrinks and fades out, the star streaks
// (DockWarp, 18 frames) rush past and the new one zips in from small to its resting size, ~0.7 s in all. It is
// interruptible: a new selection mid-warp throws the unseen target away and aims at the latest, so a fast tap
// through the worlds never queues a long animation. No flash. Only the planet on show and the one warping in
// are ever loaded (PlanetFlipbooks.MaxLoaded); the sheet of the one that left is unloaded when it is gone.
//
// Nothing is allocated per frame: the bodies, the streak quad and the sprites are built once.
public class DockPlanet : MonoBehaviour
{
    public const int Order = -100;               // above the backdrop (BackdropCatalog.BaseOrder -500 up), below the lane (0)
    public const float Dim = .72f;
    public const int Worlds = 7;                 // Space, Frost, Verdant, Ember, Tide, Storm, Desert (WorldManager order)

    // ---- tunables ----
    // The disc's diameter as a share of the pane's width, and the centre's offset right of the pane's middle (the same share).
    public static float DiameterShare = .60f, OffsetShare = .25f;
    // The centre's height in the lot's space (0 bottom, 1 top) and the most of that space's height the disc may take.
    public static float CentreYShare = .5f, MaxHeightShare = .94f;
    public const float MinDiameter = .5f;

    public SpriteRenderer Renderer { get { return front != null ? front.Main : null; } }
    public string CellName { get { return front != null ? front.SpriteName : null; } }
    public float Diameter { get; private set; }
    public int World { get; private set; }               // the world selection on show / warping in (0 = Space)
    public bool IsSpace { get { return front != null && front.World == 0; } }
    public bool Warping { get { return warping; } }
    public float WarpSeconds { get { return warpT; } }
    public int OutWorld { get { return warping ? outWorld : -1; } }
    public float FrontAlpha { get { return front != null ? front.Alpha : 0f; } }
    public DockBody Front { get { return front; } }
    public DockBody Back { get { return back; } }
    public SpriteRenderer Streaks { get { return streak; } }
    public int StreakFrame { get { return streakFrame; } }
    // Warps begun (tests).
    public int WarpsStarted { get; private set; }

    DockBody front, back;
    SpriteRenderer streak;
    DockSpaceArt spaceArt;
    DockWarp warp;
    Sprite[] streakFrames;
    Texture2D streakTex;
    bool warping, frontReady;
    float warpT;
    int outWorld, streakFrame = -1;
    Vector2 centre;
    Rect view;

    // `world`: the start-world selection to show first (0 = Space: the sun).
    public static DockPlanet Create(Transform parent, int world)
    {
        var go = new GameObject("~DockPlanet");
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<DockPlanet>();
        p.Build();
        p.Show(world, false);
        return p;
    }

    // ---- placement ----

    // Where the destination sits for a pane (the lot's width) and the lot space's vertical extent: the pure rule,
    // shared with the screen-fit checks. `diameter` is the disc's diameter in world units.
    public static void Fit(Rect pane, float spaceBottom, float spaceTop, out Vector2 centre, out float diameter)
    {
        float room = Mathf.Max(MinDiameter, (spaceTop - spaceBottom) * MaxHeightShare);
        diameter = Mathf.Max(MinDiameter, Mathf.Min(pane.width * DiameterShare, room));
        // the owner wants it far right: the sphere may run off the RIGHT edge (about 5% of the width at .25 / .60) but never
        // the left; top and bottom stay whole through the height cap and the clamp below
        float cx = Mathf.Max(pane.center.x + pane.width * OffsetShare, pane.xMin + diameter * .5f);
        float cy = Mathf.Lerp(spaceBottom, spaceTop, CentreYShare);
        if (spaceTop - spaceBottom > diameter) cy = Mathf.Clamp(cy, spaceBottom + diameter * .5f, spaceTop - diameter * .5f);
        centre = new Vector2(cx, cy);
    }

    // `centre` and `diameter` in world units (the drawn disc's width).
    public void Place(Vector2 centre, float diameter)
    {
        Diameter = diameter;
        this.centre = centre;
        transform.position = new Vector3(centre.x, centre.y, 0f);
        LookAll();
        FitStreaks();
    }

    // The view the streaks must cover (world units): SpaceDock gives it with the placement.
    public void SetView(Rect worldView)
    {
        view = worldView;
        FitStreaks();
    }

    // ---- build ----

    void Build()
    {
        spaceArt = DockSpaceArt.Load();
        warp = DockWarp.Load();
        front = new DockBody(transform, "Destination", Order, spaceArt);
        back = new DockBody(transform, "DestinationOut", Order, spaceArt);
        var streakTexture = Resources.Load<Texture2D>(DockWarp.StreaksPath);
        if (streakTexture != null)
        {
            var l = FlipbookGrid.Find(streakTexture.width, streakTexture.height, DockWarp.Frames, DockWarp.Aspect);
            if (l.Valid) { streakTex = streakTexture; streakFrames = FlipbookGrid.Cut(streakTexture, l, "dock_warp"); }
        }
        var go = new GameObject("Warp");
        go.transform.SetParent(transform, false);
        streak = go.AddComponent<SpriteRenderer>();
        streak.sortingOrder = Order + 4;
        streak.enabled = false;
        World = -1;
    }

    // ---- showing ----

    public void Show(int world) { Show(world, true); }

    // Shows the destination of the start-world selection `world`. A change warps there; the first call, or
    // `animate` false, sets it at once.
    public void Show(int world, bool animate)
    {
        if (front == null) return;
        world = world >= 1 && world < Worlds ? world : 0;
        if (!animate || World < 0) { Snap(world); return; }
        if (world == World) return;
        if (!warping) BeginWarp(world); else Retarget(world);
    }

    // At once, no warp: nothing else stays loaded.
    void Snap(int world)
    {
        EndWarp(false);
        back.Clear();
        World = world;
        if (world == 0) front.ShowSpace(); else front.ShowPlanetNow(world);
        frontReady = front.Ready;
        LookAll();
    }

    void BeginWarp(int world)
    {
        outWorld = World;
        var t = front; front = back; back = t;     // what was showing goes out
        World = world;
        warping = true;
        warpT = 0f;
        WarpsStarted++;
        AimFront(world);
    }

    // A new selection mid-warp: the target nobody has seen is dropped (its sheet goes back, or its load is
    // abandoned) and the latest is aimed at. The old planet has long since shrunk away if the warp was past its
    // first half, so the streaks just carry on: the warp never grows by the number of taps.
    void Retarget(int world)
    {
        World = world;
        warpT = Mathf.Min(warpT, DockWarp.SecondsAt(DockWarp.HoldIndex));
        AimFront(world);
    }

    void AimFront(int world)
    {
        front.Clear();
        frontReady = false;
        if (world == 0) frontReady = front.ShowSpace();
        else front.WantPlanet(world);
        front.SetLook(.05f, 0f, Diameter);
    }

    void EndWarp(bool land)
    {
        if (!warping) return;
        warping = false;
        back.Clear();
        if (streak != null) streak.enabled = false;
        streakFrame = -1;
        if (land) front.SetLook(1f, 1f, Diameter);
    }

    void LookAll()
    {
        if (front == null || warping) return;
        front.SetLook(1f, 1f, Diameter);
        back.SetLook(1f, 1f, Diameter);
    }

    // ---- streaks ----

    void FitStreaks()
    {
        if (streak == null || view.width <= 0f) return;
        // the quad's centre is the vanishing point (the destination); it must reach the view's far corner
        float dx = Mathf.Max(Mathf.Abs(view.xMin - centre.x), Mathf.Abs(view.xMax - centre.x));
        float dy = Mathf.Max(Mathf.Abs(view.yMin - centre.y), Mathf.Abs(view.yMax - centre.y));
        float k = Mathf.Max(dx * 2f, dy * 2f / DockWarp.Aspect) * 1.02f;
        streak.transform.localPosition = Vector3.zero;
        streak.transform.localScale = new Vector3(k, k, 1f);
    }

    void SetStreak(float index)
    {
        if (streak == null || streakFrames == null) return;
        int f = Mathf.Clamp(Mathf.RoundToInt(index), 0, streakFrames.Length - 1);
        if (f != streakFrame) { streak.sprite = streakFrames[f]; streakFrame = f; }
        streak.enabled = true;
    }

    // ---- stepping ----

    // One step, dt in unscaled seconds (tests drive it directly).
    public void Tick(float dt)
    {
        if (front == null) return;
        PlanetFlipbooks.Pump();
        if (warping) StepWarp(dt);
        front.Tick(dt);
        if (warping) back.Tick(dt);
    }

    void StepWarp(float dt)
    {
        bool mayRequest = PlanetFlipbooks.LoadedCount < PlanetFlipbooks.MaxLoaded;
        if (!frontReady) frontReady = front.Pump(mayRequest);
        warpT += dt;
        float index = DockWarp.IndexAt(warpT);
        if (!frontReady && index > DockWarp.HoldIndex)
        {
            // the new planet is still loading: the streaks hold where the old one has gone
            index = DockWarp.HoldIndex;
            warpT = DockWarp.SecondsAt(index);
        }
        if (index >= DockWarp.Frames - 1 - .001f && frontReady) { EndWarp(true); return; }
        back.SetLook(Mathf.Max(.0001f, warp.OutScale(index)), warp.OutAlpha(index), Diameter);
        front.SetLook(Mathf.Max(.0001f, warp.InScale(index)), frontReady ? warp.InAlpha(index) : 0f, Diameter);
        if (back.World >= 0 && warp.OutAlpha(index) <= .001f) back.Clear();
        SetStreak(index);
    }

    void Update()
    {
        Tick(Time.unscaledDeltaTime);   // a run may have left timeScale at 0
    }

    void OnDestroy() { Dispose(); }

    // Gives back every sheet and sprite (idempotent). OnDestroy calls it; edit-mode tests, where Unity does not send
    // OnDestroy to a plain MonoBehaviour, call it before DestroyImmediate.
    public void Dispose()
    {
        if (front != null) front.Clear();
        if (back != null) back.Clear();
        if (spaceArt != null) spaceArt.Destroy();
        if (streakFrames != null) foreach (var s in streakFrames) if (s != null) BossUtil.Kill(s);
        if (streakTex != null) Resources.UnloadAsset(streakTex);
        streakFrames = null; streakTex = null;
        spaceArt = null; front = back = null;
    }
}
