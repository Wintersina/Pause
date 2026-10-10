using System.IO;
using UnityEditor;
using UnityEngine;

// Frames for review of the lash core (plan phase 1e: AttackLash, the vine and the tentacle) and the projectile behaviours
// (phase 1f: Streak, Shatter, Flutter, Slash, Roll, Burst, drawn with their procedural bodies) over a Verdant and a Frost backdrop.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod AttackCoresDPreview.Run
//   (writes to $ATTACKCORESD_PREVIEW_DIR, else Builds/AttackCoresDPreview; PNGs at 540 x 1080, named <world>-<what>.png)
public static class AttackCoresDPreview
{
    const int Width = 540, Height = 1080;
    const float Dt = 1f / 60f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember", "Tide" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("ATTACKCORESD_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/AttackCoresDPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (int w in new[] { 2, 1 }) { Lash(dir, w); Shots(dir, w); }
            }
            finally
            {
                AttackTestKit.Cleanup();
                ShotOutline.Bold = null;
            }
        }
        EditorApplication.Exit(0);
    }

    static Camera cam;
    static RenderTexture rt;
    static Texture2D png;
    static WorldBackdrop backdrop;
    static Transform shipMark;

    static void Scene(int world)
    {
        AttackTestKit.Fresh();
        cam = AttackTestKit.Cam;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var bgGo = new GameObject("~Backdrop");
        backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Worlds[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        var ship = new GameObject("~ShipMark");
        var sr = ship.AddComponent<SpriteRenderer>();
        sr.sprite = EliteFxArt.Plate;
        sr.color = new Color(.5f, 1f, 1f);
        sr.sortingOrder = 20;
        ship.transform.localScale = new Vector3(.012f, .012f, 1f);
        ship.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
        shipMark = ship.transform;
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (png == null) png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static void Pilot(Vector2 at)
    {
        AttackTestKit.Pilot.position = new Vector3(at.x, at.y, 0f);
        shipMark.position = new Vector3(at.x, at.y, 0f);
    }

    static void Shoot(string dir, string stem)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(Path.Combine(dir, stem + ".png"), png.EncodeToPNG());
        Debug.Log("[ATKCORESD-PREVIEW] " + stem);
    }

    static void Tick(float seconds)
    {
        int n = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            EliteSystem.Step(Dt);
            foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) fx.Tick(Dt);
            if (backdrop != null) backdrop.Step(Dt);
        }
    }

    // ---- the lash: the vine on Verdant, the tentacle on Frost (the Tide look: brass-teal) ----------------------------

    static void Lash(string dir, int world)
    {
        string name = Worlds[world].ToLower();
        int look = world == 2 ? 2 : 4;   // (the art world: a vine for Verdant, a tentacle's look for the Frost backdrop)
        Scene(world);
        Vector2 pilot = new Vector2(.3f, -2.4f), muzzle = new Vector2(-.4f, 2.4f);
        Pilot(pilot);
        var lash = AttackLash.Arm(LashSpec.Standard(look), muzzle, pilot, 1.1f, null);
        Tick(.3f); Shoot(dir, name + "-lash-1-tell-early");
        Tick(.7f); Shoot(dir, name + "-lash-2-tell-late");
        lash.Ignite();
        foreach (float t in new[] { .08f, .16f, .26f })
        {
            while (lash.State == AttackHazard.Phase.Live && lash.Age < t) Tick(Dt);
            Shoot(dir, name + "-lash-3-live-t" + Mathf.RoundToInt(t * 100f));
        }
        while (lash.State == AttackHazard.Phase.Live) Tick(Dt);
        Tick(.1f);
        Shoot(dir, name + "-lash-4-retract");
        EliteSystem.Clear();
        // a pilot at the rail: the mirrored sweep
        Scene(world);
        Pilot(new Vector2(-2f, -2.9f));
        var l2 = AttackLash.Arm(LashSpec.Standard(look), new Vector2(1f, 2.6f), new Vector2(-2f, -2.9f), 1f, null);
        Tick(.9f); Shoot(dir, name + "-lash-5-rail-tell");
        l2.Ignite();
        while (l2.State == AttackHazard.Phase.Live && l2.Age < .2f) Tick(Dt);
        Shoot(dir, name + "-lash-6-rail-live");
        EliteSystem.Clear();
        // the bold keyline over a bright backdrop
        if (world == 2)
        {
            Scene(world);
            ShotOutline.Bold = true;
            Pilot(new Vector2(.3f, -2.4f));
            var l3 = AttackLash.Arm(LashSpec.Standard(2), muzzle, pilot, 1f, null);
            l3.Ignite();
            while (l3.State == AttackHazard.Phase.Live && l3.Age < .16f) Tick(Dt);
            Shoot(dir, name + "-lash-7-bold-live");
            ShotOutline.Bold = null;
            EliteSystem.Clear();
        }
    }

    // ---- the projectile behaviours ---------------------------------------------------------------------------------

    static EliteDef Def(string world, float size)
    {
        var d = new EliteDef { key = "prev_" + world, world = world, shotSize = size, shotColor = "#FF4FD8", shotCore = "#FFFFFF", lobSeconds = .8f, poolSeconds = 2.5f, hazardCount = 6, hazardSpeed = 1.5f };
        d.Resolve();
        return d;
    }

    static void SkinOn(int world, EliteShots.Kind k, ShotMotion m)
    {
        var s = ShotMotionArt.Skin(world, k, m);
        ShotSkins.Override(world, k, s);
    }

    static void Shots(string dir, int world)
    {
        string name = Worlds[world].ToLower();
        var d = Def(Worlds[world].ToLower(), .3f);
        var pool = (System.Func<EliteShots.Kind, Vector2, Vector2, EliteShot>)((k, at, v) => EliteSystem.Shots.Fire(null, d, k, at, v));
        // Streak: three slugs with their sight lines and afterimages
        Scene(world);
        Pilot(new Vector2(0f, -3.4f));
        SkinOn(world, EliteShots.Kind.Shell, ShotMotion.Streak);
        pool(EliteShots.Kind.Shell, new Vector2(-1.2f, 3.8f), new Vector2(.3f, -3f));
        pool(EliteShots.Kind.Shell, new Vector2(0f, 3.8f), new Vector2(0f, -3f));
        pool(EliteShots.Kind.Shell, new Vector2(1.2f, 3.8f), new Vector2(-.3f, -3f));
        Tick(.32f); Shoot(dir, name + "-shot-streak");
        EliteSystem.Clear();
        // Shatter: the spear, then its chips
        Scene(world);
        Pilot(new Vector2(0f, -3.4f));
        SkinOn(world, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        SkinOn(world, EliteShots.Kind.Shard, ShotMotion.Shatter);
        pool(EliteShots.Kind.Bolt, new Vector2(-1f, 3.8f), new Vector2(.4f, -2.8f));
        pool(EliteShots.Kind.Bolt, new Vector2(1f, 3.8f), new Vector2(-.4f, -2.8f));
        Tick(.9f); Shoot(dir, name + "-shot-shatter-1-spears");
        Tick(.5f); Shoot(dir, name + "-shot-shatter-2-chips");
        EliteSystem.Clear();
        // Flutter: two volleys of leaves
        Scene(world);
        Pilot(new Vector2(0f, -3.4f));
        SkinOn(world, EliteShots.Kind.Shard, ShotMotion.Flutter);
        foreach (float x in new[] { -1.4f, 0f, 1.4f })
        {
            pool(EliteShots.Kind.Shard, new Vector2(x - .15f, 3.8f), new Vector2(-.2f, -1.8f));
            pool(EliteShots.Kind.Shard, new Vector2(x + .15f, 3.8f), new Vector2(.2f, -1.8f));
        }
        Tick(2.2f); Shoot(dir, name + "-shot-flutter");
        EliteSystem.Clear();
        // Slash: crescents crossing the lane on their rows
        Scene(world);
        Pilot(new Vector2(0f, -3.4f));
        SkinOn(world, EliteShots.Kind.Shard, ShotMotion.Slash);
        pool(EliteShots.Kind.Shard, new Vector2(-2.1f, 2.2f), new Vector2(4.5f, 0f));
        pool(EliteShots.Kind.Shard, new Vector2(2.1f, .6f), new Vector2(-4.5f, 0f));
        Tick(.45f); Shoot(dir, name + "-shot-slash");
        EliteSystem.Clear();
        // Roll: a log in the air over its marked spot, then rolling
        Scene(world);
        Pilot(new Vector2(0f, -3.4f));
        SkinOn(world, EliteShots.Kind.Glob, ShotMotion.Roll);
        var dl = Def(Worlds[world].ToLower(), .55f);
        var log = EliteSystem.Shots.Fire(null, dl, EliteShots.Kind.Glob, new Vector2(-1.4f, 4f), Vector2.zero);
        log.Lob(new Vector2(-.9f, 1.4f), 1.1f);
        Tick(.6f); Shoot(dir, name + "-shot-roll-1-lob");
        Tick(1.4f); Shoot(dir, name + "-shot-roll-2-rolling");
        EliteSystem.Clear();
        // Burst: a pod lands, its cloud stays and spores fly
        Scene(world);
        Pilot(new Vector2(0f, -3.4f));
        SkinOn(world, EliteShots.Kind.Glob, ShotMotion.Burst);
        SkinOn(world, EliteShots.Kind.Shard, ShotMotion.Burst);
        var pod = EliteSystem.Shots.Fire(null, d, EliteShots.Kind.Glob, new Vector2(0f, 4f), Vector2.zero);
        pod.Lob(new Vector2(0f, 1.2f), 1f);
        Tick(.6f); Shoot(dir, name + "-shot-burst-1-lob");
        Tick(.65f); Shoot(dir, name + "-shot-burst-2-spores");
        EliteSystem.Clear();
    }
}
