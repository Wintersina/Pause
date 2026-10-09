using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The talking-robot tutorial: the script table stays short and every step can
// actually be completed in tutorialS5, the speech/bubble/panel animation never
// reads scaled time (the tutorial world sits at timeScale 0 whenever the
// player lifts their finger), Skip and completion both mark the tutorial done,
// the bubble stays inside the safe area on every common phone shape, and the
// end card is a proper Flight Complete-style panel wired to the scene's own
// buttons.
public static class TutorialRobotTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TR] PASS  " : "[TR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        ScriptTable();
        Syllables();
        AdvanceConditionsAreReachable();
        WholeTutorialCanBeFinished();
        SpeechUsesUnscaledTime();
        BubbleStaysInSafeArea();
        LinesFitTheBubble();
        SkipFinishesTheTutorial();
        CompletionMarksTutorialDone();
        WeaponIsHeldThenFiresOnce();
        PauseJumpOpensThePortal();
        SpeakerTapCompletesLine();
        CompletePanel();
        ArtIsThePixelKit();
        TutorialBackdropIsSpace();

        Debug.Log("[TR] failures: " + fails);
        return fails;
    }

    // ---- The script table ----

    static void ScriptTable()
    {
        var steps = TutorialScript.Steps;
        Check("script has at most " + TutorialScript.MaxSteps + " steps (" + steps.Length + ")",
              steps.Length > 0 && steps.Length <= TutorialScript.MaxSteps);
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var s in steps)
        {
            string plain = TutorialScript.ToPlainText(s.line);
            Check("\"" + s.id + "\" line is <= " + TutorialScript.MaxLineLength + " characters (" + plain.Length + ")",
                  plain.Length <= TutorialScript.MaxLineLength);
            int words = plain.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            Check("\"" + s.id + "\" line is <= 8 words (" + words + ")", words <= 8);
            Check("\"" + s.id + "\" line is plain ASCII (the bubble font has no fancy glyphs)",
                  Regex.IsMatch(s.line, @"^[\x20-\x7E]+$"));
            Check("\"" + s.id + "\" highlight markers are balanced", s.line.Split('*').Length % 2 == 1);
            Check("\"" + s.id + "\" id is unique", ids.Add(s.id));
            Check("\"" + s.id + "\" asks for a positive amount", s.amount > 0f);
        }

        // The order the brief asks for.
        Check("teaches flying first", steps[0].advance == TutorialAdvance.FlySeconds);
        Check("then letting go to freeze", steps[1].advance == TutorialAdvance.LetGo);
        int pausesAt = IndexOf(TutorialAdvance.SpendPause), dustAt = IndexOf(TutorialAdvance.CollectStar),
            atomAt = IndexOf(TutorialAdvance.CollectRedAtom);
        Check("pauses are limited, before star dust, before the power-up",
              pausesAt > 1 && dustAt > pausesAt && atomAt > dustAt);
        int enemyAt = System.Array.FindIndex(steps, s => s.id == "enemies");
        Check("enemies are covered after the pickups", enemyAt > dustAt);
        Check("the enemy step sends one alien (SpawnEnemy) and waits until it is gone",
              enemyAt >= 0 && steps[enemyAt].cue == TutorialCue.SpawnEnemy
              && steps[enemyAt].advance == TutorialAdvance.EnemyGone && steps[enemyAt].amount == 1f);
        int powerAt = IndexOf(TutorialAdvance.FirePower);
        Check("a power step comes after the atoms are taught", powerAt > atomAt
              && powerAt > IndexOf(TutorialAdvance.CollectGreenAtom) && powerAt > IndexOf(TutorialAdvance.CollectBlueAtom));
        Check("it keeps charge atoms coming and waits for the weapon to go off once",
              powerAt >= 0 && steps[powerAt].cue == TutorialCue.SpawnChargeAtoms && steps[powerAt].amount == 1f);
        Check("its charge takes a few atoms (" + TutorialScript.PowerAtoms + ")",
              TutorialScript.PowerAtoms >= 2 && TutorialScript.PowerAtoms <= 4);
    }

    static int IndexOf(TutorialAdvance a)
    {
        return System.Array.FindIndex(TutorialScript.Steps, s => s.advance == a);
    }

    static void Syllables()
    {
        foreach (var s in TutorialScript.Steps)
        {
            var spoken = TutorialScript.Speak(s.line);
            bool monotonic = spoken.SyllableCount > 0;
            for (int i = 1; i < spoken.SyllableCount; i++)
                monotonic &= spoken.syllableEnds[i] > spoken.syllableEnds[i - 1]
                             && spoken.glyphsVisible[i] >= spoken.glyphsVisible[i - 1];
            Check("\"" + s.id + "\" splits into ordered syllables (" + spoken.SyllableCount + ")", monotonic);
            Check("\"" + s.id + "\" last syllable reveals the whole line",
                  spoken.syllableEnds[spoken.SyllableCount - 1] == spoken.plainText.Length
                  && spoken.glyphsVisible[spoken.SyllableCount - 1] == spoken.totalGlyphs);
            int words = spoken.plainText.Split(' ').Length;
            Check("\"" + s.id + "\" has at least a beat per word", spoken.SyllableCount >= words);
            Check("\"" + s.id + "\" has some stressed beats", System.Array.IndexOf(spoken.stressed, true) >= 0);
        }

        var time = TutorialScript.Speak("Let go. Time *freezes*!");
        Check("silent final e is not its own beat (\"Time\" is one syllable)",
              time.SyllableCount == 5);   // Let | go. | Time | free | zes!
        Check("highlight becomes gold rich text", time.richText.Contains("<color=" + TutorialScript.HighlightColor + ">freezes</color>"));
    }

    // ---- Every advance condition can be met, in tutorialS5 ----

    static void AdvanceConditionsAreReachable()
    {
        foreach (var s in TutorialScript.Steps)
        {
            var start = new TutorialSignals { worldMoving = true, pressed = true };
            Check("\"" + s.id + "\" is not already met when it starts", !TutorialScript.IsMet(s, start, start));
            var after = Perform(s, start);
            Check("\"" + s.id + "\" is met once the player " + s.advance + " x" + s.amount,
                  TutorialScript.IsMet(s, start, after));
        }

        // What the actions need from the scene.
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var spawner = Object.FindFirstObjectByType<spawnGoodStuffTut>();
        Check("tutorialS5 has the pickup spawner", spawner != null);
        if (spawner != null)
        {
            Check("its star is a collectable star prefab",
                  spawner.smStar != null && PrefabName.Is(spawner.smStar, "smStar1"));
            Check("its mid star is a collectable star prefab",
                  spawner.midStar != null && PrefabName.Is(spawner.midStar, "LargeStar1"));
            Check("its red atom is the pause atom collisionDetection credits",
                  spawner.redAtom != null && PrefabName.Is(spawner.redAtom, "pauseAtom"));
        }
        var ship = Object.FindFirstObjectByType<movePlayerInTut>();
        Check("the tutorial ship collects pickups (collisionDetection)",
              ship != null && ship.GetComponent<collisionDetection>() != null);
        Check("the tutorial has a Hints director", Object.FindFirstObjectByType<Hints>() != null);

        int pauseSteps = 0;
        foreach (var s in TutorialScript.Steps)
            if (s.advance == TutorialAdvance.LetGo || s.advance == TutorialAdvance.Touch || s.advance == TutorialAdvance.SpendPause)
                pauseSteps++;
        Check("50 tutorial pauses are plenty for the freeze steps (" + pauseSteps + ")", pauseSteps * 3 < 50);

        // Pickups the steps count are the ones the scene produces.
        int before = score.dustPickups;
        score.paysRealDust = false;
        score.AwardStarDust(.5f);
        Check("a star pickup is counted for the star step", score.dustPickups == before + 1);

        int stars = IndexOf(TutorialAdvance.CollectStar);
        Check("the star step starts the stars", TutorialScript.Steps[stars].cue == TutorialCue.SpawnStars);
        int atom = IndexOf(TutorialAdvance.CollectRedAtom);
        Check("the red-atom step keeps a red atom coming", TutorialScript.Steps[atom].cue == TutorialCue.SpawnRedAtom);
        EveryAtomIsTaught(spawner);
    }

    // Star dust, the green heal atom, the blue shield atom and the red pause
    // atom each get a step that spawns that pickup and waits for the player
    // to catch it, and the scene can actually produce each one.
    static void EveryAtomIsTaught(spawnGoodStuffTut spawner)
    {
        var kinds = new[]
        {
            (TutorialAdvance.CollectGreenAtom, TutorialCue.SpawnGreenAtom, TutorialAtom.Green, "green heal atom"),
            (TutorialAdvance.CollectBlueAtom, TutorialCue.SpawnBlueAtom, TutorialAtom.Blue, "blue shield atom"),
            (TutorialAdvance.CollectRedAtom, TutorialCue.SpawnRedAtom, TutorialAtom.Red, "red pause atom"),
        };
        foreach (var k in kinds)
        {
            int i = IndexOf(k.Item1);
            Check("the " + k.Item4 + " has its own step", i >= 0);
            if (i < 0) continue;
            Check("the " + k.Item4 + " spawns the moment its line starts", TutorialScript.Steps[i].cue == k.Item2);
            Check("that cue asks the spawner for the " + k.Item4, Hints.AtomFor(k.Item2) == k.Item3);
        }
        Check("star dust has its own step", IndexOf(TutorialAdvance.CollectStar) >= 0);

        if (spawner == null) return;
        Check("the blue atom prefab is the shield atom collisionDetection credits",
              spawner.Atom != null && PrefabName.Is(spawner.Atom, "atom3a"));
        var spawn = typeof(spawnGoodStuffTut).GetMethod("spawnAtom", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var k in kinds)
        {
            spawn.Invoke(spawner, new object[] { k.Item3 });
            var live = spawnGoodStuffTut.LiveAtom;
            string expected = k.Item3 == TutorialAtom.Green ? HealAtom.ObjectName : k.Item3 == TutorialAtom.Blue ? "atom3a" : "pauseAtom";
            Check("the tutorial spawns a real " + k.Item4 + " (" + (live != null ? live.name : "nothing") + ")",
                  live != null && PrefabName.Is(live.gameObject, expected));
            if (live != null) Object.DestroyImmediate(live.gameObject);
        }

        string pickups = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        foreach (var counter in new[] { "healAtomPickups++", "shieldAtomPickups++", "pauseAtomPickups++" })
            Check("collisionDetection counts the pickup (" + counter + ")", pickups.Contains(counter));
    }

    // The canonical player action for each kind of step.
    static TutorialSignals Perform(TutorialStep s, TutorialSignals now)
    {
        switch (s.advance)
        {
            case TutorialAdvance.FlySeconds:
                now.flySeconds += s.amount + .01f; break;
            case TutorialAdvance.LetGo:
                now.pressed = false; now.worldMoving = false; now.frozenSeconds = s.amount + .01f; break;
            case TutorialAdvance.Touch:
                now.presses += Mathf.CeilToInt(s.amount); now.pressed = true; now.worldMoving = true; now.frozenSeconds = 0f; break;
            case TutorialAdvance.SpendPause:
                now.pausesSpent += Mathf.CeilToInt(s.amount); break;
            case TutorialAdvance.CollectStar:
                now.starsCollected += Mathf.CeilToInt(s.amount); break;
            case TutorialAdvance.CollectGreenAtom:
                now.greenAtomsCollected += Mathf.CeilToInt(s.amount); break;
            case TutorialAdvance.CollectBlueAtom:
                now.blueAtomsCollected += Mathf.CeilToInt(s.amount); break;
            case TutorialAdvance.CollectRedAtom:
                now.redAtomsCollected += Mathf.CeilToInt(s.amount); break;
            case TutorialAdvance.EnemyGone:
                now.enemiesGone += Mathf.CeilToInt(s.amount); break;
            case TutorialAdvance.FirePower:
                now.powersFired += Mathf.CeilToInt(s.amount); break;
        }
        return now;
    }

    static void WholeTutorialCanBeFinished()
    {
        // One running set of signals through every step, the way Hints keeps it.
        var signals = new TutorialSignals { worldMoving = false };
        int completed = 0;
        foreach (var s in TutorialScript.Steps)
        {
            var start = signals;
            signals = Perform(s, signals);
            if (TutorialScript.IsMet(s, start, signals)) completed++;
        }
        Check("a straight playthrough completes all " + TutorialScript.Steps.Length + " steps",
              completed == TutorialScript.Steps.Length);
    }

    // ---- Unscaled time only ----

    static void SpeechUsesUnscaledTime()
    {
        var scaled = new Regex(@"Time\.(time|deltaTime|fixedDeltaTime|smoothDeltaTime)\b|WaitForSeconds\(");
        foreach (var file in new[] { "RobotSpeaker.cs", "SpeechRevealEffect.cs", "TutorialGuides.cs",
                                     "TutorialCompletePanel.cs", "Hints.cs", "TutorialSkip.cs" })
        {
            string src = File.ReadAllText("Assets/Scripts/Tutorial/" + file);
            Check(file + " never reads scaled time", !scaled.IsMatch(src));
        }
        Check("RobotSpeaker animates on Time.unscaledTime",
              File.ReadAllText("Assets/Scripts/Tutorial/RobotSpeaker.cs").Contains("Time.unscaledTime"));
        Check("SpeechRevealEffect pops on Time.unscaledTime",
              File.ReadAllText("Assets/Scripts/Tutorial/SpeechRevealEffect.cs").Contains("Time.unscaledTime"));

        // No per-frame allocation: RobotSpeaker.Update allocates nothing once warm.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var speaker = RobotSpeaker.Create(null);
        speaker.Say(TutorialScript.Speak(TutorialScript.Steps[3].line));
        var update = typeof(RobotSpeaker).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var guides = TutorialGuides.Create(speaker.Root);
        var guidesUpdate = typeof(TutorialGuides).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        guides.ShowTouch(true);
        object[] none = new object[0];
        for (int i = 0; i < 5; i++) { update.Invoke(speaker, none); guidesUpdate.Invoke(guides, none); }
        System.GC.Collect();
        long before = Allocated();
        for (int i = 0; i < 120; i++) { update.Invoke(speaker, none); guidesUpdate.Invoke(guides, none); }
        long allocated = Allocated() - before;
        // Reflection's own Invoke allocates a little per call; anything beyond
        // that comes from the Update bodies.
        long baseline = MeasureEmptyInvokes(240);
        long probe = Allocated();
        var garbage = new byte[4096];
        Check("the allocation counter actually counts (" + garbage.Length + " byte probe)",
              Allocated() - probe >= 4096);
        Check("speaker + guides Update allocate nothing per frame (" + (allocated - baseline) + " bytes over 120 frames)",
              allocated - baseline <= 0);
        Object.DestroyImmediate(speaker.gameObject);
    }

    // Unity's Mono does not implement GC.GetAllocatedBytesForCurrentThread
    // (it reads 0), so measure managed heap growth instead; the probe below
    // proves the measurement sees allocations at all.
    static long Allocated() { return System.GC.GetTotalMemory(false); }

    class Empty { void Update() { } }
    static long MeasureEmptyInvokes(int n)
    {
        var target = new Empty();
        var m = typeof(Empty).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        object[] none = new object[0];
        for (int i = 0; i < 5; i++) m.Invoke(target, none);
        System.GC.Collect();
        long before = Allocated();
        for (int i = 0; i < n; i++) m.Invoke(target, none);
        return Allocated() - before;
    }

    // ---- Safe area at many shapes ----

    struct Device
    {
        public string name; public int w, h; public float top, bottom, left, right;
        public Device(string n, int w, int h, float top = 0, float bottom = 0, float left = 0, float right = 0)
        { name = n; this.w = w; this.h = h; this.top = top; this.bottom = bottom; this.left = left; this.right = right; }
    }

    static readonly Device[] Devices =
    {
        new Device("16:9 1080x1920", 1080, 1920),
        new Device("19.5:9 notch 1170x2532", 1170, 2532, 141, 102),
        new Device("20:9 punch-hole 1080x2400", 1080, 2400, 80),
        new Device("21:9 tall 1080x2520", 1080, 2520, 96, 48),
        new Device("9:22 tall 1080x2640", 1080, 2640, 96, 48),
        new Device("9:24 tall 1080x2880", 1080, 2880, 96, 48),
        new Device("Z Fold cover 968x2376", 968, 2376, 90, 40),
        new Device("4:3 tablet 1536x2048", 1536, 2048, 40, 40),
        new Device("3:2 1200x1800", 1200, 1800),
        new Device("small 640x1136", 640, 1136),
        new Device("very narrow 600x1600", 600, 1600, 60),
        new Device("Mac window 540x960", 540, 960),
    };

    static void BubbleStaysInSafeArea()
    {
        foreach (var d in Devices)
        {
            float sf = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(d.w / RobotSpeaker.ReferenceResolution.x, 2f),
                                                 Mathf.Log(d.h / RobotSpeaker.ReferenceResolution.y, 2f),
                                                 RobotSpeaker.MatchWidthOrHeight));
            float cw = d.w / sf, ch = d.h / sf;
            var safe = new Rect(d.left / sf - cw * .5f, d.bottom / sf - ch * .5f,
                                (d.w - d.left - d.right) / sf, (d.h - d.top - d.bottom) / sf);
            foreach (float blocked in new[] { 0f, 180f, 260f })
            {
                Rect robot, bubble; float scale;
                RobotSpeaker.ComputeLayout(safe, blocked, out robot, out bubble, out scale);
                string tag = d.name + ", top " + blocked + ": ";
                float over = RobotSpeaker.MaxAnimatedOverhang;
                Check(tag + "robot (with hover) inside the safe area", Contains(safe, Grow(robot, over)));
                Check(tag + "bubble (with glow and pop) inside the safe area",
                      Contains(safe, Grow(bubble, RobotSpeaker.BubbleMargin * scale + over)));
                Check(tag + "tail tip inside the safe area", bubble.xMin - RobotSpeaker.TailReach * scale >= safe.xMin);
                Check(tag + "bubble clears the robot", bubble.xMin >= robot.xMax);
                Check(tag + "bubble is at least MinBubbleWidth (scaled)",
                      bubble.width >= RobotSpeaker.MinBubbleWidth * scale - .01f);
                Check(tag + "row sits below the blocked band",
                      Mathf.Max(robot.yMax, bubble.yMax + RobotSpeaker.BubbleMargin * scale) <= safe.yMax - blocked + .01f
                      || blocked + 200f > safe.height);
                Check(tag + "not shrunk below readable (" + scale.ToString("F2") + ")", scale >= .85f);
            }
        }
    }

    static Rect Grow(Rect r, float by) { return new Rect(r.xMin - by, r.yMin - by, r.width + 2f * by, r.height + 2f * by); }
    static bool Contains(Rect outer, Rect inner)
    {
        const float e = .01f;
        return inner.xMin >= outer.xMin - e && inner.xMax <= outer.xMax + e && inner.yMin >= outer.yMin - e && inner.yMax <= outer.yMax + e;
    }

    // Every line fits the narrowest bubble at a readable size, and UI Text
    // emits exactly one quad per visible glyph (what the reveal relies on).
    static void LinesFitTheBubble()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/Orbitron/Orbitron-Bold.ttf");
        Check("Orbitron Bold is where the scenes expect it", font != null);
        if (font == null) return;

        var extents = new Vector2(RobotSpeaker.MinBubbleWidth - 2f * RobotSpeaker.TextPadX,
                                  RobotSpeaker.BubbleHeight - 2f * RobotSpeaker.TextPadY);
        foreach (var s in TutorialScript.Steps)
        {
            var spoken = TutorialScript.Speak(s.line);
            var gen = new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = RobotSpeaker.FontMax,
                fontStyle = FontStyle.Bold,
                lineSpacing = 1.05f,
                richText = true,
                scaleFactor = 1f,
                color = Color.white,
                textAnchor = TextAnchor.MiddleLeft,
                resizeTextForBestFit = true,
                resizeTextMinSize = RobotSpeaker.FontMin,
                resizeTextMaxSize = RobotSpeaker.FontMax,
                updateBounds = true,
                verticalOverflow = VerticalWrapMode.Truncate,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                generationExtents = extents,
                pivot = new Vector2(.5f, .5f),
                generateOutOfBounds = false,
            };
            gen.Populate(spoken.richText, settings);
            int size = gen.fontSizeUsedForBestFit;
            Check("\"" + s.id + "\" fits the narrowest bubble at " + size + "pt (min " + RobotSpeaker.FontMin + ")",
                  size >= RobotSpeaker.FontMin + 2 && gen.lineCount <= 3);
            Check("\"" + s.id + "\" text mesh has one quad per visible glyph (" + gen.vertexCount / 4 + " vs " + spoken.totalGlyphs + ")",
                  gen.vertexCount / 4 == spoken.totalGlyphs);
        }
    }

    // ---- Skip and completion both mark the tutorial done ----

    static void SkipFinishesTheTutorial()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "false");
        startMenu.youAreInTutorial = true;
        Hints.reachedTheEndOfTut = false;
        Time.timeScale = 0f;

        var host = new GameObject("~SkipTest");
        var skip = host.AddComponent<TutorialSkip>();
        skip.SendMessage("Start");
        Check("skip button was built", skip.Button != null);
        if (skip.Button != null)
        {
            try { skip.Button.onClick.Invoke(); }
            catch (System.Exception) { /* LoadScene refuses edit mode; state is set before it */ }
        }
        Check("tapping Skip sets HasDoneTut", PlayerPrefs.GetString("HasDoneTut") == "true");
        Check("and leaves tutorial mode", !startMenu.youAreInTutorial);
        Check("and unfreezes time for the next scene", Mathf.Approximately(Time.timeScale, 1f));
        Check("and stops the tutorial", Hints.reachedTheEndOfTut);

        PlayerPrefs.SetString("HasDoneTut", "false");
        string next = TutorialSkip.FinishBySkipping();
        Check("Skip goes straight into the game (" + next + ")", next == "gameS1");
        bool inBuild = false;
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && s.path.EndsWith("/" + next + ".unity")) inBuild = true;
        Check("and that scene is in the build", inBuild);
        Check("Skip still sets HasDoneTut when called directly", PlayerPrefs.GetString("HasDoneTut") == "true");

        Check("skip sits below the quick-action row (top " + skip.topMargin + ")",
              skip.topMargin >= 18f + PauseQuickActions.ButtonSize + 8f);
        Object.DestroyImmediate(host);
    }

    static void CompletionMarksTutorialDone()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "false");
        var hints = Object.FindFirstObjectByType<Hints>();
        if (hints == null) { Check("tutorialS5 has Hints", false); return; }
        hints.SendMessage("Start");

        var begin = typeof(Hints).GetMethod("BeginStep", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < TutorialScript.Steps.Length; i++) begin.Invoke(hints, new object[] { i });
        Check("HasDoneTut is not set while steps are still running", PlayerPrefs.GetString("HasDoneTut") != "true");
        Check("the old typed text box is hidden", hints.startHintText == null || !hints.startHintText.gameObject.activeSelf);
        Check("the old still robot sprite is hidden", hints.playerIcon == null || !hints.playerIcon.activeSelf);

        typeof(Hints).GetMethod("BeginEnding", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hints, null);
        Check("finishing the last step sets HasDoneTut", PlayerPrefs.GetString("HasDoneTut") == "true");
        Check("and marks the end of the tutorial", Hints.reachedTheEndOfTut);
        Check("and no atom keeps coming", spawnGoodStuffTut.keepAtomComing == TutorialAtom.None);
        Check("and no tutorial alien is left flying", TutorialEnemy.Live == null);
        var heldPower = Object.FindFirstObjectByType<movePlayerInTut>()?.GetComponent<ShipPowerController>();
        Check("and the weapon is held again", heldPower != null && heldPower.chargeMode == ShipPowerController.ChargeMode.Held);

        foreach (var s in Object.FindObjectsByType<RobotSpeaker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(s.gameObject);
    }

    // ---- The power step: the weapon is held, then armed for a few atoms ----

    static void WeaponIsHeldThenFiresOnce()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        buttonClicks.playerDied = false;
        var hints = Object.FindFirstObjectByType<Hints>();
        var ship = Object.FindFirstObjectByType<movePlayerInTut>();
        if (hints == null || ship == null) { Check("tutorialS5 has Hints and the ship", false); return; }
        hints.SendMessage("Start");
        var power = ship.GetComponent<ShipPowerController>();
        Check("the tutorial ship carries the real weapon (ShipPowerController)", power != null);
        if (power == null) return;
        power.SendMessage("Awake");
        power.SendMessage("Start");
        var gun = ship.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");   // edit mode: Awake doesn't run on its own
        score.pauseCounter = 0;   // the world runs with no finger down
        try
        {
            Check("it is held from the start", power.chargeMode == ShipPowerController.ChargeMode.Held);
            float left = power.SecondsLeft;
            for (int i = 0; i < 600; i++) power.SendMessage("Update");
            power.ReduceTimer(1000f);
            power.FreeShot();
            Check("held: no countdown, pickups don't cut it, no free shot, never fires",
                  Mathf.Approximately(power.SecondsLeft, left) && power.UltimatesFired == 0 && power.FreeShotsFired == 0
                  && power.PendingFreeShots == 0);

            int powerAt = System.Array.FindIndex(TutorialScript.Steps, s => s.advance == TutorialAdvance.FirePower);
            typeof(Hints).GetMethod("BeginStep", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hints, new object[] { powerAt });
            Check("the power step arms it: only pickups fill it",
                  power.chargeMode == ShipPowerController.ChargeMode.PickupsOnly && power.Charge01 < .01f);
            Check("the power step keeps charge atoms coming", spawnGoodStuffTut.keepAtomComing == TutorialAtom.Charge);
            for (int i = 0; i < 600; i++) power.SendMessage("Update");
            Check("armed: time alone never fires it", power.UltimatesFired == 0);

            for (int i = 0; i < TutorialScript.PowerAtoms; i++)
            {
                Check("atom " + (i + 1) + " of " + TutorialScript.PowerAtoms + ": not fired yet", power.UltimatesFired == 0);
                power.ReduceTimer(power.secondsPerAtom);
                power.SendMessage("Update");
            }
            Check("after exactly " + TutorialScript.PowerAtoms + " atoms the weapon goes off once (" + power.UltimatesFired + ")",
                  power.UltimatesFired == 1);
        }
        finally
        {
            spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
            power.SendMessage("OnDestroy");
            if (power.Runner != null) power.Runner.SendMessage("OnDestroy");
            if (power.Secret != null) power.Secret.SendMessage("OnDestroy");
            AttackPool.StopAll();
            typeof(ShipPowerController).GetMethod("FinishCinematic", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            score.pauseCounter = 50;
        }
        foreach (var s in Object.FindObjectsByType<RobotSpeaker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(s.gameObject);
    }

    // ---- The pause-jump shows the portal, as in a run ----

    static void PauseJumpOpensThePortal()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int Vortices() => System.Array.FindAll(Object.FindObjectsByType<Transform>(FindObjectsSortMode.None),
                                               t => t.name == "~TeleportVortex").Length;
        int before = Vortices();
        bool nudge = movePlayerInTut.Arrive(Vector3.zero, new Vector3(TeleportFx.MinimumJump * .5f, 0f, 0f));
        Check("a nudge is steering, not a jump: no portal", !nudge && Vortices() == before);
        bool jump = movePlayerInTut.Arrive(new Vector3(-1.5f, -3f, 0f), new Vector3(1.5f, 1f, 0f));
        Check("a pause-jump opens the portal where it left and where it lands (" + (Vortices() - before) + ")",
              jump && Vortices() - before == 2);

        string src = File.ReadAllText("Assets/Scripts/Gameplay/movePlayerInTut.cs");
        Check("the tutorial ship's first frame of a press is the arrival (Arrive)",
              src.Contains("if (!held) Arrive(before, transform.position)"));
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.parent == null && (t.name == "~TeleportVortex" || t.name == "~TeleportFx")) Object.DestroyImmediate(t.gameObject);
    }

    static void SpeakerTapCompletesLine()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var speaker = RobotSpeaker.Create(null);
        var line = TutorialScript.Speak(TutorialScript.Steps[0].line);
        speaker.Say(line);
        Check("a new line starts out being spoken", speaker.Talking && !speaker.LineFinished);
        speaker.OnPointerDown(null);
        Check("a tap while it is spoken completes it at once", speaker.LineFinished && !speaker.Talking);
        var reveal = speaker.Text.GetComponent<SpeechRevealEffect>();
        Check("and every glyph is shown", reveal != null && reveal.Visible == line.totalGlyphs);
        Check("reveal runs before the outline (so the outline copies its alpha)",
              System.Array.IndexOf(speaker.Text.GetComponents<BaseMeshEffect>(), reveal) == 0);
        speaker.OnPointerDown(null);
        Check("a second tap does not skip the step (none advance by tap)", speaker.LineFinished);
        Check("the finishing beat snaps the mouth to its big frame", speaker.MouthFrame == 4);
        Object.DestroyImmediate(speaker.gameObject);
    }

    // ---- The end card ----

    static void CompletePanel()
    {
        var panel = TutorialCompletePanel.PanelRect;
        var inner = new Rect(panel.xMin + 16f, panel.yMin + 16f, panel.width - 32f, panel.height - 32f);
        var rows = new[] { TutorialCompletePanel.HeaderRect, TutorialCompletePanel.DividerRect,
                           TutorialCompletePanel.CardRects[0], TutorialCompletePanel.CardRects[1],
                           TutorialCompletePanel.FooterRect, TutorialCompletePanel.PlayRect, TutorialCompletePanel.MenuRect };
        bool insideAll = true, noOverlap = true;
        for (int i = 0; i < rows.Length; i++)
        {
            insideAll &= Contains(inner, rows[i]);
            for (int j = i + 1; j < rows.Length; j++) noOverlap &= !rows[i].Overlaps(rows[j]);
        }
        Check("end card: every row sits inside the panel with padding", insideAll);
        Check("end card: no rows overlap", noOverlap);
        Check("end card buttons match the Flight Complete buttons",
              Mathf.Approximately(TutorialCompletePanel.ButtonWidth, DeathPanelView.ButtonWidth)
              && Mathf.Approximately(TutorialCompletePanel.ButtonHeight, DeathPanelView.ButtonHeight));

        foreach (var d in Devices)
        {
            // PopUpCanvas: 800x600 reference, Expand.
            float sf = Mathf.Min(d.w / 800f, d.h / 600f);
            float cw = d.w / sf, ch = d.h / sf;
            var safe = new Rect(d.left / sf - cw * .5f, d.bottom / sf - ch * .5f,
                                (d.w - d.left - d.right) / sf, (d.h - d.top - d.bottom) / sf);
            Vector2 centre; float scale;
            TutorialCompletePanel.ComputeFit(safe, null, out centre, out scale);
            float w = (TutorialCompletePanel.Width + 2f * TutorialCompletePanel.FrameMargin) * 1.06f * scale;
            float h = (TutorialCompletePanel.Height + 2f * TutorialCompletePanel.FrameMargin) * 1.06f * scale;
            Check("end card fits the safe area on " + d.name,
                  Contains(safe, new Rect(centre.x - w * .5f, centre.y - h * .5f, w, h)));
        }

        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var view = TutorialCompletePanel.Show(3.5f, score.RealRunPauses);
        Check("end card builds in tutorialS5", view != null);
        if (view == null) return;
        view.Skip();

        var play = SceneUtil.FindAny(TutorialCompletePanel.PlayButtonName);
        var menu = SceneUtil.FindAny(TutorialCompletePanel.MenuButtonName);
        Check("PLAY is the scene's own button, moved into the card",
              play != null && play.transform.IsChildOf(view.PlaySlot));
        Check("MENU is the scene's own button, moved into the card",
              menu != null && menu.transform.IsChildOf(view.MenuSlot));
        Check("PLAY still calls tutButtonClicks.replay", Wired(play, "replay"));
        Check("MENU still calls tutButtonClicks.mainMenuButton", Wired(menu, "mainMenuButton"));
        Check("PLAY is visible and clickable", play != null && play.activeInHierarchy && play.GetComponent<Button>().interactable);
        var legacy = SceneUtil.FindAny(TutorialCompletePanel.LegacyDialogName);
        Check("the old \"End of tutorial\" dialog is switched off", legacy == null || !legacy.activeSelf);
        Check("intro settles", view.IntroFinished);
        Check("no leftover CONTINUE TO GAME overlay", GameObject.Find("TutorialFinishCanvas") == null);
    }

    // ---- Art direction: the low-res cyberpunk pixel kit ----

    // Every sprite of the kit and its size in pixels (the layout code places
    // parts in these units, so a redraw must keep them).
    static readonly (string name, int w, int h)[] Kit =
    {
        ("tut_robot", 256, 256), ("tut_bubble", 192, 192), ("tut_tail", 52, 40), ("tut_ring", 128, 128),
        ("tut_glow", 64, 64), ("tut_lamp", 24, 24), ("tut_jet_a", 60, 52), ("tut_jet_b", 60, 52),
        ("tut_eye_open", 52, 40), ("tut_eye_half", 52, 40), ("tut_eye_shut", 52, 40), ("tut_eye_happy", 52, 40),
        ("tut_mouth_rest", 72, 40), ("tut_mouth_a", 72, 40), ("tut_mouth_e", 72, 40), ("tut_mouth_o", 72, 40),
        ("tut_mouth_big", 72, 40), ("tut_card", 96, 96), ("tut_button", 96, 80), ("tut_arrow", 0, 0),
    };

    static float Luminance(Color c)
    {
        float Lin(float v) { return v <= .03928f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f); }
        return .2126f * Lin(c.r) + .7152f * Lin(c.g) + .0722f * Lin(c.b);
    }

    static float Contrast(Color a, Color b)
    {
        float la = Luminance(a), lb = Luminance(b);
        return (Mathf.Max(la, lb) + .05f) / (Mathf.Min(la, lb) + .05f);
    }

    // The PNG's pixels (the imported sprites are not CPU-readable).
    static Texture2D ReadPng(string resource)
    {
        string path = "Assets/Art/Resources/" + resource + ".png";
        if (!File.Exists(path)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return t.LoadImage(File.ReadAllBytes(path)) ? t : null;
    }

    static void ArtIsThePixelKit()
    {
        foreach (var k in Kit)
        {
            var sprite = Resources.Load<Sprite>("Tutorial/" + k.name);
            Check(k.name + " loads", sprite != null);
            if (sprite == null || k.w == 0) continue;
            Check(k.name + " is " + k.w + "x" + k.h + " (the layout's registration)",
                  Mathf.RoundToInt(sprite.rect.width) == k.w && Mathf.RoundToInt(sprite.rect.height) == k.h);
        }
        Check("the 20 kit sprites are all there (" + Kit.Length + ")", Kit.Length == 20);
        Check("the old SVG sources and palette.env are gone",
              !File.Exists("Assets/Art/UI/Tutorial/src~/palette.env") && !File.Exists("Assets/Art/UI/Tutorial/src~/render.sh"));
        Check("the staging folder is gone", !Directory.Exists("Assets/Art/Resources/Tutorial_new~"));

        // The bubble: a nine-slice whose border clears the panel's corner brackets, and a
        // text area inside the panel's navy fill.
        var bubble = Resources.Load<Sprite>("Tutorial/tut_bubble");
        var tex = ReadPng("Tutorial/tut_bubble");
        Check("the bubble is nine-sliced with a border inside half its size",
              bubble != null && bubble.border.x >= 24f && bubble.border.x * 2f < bubble.rect.width
              && bubble.border.y >= 24f && bubble.border.y * 2f < bubble.rect.height);
        if (tex != null)
        {
            Color fill = tex.GetPixel(tex.width / 2, tex.height / 2);
            Check("TutorialPalette.Panel is the bubble's fill", TutorialPalette.Html(fill) == TutorialPalette.Html(TutorialPalette.Panel));
        }
        // Text on the panel stays readable; the red emphasis stays red.
        Check("body text reads on the panel (" + Contrast(TutorialPalette.Paper, TutorialPalette.Panel).ToString("0.0") + ":1)",
              Contrast(TutorialPalette.Paper, TutorialPalette.Panel) >= 7f);
        Check("the amber highlight reads on the panel (" + Contrast(TutorialPalette.Orange, TutorialPalette.Panel).ToString("0.0") + ":1)",
              Contrast(TutorialPalette.Orange, TutorialPalette.Panel) >= 7f);
        Check("the bubble highlight is the palette's amber",
              TutorialScript.HighlightColor == TutorialPalette.Html(TutorialPalette.Orange));
        Check("highlight and body text are told apart", Contrast(TutorialPalette.Orange, TutorialPalette.Paper) >= 1.1f
              && Vector3.Distance(new Vector3(TutorialPalette.Orange.r, TutorialPalette.Orange.g, TutorialPalette.Orange.b),
                                  new Vector3(TutorialPalette.Paper.r, TutorialPalette.Paper.g, TutorialPalette.Paper.b)) > .2f);
        Check("emphasis red is still red", TutorialPalette.Red.r > .8f && TutorialPalette.Red.g < .25f && TutorialPalette.Red.b < .25f);
        Check("the bubble's text box sits inside the frame (pad " + RobotSpeaker.TextPadX + "/" + RobotSpeaker.TextPadY + ")",
              bubble != null && RobotSpeaker.TextPadX * 2f >= bubble.border.x / 2f && RobotSpeaker.TextPadY * 2f >= bubble.border.y / 4f);

        // Painted sprites are drawn as they are, never multiplied by a tint.
        string skip = File.ReadAllText("Assets/Scripts/Tutorial/TutorialSkip.cs");
        string card = File.ReadAllText("Assets/Scripts/Tutorial/TutorialCompletePanel.cs");
        Check("the skip plate is not tinted", skip.Contains("img.color = Color.white"));
        Check("the end card's plates are not tinted", card.Contains("Load(\"tut_card\"), Color.white") && card.Contains("frame.color = Color.white"));
        Check("the voice pulse tints the white ring cyan",
              File.ReadAllText("Assets/Scripts/Tutorial/RobotSpeaker.cs").Contains("Load(\"tut_ring\"), TutorialPalette.Cyan"));
        Check("PLAY and MENU labels are told apart (amber vs paper) on identical plates",
              card.Contains("\"PLAY\", TutorialPalette.Orange") && card.Contains("\"MENU\", TutorialPalette.Paper")
              && card.Contains("text.color = accent"));
        Check("the palette is the kit's: cyan 0BD0F6, panel 0C1725, ink 05060C",
              TutorialPalette.Html(TutorialPalette.Cyan) == "#0BD0F6" && TutorialPalette.Html(TutorialPalette.Panel) == "#0C1725"
              && TutorialPalette.Html(TutorialPalette.Ink) == "#05060C");
        Check("magenta is a pixel accent only: not used for body text (" + Contrast(TutorialPalette.Magenta, TutorialPalette.Panel).ToString("0.0") + ":1)",
              !File.ReadAllText("Assets/Scripts/Tutorial/RobotSpeaker.cs").Contains("TutorialPalette.Magenta"));
        Check("cyan labels read on the panel (" + Contrast(TutorialPalette.Cyan, TutorialPalette.Panel).ToString("0.0") + ":1)",
              Contrast(TutorialPalette.Cyan, TutorialPalette.Panel) >= 7f);

        // True pixel art: crisp import settings, and every sprite is an exact
        // 4x nearest-neighbour upscale of a small native grid with few colours.
        foreach (var k in Kit)
        {
            var imp = AssetImporter.GetAtPath("Assets/Art/Resources/Tutorial/" + k.name + ".png") as TextureImporter;
            Check(k.name + " imports crisp: point filter, no mipmaps, uncompressed",
                  imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled
                  && imp.textureCompression == TextureImporterCompression.Uncompressed);
            var px = ReadPng("Tutorial/" + k.name);
            if (px == null) continue;
            bool blocks = px.width % 4 == 0 && px.height % 4 == 0;
            var colours = new System.Collections.Generic.HashSet<Color32>();
            for (int by = 0; blocks && by < px.height; by += 4)
                for (int bx = 0; blocks && bx < px.width; bx += 4)
                {
                    var c0 = px.GetPixel(bx, by);
                    colours.Add(c0);
                    for (int dy = 0; blocks && dy < 4; dy++)
                        for (int dx = 0; dx < 4; dx++)
                            if (px.GetPixel(bx + dx, by + dy) != c0) { blocks = false; break; }
                }
            Check(k.name + " is an exact 4x upscale (every 4x4 block is one colour)", blocks);
            Check(k.name + " keeps a small palette (" + colours.Count + " colours)", colours.Count <= 32);
        }

        // The robot's face parts line up with the visor (tut_robot is
        // 256 px = 128 art units; the visor's dark screen spans x 29..99,
        // y 43..87 units).
        var robot = Resources.Load<Sprite>("Tutorial/tut_robot");
        var rt = ReadPng("Tutorial/tut_robot");
        if (rt != null)
        {
            Color screen = rt.GetPixel(128, 256 - 128);
            bool ok = true;
            foreach (var p in new[] { new Vector2(47f, 57f), new Vector2(81f, 57f), new Vector2(64f, 79f) })
            {
                Color c = rt.GetPixel(Mathf.RoundToInt(p.x * 2f), 256 - Mathf.RoundToInt(p.y * 2f));
                ok &= Mathf.Abs(c.r - screen.r) + Mathf.Abs(c.g - screen.g) + Mathf.Abs(c.b - screen.b) < .05f;
            }
            Check("the eyes and mouth sit on the visor's dark screen", ok);
            bool within = true;
            foreach (var r in new[] { new Rect(47f - 13f, 57f - 10f, 26f, 20f), new Rect(81f - 13f, 57f - 10f, 26f, 20f), new Rect(64f - 18f, 79f - 10f, 36f, 20f) })
                foreach (var c in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax - .5f, r.yMin), new Vector2(r.xMin, r.yMax - .5f), new Vector2(r.xMax - .5f, r.yMax - .5f) })
                    within &= rt.GetPixel(Mathf.RoundToInt(c.x * 2f), 256 - Mathf.RoundToInt(c.y * 2f) - 1).a > .99f;
            Check("the eye and mouth rects lie wholly on the opaque shell", within);
        }
        foreach (var frame in new[] { "tut_mouth_rest", "tut_mouth_a", "tut_mouth_e", "tut_mouth_o", "tut_mouth_big",
                                      "tut_eye_open", "tut_eye_half", "tut_eye_shut", "tut_eye_happy", "tut_jet_a", "tut_jet_b" })
            Check("talking/blink frame " + frame + " loads", Resources.Load<Sprite>("Tutorial/" + frame) != null);
    }

    // ---- The backdrop: the real Space world, as in a run ----

    static void TutorialBackdropIsSpace()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var legacy = GameObject.Find("starsBackground");
        Check("the legacy star quad has no renderer (nothing flat under the backdrop)",
              legacy == null || legacy.GetComponent<Renderer>() == null);
        Check("the legacy star material is gone", !File.Exists("Assets/Art/Backgrounds/Materials/starField_17.mat"));

        string boot = File.ReadAllText("Assets/Scripts/Worlds/Backdrop/WorldBackdrop.cs");
        Check("the tutorial scene starts the Space world backdrop",
              boot.Contains("scene.name == \"tutorialS5\"") && boot.Contains("WorldBackdrop.Create(WorldManager.Worlds[0].displayName)"));
        Check("Worlds[0] is Space", WorldManager.Worlds[0].displayName == "Space");

        var go = new GameObject("~TutorialBackdropTest");
        var wb = go.AddComponent<WorldBackdrop>();
        wb.Show(WorldManager.Worlds[0].displayName, false);
        for (int i = 0; i < 60; i++) wb.Step(1f / 60f);
        var set = wb.Current;
        Check("it builds the complete Space set", set != null && set.Complete && set.Spec.world == "Space");
        if (set != null)
        {
            int expected = 0;
            foreach (var l in set.Spec.layers) if (l.kind != BackdropCatalog.Kind.Pieces) expected++;
            Check("with every BackdropCatalog tile layer a run has (" + set.Tiles.Count + "/" + expected + ")", set.Tiles.Count == expected && expected > 0);
            Check("and its Space director", set.Director != null);
            Check("scrolling slowly even at speed 0 (" + WorldBackdrop.ScrollVelocity(0f) + " u/s)",
                  WorldBackdrop.ScrollVelocity(0f) > 0f && WorldBackdrop.ScrollVelocity(0f) < 10f);
        }
        Object.DestroyImmediate(go);
    }

    static bool Wired(GameObject go, string method)
    {
        var b = go != null ? go.GetComponent<Button>() : null;
        if (b == null) return false;
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            if (b.onClick.GetPersistentMethodName(i) == method && b.onClick.GetPersistentTarget(i) is tutButtonClicks)
                return true;
        return false;
    }
}
