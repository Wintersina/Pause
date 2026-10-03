using System.Collections.Generic;
using UnityEngine;

// The ship's secret power: a second, slower meter that fills from star dust
// and kills (never on its own), shown by the SecretMeter badge beside the
// hull. Once full it waits for its trigger (ShipLoadout.trigger) -- the
// moment the power is actually useful -- and fires itself. No new controls.
//
// Hit-avoidance powers (Shield Pulse, Phase Cloak, Blink Dash) fire when a
// hazard is about to touch the hull: predicted every frame from the hazard
// registry, and, as a guarantee, collisionDetection asks InterceptHit()
// before it applies damage, so a full meter always spends itself on the hit
// rather than letting it land. Hard Shell sits up waiting and eats the next
// hit the same way.
//
// Everything runs on gameplay time and freezes with the world; the drawn
// effects come from AttackPool; the bookkeeping lists are preallocated.
public class SecretPowerController : MonoBehaviour
{
    public const float Full = 100f;

    [Header("Meter")]
    public float perSmallDust = 3f;
    public float perLargeDust = 6f;
    public float perKill = 5f;

    [Header("Tuning")]
    public float lookahead = .28f;
    public float cloakSeconds = 4f;
    public float pulseRadius = 1.4f;
    public float pulseInvulnerable = .75f;
    public float stunSeconds = 3f;
    public float novaRadius = 2.1f;
    public float magnetSeconds = 5f;
    public float magnetSpeed = 7f;
    public float clapRadius = 2.6f;
    public float clapDistance = 2.4f;
    public float decoySeconds = 2.5f;
    public float decoyBlastRadius = 1.5f;
    public float bubbleScale = .45f;
    public float bubbleSeconds = 2.5f;
    public float dashDistance = 1.25f;
    public float dashSeconds = .7f;
    public float holeSeconds = 2.6f;
    public float holePullRadius = 2.7f;
    public float holeKillRadius = .45f;
    public int showerStars = 10;
    public float shellSeconds = 8f;

    public static SecretPowerController Instance { get; private set; }

    int ship;
    ShipLoadout loadout;
    float meter;
    float hullRadius = .28f;
    SecretMeter badge;

    public int Ship => ship;
    public ShipLoadout Loadout => loadout;
    public SecretPower Power => loadout.power;
    public float Meter => meter;
    public float Meter01 => Mathf.Clamp01(meter / Full);
    public bool Ready => meter >= Full;
    public int FireCount { get; private set; }
    public SecretMeter Badge => badge;

    // Ongoing effects, all counted down on gameplay time.
    float stunLeft, magnetLeft, clapLeft, decoyLeft, bubbleLeft, dashLeft, holeLeft, shellLeft;
    Vector3 holeAt;
    AttackSprite holeFx, shellFx, magnetFx, bubbleFx, decoyFx, cloakFx;

    const int MaxLocked = 48;
    readonly Behaviour[] locked = new Behaviour[MaxLocked];
    int lockedCount;
    readonly Transform[] pushed = new Transform[32];
    int pushedCount;
    readonly List<ClearTarget> scratch = new List<ClearTarget>(64);

    public bool ShellUp => shellLeft > 0f;
    public bool HoleActive => holeLeft > 0f;
    public bool Stunning => stunLeft > 0f;
    public int LockedCount => lockedCount;

    // Blink Dash: how far the hull is thrown sideways off the finger
    // (movePlayer adds it), easing back over the last of the dash.
    public static float DashOffsetX { get; private set; }

    public static SecretPowerController Attach(GameObject go, int shipId)
    {
        var c = go.GetComponent<SecretPowerController>();
        if (c == null) c = go.AddComponent<SecretPowerController>();
        c.Setup(shipId);
        return c;
    }

    public void Setup(int shipId)
    {
        Instance = this;
        ship = shipId;
        loadout = ShipLoadoutTable.For(shipId);
        var hull = GetComponent<SpriteRenderer>();
        if (hull != null && hull.sprite != null)
        {
            Vector3 e = hull.sprite.bounds.extents;
            Vector3 s = transform.lossyScale;
            hullRadius = Mathf.Max(.15f, Mathf.Min(e.x * Mathf.Abs(s.x), e.y * Mathf.Abs(s.y)) * .9f);
        }
        if (badge == null) badge = SecretMeter.Attach(this);
    }

    void Awake() { Instance = this; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        EndAll();
    }

    // ---------------------------------------------------------------- meter

    public void Add(float amount)
    {
        if (amount <= 0f) return;
        meter = Mathf.Min(Full, meter + amount);
    }

    public void SetMeter(float value) { meter = Mathf.Clamp(value, 0f, Full); }

    // collisionDetection's pickup and kill hooks.
    public static void OnDust(bool large)
    {
        var c = Instance;
        if (c != null) c.Add(large ? c.perLargeDust : c.perSmallDust);
    }

    public static void OnKill()
    {
        var c = Instance;
        if (c != null) c.Add(c.perKill);
    }

    // ---------------------------------------------------------------- tick

    void Update()
    {
        Step(ShipAttackRunner.ScaledDelta());
    }

    public void Step(float dt)
    {
        if (dt <= 0f || buttonClicks.playerDied) return;
        TickEffects(dt);
        if (Ready && ShouldTrigger()) Fire();
    }

    public bool ShouldTrigger()
    {
        Vector3 at = transform.position;
        int n = loadout.triggerCount;
        switch (loadout.trigger)
        {
            case PowerTrigger.ImminentHit:
                return ShipTargets.Imminent(at, hullRadius, lookahead) != null;
            case PowerTrigger.EnemiesOnScreen:
                return ShipTargets.CountOnScreen(true) >= n;
            case PowerTrigger.HazardsNearShip:
                return ShipTargets.CountNear(at, loadout.triggerRadius) >= n;
            case PowerTrigger.TargetsWhileCharging:
                {
                    var power = ShipPowerController.Instance;
                    return ShipTargets.CountOnScreen(false) >= n && power != null && power.Charge01 < .65f;
                }
            case PowerTrigger.LowPauses:
                return score.pauseCounter <= n;
            case PowerTrigger.PickupsOnScreen:
                return ShipTargets.CountPickupsOnScreen() >= n;
            case PowerTrigger.EnemyClosing:
                return ChaserClosing(at, 4.5f) || ShipTargets.CountNear(at, loadout.triggerRadius, true) >= n;
            case PowerTrigger.ScreenCalm:
                return ShipTargets.CountOnScreen(false) <= n;
            case PowerTrigger.HullDamaged:
                return collisionDetection.lifeCounter > 0;
            case PowerTrigger.HazardInLane:
                return HazardInLane(at, loadout.triggerRadius);
        }
        return false;
    }

    static bool ChaserClosing(Vector3 at, float within)
    {
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!ShipTargets.IsHazard(t)) continue;
            var chaser = t.GetComponent<ChaserEnemy>();
            if (chaser == null || !chaser.IsChasing) continue;
            if (((Vector2)(t.transform.position - at)).sqrMagnitude <= within * within) return true;
        }
        return false;
    }

    bool HazardInLane(Vector3 at, float ahead)
    {
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!ShipTargets.IsHazard(t)) continue;
            Vector3 p = t.transform.position;
            float dy = p.y - at.y;
            if (dy > 0f && dy <= ahead && Mathf.Abs(p.x - at.x) <= hullRadius + t.Radius) return true;
        }
        return false;
    }

    // Called by collisionDetection the instant a hazard touches a ship that
    // is not already invulnerable. True: the hit is absorbed (the caller
    // treats it as a shielded hit).
    public static bool InterceptHit(GameObject hazard)
    {
        var c = Instance;
        if (c == null) return false;
        if (c.ShellUp)
        {
            c.shellLeft = 0f;
            if (c.shellFx != null && c.shellFx.Active) c.shellFx.Stop();
            c.shellFx = null;
            c.Burst(c.transform.position, 2.2f, null);
            return true;
        }
        if (c.Ready && c.loadout.trigger == PowerTrigger.ImminentHit && !buttonClicks.playerDied)
        {
            c.Fire(hazard);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- fire

    public void Fire(GameObject threat = null)
    {
        meter = 0f;
        FireCount++;
        if (badge != null) badge.Flash();
        Vector3 at = transform.position;
        switch (loadout.power)
        {
            case SecretPower.ShieldPulse:
                collisionDetection.BeginCloak(pulseInvulnerable);
                Burst(at, pulseRadius * 2.3f, transform);
                KillWithin(at, pulseRadius);
                break;

            case SecretPower.EmpStun:
                Burst(at, 6f, transform);
                Stun();
                break;

            case SecretPower.SolarNova:
                Burst(at, novaRadius * 2.3f, transform);
                if (KillWithin(at, novaRadius) > 0) WorldTimeFx.HitStop(.06f);
                CameraKick.Kick(.04f);
                break;

            case SecretPower.CapacitorDump:
                {
                    var power = ShipPowerController.Instance;
                    if (power != null) power.RechargeNow();
                    Burst(at + Vector3.up * .5f, 1.6f, transform);
                }
                break;

            case SecretPower.IonBattery:
                score.incromentPause();
                Burst(at, 2f, transform);
                break;

            case SecretPower.PhaseCloak:
                cloakFx = BeginPhaseCloak(transform, ship, cloakSeconds);
                break;

            case SecretPower.GoldMagnet:
                magnetLeft = magnetSeconds;
                magnetFx = Loop(at, 3.2f, magnetSeconds, transform, 41);
                break;

            case SecretPower.Thunderclap:
                Burst(at, clapRadius * 2.2f, transform);
                Clap(at);
                break;

            case SecretPower.FlareDecoy:
                {
                    Rect view = ShipTargets.View(-.4f);
                    float side = at.x > 0f ? -1f : 1f;
                    Vector3 spot = new Vector3(Mathf.Clamp(at.x + side * 1.2f, view.xMin, view.xMax), at.y + .9f, 0f);
                    decoyLeft = decoySeconds;
                    ShipDecoy.Place(spot);
                    decoyFx = Loop(spot, .9f, decoySeconds, null, 66);
                }
                break;

            case SecretPower.TimeBubble:
                bubbleLeft = bubbleSeconds;
                WorldTimeFx.BubbleScale = bubbleScale;
                bubbleFx = Loop(at, 3.6f, bubbleSeconds, transform, 41);
                break;

            case SecretPower.BlinkDash:
                Dash(threat);
                break;

            case SecretPower.BlackHole:
                {
                    Rect view = ShipTargets.View(-.8f);
                    holeAt = new Vector3(Mathf.Clamp(at.x, view.xMin, view.xMax), Mathf.Min(at.y + 2.6f, view.yMax), 0f);
                    holeLeft = holeSeconds;
                    holeFx = Loop(holeAt, 2.4f, holeSeconds, null, 67);
                    Burst(holeAt, 2.6f, null);
                }
                break;

            case SecretPower.StarShower:
                Shower(at);
                Burst(at + Vector3.up * 2.4f, 3f, null);
                break;

            case SecretPower.Mending:
                if (collisionDetection.lifeCounter > 0) collisionDetection.lifeCounter--;
                Burst(at, 2f, transform);
                break;

            case SecretPower.HardShell:
                shellLeft = shellSeconds;
                shellFx = Loop(at, 1.25f, shellSeconds, transform, 45);
                break;
        }
    }

    // ---------------------------------------------------------------- effects

    // Phase Cloak: real invulnerability on collisionDetection's own cloak
    // clock (scaled time, like the shield), with the phase shimmer riding on
    // the hull for exactly as long. Not the blue-atom shield: no boost, no
    // music change, and neither can shorten the other.
    public static AttackSprite BeginPhaseCloak(Transform hull, int shipId, float seconds)
    {
        collisionDetection.BeginCloak(seconds);
        Vector3 at = hull != null ? hull.position : Vector3.zero;
        AttackPool.Sprite().Play(AttackSprite.Source.PowerBurst, shipId, at, Vector3.one * 2.4f, 0f,
            ShipFxArt.PowerBurstTicks, false, 0f, hull, Vector3.zero, 73);
        var aura = AttackPool.Sprite().Play(AttackSprite.Source.PowerLoop, shipId, at, Vector3.one * 1.6f, 0f,
            ShipFxArt.PowerLoopTicks, true, seconds, hull, Vector3.zero, 40);
        aura.Rename(CloakAuraName);
        return aura;
    }

    public const string CloakAuraName = "~CloakAura";

    AttackSprite Burst(Vector3 at, float size, Transform follow)
    {
        return AttackPool.Sprite().Play(AttackSprite.Source.PowerBurst, ship, at, Vector3.one * size, 0f,
            ShipFxArt.PowerBurstTicks, false, 0f, follow, Vector3.zero, 73);
    }

    AttackSprite Loop(Vector3 at, float size, float life, Transform follow, int order)
    {
        return AttackPool.Sprite().Play(AttackSprite.Source.PowerLoop, ship, at, Vector3.one * size, 0f,
            ShipFxArt.PowerLoopTicks, true, life, follow, Vector3.zero, order);
    }

    int KillWithin(Vector3 at, float radius)
    {
        ShipTargets.Collect(scratch);
        float budget = 1f;
        int kills = 0;
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector2 d = t.transform.position - at;
            float r = radius + t.Radius * .5f;
            if (d.sqrMagnitude > r * r) continue;
            if (ShipAttackHits.Hit(t.gameObject, ship, 1f, ref budget)) kills++;
        }
        return kills;
    }

    void Lock(GameObject go)
    {
        lockedCount = HazardMovers.Disable(go, locked, lockedCount);
    }

    void Stun()
    {
        ReleaseLocked();
        ShipTargets.Collect(scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.CompareTag("Enimey")) continue;
            int before = lockedCount;
            Lock(t.gameObject);
            if (lockedCount > before)
                AttackPool.Sprite().Play(AttackSprite.Source.PowerLoop, ship, t.transform.position,
                    Vector3.one * Mathf.Max(.7f, t.Radius * 2.4f), 0f, ShipFxArt.PowerLoopTicks, true, stunSeconds,
                    t.transform, Vector3.zero, 67);
        }
        stunLeft = stunSeconds;
    }

    void ReleaseLocked()
    {
        for (int i = 0; i < lockedCount; i++)
        {
            if (locked[i] != null) locked[i].enabled = true;
            locked[i] = null;
        }
        lockedCount = 0;
    }

    void Clap(Vector3 at)
    {
        pushedCount = 0;
        ShipTargets.Collect(scratch);
        for (int i = 0; i < scratch.Count && pushedCount < pushed.Length; i++)
        {
            var t = scratch[i];
            if (t == null) continue;
            Vector2 d = t.transform.position - at;
            float r = clapRadius + t.Radius;
            if (d.sqrMagnitude > r * r) continue;
            pushed[pushedCount++] = t.transform;
        }
        clapLeft = .25f;
    }

    void Dash(GameObject threat)
    {
        Vector3 at = transform.position;
        float dir;
        if (threat != null && Mathf.Abs(threat.transform.position.x - at.x) > .05f)
            dir = at.x > threat.transform.position.x ? 1f : -1f;
        else
            dir = at.x > 0f ? -1f : 1f;
        // don't dash into the wall: go the other way if there's no room
        if (Mathf.Abs(at.x + dir * dashDistance) > 2.4f) dir = -dir;
        DashOffsetX = dir * dashDistance;
        dashLeft = dashSeconds;
        collisionDetection.BeginCloak(.6f);
        Burst(at, 1.4f, null);
        transform.position = new Vector3(Mathf.Clamp(at.x + DashOffsetX, -2.4f, 2.4f), at.y, at.z);
        Burst(transform.position, 1.2f, transform);
    }

    void Shower(Vector3 at)
    {
        var prefab = Resources.Load<GameObject>("prefabs/smStar_1");
        if (prefab == null) return;
        Rect view = ShipTargets.View(-.35f);
        for (int i = 0; i < showerStars; i++)
        {
            float k = showerStars <= 1 ? .5f : i / (float)(showerStars - 1);
            float x = Mathf.Clamp(at.x + Mathf.Lerp(-1.6f, 1.6f, k), view.xMin, view.xMax);
            float y = Mathf.Min(at.y + 2.2f + Mathf.Sin(k * Mathf.PI) * 1.1f, view.yMax);
            var star = Object.Instantiate(prefab, new Vector3(x, y, 0f), Quaternion.identity);
            // keep the arc: the star's sideways weave would collapse it
            var weave = star.GetComponent<moveEnimes>();
            if (weave != null) weave.enabled = false;
        }
    }

    void TickEffects(float dt)
    {
        if (stunLeft > 0f && (stunLeft -= dt) <= 0f) { stunLeft = 0f; ReleaseLocked(); }

        if (magnetLeft > 0f)
        {
            magnetLeft -= dt;
            Vector3 at = transform.position;
            var live = ClearTarget.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var t = live[i];
                if (!ShipTargets.IsPickup(t)) continue;
                if (ShipTargets.View().Contains(t.transform.position) && lockedCount < MaxLocked) Lock(t.gameObject);
                t.transform.position = Vector3.MoveTowards(t.transform.position, at, magnetSpeed * dt);
            }
            if (magnetLeft <= 0f) { magnetLeft = 0f; ReleaseLocked(); }
        }

        if (clapLeft > 0f)
        {
            float step = clapDistance / .25f * Mathf.Min(dt, clapLeft);
            for (int i = 0; i < pushedCount; i++)
                if (pushed[i] != null) pushed[i].position += Vector3.up * step;
            if ((clapLeft -= dt) <= 0f) { clapLeft = 0f; pushedCount = 0; }
        }

        if (decoyLeft > 0f && (decoyLeft -= dt) <= 0f)
        {
            decoyLeft = 0f;
            Vector3 spot = ShipDecoy.Position;
            ShipDecoy.Clear();
            Burst(spot, decoyBlastRadius * 2.3f, null);
            KillWithin(spot, decoyBlastRadius);
        }

        if (bubbleLeft > 0f && (bubbleLeft -= dt) <= 0f) { bubbleLeft = 0f; WorldTimeFx.BubbleScale = 1f; }

        if (dashLeft > 0f)
        {
            dashLeft -= dt;
            // hold the sidestep, then ease back onto the finger
            float k = Mathf.Clamp01(dashLeft / .3f);
            DashOffsetX = Mathf.Sign(DashOffsetX) * dashDistance * k;
            if (dashLeft <= 0f) { dashLeft = 0f; DashOffsetX = 0f; }
        }

        if (holeLeft > 0f)
        {
            holeLeft -= dt;
            StepHole(dt);
            if (holeLeft <= 0f) { holeLeft = 0f; ReleaseLocked(); }
        }

        if (shellLeft > 0f && (shellLeft -= dt) <= 0f) shellLeft = 0f;
    }

    void StepHole(float dt)
    {
        ShipTargets.Collect(scratch);
        float budget = .34f * dt; // a boss caught in it takes a trickle, not a hit
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector3 p = t.transform.position;
            Vector2 d = holeAt - p;
            float dist = d.magnitude;
            if (dist > holePullRadius + t.Radius) continue;
            if (t.GetComponent<IShipAttackTarget>() != null)
            {
                ShipAttackHits.Hit(t.gameObject, ship, budget, ref budget);
                continue;
            }
            if (dist <= holeKillRadius + t.Radius * .5f)
            {
                ShipAttackHits.Hit(t.gameObject, ship);
                continue;
            }
            if (lockedCount < MaxLocked) Lock(t.gameObject);
            // pulls harder the closer it gets, with a swirl
            float pull = Mathf.Lerp(7f, 2.5f, dist / holePullRadius);
            Vector3 swirl = new Vector3(-d.y, d.x, 0f).normalized * 1.6f;
            t.transform.position = p + ((Vector3)d.normalized * pull + swirl) * dt;
        }
    }

    void EndAll()
    {
        ReleaseLocked();
        if (bubbleLeft > 0f) WorldTimeFx.BubbleScale = 1f;
        stunLeft = magnetLeft = clapLeft = decoyLeft = bubbleLeft = dashLeft = holeLeft = shellLeft = 0f;
        DashOffsetX = 0f;
        ShipDecoy.Clear();
    }
}

// Where the Flare Decoy is burning, for ChaserEnemy to hunt instead of the
// player.
public static class ShipDecoy
{
    public static bool Active { get; private set; }
    public static Vector3 Position { get; private set; }

    public static void Place(Vector3 at) { Active = true; Position = at; }
    public static void Clear() { Active = false; }
}

// Freezes a hazard in place by switching off whatever moves it (EMP stun,
// a black hole's grip, the magnet's pull). The caller keeps the switched-off
// components in its own fixed array and turns them back on.
public static class HazardMovers
{
    public static int Disable(GameObject go, Behaviour[] into, int count)
    {
        count = Add(go.GetComponent<moveEnimes>(), into, count);
        count = Add(go.GetComponent<moveItemEnmInStrightLine>(), into, count);
        count = Add(go.GetComponent<ChaserEnemy>(), into, count);
        return count;
    }

    static int Add(Behaviour b, Behaviour[] into, int count)
    {
        if (b == null || !b.enabled || count >= into.Length) return count;
        b.enabled = false;
        into[count] = b;
        return count + 1;
    }
}
