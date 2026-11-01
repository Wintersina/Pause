using UnityEngine;

// The Gold Warden's deployed sentry as two drawings: a static BASE and a gun HEAD that turns all the way round
// (no arc limit) to face its mark. Art: Resources/Weapons/Powers2/GoldWarden_turret_base.png / _head.png, 256 px
// cells, one world unit across at scale 1 (the same pixel density as the attack strips). The head is drawn pointing
// straight up with its pivot on the cell centre; its muzzle tip is 24 px from the top of the cell, so
// 104 / 256 of a unit from the pivot. The head always sorts one above the base, so the barrel never draws under it.
// Plain object (no per-frame allocation): ShipAttackRunner owns one per sentry and steps it with gameplay time.
public sealed class SentryTurret
{
    public const float MuzzlePixels = 104f, CellPixels = 256f;
    public const float MuzzleReach = MuzzlePixels / CellPixels;   // pivot to muzzle tip, in units at scale 1
    public const float TurnSpeed = 540f;                          // degrees a second, either way round
    public const float FireCone = 10f;                            // it only fires once the head is this close to the aim
    public const float IdleSweep = 25f, IdleRate = 1.3f;          // no target: the head sweeps +-25 deg about straight up
    public const int BaseOrder = 68, HeadOrder = 69;
    public const float SentryScaleForTests = .9f;   // the size the sentries deploy at (ShipAttackRunner.SentryTurretSize)

    GameObject root;
    SpriteRenderer baseSr, headSr;
    float scale = .9f, shown = 1f, idleClock;
    static Sprite baseSprite, headSprite;

    public float Angle { get; private set; }   // the head's turn, degrees counter-clockwise from straight up
    public bool Alive { get { return root != null; } }
    public bool Visible { get { return root != null && root.activeSelf; } }
    public Vector3 Pivot { get { return root != null ? root.transform.position : Vector3.zero; } }
    public Transform HeadTransform { get { return headSr != null ? headSr.transform : null; } }
    public SpriteRenderer HeadRenderer { get { return headSr; } }
    public SpriteRenderer BaseRenderer { get { return baseSr; } }

    static void LoadSprites()
    {
        if (baseSprite == null) baseSprite = Resources.Load<Sprite>("Weapons/Powers2/GoldWarden_turret_base");
        if (headSprite == null) headSprite = Resources.Load<Sprite>("Weapons/Powers2/GoldWarden_turret_head");
    }

    void Build()
    {
        LoadSprites();
        root = new GameObject("~sentryTurret");
        var b = new GameObject("Base", typeof(SpriteRenderer));
        b.transform.SetParent(root.transform, false);
        baseSr = b.GetComponent<SpriteRenderer>();
        baseSr.sprite = baseSprite;
        baseSr.sortingOrder = BaseOrder;
        var h = new GameObject("Head", typeof(SpriteRenderer));
        h.transform.SetParent(root.transform, false);
        headSr = h.GetComponent<SpriteRenderer>();
        headSr.sprite = headSprite;
        headSr.sortingOrder = HeadOrder;
        root.SetActive(false);
    }

    // Places the turret open (pop-in scale `k` 0..1: 0.4 up to full size) with the head straight up.
    public void Show(Vector3 at, float size)
    {
        if (root == null) Build();
        if (baseSr.sprite == null) { LoadSprites(); baseSr.sprite = baseSprite; headSr.sprite = headSprite; }
        scale = size;
        Angle = 0f;
        idleClock = 0f;
        root.transform.position = at;
        headSr.transform.localRotation = Quaternion.identity;
        Unfold(0f);
        root.SetActive(true);
    }

    // The deploy pop-in (k 0 -> 1) and the fold-away (k 1 -> 0): both parts grow from 40% of their size.
    public void Unfold(float k)
    {
        if (root == null) return;
        k = Mathf.Clamp01(k);
        float e = 1f - (1f - k) * (1f - k);
        float s = scale * Mathf.Lerp(.4f, 1f, e);
        root.transform.localScale = new Vector3(s, s, 1f);
        var c = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 3f));
        baseSr.color = c;
        headSr.color = c;
    }

    public void SetPosition(Vector3 at) { if (root != null) root.transform.position = at; }

    public void Destroy()
    {
        if (root == null) return;
        if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root);
        root = null;
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    public static float AngleToward(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;   // the drawing points up
    }

    // Turns the head toward `target` by the shortest way round, at most TurnSpeed * dt. Null target: the idle sweep.
    public void Aim(Vector3? target, float dt)
    {
        float want;
        if (target.HasValue) want = AngleToward(Pivot, target.Value);
        else { idleClock += dt; want = Mathf.Sin(idleClock * IdleRate) * IdleSweep; }
        Angle = Mathf.MoveTowardsAngle(Angle, want, TurnSpeed * dt);
        if (headSr != null) headSr.transform.localRotation = Quaternion.Euler(0f, 0f, Angle);
    }

    // How far the head is from facing `target`, 0..180 degrees.
    public float Error(Vector3 target) { return Mathf.Abs(Mathf.DeltaAngle(Angle, AngleToward(Pivot, target))); }

    // Where a shot leaves: the muzzle tip of the head as it points now.
    public Vector3 Muzzle { get { return MuzzleAt(Pivot, Angle, scale); } }

    public static Vector3 MuzzleAt(Vector3 pivot, float angle, float size)
    {
        float r = angle * Mathf.Deg2Rad;
        return pivot + new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f) * (MuzzleReach * size);
    }
}
