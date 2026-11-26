using UnityEngine;

// One bay of the space parking lot and the ship parked in it. The bay root is never rotated (labels stay upright);
// its "Orient" child holds the floor, wall, highlight, shadow and ship and is turned so the nose points up, down or
// sideways into the lane (SetFacing). A parking space is 96 x 128 kit px.
//
// A parked ship is powered down: greyed out, engine cold, no idle motion,
// bay dark. Selecting it powers it up -- colour
// warms back in, a thin cyan highlight frames the bay, the
// engine lights and the hull starts a gentle hover. The component disables
// itself once a berth has settled powered-down, so fourteen idle berths cost
// nothing per frame.
//
// A ship the player has not bought yet is parked as a solid ink silhouette
// of its hull ("who's that Pokemon?"): no colour, no interior detail, no
// idle flipbook, no hover and a cold engine, even while selected -- only the
// berth lights answer the tap, and its name and price tag stay readable.
// Buying it reveals it: the shape fills with white, then the white fades
// into the hull's own colours.
public partial class DockBay : MonoBehaviour
{
    // Sorting orders, back to front.
    public const int OrderBay = 2, OrderBack = 3, OrderShadow = 5, OrderHighlight = 10,
                     OrderShip = 11, OrderLabel = 12, OrderLaunch = 40;

    // The ship sits a hair toward its nose, clear of the service wall it is backed against.
    public static readonly Vector3 ShipRest = new Vector3(0f, 4f * DockLayout.Px, 0f);
    // A hull fills about 75% of its bay: at most 76 px across and 100 px long (nose-up frame).
    public static readonly Vector2 HullBox = new Vector2(76f * DockLayout.Px, 100f * DockLayout.Px);
    public const float HullSize = 100f * DockLayout.Px;   // longest edge a parked hull may have
    public const float LabelBase = .8f;      // number / tag / icon size next to the 75% lot
    const float PowerTime = .38f;
    public const float RevealTime = .6f;
    public const float RevealPeak = .3f;     // white-out point of the reveal (0..1)
    public static readonly Color SilhouetteInk = AkiraPalette.Ink;

    static readonly Color OffTint = new Color(.62f, .68f, .80f, 1f);
    static readonly Color NameOwned = AkiraPalette.WithAlpha(AkiraPalette.Bone, .95f);
    static readonly Color NameLocked = AkiraPalette.WithAlpha(AkiraPalette.Muted, .9f);
    static int saturationId, silhouetteId, inkId, flashId;

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

    SpriteRenderer floor, back, highlight, shadow, statusIcon, chip, chipDust;
    readonly SpriteRenderer[] digits = new SpriteRenderer[2];
    Transform orient;
    Vector2 extent = new Vector2(DockLayout.BayW, DockLayout.BayL) * .5f;   // half size, bay-root space
    Vector2 nose = Vector2.up;
    TextMesh nameText, chipText;
    readonly Sprite[] idle = new Sprite[ShipHullArt.IdleDrawings];
    Sprite rest;
    MaterialPropertyBlock block;
    float power;
    float spin;
    float phase;
    ShipRotors rotors;                       // spinning rotors, for ships that have them
    float silhouette;                        // 1 = ink shadow, 0 = full art
    float flash;                             // white fill during the reveal
    float reveal = -1f;                      // reveal progress 0..1, <0 idle

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
        if (saturationId == 0)
        {
            saturationId = Shader.PropertyToID("_Saturation");
            silhouetteId = Shader.PropertyToID("_Silhouette");
            inkId = Shader.PropertyToID("_Ink");
            flashId = Shader.PropertyToID("_Flash");
        }
        block = new MaterialPropertyBlock();

        var orientGo = new GameObject("Orient");
        orient = orientGo.transform;
        orient.SetParent(transform, false);
        floor = MakeSprite("Bay", DockArt.Lot("bay_floor"), Vector3.zero, OrderBay, orient);
        // the service wall / clamp is the bottom 24 px of the nose-up frame
        back = MakeSprite("Wall", DockArt.Lot("bay_back"), new Vector3(0f, -(64f - 12f) * DockLayout.Px, 0f), OrderBack, orient);
        floor.color = back.color = new Color(1f, 1f, 1f, DockArt.DeckOpacity);
        highlight = MakeSprite("Highlight", DockArt.Lot("bay_highlight"), Vector3.zero, OrderHighlight, orient);
        shadow = MakeSprite("Shadow", DockArt.Get("shadow"), ShipRest + new Vector3(.05f, -.06f), OrderShadow, orient);
        for (int k = 0; k < 2; k++) digits[k] = MakeSprite("Digit" + k, null, Vector3.zero, OrderLabel, transform);

        // The ship itself. Named "ship<N>" so the rest of the game (and the
        // tests) find it the way they always have.
        var shipGo = new GameObject("ship" + i);
        ship = shipGo.transform;
        ship.SetParent(orient, false);
        ship.localPosition = ShipRest;
        hull = shipGo.AddComponent<SpriteRenderer>();
        LoadSkin(ShipSkins.Shown(i));
        hull.sprite = rest;
        hull.sortingOrder = OrderShip;
        var material = DockArt.ShipMaterial;
        if (material != null) hull.sharedMaterial = material;
        float k2 = 1f;
        if (rest != null && rest.bounds.size.x > 0f && rest.bounds.size.y > 0f)
            k2 = Mathf.Min(HullBox.x / rest.bounds.size.x, HullBox.y / rest.bounds.size.y);
        ship.localScale = new Vector3(k2, k2, 1f);
        hullHalfSize = rest != null ? (Vector2)rest.bounds.extents * k2 : new Vector2(.3f, .3f);
        shadow.transform.localScale = new Vector3(hullHalfSize.x * 2.4f / .64f, hullHalfSize.y * 2.2f / .64f, 1f);

        // Spinning rotors for ships that have a rotors file (Turtle); they follow the hull's tint.
        rotors = ShipRotors.Attach(shipGo, i, hull, true);

        // Engine: the same mounts the flight flame uses, cold until selected.
        ShipExhaust.ConfigureBoost(shipGo, i);
        if (!wind)
        {
            thruster = shipGo.AddComponent<ShipThruster>();
            thruster.respondToPause = false;
            thruster.idleScale = .55f;
            thruster.startCold = true;
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
        // (the lot's bays carry a number, not a name plate; the ship's card names it. Kept for NameLabel.)
        nameText = MakeText("Name", font, Vector3.zero, .078f, TextAnchor.MiddleCenter);
        nameText.text = (shopingShips.NameFor(i) ?? "").ToUpperInvariant();
        nameText.GetComponent<MeshRenderer>().enabled = false;

        // Status corner: a check (owned), a chevron (equipped) or a price tag.
        statusIcon = MakeSprite("Status", DockArt.Get("icon_owned"), Vector3.zero, OrderLabel);
        chip = MakeSprite("PriceTag", DockArt.Get("chip", 6f), Vector3.zero, OrderLabel);
        chip.drawMode = SpriteDrawMode.Sliced;
        chipDust = MakeSprite("Dust", DockArt.Get("icon_dust"), Vector3.zero, OrderLabel + 1);
        chipDust.transform.SetParent(chip.transform, false);
        chipDust.transform.localScale = Vector3.one * .8f;
        chipText = MakeText("Price", font, Vector3.zero, .125f, TextAnchor.MiddleRight);
        chipText.transform.SetParent(chip.transform, false);
        chipText.color = DockArt.Gold;

        SetNumber(i);
        BuildLoadout(font);
        SetFacing(Facing.Up);
        SetLabelScale(1f);
        ApplyPower(0f);
        enabled = false;
    }

    SpriteRenderer MakeSprite(string name, Sprite sprite, Vector3 local, int order, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
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
        if (Power > .05f && !Launching && silhouette <= 0f)
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
        RefreshLoadout();
        nameText.color = equipped ? DockArt.Cyan : owned ? NameOwned : NameLocked;
        statusIcon.enabled = owned;
        statusIcon.sprite = DockArt.Get(equipped ? "icon_active" : "icon_owned");
        statusIcon.color = equipped ? Color.white : new Color(1f, 1f, 1f, .85f);
        bool locked = !owned;
        // Unbought: the ink silhouette. Bought: full art, unless the reveal
        // that BeginReveal started is still playing.
        if (locked) { silhouette = 1f; flash = 0f; reveal = -1f; }
        else if (reveal < 0f) { silhouette = 0f; flash = 0f; }
        if (silhouette > 0f && hull != null && !Launching) hull.sprite = rest;
        ApplyPower(DockTween.InOutCubic(power));
        chip.gameObject.SetActive(locked);
        if (locked)
        {
            // a premium ship has no dust price and is bought in the Shop screen: the tag says SHOP
            bool store = PremiumShips.IsPremium(index);
            string label = store ? DockPopup.ShopButtonText : Mathf.RoundToInt(price).ToString("N0");
            chipDust.gameObject.SetActive(!store);
            chipText.text = label;
            float width = (store ? .2f : .42f) + label.Length * .075f;
            chip.size = new Vector2(width, ChipH);
            chipWidth = width;
            PlaceChip();
            chipDust.transform.localPosition = new Vector3(-width * .5f + .13f, 0f, 0f);
            chipText.transform.localPosition = new Vector3(width * .5f - .09f, -.004f, 0f);
        }
    }

    // The corner price tag, right edge fixed, at the current label scale.
    float chipWidth = .4f, labelK = 1f;
    float E { get { return labelK * LabelBase; } }

    // The tag sits on the service wall: right of the number in a row bay, under it in a lane bay (flush with the
    // wall's outer edge, so it grows into the bay, never toward the neighbouring bay).
    void PlaceChip()
    {
        chip.transform.localScale = Vector3.one;
        float w = chipWidth;
        float px = DockLayout.Px;
        // bottom corner on the wall side: LabelPad clear of the bay's outer edge and of its bottom edge (the number
        // sits in the top corner, so the two never meet)
        if (Side) chip.transform.localPosition = new Vector3(WallSign * (64f * px - LabelPad - w * .5f), -ChipY, 0f);
        else chip.transform.localPosition = new Vector3(43f * px - w * .5f, WallY, 0f);
    }

    public const float LabelPad = 8f * DockLayout.Px;     // clear space between a label and the bay's frame
    public const float ChipH = .24f;                       // price pill height, world units (about 20 kit px)
    public const float DigitK = .6f;                       // the bay number's size next to the 12 x 16 px stencil
    static readonly float ChipY = 48f * DockLayout.Px - LabelPad - ChipH * .5f;

    bool Side { get { return nose.x != 0f; } }
    float WallSign { get { return -nose.x; } }                              // lane bays: which side the wall is on
    float WallY { get { return -nose.y * 52f * DockLayout.Px; } }           // row bays: wall centre
    public Facing Face { get; private set; }
    public Vector2 Extent { get { return extent; } }
    // How far the hull reaches above / below its centre on screen (the card floats clear of it).
    public float ScreenHalfHeight { get { return Side ? hullHalfSize.x : hullHalfSize.y; } }

    // Turns the bay so the nose points `f`; labels stay upright. The wall is on the opposite side.
    public void SetFacing(Facing f)
    {
        Face = f;
        float angle = f == Facing.Up ? 0f : f == Facing.Down ? 180f : f == Facing.Right ? -90f : 90f;
        orient.localRotation = Quaternion.Euler(0f, 0f, angle);
        nose = f == Facing.Up ? Vector2.up : f == Facing.Down ? Vector2.down : f == Facing.Right ? Vector2.right : Vector2.left;
        extent = (Side ? new Vector2(DockLayout.BayL, DockLayout.BayW) : new Vector2(DockLayout.BayW, DockLayout.BayL)) * .5f;
        PositionLabels();
    }

    // Number (two stencil digits) and status icon on the wall.
    void PositionLabels()
    {
        float px = DockLayout.Px;
        Vector3 numberAt, iconAt;
        if (Side)
        {
            // centred on the 24 px service wall, top corner: a few px of wall to either side, 8 px clear above
            numberAt = new Vector3(WallSign * 50f * px, 33f * px, 0f);
            iconAt = new Vector3(WallSign * 50f * px, -ChipY, 0f);
        }
        else
        {
            numberAt = new Vector3(-29f * px, WallY, 0f);
            iconAt = new Vector3(40f * px, WallY, 0f);
        }
        // digit cells are 12 px wide; the pair is centred on numberAt
        digits[0].transform.localPosition = numberAt + new Vector3(-6f * px * DigitK, 0f, 0f);
        digits[1].transform.localPosition = numberAt + new Vector3(6f * px * DigitK, 0f, 0f);
        statusIcon.transform.localPosition = iconAt;
        PlaceChip();
        PlaceLoadout();
    }

    int number;
    public int Number { get { return number; } }
    // The bay's number on the wall, 01..99.
    public void SetNumber(int n)
    {
        number = n;
        digits[0].sprite = DockArt.Digit(Mathf.Clamp(n, 0, 99) / 10);
        digits[1].sprite = DockArt.Digit(Mathf.Clamp(n, 0, 99) % 10);
    }

    // Rectangles (bay-root space) the number and tag occupy, for the layout tests.
    public Rect NumberRect
    {
        get
        {
            Vector3 a = digits[0].transform.localPosition, b = digits[1].transform.localPosition;
            float k = DigitK * DockLayout.Px;
            return new Rect(a.x - 6f * k, a.y - 8f * k, (b.x - a.x) + 12f * k, 16f * k);
        }
    }

    // The price pill, bay-root space (empty when the bay shows no price).
    public Rect ChipRect
    {
        get
        {
            if (!chip.gameObject.activeSelf) return new Rect(0f, 0f, 0f, 0f);
            Vector3 c = chip.transform.localPosition;
            return new Rect(c.x - chipWidth * .5f, c.y - ChipH * .5f, chipWidth, ChipH);
        }
    }

    // A rack drawn smaller than the berths were designed for (the wide dock beside its card) grows the
    // name plates, price tags and status icons by `k` (1 = as designed) so they stay readable.
    public void SetLabelScale(float k)
    {
        labelK = k;
        PlaceLoadout();
        nameText.transform.localScale = new Vector3(E, E, 1f);
        statusIcon.transform.localScale = Vector3.one * .62f * E;
        for (int d = 0; d < 2; d++) digits[d].transform.localScale = new Vector3(k * DigitK, k * DigitK, 1f);
        PlaceChip();
    }

    // The ship was just bought: play the black -> white -> colour reveal.
    // Call before RefreshStatus marks the berth owned.
    public void BeginReveal()
    {
        if (silhouette <= 0f) return;
        reveal = 0f;
        enabled = true;
    }

    // Reveal curve: the ink fills with white up to RevealPeak, the art swaps
    // in under the full white, and the white fades away into the colours.
    void StepReveal(float t)
    {
        reveal = Mathf.Clamp01(t);
        if (reveal < RevealPeak)
        {
            silhouette = 1f;
            flash = DockTween.InOutCubic(reveal / RevealPeak);
        }
        else
        {
            silhouette = 0f;
            flash = 1f - DockTween.InOutCubic((reveal - RevealPeak) / (1f - RevealPeak));
        }
        if (reveal >= 1f) { reveal = -1f; silhouette = 0f; flash = 0f; }
    }

    public bool Silhouetted { get { return silhouette > 0f; } }
    public float SilhouetteAmount { get { return silhouette; } }
    public float FlashAmount { get { return flash; } }
    public bool Revealing { get { return reveal >= 0f; } }
    // Whether the hull shows its own art and runs its idle life (flipbook,
    // hover, engine): bought, and not still in the black part of the reveal.
    bool Alive { get { return Owned && silhouette <= 0f; } }

    public void SetPowered(bool on)
    {
        if (Powered == on && enabled) return;
        Powered = on;
        enabled = true;
    }

    // The launching hull has left its bay: the selection highlight goes out.
    public void SetHighlightOff() { highlight.color = new Color(1f, 1f, 1f, 0f); }

    public void SetShadow(float altitude, float alpha)
    {
        // the shadow falls down and to the right on screen whichever way the bay is turned
        shadow.transform.position = ship.position + new Vector3(.05f + altitude * .5f, -.06f - altitude, 0f);
        shadow.color = new Color(1f, 1f, 1f, alpha);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float target = Powered || Launching ? 1f : 0f;
        power = Mathf.MoveTowards(power, target, dt / PowerTime);
        if (reveal >= 0f) StepReveal(reveal + dt / RevealTime);
        float p = DockTween.InOutCubic(power);
        ApplyPower(p);

        if (!Launching)
        {
            float t = Time.unscaledTime;
            float live = Alive ? p : 0f;
            float bob = Mathf.Sin(t * 2.4f + phase) * .022f * live;
            ship.localPosition = new Vector3(ShipRest.x, ShipRest.y + bob, 0f);
            // The hull's own idle flipbook (lights blink, canopy glint), on
            // the same tick table it flies with; a parked hull holds still.
            int drawing = ShipHullArt.IdleDrawingAt(t * ShipHullArt.TicksPerSecond + phase * 10f);
            hull.sprite = live > .05f ? idle[Mathf.Clamp(drawing, 0, idle.Length - 1)] : rest;
            if (wind)
            {
                spin += 80f * live * dt;
                ship.localRotation = Quaternion.Euler(0f, 0f, spin);
            }
            SetShadow(bob * .6f, .55f);
        }

        if (!Powered && !Launching && power <= 0f && reveal < 0f)
        {
            if (wind) { spin = 0f; ship.localRotation = Quaternion.identity; }
            ApplyPower(0f);
            enabled = false;
        }
    }

    void ApplyPower(float p)
    {
        if (rotors != null) rotors.hidden = silhouette > 0f;
        if (hull != null)
        {
            hull.color = Color.Lerp(OffTint, Color.white, p);
            hull.GetPropertyBlock(block);
            block.SetFloat(saturationId, Mathf.Lerp(.22f, 1f, p));
            block.SetFloat(silhouetteId, silhouette);
            block.SetColor(inkId, SilhouetteInk);
            block.SetFloat(flashId, flash);
            hull.SetPropertyBlock(block);
            // Without the dock shader (unsupported device) a black tint is
            // the closest the default sprite material gets to a silhouette.
            if (silhouette > 0f && hull.sharedMaterial != DockArt.ShipMaterial) hull.color = SilhouetteInk;
        }
        float t = Time.unscaledTime;
        float pulse = p > 0f ? .5f + .5f * Mathf.Sin(t * 3.2f + phase) : 0f;
        // the selected bay: a thin cyan highlight (kit bay_highlight), nothing at all while parked
        highlight.color = new Color(1f, 1f, 1f, p * (1f - .2f * pulse));
        // A silhouette's engine stays cold, selected or not.
        bool burn = Launching || (Powered && power > .3f && Alive);
        if (thruster != null) thruster.powered = burn;
        if (drift != null)
        {
            drift.powered = burn;
            drift.boost = Launching;
        }
    }

    // Jumps straight to the current power target (no fade). Used when the
    // dock is rebuilt and by the edit-mode tests, where no time passes.
    public void SnapPower()
    {
        if (reveal >= 0f) StepReveal(1f);
        power = Powered || Launching ? 1f : 0f;
        ApplyPower(power);
    }

    public string NameLabel { get { return nameText.text; } }
    public bool ShowsStatusIcon { get { return statusIcon.enabled; } }
    public bool ShowsPrice { get { return chip.gameObject.activeSelf; } }
    public string PriceLabel { get { return chipText.text; } }
    public float LightLevel { get { return highlight.color.a; } }

    public bool Contains(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return Mathf.Abs(local.x) <= extent.x && Mathf.Abs(local.y) <= extent.y;
    }

}
