using UnityEngine;
using UnityEngine.SceneManagement;

// WORLD HAZARDS: the manager (docs/world-weather-hazards.md). It schedules the rare weather events of the
// world the run is in, runs the field of the live one, and pushes or pulls what is in it:
//
//   * the player's ship through a capped finger-offset (WorldHazards.PlayerOffset, read by movePlayer),
//   * enemy bodies, rocks and engaged pilots (a lane-clamped displacement, never a crash or a kill),
//   * elites (a little velocity, their own steering eases them back),
//   * hostile projectiles (IHostileShot.ShotShove, boss shots included) and the player's (AttackProjectile.Shove).
//
// It NEVER damages anything and never moves a boss, a beam, a rail mine or a pickup. Stepped on running frames
// only (WorldManager.Flying). One event at a time (a second field only during a fade-out). Everything is a fixed
// array: nothing allocates after Prewarm.
[DefaultExecutionOrder(-25)]   // before EnemyBrain (-20) reads where its body is
public sealed class WorldHazards : MonoBehaviour
{
    public static WorldHazards Instance { get; private set; }

    // Off (tests, dev): nothing is scheduled and nothing acts.
    public static bool Disabled;

    // The ship's displacement from the finger (movePlayer adds it; ShipReach still has the last word).
    public static Vector2 PlayerOffset;
    public static float PlayerOffsetX => PlayerOffset.x;
    public static float PlayerOffsetY => PlayerOffset.y;

    static readonly HazardField[] fields = MakeFields();
    static HazardField[] MakeFields()
    {
        var a = new HazardField[WorldWeatherTable.MaxFields];
        for (int i = 0; i < a.Length; i++) a[i] = new HazardField();
        return a;
    }
    public static HazardField[] Fields => fields;

    // A hazard is warning, acting or fading (what the boulder director asks).
    public static bool AnyActive { get { for (int i = 0; i < fields.Length; i++) if (fields[i].Active) return true; return false; } }
    public static bool AnyActing { get { for (int i = 0; i < fields.Length; i++) if (fields[i].Acting) return true; return false; } }

    // Counters (tests, previews).
    public static int Started, LastShotsPushed, LastMinePushed, LastBodiesPushed, LastEliteKicks;
    public static string LastStartedId;

    // ---- what the scheduler reads about the game (tests supply their own) ----
    public struct Situation
    {
        public int world;
        public bool tutorial, entry, transition, portalOpen, playerDead, cinematic;
        public bool bossRunUp;            // the boss is about to come (SecondsLeftInWorld < BossLeadSeconds)
        public bool bossBusy;             // a boss intro / outro (a boss that is not in its fight)
        public bool bossFight;            // the boss fight itself: hazards are allowed
        public bool eliteBusy;            // an elite is parked, lifting off or joining
        public bool eliteJoined;          // an elite joined this frame
    }
    public static System.Func<Situation> SituationOverride;
    public static bool? BossRunningOverride;   // tests: pretend a boss encounter is (not) running

    public static Situation Read()
    {
        var s = new Situation();
        var wm = WorldManager.Instance;
        s.world = wm != null ? WorldManager.SpawnWorld : -1;
        s.tutorial = startMenu.youAreInTutorial;
        s.entry = WorldEntry.Active || Liftoff.HoldsShip;
        s.transition = WorldTransition.InProgress;
        s.portalOpen = wm != null && wm.PortalIsOpen;
        s.playerDead = buttonClicks.playerDied || DeathCrash.Running;
        s.cinematic = ShipPowerController.CinematicClearActive;
        s.bossRunUp = wm != null && wm.DistanceLeft > 0f && wm.SecondsLeftInWorld < WorldWeatherTable.BossLeadSeconds;
        var boss = BossEncounter.Instance;
        s.bossFight = boss != null && boss.State == BossEncounter.Phase.Fight;
        s.bossBusy = BossEncounter.Running && !s.bossFight;
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null) continue;
            if (e.State == EliteState.Parked || e.State == EliteState.LiftOff) s.eliteBusy = true;
            else if (e.State == EliteState.Join) { s.eliteBusy = true; s.eliteJoined = true; }
        }
        return s;
    }

    // Why no event may START now, or null.
    public static string Blocked(in Situation s, float secondsInWorld, float sinceElite)
    {
        if (Disabled) return "disabled";
        if (s.tutorial) return "tutorial";
        if (s.world < 0) return "no world";
        if (s.entry) return "entry";
        if (s.transition) return "transition";
        if (s.portalOpen) return "portal";
        if (s.playerDead) return "dead";
        if (s.cinematic) return "cinematic";
        if (s.bossBusy) return "boss intro / outro";
        if (s.bossRunUp && !s.bossFight) return "boss soon";
        if (s.eliteBusy) return "elite";
        if (sinceElite < WorldWeatherTable.MinGapAfterElite) return "elite just joined";
        if (secondsInWorld < 8f) return "calm start";
        return null;
    }

    // A block that also ENDS a running event (a clean board).
    public static bool EndsEvents(in Situation s)
    {
        return Disabled || s.tutorial || s.world < 0 || s.entry || s.transition || s.portalOpen || s.playerDead || s.bossBusy;
    }

    // ---- scheduler state ----
    int world = -1;
    float worldSeconds, untilNext, sinceElite = 99f;
    bool wasBossFight;
    string lastId;
    readonly System.Random rng = new System.Random();
    HazardVisuals visuals;
    WeatherLayer weather;
    WindVisuals wind;

    public float WorldSeconds => worldSeconds;
    public float UntilNext => untilNext;
    public System.Random Rng => rng;
    public HazardVisuals Visuals { get { return visuals ?? (visuals = new HazardVisuals()); } }
    public WindVisuals Wind { get { return wind ?? (wind = new WindVisuals()); } }
    public WeatherLayer Weather { get { return weather ?? (weather = new WeatherLayer()); } }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    void Awake()
    {
        Instance = this;
        untilNext = 30f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearAll();
        PlayerOffset = Vector2.zero;
        if (visuals != null) visuals.Destroy();
        if (weather != null) weather.Destroy();
        if (wind != null) wind.Destroy();
        WeatherWind.Reset(null);
    }

    public static void ClearAll()
    {
        for (int i = 0; i < fields.Length; i++) EndNow(fields[i]);
    }

    void LateUpdate()
    {
        bool hazardsOn = FeatureFlags.IsOn(FeatureFlags.WorldHazards);
        bool weatherOn = FeatureFlags.IsOn(FeatureFlags.WorldWeather);
        float dt = Time.deltaTime;
        bool flying = WorldManager.Flying;
        if (hazardsOn && flying && dt > 0f && !Disabled) Step(dt);
        else if (!hazardsOn && AnyActive) { ClearAll(); PlayerOffset = Vector2.zero; }
        Visuals.Draw(fields, hazardsOn);
        Wind.Draw(flying ? dt : 0f, hazardsOn && weatherOn, world >= 0 ? world : (WorldManager.Instance != null ? WorldManager.SpawnWorld : 0));
        Weather.Step(flying ? dt : 0f, weatherOn, world >= 0 ? world : (WorldManager.Instance != null ? WorldManager.SpawnWorld : 0), fields);
    }

    // ---- one running frame (dt explicit: tests and simulations step it by hand) ----
    public void Step(float dt)
    {
        var sit = SituationOverride != null ? SituationOverride() : Read();
        Tick(dt, sit);
        StepFields(dt);
        Apply(dt);
    }

    public void Tick(float dt, in Situation sit)
    {
        if (sit.world != world)
        {
            world = sit.world; worldSeconds = 0f;
            var wen = WorldWeatherTable.ForWorld(world);
            WeatherWind.Reset(wen != null ? wen.wind : null);
            for (int i = 0; i < fields.Length; i++) if (fields[i].Active) EndNow(fields[i]);
            var en = WorldWeatherTable.ForWorld(world);
            untilNext = en != null ? en.first + Rand(0f, 6f) : 30f;
        }
        worldSeconds += dt;
        sinceElite = sit.eliteJoined ? 0f : sinceElite + dt;

        // the boss fight begins: bring the next event forward (hazards are allowed in a fight)
        if (sit.bossFight && !wasBossFight)
        {
            var en = WorldWeatherTable.ForWorld(world);
            if (en != null) untilNext = Mathf.Min(untilNext, Rand(en.bossFirst.x, en.bossFirst.y));
        }
        wasBossFight = sit.bossFight;

        if (EndsEvents(sit))
            for (int i = 0; i < fields.Length; i++) if (fields[i].Active) Fade(fields[i], .6f);

        // the always-on wind: blows on a clean running board after the calm start; a gust is never scheduled while an event
        // runs or is about to (the event is the big weather, the gust the small), nor while a boss intro / outro is on
        bool windOn = !EndsEvents(sit) && worldSeconds >= 6f;
        WeatherWind.Step(dt, windOn, !AnyActive && !sit.bossBusy && !sit.bossRunUp || sit.bossFight && !AnyActive);

        if (AnyActive) return;
        untilNext -= dt;
        if (untilNext > 0f) return;
        if (Blocked(sit, worldSeconds, sinceElite) != null) { untilNext = 1f; return; }
        var entry = WorldWeatherTable.ForWorld(world);
        if (entry == null || entry.hazards.Length == 0) { untilNext = 30f; return; }
        int pick = rng.Next(entry.hazards.Length);
        if (entry.hazards.Length > 1 && entry.hazards[pick].id == lastId) pick = (pick + 1) % entry.hazards.Length;
        Begin(entry.hazards[pick]);
        float loopScale = 1f / Mathf.Min(WorldWeatherTable.LoopShortenMax, 1f + WorldWeatherTable.LoopShorten * Mathf.Max(0, RunLoop.Index));
        untilNext = Rand(entry.gap.x, entry.gap.y) * loopScale;
    }

    // Starts `def` now (the scheduler's pick, or a dev / test call). False when both slots are busy.
    public bool Begin(HazardDef def)
    {
        HazardField f = null;
        for (int i = 0; i < fields.Length && f == null; i++) if (!fields[i].Active) f = fields[i];
        if (f == null) return false;
        f.def = def; f.phase = HazardField.Phase.Warning; f.t = 0f; f.fadeT = 0f; f.fadeLen = WorldWeatherTable.RampOut;
        f.side = rng.Next(2) == 0 ? -1f : 1f;
        f.variant = rng.Next(4);
        if (def.artRow >= 0 && !f.hazArtHeld) { HazardArt.Acquire(def.world); f.hazArtHeld = true; }
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        f.viewTop = top; f.viewBottom = bottom;
        var cam = Camera.main;
        f.laneHalf = cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect + .5f : 4f;
        float viewH = top - bottom;
        switch (def.shape)
        {
            case HazardShape.Swirl:
                f.centre = new Vector2(Rand(-1.4f, 1.4f), bottom + viewH * Rand(.4f, .66f));
                f.vel = new Vector2(f.side * def.speed * .45f, -.1f * def.speed);
                f.liveFor = def.live;
                break;
            case HazardShape.Column:
                f.centre = new Vector2(Rand(-1.6f, 1.6f), bottom);
                f.vel = new Vector2(f.side * .15f, 0f);
                f.liveFor = def.live;
                break;
            case HazardShape.Sweep:
            {
                // a vertical band train crossing sideways: it starts wholly off the near edge and ends wholly off the far one
                float edge = f.laneHalf + def.size * .5f;
                f.sweepX = -f.side * edge;
                float travel = 2f * edge + Mathf.Max(0, def.bands - 1) * def.bandGap;
                f.liveFor = travel / Mathf.Max(.1f, def.speed);
                break;
            }
            default:
                f.frontY = top + def.size * .5f;
                f.liveFor = (viewH + def.size) / Mathf.Max(.1f, def.speed);
                break;
        }
        lastId = def.id; LastStartedId = def.id; Started++;
        return true;
    }

    // Dev / tests: start the hazard `id` of any world now.
    public static bool Force(string id)
    {
        if (Instance == null) return false;
        foreach (var e in WorldWeatherTable.Entries)
            foreach (var h in e.hazards)
                if (h.id == id) return Instance.Begin(h);
        return false;
    }

    static void Fade(HazardField f, float seconds)
    {
        if (f.phase == HazardField.Phase.Warning) { EndNow(f); return; }
        if (f.phase == HazardField.Phase.Fading) return;
        f.phase = HazardField.Phase.Fading; f.fadeT = 0f; f.fadeLen = seconds;
    }

    static void EndNow(HazardField f)
    {
        if (f.hazArtHeld) { f.hazArtHeld = false; HazardArt.Release(); }
        f.phase = HazardField.Phase.Idle; f.def = null;
    }

    public void StepFields(float dt)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (!f.Active) continue;
            f.t += dt;
            if (f.phase == HazardField.Phase.Warning && f.t >= f.def.warning) f.phase = HazardField.Phase.Live;
            if (f.phase == HazardField.Phase.Live)
            {
                float liveT = f.t - f.def.warning;
                switch (f.def.shape)
                {
                    case HazardShape.Swirl:
                        f.centre += f.vel * dt;
                        float lim = 2.0f;
                        if (Mathf.Abs(f.centre.x) > lim) { f.centre.x = Mathf.Sign(f.centre.x) * lim; f.vel.x = -f.vel.x; }
                        f.centre.y = Mathf.Clamp(f.centre.y, f.viewBottom + 2f, f.viewTop - 2f);
                        break;
                    case HazardShape.Column:
                        f.centre.x = Mathf.Clamp(f.centre.x + f.vel.x * dt, -2f, 2f);
                        break;
                    case HazardShape.Sweep:
                        f.sweepX = -f.side * (f.laneHalf + f.def.size * .5f) + f.side * f.def.speed * liveT;
                        break;
                    default:
                        f.frontY = f.viewTop + f.def.size * .5f - f.def.speed * liveT;
                        break;
                }
                if (liveT >= f.liveFor) EndNow(f);
            }
            else if (f.phase == HazardField.Phase.Fading)
            {
                f.fadeT += dt;
                if (f.fadeT >= f.fadeLen) EndNow(f);
                else if (f.def.shape == HazardShape.Swirl) f.centre += f.vel * dt;
            }
        }
    }

    // ---- applying the forces ----
    public static void Apply(float dt)
    {
        LastShotsPushed = LastMinePushed = LastBodiesPushed = LastEliteKicks = LastStunned = 0;
        Vector2 playerV = Vector2.zero;
        Transform player = EliteSystem.Player;
        bool boss = BossRunningOverride ?? BossEncounter.Running;
        float rigid = 0f;
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (!f.Acting) continue;
            Rect b = f.Bounds;
            PushShots(f, b, dt, boss);
            PushBodies(f, b, dt);
            PushElites(f, b, dt);
            if (f.def.stuns && f.phase == HazardField.Phase.Live) StunScreen(f);
            if (player != null && HazardForce.Sample(f, player.position, HazardTarget.Player, out Vector2 pv)) playerV += pv;
            rigid += HazardForce.RigidX(f, HazardTarget.Hostile) * f.def.bossShotScale;
        }
        // the always-on wind: the same targets, the same caps (it is a field everywhere, so no bounds cull)
        if (WeatherWind.Profile != null && WeatherWind.Fade > 0f)
        {
            PushShots(null, WorldRect, dt, boss);
            PushBodies(null, WorldRect, dt);
            PushElites(null, WorldRect, dt);
            if (player != null) playerV += WeatherWind.Sample(player.position, HazardTarget.Player);
        }
        // boss fights: one rigid, slew-limited, capped sideways drift for every live hostile shot (see WeatherWind)
        if (boss) BossShotsDrift(WeatherWind.StepBossWind(dt, rigid), dt);
        else WeatherWind.StepBossWind(dt, 0f);
        // the ship: a finger-offset that follows the field and springs back, always capped
        PlayerOffset += playerV * (WorldWeatherTable.PlayerGain * dt);
        PlayerOffset *= Mathf.Exp(-WorldWeatherTable.ReturnRate * dt);
        PlayerOffset = HazardForce.Clamp(PlayerOffset, WorldWeatherTable.PlayerCap);
        if (PlayerOffset.sqrMagnitude < 1e-8f) PlayerOffset = Vector2.zero;
    }

    // During a boss encounter every hostile shot gets only the rigid drift (BossShotsDrift): a swirl or a shearing field
    // would close the gaps of the beat-locked waves (docs/weather-overhaul.md, fairness).
    static void PushShots(HazardField f, Rect b, float dt, bool boss)
    {
        var hostile = HostileShots.All;
        float cap = WorldWeatherTable.ShotMaxSpeed * Cap(f);
        for (int i = 0; boss == false && i < hostile.Count; i++)
        {
            var s = hostile[i];
            if (s == null || (Object)s == null || !s.ShotShovable) continue;
            Vector2 p = s.ShotPosition;
            if (!b.Contains(p)) continue;
            if (!Field(f, p, HazardTarget.Hostile, out Vector2 v)) continue;
            v = HazardForce.Clamp(v, cap);
            s.ShotShove(v * dt);
            LastShotsPushed++;
        }
        var mine = AttackProjectile.Flying;
        for (int i = 0; i < mine.Count; i++)
        {
            var a = mine[i];
            if (a == null || !a.Active) continue;
            Vector2 p = a.transform.position;
            if (!b.Contains(p)) continue;
            if (!Field(f, p, HazardTarget.Mine, out Vector2 v)) continue;
            a.Shove(HazardForce.Clamp(v, cap) * dt);
            LastMinePushed++;
        }
    }

    // The whole play area, for the always-on wind (a field everywhere: the cull is the view test each pusher already does).
    static readonly Rect WorldRect = new Rect(-30f, -60f, 60f, 120f);

    // A hazard field's force on a target, or (f null) the wind's.
    static bool Field(HazardField f, Vector2 p, HazardTarget t, out Vector2 v)
    {
        if (f != null) return HazardForce.Sample(f, p, t, out v);
        v = WeatherWind.Sample(p, t);
        return v.sqrMagnitude > 1e-6f;
    }

    static float Cap(HazardField f) { return f != null ? f.def.capMul : 1f; }

    // Boss fights: every live hostile shot drifts the same sideways amount (see WeatherWind.BossShotWind).
    static void BossShotsDrift(float vx, float dt)
    {
        if (Mathf.Abs(vx) < 1e-5f) return;
        var hostile = HostileShots.All;
        for (int i = 0; i < hostile.Count; i++)
        {
            var s = hostile[i];
            if (s == null || (Object)s == null || !s.ShotShovable) continue;
            s.ShotShove(new Vector2(vx * dt, 0f));
            LastShotsPushed++;
        }
    }

    static void PushBodies(HazardField f, Rect b, float dt)
    {
        var live = ClearTarget.Live;
        float top = CameraFit.ViewTop;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.Scenery) continue;
            var tr = t.transform;
            Vector3 p3 = tr.position;
            Vector2 p = p3;
            if (!b.Contains(p) || p.y > top - .2f) continue;   // not in play (above the view)
            var go = t.gameObject;
            bool rock = go.CompareTag("Astr");
            if (!rock && !go.CompareTag("Enimey")) continue;
            if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _)) continue;
            if (go.TryGetComponent(out EliteShip _)) continue;          // elites: PushElites
            if (RunScore.IsHostileShot(go)) continue;
            if (go.TryGetComponent(out RailMineMount mount) && mount.rail != null) continue;   // rides its rail
            float share = 1f;
            var brain = t.Brain;
            if (brain != null && brain.enabled && brain.Behaviour != null && brain.IsPilot)
            {
                if (brain.Stage != EnemyBrain.PilotStage.Entering && brain.Stage != EnemyBrain.PilotStage.Engaging) continue;
                share = WorldWeatherTable.PilotShare;
            }
            if (!Field(f, p, HazardTarget.Body, out Vector2 v)) continue;
            float mass = rock ? HazardSize.ShoveScale(EnemyIdentity.ScaleOf(go)) : 1f;
            v = HazardForce.Clamp(v * (share * mass), WorldWeatherTable.BodyMaxSpeed * Cap(f));
            SpawnFootprint fp;
            Vector2 half = go.TryGetComponent(out fp) ? fp.half : new Vector2(t.Radius, t.Radius);
            Vector2 move = v * dt;
            float lane = SpawnLane.LaneHalf - half.x;
            float nx = p.x + move.x;
            if (Mathf.Abs(p.x) <= lane) nx = Mathf.Clamp(nx, -lane, lane);
            else if (Mathf.Abs(nx) > Mathf.Abs(p.x)) nx = p.x;     // already outside: never further
            float ny = Mathf.Min(p.y + move.y, top - half.y);       // never up and out of the view
            // a field never drives a body INTO another (that would be a friendly-fire crash the hazard caused): a move that
            // would newly touch a neighbour is held, and the soft avoidance (SpawnSpace.Soft) keeps working as before
            if (fp != null && WouldTouchNew(fp, p, new Vector2(nx, ny), half)) continue;
            tr.position = new Vector3(nx, ny, p3.z);
            LastBodiesPushed++;
        }
    }

    // A stunning flare holds every enemy and elite in view for as long as it is live: re-asserted each frame for a moment, so the
    // stun lapses by itself right after the flare ends (or is cut short). Bosses, rails' mines and rocks are not touched.
    public const float StunHold = .35f;
    public static int LastStunned;

    static void StunScreen(HazardField f)
    {
        float top = f.viewTop - .2f, bottom = f.viewBottom;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.Scenery) continue;
            var go = t.gameObject;
            if (!go.CompareTag("Enimey")) continue;
            Vector3 p = go.transform.position;
            if (p.y > top || p.y < bottom || Mathf.Abs(p.x) > f.laneHalf) continue;
            if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _)) continue;
            if (go.TryGetComponent(out EliteShip _)) continue;          // elites: below
            if (RunScore.IsHostileShot(go)) continue;
            if (go.TryGetComponent(out RailMineMount mount) && mount.rail != null) continue;
            if (FrozenState.Freeze(go, StunHold) != null) LastStunned++;
        }
        var elites = EliteShip.Live;
        for (int i = 0; i < elites.Count; i++)
        {
            var e = elites[i];
            if (e == null || !e.InPlay) continue;
            Vector2 p = e.transform.position;
            if (p.y > top || p.y < bottom || Mathf.Abs(p.x) > f.laneHalf) continue;
            if (e.Stun(StunHold)) LastStunned++;
        }
    }

    const float BodyMargin = .03f;

    // Would `to` newly touch another enemy body (it does not touch it at `from`)?
    static bool WouldTouchNew(SpawnFootprint self, Vector2 from, Vector2 to, Vector2 half)
    {
        var all = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < all.Count; i++)
        {
            var o = all[i];
            if (o == null || o == self) continue;
            Vector2 at = o.transform.position;
            bool now = Mathf.Abs(from.x - at.x) < half.x + o.half.x + BodyMargin && Mathf.Abs(from.y - at.y) < half.y + o.half.y + BodyMargin;
            if (now) continue;
            if (Mathf.Abs(to.x - at.x) < half.x + o.half.x + BodyMargin && Mathf.Abs(to.y - at.y) < half.y + o.half.y + BodyMargin) return true;
        }
        return false;
    }

    static void PushElites(HazardField f, Rect b, float dt)
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || !e.InPlay) continue;
            Vector2 p = e.transform.position;
            if (!b.Contains(p)) continue;
            if (!Field(f, p, HazardTarget.Elite, out Vector2 v)) continue;
            if (e.Velocity.sqrMagnitude >= WorldWeatherTable.EliteMaxSpeed * WorldWeatherTable.EliteMaxSpeed) continue;
            e.Velocity += v * (WorldWeatherTable.EliteAccel * dt);
            LastEliteKicks++;
        }
    }
}

// Attaches the manager when the game scene loads (gameS1 only), like EliteDirector.
public static class WorldHazardsBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (WorldHazards.Instance != null) return;
        new GameObject("~WorldHazards").AddComponent<WorldHazards>();
    }
}
