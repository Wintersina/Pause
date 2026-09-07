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
    const float TargetHullHeight = 1.05f;

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

    // The source sprites point up. Parked ships face out toward the nearest
    // hangar door, then rotate back to this natural (up) heading for launch.
    public static float DockAngle(int index)
    {
        return index % 2 == 0 ? -90f : 90f;
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
        EnsureInstruction(canvas);

        for (int i = 1; i < total; i++)
        {
            EnsureMarker("face" + i, FaceSlot(i));
            EnsureMarker("return" + i, ShipSlot(i));
            EnsureShip(i);
            EnsureButton(i, canvas, templateButton);
            EnsureDockBay(i);
        }

        // The authored scene still has its retired seven-ship layout. Only
        // the three finished 80s ships belong in this dock.
        for (int i = total; i < 12; i++)
        {
            SetActiveIfFound("ship" + i, false);
            SetActiveIfFound("Button" + i, false);
            SetActiveIfFound("~DockBay" + i, false);
        }

        // Launch lanes must extend beyond the dock, rather than steering a
        // selected ship back toward the middle before it exits the screen.
        EnsureMarker("LiftOffLeft", new Vector3(-FaceX, 6.6f, 0f));
        EnsureMarker("LiftOffRight", new Vector3(FaceX, 6.6f, 0f));
        DockScrollView.Build(canvas);
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

    static void SetActiveIfFound(string name, bool active)
    {
        var go = SceneUtil.FindAny(name);
        if (go != null) go.SetActive(active);
    }

    static void EnsureShip(int index)
    {
        var go = SceneUtil.FindAny("ship" + index);
        if (go == null)
        {
            go = new GameObject("ship" + index);
            go.AddComponent<SpriteRenderer>();
        }
        go.SetActive(true);

        go.transform.position = ShipSlot(index);

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();

        // Always use the undamaged frame -- some authored previews pointed at a
        // battle-scarred one.
        var sprite = LoadShipSprite(index);
        if (sprite != null) sr.sprite = sprite;
        sr.sortingOrder = 5;

        // Legacy scene hulls have an old UI Image on top of their runtime
        // SpriteRenderer. Disable it so the intended 80s sprite can show.
        foreach (var image in go.GetComponentsInChildren<Image>(true))
            image.enabled = false;

        // Every hull is normalised now. The authored scales were hand-tuned for
        // a four-ship dock and are much too large once seven have to fit.
        NormaliseScale(go, sr);
        go.transform.rotation = Quaternion.Euler(0f, 0f, DockAngle(index));

        var idleAnimator = go.GetComponent<DockShipIdleAnimator>();
        if (idleAnimator == null) idleAnimator = go.AddComponent<DockShipIdleAnimator>();
        idleAnimator.shipIndex = index;

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
        return shopingShips.SpriteFor(index, 0);
    }

    // rotateRight looks these up by the exact name "Boost<N>", so the generated
    // ones must match or the lift-off flare silently never appears.
    static void EnsureBoost(GameObject ship, int index)
    {
        ShipExhaust.ConfigureBoost(ship, index);
    }

    // A restrained berth, deliberately without the old stack of horizontal
    // neon rules. The outward-facing ship is now the visual focus.
    static readonly Color DockPlate = new Color(0.035f, 0.06f, 0.11f, 0.86f);
    static readonly Color DockCyan = new Color(0.18f, 0.88f, 1f, 0.78f);
    static readonly Color DockPink = new Color(1f, 0.22f, 0.65f, 0.68f);

    static void EnsureDockBay(int index)
    {
        var root = SceneUtil.FindAny("~DockBay" + index);
        if (root == null) root = new GameObject("~DockBay" + index);
        root.SetActive(true);
        root.transform.position = ShipSlot(index) + new Vector3(0f, -0.04f, 0.2f);

        EnsureDockPiece(root.transform, "Plate", Vector3.zero,
                        new Vector2(1.88f, 0.74f), DockPlate, 1);
        EnsureDockPiece(root.transform, "DoorGlow", new Vector3(index % 2 == 0 ? 0.86f : -0.86f, 0f),
                        new Vector2(0.035f, 0.58f), index % 2 == 0 ? DockPink : DockCyan, 2);

        // Retire guide pieces made by an earlier dock layout if this scene was
        // rebuilt while open in the editor.
        RemoveDockPiece(root.transform, "TopRail");
        RemoveDockPiece(root.transform, "BottomRail");
        RemoveDockPiece(root.transform, "CenterGuide");
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

    static void RemoveDockPiece(Transform parent, string name)
    {
        var old = parent.Find(name);
        if (old != null) Object.Destroy(old.gameObject);
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
        go.SetActive(true);

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

    static void EnsureInstruction(GameObject canvas)
    {
        // The authored scene contains two copies of the old, too-wide hint.
        // On portrait screens they split across both edges of the display.
        foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (text == null || !text.gameObject.scene.IsValid()) continue;
            if (text.text != null && text.text.Contains("Touch any of the ships"))
                text.gameObject.SetActive(false);
        }

        if (canvas == null) return;
        var go = SceneUtil.FindAny("~DockInstruction");
        if (go == null)
        {
            go = new GameObject("~DockInstruction", typeof(Text));
            go.transform.SetParent(canvas.transform, false);
        }
        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = "TOUCH A SHIP TO SELECT OR BUY";
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.82f, 0.92f, 1f, 0.88f);
        label.raycastTarget = false;
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -88f);
        rt.sizeDelta = new Vector2(520f, 34f);
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
