using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The blast + drifting smoke when a hit costs a heart but not the run
// (HeartLossFx / HeartLossSmoke):
//   - a non-fatal hit pools a blast flipbook and a puff cloud; the fatal hit
//     starts neither (DeathCrash owns it)
//   - the smoke is semi-transparent, behind the hull, thins to nothing and
//     lingers longer than the blast
//   - it rides the scrolling world past the ship (does not stay glued to it)
//     and is gone by its life's end or off the bottom
//   - frozen at timeScale 0 / dt 0, slowed with slow motion
//   - the pool is fixed (repeat hits never grow it) and per-hit emit and the
//     per-frame tick allocate nothing
public static class HeartLossFxTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HLF] PASS  " : "[HLF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Check("the smoke art loads (DamageFx smoke row)", ShipDamageFx.Frame(ShipDamageFx.RowSmoke, 0) != null &&
              ShipDamageFx.Frame(ShipDamageFx.RowSmoke, 3) != null);
        Check("the alpha curve is semi-transparent, starts and ends clear",
              HeartLossSmoke.AlphaAt(0f) == 0f && HeartLossSmoke.AlphaAt(1f) == 0f &&
              HeartLossSmoke.AlphaAt(.3f) > 0f && HeartLossSmoke.AlphaAt(.3f) <= HeartLossSmoke.PeakAlpha &&
              HeartLossSmoke.PeakAlpha <= .6f);
        Check("smoke outlives the blast",
              HeartLossSmoke.LifeMin > TargetExplosion.SecondsFor(TargetExplosion.Size.Small) * HeartLossFx.BlastHoldScale);
        Check("the pool holds several hits' puffs", HeartLossSmoke.MaxPuffs >= HeartLossSmoke.PuffsPerHit * 3);

        NonFatalHitMakesSmoke();
        FatalHitDoesNot();
        DriftsWithWorld();
        PausedAndSlowMo();
        PoolBounded();

        CleanUp();
        Debug.Log("[HLF] failures: " + fails);
        return fails;
    }

    static void CleanUp()
    {
        HeartLossSmoke.Clear();
        foreach (var name in new[] { "~HeartLossSmoke", "~WeaponFx" })
            for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name)) Object.DestroyImmediate(go);
        // the fatal-hit check starts a real DeathCrash; end it so the world moves again
        foreach (var crash in Object.FindObjectsByType<DeathCrash>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(crash.gameObject);
        Time.timeScale = 1f;
        moveBackGround.speed = 0f;
        collisionDetection.lifeCounter = 0;
        buttonClicks.playerDied = false;
        PlayerInvuln.Reset();
    }

    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        public Action<Collider2D> trigger;

        public Rig(int id, int maxLife)
        {
            ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
            ship.GetComponent<SpriteRenderer>().sprite = shopingShips.SpriteFor(id, 0);
            ship.GetComponent<SpriteRenderer>().sortingOrder = 5;
            ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            ship.AddComponent<BoxCollider2D>().isTrigger = true;
            cd = ship.AddComponent<collisionDetection>();
            cd.explosionAnimation = new GameObject("~TestExplosion");
            cd.boostSound = ship.AddComponent<AudioSource>();
            cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
            cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
            cd.boost = new GameObject("~boost");
            cd.boost.SetActive(false);
            trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), cd,
                typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
            collisionDetection.MAXLIFE = maxLife;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }

        public void Hit(string name)
        {
            var go = new GameObject(name, typeof(CircleCollider2D));
            go.tag = "Astr";
            go.transform.position = ship.transform.position + Vector3.right * .3f;
            trigger(go.GetComponent<Collider2D>());
            if (go != null) Object.DestroyImmediate(go);
        }

        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) Object.DestroyImmediate(go);
            if (ship != null) Object.DestroyImmediate(ship);
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }
    }

    static void NonFatalHitMakesSmoke()
    {
        CleanUp();
        var r = new Rig(3, 3);
        int before = WeaponFx.ActiveOf(FlipbookFx.Mode.Explosion);
        r.Hit("rock");
        Check("a non-fatal hit costs a heart", collisionDetection.lifeCounter == 1);
        Check("and starts the smoke cloud (" + HeartLossSmoke.ActiveCount + " puffs)",
              HeartLossSmoke.ActiveCount == HeartLossSmoke.PuffsPerHit);
        int bodies = 0;
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (fx.Active && fx.CurrentMode == FlipbookFx.Mode.Explosion) bodies++;
        Check("and a pooled blast flipbook on the hull (" + bodies + " bodies, ActiveOf " + WeaponFx.ActiveOf(FlipbookFx.Mode.Explosion) + ", before " + before + ")", bodies >= 1);
        var s = HeartLossSmoke.Instance;
        bool behind = true, soft = true, near = true;
        for (int i = 0; i < HeartLossSmoke.PoolSize; i++)
        {
            if (!s.PuffAlive(i)) continue;
            behind &= s.PuffRenderer(i).sortingOrder < r.ship.GetComponent<SpriteRenderer>().sortingOrder;
            soft &= s.PuffRenderer(i).color.a <= HeartLossSmoke.PeakAlpha + 1e-4f;
            near &= Vector2.Distance(s.PuffPosition(i), r.ship.transform.position) < .5f;
        }
        Check("puffs start on the ship, behind its hull, semi-transparent", behind && soft && near);
        r.Dispose();
    }

    static void FatalHitDoesNot()
    {
        CleanUp();
        var r = new Rig(3, 1);
        bool threw = false;
        try { r.Hit("rock"); } catch (Exception) { threw = true; }   // the death crash needs a real scene
        Check("the fatal hit makes no heart-loss smoke (the death crash owns it)", HeartLossSmoke.ActiveCount == 0);
        if (threw) Debug.Log("[HLF] (death crash threw in the bare test scene, as expected)");
        r.Dispose();
        CleanUp();
    }

    static void Setup()
    {
        CleanUp();
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .1f;
        Debug.Log("[HLF] world: scrolling=" + TargetExplosion.WorldScrolling + " crashAnimating=" + DeathCrash.Animating +
                  " pressed=" + TouchInput.IsPressed + " died=" + buttonClicks.playerDied + " ts=" + Time.timeScale);
    }

    static void DriftsWithWorld()
    {
        Setup();
        HeartLossSmoke.Emit(Vector3.zero, 0, 5);
        var s = HeartLossSmoke.Instance;
        float y0 = 0f; int n = 0;
        for (int i = 0; i < HeartLossSmoke.PoolSize; i++) if (s.PuffAlive(i)) { y0 += s.PuffPosition(i).y; n++; }
        y0 /= Mathf.Max(1, n);
        for (int k = 0; k < 30; k++) s.Tick(Dt);
        float y1 = 0f;
        for (int i = 0; i < HeartLossSmoke.PoolSize; i++) if (s.PuffAlive(i)) y1 += s.PuffPosition(i).y;
        y1 /= Mathf.Max(1, HeartLossSmoke.ActiveCount);
        Check("the smoke rides the scrolling world down past the ship (" + y0.ToString("F2") + " -> " + y1.ToString("F2") + ")",
              y1 < y0 - .5f * moveBackGround.speed * 30f * 30f * Dt);

        for (int k = 0; k < 150; k++) s.Tick(Dt);   // 2.5 s total: past LifeMax
        Check("and is gone after its life", HeartLossSmoke.ActiveCount == 0);

        // a stopped world (boosting nothing, speed 0) still thins out and ends
        moveBackGround.speed = 0f;
        HeartLossSmoke.Emit(Vector3.zero, 0, 5);
        for (int k = 0; k < 150; k++) s.Tick(Dt);
        Check("with the world still, it drifts a little and still fades out", HeartLossSmoke.ActiveCount == 0);
        CleanUp();
    }

    static void PausedAndSlowMo()
    {
        Setup();
        HeartLossSmoke.Emit(Vector3.zero, 0, 5);
        var s = HeartLossSmoke.Instance;
        s.Tick(Dt);
        var p0 = new Vector3[HeartLossSmoke.PoolSize];
        for (int i = 0; i < p0.Length; i++) p0[i] = s.PuffPosition(i);
        int active = HeartLossSmoke.ActiveCount;
        for (int k = 0; k < 120; k++) s.Tick(0f);
        bool still = HeartLossSmoke.ActiveCount == active;
        for (int i = 0; i < p0.Length; i++) still &= p0[i] == s.PuffPosition(i);
        Check("paused (dt 0): no puff moves, ages or fades", still && active > 0);

        Time.timeScale = 0f;
        Check("at timeScale 0 the gameplay clock the smoke runs on is 0", TargetExplosion.Delta() == 0f);
        Time.timeScale = 1f;

        // slow-mo: the same game time takes proportionally more frames
        float full = TimeToClear(1f), slow = TimeToClear(.25f);
        Check("slow motion slows it (" + full.ToString("F2") + " s vs " + slow.ToString("F2") + " s of real time)",
              slow > full * 3f);
        CleanUp();
    }

    // Real seconds until the cloud ends, fed scaled steps like Time.deltaTime does.
    static float TimeToClear(float scale)
    {
        Setup();
        HeartLossSmoke.Emit(Vector3.zero, 0, 5);
        float real = 0f;
        for (int k = 0; k < 4000 && HeartLossSmoke.ActiveCount > 0; k++)
        {
            HeartLossSmoke.Instance.Tick(Dt * scale);
            real += Dt;
        }
        return real;
    }

    static void PoolBounded()
    {
        Setup();
        for (int k = 0; k < 10; k++) HeartLossSmoke.Emit(Vector3.zero, 0, 5);
        Check("repeat hits never grow the pool (" + HeartLossSmoke.PoolSize + ")",
              HeartLossSmoke.PoolSize == HeartLossSmoke.MaxPuffs && HeartLossSmoke.ActiveCount <= HeartLossSmoke.MaxPuffs);
        var s = HeartLossSmoke.Instance;
        Check("a full pool still gives the latest hit all its puffs (" + HeartLossSmoke.ActiveCount + ")",
              HeartLossSmoke.ActiveCount == HeartLossSmoke.MaxPuffs);
        HeartLossSmoke.Clear();
        for (int k = 0; k < 4; k++) HeartLossSmoke.Emit(Vector3.zero, 0, 5);   // saturate (28 > 24)
        int fresh = 0;
        for (int i = 0; i < HeartLossSmoke.PoolSize; i++) if (s.PuffAlive(i) && s.PuffRenderer(i).enabled) fresh++;
        Check("saturated: every slot live, none collapsed", fresh == HeartLossSmoke.MaxPuffs);
        s.Tick(Dt);
        int children = s.transform.childCount;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 6; k++) HeartLossSmoke.Emit(Vector3.zero, 0, 5);
        for (int k = 0; k < 200; k++) s.Tick(Dt);
        long grown = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("emit and tick create no objects (" + grown + " bytes)", s.transform.childCount == children && grown < 4096);
        CleanUp();
    }
}
