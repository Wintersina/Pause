using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Feature: "when the boss enters, don't give all the specs in the intro
// title, the name is sufficient, and have the name fall apart and drop down
// and past the user."
//
// Drives BossIntroUI.Step(dt) frame by frame in edit mode at timeScale 0 (the
// intro freezes the world), on every screen shape TallScreenTest covers.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossIntroTest.Run
public static class BossIntroTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[INTRO] PASS  " : "[INTRO] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const float Dt = 1f / 60f;
    static readonly Vector3[] corners = new Vector3[4];

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            TimingIsInBounds();
            foreach (var boss in BossCatalog.All) NameOnly(boss);
            foreach (var s in TallScreenTest.Screens)
                foreach (var boss in BossCatalog.All) Crumbles(boss, s.name, s.w, s.h);
            PurelyVisual();
            NoAllocationsPerFrame();
        }
        finally
        {
            Time.timeScale = 1f;
        }
        Debug.Log("[INTRO] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static Camera FreshScene(int w = 1080, int h = 1920)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, w, h);
        cam.aspect = (float)w / h;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        Time.timeScale = 0f; // the intro runs with the world frozen
        return cam;
    }

    static void RunTo(BossIntroUI ui, float clock)
    {
        for (int i = 0; i < 100000 && ui.Clock < clock; i++) ui.Step(Dt);
    }

    static void Close(BossIntroUI ui)
    {
        if (ui != null) ui.Close();
    }

    // ---- tests ---------------------------------------------------------

    static void TimingIsInBounds()
    {
        float settled = BossConfig.NameCardAt + 3f * BossArt.Tick;
        float hold = BossConfig.NameBreakAt - settled;
        Check("name holds readable for 0.6-0.8s before it breaks (" + hold.ToString("F2") + "s)",
              hold >= .6f - .03f && hold <= .8f);
        Check("the fight still starts at the same moment (IntroSeconds 2.2)",
              Mathf.Approximately(BossConfig.IntroSeconds, 2.2f));
        float over = BossIntroUI.FinishedAt - BossConfig.IntroSeconds;
        Check("the fall ends within 0.5s of the intro window (" + BossIntroUI.FinishedAt.ToString("F2") + "s, +" +
              over.ToString("F2") + ")", over <= .5f);
        Check("the name breaks after the card is in and the boss has landed",
              BossConfig.NameBreakAt > settled &&
              BossConfig.NameBreakAt > BossConfig.BossArriveAt + BossConfig.BossArriveSeconds);
        Check("the crumble is staggered (" + BossConfig.NameStaggerSeconds.ToString("F2") + "s)",
              BossConfig.NameStaggerSeconds >= .1f);
    }

    static void NameOnly(BossDef boss)
    {
        FreshScene();
        var ui = BossIntroUI.Play(boss);
        try
        {
            string expect = boss.name.Replace(" ", "");
            Check(boss.artKey + ": the intro shows the name \"" + boss.name + "\" (" + ui.ShownName + ")",
                  ui.ShownName == expect);
            Check(boss.artKey + ": one letter piece per letter (" + ui.LetterCount + ")", ui.LetterCount == expect.Length);
            // Every piece of text anywhere in the intro, overlay and world.
            var all = new System.Text.StringBuilder();
            foreach (var t in ui.GetComponentsInChildren<Text>(true)) all.Append(t.text);
            foreach (var t in ui.WorldRoot.GetComponentsInChildren<Text>(true)) all.Append(t.text);
            string shown = all.ToString();
            Check(boss.artKey + ": all the text in the intro is the name, letter by letter (" + shown + ")", shown == expect);
            Check(boss.artKey + ": no WORLD / // / class spec text",
                  !shown.Contains("WORLD") && !shown.Contains("//") && !shown.Contains(boss.title.Replace(" ", "")));
            var card = BossArt.Card(boss);
            bool usesCard = false;
            foreach (var img in ui.GetComponentsInChildren<Image>(true)) usesCard |= card != null && img.sprite == card;
            foreach (var img in ui.WorldRoot.GetComponentsInChildren<Image>(true)) usesCard |= card != null && img.sprite == card;
            Check(boss.artKey + ": the baked spec card art isn't shown", !usesCard);

            RunTo(ui, BossConfig.NameCardAt + 3f * BossArt.Tick + .01f);
            bool on = true;
            for (int i = 0; i < ui.LetterCount; i++) on &= ui.PieceVisible(i);
            Check(boss.artKey + ": every letter is up once the card lands", on && !ui.Cracked);
        }
        finally { Close(ui); }
    }

    static void Crumbles(BossDef boss, string screen, int w, int h)
    {
        var cam = FreshScene(w, h);
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        string who = boss.artKey + " " + screen;
        var ui = BossIntroUI.Play(boss);
        try
        {
            // Fully readable right before the break: every letter on screen,
            // unrotated, at its place on the plate, inside the plate.
            RunTo(ui, BossConfig.NameBreakAt - .02f);
            bool inView = true, still = true;
            var home = new Vector3[ui.LetterCount];
            for (int i = 0; i < ui.LetterCount; i++)
            {
                ui.PieceAt(i).GetWorldCorners(corners);
                foreach (var c in corners)
                    inView &= c.x >= -halfW && c.x <= halfW && c.y >= -halfH && c.y <= halfH;
                inView &= ui.PieceVisible(i);
                still &= Quaternion.Angle(ui.PieceAt(i).rotation, Quaternion.identity) < .01f;
                home[i] = ui.PieceAt(i).position;
            }
            Check(who + ": the whole name fits on screen before it breaks", inView && ui.LetterCount > 0);
            Check(who + ": ... and is held still (readable)", still && !ui.Cracked);
            var text0 = ui.PieceAt(0).GetComponent<Text>();
            float em = text0.fontSize * ui.PieceAt(0).lossyScale.y;
            Check(who + ": letters are a readable size (" + em.ToString("F2") + " u em)", em >= .25f);

            RunTo(ui, BossConfig.NameBreakAt + .01f);
            Check(who + ": it cracks at NameBreakAt", ui.Cracked);

            // Staggered: shortly after the fall starts some letters are on
            // their way and some still hang on.
            RunTo(ui, BossConfig.NameFallAt + BossConfig.NameStaggerSeconds * .4f);
            int moved = 0;
            for (int i = 0; i < ui.LetterCount; i++)
                if ((ui.PieceAt(i).position - home[i]).sqrMagnitude > 1e-4f) moved++;
            Check(who + ": the letters go one after another (" + moved + "/" + ui.LetterCount + " falling)",
                  moved > 0 && moved < ui.LetterCount);

            // Each letter is its own piece, kicked, spun and dropped.
            RunTo(ui, BossConfig.NameFallAt + BossConfig.NameStaggerSeconds + .25f);
            bool fell = true, spun = true;
            var seen = new System.Collections.Generic.HashSet<Transform>();
            for (int i = 0; i < ui.LetterCount; i++)
            {
                fell &= ui.PieceAt(i).position.y < home[i].y - .1f;
                spun &= Quaternion.Angle(ui.PieceAt(i).rotation, Quaternion.identity) > 5f;
                seen.Add(ui.PieceAt(i));
            }
            Check(who + ": every letter breaks off as its own piece", seen.Count == ui.LetterCount);
            Check(who + ": ... and drops and tumbles", fell && spun);

            // Past the ship: every piece ends below the camera's bottom edge.
            RunTo(ui, BossIntroUI.FinishedAt + .02f);
            float bottom = CameraFit.ViewBottom;
            bool below = true;
            for (int i = 0; i < ui.PieceCount; i++)
                below &= ui.PieceAt(i).position.y + ui.PieceRadius(i) < bottom;
            Check(who + ": every piece ends below the bottom edge by " + BossIntroUI.FinishedAt.ToString("F2") + "s",
                  below && ui.AllGone);
        }
        finally { Close(ui); }
    }

    static void PurelyVisual()
    {
        FreshScene();
        var ui = BossIntroUI.Play(BossCatalog.ForWorld(0));
        try
        {
            RunTo(ui, BossConfig.IntroSeconds);
            var root = ui.WorldRoot;
            Check("pieces have no colliders or bodies",
                  root.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                  root.GetComponentsInChildren<Collider>(true).Length == 0 &&
                  root.GetComponentsInChildren<Rigidbody2D>(true).Length == 0);
            bool rays = false;
            foreach (var g in ui.GetComponentsInChildren<Graphic>(true)) rays |= g.raycastTarget;
            foreach (var g in root.GetComponentsInChildren<Graphic>(true)) rays |= g.raycastTarget;
            Check("nothing in the intro is a raycast target, and it has no raycaster",
                  !rays && ui.GetComponent<GraphicRaycaster>() == null && root.GetComponent<GraphicRaycaster>() == null);
            Check("pieces are mid-fall as the fight starts (purely visual overlap)", !ui.AllGone);
            string src = File.ReadAllText("Assets/Scripts/Bosses/BossIntroUI.cs");
            Check("intro never reads the spec subtitle", !src.Contains(".title"));
            Check("intro steps on unscaled time",
                  src.Contains("Time.unscaledDeltaTime") && !src.Contains("Time.deltaTime") && !src.Contains("Time.time"));
        }
        finally { Close(ui); }
    }

    static void NoAllocationsPerFrame()
    {
        FreshScene(1080, 2880);
        var boss = BossCatalog.ForWorld(1);
        // Warm-up run (first-use caches), then measure a full second run.
        var warm = BossIntroUI.Play(boss);
        RunTo(warm, BossIntroUI.FinishedAt + .1f);
        Close(warm);
        var ui = BossIntroUI.Play(boss);
        try
        {
            ui.Step(Dt);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int frames = 0;
            for (; frames < 100000 && ui.Clock < BossIntroUI.FinishedAt + .1f; frames++) ui.Step(Dt);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("no allocations per frame (" + bytes + " bytes over " + frames + " frames)", bytes == 0);
        }
        finally { Close(ui); }
    }
}
