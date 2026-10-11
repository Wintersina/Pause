using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// The tutorial polish, rendered on a 1080x2340 phone through the screen-fit rig
// (real canvases, real camera): the dust rush with its arrows, the alien's
// crash with the orbiting hearts, the atom introductions in order, the
// Tutorial Complete card, the portal lift-off, the tutorial ship next to the
// gameplay ship before / after, and the finger offset before / after.
//
//   scripts/unity-batch.sh -executeMethod TutorialPolishPreview.Run
//   (output: $TUTORIAL_POLISH_DIR, else Builds/TutorialPolish)
public static class TutorialPolishPreview
{
    const int FrameHeight = 1100;
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static string dir;
    static FitDevice device;

    public static void Run()
    {
        dir = Environment.GetEnvironmentVariable("TUTORIAL_POLISH_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/TutorialPolish";
        Directory.CreateDirectory(dir);
        device = FitDevice.Find("and-1080x2340-notch");
        using (new TestHarness.Sandbox())
        {
            DustRush();
            Crash();
            Atoms();
            Card();
            LiftOff();
            ShipSize();
            Finger();
        }
        EditorApplication.Exit(0);
    }

    // ---- staging -----------------------------------------------------------

    static FitScreen Screen(string id, string scene, Action<ScreenFitRig> stage)
    {
        return new FitScreen { id = id, scene = scene, title = id, gameplayView = true, fullBleed = true, stage = stage };
    }

    static string Frame(string name, FitScreen screen)
    {
        string path = Path.Combine(dir, name + ".png");
        ScreenFitRunner.Run(screen, device, path, FrameHeight);
        return path;
    }

    static T Invoke<T>(string method, params object[] args)
    {
        return (T)typeof(ScreenFitScreens).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, args);
    }

    // The tutorial scene as a player sees it: backdrop, HUD, the ship (hearts orbiting), the robot saying `line`.
    static RobotSpeaker TutorialBase(ScreenFitRig rig, string line, Vector3 shipAt, out GameObject ship, out TutorialGuides guides)
    {
        Invoke<HudStyler>("WorldBase", rig, 0, false, true);
        ship = TutorialShipHull.Apply();
        Own(ship.GetComponent<lifeControler>(), "Start");
        ship.transform.position = shipAt;
        var cd = ship.GetComponent<collisionDetection>();
        collisionDetection.MAXLIFE = ShipLives.TutorialMax(ShipId.Of(ship, ShipId.Equipped()));
        collisionDetection.lifeCounter = 0;
        var hearts = ship.GetComponent<ShipLivesIndicator>() ?? ship.AddComponent<ShipLivesIndicator>();
        Own(hearts, "Start");
        Hearts(ship, 8);
        var speaker = RobotSpeaker.Create(null);
        rig.Sync();
        speaker.Say(TutorialScript.Speak(line));
        speaker.CompleteLine();
        typeof(RobotSpeaker).GetMethod("Fit", Any).Invoke(speaker, new object[] { true });
        typeof(ScreenFitScreens).GetMethod("SnapRobot", Any).Invoke(null, new object[] { speaker });
        guides = TutorialGuides.Create(speaker.Root);
        rig.Sync();
        return speaker;
    }

    static void Hearts(GameObject ship, int frames)
    {
        var hearts = ship.GetComponent<ShipLivesIndicator>();
        if (hearts == null) return;
        var place = typeof(HeartOrbit).GetMethod("Place", Any);
        var breaks = typeof(HeartOrbit).GetMethod("StepBreaks", Any);
        for (int i = 0; i < frames; i++)
        {
            place.Invoke(hearts, new object[] { 1f / 60f, 1f / 60f });
            if (breaks != null) breaks.Invoke(hearts, new object[] { 1f / 60f });
        }
    }

    static void Own(object c, string method) { c.GetType().GetMethod(method, Any).Invoke(c, null); }

    static string Line(string id) { return Array.Find(TutorialScript.Steps, s => s.id == id).line; }

    static void Finish(ScreenFitRig rig, TutorialGuides guides)
    {
        guides.SendMessage("Update");
        rig.Sync();
    }

    // ---- 1: the dust rush ----------------------------------------------------

    static void DustRush()
    {
        var files = new List<string>();
        foreach (float t in new[] { 0f, .3f, .6f })
        {
            float at = t;
            var screen = Screen("dust-" + t, "tutorialS5", rig =>
            {
                GameObject ship; TutorialGuides guides;
                TutorialBase(rig, Line("dust"), new Vector3(1.1f, -2.6f, 0f), out ship, out guides);
                var spawner = UnityEngine.Object.FindFirstObjectByType<spawnGoodStuffTut>();
                spawner.SendMessage("Start");
                float bottom, top;
                TutorialAtomDrift.View(out bottom, out top);
                spawner.RushStars(ship.transform.position, top);
                foreach (var s in new List<Transform>(spawnGoodStuffTut.LiveStars))
                    for (float f = 0f; f < at; f += 1f / 60f) s.GetComponent<TutorialStarDrift>().Step(1f / 60f, true);
                guides.PointAtStars(spawnGoodStuffTut.LiveStars);
                Finish(rig, guides);
            });
            files.Add(Frame("dust_rush_" + files.Count, screen));
        }
        Strip(files, "1_dust_rush_strip.png");
    }

    // ---- 2: the alien crashes into the ship with its hearts orbiting ---------

    static void Crash()
    {
        var files = new List<string>();
        for (int phase = 0; phase < 3; phase++)
        {
            int ph = phase;
            var screen = Screen("crash-" + ph, "tutorialS5", rig =>
            {
                GameObject ship; TutorialGuides guides;
                TutorialBase(rig, Line("enemies"), new Vector3(0f, -2.4f, 0f), out ship, out guides);
                var alien = TutorialEnemy.Spawn(ship.transform.position.x);
                float bottom, top;
                TutorialAtomDrift.View(out bottom, out top);
                if (alien != null)
                {
                    alien.transform.position = new Vector3(ship.transform.position.x, ph == 0 ? ship.transform.position.y + 3.2f : ship.transform.position.y + .15f, 0f);
                    if (ph == 0) guides.PointAt(alien.transform);
                }
                if (ph >= 1 && alien != null)
                {
                    var cd = ship.GetComponent<collisionDetection>();
                    PlayerInvuln.Reset();
                    typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Any).Invoke(cd, new object[] { alien.GetComponent<Collider2D>() });
                    Hearts(ship, ph == 1 ? 10 : 70);
                }
                else Hearts(ship, 20);
                Finish(rig, guides);
            });
            files.Add(Frame("crash_" + ph, screen));
        }
        Strip(files, "2_alien_crash_hearts_strip.png");
    }

    // ---- 3: the atoms, one by one, in order -------------------------------------

    static void Atoms()
    {
        var files = new List<string>();
        foreach (var kind in TutorialScript.AtomOrder)
        {
            var k = kind;
            var step = Array.Find(TutorialScript.Steps, s => Hints.AtomFor(s.cue) == k);
            var screen = Screen("atom-" + k, "tutorialS5", rig =>
            {
                GameObject ship; TutorialGuides guides;
                TutorialBase(rig, step.line, new Vector3(0f, -2.8f, 0f), out ship, out guides);
                if (k == TutorialAtom.Green)
                {
                    collisionDetection.lifeCounter = 1;
                    ShipLivesIndicator.Impact(ship.transform.position + Vector3.up * .6f);
                    Hearts(ship, 60);
                }
                var spawner = UnityEngine.Object.FindFirstObjectByType<spawnGoodStuffTut>();
                spawner.SendMessage("Start");
                spawner.SpawnIntro(k);
                var drift = spawnGoodStuffTut.LiveAtom.GetComponent<TutorialAtomDrift>();
                for (float t = 0f; t < 1.6f; t += 1f / 60f) drift.Step(1f / 60f, true);
                guides.PointAt(spawnGoodStuffTut.LiveAtom);
                Finish(rig, guides);
            });
            files.Add(Frame("atom_" + files.Count + "_" + k, screen));
        }
        Strip(files, "3_atom_intro_order_strip.png");
    }

    // ---- 4: the Tutorial Complete card ----------------------------------------------

    static void Card()
    {
        var screen = Array.Find(ScreenFitScreens.All, s => s.id == "tutorial-complete");
        Frame("4_tutorial_complete_card", screen);
        // the Flight Complete panel beside it, for the style match
        var death = Array.Find(ScreenFitScreens.All, s => s.id == "game-death");
        if (death != null)
        {
            Frame("4_flight_complete_for_comparison", death);
            Strip(new List<string> { Path.Combine(dir, "4_tutorial_complete_card.png"), Path.Combine(dir, "4_flight_complete_for_comparison.png") }, "4_card_vs_flight_complete.png");
        }
    }

    // ---- 5: LIFT OFF through the portal -----------------------------------------------

    static void LiftOff()
    {
        var files = new List<string>();
        foreach (float t in new[] { 0f, .5f, 1f, 1.5f, 1.9f })
        {
            float at = t;
            var screen = Screen("liftoff-" + t, "tutorialS5", rig =>
            {
                GameObject ship; TutorialGuides guides;
                var speaker = TutorialBase(rig, Line("hearts"), new Vector3(.4f, -2.8f, 0f), out ship, out guides);
                speaker.gameObject.SetActive(false);
                var old = TutorialLiftOff.LoadGame;
                TutorialLiftOff.LoadGame = () => { };
                TutorialLiftOff.Reset();
                var flight = TutorialLiftOff.Begin();
                for (float f = 0f; f < at; f += 1f / 60f) flight.Step(1f / 60f);
                Hearts(ship, 1);
                rig.Sync();
                TutorialLiftOff.LoadGame = old;
            });
            files.Add(Frame("liftoff_" + files.Count, screen));
        }
        Strip(files, "5_portal_liftoff_strip.png");
        TutorialLiftOff.Reset();
    }

    // ---- 6: the ship's size: tutorial before / after / gameplay ---------------------------

    static void ShipSize()
    {
        var files = new List<string>();
        foreach (int variant in new[] { 0, 1, 2 })
        {
            int v = variant;
            string scene = v == 2 ? "gameS1" : "tutorialS5";
            var screen = Screen("size-" + v, scene, rig =>
            {
                if (v == 2)
                {
                    Invoke<HudStyler>("WorldBase", rig, 0, false, false);
                    int id = ShipId.Equipped();
                    var go = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(spawnShips.PrefabPathFor(id)));
                    go.name = ShipId.ObjectName(id) + "(Clone)";
                    spawnShips.ApplyHull(go, id);
                    go.transform.position = new Vector3(0f, -2.8f, 0f);
                    return;
                }
                GameObject ship; TutorialGuides guides;
                TutorialBase(rig, "Same ship, same size.", new Vector3(0f, -2.8f, 0f), out ship, out guides);
                if (v == 0) ship.transform.localScale = ship.transform.localScale / ShipScale.Main;   // how the tutorial drew it before
                Finish(rig, guides);
            });
            files.Add(Frame("ship_size_" + new[] { "tutorial_before", "tutorial_after", "gameplay" }[v], screen));
        }
        Strip(files, "6_ship_size_tutorial_before_after_gameplay.png");
    }

    // ---- 7: the finger offset, before / after -----------------------------------------------

    static void Finger()
    {
        var files = new List<string>();
        foreach (float offset in new[] { 1f, ShipReach.FingerOffset })
        {
            float o = offset;
            var screen = Screen("finger-" + o, "tutorialS5", rig =>
            {
                GameObject ship; TutorialGuides guides;
                Vector3 finger = new Vector3(.3f, -3.4f, 0f);
                TutorialBase(rig, "Fly with a finger.", finger + Vector3.up * o, out ship, out guides);
                var marker = new GameObject("~Finger");
                var sr = marker.AddComponent<SpriteRenderer>();
                sr.sprite = PickupGlow.HaloSprite;
                sr.color = new Color(1f, 1f, 1f, .75f);
                sr.sortingOrder = 900;
                marker.transform.position = finger;
                marker.transform.localScale = Vector3.one * .5f;
                var ring = new GameObject("~FingerRing").AddComponent<SpriteRenderer>();
                ring.sprite = PickupGlow.RingSprite;
                ring.color = Color.white;
                ring.sortingOrder = 901;
                ring.transform.position = finger;
                ring.transform.localScale = Vector3.one * .4f;
                Hearts(ship, 8);
                Finish(rig, guides);
            });
            files.Add(Frame("finger_" + (o < 1.1f ? "before_1.0" : "after_1.25"), screen));
        }
        Strip(files, "7_finger_offset_before_after.png");
    }

    // ---- filmstrips -----------------------------------------------------------------------

    static void Strip(List<string> files, string name)
    {
        var tex = new List<Texture2D>();
        int w = 0, h = 0;
        foreach (var f in files)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
            t.LoadImage(File.ReadAllBytes(f));
            tex.Add(t);
            w += t.width + 8;
            h = Mathf.Max(h, t.height);
        }
        var strip = new Texture2D(w, h, TextureFormat.RGB24, false);
        var fill = new Color32[w * h];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 28, 255);
        strip.SetPixels32(fill);
        int x = 0;
        foreach (var t in tex)
        {
            strip.SetPixels(x, 0, t.width, t.height, t.GetPixels());
            x += t.width + 8;
            UnityEngine.Object.DestroyImmediate(t);
        }
        strip.Apply();
        File.WriteAllBytes(Path.Combine(dir, name), strip.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(strip);
    }
}
