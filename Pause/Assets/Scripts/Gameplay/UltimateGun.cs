using UnityEngine;

// The small weapon that telegraphs and fires the ship's ultimate.
//
// Replaces the old "second finger + text countdown" trigger: the power now
// fires itself the instant it is charged, and this is how the player sees it
// coming. Sized from the hull's own sprite bounds (the same approach
// ShipExhaust and the shield use) so it reads correctly on any ship
// regardless of that ship's own scale. It stays tucked in against the hull's
// left edge almost all the time; ShipPowerController calls Tick() every
// frame with how close the cooldown is to firing, and this eases the barrel
// out over that last stretch, then Fire() plays the ship's own muzzle-flash
// flipbook (WeaponArt) the instant it actually goes off, and the barrel
// slides back in as the cooldown resets.
//
// It is also the ship's firefighter (ShipDamageFx drives it): a hit
// startles it (a jolt, a red alarm blink, a pop), then while the ship is
// hurt it hurries in to its station beside the worst live hot spot, leans
// toward it, pops its retardant nozzle out and sprays (Firefight). The
// station is always inside its rest envelope (LocalRestEnvelope), so it
// never strays onto the hearts, the other ship elements or the thumb. It
// gives way the moment the ultimate starts sliding out (Busy) and drifts back
// to its normal hover. All of the firefighting runs on scaled time (it holds
// still while the world is frozen); at rest it is exactly the old hover.
public class UltimateGun : MonoBehaviour
{
    Transform barrel;
    Transform muzzle;
    SpriteRenderer muzzleRenderer;
    SpriteRenderer barrelRenderer;
    Transform nozzle;
    SpriteRenderer nozzleRenderer;

    float mountY, barrelLength;
    float extend; // 0 retracted .. 1 fully extended, eased toward Tick's target
    float flashT = -1f; // seconds since Fire() on unscaled time; < 0 when idle
    const float MuzzleWorldSize = .62f;
    float firePop;
    int shipIndex;
    Vector3 restingOffset, firingOffset;
    Vector3 muzzleBaseScale;

    public static UltimateGun Attach(GameObject ship)
    {
        var existing = ship.GetComponentInChildren<UltimateGun>();
        if (existing != null) return existing;

        var go = new GameObject("~UltimateGun");
        go.transform.SetParent(ship.transform, false);
        return go.AddComponent<UltimateGun>();
    }

    void Awake()
    {
        var hull = GetComponentInParent<SpriteRenderer>();
        Vector2 extents = hull != null && hull.sprite != null ? hull.sprite.bounds.extents : new Vector2(0.4f, 0.5f);

        shipIndex = ShipId.Of(hull != null ? hull.gameObject : gameObject, ShipId.Equipped());
        barrelLength = extents.x * 0.92f;
        // A companion weapon hovers beside its owner while charging, then
        // drifts into the forward firing slot just before the sweep begins.
        mountY = 0f;
        restingOffset = new Vector3(extents.x * (shipIndex % 2 == 0 ? -1.22f : 1.22f),
                                    extents.y * .12f, .02f);
        firingOffset = new Vector3(0f, extents.y * 1.08f, .02f);

        var barrelGo = new GameObject("Barrel", typeof(SpriteRenderer));
        barrelGo.transform.SetParent(transform, false);
        barrelRenderer = barrelGo.GetComponent<SpriteRenderer>();
        barrelRenderer.sprite = GunSpriteFor(shipIndex);
        barrelRenderer.color = Color.white;
        barrelRenderer.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 1;
        if (barrelRenderer.sprite != null)
        {
            float fit = Mathf.Max(barrelRenderer.sprite.bounds.size.x, barrelRenderer.sprite.bounds.size.y);
            float k = (extents.y * 0.72f) / Mathf.Max(0.0001f, fit);
            barrelGo.transform.localScale = Vector3.one * k;
        }
        else
            barrelGo.transform.localScale = new Vector3(barrelLength, extents.y * 0.14f, 1f);
        barrel = barrelGo.transform;

        var muzzleGo = new GameObject("Muzzle", typeof(SpriteRenderer));
        muzzleGo.transform.SetParent(transform, false);
        muzzleRenderer = muzzleGo.GetComponent<SpriteRenderer>();
        // The ship's own flat cel muzzle flash flipbook (WeaponArt), hidden
        // until Fire().
        muzzleRenderer.sprite = WeaponArt.Muzzle(shipIndex, 0);
        muzzleRenderer.enabled = false;
        muzzleRenderer.sortingOrder = barrelRenderer.sortingOrder + 6;
        muzzle = muzzleGo.transform;
        muzzleBaseScale = Vector3.one;

        // The retardant nozzle: rides the drone's body (inside the barrel
        // art's rect, so it never widens the gun's footprint) and turns to
        // face the hot spot; hidden at rest.
        var nozzleGo = new GameObject("Nozzle", typeof(SpriteRenderer));
        nozzleGo.transform.SetParent(barrel, false);
        nozzleRenderer = nozzleGo.GetComponent<SpriteRenderer>();
        nozzleRenderer.sprite = ShipDamageFx.Frame(ShipDamageFx.RowNozzle, 0);
        nozzleRenderer.sortingOrder = barrelRenderer.sortingOrder + 1;
        nozzleRenderer.enabled = false;
        nozzle = nozzleGo.transform;
        if (barrelRenderer.sprite != null)
        {
            var sb = barrelRenderer.sprite.bounds;
            nozzle.localPosition = new Vector3(sb.center.x, sb.center.y - sb.size.y * .08f, -.01f);
            nozzleSize = Mathf.Min(sb.size.x, sb.size.y) * NozzleSpan;
        }
        else nozzleSize = NozzleSpan;

        restPos = transform.localPosition;
        Reposition(0f, Time.unscaledDeltaTime);
        ShipUiSlots.Register(transform.parent, this, () => ShipUiSlots.GunToWorld(transform.parent, shipIndex, LocalEnvelope()),
                             () => ShipUiSlots.DrawnBounds(transform));
    }

    void Reposition(float extend01, float unscaledDt)
    {
        Vector3 hover = HoverOffset(shipIndex, Time.unscaledTime);
        Vector3 target = Vector3.Lerp(restingOffset + hover, firingOffset + hover * .2f, extend01);
        var hull = transform.parent;
        bool spin = hull != null && ShipUiSlots.Spins(shipIndex);
        // At rest the hover eases on from wherever the gun is (as it always
        // has); while firefighting the hover pose eases on by itself and the
        // duty pose is laid over it.
        if (!offRest) restPos = spin ? transform.position : transform.localPosition;
        float k = 1f - Mathf.Exp(-10f * unscaledDt);
        if (spin)
        {
            // Ninja and UFO spin their whole hull: keep the gun upright
            // beside it (the same offsets, unrotated) instead of orbiting.
            Vector3 world = hull.position + Vector3.Scale(target, hull.lossyScale);
            restPos = Vector3.Lerp(restPos, world, k);
        }
        else restPos = Vector3.Lerp(restPos, target, k);

        Vector3 at = restPos;
        offRest = duty > 0f || shake != 0f;
        if (offRest && hull != null)
        {
            Vector3 g = spin ? GunSpaceOf(restPos) : restPos;
            if (duty > 0f) g = Vector3.Lerp(g, stationNow, Smooth01(duty));
            g = ClampToRest(g, barrelAngle + shake, transform.localScale.x);
            if (spin)
            {
                Vector3 s = hull.lossyScale;
                at = new Vector3(hull.position.x + g.x * s.x, hull.position.y + g.y * s.y, restPos.z);
            }
            else at = new Vector3(g.x, g.y, restPos.z);
        }
        if (spin)
        {
            transform.position = at;
            transform.rotation = Quaternion.identity;
        }
        else transform.localPosition = at;
        barrel.localPosition = new Vector3(0f, mountY, 0.02f);
        muzzle.localPosition = new Vector3(0f, mountY + barrelLength * .58f, 0.01f);
    }

    // targetExtend01: 0 fully retracted .. 1 fully extended -- the caller
    // works out how close to firing it is, this just eases toward it.
    public void Tick(float targetExtend01)
    {
        Step(targetExtend01, Time.unscaledDeltaTime, Time.deltaTime);
    }

    // One frame: unscaledDt drives the ultimate (as always), scaledDt the
    // firefighting.
    public void Step(float targetExtend01, float unscaledDt, float scaledDt)
    {
        targetExtend = targetExtend01;
        extend = Mathf.MoveTowards(extend, targetExtend01, unscaledDt * 2.5f);
        StepFirefight(unscaledDt, scaledDt);

        firePop = Mathf.Max(0f, firePop - unscaledDt * 3.8f);
        float scale = 1f + extend * ExtendGrow + firePop * FirePopGrow + startlePop;
        transform.localScale = Vector3.one * scale;
        Reposition(extend, unscaledDt);

        if (flashT >= 0f && muzzleRenderer != null)
        {
            // pinch, flash, forward smear, speed lines, held on the art's
            // tick table -- unscaled so the
            // cinematic slow motion doesn't hold the flash on screen
            flashT += unscaledDt;
            int frame = WeaponArt.FrameAt(WeaponArt.MuzzleTicks, flashT, false);
            if (frame >= WeaponArt.MuzzleFrames)
            {
                flashT = -1f;
                muzzleRenderer.enabled = false;
            }
            else
            {
                muzzleRenderer.enabled = true;
                muzzleRenderer.sprite = WeaponArt.Muzzle(shipIndex, frame);
                float world = MuzzleWorldSize / Mathf.Max(.0001f, Mathf.Abs(transform.lossyScale.x));
                muzzle.localScale = muzzleBaseScale * world; // the smear is drawn
                muzzle.rotation = Quaternion.identity;
            }
        }
    }

    public void Fire()
    {
        flashT = 0f;
        firePop = 1f;
    }

    // 0 tucked beside the hull .. 1 in the forward firing slot.
    public float Extend01 => extend;
    public bool Flashing => flashT >= 0f;

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up;

    // ---- firefighting ----------------------------------------------------

    public const float StartleSeconds = .38f;
    public const float MaxTilt = 24f;          // the drone leans toward the spot, at most this
    const float StartleShake = 11f, StartlePop = .1f;
    const float DutyArrive = 3.2f, DutyLeave = 2.2f, StationChase = 9f;
    const float NozzleSpan = .42f;             // nozzle size, of the barrel art's narrow edge
    static readonly Color Alarm = new Color(1f, .42f, .36f, 1f);

    Vector3 restPos;          // the hover / firing pose (local; world on a spinner)
    bool offRest;             // last frame laid a firefighting pose over it
    float targetExtend;
    float duty;               // 0 at rest .. 1 on station (scaled time)
    bool dutyWanted, dutySpraying, haveStation;
    Vector3 dutySpot;         // the hot spot, world
    Vector3 stationNow;       // where it is headed, gun space (GunSpaceOf)
    float dutyClock, barrelAngle, nozzleAngle, nozzleOpen, nozzleClock, nozzleSize;
    float startle, shake, startlePop;

    // Orders from ShipDamageFx, every frame: go (or not) to the station for
    // the hot spot at `spotWorld`, spraying it or not.
    public void Firefight(bool onDuty, Vector3 spotWorld, bool spraying)
    {
        dutyWanted = onDuty;
        dutySpot = spotWorld;
        dutySpraying = onDuty && spraying;
    }

    // The ship was hit: the drone jolts and blinks.
    public void Startle()
    {
        startle = StartleSeconds;
        Startles++;
    }

    public int Startles { get; private set; }
    public bool Startling => startle > 0f;
    // Sliding out for, firing or settling back from the ultimate.
    public bool Busy => extend > .001f || targetExtend > 0f || Flashing;
    // 0 resting .. 1 on its firefighting station.
    public float Duty => duty;
    public bool OnStation => duty >= .9f && !Busy;
    public bool NozzleShown => nozzleRenderer != null && nozzleRenderer.enabled;
    public SpriteRenderer NozzleRenderer => nozzleRenderer;
    public SpriteRenderer BarrelRenderer => barrelRenderer;
    public float Lean => barrelAngle;
    public float Shake => shake;
    public Vector3 Station => stationNow;
    // Where the retardant leaves the nozzle (world).
    public Vector3 NozzlePosition =>
        nozzle != null ? nozzle.TransformPoint(new Vector3(0f, .16f, 0f)) : transform.position;

    // The hull's frame the gun lives in: hull-local on most ships; on a
    // spinner the unrotated offset from the hull (the gun stays upright).
    public Vector3 GunSpaceOf(Vector3 world)
    {
        var hull = transform.parent;
        if (hull == null) return world;
        if (!ShipUiSlots.Spins(shipIndex)) return hull.InverseTransformPoint(world);
        Vector3 s = hull.lossyScale, d = world - hull.position;
        return new Vector3(d.x / NonZero(s.x), d.y / NonZero(s.y), 0f);
    }

    static float NonZero(float v) { return Mathf.Abs(v) < 1e-5f ? 1e-5f : v; }
    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    void StepFirefight(float unscaledDt, float dt)
    {
        bool busy = Busy;
        bool want = dutyWanted && !busy && transform.parent != null;
        if (busy) duty = Mathf.MoveTowards(duty, 0f, Mathf.Max(dt, unscaledDt) * DutyLeave * 2f);
        else duty = Mathf.MoveTowards(duty, want ? 1f : 0f, dt * (want ? DutyArrive : DutyLeave));
        if (duty <= 0f) haveStation = false;

        Vector3 spot = GunSpaceOf(dutySpot);
        if (want && dt > 0f)
        {
            dutyClock += dt;
            if (!haveStation)
            {
                Vector3 now = ShipUiSlots.Spins(shipIndex) ? GunSpaceOf(transform.position) : transform.localPosition;
                stationNow = new Vector3(now.x, now.y, 0f);
                haveStation = true;
            }
            stationNow = Vector3.Lerp(stationNow, StationFor(spot), 1f - Mathf.Exp(-StationChase * dt));
        }

        // lean toward the spot, the nozzle facing it
        Vector3 to = spot - stationNow;
        float aim = haveStation && (to.x * to.x + to.y * to.y) > 1e-8f ? Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg : 0f;
        float lean = want ? Mathf.Clamp(aim, -MaxTilt, MaxTilt) * Smooth01(duty) : 0f;
        if (dt > 0f)
        {
            startle = Mathf.Max(0f, startle - dt);
            nozzleOpen = Mathf.MoveTowards(nozzleOpen, want && duty > .5f ? 1f : 0f, dt * 5f);
            nozzleClock = dutySpraying && want && nozzleOpen >= 1f ? nozzleClock + dt : 0f;
            barrelAngle = Mathf.MoveTowardsAngle(barrelAngle, lean, dt * 260f);
        }
        shake = 0f;
        startlePop = 0f;
        bool blink = false;
        if (startle > 0f && !busy)
        {
            float u = 1f - startle / StartleSeconds;
            shake = Mathf.Sin(u * Mathf.PI * 6f) * StartleShake * (1f - u);
            startlePop = Mathf.Sin(u * Mathf.PI) * StartlePop;
            blink = u < .8f && Mathf.FloorToInt(u * 10f) % 2 == 0;
        }
        if (busy) barrelAngle = 0f;
        float angle = barrelAngle + shake;
        if (barrel != null) barrel.localRotation = angle != 0f ? Quaternion.Euler(0f, 0f, angle) : Quaternion.identity;
        if (barrelRenderer != null) barrelRenderer.color = blink ? Alarm : Color.white;

        if (nozzleRenderer != null)
        {
            bool show = nozzleOpen > 0f && !busy;
            nozzleRenderer.enabled = show;
            if (show)
            {
                if (dt > 0f)
                    nozzleAngle = Mathf.MoveTowardsAngle(nozzleAngle,
                        Mathf.Clamp(Mathf.DeltaAngle(barrelAngle, aim), -150f, 150f), dt * 540f);
                nozzle.localRotation = Quaternion.Euler(0f, 0f, nozzleAngle);
                nozzle.localScale = Vector3.one * (nozzleSize * Smooth01(nozzleOpen));
                int frame = 0;
                if (nozzleClock > 0f) frame = nozzleClock < .08f ? 1 : 2 + Mathf.FloorToInt(nozzleClock * 12f) % 2;
                nozzleRenderer.sprite = ShipDamageFx.Frame(ShipDamageFx.RowNozzle, frame);
            }
        }
    }

    // Where to hover to spray the spot (gun space): just off it on the rest
    // side, circling a little, kept inside the rest envelope.
    Vector3 StationFor(Vector3 spot)
    {
        Vector3 away = restingOffset - spot;
        away.z = 0f;
        float standoff = barrelRenderer != null && barrelRenderer.sprite != null
            ? barrelRenderer.sprite.bounds.size.y * barrel.localScale.y * .9f : .3f;
        Vector3 want = spot + (away.sqrMagnitude > 1e-8f ? away.normalized : Vector3.left) * standoff;
        Vector2 a = HoverAmplitude(shipIndex);
        want += new Vector3(Mathf.Cos(dutyClock * 2.6f) * a.x, Mathf.Sin(dutyClock * 3.3f) * a.y, 0f) * .3f;
        Vector3 to = spot - want;
        float lean = Mathf.Clamp(Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg, -MaxTilt, MaxTilt);
        want = ClampToRest(want, lean, 1f + StartlePop);
        want.z = 0f;
        return want;
    }

    // `g` (gun space) moved so the barrel art -- leaning `angle` degrees at
    // `scale` -- draws inside LocalRestEnvelope.
    Vector3 ClampToRest(Vector3 g, float angle, float scale)
    {
        if (barrelRenderer == null || barrelRenderer.sprite == null) return g;
        Bounds env = LocalRestEnvelope();
        Bounds sb = barrelRenderer.sprite.bounds;
        Vector3 ls = barrel.localScale;
        float c = Mathf.Cos(angle * Mathf.Deg2Rad), sn = Mathf.Sin(angle * Mathf.Deg2Rad);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float x = ((i & 1) == 0 ? sb.min.x : sb.max.x) * ls.x;
            float y = ((i & 2) == 0 ? sb.min.y : sb.max.y) * ls.y + mountY;
            float rx = (x * c - y * sn) * scale, ry = (x * sn + y * c) * scale;
            minX = Mathf.Min(minX, rx); maxX = Mathf.Max(maxX, rx);
            minY = Mathf.Min(minY, ry); maxY = Mathf.Max(maxY, ry);
        }
        float loX = env.min.x - minX, hiX = env.max.x - maxX, loY = env.min.y - minY, hiY = env.max.y - maxY;
        g.x = loX <= hiX ? Mathf.Clamp(g.x, loX, hiX) : (loX + hiX) * .5f;
        g.y = loY <= hiY ? Mathf.Clamp(g.y, loY, hiY) : (loY + hiY) * .5f;
        return g;
    }

    // Four readable companion behaviors distributed across the roster:
    // orbit, side-to-side float, vertical bob and a loose figure-eight.
    public static int HoverModeFor(int index) => Mathf.Abs(index) % 4;
    static Vector3 HoverOffset(int index, float time)
    {
        float phase = time * 2.4f + index * .71f;
        Vector2 a = HoverAmplitude(index);
        switch (HoverModeFor(index))
        {
            case 0: return new Vector3(Mathf.Cos(phase) * a.x, Mathf.Sin(phase) * a.y, 0f);
            case 1: return new Vector3(Mathf.Sin(phase) * a.x, Mathf.Sin(phase * 2f) * a.y, 0f);
            case 2: return new Vector3(Mathf.Sin(phase * .7f) * a.x, Mathf.Sin(phase) * a.y, 0f);
            default: return new Vector3(Mathf.Sin(phase) * a.x, Mathf.Sin(phase * 2f) * a.y, 0f);
        }
    }

    // Peak hover drift per behaviour, in the hull's local units.
    public static Vector2 HoverAmplitude(int index)
    {
        switch (HoverModeFor(index))
        {
            case 0: return new Vector2(.18f, .18f);
            case 1: return new Vector2(.28f, .06f);
            case 2: return new Vector2(.08f, .25f);
            default: return new Vector2(.22f, .13f);
        }
    }

    // ---- ShipUiSlots footprint ------------------------------------------

    const float ExtendGrow = .14f, FirePopGrow = .24f;
    const float MaxGrow = 1f + ExtendGrow + FirePopGrow;

    // Every place the gun's mount can be, in the hull's local space: tucked
    // beside it with the full hover drift, and out in the firing slot.
    Bounds MountRange()
    {
        Vector2 a = HoverAmplitude(shipIndex);
        var range = new Bounds(restingOffset, new Vector3(a.x * 2f, a.y * 2f, 0f));
        range.Encapsulate(new Bounds(firingOffset, new Vector3(a.x * .4f, a.y * .4f, 0f)));
        return range;
    }

    static Bounds Grown(Bounds part)
    {
        var b = part;
        b.Encapsulate(new Bounds(part.center * MaxGrow, part.size * MaxGrow));
        return b;
    }

    // Everywhere the gun can draw (barrel at full fire-pop size, the muzzle
    // flash in the firing slot), in the hull's local space.
    public Bounds LocalEnvelope()
    {
        Bounds part = new Bounds(Vector3.zero, Vector3.zero);
        var br = barrelRenderer;
        if (br != null && br.sprite != null)
            part = new Bounds(barrel.localPosition + Vector3.Scale(br.sprite.bounds.center, barrel.localScale),
                              Vector3.Scale(br.sprite.bounds.size, barrel.localScale));
        part = Grown(part);
        Bounds mounts = MountRange();
        var env = new Bounds(mounts.center + part.center, mounts.size + part.size);
        var hull = transform.parent;
        float shipScale = hull != null ? Mathf.Max(.0001f, Mathf.Abs(hull.lossyScale.x)) : 1f;
        float flash = MuzzleWorldSize / shipScale;
        Vector3 tip = firingOffset + new Vector3(0f, (mountY + barrelLength * .58f) * MaxGrow, 0f);
        Vector2 drift = HoverAmplitude(shipIndex) * .4f;
        env.Encapsulate(new Bounds(tip, new Vector3(flash + drift.x, flash + drift.y, 0f)));
        env.center = new Vector3(env.center.x, env.center.y, 0f);
        return env;
    }

    // Where the gun draws while it rests beside the hull (its full hover
    // drift, barrel at full size), in the hull's local space: the low part
    // of LocalEnvelope, which also spans the ride up to the firing slot.
    public Bounds LocalRestEnvelope()
    {
        Bounds part = new Bounds(Vector3.zero, Vector3.zero);
        var br = barrelRenderer;
        if (br != null && br.sprite != null)
            part = new Bounds(barrel.localPosition + Vector3.Scale(br.sprite.bounds.center, barrel.localScale),
                              Vector3.Scale(br.sprite.bounds.size, barrel.localScale));
        part = Grown(part);
        Vector2 a = HoverAmplitude(shipIndex);
        var env = new Bounds(restingOffset + part.center, new Vector3(a.x * 2f, a.y * 2f, 0f) + part.size);
        env.center = new Vector3(env.center.x, env.center.y, 0f);
        return env;
    }

    // Everywhere the muzzle point can be, in the hull's local space (the
    // charge indicator rides onto it when the ultimate is ready).
    public Bounds LocalMuzzleEnvelope()
    {
        var tip = new Bounds(new Vector3(0f, mountY + barrelLength * .58f, 0f), Vector3.zero);
        tip = Grown(tip);
        Bounds mounts = MountRange();
        var env = new Bounds(mounts.center + tip.center, mounts.size + tip.size);
        env.center = new Vector3(env.center.x, env.center.y, 0f);
        return env;
    }

    static readonly Sprite[] gunSprites = new Sprite[16];
    static Sprite GunSpriteFor(int shipIndex)
    {
        // Roster ships are numbered 1-15 while the generated sheet is
        // zero-based, so subtract one to give every purchasable hull its own
        // tile. Ship 0 uses the first tile as the safe fallback.
        int slot = Mathf.Clamp(shipIndex <= 0 ? 0 : shipIndex - 1, 0, 14);
        if (gunSprites[slot] != null) return gunSprites[slot];
        var tex = Resources.Load<Texture2D>("ShipArt/Guns/ship_gun_roster");
        if (tex == null) return SolidSprite();
        const float Cell = 313.5f;
        int col = slot % 4;
        int row = 3 - slot / 4;
        gunSprites[slot] = Sprite.Create(tex, new Rect(col * Cell, row * Cell, Cell, Cell),
            new Vector2(.5f, .12f), 100f);
        return gunSprites[slot];
    }

    static Sprite solidCache;
    static Sprite SolidSprite()
    {
        if (solidCache != null) return solidCache;
        const int S = 4;
        var tex = new Texture2D(S, S);
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        // pixelsPerUnit == texture size: the sprite is exactly 1x1 world unit
        // at scale 1, so a renderer's localScale directly is its world size.
        solidCache = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return solidCache;
    }

}
