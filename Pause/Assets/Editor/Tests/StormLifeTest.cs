using UnityEditor.SceneManagement;
using UnityEngine;

// Storm's backdrop is ALIVE (StormLife) and its boulder is gone.
//   * the turbine raft's three fans have rotors cut into their own atlas ("rotors") and a rotation animator: each speed > 0,
//     they really turn over time, the other landmarks show no rotor;
//   * the kept landmarks hover on the wind (the towers' breathing / glow / lightning and the spires' tip crackle were retired
//     with those landmarks on 2026-11-28);
//   * a paused game is a still frame (dt 0); no per-frame allocation;
//   * the world's boulder is off (CoverBoulderTable) while the other worlds keep theirs.
public static class StormLifeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SLIFE] PASS  " : "[SLIFE] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Fans();
            Hover();
            Boulder();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            BackdropVariants.For("Storm").Reset();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[SLIFE] failures: " + fails);
        return fails;
    }

    static WorldBackdrop wb;
    static StormDirector Fresh(int variant)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var sel = BackdropVariants.For("Storm");
        sel.Reset(); sel.Seed(11); sel.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        wb = WorldBackdrop.Create("Storm");
        wb.Show("Storm", false);
        return wb.Current.Director as StormDirector;
    }

    static void Fans()
    {
        var d = Fresh(1);
        Check("the rotors atlas carries three rotor sprites", d != null && d.Life != null);
        var atlas = new BackdropSet("Storm", new GameObject("~r").transform, 3f, 5f).Atlas("rotors");
        Check("rotors atlas: rotor_00..02 exist", atlas.Frames("rotor").Length == 3);
        Object.DestroyImmediate(GameObject.Find("~r"));
        bool speeds = StormLifeTuning.FanSpeeds.Length == 3;
        for (int k = 0; k < 3 && speeds; k++)
            for (float t = 0f; t < 60f; t += .5f) speeds &= Mathf.Abs(StormLife.FanSpeedAt(k, t)) > 15f && Mathf.Abs(StormLife.FanSpeedAt(k, t)) < 140f;
        Check("every fan's speed stays between 15 and 140 deg/s (believable, never stalls, never a blur)", speeds);

        var fan = d.Showcase(StormLifeTuning.TurbinePiece, 1.7f, 0f, 0f);
        var other = d.Showcase("gantry_00", 1.7f, 0f, 0f);
        Check("a gantry shows no rotors", other != null && !d.Life.RotorsShown(other));
        fan = d.Showcase(StormLifeTuning.TurbinePiece, 1.7f, 0f, 0f);
        for (int i = 0; i < 5; i++) wb.Step(Dt);
        Check("the turbine raft is a fan piece with three rotors showing", fan != null && d.Life.IsFan(fan) && d.Life.RotorsShown(fan));
        var ang = d.Life.RotorAngles(fan);
        float[] a0 = { ang[0], ang[1], ang[2] };
        for (int i = 0; i < 120; i++) wb.Step(Dt);
        bool turned = true;
        for (int k = 0; k < 3; k++) turned &= Mathf.Abs(ang[k] - a0[k]) > 60f && Mathf.Sign(ang[k] - a0[k]) == Mathf.Sign(StormLifeTuning.FanSpeeds[k]);
        Check("in 2 s every fan turned > 60 deg in its own direction (" + (ang[0] - a0[0]).ToString("0") + ", " + (ang[1] - a0[1]).ToString("0") + ", " + (ang[2] - a0[2]).ToString("0") + ")", turned);
        Check("the fans differ in speed (not locked together)", Mathf.Abs(Mathf.Abs(ang[0] - a0[0]) - Mathf.Abs(ang[1] - a0[1])) > 3f);
        float before = ang[0];
        wb.Step(0f);
        Check("paused (dt 0): the fans hold still", ang[0] == before);

        // the relay dish turns on its mast (steady loop, foreshortened by |cos|, frozen with the world clock)
        var dish = d.Showcase(StormLifeTuning.DishPiece, 1.7f, 0f, 0f);
        for (int i = 0; i < 5; i++) wb.Step(Dt);
        Check("the relay dish piece shows its turning dish", dish != null && d.Life.IsDish(dish) && d.Life.DishWidth(dish) != 0f);
        float d0 = d.Life.DishAngle(dish), wMin = 9f, wMax = -9f;
        for (int i = 0; i < 150; i++) { wb.Step(Dt); float w = d.Life.DishWidth(dish) / StormLifeTuning.DishScale; wMin = Mathf.Min(wMin, w); wMax = Mathf.Max(wMax, w); }
        Check("in 2.5 s the dish turned about " + (StormLifeTuning.DishSpeed * 2.5f).ToString("0") + " deg and swept through edge-on and mirrored (width " + wMin.ToString("F2") + " .. " + wMax.ToString("F2") + ")",
              Mathf.Abs(d.Life.DishAngle(dish) - d0 - StormLifeTuning.DishSpeed * 2.5f) < 8f && wMin < 0f && wMax > .9f);
        float da = d.Life.DishAngle(dish);
        wb.Step(0f);
        Check("paused (dt 0): the dish holds still", d.Life.DishAngle(dish) == da);
        Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
    }

    // The retired pieces' life is gone (2026-11-28: towers, their lightning layer and the spires' tip crackle went with those
    // landmarks); what stays: every kept landmark hovers a hair on the wind, a paused game holds still, nothing allocates.
    static void Hover()
    {
        var d = Fresh(2);
        var gantry = d.Showcase("gantry_00", 1.7f * StormTuning.PieceScale, 0f, 0f);
        float maxPos = 0f;
        for (int i = 0; i < 60 * 10; i++)
        {
            wb.Step(Dt);
            if (gantry != null) maxPos = Mathf.Max(maxPos, gantry.body.localPosition.magnitude);
        }
        Check("a kept landmark hovers a hair on the wind (" + maxPos.ToString("0.000") + " body units)", gantry != null && maxPos > 0f);
        Vector3 at = gantry != null ? gantry.body.localPosition : Vector3.zero;
        for (int i = 0; i < 30; i++) wb.Step(0f);
        Check("paused (dt 0): it holds still", gantry != null && gantry.body.localPosition == at);
        Check("the retired landmarks are gone from the atlas and the placement lists (" + string.Join(", ", StormTuning.AllPieces()) + ")",
              string.Join(",", StormTuning.AllPieces()) == "gantry_00,turbine_raft_00,relay_dish_00" &&
              d.Showcase("anvil_tower_00", 1f, 0f, 0f) == null && d.Showcase("spire_00", 1f, 0f, 0f) == null);

        // no allocation over many frames
        System.GC.Collect();
        long m0 = System.GC.GetTotalMemory(false);
        for (int i = 0; i < 300; i++) wb.Step(Dt);
        long grown = System.GC.GetTotalMemory(false) - m0;
        Check("300 frames of backdrop life allocate ~nothing (" + grown + " bytes)", grown < 4096);
        Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
    }

    static void Boulder()
    {
        Check("Storm's boulder is off", !CoverBoulderTable.Enabled(5) && CoverBoulderTable.Entries[5].world == "Storm");
        string why = CoverBoulderDirector.Blocked(5, 100000f);
        Check("the director never spawns it in Storm (blocked: " + why + ")", why != null);
        bool others = true;
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
            if (w != 5) others &= CoverBoulderTable.Entries[w].enabled;
        Check("every other world keeps its boulder", others);
    }
}
