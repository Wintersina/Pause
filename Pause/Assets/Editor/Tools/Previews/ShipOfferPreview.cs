using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Renders the death-screen ship offer (ShipOfferCard) on a 1080x2400 phone in
// gameS1's PopUpCanvas: the blacked-out offer, the reveal half way, and the
// revealed ship after BUY.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipOfferPreview.Run
//   (writes to $SHIP_OFFER_PREVIEW_DIR, else Builds/ShipOfferPreview)
public static class ShipOfferPreview
{
    static readonly Vector2Int Size = new Vector2Int(1080, 2400);

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("SHIP_OFFER_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ShipOfferPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            EditorSceneLoader.Open("gameS1");
            DeveloperUnlocks.SetEnabled(false);
            for (int id = 2; id < shopingShips.RosterTotal; id++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(id));
            PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
            for (int n = 1; n < ShipSkins.CountFor(1); n++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(1, n), 1);
            ShipId.Equip(1);
            int next = ShipOfferStore.NextShip(1);
            float price = shopingShips.CostFor(next);
            PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, price + 1234f);
            var offer = new ShipOfferRules.Offer { shipId = next, flownId = 1, price = price };

            var canvasGo = SceneUtil.FindAny("PopUpCanvas");
            var dead = Object.FindFirstObjectByType<playerIsDead>(FindObjectsInactive.Include);
            Font font = dead != null && dead.deathHighScoreText != null ? dead.deathHighScoreText.font
                                                                        : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // A root canvas of its own with the PopUpCanvas's scaler settings (the
            // scene's PopUpCanvas sits under other objects in edit mode).
            var src = canvasGo != null ? canvasGo.GetComponent<CanvasScaler>() : null;
            var rootGo = new GameObject("~ShipOfferPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            rootGo.layer = 5;
            var rootScaler = rootGo.GetComponent<CanvasScaler>();
            rootScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            rootScaler.referenceResolution = src != null ? src.referenceResolution : new Vector2(800f, 1280f);
            rootScaler.screenMatchMode = src != null ? src.screenMatchMode : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            rootScaler.matchWidthOrHeight = src != null ? src.matchWidthOrHeight : .5f;
            Debug.Log("[SHIPOFFERPREVIEW] popup canvas " + (canvasGo != null) + " ref " + rootScaler.referenceResolution + " match " + rootScaler.matchWidthOrHeight + " " + rootScaler.screenMatchMode);
            rootGo.GetComponent<Canvas>().sortingOrder = 500;
            canvasGo = rootGo;
            var card = ShipOfferCard.Show(canvasGo.transform, font, offer, price + 1234f, b => { });
            card.Tick(card.ShownAt + 1f);
            Shot(canvasGo.GetComponent<Canvas>(), Path.Combine(dir, "1-offer.png"));
            card.BuyButton.onClick.Invoke();
            card.Tick(card.RevealStartedAt + ShipOfferRules.RevealSeconds * .5f);
            Shot(canvasGo.GetComponent<Canvas>(), Path.Combine(dir, "2-reveal-half.png"));
            card.Tick(card.RevealStartedAt + ShipOfferRules.RevealSeconds);
            Shot(canvasGo.GetComponent<Canvas>(), Path.Combine(dir, "3-revealed.png"));
        }
        EditorApplication.Exit(0);
    }

    static void Shot(Canvas canvas, string path)
    {
        var camera = Camera.main;
        var target = new RenderTexture(Size.x, Size.y, 24);
        camera.aspect = Size.x / (float)Size.y;
        camera.targetTexture = target;
        camera.cullingMask = -1;
        var scaler = canvas.GetComponent<CanvasScaler>();
        float scale = 1f;
        if (scaler != null)
        {
            Vector2 r = scaler.referenceResolution;
            float lw = Mathf.Log(Size.x / r.x, 2f), lh = Mathf.Log(Size.y / r.y, 2f);
            scale = Mathf.Pow(2f, Mathf.Lerp(lw, lh, scaler.matchWidthOrHeight));
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) scale = Mathf.Min(Size.x / r.x, Size.y / r.y);
            scaler.enabled = false;
        }
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = camera.nearClipPlane + .5f;
        canvas.scaleFactor = scale;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(Size.x, Size.y, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Size.x, Size.y), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(path, png.EncodeToPNG());
        camera.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(target);
        Debug.Log("[SHIPOFFERPREVIEW] " + path + " scale " + scale);
    }
}
