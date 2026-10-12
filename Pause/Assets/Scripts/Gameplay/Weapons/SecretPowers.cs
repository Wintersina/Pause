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
    public float pulseInvulnerable = 1.2f;
    public float pulseShotRadius = 2.2f;   // the pulse ring also deletes enemy shots this close
    public float stunSeconds = 3f;
    public float novaRadius = 3f;
    public float novaBurnSeconds = 2f;     // the ring the nova leaves behind
    public float ionSlowSeconds = 2f;      // Ion Battery's slow-time after the +2 pauses
    public float ionSlowScale = .5f;
    public float ghostAlpha = .4f;         // Phase Cloak: how solid the hull stays
    public float magnetSeconds = 5f;
    public float magnetDustMultiplier = 2f;
    public float magnetSpeed = 7f;
    public float clapRadius = 2.6f;
    public float decoySeconds = 2.5f;
    public float decoyBlastRadius = 1.5f;
    public float bubbleScale = .45f;
    public float bubbleRadius = 1.8f;      // the Time Bubble dome's radius (drawn, and the slow reads as inside it)
    public float bubbleSeconds = 2.5f;
    public float dashDistance = 1.25f;
    public float dashThreatRadius = 2f;    // Blink Dash also fires proactively when a hazard is this close
    public float dashSeconds = .7f;
    public float holeSeconds = 2.6f;
    public float holePullRadius = 2.7f;
    public float holeKillRadius = .45f;
    public float tractorSeconds = 1.8f;    // UFO Tractor Pull: how long the pull lasts
    public float tractorPullRadius = 4f;
    public float tractorPullSpeed = 6f;
    public float tractorKillRadius = 1f;   // reaching the ship's tractor field kills (and drops dust)
    public int tractorStarsPerKill = 2;
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
    float stunLeft, magnetLeft, tractorLeft, decoyLeft, bubbleLeft, dashLeft, holeLeft, shellLeft, novaLeft, ghostLeft;
    Vector3 holeAt, novaAt;
    SpriteRenderer ghostHull;
    float ghostEmit;
    Vector3 lastGhostAt;
    AttackSprite holeFx, shellFx, magnetFx, bubbleFx, decoyFx, cloakFx;

    const int MaxLocked = 48;
    readonly Behaviour[] locked = new Behaviour[MaxLocked];
    int lockedCount;
    readonly List<ClearTarget> scratch = new List<ClearTarget>(64);

    public bool ShellUp => shellLeft > 0f;
    public bool HoleActive => holeLeft > 0f;
    public bool Stunning => stunLeft > 0f;
    public bool Burning => novaLeft > 0f;
    public bool GhostActive => ghostLeft > 0f;
    public bool MagnetActive => magnetLeft > 0f;
    public bool TractorActive => tractorLeft > 0f;
    public bool DecoyActive => decoyLeft > 0f;
    public bool BubbleActive => bubbleLeft > 0f && loadout.power == SecretPower.TimeBubble;
    public AttackSprite ShellFx => shellFx;
    public AttackSprite BubbleFx => bubbleFx;
    public AttackSprite DecoyFx => decoyFx;

    // Counters (tests, previews).
    public int ClapKills { get; private set; }
    public int Mends { get; private set; }
    public int AegisShields { get; private set; }
    public int ShellBreaks { get; private set; }
    public int TractorKills { get; private set; }
    public int TractorStars { get; private set; }
    public int DashCount { get; private set; }
    public float LastDashDirection { get; private set; }

    public const string ShellAuraName = "~ShellAura";
    public const string BubbleDomeName = "~BubbleDome";
    public static readonly Color BubbleTint = new Color(.45f, .8f, 1f, .55f);
    public static readonly Color DecoyTint = new Color(1f, .82f, .35f, 1f);

    // Phase Cloak: hazards pass through the ghosted hull (collisionDetection).
    public static bool Ghosted { get { var c = Instance; return c != null && c.ghostLeft > 0f; } }

    // Gold Magnet: star dust is worth this much while the magnet pulls.
    public static float DustMultiplier
    {
        get { var c = Instance; return c != null && c.magnetLeft > 0f ? c.magnetDustMultiplier : 1f; }
    }
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
        ghostHull = hull;
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
        // a timed meter (Aegis Shield) fills with time only
        if (loadout.power == SecretPower.AegisShield) return;
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
        if (loadout.power == SecretPower.AegisShield && !Ready && !ShipPowerController.TransitionFrozen)
            meter = Mathf.Min(Full, meter + Full * dt / Mathf.Max(.5f, PremiumShips.Current(ship).shieldEvery));
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
            case PowerTrigger.ThreatClose:
                return ShipTargets.Imminent(at, hullRadius, lookahead) != null ||
                       ShipTargets.CountNear(at, loadout.triggerRadius) >= n;
            case PowerTrigger.EnemiesOnScreen:
                return ShipTargets.CountOnScreen(true) >= n;
            case PowerTrigger.HazardsNearShip:
                return ShipTargets.CountNear(at, loadout.triggerRadius) >= n;
            case PowerTrigger.TargetsOnScreen:
                return ShipTargets.CountOnScreen(false) >= n;
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
            case PowerTrigger.Always:
                return true;
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
            c.BreakShell();
            return true;
        }
        if (c.Ready && (c.loadout.trigger == PowerTrigger.ImminentHit || c.loadout.trigger == PowerTrigger.ThreatClose) && !buttonClicks.playerDied)
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
        AchievementTracker.OnSecretPower();
        if (badge != null) badge.Flash();
        Vector3 at = transform.position;
        switch (loadout.power)
        {
            case SecretPower.ShieldPulse:
                collisionDetection.BeginCloak(pulseInvulnerable);
                Burst(at, pulseShotRadius * 2f, transform);
                KillWithin(at, pulseRadius);
                HostileShots.ClearWithin(at, pulseShotRadius);
                break;

            case SecretPower.EmpStun:
                Burst(at, 6f, transform);
                Stun();
                HostileShots.ClearIn(ShipTargets.View());
                break;

            case SecretPower.SolarNova:
                Burst(at, novaRadius * 2.3f, transform);
                if (KillWithin(at, novaRadius) > 0) WorldTimeFx.HitStop(.06f);
                CameraKick.Kick(.04f);
                // ...and the ring stays burning where it went off
                novaAt = at;
                novaLeft = novaBurnSeconds;
                Loop(at, novaRadius * 2.2f, novaBurnSeconds, null, 66);
                break;

            case SecretPower.CapacitorDump:
                {
                    // the rail goes off right now, on top of the usual countdown
                    var runner = GetComponent<ShipAttackRunner>();
                    if (runner != null && runner.Fire()) UltimateShotSound.Play(ship);
                    Burst(at + Vector3.up * .5f, 1.6f, transform);
                }
                break;

            case SecretPower.IonBattery:
                score.incromentPause();
                PowerTrailSprite.Pack(ship, at);
                bubbleLeft = ionSlowSeconds;
                WorldTimeFx.BubbleScale = ionSlowScale;
                Burst(at, 2f, transform);
                break;

            case SecretPower.PhaseCloak:
                cloakFx = BeginPhaseCloak(transform, ship, cloakSeconds);
                BeginGhost(cloakSeconds);
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
                    decoyFx.SetColor(DecoyTint);
                }
                break;

            case SecretPower.TimeBubble:
                bubbleLeft = bubbleSeconds;
                WorldTimeFx.BubbleScale = bubbleScale;
                bubbleFx = Loop(at, bubbleRadius * 2f, bubbleSeconds, transform, 41);
                bubbleFx.Rename(BubbleDomeName);
                bubbleFx.SetColor(BubbleTint);
                Burst(at, bubbleRadius * 2.4f, transform).SetColor(BubbleTint);
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

            case SecretPower.TractorPull:
                tractorLeft = tractorSeconds;
                Loop(at + Vector3.up * 1.6f, tractorPullRadius * 1.1f, tractorSeconds, transform, 66);
                Burst(at, 2.4f, transform);
                break;

            case SecretPower.Mending:
                if (collisionDetection.lifeCounter > 0) { collisionDetection.lifeCounter--; Mends++; }
                // a feather burst round the hull, a second one rising off it, and the word
                Burst(at, 2.6f, transform).SetColor(PickupGlow.HealGreen);
                Burst(at + Vector3.up * .7f, 1.5f, null);
                var hud = ScoreHud.Current;
                if (hud != null) hud.ShowWord(collisionDetection.HealWord, at + Vector3.up * collisionDetection.PickupWordLift, PickupGlow.HealGreen, 28);
                break;

            case SecretPower.AegisShield:
                {
                    // the blue-atom shield (same art, same absorb rule), on a timer
                    var hit = GetComponent<collisionDetection>();
                    if (hit != null) hit.RaiseShield(PremiumShips.Current(ship).shieldSeconds);
                    Burst(at, 2.4f, transform);
                    AegisShields++;
                }
                break;

            case SecretPower.HardShell:
                shellLeft = shellSeconds;
                // The shell is a shield: the ship's hit zone grows to the shield zone.
                ShipHitbox.SetShield(gameObject, ShipHitbox.Source.Shell, true);
                shellFx = ShellLoop(shellSeconds);
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

    // The ghosted hull: translucent for as long as the cloak, hazards pass
    // through it (Ghosted).
    void BeginGhost(float seconds)
    {
        ghostLeft = seconds;
        ghostEmit = 0f;
        lastGhostAt = transform.position;
        SetHullAlpha(ghostAlpha);
    }

    void SetHullAlpha(float a)
    {
        if (ghostHull == null) return;
        var c = ghostHull.color;
        c.a = a;
        ghostHull.color = c;
    }

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
            // a power that catches the boss is worth half an ultimate hit
            if (ShipAttackHits.Hit(t.gameObject, ship, .5f, ref budget)) kills++;
        }
        return kills;
    }

    // The nova's burning ring: whatever enters it dies. The boss is left to
    // the nova's one blast (a ring ticking every frame would hammer it).
    int BurnWithin(Vector3 at, float radius)
    {
        ShipTargets.Collect(scratch);
        int kills = 0;
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.isActiveAndEnabled || t.GetComponent<IShipAttackTarget>() != null) continue;
            Vector2 d = t.transform.position - at;
            float r = radius + t.Radius * .5f;
            if (d.sqrMagnitude > r * r) continue;
            if (ShipAttackHits.Hit(t.gameObject, ship)) kills++;
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
            {
                AttackPool.Sprite().Play(AttackSprite.Source.PowerLoop, ship, t.transform.position,
                    Vector3.one * Mathf.Max(.7f, t.Radius * 2.4f), 0f, ShipFxArt.PowerLoopTicks, true, stunSeconds,
                    t.transform, Vector3.zero, 67);
                ArcTo(t.transform.position);
                ArcCount++;
            }
        }
        stunLeft = stunSeconds;
    }

    // Counts EMP arcs drawn (tests, previews).
    public int ArcCount { get; private set; }

    // A bolt from the hull to a stunned enemy.
    void ArcTo(Vector3 to)
    {
        Vector3 a = transform.position;
        Vector3 d = to - a;
        float len = Mathf.Max(.2f, d.magnitude);
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        AttackPool.Sprite().Play(AttackSprite.Source.PowerLoop, ship, (a + to) * .5f, new Vector3(len, .55f, 1f),
            ang, ShipFxArt.PowerLoopTicks, true, .35f, null, Vector3.zero, 70);
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

    // Thunderclap: lightning strikes every hazard within the radius dead (a
    // boss takes a half hit from the whole clap, like every power).
    void Clap(Vector3 at)
    {
        ClapKills += KillWithin(at, clapRadius);
        CameraKick.Kick(.05f);
    }

    // The shell, drawn on the hull for as long as it stands.
    AttackSprite ShellLoop(float seconds)
    {
        var fx = Loop(transform.position, 1.25f, seconds, transform, 45);
        fx.Rename(ShellAuraName);
        return fx;
    }

    bool ShellDrawn => shellFx != null && shellFx.Active && shellFx.name == ShellAuraName;

    // The shell takes a hit: it shatters (a wide burst plus a bright inner
    // flash, distinct from every other power's), and the tight hull returns.
    void BreakShell()
    {
        ShellBreaks++;
        shellLeft = 0f;
        ShipHitbox.SetShield(gameObject, ShipHitbox.Source.Shell, false);
        if (shellFx != null && shellFx.Active) shellFx.Stop();
        shellFx = null;
        Vector3 at = transform.position;
        Burst(at, 3.2f, null).SetColor(new Color(.75f, 1f, .85f, 1f));
        Burst(at, 1.6f, transform);
        CameraKick.Kick(.03f);
    }

    // Which side is safer to sidestep to: the one whose landing spot keeps
    // the most clearance from the hazards around the hull (ties: away from
    // the threat, then away from the nearer wall).
    public float SafestDashDirection(GameObject threat)
    {
        Vector3 at = transform.position;
        float left = ShipReach.ClampX(at.x - dashDistance), right = ShipReach.ClampX(at.x + dashDistance);
        float cl = Clearance(new Vector2(left, at.y), at.y), cr = Clearance(new Vector2(right, at.y), at.y);
        if (Mathf.Abs(cl - cr) > .02f) return cl > cr ? -1f : 1f;
        if (threat != null && Mathf.Abs(threat.transform.position.x - at.x) > .05f)
            return at.x > threat.transform.position.x ? 1f : -1f;
        return at.x > 0f ? -1f : 1f;
    }

    // Distance from `spot` to the nearest hazard around the hull's height
    // (less its radius); big when nothing is near.
    float Clearance(Vector2 spot, float shipY)
    {
        var live = ClearTarget.Live;
        float best = 99f;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!ShipTargets.IsHazard(t)) continue;
            Vector3 p = t.transform.position;
            if (p.y < shipY - 1.2f || p.y > shipY + 3f) continue;
            best = Mathf.Min(best, ((Vector2)p - spot).magnitude - t.Radius);
        }
        return best;
    }

    void Dash(GameObject threat)
    {
        Vector3 at = transform.position;
        float dir = SafestDashDirection(threat);
        LastDashDirection = dir;
        DashCount++;
        DashOffsetX = dir * dashDistance;
        dashLeft = dashSeconds;
        collisionDetection.BeginCloak(.6f);
        Burst(at, 1.4f, null);
        transform.position = new Vector3(ShipReach.ClampX(at.x + DashOffsetX), at.y, at.z);
        Burst(transform.position, 1.2f, transform);
    }

    // A star dust pickup at a kill, the way the old shower dropped them.
    void DropStar(Vector3 at)
    {
        TractorStars++;
        var prefab = Resources.Load<GameObject>("prefabs/smStar_1");
        if (prefab == null) return;
        Rect view = ShipTargets.View(-.35f);
        Vector3 p = new Vector3(Mathf.Clamp(at.x, view.xMin, view.xMax), Mathf.Clamp(at.y, view.yMin, view.yMax), 0f);
        var star = Object.Instantiate(prefab, p, Quaternion.identity);
        PickupArt.ApplyInGameScale(star, prefab);
    }

    // Tractor Pull: every hazard near the ship is dragged in; what reaches
    // the field dies and drops star dust. The boss is never moved.
    void StepTractor(float dt)
    {
        Vector3 at = transform.position;
        ShipTargets.Collect(scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.isActiveAndEnabled || t.GetComponent<IShipAttackTarget>() != null) continue;
            Vector3 p = t.transform.position;
            Vector2 d = at - p;
            float dist = d.magnitude;
            if (dist > tractorPullRadius + t.Radius) continue;
            if (dist <= tractorKillRadius + t.Radius * .5f)
            {
                Vector3 where = p;
                if (ShipAttackHits.Hit(t.gameObject, ship))
                {
                    TractorKills++;
                    for (int k = 0; k < tractorStarsPerKill; k++) DropStar(where + new Vector3((k - .5f) * .3f, 0f, 0f));
                }
                continue;
            }
            if (lockedCount < MaxLocked) Lock(t.gameObject);
            t.transform.position = Vector3.MoveTowards(p, at, tractorPullSpeed * dt);
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

        if (tractorLeft > 0f)
        {
            tractorLeft -= dt;
            StepTractor(dt);
            if (tractorLeft <= 0f) { tractorLeft = 0f; ReleaseLocked(); }
        }

        if (decoyLeft > 0f && (decoyLeft -= dt) <= 0f)
        {
            decoyLeft = 0f;
            Vector3 spot = ShipDecoy.Position;
            ShipDecoy.Clear();
            Burst(spot, decoyBlastRadius * 2.3f, null);
            KillWithin(spot, decoyBlastRadius);
        }

        if (bubbleLeft > 0f && (bubbleLeft -= dt) <= 0f)
        {
            bubbleLeft = 0f;
            WorldTimeFx.BubbleScale = 1f;
            if (bubbleFx != null && bubbleFx.Active && bubbleFx.name == BubbleDomeName) bubbleFx.Stop();
            bubbleFx = null;
        }

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

        if (novaLeft > 0f)
        {
            novaLeft -= dt;
            BurnWithin(novaAt, novaRadius);
            if (novaLeft <= 0f) novaLeft = 0f;
        }

        if (ghostLeft > 0f)
        {
            ghostLeft -= dt;
            SetHullAlpha(ghostAlpha);
            // afterimages: a translucent hull copy every ~.06 s once it has moved
            ghostEmit -= dt;
            if (ghostEmit <= 0f)
            {
                ghostEmit = .06f;
                if ((transform.position - lastGhostAt).sqrMagnitude > .0025f)
                {
                    PowerTrailSprite.Ghost(ghostHull, ghostAlpha * .9f);
                    lastGhostAt = transform.position;
                }
            }
            if (ghostLeft <= 0f) { ghostLeft = 0f; SetHullAlpha(1f); }
        }

        if (shellLeft > 0f)
        {
            shellLeft -= dt;
            if (shellLeft <= 0f)
            {
                shellLeft = 0f;
                ShipHitbox.SetShield(gameObject, ShipHitbox.Source.Shell, false);
                if (shellFx != null && shellFx.Active && shellFx.name == ShellAuraName) shellFx.Stop();
                shellFx = null;
            }
            else if (!ShellDrawn) shellFx = ShellLoop(shellLeft);   // the pool took it back: draw it again
        }
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
        stunLeft = magnetLeft = tractorLeft = decoyLeft = bubbleLeft = dashLeft = holeLeft = shellLeft = novaLeft = 0f;
        if (ghostLeft > 0f) { ghostLeft = 0f; SetHullAlpha(1f); }
        ShipHitbox.SetShield(gameObject, ShipHitbox.Source.Shell, false);
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

    // Where an enemy shooter takes aim: the flare while one burns, else `real`.
    public static Vector2 AimPoint(Vector2 real) { return Active ? (Vector2)Position : real; }
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
