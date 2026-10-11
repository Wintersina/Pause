using UnityEditor.SceneManagement;
using UnityEngine;

// The hearts after a hull-colour (health) upgrade are the right size, on the
// right orbit, on every FitDevice -- the way a run really starts. The bug:
// every run begins with the portal arrival (WorldEntry), which flies the
// ship out of the vortex from 6% of its size, and the hearts were built in
// that first frame and sized against the tiny ship (HeartOrbit.BuildHearts
// divided by the ship's lossy scale of that moment), so once the ship grew
// they came out ~16x too big ("MASSIVE, spinning round the ship").
//
// Covers: hearts built mid-arrival after buying colour 1, 2 and 3, hearts on
// a ship at rest, a save that already owns the upgrade (RunMax with no
// collisionDetection yet), the tutorial hull, and the sanity band (a heart's
// world size, its pixels on each FitDevice, the orbit radius).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod UpgradeHeartsSizeTest.Run
public static class UpgradeHeartsSizeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[UH] PASS  " : "[UH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Ships tried: the starter (train), a one-orbit spinner, a crown, a wide hull.
    static readonly int[] Ships = { 1, 7, 8, 11 };
    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (int id in Ships)
            {
                for (int level = 0; level <= 3; level++)
                {
                    Buy(id, level);
                    CheckRun(id, level, true, true);    // as a run starts: mid portal arrival
                    CheckRun(id, level, false, true);   // at rest (replay: no entry)
                }
                // a save that already owns the upgrade: no collisionDetection yet
                Buy(id, 3);
                CheckRun(id, 3, true, false);
            }
            TutorialHull();
        }
        finally
        {
            ShipUiSlots.ScreenOverride = null;
            WorldEntry.Cancel();
            collisionDetection.MAXLIFE = 3;
            collisionDetection.lifeCounter = 0;
            foreach (int id in ShipId.All)
                for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
        Debug.Log("[UH] failures: " + fails);
        return fails;
    }

    // The shop's own purchase call, `level` colours of ship `id`.
    static void Buy(int id, int level)
    {
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        PlayerPrefs.SetString("boughtship" + id, "True");
        PlayerPrefs.SetInt("spawnShip", id);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000000f);
        for (int n = 1; n <= level; n++)
            if (ShipSkins.TryPurchase(id, n) != ShipSkins.PurchaseResult.Bought) Check("ship " + id + " colour " + n + " bought", false);
    }

    // heartSize*(1 +- DepthScale), a little slack for the pop and lean.
    static float Lo(float heartSize) { return heartSize * (1f - HeartOrbit.DepthScale) * .95f; }
    static float Hi(float heartSize) { return heartSize * (1f + HeartOrbit.DepthScale) * 1.05f; }

    static float WorldSize(Transform heart)
    {
        var sprite = heart.GetComponent<SpriteRenderer>().sprite;
        return Mathf.Max(Mathf.Abs(heart.lossyScale.x), Mathf.Abs(heart.lossyScale.y)) *
               Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
    }

    static void Cleanup()
    {
        WorldEntry.Cancel();
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.parent == null) Object.DestroyImmediate(t.gameObject);
    }

    // One run's hearts for `id` with the colours bought: built `entry` (the
    // ship tiny, flying out of the portal) or at rest; `explicitCount` builds
    // the run's max the way collisionDetection does, else the save's own.
    static void CheckRun(int id, int level, bool entry, bool explicitCount)
    {
        string tag = ShipId.NameOf(id) + " +" + level + " colours" + (entry ? " mid-entry" : " at rest") + (explicitCount ? "" : " (loaded save)");
        Cleanup();
        collisionDetection.lifeCounter = 0;
        collisionDetection.MAXLIFE = explicitCount ? ShipLives.Max(id) : 0;
        int want = ShipLives.Max(id);

        var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        var ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        var sr = ship.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(id);
        sr.sortingOrder = 10;
        float scale = shopingShips.NormalizedHullScale(sr.sprite) * ShipScale.Main;
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        ship.transform.position = new Vector3(0f, -2f, 1f);

        if (entry)
        {
            Check(tag + ": the portal arrival opens", PortalArrival.Spawn(ship.transform, Color.cyan) != null);
            Check(tag + ": the ship starts tiny", ship.transform.localScale.x < scale * .2f);
        }
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        hearts.SendMessage("Start");   // built in the very first frame, as in a run
        // the arrival plays out (2.6 s) while the hearts orbit
        for (int f = 0; f < 200; f++)
        {
            var arrival = PortalArrival.Live;
            if (arrival != null && arrival.State != PortalArrival.Stage.Done) arrival.Step(Dt);
            hearts.Place(Dt, Dt);
        }
        Check(tag + ": the ship is full size again", Mathf.Abs(ship.transform.localScale.x - scale) < scale * .01f);
        Check(tag + ": " + want + " hearts built (" + hearts.Hearts.Length + ")", hearts.Hearts.Length == want);

        float lo = Lo(hearts.heartSize), hi = Hi(hearts.heartSize);
        float worst = 0f;
        foreach (var h in hearts.Hearts) worst = Mathf.Max(worst, WorldSize(h));
        float least = float.MaxValue;
        foreach (var h in hearts.Hearts) least = Mathf.Min(least, WorldSize(h));
        Check(tag + ": each heart " + least.ToString("F3") + ".." + worst.ToString("F3") + " u, expected " + lo.ToString("F3") + ".." + hi.ToString("F3"),
              least >= lo && worst <= hi);

        // the orbit: round the hull, no wider than a few hull widths
        hearts.Measure();
        var hull = ShipUiSlots.HullBounds(ship.transform, id);
        Vector2 r = hearts.OrbitRadii;
        float maxExt = Mathf.Max(hull.extents.x, hull.extents.y);
        Check(tag + ": orbit radii " + r.x.ToString("F2") + " x " + r.y.ToString("F2") + " u outside the hull (" + hull.extents.x.ToString("F2") + " x " + hull.extents.y.ToString("F2") + ")",
              r.x > hull.extents.x && r.y > hull.extents.y && Mathf.Max(r.x, r.y) < maxExt * 2.2f + hearts.heartSize * 2f);
        float far = 0f;
        foreach (var h in hearts.Hearts) far = Mathf.Max(far, ((Vector2)(h.position - ship.transform.position)).magnitude);
        Check(tag + ": the farthest heart is " + far.ToString("F2") + " u from the ship", far < Mathf.Max(r.x, r.y) * 1.3f + hearts.heartSize);

        // on every FitDevice: on-screen pixels of a heart
        foreach (var d in FitDevice.All)
        {
            float halfW = CameraFit.GameplayViewHalfWidth(new Vector2(d.w, d.h));
            float pxPerUnit = d.w / (2f * halfW);
            float px = worst * pxPerUnit, pxMax = hi * pxPerUnit;
            float share = px / d.w;
            if (px > pxMax || share >= .09f || share <= .02f)
                Check(tag + " on " + d.id + ": heart " + px.ToString("F0") + " px = " + (share * 100f).ToString("F1") + "% of the width", false);
        }
        Cleanup();
    }

    // The tutorial's bare hull at the normalised size (no 1.35x), no entry.
    static void TutorialHull()
    {
        Cleanup();
        collisionDetection.MAXLIFE = ShipLives.TutorialMax(1);
        var ship = new GameObject(ShipId.ObjectName(1), typeof(SpriteRenderer));
        var sr = ship.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(1);
        float scale = shopingShips.NormalizedHullScale(sr.sprite);
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        hearts.SendMessage("Start");
        for (int f = 0; f < 60; f++) hearts.Place(Dt, Dt);
        float worst = 0f;
        foreach (var h in hearts.Hearts) worst = Mathf.Max(worst, WorldSize(h));
        Check("tutorial hull: hearts at most " + worst.ToString("F3") + " u", worst <= Hi(hearts.heartSize));
        Cleanup();
    }
}
