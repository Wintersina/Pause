using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// The ship with 1, 2 and 3 upgraded hearts as a run starts it: the hearts
// are built while the portal arrival still has the ship tiny, then the ship
// flies out and the hearts orbit it (UpgradeHeartsSizeTest's case).
//   Unity -batchmode -quit -projectPath Pause -executeMethod UpgradeHeartsPreview.Run
//   (writes hearts_<tag>_<ship>_L<level>.png into $UPGRADE_HEARTS_DIR; tag = $UPGRADE_HEARTS_TAG, default "now")
public static class UpgradeHeartsPreview
{
    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("UPGRADE_HEARTS_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/UpgradeHeartsPreview";
        string tag = System.Environment.GetEnvironmentVariable("UPGRADE_HEARTS_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "now";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            foreach (int id in new[] { 1, 7 })
                for (int level = 1; level <= 3; level++)
                    One(Path.Combine(dir, "hearts_" + tag + "_" + ShipId.KeyOf(id) + "_L" + level + ".png"), id, level);
            WorldEntry.Cancel();
        }
        UnityEditor.EditorApplication.Exit(0);
    }

    static void One(string path, int id, int level)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        PlayerPrefs.SetString("boughtship" + id, "True");
        PlayerPrefs.SetInt("spawnShip", id);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000000f);
        for (int n = 1; n <= level; n++) ShipSkins.TryPurchase(id, n);
        collisionDetection.lifeCounter = 0;
        collisionDetection.MAXLIFE = ShipLives.Max(id);
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-3.72f, -6.6f, 3.72f, 6.6f);

        // a gameplay camera: 3.72 half width on a 1080x1920 phone
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .06f, .12f);
        cam.aspect = 1080f / 1920f;
        cam.orthographicSize = 3.72f * 1920f / 1080f;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        var ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        var sr = ship.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(id);
        sr.sortingOrder = 10;
        float scale = shopingShips.NormalizedHullScale(sr.sprite) * ShipScale.Main;
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        ship.transform.position = new Vector3(0f, -2f, 1f);
        PortalArrival.Spawn(ship.transform, Color.cyan);
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        hearts.SendMessage("Start");
        for (int f = 0; f < 240; f++)
        {
            var a = PortalArrival.Live;
            if (a != null && a.State != PortalArrival.Stage.Done) a.Step(1f / 60f);
            hearts.Place(1f / 60f, 1f / 60f);
        }
        // crop on the ship: the full-width view, the ship's neighbourhood
        cam.transform.position = new Vector3(0f, ship.transform.position.y, -10f);
        cam.orthographicSize = 3.72f * 1920f / 1080f * .5f;
        Shoot(cam, path, 540, 960);
        WorldEntry.Cancel();
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(ship);
    }

    static void Shoot(Camera cam, string path, int px, int py)
    {
        var rt = new RenderTexture(px, py, 24);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(px, py, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, px, py), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
