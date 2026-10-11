using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Three tutorial fixes:
//
//   DUST    "here comes star dust" rushes a short, easy stream right ahead of
//           the ship (on it within ~1 s), every dust piece wears an arrow
//           (gone with the piece), the step advances on a catch, and a missed
//           stream is rushed again. Driven with a fake clock.
//   HEARTS  the tutorial ship flies with extra orbiting hearts; the alien's
//           crash costs one and never kills, however many times; hearts are
//           topped back up before the next beat; a normal run afterwards
//           starts with its usual hearts.
//   ATOMS   each atom type is introduced exactly once, in the documented order, by one
//           short sentence, with no random atoms; each one can be collected, does its real
//           effect and advances its step
//   TIMING  a competent player finishes in 30-45 s, the all-timeouts worst case in <75 s
//   PANEL   the Tutorial Complete card: title + LIFT OFF + HOME, real taps on both
//   LIFTOFF the portal departure plays once, then the first level loads once
//   FINGER  one finger-to-ship offset for every touch controller
//   SIZE    the tutorial's ship is drawn as big on screen as gameS1's, on
//           every device of the screen-fit matrix (within 2%).
public static class TutorialPolishTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TP] PASS  " : "[TP] FAIL  ") + what);
        if (!ok) fails++;
    }

    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            ScriptTable();
            Timeline();
            AtomOrder();
            AtomEffects();
            CompletePanel();
            LiftOff();
            FingerOffset();
            DustRush();
            Hearts();
            NormalRunHearts();
            ShipSize();
        }
        finally
        {
            ScreenInfo.ClearOverride();
            Time.timeScale = 1f;
        }
        Debug.Log("[TP] failures: " + fails);
        return fails;
    }

    static int StepIndex(string id) { return System.Array.FindIndex(TutorialScript.Steps, s => s.id == id); }

    static void ScriptTable()
    {
        int hearts = StepIndex("hearts"), enemies = StepIndex("enemies"), dust = StepIndex("dust");
        Check("the hearts line comes right before the alien", hearts >= 0 && hearts + 1 == enemies);
        Check("the dust step rushes the dust (SpawnStars cue)", dust >= 0 && TutorialScript.Steps[dust].cue == TutorialCue.SpawnStars);
        if (hearts >= 0)
        {
            string line = TutorialScript.Steps[hearts].line;
            Check("the hearts line says the hearts protect (\"" + line + "\")", line.Contains("hearts") && line.Length <= TutorialScript.MaxLineLength);
        }
    }

    // A tutorial scene with the director started and a ship in its lane.
    static Hints OpenTutorial(out GameObject ship)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        score.pauseCounter = 50;
        score.paysRealDust = false;
        var hints = Object.FindFirstObjectByType<Hints>();
        hints.SendMessage("Start");
        ship = GameObject.Find("ship1");
        var spawner = Object.FindFirstObjectByType<spawnGoodStuffTut>();
        spawner.SendMessage("Start");
        return hints;
    }

    static void Begin(Hints h, int index)
    {
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(h, new object[] { index });
    }

    static void Sample(Hints h, float dt)
    {
        typeof(Hints).GetMethod("Sample", Inst).Invoke(h, new object[] { dt });
    }

    static TutorialGuides GuidesOf(Hints h)
    {
        return (TutorialGuides)typeof(Hints).GetField("guides", Inst).GetValue(h);
    }

    static void Cleanup()
    {
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        TutorialEnemy.Clear();
        foreach (var s in Object.FindObjectsByType<RobotSpeaker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(s.gameObject);
    }

    // ---- DUST ----------------------------------------------------------------

    static void DustRush()
    {
        GameObject ship;
        var hints = OpenTutorial(out ship);
        Check("tutorialS5 has the director and the ship", hints != null && ship != null);
        if (hints == null || ship == null) return;
        var dev = FitDevice.All[0];
        foreach (var d in FitDevice.All) if (d.id == "and-1080x2340-notch") dev = d;

        using (var rig = new ScreenFitRig(dev, CameraFit.GameplayHalfWidth))
        {
            // the ship in its low lane, off-centre
            ship.transform.position = new Vector3(1.1f, -2.6f, 0f);
            Begin(hints, StepIndex("dust"));
            rig.Sync();

            var stars = spawnGoodStuffTut.LiveStars;
            int rushed = 0;
            float latest = 0f;
            bool allAhead = true, allLane = true, allDrift = true;
            foreach (var t in stars)
            {
                var drift = t.GetComponent<TutorialStarDrift>();
                if (drift == null) { allDrift = false; continue; }
                rushed++;
                float dy = t.position.y - ship.transform.position.y;
                if (dy <= 0f) allAhead = false;
                latest = Mathf.Max(latest, dy / TutorialStarDrift.Speed);
                if (Mathf.Abs(t.position.x) > RailInset.PickupLaneHalf + .001f || Mathf.Abs(t.position.x - ship.transform.position.x) > .6f) allLane = false;
                if (!(PrefabName.Is(t.gameObject, "smStar1") || PrefabName.Is(t.gameObject, "LargeStar1"))) allDrift = false;
            }
            Debug.Log("[TP] rushed " + rushed + " pieces, the last arrives in " + latest.ToString("0.00") + " s");
            Check("a handful of dust pieces (" + rushed + ") rush in", rushed >= 4 && rushed <= 8);
            Check("all above the ship, in its lane", allAhead && allLane);
            Check("each is a real star prefab with the tutorial drift", allDrift);
            Check("the last piece is on the ship within 1.2 s (" + latest.ToString("0.00") + ")", latest > 0f && latest <= 1.2f);

            // fake clock: fall at 60 fps with the finger down; the first pieces reach the ship
            var first = new List<Transform>();
            foreach (var t in stars) if (t != null) first.Add(t);
            float time = 0f, reached = -1f;
            while (time < 1.5f)
            {
                foreach (var t in first)
                    if (t != null) t.GetComponent<TutorialStarDrift>().Step(1f / 60f, true);
                time += 1f / 60f;
                if (reached < 0f)
                {
                    bool all = true;
                    foreach (var t in first) if (t != null && t.position.y > ship.transform.position.y) all = false;
                    if (all) reached = time;
                }
            }
            Check("every piece has passed the ship's height after " + reached.ToString("0.00") + " s fake time", reached > 0f && reached <= 1.25f);
            Cleanup0();

            // frozen world: nothing falls
            Begin(hints, StepIndex("dust"));
            Transform one = null;
            foreach (var t in spawnGoodStuffTut.LiveStars) if (t != null && t.GetComponent<TutorialStarDrift>() != null) { one = t; break; }
            float y0 = one != null ? one.position.y : 0f;
            if (one != null) one.GetComponent<TutorialStarDrift>().Step(1f, false);
            Check("with the world frozen the dust holds still", one != null && Mathf.Approximately(one.position.y, y0));

            // arrows: one per piece, drawn by the guides
            var guides = GuidesOf(hints);
            rig.Sync();
            ship.transform.position = new Vector3(1.1f, -2.6f, 0f);
            Cleanup0();
            Begin(hints, StepIndex("dust"));
            rig.Sync();
            guides.PointAtStars(spawnGoodStuffTut.LiveStars);
            guides.SendMessage("Update");
            int onScreen = 0;
            var cam = Camera.main;
            foreach (var t in spawnGoodStuffTut.LiveStars)
            {
                Vector3 sp = cam.WorldToScreenPoint(t.position);
                if (sp.y >= 0f && sp.y <= ScreenInfo.Height && sp.x >= 0f && sp.x <= ScreenInfo.Width) onScreen++;
            }
            Check("an arrow over every piece on screen (" + guides.StarArrowsShown + " / " + onScreen + ")",
                  onScreen >= 4 && guides.StarArrowsShown == onScreen);

            // collect one: its arrow goes with it
            var live = new List<Transform>(spawnGoodStuffTut.LiveStars);
            Object.DestroyImmediate(live[0].gameObject);
            guides.SendMessage("Update");
            Check("a collected piece loses its arrow (" + guides.StarArrowsShown + ")", guides.StarArrowsShown == onScreen - 1);

            // ... and a piece that leaves the screen loses its arrow too
            live[1].position = new Vector3(0f, -50f, 0f);
            guides.SendMessage("Update");
            Check("so does one that left the screen (" + guides.StarArrowsShown + ")", guides.StarArrowsShown == onScreen - 2);

            // other prompts keep the arrow to themselves
            Check("arrows stay off during the atom steps, the alien and the pause readout",
                  !Hints.StarArrowsAllowed(TutorialCue.SpawnGreenAtom) && !Hints.StarArrowsAllowed(TutorialCue.SpawnCapacitorAtom) &&
                  !Hints.StarArrowsAllowed(TutorialCue.SpawnEnemy) && !Hints.StarArrowsAllowed(TutorialCue.PointAtPauses) &&
                  Hints.StarArrowsAllowed(TutorialCue.SpawnStars));

            // advance on collect
            var dustStep = TutorialScript.Steps[StepIndex("dust")];
            Sample(hints, 1f / 60f);
            var start = hints.Signals;
            Check("the step is not met before a catch", !TutorialScript.IsMet(dustStep, start, hints.Signals));
            score.AwardStarDust(.5f);   // what collisionDetection does on a catch
            Sample(hints, 1f / 60f);
            Check("the step advances once the dust is collected", TutorialScript.IsMet(dustStep, start, hints.Signals));

            // missed: every piece falls away -> a fresh stream after RushAgainSeconds (fake clock)
            Cleanup0();
            Begin(hints, StepIndex("dust"));
            foreach (var t in new List<Transform>(spawnGoodStuffTut.LiveStars))
                if (t != null) Object.DestroyImmediate(t.gameObject);
            hints.TickDust(Hints.RushAgainSeconds * .5f);
            int before = CountRushed();
            hints.TickDust(Hints.RushAgainSeconds * .6f);
            int after = CountRushed();
            Check("missed dust is rushed again after " + Hints.RushAgainSeconds + " s (" + before + " -> " + after + ")", before == 0 && after >= 4);
            hints.TickDust(5f);
            Check("but never stacked while a stream is still falling", CountRushed() == after);
        }
        Cleanup();
    }

    static void Cleanup0()
    {
        foreach (var t in new List<Transform>(spawnGoodStuffTut.LiveStars))
            if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    static int CountRushed()
    {
        int n = 0;
        foreach (var t in spawnGoodStuffTut.LiveStars) if (t != null && t.GetComponent<TutorialStarDrift>() != null) n++;
        return n;
    }

    // ---- HEARTS --------------------------------------------------------------

    static void Hearts()
    {
        GameObject ship;
        var hints = OpenTutorial(out ship);
        if (hints == null || ship == null) { Check("tutorialS5 has the director and the ship", false); return; }
        var cd = ship.GetComponent<collisionDetection>();
        Check("the tutorial ship has collisionDetection", cd != null);
        if (cd == null) return;
        collisionDetection.MAXLIFE = -1;
        collisionDetection.lifeCounter = 5;   // stale from an earlier run
        typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
        int id = ShipId.Of(ship, ShipId.Equipped());
        Check("the tutorial flies the hull plus " + ShipLives.TutorialExtraHearts + " extra hearts (MAXLIFE " + collisionDetection.MAXLIFE + ")",
              collisionDetection.MAXLIFE == ShipLives.TutorialMax(id) && collisionDetection.MAXLIFE >= ShipLives.TutorialExtraHearts + 1 &&
              collisionDetection.MAXLIFE >= ShipLives.Base(id) + ShipLives.TutorialExtraHearts);
        Check("and starts with every heart (lifeCounter " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 0);

        // the orbiting hearts: the attach script finds the tutorial ship
        var attach = new GameObject("~attach").AddComponent<ShipLivesIndicatorAttach>();
        attach.SendMessage("Update");
        var indicator = ship.GetComponent<ShipLivesIndicator>();
        Check("the hearts indicator attaches to the tutorial ship", indicator != null);
        if (indicator != null)
        {
            Own(indicator, "Start");
            Check("it builds one orbiting heart per life (" + indicator.ShownCount + ")", indicator.ShownCount == collisionDetection.MAXLIFE);
        }

        // the dust before it, the hearts step topping up
        Begin(hints, StepIndex("hearts"));
        Check("the hearts step has the touch cue", TutorialScript.Steps[StepIndex("hearts")].cue == TutorialCue.GrantHearts);

        // the alien crashes into the ship, again and again: never a death
        var spawnedPlayerDied = false;
        int maxMark = collisionDetection.MAXLIFE;
        for (int crash = 1; crash <= maxMark + 3; crash++)
        {
            var alien = TutorialEnemy.Spawn(ship.transform.position.x);
            if (alien == null) { Check("the alien spawns", false); break; }
            alien.transform.position = ship.transform.position;
            PlayerInvuln.Reset();
            var hit = alien.GetComponent<Collider2D>();
            if (hit == null) { Check("the alien has a collider", false); break; }
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst).Invoke(cd, new object[] { hit });
            if (crash == 1)
            {
                Check("the first crash costs exactly one heart (lifeCounter " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 1);
                Check("and the player survives it", !buttonClicks.playerDied);
            }
            if (buttonClicks.playerDied) spawnedPlayerDied = true;
            TutorialEnemy.Clear();
        }
        Check("crash after crash, " + (maxMark + 3) + " in all, the tutorial never ends in a death (lifeCounter " + collisionDetection.lifeCounter + " of " + maxMark + ")",
              !spawnedPlayerDied && !buttonClicks.playerDied && collisionDetection.lifeCounter <= maxMark - 1);
        Check("DeathCrash never started", !DeathCrash.Running);

        // the next beat starts with every heart
        collisionDetection.lifeCounter = 1;
        Begin(hints, StepIndex("power"));
        Check("hearts are topped back up before the next beat (lifeCounter " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 0);

        // the safety net between beats: never down to the last heart
        collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
        typeof(Hints).GetMethod("KeepHeartsUp", Inst).Invoke(hints, null);
        Check("and the last heart is never left as the only one (" + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter <= collisionDetection.MAXLIFE - 2);

        // a hit that would be fatal is not
        PlayerInvuln.Reset();
        collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
        var rock = new GameObject("rock", typeof(CircleCollider2D));
        rock.tag = "Astr";
        typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst).Invoke(cd, new object[] { rock.GetComponent<Collider2D>() });
        Check("even a hit on the last heart cannot kill the tutorial", !buttonClicks.playerDied && collisionDetection.lifeCounter == collisionDetection.MAXLIFE - 1);
        Object.DestroyImmediate(rock);
        Object.DestroyImmediate(attach.gameObject);
        collisionDetection.lifeCounter = 0;
        PlayerInvuln.Reset();
        Cleanup();
    }

    // ---- normal runs after the tutorial ---------------------------------------

    static void NormalRunHearts()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        buttonClicks.playerDied = false;
        foreach (int id in new[] { ShipId.Starter, 1, 3 })
        {
            if (!ShipId.IsValid(id)) continue;
            // the tutorial just left the counters low and high
            collisionDetection.lifeCounter = 2;
            collisionDetection.MAXLIFE = ShipLives.TutorialMax(id);
            var prefab = Resources.Load<GameObject>(spawnShips.PrefabPathFor(id));
            var go = Object.Instantiate(prefab);
            go.name = ShipId.ObjectName(id) + "(Clone)";
            spawnShips.ApplyHull(go, id);
            var cd = go.GetComponent<collisionDetection>();
            if (cd == null) { Check("ship " + id + " has collisionDetection", false); Object.DestroyImmediate(go); continue; }
            typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
            Check("ship " + id + ": a run after the tutorial starts with its usual hearts (" + collisionDetection.MAXLIFE + " = ShipLives.Max " + ShipLives.Max(id) + ")",
                  collisionDetection.MAXLIFE == ShipLives.Max(id) && collisionDetection.lifeCounter == 0);
            Check("ship " + id + ": the run is not the tutorial (a fatal hit still kills)", !collisionDetection.TutorialCannotDie(go));
            Object.DestroyImmediate(go);
        }
        Check("the tutorial's extra hearts live only in ShipLives.TutorialMax",
              ShipLives.TutorialMax(ShipId.Starter) == Mathf.Max(ShipLives.Base(ShipId.Starter) + ShipLives.TutorialExtraHearts, ShipLives.TutorialExtraHearts + 1) &&
              ShipLives.Max(ShipId.Starter) <= ShipLives.Most);
        collisionDetection.lifeCounter = 0;
    }

    // ---- TIMING --------------------------------------------------------------

    static void Timeline()
    {
        var steps = TutorialScript.Steps;
        float competent = TutorialScript.IntroSeconds, worst = TutorialScript.IntroSeconds;
        string table = "";
        foreach (var st in steps)
        {
            float speak = TutorialScript.SpeakSeconds(TutorialScript.Speak(st.line)) + .6f;   // + Hints.readSeconds
            float c = Mathf.Max(speak, st.par);
            float w = Mathf.Max(speak, st.timeout);
            competent += c;
            worst += w;
            table += "\n  " + st.id + ": spoken+read " + speak.ToString("0.0") + ", par " + st.par.ToString("0.0") + " -> " + c.ToString("0.0") + " s; timeout " + st.timeout.ToString("0.0") + " -> " + w.ToString("0.0") + " s";
        }
        competent += TutorialScript.EndingSeconds;
        worst += TutorialScript.EndingSeconds;
        Debug.Log("[TP] beat timeline:" + table + "\n  total competent " + competent.ToString("0.0") + " s, worst " + worst.ToString("0.0") + " s");
        Check("a competent player reaches the completion panel in 30-45 s (" + competent.ToString("0.0") + ")", competent >= 30f && competent <= 45f);
        Check("even every step timing out stays under 75 s (" + worst.ToString("0.0") + ")", worst < 75f);

        // fake clock: a player who does nothing at all is moved on by every timeout
        float clock = TutorialScript.IntroSeconds;
        int done = 0;
        var none = new TutorialSignals { worldMoving = false };
        foreach (var st in steps)
        {
            float lineDone = TutorialScript.SpeakSeconds(TutorialScript.Speak(st.line));
            bool advanced = false;
            for (float t = 0f; t < 120f; t += .05f)
            {
                bool finished = t >= lineDone;
                float since = Mathf.Max(0f, t - lineDone);
                if (TutorialScript.CanAdvance(st, none, none, finished, since, .6f, t)) { clock += t; advanced = true; break; }
            }
            if (advanced) done++;
        }
        Check("a player who does nothing is still taken through all " + steps.Length + " steps by the timeouts (" + done + ", " + clock.ToString("0.0") + " s)",
              done == steps.Length && clock + TutorialScript.EndingSeconds < 75f);
        var first = steps[0];
        Check("a step never ends before its line is spoken and read",
              !TutorialScript.CanAdvance(first, none, none, false, 0f, .6f, 999f) && !TutorialScript.CanAdvance(first, none, none, true, .1f, .6f, 999f));
        Check("a step whose action is done ends once the line is read, before its timeout",
              TutorialScript.CanAdvance(TutorialScript.Steps[StepIndex("hearts")], none, none, true, .7f, .6f, .1f));
        string doc = File.Exists("../docs/tutorial-flow.md") ? File.ReadAllText("../docs/tutorial-flow.md") : "";
        bool allIds = doc.Length > 0;
        foreach (var st in steps) allIds &= doc.Contains("`" + st.id + "`");
        Check("docs/tutorial-flow.md documents the beat timeline and atom order for every step", allIds && doc.Contains("Atom order"));
    }

    // ---- ATOMS ---------------------------------------------------------------

    static void AtomOrder()
    {
        var steps = TutorialScript.Steps;
        var order = new List<TutorialAtom>();
        foreach (var st in steps)
        {
            var kind = Hints.AtomFor(st.cue);
            if (kind != TutorialAtom.None) order.Add(kind);
            string line = TutorialScript.ToPlainText(st.line);
            int stops = 0;
            foreach (char c in line) if (c == '.' || c == '!' || c == '?') stops++;
            bool atomStep = kind != TutorialAtom.None;
            Check("\"" + st.id + "\" is " + (atomStep ? "one sentence" : "a short line") + " of <= 70 characters (" + line.Length + ")",
                  line.Length <= 70 && stops <= (atomStep ? 1 : 2) && (!atomStep || (line.EndsWith(".") || line.EndsWith("!"))));
        }
        string got = string.Join(",", order), want = string.Join(",", TutorialScript.AtomOrder);
        Check("atoms are introduced in the documented order: repair (green), shield (blue), pause (red), weapon (violet) (" + got + ")", got == want);
        Check("every atom type is introduced exactly once", order.Count == new HashSet<TutorialAtom>(order).Count && order.Count == 4);

        // the run: every step in order, the way Hints plays them
        GameObject ship;
        var hints = OpenTutorial(out ship);
        Check("the tutorial scene has no random pickup spawner",
              Object.FindFirstObjectByType<spawnGoodStuff>() == null && Object.FindFirstObjectByType<HealAtomSpawner>() == null);
        if (hints == null) return;
        for (int i = 0; i < steps.Length; i++)
        {
            Begin(hints, i);
            var kind = Hints.AtomFor(steps[i].cue);
            if (kind != TutorialAtom.None)
            {
                Check("step \"" + steps[i].id + "\" drops its " + kind + " atom in, once (" + spawnGoodStuffTut.Spawned(kind) + ")",
                      spawnGoodStuffTut.Spawned(kind) == 1 && spawnGoodStuffTut.LiveAtom != null);
                // idle on the step for a long fake time: nothing else is spawned
                var spawn = typeof(spawnGoodStuffTut).GetMethod("spawn", Inst);
                var spawner = Object.FindFirstObjectByType<spawnGoodStuffTut>();
                for (int f = 0; f < 600; f++) spawn.Invoke(spawner, null);
            }
            typeof(Hints).GetMethod("EndStep", Inst).Invoke(hints, new object[] { steps[i] });
            Check("step \"" + steps[i].id + "\" leaves no atom behind", spawnGoodStuffTut.LiveAtom == null);
        }
        int total = 0;
        foreach (var k in TutorialScript.AtomOrder) total += spawnGoodStuffTut.Spawned(k);
        Check("after the whole script exactly 4 atoms were ever spawned, one of each (" + total + ", all " + spawnGoodStuffTut.SpawnedTotal + ")",
              total == 4 && spawnGoodStuffTut.SpawnedTotal == 4);
        Cleanup();
    }

    // Each atom: spawns where it can be seen, is collected through the ship's real
    // pickup path, does its real effect, gives feedback and ends its step.
    static void AtomEffects()
    {
        foreach (var kind in TutorialScript.AtomOrder)
        {
            GameObject ship;
            var hints = OpenTutorial(out ship);
            if (hints == null || ship == null) { Check("tutorial for the " + kind + " atom", false); continue; }
            var cd = ship.GetComponent<collisionDetection>();
            collisionDetection.MAXLIFE = -1;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
            TutorialShipHull.Apply();
            var power = ship.GetComponent<ShipPowerController>();
            if (power != null)
            {
                Own(power, "Awake");
                Own(power, "Start");
                var gun = ship.GetComponentInChildren<UltimateGun>();
                if (gun != null) Own(gun, "Awake");
            }
            score.pauseCounter = 7;
            int stepIndex = System.Array.FindIndex(TutorialScript.Steps, st => Hints.AtomFor(st.cue) == kind);
            var step = TutorialScript.Steps[stepIndex];
            string name = kind + " atom";
            ship.transform.position = new Vector3(0f, -2.6f, 0f);
            Begin(hints, stepIndex);
            Sample(hints, 1f / 60f);
            var start = hints.Signals;
            var live = spawnGoodStuffTut.LiveAtom;
            Check(name + " spawns", live != null);
            if (live == null) continue;
            var drift = live.GetComponent<TutorialAtomDrift>();
            var col = live.GetComponent<Collider2D>();
            Check(name + " is a pickUp with an enabled collider",
                  col != null && col.enabled && live.CompareTag("pickUp"));
            Check(name + " can touch the ship's layer", !Physics2D.GetIgnoreLayerCollision(ship.layer, live.gameObject.layer));

            // visible within a second or so, in the lane, and stays on screen
            float bottom, top;
            TutorialAtomDrift.View(out bottom, out top);
            float seenAt = -1f;
            for (float t = 0f; t < 3f; t += 1f / 60f)
            {
                drift.Step(1f / 60f, true);
                if (seenAt < 0f && live.position.y < top - .3f) seenAt = t;
            }
            Check(name + " is on screen and in the lane (y " + live.position.y.ToString("0.0") + ", seen after " + seenAt.ToString("0.0") + " s)",
                  seenAt >= 0f && seenAt < 1.5f && live.position.y > bottom && live.position.y < top && Mathf.Abs(live.position.x) <= 2.4f);

            if (kind == TutorialAtom.Cooldown) score.pauseCounter = 0;   // the world runs, so the weapon can fire
            int heal0 = collisionDetection.healAtomPickups, blue0 = collisionDetection.shieldAtomPickups,
                red0 = collisionDetection.pauseAtomPickups, pauses0 = score.pauseCounter;
            int life0 = collisionDetection.lifeCounter;
            ship.transform.position = new Vector3(live.position.x, live.position.y - .1f, 0f);
            Physics2D.SyncTransforms();
            var hb = ShipHitbox.Of(ship);
            int caught = hb != null ? hb.CatchPickups() : (cd.CollectPickup(col) ? 1 : 0);
            Check(name + " is caught through the ship's pickup path (" + caught + ")", caught == 1);
            Check(name + " is spent once collected", spawnGoodStuffTut.LiveAtom == null || spawnGoodStuffTut.LiveAtom.GetComponent<Collider2D>() == null || !spawnGoodStuffTut.LiveAtom.GetComponent<Collider2D>().enabled);

            switch (kind)
            {
                case TutorialAtom.Green:
                    Check("repair: the dented ship (" + life0 + " hit) gets its heart back (" + collisionDetection.lifeCounter + ")",
                          life0 == 1 && collisionDetection.lifeCounter == 0 && collisionDetection.healAtomPickups == heal0 + 1);
                    Check("repair says so", cd.hypeText != null && cd.hypeText.text == "REPAIRED");
                    break;
                case TutorialAtom.Blue:
                    var shield = ShipShield.For(ship);
                    Check("shield: the shield comes up (atomCheck " + collisionDetection.atomCheck + ", " + collisionDetection.invTimer.ToString("0.0") + " s)",
                          collisionDetection.atomCheck && collisionDetection.invTimer > 5f && shield != null && shield.IsUp && collisionDetection.shieldAtomPickups == blue0 + 1);
                    Check("shield says so", cd.boostText != null && cd.boostText.text == "Boost!");
                    break;
                case TutorialAtom.Red:
                    Check("pause: the pause counter goes up (" + pauses0 + " -> " + score.pauseCounter + ")",
                          score.pauseCounter > pauses0 && collisionDetection.pauseAtomPickups == red0 + 1);
                    break;
                case TutorialAtom.Cooldown:
                    Own(power, "Update");
                    Check("violet: the armed weapon is charged and goes off (" + (power != null ? power.UltimatesFired : -1) + ")",
                          power != null && power.UltimatesFired == 1);
                    break;
            }

            Sample(hints, 1f / 60f);
            Check(name + ": the step advances once the line is read",
                  TutorialScript.CanAdvance(step, start, hints.Signals, true, 1f, .6f, 2f));
            Check(name + ": and not before the atom is caught or the step times out",
                  !TutorialScript.CanAdvance(step, start, start, true, 1f, .6f, step.timeout - .5f) &&
                  TutorialScript.CanAdvance(step, start, start, true, 1f, .6f, step.timeout));
            if (kind == TutorialAtom.Cooldown && power != null)
            {
                Own(power, "OnDestroy");
                if (power.Runner != null) power.Runner.SendMessage("OnDestroy");
                if (power.Secret != null) power.Secret.SendMessage("OnDestroy");
                AttackPool.StopAll();
                typeof(ShipPowerController).GetMethod("FinishCinematic", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.lifeCounter = 0;
            Cleanup();
        }
        score.pauseCounter = 50;
    }

    // SendMessage reaches every component on the object (the ship's collisionDetection
    // would Start twice): call the one component's own method.
    static void Own(object component, string method)
    {
        var m = component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m != null) m.Invoke(component, null);
    }

    // ---- PANEL ---------------------------------------------------------------

    static GameObject TapAt(ScreenFitRig rig, TutorialCompletePanel panel, Vector2 pos)
    {
        var es = EventSystem.current;
        if (es == null) es = new GameObject("~ES", typeof(EventSystem)).GetComponent<EventSystem>();
        var ped = new PointerEventData(es) { position = pos, button = PointerEventData.InputButton.Left, clickCount = 1 };
        Graphic best = null;
        int bestDepth = int.MinValue, gi = -1;
        foreach (var g in panel.GetComponentsInChildren<Graphic>(false))
        {
            gi++;
            if (!g.raycastTarget || !g.enabled || g.canvas == null || g.canvasRenderer.cull) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pos, rig.ui, g.raycastPadding)) continue;
            bool blocked = false;
            for (var t = g.transform; t != null; t = t.parent)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null && (!cg.blocksRaycasts || !cg.interactable && t.GetComponent<Selectable>() == null && false)) { blocked = true; break; }
            }
            if (blocked) continue;
            if (gi > bestDepth) { best = g; bestDepth = gi; }
        }
        if (best == null) return null;
        var hit = best.gameObject;
        var result = new RaycastResult { gameObject = hit };
        ped.pointerCurrentRaycast = result;
        ped.pointerPressRaycast = result;
        var down = ExecuteEvents.ExecuteHierarchy(hit, ped, ExecuteEvents.pointerDownHandler);
        ped.pointerPress = down != null ? down : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
        ped.eligibleForClick = true;
        ExecuteEvents.ExecuteHierarchy(hit, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(hit, ped, ExecuteEvents.pointerClickHandler);
        return hit;
    }

    static Vector2 CentreOf(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * .5f;
    }

    static void CompletePanel()
    {
        FitDevice dev = FitDevice.All[0];
        foreach (var d in FitDevice.All) if (d.id == "and-1080x2340-notch") dev = d;
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        using (var rig = new ScreenFitRig(dev, CameraFit.GameplayHalfWidth))
        {
            var oldHome = tutButtonClicks.LoadMenu;
            var oldLoad = TutorialLiftOff.LoadGame;
            int homes = 0, loads = 0;
            tutButtonClicks.LoadMenu = () => homes++;
            TutorialLiftOff.LoadGame = () => loads++;
            TutorialLiftOff.Reset();
            try
            {
                // edit mode runs persistent listeners only if told to
                foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                        UnityEditor.Events.UnityEventTools.SetPersistentListenerState(b.onClick, i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                var host = new GameObject("~tutClicks").AddComponent<tutButtonClicks>();
                host.SendMessage("Start");
                var panel = TutorialCompletePanel.Show();
                Check("the card builds", panel != null);
                if (panel == null) return;
                panel.Skip();
                rig.Sync();
                Canvas.ForceUpdateCanvases();
                var panelRt = panel.Panel;

                var texts = new List<string>();
                foreach (var t in panel.GetComponentsInChildren<Text>(false)) texts.Add(t.text);
                Debug.Log("[TP] card texts: " + string.Join(" | ", texts));
                Check("the title is TUTORIAL COMPLETE", panel.TitleLabel == "TUTORIAL COMPLETE");
                Check("the choices are LIFT OFF and HOME", panel.LiftOffLabel == "LIFT OFF" && panel.HomeLabel == "HOME");
                Check("nothing else on the card: no stats, footer or extra text (" + texts.Count + " texts)",
                      texts.Count == 3 && texts.Contains("TUTORIAL COMPLETE") && texts.Contains("LIFT OFF") && texts.Contains("HOME"));
                int buttons = panel.GetComponentsInChildren<Button>(false).Length;
                Check("exactly two buttons (" + buttons + ")", buttons == 2);
                var frame = panelRt.Find("Frame");
                var frameImg = frame != null ? frame.GetComponent<Image>() : null;
                Check("the frame is the Flight Complete frame (dp_panel)", frameImg != null && frameImg.sprite == Resources.Load<Sprite>("DeathPanel/dp_panel"));
                var liftBtn = panel.LiftOffSlot.GetComponentInChildren<Button>();
                var homeBtn = panel.HomeSlot.GetComponentInChildren<Button>();
                Check("the buttons wear the same plate and size as Flight Complete's",
                      liftBtn != null && homeBtn != null &&
                      liftBtn.GetComponent<Image>().sprite == Resources.Load<Sprite>("DeathPanel/dp_button") &&
                      Mathf.Approximately(((RectTransform)liftBtn.transform).sizeDelta.x, DeathPanelView.ButtonWidth) &&
                      Mathf.Approximately(((RectTransform)liftBtn.transform).sizeDelta.y, DeathPanelView.ButtonHeight));
                var title = panelRt.Find("Title").GetComponent<Text>();
                var deathFont = Object.FindFirstObjectByType<score>().speedValue.font;
                Check("it uses the HUD font like the other panels", title.font == deathFont || deathFont == null);

                // real taps: HOME
                var hit = TapAt(rig, panel, CentreOf(panel.HomeSlot));
                Check("a tap on HOME reaches the HOME button (" + (hit != null ? hit.name : "nothing") + ")", hit != null && homeBtn != null && hit.transform.IsChildOf(homeBtn.transform));
                Check("and goes home once (" + homes + "), not into the level", homes == 1 && loads == 0 && TutorialLiftOff.Started == 0);

                // real taps: LIFT OFF (the first press starts the departure, a second does nothing)
                hit = TapAt(rig, panel, CentreOf(panel.LiftOffSlot));
                Check("a tap on LIFT OFF reaches the LIFT OFF button (" + (hit != null ? hit.name : "nothing") + ")", hit != null && liftBtn != null && hit.transform.IsChildOf(liftBtn.transform));
                Check("and starts the portal lift-off, not the level yet (started " + TutorialLiftOff.Started + ", loaded " + loads + ")",
                      TutorialLiftOff.Started == 1 && loads == 0 && TutorialLiftOff.Playing);
                Check("the card is gone while the ship flies", !panel.gameObject.activeSelf);
                liftBtn.onClick.Invoke();
                homeBtn.onClick.Invoke();
                Check("a second press (or HOME) mid-flight does nothing (started " + TutorialLiftOff.Started + ", homes " + homes + ")",
                      TutorialLiftOff.Started == 1 && homes == 1);
                Object.DestroyImmediate(host.gameObject);
            }
            finally
            {
                tutButtonClicks.LoadMenu = oldHome;
                TutorialLiftOff.LoadGame = oldLoad;
                if (TutorialLiftOff.Live != null) Object.DestroyImmediate(TutorialLiftOff.Live.gameObject);
                var stray = GameObject.Find("~PortalDeparture");
                if (stray != null) Object.DestroyImmediate(stray);
                TutorialLiftOff.Reset();
            }
        }
    }

    // ---- LIFT OFF ------------------------------------------------------------

    static void LiftOff()
    {
        GameObject ship;
        var hints = OpenTutorial(out ship);
        if (ship == null) { Check("tutorial ship for the lift-off", false); return; }
        var events = new List<string>();
        var oldLoad = TutorialLiftOff.LoadGame;
        TutorialLiftOff.Reset();
        TutorialLiftOff.LoadGame = () => events.Add("load " + TutorialLiftOff.NextScene);
        try
        {
            ship.transform.position = new Vector3(.6f, -2.8f, 0f);
            float scale0 = ship.transform.localScale.x;
            var flight = TutorialLiftOff.Begin();
            events.Add("begin");
            Check("a portal departure plays", flight != null && flight.Portal != null && flight.Portal.Departing);
            var again = TutorialLiftOff.Begin();
            Check("pressing again does not start a second one", again == flight && TutorialLiftOff.Started == 1);
            Check("it does not take over the run-start hand-off (PortalArrival.Active stays off)", !PortalArrival.Active);

            bool portalSeen = false;
            float minScale = scale0, endY = ship.transform.position.y;
            float t = 0f;
            while (TutorialLiftOff.Playing && t < 6f)
            {
                flight.Step(1f / 60f);
                t += 1f / 60f;
                if (flight.Portal != null && flight.Portal.CoreRenderer != null && flight.Portal.CoreRenderer.enabled) portalSeen = true;
                minScale = Mathf.Min(minScale, ship.transform.localScale.x);
            }
            Check("the gateway is drawn while it plays", portalSeen);
            Check("the ship flies into it and shrinks away (" + (minScale / scale0).ToString("0.000") + " of its size)", minScale < scale0 * .1f);
            Check("it takes about " + TutorialLiftOff.Seconds + " s (" + t.ToString("0.00") + ")", t > TutorialLiftOff.Seconds * .8f && t < TutorialLiftOff.Seconds + .7f);
            events.Add("end");
            Check("the portal plays exactly once and the level loads exactly once, straight to gameS1 (" + string.Join(", ", events) + ")",
                  string.Join(",", events) == "begin,load gameS1,end" && TutorialLiftOff.Loaded == 1);
            flight.Step(1f);
            Check("stepping on after the end does nothing more", TutorialLiftOff.Loaded == 1);

            // and what plays when the level opens: the usual portal arrival, no replay shortcut
            Check("the first level then plays the portal arrival (the run is not a replay)",
                  WorldManager.PinnedReplayWorld < 0 && WorldEntry.Plan(0, WorldManager.PinnedReplayWorld >= 0) == WorldEntry.Kind.Portal);
            string tut = File.ReadAllText("Assets/Scripts/Tutorial/tutButtonClicks.cs");
            Check("LIFT OFF no longer loads the level at once (no LoadScene(\"gameS1\") in replay)", !tut.Contains("LoadScene(\"gameS1\")"));
        }
        finally
        {
            TutorialLiftOff.LoadGame = oldLoad;
            var stray = GameObject.Find("~PortalDeparture");
            if (stray != null) Object.DestroyImmediate(stray);
            if (TutorialLiftOff.Live != null) Object.DestroyImmediate(TutorialLiftOff.Live.gameObject);
            TutorialLiftOff.Reset();
        }
        Cleanup();
    }

    // ---- FINGER OFFSET ---------------------------------------------------------

    static void FingerOffset()
    {
        Check("the finger-to-ship offset is 25% up on the old 1.0 u (" + ShipReach.FingerOffset + ")", Mathf.Approximately(ShipReach.FingerOffset, 1.25f));
        Check("the hearts' thumb clearance follows it", Mathf.Approximately(HeartOrbit.ThumbBelow, ShipReach.FingerOffset));
        foreach (var f in new[] { "Assets/Scripts/Ship/movePlayer.cs", "Assets/Scripts/Gameplay/movePlayerInTut.cs", "Assets/Scripts/Worlds/Planetfall/Planetfall.cs" })
        {
            string src = File.ReadAllText(f);
            Check(Path.GetFileName(f) + " places the ship with ShipReach.FingerOffset (no private number)",
                  src.Contains("ShipReach.FingerOffset") && !src.Contains("fingerPos.y + 1.5f") && !src.Contains("fingerPos.y + 1f"));
        }
        // every touch controller in the project reads it: no other script maps a finger to the ship
        var users = new List<string>();
        foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            if (File.ReadAllText(f).Contains("TouchInput.Position") && File.ReadAllText(f).Contains("fingerPos")) users.Add(Path.GetFileName(f));
        bool allUse = users.Count >= 2;
        foreach (var u in users)
        {
            var path = Directory.GetFiles("Assets/Scripts", u, SearchOption.AllDirectories)[0];
            allUse &= File.ReadAllText(path).Contains("ShipReach.FingerOffset");
        }
        Check("every touch controller (" + string.Join(", ", users) + ") uses it", allUse);

        // the whole lane is still reachable, bottom edge and sides included
        foreach (var d in FitDevice.All)
        {
            ScreenInfo.ClearOverride();
            ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
            var cam = Camera.main;
            if (cam == null) break;
            cam.aspect = d.Aspect;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
            PlayField.Reset();
            var f = PlayField.Live;
            float lowest = ShipReach.ClampY(f.bottom + ShipReach.FingerOffset);   // a finger on the screen's bottom edge
            if (lowest > ShipReach.Bottom + .05f)
                Check(d.id + ": a finger on the bottom edge reaches the ship's floor (" + lowest.ToString("0.000") + " vs " + ShipReach.Bottom.ToString("0.000") + ")", false);
            float highest = ShipReach.ClampY(f.top);
            if (Mathf.Abs(highest - ShipReach.Top) > .001f) Check(d.id + ": the top is reachable", false);
        }
        Check("on every device a finger on the bottom edge puts the ship within 0.05 u of its floor, and the top is reachable", true);
        ScreenInfo.ClearOverride();
        PlayField.Reset();
    }

    // ---- SIZE ----------------------------------------------------------------

    static void ShipSize()
    {
        int id = ShipId.Equipped();
        if (!ShipId.IsValid(id)) id = ShipId.Starter;

        // the tutorial's ship, dressed as the equipped hull like the scene does at load
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var tutShip = TutorialShipHull.Apply();
        Check("tutorialS5 has its ship", tutShip != null);
        if (tutShip == null) return;
        Own(tutShip.GetComponent<lifeControler>(), "Start");
        var tutRenderer = tutShip.GetComponent<SpriteRenderer>();
        float tutHeightU = tutRenderer.bounds.size.y;
        float tutOrtho = Camera.main.orthographicSize;
        var tutHit = ShipHitbox.Of(tutShip);
        Physics2D.SyncTransforms();
        Vector2 tutHull = tutHit != null && tutHit.Hull != null ? (Vector2)tutHit.Hull.bounds.size : Vector2.zero;
        float tutScale = tutShip.transform.localScale.x;

        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var prefab = Resources.Load<GameObject>(spawnShips.PrefabPathFor(id));
        var gameShip = Object.Instantiate(prefab);
        gameShip.name = ShipId.ObjectName(id) + "(Clone)";
        spawnShips.ApplyHull(gameShip, id);
        var gameRenderer = gameShip.GetComponent<SpriteRenderer>();
        float gameHeightU = gameRenderer.bounds.size.y;
        float gameOrtho = Camera.main.orthographicSize;
        var gameHit = ShipHitbox.Of(gameShip);
        Physics2D.SyncTransforms();
        Vector2 gameHull = gameHit != null && gameHit.Hull != null ? (Vector2)gameHit.Hull.bounds.size : Vector2.zero;
        Object.DestroyImmediate(gameShip);

        Check("both scenes draw the same hull at the same world size (tutorial " + tutHeightU.ToString("0.000") + " u, game " + gameHeightU.ToString("0.000") + " u, scale " +
              tutScale.ToString("0.000") + ")", Mathf.Abs(tutHeightU / gameHeightU - 1f) < .005f);
        Check("both cameras start from the same size (" + tutOrtho + " / " + gameOrtho + ")", Mathf.Approximately(tutOrtho, gameOrtho));
        if (tutHull.y > 0f && gameHull.y > 0f)
            Check("the hit polygon is the same size too (" + tutHull.y.ToString("0.000") + " / " + gameHull.y.ToString("0.000") + " u)",
                  Mathf.Abs(tutHull.y / gameHull.y - 1f) < .01f && Mathf.Abs(tutHull.x / gameHull.x - 1f) < .01f);

        int bad = 0;
        float worst = 0f;
        string detail = "";
        foreach (var d in FitDevice.All)
        {
            // both gameplay scenes run CameraFit at GameplayHalfWidth: the same view on this device
            float size = CameraFit.ComputeSize(tutOrtho, CameraFit.GameplayHalfWidth, d.w, d.h);
            float gameSize = CameraFit.ComputeSize(gameOrtho, CameraFit.GameplayHalfWidth, d.w, d.h);
            float tutPx = tutHeightU * d.h / (2f * size);
            float gamePx = gameHeightU * d.h / (2f * gameSize);
            float off = Mathf.Abs(tutPx / gamePx - 1f);
            worst = Mathf.Max(worst, off);
            if (off > .02f) { bad++; detail += " " + d.id + ":" + tutPx.ToString("0.0") + "/" + gamePx.ToString("0.0"); }
        }
        Check("on all " + FitDevice.All.Length + " devices the tutorial ship's on-screen height is the gameplay ship's within 2% (worst " + (worst * 100f).ToString("0.00") + "%)" + detail,
              bad == 0);
    }
}
