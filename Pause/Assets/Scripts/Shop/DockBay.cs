using UnityEngine;

// One berth of the space dock and the ship parked in it.
//
// A parked ship is powered down: greyed out, engine cold, no idle motion,
// berth lights at a low amber standby. Selecting it powers it up -- colour
// warms back in, the bay lights come up cyan, the landing ring glows, the
// engine lights and the hull starts a gentle hover. The component disables
// itself once a berth has settled powered-down, so fourteen idle berths cost
// nothing per frame.
public class DockBay : MonoBehaviour
{
    // Sorting orders, back to front.
    public const int OrderBay = 2, OrderRing = 4, OrderShadow = 5, OrderClamp = 6,
                     OrderLight = 7, OrderLabel = 8, OrderShip = 11, OrderLaunch = 40;

    public static readonly Vector3 ShipRest = new Vector3(0f, .10f, 0f);
    public const float HullSize = .64f;      // longest edge of every parked hull
    const float PowerTime = .38f;

    static readonly Vector3[] LightSpots =
    {
        new Vector3(-.69f, .56f), new Vector3(.69f, .56f),
        new Vector3(-.69f, -.36f), new Vector3(.69f, -.36f),
    };
    static readonly Color OffTint = new Color(.62f, .68f, .80f, 1f);
    static readonly Color LightOff = AkiraPalette.WithAlpha(AkiraPalette.Sodium, .38f);
    static readonly Color LightOn = AkiraPalette.Cyan;
    static readonly Color NameOwned = AkiraPalette.WithAlpha(AkiraPalette.Bone, .95f);
    static readonly Color NameLocked = AkiraPalette.WithAlpha(AkiraPalette.Muted, .9f);
    static int saturationId;

    public int index;
    public Transform ship;
    public SpriteRenderer hull;
    public ShipThruster thruster;
    public ShipSpinDrift drift;             // spinners' engine instead of a thruster
    public bool wind;
    public Vector2 hullHalfSize;            // in berth-local units

    public bool Powered { get; private set; }
    public float Power { get { return power; } }
    public bool Launching { get; set; }
    public bool Owned { get; private set; }
    public bool Equipped { get; private set; }

    SpriteRenderer bayRenderer, ring, shadow, statusIcon, chip, chipDust;
    readonly SpriteRenderer[] lights = new SpriteRenderer[4];
    readonly Transform[] clamps = new Transform[2];
    TextMesh nameText, chipText;
    readonly Sprite[] idle = new Sprite[ShipHullArt.IdleDrawings];
    Sprite rest;
    MaterialPropertyBlock block;
    float power;
    float clampOpen;
    float spin;
    float phase;

    public static DockBay Create(Transform parent, int index, Font font)
    {
        var root = new GameObject("~DockBay" + index);
        root.transform.SetParent(parent, false);
        var bay = root.AddComponent<DockBay>();
        bay.Build(index, font);
        return bay;
    }

    void Build(int i, Font font)
    {
        index = i;
        phase = i * 1.37f;
        wind = ShipExhaust.UsesWind(i);
        if (saturationId == 0) saturationId = Shader.PropertyToID("_Saturation");
        block = new MaterialPropertyBlock();

        bayRenderer = MakeSprite("Berth", DockArt.Get("bay"), Vector3.zero, OrderBay);
        ring = MakeSprite("Ring", DockArt.Get("ring"), ShipRest, OrderRing);
        shadow = MakeSprite("Shadow", DockArt.Get("shadow"), ShipRest + new Vector3(.05f, -.06f), OrderShadow);
        for (int k = 0; k < lights.Length; k++)
        {
            lights[k] = MakeSprite("Light" + k, DockArt.Get("light"), LightSpots[k], OrderLight);
            lights[k].transform.localScale = Vector3.one * .9f;
        }
        for (int k = 0; k < 2; k++)
        {
            var clamp = MakeSprite("Clamp" + k, DockArt.Get("clamp"), Vector3.zero, OrderClamp);
            clamp.flipX = k == 1;
            clamps[k] = clamp.transform;
        }

        // The ship itself. Named "ship<N>" so the rest of the game (and the
        // tests) find it the way they always have.
        var shipGo = new GameObject("ship" + i);
        ship = shipGo.transform;
        ship.SetParent(transform, false);
        ship.localPosition = ShipRest;
        hull = shipGo.AddComponent<SpriteRenderer>();
        LoadSkin(ShipSkins.Shown(i));
        hull.sprite = rest;
        hull.sortingOrder = OrderShip;
        var material = DockArt.ShipMaterial;
        if (material != null) hull.sharedMaterial = material;
        float extent = rest != null ? Mathf.Max(rest.bounds.size.x, rest.bounds.size.y) : 1f;
        float k2 = extent > 0f ? HullSize / extent : 1f;
        ship.localScale = new Vector3(k2, k2, 1f);
        hullHalfSize = rest != null ? (Vector2)rest.bounds.extents * k2 : new Vector2(.3f, .3f);
        shadow.transform.localScale = new Vector3(hullHalfSize.x * 2.4f / .64f, hullHalfSize.y * 2.2f / .64f, 1f);

        // Engine: the same mounts the flight flame uses, cold until selected.
        ShipExhaust.ConfigureBoost(shipGo, i);
        if (!wind)
        {
            thruster = shipGo.AddComponent<ShipThruster>();
            thruster.respondToPause = false;
            thruster.idleScale = .55f;
            thruster.flicker = .12f;
            thruster.powered = false;
        }
        else
        {
            // Spinners: their spin drift, turned by this berth's own spin.
            drift = shipGo.AddComponent<ShipSpinDrift>();
            drift.spinHull = false;
            drift.respondToPause = false;
            drift.powered = false;
            drift.wakeScale = .5f;    // keep the wake inside the berth
        }

        // Name plate, set into the recess along the berth's back wall.
        nameText = MakeText("Name", font, new Vector3(0f, -.553f, 0f), .078f, TextAnchor.MiddleCenter);
        nameText.text = (shopingShips.NameFor(i) ?? "").ToUpperInvariant();

        // Status corner: a check (owned), a chevron (equipped) or a price tag.
        statusIcon = MakeSprite("Status", DockArt.Get("icon_owned"), new Vector3(.47f, .42f), OrderLabel);
        statusIcon.transform.localScale = Vector3.one * .62f;
        chip = MakeSprite("PriceTag", DockArt.Get("chip", 6f), new Vector3(.3f, .43f), OrderLabel);
        chip.drawMode = SpriteDrawMode.Sliced;
        chipDust = MakeSprite("Dust", DockArt.Get("icon_dust"), Vector3.zero, OrderLabel + 1);
        chipDust.transform.SetParent(chip.transform, false);
        chipDust.transform.localScale = Vector3.one * .48f;
        chipText = MakeText("Price", font, Vector3.zero, .07f, TextAnchor.MiddleRight);
        chipText.transform.SetParent(chip.transform, false);
        chipText.color = DockArt.Gold;

        SetClampOpen(0f);
        ApplyPower(0f);
        enabled = false;
    }

    SpriteRenderer MakeSprite(string name, Sprite sprite, Vector3 local, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    TextMesh MakeText(string name, Font font, Vector3 local, float height, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var text = go.AddComponent<TextMesh>();
        text.font = font;
        text.fontSize = 64;
        text.fontStyle = FontStyle.Normal;
        // TextMesh lines are fontSize * characterSize / 10 units tall.
        text.characterSize = height * 10f / 64f;
        text.anchor = anchor;
        text.alignment = TextAlignment.Center;
        var renderer = go.GetComponent<MeshRenderer>();
        if (font != null) renderer.sharedMaterial = font.material;
        renderer.sortingOrder = OrderLabel + 1;
        return text;
    }

    // The hull skin this berth shows (equipped, or the one being previewed).
    public int Skin { get; private set; }

    void LoadSkin(int skin)
    {
        Skin = skin;
        rest = ShipHullArt.Get(index, skin, 0, 0);
        for (int f = 0; f < idle.Length; f++)
            idle[f] = ShipHullArt.Get(index, skin, 0, f) ?? rest;
    }

    // Repaints the parked hull in `skin` (same shape, so nothing else moves).
    // A powered berth keeps its idle animation running in the new colours.
    public void ShowSkin(int skin)
    {
        if (skin == Skin && rest != null) return;
        LoadSkin(skin);
        if (hull == null || Launching) return;
        if (Power > .05f && !Launching)
        {
            int drawing = ShipHullArt.IdleDrawingAt(Time.unscaledTime * ShipHullArt.TicksPerSecond + phase * 10f);
            hull.sprite = idle[Mathf.Clamp(drawing, 0, idle.Length - 1)];
        }
        else hull.sprite = rest;
    }

    // Owned / equipped / price, from the same PlayerPrefs keys as always.
    public void RefreshStatus(bool owned, bool equipped, float price)
    {
        Owned = owned;
        Equipped = equipped;
        nameText.color = equipped ? DockArt.Cyan : owned ? NameOwned : NameLocked;
        statusIcon.enabled = owned;
        statusIcon.sprite = DockArt.Get(equipped ? "icon_active" : "icon_owned");
        statusIcon.color = equipped ? Color.white : new Color(1f, 1f, 1f, .85f);
        bool locked = !owned;
        chip.gameObject.SetActive(locked);
        if (locked)
        {
            string label = Mathf.RoundToInt(price).ToString("N0");
            chipText.text = label;
            float width = .25f + label.Length * .058f;
            chip.size = new Vector2(width, .15f);
            float right = .555f;
            chip.transform.localPosition = new Vector3(right - width * .5f, .43f, 0f);
            chipDust.transform.localPosition = new Vector3(-width * .5f + .08f, 0f, 0f);
            chipText.transform.localPosition = new Vector3(width * .5f - .045f, -.002f, 0f);
        }
    }

    public void SetPowered(bool on)
    {
        if (Powered == on && enabled) return;
        Powered = on;
        enabled = true;
    }

    // 0 = clamps gripping the hull, 1 = retracted into the berth walls.
    public void SetClampOpen(float open)
    {
        clampOpen = Mathf.Clamp01(open);
        float grip = hullHalfSize.x + .005f;
        float retract = .14f * clampOpen;
        // The clamp sprite is .40 wide; its jaw is its inner edge.
        clamps[0].localPosition = new Vector3(-(grip + .20f + retract), ShipRest.y, 0f);
        clamps[1].localPosition = new Vector3(grip + .20f + retract, ShipRest.y, 0f);
    }

    public void SetShadow(float altitude, float alpha)
    {
        shadow.transform.localPosition = ship.localPosition + new Vector3(.05f + altitude * .5f, -.06f - altitude, 0f);
        shadow.color = new Color(1f, 1f, 1f, alpha);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float target = Powered || Launching ? 1f : 0f;
        power = Mathf.MoveTowards(power, target, dt / PowerTime);
        float p = DockTween.InOutCubic(power);
        ApplyPower(p);

        if (!Launching)
        {
            float t = Time.unscaledTime;
            float bob = Mathf.Sin(t * 2.4f + phase) * .022f * p;
            ship.localPosition = new Vector3(ShipRest.x, ShipRest.y + bob, 0f);
            // The hull's own idle flipbook (lights blink, canopy glint), on
            // the same tick table it flies with; a parked hull holds still.
            int drawing = ShipHullArt.IdleDrawingAt(t * ShipHullArt.TicksPerSecond + phase * 10f);
            hull.sprite = p > .05f ? idle[Mathf.Clamp(drawing, 0, idle.Length - 1)] : rest;
            if (wind)
            {
                spin += 80f * p * dt;
                ship.localRotation = Quaternion.Euler(0f, 0f, spin);
            }
            SetShadow(bob * .6f, .55f);
        }

        if (!Powered && !Launching && power <= 0f)
        {
            if (wind) { spin = 0f; ship.localRotation = Quaternion.identity; }
            ApplyPower(0f);
            enabled = false;
        }
    }

    void ApplyPower(float p)
    {
        if (hull != null)
        {
            hull.color = Color.Lerp(OffTint, Color.white, p);
            hull.GetPropertyBlock(block);
            block.SetFloat(saturationId, Mathf.Lerp(.22f, 1f, p));
            hull.SetPropertyBlock(block);
        }
        float t = Time.unscaledTime;
        float pulse = p > 0f ? .5f + .5f * Mathf.Sin(t * 3.2f + phase) : 0f;
        Color light = Color.Lerp(LightOff, LightOn, p);
        light.a *= 1f - .18f * pulse * p;
        float lightScale = Mathf.Lerp(.55f, 1.05f, p);
        for (int k = 0; k < lights.Length; k++)
        {
            lights[k].color = light;
            lights[k].transform.localScale = new Vector3(lightScale, lightScale, 1f);
        }
        ring.color = new Color(DockArt.Cyan.r, DockArt.Cyan.g, DockArt.Cyan.b, p * (.5f + .25f * pulse));
        float rs = Mathf.Lerp(.86f, 1f, p);
        ring.transform.localScale = new Vector3(rs, rs, 1f);
        if (thruster != null) thruster.powered = Launching || (Powered && power > .3f);
        if (drift != null)
        {
            drift.powered = Launching || (Powered && power > .3f);
            drift.boost = Launching;
        }
    }

    // Jumps straight to the current power target (no fade). Used when the
    // dock is rebuilt and by the edit-mode tests, where no time passes.
    public void SnapPower()
    {
        power = Powered || Launching ? 1f : 0f;
        ApplyPower(power);
    }

    public string NameLabel { get { return nameText.text; } }
    public bool ShowsStatusIcon { get { return statusIcon.enabled; } }
    public bool ShowsPrice { get { return chip.gameObject.activeSelf; } }
    public string PriceLabel { get { return chipText.text; } }
    public float LightLevel { get { return lights[0].color.a; } }

    public bool Contains(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return Mathf.Abs(local.x) <= DockLayout.BaySize.x * .5f &&
               Mathf.Abs(local.y) <= DockLayout.BaySize.y * .5f;
    }

}
