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
// out over that last stretch, then Fire() flashes the muzzle the instant it
// actually goes off, and the barrel slides back in as the cooldown resets.
public class UltimateGun : MonoBehaviour
{
    Transform barrel;
    Transform muzzle;
    SpriteRenderer muzzleRenderer;

    float mountY, barrelLength;
    float extend; // 0 retracted .. 1 fully extended, eased toward Tick's target
    float flash;  // 1 right after firing, decays to 0
    float firePop;
    int shipIndex;
    Vector3 restingOffset, firingOffset;

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

        shipIndex = ShipExhaust.IndexFor(hull != null ? hull.gameObject : gameObject);
        barrelLength = extents.x * 0.92f;
        // A companion weapon hovers beside its owner while charging, then
        // drifts into the forward firing slot just before the sweep begins.
        mountY = 0f;
        restingOffset = new Vector3(extents.x * (shipIndex % 2 == 0 ? -1.22f : 1.22f),
                                    extents.y * .12f, .02f);
        firingOffset = new Vector3(0f, extents.y * 1.08f, .02f);

        var barrelGo = new GameObject("Barrel", typeof(SpriteRenderer));
        barrelGo.transform.SetParent(transform, false);
        var barrelRenderer = barrelGo.GetComponent<SpriteRenderer>();
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
        muzzleRenderer.sprite = LoadVfx("vfx_light_02");
        muzzleRenderer.color = new Color(1f, 0.85f, 0.4f, 0f);
        muzzleRenderer.sortingOrder = barrelRenderer.sortingOrder + 1;
        muzzleGo.transform.localScale = Vector3.one * extents.y * 0.6f;
        muzzle = muzzleGo.transform;

        Reposition(0f);
    }

    void Reposition(float extend01)
    {
        Vector3 hover = HoverOffset(shipIndex, Time.unscaledTime);
        Vector3 target = Vector3.Lerp(restingOffset + hover, firingOffset + hover * .2f, extend01);
        transform.localPosition = Vector3.Lerp(transform.localPosition, target,
            1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        barrel.localPosition = new Vector3(0f, mountY, 0.02f);
        muzzle.localPosition = new Vector3(0f, mountY + barrelLength * .58f, 0.01f);
    }

    // targetExtend01: 0 fully retracted .. 1 fully extended -- the caller
    // works out how close to firing it is, this just eases toward it.
    public void Tick(float targetExtend01)
    {
        extend = Mathf.MoveTowards(extend, targetExtend01, Time.unscaledDeltaTime * 2.5f);
        Reposition(extend);

        firePop = Mathf.Max(0f, firePop - Time.unscaledDeltaTime * 3.8f);
        float scale = 1f + extend * .14f + firePop * .24f;
        transform.localScale = Vector3.one * scale;

        if (flash > 0f)
        {
            flash = Mathf.Max(0f, flash - Time.deltaTime * 2.2f);
            var c = muzzleRenderer.color;
            c.a = flash;
            muzzleRenderer.color = c;
        }
    }

    public void Fire()
    {
        flash = 1f;
        firePop = 1f;
    }

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up;

    // Four readable companion behaviors distributed across the roster:
    // orbit, side-to-side float, vertical bob and a loose figure-eight.
    public static int HoverModeFor(int index) => Mathf.Abs(index) % 4;
    static Vector3 HoverOffset(int index, float time)
    {
        float phase = time * 2.4f + index * .71f;
        switch (HoverModeFor(index))
        {
            case 0: return new Vector3(Mathf.Cos(phase) * .18f, Mathf.Sin(phase) * .18f, 0f);
            case 1: return new Vector3(Mathf.Sin(phase) * .28f, Mathf.Sin(phase * 2f) * .06f, 0f);
            case 2: return new Vector3(Mathf.Sin(phase * .7f) * .08f, Mathf.Sin(phase) * .25f, 0f);
            default: return new Vector3(Mathf.Sin(phase) * .22f, Mathf.Sin(phase * 2f) * .13f, 0f);
        }
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

    static Sprite LoadVfx(string file)
    {
        var tex = Resources.Load<Texture2D>("Prefabs/Vfx/" + file);
        return tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
    }
}
