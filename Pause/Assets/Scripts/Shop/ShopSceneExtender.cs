using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Fills in the shop objects the scene does not have.
//
// shopS6 was authored for a four-ship roster, so it only contains ship1-4 and
// face/return markers for the first three. Rather than hand-authoring the rest
// into the scene, the missing pieces are built here from the same grid the
// authored ones already sit on -- so growing the roster stays a code change.
//
// Runs before shopingShips.Start() via a lower script execution order is not
// required: it runs from sceneLoaded, which fires before any Start().
public class ShopSceneExtender : MonoBehaviour
{
    // Matches the authored layout exactly:
    //   ship1 (-0.5, 3)  ship2 (0.5, 3)
    //   ship3 (-0.5, 2)  ship4 (0.5, 2)   ...
    // face markers sit one unit further out than their ship.
    // Camera is orthographic size 5, so the view is y in [-5, 5] and about
    // x in [-2.8, 2.8] on a portrait phone. Seven ships sit in two columns.
    const float ColumnX = 1.08f;
    const float FaceX = 2.42f;
    const float TopY = 2.80f;
    const float RowStep = 1.34f;

    // Authored previews are hand-scaled so every hull ends up roughly this tall
    // in world units, regardless of how large its source texture is -- Darkwing
    // is 15401px wide and sits at scale 0.03, Proteus is 400px at scale 0.60.
    // Generated ships are normalised to the same visual size instead of being
    // left at scale 1, which is why they towered over the rest.
    const float TargetHullHeight = 0.58f;

    public static Vector3 ShipSlot(int i)
    {
        int col = (i - 1) % 2, row = (i - 1) / 2;
        return new Vector3(col == 0 ? -ColumnX : ColumnX, TopY - row * RowStep, 0f);
    }

    public static Vector3 FaceSlot(int i)
    {
        int col = (i - 1) % 2;
        var p = ShipSlot(i);
        return new Vector3(col == 0 ? -FaceX : FaceX, p.y, 0f);
    }

    public static void Build()
    {
        int total = shopingShips.shipTotal;

        var canvas = SceneUtil.FindAny("Canvas");
        var templateButton = SceneUtil.FindAny("Button3");

        // This canvas was authored inactive, which made the only currency
        // readout disappear even though shopingShips was correctly updating it.
        var dustCanvas = SceneUtil.FindAny("StarDustCanvas");
        if (dustCanvas != null) dustCanvas.SetActive(true);

        for (int i = 1; i < total; i++)
        {
            EnsureMarker("face" + i, FaceSlot(i));
            EnsureMarker("return" + i, ShipSlot(i));
            EnsureShip(i);
            EnsureButton(i, canvas, templateButton);
            EnsureDockBay(i);
        }

        // Launch lanes must extend beyond the dock, rather than steering a
        // selected ship back toward the middle before it exits the screen.
        EnsureMarker("LiftOffLeft", new Vector3(-FaceX, 6.6f, 0f));
        EnsureMarker("LiftOffRight", new Vector3(FaceX, 6.6f, 0f));
    }

    // face/return are pure position markers; rotateRight only reads .position.
    static void EnsureMarker(string name, Vector3 pos)
    {
        var go = SceneUtil.FindAny(name);
        if (go == null) go = new GameObject(name);
        var rect = go.transform as RectTransform;
        if (rect != null) rect.anchoredPosition = new Vector2(pos.x, pos.y);
        else go.transform.position = pos;
    }

    static void EnsureShip(int index)
    {
        var go = SceneUtil.FindAny("ship" + index);
        if (go == null)
        {
            go = new GameObject("ship" + index);
            go.AddComponent<SpriteRenderer>();
        }

        go.transform.position = ShipSlot(index);

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();

        // Always use the undamaged frame -- some authored previews pointed at a
        // battle-scarred one.
        var sprite = LoadShipSprite(index);
        if (sprite != null) sr.sprite = sprite;
        sr.sortingOrder = 5;

        // Every hull is normalised now. The authored scales were hand-tuned for
        // a four-ship dock and are much too large once seven have to fit.
        NormaliseScale(go, sr);

        EnsureBoost(go, index);

        // Parked ships idle rather than hanging dead in space.
        if (go.GetComponent<ShipThruster>() == null)
        {
            var thruster = go.AddComponent<ShipThruster>();
            thruster.respondToPause = false;
            thruster.idleScale = 0.26f;
            thruster.flicker = 0.14f;
        }
    }

    static void NormaliseScale(GameObject go, SpriteRenderer sr)
    {
        if (sr.sprite == null) return;
        float h = sr.sprite.bounds.size.y;
        if (h <= 0.0001f) return;
        float k = TargetHullHeight / h;
        go.transform.localScale = new Vector3(k, k, 1f);
    }

    // First damage frame of the ship's sheet, matching what lifeControler loads.
    static Sprite LoadShipSprite(int index)
    {
        string name = shopingShips.NameFor(index);
        if (string.IsNullOrEmpty(name)) return null;

        var frames = Resources.LoadAll<Sprite>("Prefabs/Ships/Sprites/" + name);
        return (frames != null && frames.Length > 0) ? frames[0] : null;
    }

    // rotateRight looks these up by the exact name "Boost<N>", so the generated
    // ones must match or the lift-off flare silently never appears.
    static void EnsureBoost(GameObject ship, int index)
    {
        foreach (Transform c in ship.transform)
            if (c.name.StartsWith("Boost"))
            {
                PopulateBoost(c.gameObject);
                return;
            }

        var boost = new GameObject("Boost" + index);
        boost.transform.SetParent(ship.transform, false);
        boost.transform.localPosition = new Vector3(0f, -0.48f, 0f);
        PopulateBoost(boost);
        boost.SetActive(false);
    }

    static void PopulateBoost(GameObject boost)
    {
        boost.tag = "boost";
        var sr = boost.GetComponent<SpriteRenderer>();
        if (sr == null) sr = boost.AddComponent<SpriteRenderer>();
        if (sr.sprite == null)
            sr.sprite = Resources.Load<Sprite>("Prefabs/Vfx/vfx_flare_01");
        sr.sortingOrder = 4;
        boost.transform.localScale = Vector3.one * 0.30f;
    }

    // The old shop was a bare starfield. These bay plates and neon guide rails
    // give every parked ship a place in the hangar and make the two launch
    // directions instantly legible without requiring scene art edits.
    static readonly Color DockPlate = new Color(0.035f, 0.06f, 0.11f, 0.86f);
    static readonly Color DockCyan = new Color(0.18f, 0.88f, 1f, 0.78f);
    static readonly Color DockPink = new Color(1f, 0.22f, 0.65f, 0.68f);

    static void EnsureDockBay(int index)
    {
        var root = SceneUtil.FindAny("~DockBay" + index);
        if (root == null) root = new GameObject("~DockBay" + index);
        root.transform.position = ShipSlot(index) + new Vector3(0f, -0.04f, 0.2f);

        EnsureDockPiece(root.transform, "Plate", Vector3.zero,
                        new Vector2(2.22f, 0.86f), DockPlate, 1);
        EnsureDockPiece(root.transform, "TopRail", new Vector3(0f, 0.41f),
                        new Vector2(2.16f, 0.035f), DockCyan, 2);
        EnsureDockPiece(root.transform, "BottomRail", new Vector3(0f, -0.41f),
                        new Vector2(2.16f, 0.035f), DockPink, 2);
        EnsureDockPiece(root.transform, "CenterGuide", new Vector3(0f, -0.27f),
                        new Vector2(0.42f, 0.025f), DockCyan, 2);
    }

    static void EnsureDockPiece(Transform parent, string name, Vector3 localPos,
                                Vector2 size, Color color, int order)
    {
        var t = parent.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name);
        if (t == null) go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();
        if (sr.sprite == null)
            sr.sprite = Sprite.Create(Texture2D.whiteTexture,
                                      new Rect(0, 0, 1, 1),
                                      new Vector2(0.5f, 0.5f), 1f);
        sr.color = color;
        sr.sortingOrder = order;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    static void EnsureButton(int index, GameObject canvas, GameObject template)
    {
        var go = SceneUtil.FindAny("Button" + index);

        if (go == null)
        {
            if (canvas == null || template == null) return;
            go = Instantiate(template, template.transform.parent);
            go.name = "Button" + index;
            // shipselected() keys off the button's name, so the cloned handler
            // is already correct -- nothing else to wire.
        }

        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            // The old 44px plate lived below the hull. It looked like a ship
            // button but did not let the player tap the ship, which made the
            // dock feel broken on a phone. The hit area now covers the hull,
            // its idle flame and the price label together.
            rt.sizeDelta = new Vector2(230f, 148f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // Keep the hit box invisible: the bay frame is the visual affordance,
        // not a second oversized UI card on top of the ship art.
        var img = go.GetComponent<Image>();
        if (img != null)
        {
            img.color = new Color(0.07f, 0.09f, 0.14f, 0.01f);
            img.raycastTarget = true;
        }

        EnsureLabel(go);

        // Keep each button pinned beneath its ship at runtime, where the real
        // screen size is known -- the world grid and the canvas do not share a
        // coordinate space, so this cannot be baked in.
        var aligner = go.GetComponent<ShopButtonAligner>();
        if (aligner == null) aligner = go.AddComponent<ShopButtonAligner>();
        aligner.shipIndex = index;
    }

    // Buttons cloned from the scene came without the authored "Select" child,
    // so several had no text at all and showed neither name nor price.
    static void EnsureLabel(GameObject button)
    {
        var text = button.GetComponentInChildren<Text>(true);
        if (text == null)
        {
            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(button.transform, false);
            text = labelGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        }

        // Best-fit blew the labels up to fill the whole plate; a fixed size
        // keeps every button reading the same.
        text.resizeTextForBestFit = false;
        text.fontSize = 22;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        // Text stays visibly below the hull even though its parent hit box is
        // centred on the hull. This separates presentation from touch target.
        var rt = text.transform as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 7f);
            rt.sizeDelta = new Vector2(0f, 34f);
        }
    }


    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "shopS6") return;
        ShopSceneExtender.Build();
    }
}

// The method above was left orphaned during the dock refactor: Unity never
// called it, so the scene remained its old four-ship shell. Register once at
// process start and build before the scene's Start() methods query the ships.
public static class ShopSceneBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Also support opening shopS6 directly in the editor or as a build's
        // first scene; AfterSceneLoad registration would otherwise miss it.
        if (SceneManager.GetActiveScene().name == "shopS6")
            ShopSceneExtender.Build();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "shopS6") ShopSceneExtender.Build();
    }
}
