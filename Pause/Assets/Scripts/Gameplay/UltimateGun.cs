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

    float retractedX, extendedX, mountY, barrelLength;
    float extend; // 0 retracted .. 1 fully extended, eased toward Tick's target
    float flash;  // 1 right after firing, decays to 0

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

        int shipIndex = ShipExhaust.IndexFor(hull != null ? hull.gameObject : gameObject);
        barrelLength = extents.x * 0.92f;
        retractedX = -extents.x * 0.1f;
        extendedX = -extents.x * 0.34f;
        // Every hull points up during gameplay, so the weapon deploys from
        // the nose and every shot begins at the muzzle tip.
        mountY = extents.y * 0.74f;

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
        float x = Mathf.Lerp(retractedX, extendedX, extend01);
        barrel.localPosition = new Vector3(x, mountY, 0.02f);
        muzzle.localPosition = new Vector3(x, mountY + barrelLength * .58f, 0.01f);
    }

    // targetExtend01: 0 fully retracted .. 1 fully extended -- the caller
    // works out how close to firing it is, this just eases toward it.
    public void Tick(float targetExtend01)
    {
        extend = Mathf.MoveTowards(extend, targetExtend01, Time.deltaTime * 2.5f);
        Reposition(extend);

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
    }

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up;

    static readonly Sprite[] gunSprites = new Sprite[16];
    static Sprite GunSpriteFor(int shipIndex)
    {
        int slot = Mathf.Clamp(shipIndex, 0, 14);
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
