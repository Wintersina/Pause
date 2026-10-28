using System;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

// The second first-use hitch sweep (FirstUseWarm): the first shot's clips, the ship's weapon / attack / power / rotor
// art, the first pause's sprites, the death panel's sprites + translucent button + frame measurements, the fusion
// strips and generated overlays, the liftoff shader and the first toast's icons are all done AHEAD of their first use,
// one step a frame, idempotently and without allocation once finished.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites FirstUseWarmTest
public static class FirstUseWarmTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FUW] PASS  " : "[FUW] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static string Src(string rel) { return File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/" + rel)); }

    static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, StringComparison.Ordinal);
        if (i < 0) return "";
        int end = src.IndexOf("\n    }", i, StringComparison.Ordinal);
        return end < 0 ? src.Substring(i) : src.Substring(i, end - i);
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        FirstUseWarm.Clear();
        WarmUpRunsOneStepAFrameAndFinishes();
        WarmedThingsAreResident();
        OverlayAndStripsAreBuiltByTheWarmUp();
        IdempotentAndAllocationFree();
        WiredIntoTheGame();
        FirstUseWarm.Clear();
        Debug.Log("[FUW] failures: " + fails);
        return fails;
    }

    static int ship = ShipId.Starter;
    static bool NoGpu { get { return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null; } }

    static void WarmUpRunsOneStepAFrameAndFinishes()
    {
        FusionFx.ResetStripsForTests();
        FusionFx.ResetOverlaySpritesForTests();
        FirstUseWarm.QueueSession();
        FirstUseWarm.QueueShip(ship);
        FirstUseWarm.QueueWorld(0);
        int queued = FirstUseWarm.Queued;
        Check("the session, the ship and the world queue steps (" + queued + ")", queued > 40);

        int frames = 0, worst = 0;
        bool strip = FusionFx.StripReady(0);
        while (!FirstUseWarm.Done && frames < 400)
        {
            int before = FirstUseWarm.Ran;
            FirstUseWarm.Pump();
            worst = Math.Max(worst, FirstUseWarm.Ran - before);
            frames++;
        }
        Check("never more than " + FirstUseWarm.StepsPerFrame + " step in a frame (worst " + worst + ")", worst <= FirstUseWarm.StepsPerFrame);
        // about 80 steps at one a frame is roughly a second of the menu idle at 60 fps, a sliver of the 8 s calm start
        Check("the warm-up finishes in a bounded number of frames (" + frames + " for " + queued + " steps, <= 150)", FirstUseWarm.Done && frames <= 150);
        Check("fusion strip 0 was not sliced before the warm-up and is after", !strip && FusionFx.StripReady(0));
        Check("the ring is " + (FusionFx.OverlayWarmSteps - 1) + " chunks of " + FusionFx.RingRowsPerStep + " rows (a chunk is a fixed small loop)",
              (FusionFx.OverlayWarmSteps - 1) * FusionFx.RingRowsPerStep == FusionFx.RingSize);
    }

    static bool Resident(Type type, string path)
    {
        var o = Resources.Load(path, type);
        return o != null && FirstUseWarm.Holds(o);
    }

    static void WarmedThingsAreResident()
    {
        Check("the enemy shot's laser clip is held", Resident(typeof(AudioClip), PewSound.LaserPath));
        Check("the enemy shot's gun clip is held", Resident(typeof(AudioClip), PewSound.GunPath));
        Check("the weapon sheet of the ship is sliced", WeaponArt.SheetReady(ship));
        Check("the target explosions sheet is sliced", WeaponArt.ExplosionsReady);
        Check("the ship's attack strip is sliced", ShipFxArt.StripReady(ship, "attack"));
        Check("the secret-power badge atlas is sliced", ShipFxArt.MeterSheetReady);
        Check("the ultimate gun tile is cut", UltimateGun.GunReady(ship));
        foreach (var p in new[] { ResumeFx.VignetteSprite, ResumeFx.StreakSprite, "Hud/hud_meter", PausedOverlayAnim.FxRoot + "pausedGlow_a_0", PausedOverlayAnim.FxRoot + "pausedGlow_b_5" })
            Check("pause sprite held: " + p, Resident(typeof(Sprite), p));
        foreach (var n in DeathPanelView.WarmSprites)
            Check("death panel sprite held: " + n, Resident(typeof(Sprite), DeathPanelView.SpriteRoot + n));
        Check("the death frame sprite of world 0 is held", Resident(typeof(Sprite), HudSkin.DeathPanelPath(0)));
        Check("the frame's top and bottom measurements are queued", FirstUseWarm.Asked("death:top:0") && FirstUseWarm.Asked("death:bottom:0"));
        if (!NoGpu) Check("the translucent death button copy exists before the first death", DeathPanelView.ButtonReady);
        Check("the planetfall / liftoff shader is held", Resident(typeof(Shader), Planetfall.ShaderPath));
        Check("its warm-up step ran", FirstUseWarm.Asked(FirstUseWarmSteps.LiftoffShaderKey) && (FirstUseWarmSteps.ShaderWarmed || NoGpu));
        Check("the legacy explosion overlay is warmed", FirstUseWarm.Asked("cue:legacyexplosion") && LegacyExplosionFx.Frames() != null);
        int toasts = 0;
        foreach (var def in AchievementCatalog.All) if (FirstUseWarm.Asked("ach:" + def.id)) toasts++;
        Check("the first locked bronze achievements' icons are warmed (" + toasts + ")", toasts > 0 && toasts <= FirstUseWarmSteps.AchievementArtWarmed);
    }

    static void OverlayAndStripsAreBuiltByTheWarmUp()
    {
        bool all = true;
        for (int i = 0; i < FusionFx.StripCount; i++) all &= FusionFx.StripReady(i);
        Check("all " + FusionFx.StripCount + " fusion strips are sliced before the run builds its renderers", all);
        Check("the dot, bar and ring sprites are generated before the run starts", FusionFx.OverlaySpritesReady);

        // a run start that arrives half-way: the strips / ring left over are finished by Build, nothing breaks
        FusionFx.ResetStripsForTests();
        FusionFx.ResetOverlaySpritesForTests();
        FusionFx.PrewarmStrip(2);
        FusionFx.PrewarmOverlay(0);
        FusionFx.PrewarmOverlay(1);
        Check("a partly warmed ring is not yet a sprite (" + FusionFx.RingRowsPainted + " of " + FusionFx.RingSize + " rows)",
              !FusionFx.OverlaySpritesReady && FusionFx.RingRowsPainted == FusionFx.RingRowsPerStep);
        if (FusionFx.Instance != null) UnityEngine.Object.DestroyImmediate(FusionFx.Instance.gameObject);
        var ship0 = new GameObject("fuw_ship", typeof(SpriteRenderer));
        try
        {
            var fx = FusionFx.Build(ship0);
            Check("Build finishes the leftovers (strips and ring)", fx != null && FusionFx.OverlaySpritesReady && FusionFx.StripReady(2) && FusionFx.StripReady(0));
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx.gameObject);
        }
        finally { UnityEngine.Object.DestroyImmediate(ship0); }
    }

    static void IdempotentAndAllocationFree()
    {
        int ran = FirstUseWarm.Ran;
        FirstUseWarm.QueueSession();
        FirstUseWarm.QueueShip(ship);
        FirstUseWarm.QueueWorld(0);
        Check("asking for the same warm-up again queues nothing", FirstUseWarm.Queued == 0 && FirstUseWarm.Done);
        FirstUseWarm.Pump();
        Check("... and runs nothing", FirstUseWarm.Ran == ran);
        Check("a key is accepted once", !FirstUseWarm.Add("pew:laser", null, null, null));

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) FirstUseWarm.Pump();
        Check("a finished warm-up allocates nothing per frame", GC.GetAllocatedBytesForCurrentThread() - a0 == 0);
    }

    static void WiredIntoTheGame()
    {
        Check("ship Start queues the session and the ship's art after the jet prewarm",
              Src("Ship/collisionDetection.cs").Contains("FirstUseWarm.QueueSession(); FirstUseWarm.QueueShip(flown);"));
        Check("each world begin queues its death frame", Body(Src("Core/ArtWarm.cs"), "public static void Begin").Contains("FirstUseWarm.QueueWorld(world)"));
        string rt = Src("Core/FirstUseWarm.cs");
        Check("the app start queues the menu-idle warm-up", rt.Contains("RuntimeInitializeOnLoadMethod") && rt.Contains("QueueAtStart()"));
        Check("one step a frame is the documented budget, loaded off-thread", FirstUseWarm.StepsPerFrame == 1 && rt.Contains("Resources.LoadAsync"));
        Check("the run's fusion build slices through the per-strip cache", Src("Gameplay/Pickups/Fusions/FusionFx.cs").Contains("static Sprite[] Strip(int i)"));
        Check("the ring is painted in chunks and finished by BuildSprites", Src("Gameplay/Pickups/Fusions/FusionFxOverlays.cs").Contains("RingStep(RingSize)"));
    }
}
