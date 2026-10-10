using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames for review of the themed area hazards (plan phases 1c AttackBlast, 1d AttackStrike): the
// cold-blast ring and the strike patterns over a Frost and an Ember backdrop, each at its tell (dotted
// footprint, glyphs, crack edges), live and burst, plus the bold keyline on a bright backdrop.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod AttackHazardPreview.Run
//   (writes to $ATTACKHAZ_PREVIEW_DIR, else Builds/AttackHazardPreview; PNGs at 540 x 1080, named <world>-<what>.png)
public static class AttackHazardPreview
{
    const int Width = 540, Height = 1080;
    const float Dt = 1f / 60f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };

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
}
