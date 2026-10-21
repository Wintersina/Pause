using UnityEditor.SceneManagement;
using UnityEngine;

// Storm's backdrop is ALIVE (StormLife) and its boulder is gone.
//   * the turbine raft's three fans have rotors cut into their own atlas ("rotors") and a rotation animator: each speed > 0,
//     they really turn over time, the other landmarks show no rotor;
//   * thunderhead towers breathe and drift, their heart glow pulses slowly and flickers rarely (<= 3 Hz, one at a time, small
//     alpha); lightning-rod masts crackle at the tip;
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
            Towers();
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
        Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
    }

    static void Towers()
    {
        var d = Fresh(2);
        var tower = d.Showcase("anvil_tower_00", 1.7f, 0f, 0f);
        for (int i = 0; i < 5; i++) wb.Step(Dt);
        Check("a thunderhead tower has a heart glow showing", tower != null && d.Life.HeartAlpha(tower) > .02f);
        float minA = 9f, maxA = 0f, maxScale = 0f, minScale = 9f, maxPos = 0f, prev = d.Life.HeartAlpha(tower), maxStep = 0f;
        int flickers = 0; bool wasUp = false;
        for (int i = 0; i < 60 * 40; i++)
        {
            wb.Step(Dt);
            float a = d.Life.HeartAlpha(tower);
            minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
            maxStep = Mathf.Max(maxStep, Mathf.Abs(a - prev)); prev = a;
            float s = Mathf.Abs(tower.body.localScale.x);
            minScale = Mathf.Min(minScale, s); maxScale = Mathf.Max(maxScale, s);
            maxPos = Mathf.Max(maxPos, tower.body.localPosition.magnitude);
            bool up = a > StormLifeTuning.HeartBase + StormLifeTuning.HeartSwing + .06f;
            if (up && !wasUp) flickers++;
            wasUp = up;
        }
        Check("the heart glow pulses (alpha " + minA.ToString("0.00") + " .. " + maxA.ToString("0.00") + ") and stays soft (< .30)", maxA - minA > .03f && maxA < .30f);
        Check("a glow alpha changes by < " + (.06f) + " per 60 fps frame (no snap): " + maxStep.ToString("0.000"), maxStep < .06f);
        Check("the tower breathes (scale " + minScale.ToString("0.000") + " .. " + maxScale.ToString("0.000") + ") and drifts a little", maxScale - minScale > .008f && maxScale < 1.05f && maxPos > 0f);
        Check("a flicker, when it fires, is at most ~3 per 10 s on one tower (" + flickers + " in 40 s)", flickers <= 14);
        Check("flicker spacing constants keep it under 3 Hz and seconds apart",
              StormLifeTuning.FlickerSecondAt >= .4f && StormLifeTuning.FlickerGapMin >= 3f && StormLifeTuning.FlickerPeak <= .2f);

        Check("a tower's lightning is its own layer (thunder atlas found)", d.Life.HasBolts);
        // a tower that stays put (the ground is still), freshly in view: the lightning layer is measured over its whole life
        moveBackGround.speed = 0f;
        tower = d.Showcase("anvil_tower_00", 1.7f, 0f, 0f);
        for (int i = 0; i < 5; i++) wb.Step(Dt);
        float idleB = 9f, peakB = 0f; int strikesUp = 0; bool boltUp = false;
        for (int i = 0; i < 60 * 40; i++)
        {
            wb.Step(Dt);
            float b = d.Life.BoltAlpha(tower);
            idleB = Mathf.Min(idleB, b); peakB = Mathf.Max(peakB, b);
            bool up = b > .4f;
            if (up && !boltUp) strikesUp++;
            boltUp = up;
        }
        Check("the bolts idle dim (" + idleB.ToString("0.00") + ") and strike bright (" + peakB.ToString("0.00") + ")", idleB < .15f && peakB > .5f);
        Check("bolt strikes happen, but never more than 2.5 per second (" + strikesUp + " in 40 s)", strikesUp >= 1 && strikesUp <= 40 * 2.5f && StormLifeTuning.BoltMinGap >= .4f);

        moveBackGround.speed = .25f;
        var mast = d.Showcase("spire_00", 1.7f, 0f, 0f);
        for (int i = 0; i < 30; i++) wb.Step(Dt);
        Check("a lightning-rod mast crackles a faint glow at its tip", mast != null && d.Life.TipAlpha(mast) > .05f && d.Life.TipAlpha(mast) < .5f);

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
