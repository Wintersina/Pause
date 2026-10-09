using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Post-hit invulnerability (PlayerInvuln): losing a heart leaves the ship
// untouchable for PostHitInvulnSeconds, blinking back into existence.
//
//   - a non-fatal hit starts a 2 s window; a second hit inside it costs
//     nothing and kills nothing (pass-through); after it, hits count again
//   - the hull blinks (fades in, strobe slows, last half-beat ghost) and
//     ends solid; lifeControler draws the hull at PlayerInvuln.HullAlpha
//   - the clock is world time: a frozen frame (dt 0) never drains it
//   - stacks with the blue-atom shield: safe while either runs
//   - the fatal hit is unchanged (no window, death sequence)
//   - every ship; no allocations on the per-frame path
public static class PostHitInvulnTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PHI] PASS  " : "[PHI] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const float D = PlayerInvuln.PostHitInvulnSeconds;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Check("the window is one named constant of 2 s", Mathf.Approximately(D, 2f));
        HitStartsWindow();
        WindowEnds();
        BlinkShape();
        HullDrawsTheBlink();
        WorldClock();
        StacksWithShield();
        FatalHitUnchanged();
        AllShips();
        NoAllocations();
        ResetClears();

        Debug.Log("[PHI] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------

    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        public Action<Collider2D> trigger;
        public Action tick;

        public Rig(int id, int maxLife)
        {
            ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
            var sprite = shopingShips.SpriteFor(id, 0);
            ship.GetComponent<SpriteRenderer>().sprite = sprite;
            if (sprite != null)
            {
                float s = shopingShips.NormalizedHullScale(sprite);
                ship.transform.localScale = new Vector3(s, s, 1f);
            }
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
            tick = (Action)Delegate.CreateDelegate(typeof(Action), cd,
                typeof(collisionDetection).GetMethod("turnTextsOff", Inst));
            collisionDetection.MAXLIFE = maxLife;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }

        public ShipShield Shield { get { return ship != null ? ship.GetComponent<ShipShield>() : null; } }

        public void Hit(string name, string tag = "Astr")
        {
            var go = new GameObject(name, typeof(CircleCollider2D));
            go.tag = tag;
            go.transform.position = ship.transform.position + Vector3.right * .3f;
            trigger(go.GetComponent<Collider2D>());
            if (go != null) Object.DestroyImmediate(go);
        }

        public void BlueAtom()
        {
            var atom = new GameObject("atom3a(Clone)", typeof(CircleCollider2D));
            atom.tag = "pickUp";
            trigger(atom.GetComponent<Collider2D>());
            if (atom != null) Object.DestroyImmediate(atom);
        }

        public void Dispose()
        {
            foreach (var go in new[] { cd != null ? cd.explosionAnimation : null, cd != null ? cd.boost : null,
                                       cd != null ? cd.boostText.gameObject : null, cd != null ? cd.hypeText.gameObject : null })
                if (go != null) Object.DestroyImmediate(go);
            if (ship != null) Object.DestroyImmediate(ship);
            foreach (var name in new[] { "~fx", "~TestExplosion(Clone)", "~PickupBurst" })
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                    Object.DestroyImmediate(go);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }
    }

    // ------------------------------------------------------------------

    static void HitStartsWindow()
    {
        var r = new Rig(3, 3);
        Check("a fresh ship has no post-hit window", !PlayerInvuln.Active && PlayerInvuln.HullAlpha == 1f);
        r.Hit("rock");
        Check("a non-fatal hit costs one heart and starts the 2 s window (" + PlayerInvuln.Remaining + ")",
              collisionDetection.lifeCounter == 1 && PlayerInvuln.Active &&
              Mathf.Approximately(PlayerInvuln.Remaining, D));
        Check("the post-hit window is not a shield or Cloak (no ram kills)",
              !collisionDetection.Invulnerable && !collisionDetection.atomCheck && !collisionDetection.Cloaked);

        r.Hit("rock2");
        r.Hit("alien1(Clone)", "Enimey");
        r.Hit("BossShot", "Enimey");
        r.Hit("BossBeam", "Enimey");
        // A mine's unshielded path would instantiate the (null) red blast and
        // throw; passing through never reaches it.
        bool threw = false;
        try { r.Hit("mine(Clone)", "Enimey"); } catch (Exception) { threw = true; }
        Check("inside the window rocks, enemies, boss shots/beams and mines cost nothing",
              collisionDetection.lifeCounter == 1 && !threw && !buttonClicks.playerDied);
        Check("and pass through harmlessly: nothing absorbed or rammed (no shield hit drawn)",
              r.Shield == null || !r.Shield.FlashShowing);
        Check("those pass-throughs never restart the window",
              Mathf.Approximately(PlayerInvuln.Remaining, D));

        // Pickups still collect.
        int heals = collisionDetection.healAtomPickups;
        var heal = new GameObject(HealAtom.ObjectName + "(Clone)", typeof(CircleCollider2D));
        heal.tag = "pickUp";
        r.trigger(heal.GetComponent<Collider2D>());
        Object.DestroyImmediate(heal);
        Check("pickups are still collected inside the window",
              collisionDetection.healAtomPickups == heals + 1 && collisionDetection.lifeCounter == 0);
        r.Dispose();
    }

    static void WindowEnds()
    {
        var r = new Rig(3, 4);
        r.Hit("rock");
        PlayerInvuln.Tick(D - .02f);
        Check("still invulnerable just before 2 s", PlayerInvuln.Active);
        r.Hit("rock2");
        Check("a second hit at 1.98 s costs nothing", collisionDetection.lifeCounter == 1);
        PlayerInvuln.Tick(.05f);
        Check("the window ends at 2 s (clamped at 0)", !PlayerInvuln.Active && PlayerInvuln.Remaining == 0f);
        Check("and the hull is solid again", PlayerInvuln.HullAlpha == 1f);
        r.Hit("rock3");
        Check("after 2 s a hit costs a heart again, and opens a new window",
              collisionDetection.lifeCounter == 2 && Mathf.Approximately(PlayerInvuln.Remaining, D));

        // The running-world frame tick drains it.
        float before = PlayerInvuln.Remaining;
        r.tick();
        Check("the per-frame tick runs the clock (never up, never negative)",
              PlayerInvuln.Remaining <= before && PlayerInvuln.Remaining >= 0f);

        PlayerInvuln.Tick(1f);
        PlayerInvuln.BeginPostHit();
        Check("a new window refreshes to the full 2 s", Mathf.Approximately(PlayerInvuln.Remaining, D));
        r.Dispose();
    }

    static void BlinkShape()
    {
        Check("it blinks back into existence: starts invisible", PlayerInvuln.Alpha(0f) < .05f);

        // Fade-in: the lit frames of the rematerialise flicker get brighter.
        float early = 0f, late = 0f;
        for (float t = 0f; t < PlayerInvuln.RematerialiseSeconds * .5f; t += .002f) early = Mathf.Max(early, PlayerInvuln.Alpha(t));
        for (float t = PlayerInvuln.RematerialiseSeconds * .5f; t < PlayerInvuln.RematerialiseSeconds; t += .002f) late = Mathf.Max(late, PlayerInvuln.Alpha(t));
        Check("the rematerialise flicker fades in (" + early.ToString("F2") + " -> " + late.ToString("F2") + ")", late > early);

        // Strobe: visible on and off states; fewer toggles in the second half.
        int firstHalf = 0, secondHalf = 0, ghosts = 0, solids = 0;
        bool prev = PlayerInvuln.Alpha(PlayerInvuln.RematerialiseSeconds) > .5f;
        for (float t = PlayerInvuln.RematerialiseSeconds; t < D; t += .001f)
        {
            float a = PlayerInvuln.Alpha(t);
            bool on = a > .5f;
            if (on) solids++; else ghosts++;
            if (on != prev) { if (t < D * .5f + PlayerInvuln.RematerialiseSeconds * .5f) firstHalf++; else secondHalf++; }
            prev = on;
        }
        Check("the strobe shows both ghost and solid frames (" + ghosts + "/" + solids + ")", ghosts > 100 && solids > 100);
        Check("the ghost frames stay faintly visible, never fully gone", PlayerInvuln.GhostAlpha > 0f && PlayerInvuln.GhostAlpha < .5f);
        Check("the blink slows toward the end (" + firstHalf + " toggles, then " + secondHalf + ")",
              secondHalf < firstHalf && PlayerInvuln.BlinkHz(D * .9f) < PlayerInvuln.BlinkHz(.3f));
        Check("the last half-beat is a ghost, so the end reads as a solid flash",
              PlayerInvuln.Alpha(D - .02f) < .5f && PlayerInvuln.Alpha(D) == 1f && PlayerInvuln.Alpha(D + 1f) == 1f);
    }

    static void HullDrawsTheBlink()
    {
        var go = new GameObject("ship3(Clone)", typeof(SpriteRenderer));
        var lc = go.AddComponent<lifeControler>();
        var sr = go.GetComponent<SpriteRenderer>();
        var sprite = shopingShips.SpriteFor(3, 0);
        typeof(lifeControler).GetField("spriteControl", Inst).SetValue(lc, sr);
        lc.img = new[] { sprite, sprite, sprite };
        typeof(lifeControler).GetField("isLiveGameplay", Inst).SetValue(lc, true);
        var apply = typeof(lifeControler).GetMethod("applyDamageSprite", Inst);

        collisionDetection.lifeCounter = 1;
        PlayerInvuln.Reset();
        PlayerInvuln.BeginPostHit();
        PlayerInvuln.Tick(D - .02f);   // a ghost half-beat
        apply.Invoke(lc, null);
        Check("the live hull is drawn at the blink alpha (" + sr.color.a.ToString("F2") + ")",
              Mathf.Approximately(sr.color.a, PlayerInvuln.HullAlpha) && sr.color.a < .5f &&
              sr.color.r == 1f && sr.color.g == 1f && sr.color.b == 1f);
        PlayerInvuln.Tick(.1f);
        apply.Invoke(lc, null);
        Check("and solid once the window ends", sr.color == Color.white);

        // A dock ship never blinks.
        PlayerInvuln.BeginPostHit();
        PlayerInvuln.Tick(D - .02f);
        typeof(lifeControler).GetField("isLiveGameplay", Inst).SetValue(lc, false);
        apply.Invoke(lc, null);
        Check("a dock (non-live) ship never blinks", sr.color == Color.white);
        PlayerInvuln.Reset();
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    static void WorldClock()
    {
        PlayerInvuln.Reset();
        PlayerInvuln.BeginPostHit();
        for (int i = 0; i < 100; i++) PlayerInvuln.Tick(0f);   // frozen frames: timeScale 0 -> dt 0
        Check("paused (frozen, dt 0) frames never drain the window", Mathf.Approximately(PlayerInvuln.Remaining, D));
        PlayerInvuln.Tick(-1f);
        Check("a negative dt never adds time", Mathf.Approximately(PlayerInvuln.Remaining, D));
        // Resume slow-mo: half-speed world time counts half.
        for (int i = 0; i < 10; i++) PlayerInvuln.Tick(.1f * .5f);
        Check("slow-mo drains it in scaled time (" + PlayerInvuln.Remaining.ToString("F2") + ")",
              Mathf.Abs(PlayerInvuln.Remaining - (D - .5f)) < 1e-4f);

        // It ticks beside Cloak in turnTextsOff (running-world frames only),
        // with the scaled Time.deltaTime -- never unscaled time.
        string src = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        int tick = src.IndexOf("PlayerInvuln.Tick(Time.deltaTime)", StringComparison.Ordinal);
        int body = src.IndexOf("void turnTextsOff()", StringComparison.Ordinal);
        Check("collisionDetection ticks it with scaled time in turnTextsOff (frozen while paused, like Cloak)",
              tick > body && body > 0 && !src.Contains("PlayerInvuln.Tick(Time.unscaledDeltaTime)"));
        PlayerInvuln.Reset();
    }

    static void StacksWithShield()
    {
        // Shield picked up during the window: the shield takes the hits.
        var r = new Rig(3, 4);
        r.Hit("rock");
        r.BlueAtom();
        Check("a blue atom inside the window raises its 5.8 s shield and leaves the window alone",
              collisionDetection.atomCheck && Mathf.Approximately(collisionDetection.invTimer, 5.8f) &&
              Mathf.Approximately(PlayerInvuln.Remaining, D));
        Check("under the shield the hull stops blinking (the bubble shows protection)", PlayerInvuln.HullAlpha == 1f);
        r.Hit("rock2");
        Check("both up: the shield absorbs (and still destroys) -- no heart",
              collisionDetection.lifeCounter == 1 && r.Shield != null && r.Shield.FlashShowing);
        PlayerInvuln.Tick(D + .1f);
        Check("the window ending under a shield leaves the shield up",
              !PlayerInvuln.Active && collisionDetection.atomCheck && collisionDetection.Invulnerable);
        r.Hit("rock3");
        Check("shield alone still absorbs", collisionDetection.lifeCounter == 1);
        collisionDetection.invTimer = 0f;
        r.tick();
        r.Hit("rock4");
        Check("then with both over hits count again", collisionDetection.lifeCounter == 2);
        r.Dispose();

        // The shield ending first: the window carries on (longer of the two).
        r = new Rig(3, 4);
        r.Hit("rock");
        r.BlueAtom();
        collisionDetection.invTimer = 0f;
        r.tick();
        Check("the shield running out mid-window drops the shield but not the window",
              !collisionDetection.atomCheck && PlayerInvuln.Active);
        r.Hit("rock2");
        Check("the window alone still protects", collisionDetection.lifeCounter == 1);
        r.Dispose();
    }

    static void FatalHitUnchanged()
    {
        var r = new Rig(3, 2);
        r.Hit("rock");
        Check("2 hearts: first hit leaves the last heart and a window", collisionDetection.lifeCounter == 1 && PlayerInvuln.Active);
        PlayerInvuln.Tick(D + .1f);
        r.Hit("rock2");
        Check("the fatal hit still kills (death sequence) and starts no window",
              collisionDetection.lifeCounter == 2 && buttonClicks.playerDied && !PlayerInvuln.Active);
        r.Dispose();
    }

    static void AllShips()
    {
        int ok = 0, n = 0;
        foreach (int id in ShipId.All)
        {
            n++;
            // (the window needs hearts to spare: a ship's real 1 or 2 hearts are covered
            // by the FatalHitUnchanged / one-heart cases, so every ship is tried with 3)
            int max = Mathf.Max(3, ShipLives.Max(id));
            var r = new Rig(id, max);
            r.Hit("rock");
            bool started = collisionDetection.lifeCounter == 1 && PlayerInvuln.Active;
            r.Hit("alien1(Clone)", "Enimey");
            bool ignored = collisionDetection.lifeCounter == 1;
            PlayerInvuln.Tick(D + .01f);
            r.Hit("rock2");
            bool counts = collisionDetection.lifeCounter == 2 && (max > 2 ? PlayerInvuln.Active && !buttonClicks.playerDied
                                                                         : buttonClicks.playerDied && !PlayerInvuln.Active);
            if (started && ignored && counts) ok++;
            else Debug.Log("[PHI]   ship " + id + " (" + max + " hearts): started " + started + " ignored " + ignored + " counts " + counts);
            r.Dispose();
        }
        Check("every ship (" + ok + "/" + n + ") gets the window, ignores a hit in it, and counts after", ok == n && n == 15);
    }

    static void NoAllocations()
    {
        PlayerInvuln.Reset();
        float sink = 0f;
        for (int i = 0; i < 10; i++) { PlayerInvuln.BeginPostHit(); PlayerInvuln.Tick(.01f); sink += PlayerInvuln.HullAlpha; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++)
        {
            if (i % 250 == 0) PlayerInvuln.BeginPostHit();
            PlayerInvuln.Tick(1f / 60f);
            sink += PlayerInvuln.HullAlpha + PlayerInvuln.Alpha(i * .0007f) + (PlayerInvuln.Active ? 1f : 0f);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("the per-frame clock and blink allocate nothing (" + bytes + " bytes)", bytes == 0 && sink > 0f);
        PlayerInvuln.Reset();
    }

    static void ResetClears()
    {
        PlayerInvuln.BeginPostHit();
        GameStateReset.Clear();
        Check("GameStateReset.Clear() ends any post-hit window", !PlayerInvuln.Active);
    }
}
