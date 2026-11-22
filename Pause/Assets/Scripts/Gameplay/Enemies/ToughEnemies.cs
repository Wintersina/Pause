using UnityEngine;

// TOUGH ENEMIES (combat rework, 2026-11-22). The player's specials are stronger and clearer, so the things they hit take more than one touch.
//
//   * Every ordinary enemy (fighter, alien, big, chaser: any world) carries EnemyHearts: 2 hearts. Each hit that lands -- a floatie bullet or
//     any ship attack / ultimate / secret-power hit (ShipAttackHits.Hit) -- takes exactly ONE heart, whatever its weight, so an enemy needs two
//     hits (two bullets, two attack hits, or one of each). The hit that takes the last heart is an ordinary kill (explosion, sound, score, dust,
//     codex: paid once, in ShipAttackHits.Kill). Two hearts orbit the enemy (EnemyHeartsView, the elites' own HeartOrbit engine, pooled and
//     capped); a hit pops one in a small burst and the other keeps orbiting. A non-lethal hit also flashes the hull white (EnemyFlipbook.Flash) and
//     throws a few white chips (RockHitFx.Chips). No outline, ring or bar is drawn on the enemy.
//   * Hazards (rocks and mines: EnemyDef.IsHazard) carry HazardHits instead: the FIRST hit deflects the hazard (it bounces away from the hit,
//     spins, flashes, throws chips, and stays inside the rails), the SECOND destroys it.
//   * Both ignore a repeat hit inside HitWindow (0.1 s), so one frame of a multi-hit attack counts once.
//   * The tutorial's enemies stay one-hit (EnemyFactory.Create tough: false), so the first lesson is not harder.
//
// Only the hits that go through ShipAttackHits.Hit are softened. Ramming under the shield, the pause-teleport blink, the death combos, shoves
// and hostile fire keep killing outright (their own rules).
public static class Toughness
{
    public const int HeartsPerEnemy = 2;          // a normal enemy
    public const float HitWindow = .1f;        // s: a second hit this soon after the last one is ignored
    public const int MaxHeartViews = 10;       // enemies wearing orbiting hearts at once; past it the hearts still count, only the drawing is skipped
    public const float DeflectSpeed = 2.4f;    // u/s a deflected hazard is thrown at
    public const float DeflectDecay = 2.6f;    // 1/s: how fast the bounce dies away
    public const float DeflectSpin = 220f;     // deg/s of extra spin the bounce adds
    public const int HitChips = 4;

    // Tests: a clock that can be moved by hand (null: Time.time).
    public static System.Func<float> ClockOverride;
    public static float Now { get { return ClockOverride != null ? ClockOverride() : Time.time; } }

    // Edit-mode tests (no frame clock): the ignore window is off unless a test turns it on (with ClockOverride).
    public static float EditModeWindow = 0f;
    public static float Window { get { return Application.isPlaying ? HitWindow : EditModeWindow; } }

    public enum Result { None, Ignored, Hurt, Lethal }

    // Counters (tests, tuning).
    public static int Hurts, Ignored, Deflections;

    public static void ResetCounters() { Hurts = 0; Ignored = 0; Deflections = 0; }

    // Gives a freshly built enemy its hearts (an ordinary enemy) or its deflect state (a hazard). The tutorial builds its enemies without (EnemyFactory.Create tough: false).
    public static void Attach(GameObject go, EnemyDef def)
    {
        if (go == null || def == null) return;
        if (def.IsHazard) { if (go.GetComponent<HazardHits>() == null) go.AddComponent<HazardHits>().Init(def.role == EnemyRole.Mine); }
        else if (go.GetComponent<EnemyHearts>() == null) go.AddComponent<EnemyHearts>().Init(HeartsPerEnemy);
    }

    // The attacker's place for "away from the hit": ShipAttackHits.HitFrom if the caller set it, else the player's ship, else straight below.
    public static Vector3 Origin(GameObject target)
    {
        if (ShipAttackHits.HitFrom.HasValue) return ShipAttackHits.HitFrom.Value;
        var ship = EliteSystem.Player;
        if (ship != null) return ship.position;
        return target.transform.position + Vector3.down;
    }

    // One hit from a weapon on `target`. True when the hit was absorbed (the enemy lives on, or the hit was ignored) so the caller must not
    // destroy it; false when the target is not tough, or this hit takes its last heart (the caller kills it).
    public static bool Absorb(GameObject target)
    {
        if (target == null) return false;
        Result r = Result.None;
        if (target.TryGetComponent(out EnemyHearts hearts)) r = hearts.Strike(Origin(target));
        else if (target.TryGetComponent(out HazardHits hazard)) r = hazard.Strike(Origin(target));
        return r == Result.Hurt || r == Result.Ignored;
    }

    // The shared non-lethal feedback: a white hit flash and a few white chips flying from the side the blow came from.
    public static void HitFx(GameObject go, Vector3 from, float radius)
    {
        EnemyFlipbook.Flash(go);
        Vector2 toward = (Vector2)(from - go.transform.position);
        if (toward.sqrMagnitude < 1e-4f) toward = Vector2.down;
        toward.Normalize();
        Vector3 rim = go.transform.position + new Vector3(toward.x, toward.y, 0f) * radius;
        RockHitFx.Chips(rim, toward, Color.white, HitChips);
    }
}

// An ordinary enemy's hearts.
public class EnemyHearts : MonoBehaviour
{
    public int Max { get; private set; }
    public int Hearts { get; private set; }
    public int Strikes { get; private set; }
    public int IgnoredHits { get; private set; }
    public EnemyHeartsView View { get; private set; }

    float lastHit = -999f;
    bool impactPending;
    Vector3 impactAt;

    public void Init(int hearts)
    {
        Max = Mathf.Max(1, hearts);
        Hearts = Max;
        lastHit = -999f;
        if (EnemyHeartsView.Live < Toughness.MaxHeartViews && GetComponent<SpriteRenderer>() != null)
        {
            View = gameObject.AddComponent<EnemyHeartsView>();
            View.Bind(this);
        }
    }

    // One heart off, unless this hit is too soon after the last (or it is already dead).
    public Toughness.Result Strike(Vector3 from, int amount = 1)
    {
        if (Hearts <= 0) return Toughness.Result.Ignored;
        float now = Toughness.Now;
        if (now - lastHit < Toughness.Window) { IgnoredHits++; Toughness.Ignored++; return Toughness.Result.Ignored; }
        lastHit = now;
        Strikes++;
        Hearts = Mathf.Max(0, Hearts - Mathf.Max(1, amount));
        if (Hearts <= 0) return Toughness.Result.Lethal;
        Toughness.Hurts++;
        impactPending = true;
        impactAt = from;
        if (View != null) View.Refresh();
        float r = ClearTarget.MeasureRadius(gameObject);
        Toughness.HitFx(gameObject, from, Mathf.Max(.15f, r));
        return Toughness.Result.Hurt;
    }

    public bool TakeImpact(out Vector3 at)
    {
        at = impactAt;
        bool had = impactPending;
        impactPending = false;
        return had;
    }
}

// The hearts orbiting an ordinary enemy: the elites' HeartOrbit engine (same heart art, same Atom orbit, same dart / shield / crumble when a
// heart goes), a little smaller, kept outside the hull so they never cover the drawing. Capped (Toughness.MaxHeartViews).
[DefaultExecutionOrder(50)]
public class EnemyHeartsView : HeartOrbit
{
    public const float Size = .11f;
    public const float Clearance = .04f;
    static int live;
    public static int Live { get { return live; } }
    public static void ResetLive() { live = 0; }

    EnemyHearts owner;
    SpriteRenderer body;
    bool counted;

    public void Bind(EnemyHearts h)
    {
        owner = h;
        body = GetComponent<SpriteRenderer>();
        heartSize = Size;
        personalSpace = 1.3f;
        if (!counted) { counted = true; live++; }
        BuildHearts();
        Update();
    }

    void OnDestroy()
    {
        if (counted) { counted = false; live = Mathf.Max(0, live - 1); }
    }

    protected override void Start() { }   // built by Bind

    public override void BuildHearts()
    {
        if (owner != null) BuildHearts(owner.Max);
    }

    public void Refresh() { Update(); }

    protected override int RemainingHearts() { return owner != null ? owner.Hearts : 0; }

    protected override bool TakeImpact(out Vector3 at)
    {
        if (owner != null) return owner.TakeImpact(out at);
        at = transform.position;
        return false;
    }

    protected override bool Outlined { get { return true; } }

    protected override Sprite HeartSprite() { return Resources.Load<Sprite>(EliteHearts.SpritePath); }

    protected override Color HeartTint { get { return ShipLivesIndicator.HeartTint; } }

    protected override void ResolveStyle(out HeartStyle s, out OrbitStyle o)
    {
        s = HeartStyle.Atom;
        o = ShipHeartStyles.Orbit(s);
        o.radius = 1f;
        o.flourishEvery = 0f;
        styleKey = 3;
    }

    protected override Bounds HullBounds()
    {
        if (body != null && body.sprite != null)
        {
            Bounds b = body.bounds;
            float r = Mathf.Max(.15f, Mathf.Min(b.extents.x, b.extents.y) * .9f);
            return new Bounds(transform.position, new Vector3(r * 2f, r * 2f, .1f));
        }
        return new Bounds(transform.position, new Vector3(.5f, .5f, .1f));
    }

    // Outside the drawing: the hull's radius, the heart's visible half and a margin.
    protected override Vector2 OrbitRadiiFor(Bounds hull)
    {
        float r = hull.extents.x + BossHearts.VisibleHalf(heartSize) + Clearance;
        return new Vector2(r, r);
    }

    protected override bool HidesBehindHull(Vector2 world)
    {
        Vector2 c = transform.position;
        float r = HullBounds().extents.x;
        return (world - c).sqrMagnitude < r * r;
    }

    protected override SpriteRenderer HullRenderer() { return body; }
}

// A hazard (rock or mine) that survives its first hit: the first hit deflects it, the second destroys it.
public class HazardHits : MonoBehaviour
{
    public bool Deflected { get; private set; }
    public int Strikes { get; private set; }
    public int IgnoredHits { get; private set; }
    public Vector2 Velocity { get { return vel; } }

    bool railBound;
    float lastHit = -999f, spin, xOff, lastX = float.NaN;
    Vector2 vel;

    public void Init(bool mine) { railBound = mine; lastHit = -999f; }

    public Toughness.Result Strike(Vector3 from)
    {
        float now = Toughness.Now;
        if (now - lastHit < Toughness.Window) { IgnoredHits++; Toughness.Ignored++; return Toughness.Result.Ignored; }
        lastHit = now;
        Strikes++;
        if (Deflected) return Toughness.Result.Lethal;
        Deflected = true;
        Toughness.Deflections++;
        Toughness.Hurts++;
        Deflect(from);
        Toughness.HitFx(gameObject, from, Mathf.Max(.15f, ClearTarget.MeasureRadius(gameObject)));
        return Toughness.Result.Hurt;
    }

    // Thrown away from the hit with a little spin. A rail mine only slides along its rail (up or down), never sideways.
    void Deflect(Vector3 from)
    {
        Vector2 away = (Vector2)(transform.position - from);
        if (away.sqrMagnitude < 1e-4f) away = Vector2.up;
        away.Normalize();
        // never thrown down the lane at the player: the bounce always has a share of "back up"
        if (away.y < .25f) away.y = .25f;
        // and a sideways share, so it visibly changes direction
        if (Mathf.Abs(away.x) < .35f) away.x = (away.x > .001f ? 1f : away.x < -.001f ? -1f : (transform.position.x > 0f ? -1f : 1f)) * .35f;
        if (railBound) away.x = 0f;
        away.Normalize();
        vel = away * Toughness.DeflectSpeed;
        float dir = away.x >= 0f ? -1f : 1f;
        spin = dir * Toughness.DeflectSpin;
    }

    void LateUpdate()
    {
        if (!Deflected) return;
        if (buttonClicks.playerDied) return;
        if (!(TouchInput.IsPressed || score.pauseCounter <= 0)) return;
        Step(Time.deltaTime);
    }

    // One frame of the bounce (dt explicit for tests). Runs after the mover (LateUpdate), so it adds to wherever the mover put the hazard.
    public void Step(float dt)
    {
        if (!Deflected || dt <= 0f) return;
        if (vel.sqrMagnitude < .0004f && Mathf.Abs(spin) < 1f) { vel = Vector2.zero; spin = 0f; return; }
        Vector3 p = transform.position;
        float dx = vel.x * dt, dy = vel.y * dt;
        bool wrote = !float.IsNaN(lastX) && Mathf.Abs(p.x - lastX) > 1e-4f;   // the mover wrote x itself (a weave): lay the whole offset over it
        if (!railBound) xOff += dx;
        float x = railBound ? p.x : (wrote ? p.x + xOff : p.x + dx);
        // inside the rails: bounce off the edge
        float lim = Mathf.Max(.3f, SpaceAsteroidField.Edge - .3f);
        if (!railBound)
        {
            if (x > lim) { xOff -= x - lim; x = lim; vel.x = -Mathf.Abs(vel.x); }
            else if (x < -lim) { xOff += -lim - x; x = -lim; vel.x = Mathf.Abs(vel.x); }
        }
        p.x = x;
        p.y += dy;
        transform.position = p;
        lastX = x;
        transform.Rotate(0f, 0f, spin * dt);
        float decay = Mathf.Exp(-Toughness.DeflectDecay * dt);
        vel *= decay;
        spin *= decay;
    }
}
