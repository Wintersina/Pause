using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames for review of the themed area hazards (plan phases 1c AttackBlast, 1d AttackStrike): the
// cold-blast ring and the strike patterns over a Frost and an Ember backdrop, each at its tell (dotted
// footprint, glyphs, crack edges), live and burst, plus the bold keyline on a bright backdrop.
// Phases 1a / 1b (AttackJet, AttackWave): the world's jet (Ember's flame cone, Tide's pressure jet, Frost's ray, Space's lance)
// and the surf wave / scan line over each world's backdrop (Tide's is Backdrop3, the dark water), at the tell (outline, flare, gap
// chevrons), live and fading, plus a bold pair over a bright day.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod AttackHazardPreview.Run
//   ... -executeMethod AttackHazardPreview.RunJetWave   (only the jet and the wave frames)
//   (writes to $ATTACKHAZ_PREVIEW_DIR, else Builds/AttackHazardPreview; PNGs at 540 x 1080, named <world>-<what>.png)
public static class AttackHazardPreview
{
    const int Width = 540, Height = 1080;
    const float Dt = 1f / 60f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember", "Tide" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("ATTACKHAZ_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/AttackHazardPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (int w in new[] { 1, 3 }) Show(dir, w);
                Bold(dir);
                foreach (int w in new[] { 0, 1, 3, 4 }) JetWave(dir, w);
                BoldJetWave(dir);
            }
            finally
            {
                AttackTestKit.Cleanup();
                ShotOutline.Bold = null;
            }
        }
        EditorApplication.Exit(0);
    }

    // Only the phase 1a / 1b frames.
    public static void RunJetWave()
    {
        string dir = System.Environment.GetEnvironmentVariable("ATKHAZ_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/AttackHazardPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (int w in new[] { 0, 1, 3, 4 }) JetWave(dir, w);
                BoldJetWave(dir);
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

    static void Scene(int world, bool dark = true)
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
        // the pilot: a white chevron for scale (the ship is ~.7 u wide)
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

    static Transform shipMark;

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
        Debug.Log("[ATKHAZ-PREVIEW] " + stem);
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

    static void Show(string dir, int world)
    {
        string name = Worlds[world].ToLower();
        Scene(world);
        // ---- the ring
        Vector2 pilot = new Vector2(.5f, -2.4f), muzzle = new Vector2(0f, 2.2f);
        Pilot(pilot);
        var spec = BlastSpec.Standard(world);
        var blast = AttackBlast.Arm(spec, muzzle, pilot, 1.1f, null);
        Tick(.35f); Shoot(dir, name + "-blast-1-tell-early");
        Tick(.55f); Shoot(dir, name + "-blast-2-tell-late");
        blast.Ignite();
        Tick(.05f);
        float[] radii = { 1.4f, 2.3f, 3.2f };
        for (int i = 0; i < radii.Length; i++)
        {
            while (blast.State == AttackHazard.Phase.Live && blast.Radius < radii[i]) Tick(Dt);
            Shoot(dir, name + "-blast-" + (3 + i) + "-live-r" + radii[i].ToString("0.0").Replace('.', '_'));
        }
        blast.Cancel();
        // ---- the strikes: three lanes round the pilot
        float[] lanes = new float[4];
        int n = StrikeLanes.Pick(pilot.x, 3, AttackStrike.MinLaneSpacing, BossRails.DrawnInnerEdge, .18f, lanes);
        var spec2 = StrikeSpec.Standard(world);
        for (int i = 0; i < n; i++) AttackStrike.Arm(spec2, lanes[i], pilot.y, 1f, null);
        Tick(.45f); Shoot(dir, name + "-strike-1-tell");
        Tick(.45f); Shoot(dir, name + "-strike-2-tell-late");
        Tick(.14f);
        Shoot(dir, name + "-strike-3-live");
        Tick(.3f);
        Shoot(dir, name + "-strike-4-burst");
        EliteSystem.Clear();
    }

    // A bright backdrop (Verdant's day) forces the bold keyline: the ring and a column over it.
    static void Bold(string dir)
    {
        Scene(2);
        ShotOutline.Bold = true;
        Vector2 pilot = new Vector2(-.6f, -2.4f), muzzle = new Vector2(0f, 2.2f);
        Pilot(pilot);
        var blast = AttackBlast.Arm(BlastSpec.Standard(2), muzzle, pilot, 1f, null);
        blast.Ignite();
        while (blast.State == AttackHazard.Phase.Live && blast.Radius < 2.4f) Tick(Dt);
        float[] lanes = new float[4];
        int n = StrikeLanes.Pick(pilot.x, 2, AttackStrike.MinLaneSpacing, BossRails.DrawnInnerEdge, .18f, lanes);
        for (int i = 0; i < n; i++) AttackStrike.Arm(StrikeSpec.Standard(2), lanes[i] + 1.1f, pilot.y + 3f, 1f, null).Ignite();
        Tick(.06f);
        Shoot(dir, "verdant-bold-ring-and-column");
        EliteSystem.Clear();
    }

    // ---- phases 1a / 1b: the world's jet and wave -------------------------------------------------------------------

    static void JetWave(string dir, int world)
    {
        string name = Worlds[world].ToLower();
        Scene(world);
        Vector2 pilot = new Vector2(.7f, -2.6f), nozzle = new Vector2(-.3f, 2.3f);
        Pilot(pilot);
        var spec = JetSpec.Standard(world);
        if (spec.style == JetStyle.Flame) spec.sweepDeg = 14f;   // (a little more than the roster's 10 so the sweep reads on a still frame)
        var jet = AttackJet.Arm(spec, nozzle, pilot, 1.1f, null);
        Tick(.3f); Shoot(dir, name + "-jet-1-tell-early");
        Tick(.6f); Shoot(dir, name + "-jet-2-tell-late");
        jet.Ignite();
        Tick(.08f); Shoot(dir, name + "-jet-3-live-start");
        Tick(Mathf.Max(.1f, jet.LiveSeconds * .6f)); Shoot(dir, name + "-jet-4-live-late");
        AdvanceThrough(jet);
        Tick(.05f); Shoot(dir, name + "-jet-5-fading");
        EliteSystem.Clear();

        // the wave: a gap turned off the pilot, seen in the tell and falling
        var ws = WaveSpec.Standard(world);
        ws.gapOffset = 1f;
        Vector2 muzzle = new Vector2(0f, 3.2f);
        var wave = AttackWave.Arm(ws, muzzle, pilot, 1.1f, null);
        Tick(.5f); Shoot(dir, name + "-wave-1-tell");
        Tick(.55f);
        wave.Ignite();
        Tick(.25f);
        Shoot(dir, name + "-wave-2-live-high");
        while (wave.State == AttackHazard.Phase.Live && wave.Y > pilot.y + 2f) Tick(Dt);
        Shoot(dir, name + "-wave-3-live-near");
        while (wave.State == AttackHazard.Phase.Live && wave.Y > pilot.y - .3f) Tick(Dt);
        Shoot(dir, name + "-wave-4-live-past");
        EliteSystem.Clear();
    }

    static void AdvanceThrough(AttackJet jet)
    {
        int n = 0;
        while (jet.State == AttackHazard.Phase.Live && n++ < 300) Tick(Dt);
    }

    // A bright backdrop (Verdant's day) forces the bold keyline: a flame and a surf wave over it.
    static void BoldJetWave(string dir)
    {
        Scene(2);
        ShotOutline.Bold = true;
        Vector2 pilot = new Vector2(-.6f, -2.6f);
        Pilot(pilot);
        var jet = AttackJet.Arm(JetSpec.Flame(2), new Vector2(1.1f, 2.3f), pilot, 1f, null);
        jet.Ignite();
        var ws = WaveSpec.Surf(2);
        ws.gapOffset = 1f;
        var wave = AttackWave.Arm(ws, new Vector2(0f, 1.6f), pilot, 1f, null);
        wave.Ignite();
        Tick(.25f);
        Shoot(dir, "verdant-bold-jet-and-wave");
        EliteSystem.Clear();
    }
}
