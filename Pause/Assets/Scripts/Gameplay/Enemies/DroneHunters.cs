using System.Collections.Generic;
using UnityEngine;

// DRONE HUNTERS. At high run power (and a big squad) some ordinary pilots (fighters and aliens: never a heavy, rock, mine, chaser or
// elite) spawn as hunters: an existing enemy's art with a new behaviour and a faint cue-coloured tell, no new art.
//
//   DIVER   hangs above the board, winds up (a glow flash and a LOCK LINE to the ship, both in its world's cue colour) for at least
//           MinTelegraphSeconds, the line freezes LockFreezeSeconds before the dive (so stepping aside always works), then dives
//           in a straight line through the locked point. The ordinary enemy-vs-ship contact does the rest: a heart is lost and it dies.
//   SNIPER  hangs back above, picks the nearest drone, shows the lock line to it, then fires ONE slow bolt at it (the pooled EliteShots,
//           roster shot). A bolt that reaches a drone pops and calls DroneSquad.KnockOut: the drone loses a core level, or leaves the squad
//           for DroneSquad.KnockOutSeconds and comes back through Activate. A bolt that reaches the hull hurts as any shot does.
//
// How many: Share(power, drones, loop) of eligible spawns, 0 below StartPower, rising monotonically with power and with the squad, scaled
// by the loop (EnemyTierThreat.LoopFactor), capped by ShareCap; never more than MaxAlive(loop) alive at once (HardMaxAlive).
// A hunter flies its own script in world space (its EnemyBrain is switched off), so it does not use PilotAirspace.
public static class DroneHunters
{
    // ---- tunables ----
    public const float StartPower = .4f, MaxShare = .2f;          // 0 under StartPower, MaxShare at power 1 with a full squad
    public const float SquadFloor = .6f;                          // share at no squad as a fraction of the full-squad share
    public const int FullSquad = 6;                               // drones counted as a full squad for the share
    public const float ShareCap = .35f;                           // whatever the loop does
    public const int BaseMaxAlive = 3, HardMaxAlive = 6, MaxSnipersAlive = 2;
    public const float SniperChance = .4f;                        // of hunters, when the squad has a drone to shoot at
    public const float MinTelegraphSeconds = .4f, BaseTelegraphSeconds = .8f;   // the diver's wind-up never drops under the first
    public const float LockFreezeSeconds = .25f;
    public const float DiveSpeed = 7f, EnterSpeed = 5f, BoltSpeed = 2.4f, BoltSize = .22f;   // x the view scale (u/s)
    public const float SniperCooldown = 3.4f, SniperMinCooldown = 1.4f;
    public const int SniperVolleys = 4;
    public const float DroneHitRadius = .4f;

    static void Prune() { for (int i = live.Count - 1; i >= 0; i--) if (live[i] == null) live.RemoveAt(i); }   // (a destroyed hunter reads null)
    public static int Alive { get { Prune(); return live.Count; } }
    public static int SnipersAlive { get { Prune(); int n = 0; for (int i = 0; i < live.Count; i++) if (live[i].Kind == DroneHunter.Variant.Sniper) n++; return n; } }
    public static void ResetAll() { live.Clear(); Made = 0; RollOverride = -1f; }
    internal static readonly List<DroneHunter> live = new List<DroneHunter>(8);
    public static int Made;
    // Tests: force every eligible spawn to roll this (negative: roll) / stand in for Random.value.
    public static float RollOverride = -1f;

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // Share of eligible spawns that hunt. 0 below StartPower; monotonic in power and in the squad; the loop multiplies it, up to ShareCap.
    public static float Share(float power01, int activeDrones, int loopIndex = 0)
    {
        if (power01 <= StartPower) return 0f;
        float ramp = Smooth((power01 - StartPower) / (1f - StartPower));
        float squad = Mathf.Lerp(SquadFloor, 1f, Mathf.Clamp01(activeDrones / (float)FullSquad));
        float s = MaxShare * ramp * squad;
        if (loopIndex > 0) s *= EnemyTierThreat.LoopFactor(loopIndex);
        return Mathf.Min(ShareCap, s);
    }

    public static int MaxAlive(int loopIndex) { return Mathf.Min(HardMaxAlive, BaseMaxAlive + Mathf.Max(0, loopIndex) / 2); }

    // The diver's wind-up: shorter on later loops, never under MinTelegraphSeconds.
    public static float TelegraphSeconds(int loopIndex)
    {
        return Mathf.Max(MinTelegraphSeconds, BaseTelegraphSeconds / Mathf.Sqrt(EnemyTierThreat.LoopFactor(loopIndex)));
    }

    public static float SniperCooldownFor(int loopIndex)
    {
        return Mathf.Max(SniperMinCooldown, SniperCooldown / Mathf.Sqrt(EnemyTierThreat.LoopFactor(loopIndex)));
    }

    public static bool Eligible(EnemyDef def, EnemyBrain brain)
    {
        return def != null && brain != null && brain.IsPilot && (def.role == EnemyRole.Fighter || def.role == EnemyRole.Alien);
    }

    // Called from Toughness.Attach for every ordinary enemy built. Cheap when it does not apply (power 0, tutorial, no brain).
    public static void MaybeMake(GameObject go, EnemyDef def)
    {
        if (go == null || startMenu.youAreInTutorial || !HeartTierRun.Live) return;   // (only a real run: fixtures and probes never grow hunters, whatever their power reads)
        float power = RunPower.Score01;
        if (power <= StartPower) return;
        if (!go.TryGetComponent(out EnemyBrain brain) || !Eligible(def, brain)) return;
        int loop = EnemyTierThreat.LoopIndex, drones = DroneSquad.ActiveCount;
        if (Alive >= MaxAlive(loop)) return;
        float roll = RollOverride >= 0f ? RollOverride : Random.value;
        if (roll >= Share(power, drones, loop)) return;
        bool sniper = drones > 0 && SnipersAlive < MaxSnipersAlive && (RollOverride >= 0f ? RollOverride : Random.value) < SniperChance;
        go.AddComponent<DroneHunter>().Begin(sniper ? DroneHunter.Variant.Sniper : DroneHunter.Variant.Diver, brain, def);
        Made++;
    }
}

public class DroneHunter : MonoBehaviour
{
    public enum Variant { Diver, Sniper }
    enum Stage { Enter, Aim, Strike, Cool, Leave }

    public Variant Kind { get; private set; }
    public int Volleys { get; private set; }
    public int Locked { get; private set; }     // drone slot a sniper aims at (-1: none), tests
    public float TellSeconds { get; private set; }
    public bool Diving => stage == Stage.Strike && Kind == Variant.Diver;
    public Transform TargetOverride;            // tests: the ship stand-in

    EnemyBrain brain; EnemyDef def; SpriteRenderer sr, glow; LineRenderer line;
    Stage stage; float t, cooldown, swayT, holdY;
    Vector2 lockAt, dir;
    EliteShot bolt;
    int loop;
    float vs = 1f;
    Color cue;
    static Material lineMat;

    Transform Ship => TargetOverride != null ? TargetOverride : EliteSystem.Player;

    public void Begin(Variant kind, EnemyBrain owner, EnemyDef d)
    {
        Kind = kind; brain = owner; def = d; Locked = -1;
        loop = EnemyTierThreat.LoopIndex;
        cue = EnemyBehaviours.ShotColor(d != null ? d.world : 0);   // the world's cue colour: pink only in Space
        TellSeconds = kind == Variant.Diver ? DroneHunters.TelegraphSeconds(loop) : DroneHunters.MinTelegraphSeconds + .5f;
        brain.enabled = false;   // the hunter flies its own script (the brain's OnDisable frees its reservations)
        if (TryGetComponent(out moveEnimes w)) w.station = true;
        if (TryGetComponent(out moveItemEnmInStrightLine m)) m.station = true;
        TryGetComponent(out sr);
        if (sr != null) sr.color = Color.Lerp(sr.color, cue, .18f);   // a faint tell, never a border
        BuildTell();
        stage = Stage.Enter;
        cooldown = 0f;
        DroneHunters.live.Add(this);
    }

    void OnDisable() { DroneHunters.live.Remove(this); }

    void BuildTell()
    {
        var g = new GameObject("HunterGlow");
        g.transform.SetParent(transform, false);
        glow = g.AddComponent<SpriteRenderer>();
        glow.sprite = EliteFxArt.Glow;
        glow.sortingOrder = 13;
        glow.color = cue;
        glow.enabled = false;
        var l = new GameObject("HunterLock");
        l.transform.SetParent(transform, false);
        line = l.AddComponent<LineRenderer>();
        if (lineMat == null) lineMat = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = lineMat;
        line.useWorldSpace = true; line.positionCount = 2; line.startWidth = .05f; line.endWidth = .03f;
        var c = cue; c.a = .8f; line.startColor = c; c.a = .35f; line.endColor = c;
        line.sortingOrder = 12;
        line.enabled = false;
    }

    static bool Flying => !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);

    void LateUpdate()
    {
        if (!Flying) return;
        float dt = Time.deltaTime;
        if (dt > 0f) Step(dt);
    }

    // One running frame (tests step it by hand).
    public void Step(float dt)
    {
        if (dt <= 0f) return;
        vs = EnemyBrain.ViewScale;
        Vector3 p = transform.position;
        var ship = Ship;
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        t += dt;
        TrackBolt();
        switch (stage)
        {
            case Stage.Enter:
            {
                holdY = top - (Kind == Variant.Sniper ? 2f : 2.6f) * vs;
                var v = ShipTargets.View();
                float x = Mathf.Clamp(p.x, v.xMin + .6f, v.xMax - .6f);
                Vector3 goal = new Vector3(x, holdY, p.z);
                transform.position = Vector3.MoveTowards(p, goal, DroneHunters.EnterSpeed * vs * dt);
                if ((transform.position - goal).sqrMagnitude < .01f) Begin(Stage.Aim);
                break;
            }
            case Stage.Aim:
            {
                if (Kind == Variant.Sniper) Sway(dt);
                bool frozen = t >= TellSeconds - DroneHunters.LockFreezeSeconds;
                if (!frozen) lockAt = AimPoint(ship);
                Pulse(dt);
                DrawLine(p, lockAt);
                if (t < TellSeconds) break;
                if (Kind == Variant.Diver)
                {
                    dir = (lockAt - (Vector2)p); dir = dir.sqrMagnitude < 1e-4f ? Vector2.down : dir.normalized;
                    HideTell();
                    Begin(Stage.Strike);
                }
                else
                {
                    FireBolt(p);
                    HideTell();
                    Volleys++;
                    cooldown = DroneHunters.SniperCooldownFor(loop);
                    Begin(Volleys >= DroneHunters.SniperVolleys ? Stage.Leave : Stage.Cool);
                }
                break;
            }
            case Stage.Strike:
                transform.position = p + (Vector3)(dir * DroneHunters.DiveSpeed * vs * dt * EnemyTierThreat.Speed(brain != null ? brain.Tier : 0, loop));
                if (transform.position.y < bottom - 1.5f) Done();
                break;
            case Stage.Cool:
                Sway(dt);
                cooldown -= dt;
                if (cooldown <= 0f)
                {
                    if (DroneSquad.ActiveCount == 0) Begin(Stage.Leave); else Begin(Stage.Aim);
                }
                break;
            case Stage.Leave:
                transform.position = p + Vector3.up * (DroneHunters.EnterSpeed * vs * dt);
                if (transform.position.y > top + 2f) Done();
                break;
        }
    }

    void Begin(Stage next) { stage = next; t = 0f; if (next == Stage.Aim && Kind == Variant.Sniper) PickDrone(); }

    void Sway(float dt)
    {
        swayT += dt;
        Vector3 p = transform.position;
        p.x += Mathf.Cos(swayT * 1.1f) * .5f * dt;
        transform.position = p;
    }

    void Pulse(float dt)
    {
        if (glow == null) return;
        glow.enabled = true;
        float k = Mathf.Clamp01(t / Mathf.Max(.1f, TellSeconds));
        glow.transform.localScale = Vector3.one * Mathf.Lerp(.5f, 1.4f, k) * (Mathf.FloorToInt(t * 12f) % 2 == 0 ? 1f : .8f);
    }

    void HideTell() { if (glow != null) glow.enabled = false; if (line != null) line.enabled = false; }

    void DrawLine(Vector3 from, Vector2 to)
    {
        if (line == null) return;
        line.enabled = true;
        line.SetPosition(0, from);
        line.SetPosition(1, new Vector3(to.x, to.y, 0f));
    }

    Vector2 AimPoint(Transform ship)
    {
        if (Kind == Variant.Sniper)
        {
            var b = FloatieBattery.Current;
            if (b != null && Locked >= 0 && DroneSquad.IsActive(Locked)) return b.BodyAt(Locked);
            return ship != null ? (Vector2)ship.position : (Vector2)transform.position + Vector2.down * 4f;
        }
        return ship != null ? ShipDecoy.AimPoint(ship.position) : (Vector2)transform.position + Vector2.down * 4f;
    }

    void PickDrone()
    {
        Locked = -1;
        var b = FloatieBattery.Current;
        if (b == null) return;
        float best = float.MaxValue;
        for (int i = 0; i < DroneSquad.Slots; i++)
        {
            if (!DroneSquad.IsActive(i)) continue;
            float d = ((Vector2)b.BodyAt(i) - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; Locked = i; }
        }
    }

    void FireBolt(Vector3 from)
    {
        var pool = EliteSystem.Shots;
        var style = brain != null && brain.Behaviour != null ? brain.Behaviour.ShotStyle : null;
        if (pool == null || style == null) return;
        Vector2 v = (lockAt - (Vector2)from);
        v = v.sqrMagnitude < 1e-4f ? Vector2.down : v.normalized;
        bolt = pool.Fire(null, style, EliteShots.Kind.Bolt, (Vector2)from, v * DroneHunters.BoltSpeed * vs, true, gameObject.GetInstanceID());
        if (bolt != null) bolt.AsRosterShot(gameObject, 0f);
    }

    // The sniper's bolt against the drones: a hit pops it and knocks the drone out (DroneSquad.KnockOut).
    void TrackBolt()
    {
        if (bolt == null) return;
        if (!bolt.Active) { bolt = null; return; }
        var b = FloatieBattery.Current;
        if (b == null) return;
        Vector2 bp = bolt.ShotPosition;
        float r2 = DroneHunters.DroneHitRadius * DroneHunters.DroneHitRadius;
        for (int i = 0; i < DroneSquad.Slots; i++)
        {
            if (!DroneSquad.IsActive(i)) continue;
            if (((Vector2)b.BodyAt(i) - bp).sqrMagnitude > r2) continue;
            HostileShots.ShootDown(bolt);
            DroneSquad.KnockOut(i, out bool lost);
            bolt = null;
            return;
        }
    }

    void Done()
    {
        HideTell();
        if (Application.isPlaying) Pooled.Destroy(gameObject); else DestroyImmediate(gameObject);
    }
}
