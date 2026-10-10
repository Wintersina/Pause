using System.Collections.Generic;
using UnityEngine;

// An elite enemy ship: a named, hunting craft with its own personality
// (EliteBrain), its own attack (EliteAttack) and two hearts.
//
// LIFE CYCLE (EliteState)
//   Parked   sitting on a landing site on the world's background terrain
//            (LandingSites): small, hazy and dim, behind gameplay, no
//            collider, not a target. In its last second the engine lights
//            blink on -- the tell that it is about to launch. It waits a
//            little longer (lights still blinking) while the air it would
//            rise into is busy.
//   LiftOff  dust and heat shimmer at the pad, the engines ignite and it
//            rises; scale, brightness and sorting interpolate from the
//            background's depth up to the play layer. Still no collider.
//            Its join point is chosen now, clear of traffic and away from
//            the pilot, slides if traffic arrives, and it hovers just under
//            the play layer if there is no clear spot yet (EliteEvasion).
//            Space has no ground: its sites are INSIDE a station, a planet
//            or a big asteroid (LandingSite.emerge). Docked there the hull
//            is hidden -- only the engine lights blink in the launch tell --
//            and the lift-off opens with a dock flare instead of dust, the
//            ship flying out from half its docked size, fading in.
//   Join     fully in the play layer: the collider, the hazard tag, the
//            hearts and its SpawnSpace footprint switch on, and it swoops
//            in from the side / behind to its pursuit position. Never ends
//            on top of the pilot (EliteDirector picks the lift-off's end
//            well away), and attacks wait out an escape window.
//   Follow   its brain flies it (interceptor, gunship, striker, hauler,
//            skirmisher, siege), dodging what the board throws at it:
//            EliteEvasion reads the board ahead and Navigate turns the
//            brain's wish into a spot it can reach without being hit.
//   Attack   the tell drawing for tellSeconds, then the action drawing
//            while its attack plays (a dash, a broadside, a dive ...).
//   Dead     its hearts ran out: the death FX (pluggable, EliteDeath),
//            the pilot's reward, gone.
// A hit (any damage) shows the hit drawing for a few ticks and costs a
// heart, which darts to the impact and crumbles (EliteHearts, HeartOrbit).
//
// DRAWINGS. Which strip cell shows in each state is the def's EliteCells
// map. The Ember layout loops idle 0..3 and has tell / action / hit cells;
// a flight layout (Frost's Rimebreaker, Verdant's Resin Warden) has real
// parked / lift-off cells, banks into sideways moves with its bank cells,
// switches to its damaged cell for good once it has lost a heart, and --
// having no tell / action / hit cells -- winds up with a charge glow at its
// muzzles and a squash and lean, fires with muzzle flashes and a recoil,
// and flashes its current frame when hit.
//
// ENDING. Elites never retreat or time out: they die by crashing -- into
// rocks, enemies, mines, other elites and the side rails -- or to the
// pilot. They read the board and dodge (EliteEvasion; `avoidance` is their
// skill at it), but what arrives inside their reaction time or a
// committed dash beats them, and they do NOT teleport with the pilot: a
// pause-teleport leaves them flying where they thought the ship was
// (perception), the best way to bait them into something. Their shots hit
// everything (friendly fire, EliteShots) without paying the pilot.
//
// DAMAGE (one heart each, then half a second of grace): a player weapon,
// the ultimate, the red atom's free shot or a secret power
// (ShipAttackHits -> TakeShipAttack), touching the pilot
// (collisionDetection -> Rammed; the pilot loses a heart too unless
// shielded), a crash, a rail, another elite, friendly fire. A shielded ram
// (blue atom / Cloak) and a pause jump landing on it (TeleportFx ->
// TeleportStrike) take every heart, grace or not.
// Every kill, crash or lure included, pays ScoreRules.EliteDown +
// EliteDownDust with an "ELITE DOWN" popup.
//
// Positions are world (screen) space: like the chaser it holds its place in
// the world while the board pours past it. Stepped by EliteSystem on the
// world's clock -- frozen at timeScale 0 -- with no per-frame allocation.
public enum EliteState { Parked, LiftOff, Join, Follow, Attack, Dead }
public enum EliteDamage { PlayerWeapon, Teleport, ShieldRam, PlayerContact, Crash, Rail, FriendlyFire, Domino, Combo, ShoveCrash }   // Combo: a DEATH COMBO link (DeathCombo), one heart, respects grace; ShoveCrash: a body the shield shockwave shoved crashed into it (ShoveCrash), one heart, respects grace

[DisallowMultipleComponent]
public class EliteShip : MonoBehaviour, IShipAttackTarget, IMovementFootprint, ISpawnShadow
{
    public static readonly List<EliteShip> Live = new List<EliteShip>(8);

    // ---- tuning shared by every elite ----
    public const int PlayOrder = 5;            // over rocks (2) and enemies (3), under mines (12)
    public const float GraceSeconds = .5f;     // after losing a heart
    public const float EscapeWindow = 2f;      // after joining: no attacks yet
    public const float JoinSeconds = 1.1f;
    public const float LiftSeconds = 1.7f;
    public const float EngineTellSeconds = 1f; // parked: lights blink before launch
    public const float MinJoinDistance = 2.6f; // lift-off ends at least this far from the pilot
    public const float HitFlashSeconds = EliteArt.HitTicks * EliteArt.Tick;
    public static readonly Color Haze = new Color(.42f, .38f, .56f, .78f);

    // ---- counters (tests, previews) ----
    public static int Kills, CrashKills, FriendlyKills, Crashes;
    public static EliteDamage LastKillCause;
    // Diagnostics (probes, tests): what the next hit comes from -- set by the
    // code about to call TakeHit, cleared by it -- and a death callback
    // (ship, cause, source). Constant strings only: nothing allocates.
    public static string HitBy;
    public static System.Action<EliteShip, EliteDamage, string> Died;
    public string LastHitBy { get; private set; }
    // Seconds since it joined the play layer.
    public float PlaySeconds { get; private set; }

    public EliteDef Def { get; private set; }
    public EliteBrain Brain { get; private set; }
    public EliteAttack Attack { get; private set; }
    public EliteState State { get; private set; }
    public int Hearts { get; private set; }
    public Vector2 Velocity { get { return velocity; } set { velocity = value; } }
    public float Facing { get { return facing; } }
    public Vector2 Seen { get { return seen; } }
    public bool PlayerLost { get { return lost; } }
    public float StateTime { get { return stateTime; } }
    public bool InPlay => State == EliteState.Join || State == EliteState.Follow || State == EliteState.Attack;
    public bool Telling => State == EliteState.Attack && attackPhase == 0;
    public bool Acting => State == EliteState.Attack && attackPhase == 1;
    public int CurrentFrame { get; private set; }
    public float Grace => grace;
    public SpriteRenderer Hull => hull;
    public CircleCollider2D Collider => col;
    public float HullScale => hullBase;
    public float AttackCooldown { get { return cooldown; } set { cooldown = value; } }
    public float EscapeLeft { get { return escapeLeft; } set { escapeLeft = value; } }
    public LandingSite Site => site;
    public Vector2 LiftTarget => liftTo;
    public int Attacks { get; private set; }
    public Vector2 Position => transform.position;
    public EliteDamage LastHitCause { get; private set; }

    Transform hullTf;
    SpriteRenderer hull;
    CircleCollider2D col;
    ClearTarget target;
    SpawnFootprint footprint;
    EliteHearts heartsView;
    Sprite[] frames, parkedFrames, liftFrames;
    SpriteRenderer[] plumes, glows, muzzleGlows;
    float[] muzzleFlash;
    float hullBase = 1f, lean, recoilLen;
    Vector2 recoilDir;
    int bankCell;   // -1 left, 0 straight, 1 right (steering frames)
    Vector2[] nozzleLocal;
    float[] nozzleAngle;
    SpriteRenderer sight;

    LandingSite site;
    Vector3 sitePos;
    float parkSeconds;
    Vector2 liftFrom, liftTo;
    Vector2 velocity;
    float facing = 90f, bank;
    float stateTime, cooldown, escapeLeft, grace, hitFlash, frameHold, blinkCooldown, exhaustClock;
    int idleStep, attackPhase;
    float thrust, lastDt;
    Vector2 seen, lastPlayer;
    bool lost, havePlayer;
    bool impactPending;
    Vector3 impactAt;
    Vector2 attackAim;
    float attackClock;

    // ---- evasion (EliteEvasion) ----
    float planIn;                 // seconds to its next read of the board
    Vector2 evadeTo, claim;       // the spot it is dodging to; where it means to be shortly
    bool evading, forcedAttack;
    float hitIn = -1f;            // the first hit still coming on its chosen path (< 0: none)
    Vector2 tellAnchor;           // where a holding wind-up began
    float liftClock, liftHeld, parkHeld, liftCheckIn;
    Vector2 liftAim;
    bool liftClear = true;

    public bool Evading => evading;
    public Vector2 EvadeTarget => evadeTo;
    // Where it means to be shortly (other elites keep off it).
    public Vector2 Claim => claim;
    public float HitIn => hitIn;
    // Seconds until a lifting elite joins the play layer.
    public float JoinsIn => State == EliteState.LiftOff ? Mathf.Max(0f, LiftSeconds - liftClock) : 0f;
    // Seconds it has waited for clear air: on its pad, and hovering under the play layer.
    public float PadWait => parkHeld;
    public float HoverWait => liftHeld;
    // Counters (tests, probes): sidesteps begun, wind-ups abandoned, attacks held back.
    public int Evasions { get; private set; }
    public int BreakOffs { get; private set; }
    public int HeldFire { get; private set; }

    // ---- creation --------------------------------------------------------

    // A parked elite on `site`; it lifts off after `parkFor` seconds and
    // ends its lift-off at `liftEnd` (world), where it joins the play.
    public static EliteShip Create(EliteDef def, LandingSite site, float parkFor, Vector2 liftEnd)
    {
        var go = new GameObject(def.key);
        go.transform.SetParent(EliteSystem.Root, false);
        var ship = go.AddComponent<EliteShip>();
        ship.Init(def);
        ship.site = site;
        ship.sitePos = site.Valid ? site.Position : Vector3.zero;
        ship.parkSeconds = Mathf.Max(EngineTellSeconds + .2f, parkFor);
        ship.liftTo = liftEnd;
        ship.transform.position = new Vector3(ship.sitePos.x, ship.sitePos.y, 0f);
        ship.EnterParked();
        return ship;
    }

    // Straight into the play layer at `at` (tests, previews).
    public static EliteShip CreateInPlay(EliteDef def, Vector2 at)
    {
        var go = new GameObject(def.key);
        go.transform.SetParent(EliteSystem.Root, false);
        go.transform.position = at;
        var ship = go.AddComponent<EliteShip>();
        ship.Init(def);
        ship.liftTo = at;
        ship.claim = at;
        ship.EnterPlay();
        ship.State = EliteState.Follow;
        ship.escapeLeft = 0f;
        return ship;
    }

    void Init(EliteDef def)
    {
        Def = def;
        Hearts = def.hearts;
        Brain = EliteBrains.Create(def.brain);
        Attack = EliteAttacks.Create(def.attack);
        Brain.Bind(this);
        Attack.Bind(this);
        frames = EliteArt.Frames(def);
        parkedFrames = EliteArt.ExtraFrames(def, EliteArt.Extra.Parked);
        liftFrames = EliteArt.ExtraFrames(def, EliteArt.Extra.Liftoff);

        hullTf = new GameObject("Hull").transform;
        hullTf.SetParent(transform, false);
        hull = hullTf.gameObject.AddComponent<SpriteRenderer>();
        hull.sortingOrder = PlayOrder;
        if (frames != null) hull.sprite = frames[0];

        col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = def.hullRadius;
        col.enabled = false;

        BuildEngines();
        BuildMuzzleGlows();
        sight = NewPiece(transform, "Sight", EliteFxArt.Sight, PlayOrder - 1);
        sight.enabled = false;

        cooldown = def.attackGap * Mathf.Lerp(.6f, 1.1f, Random.value);
        facing = def.turnsToFace ? def.noseDeg : 90f;
        Live.Add(this);
    }

    void BuildEngines()
    {
        int n = Def.nozzles.Length;
        plumes = new SpriteRenderer[n];
        glows = new SpriteRenderer[n];
        nozzleLocal = new Vector2[n];
        nozzleAngle = new float[n];
        for (int i = 0; i < n; i++)
        {
            var nz = Def.nozzles[i];
            nozzleLocal[i] = Def.PixelToLocal(nz.x, nz.y);
            nozzleAngle[i] = nz.dir >= 0f ? nz.dir : Def.noseDeg + 180f;
            plumes[i] = NewPiece(hullTf, "Plume" + i, ShipExhaust.Frame(Def.exhaustShip, false, 0), PlayOrder - 1);
            plumes[i].transform.localPosition = nozzleLocal[i];
            plumes[i].transform.localRotation = Quaternion.Euler(0f, 0f, nozzleAngle[i] + 90f);
            glows[i] = NewPiece(hullTf, "Glow" + i, EliteFxArt.Glow, PlayOrder + 1);
            glows[i].transform.localPosition = nozzleLocal[i];
            glows[i].color = Def.EngineColor;
        }
    }

    // Charge glows / muzzle flashes, for strips with no tell / action cells.
    void BuildMuzzleGlows()
    {
        int n = Def.cells.tell < 0 || Def.cells.action < 0 ? Def.muzzles.Length : 0;
        muzzleGlows = new SpriteRenderer[n];
        muzzleFlash = new float[n];
        for (int i = 0; i < n; i++)
        {
            muzzleGlows[i] = NewPiece(hullTf, "Muzzle" + i, EliteFxArt.Glow, PlayOrder + 2);
            muzzleGlows[i].transform.localPosition = Def.PixelToLocal(Def.muzzles[i].x, Def.muzzles[i].y);
        }
    }

    void SetHullScale(float s)
    {
        hullBase = s;
        hullTf.localScale = Vector3.one * s;
    }

    static SpriteRenderer NewPiece(Transform parent, string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void OnDestroy()
    {
        Live.Remove(this);
    }

    // ---- states ----------------------------------------------------------

    void EnterParked()
    {
        State = EliteState.Parked;
        stateTime = 0f;
        gameObject.tag = "Untagged";
        col.enabled = false;
        float s = Mathf.Max(.05f, site.scale > 0f ? site.scale : .3f);
        SetHullScale(site.emerge ? s * EmergeStartScale : s);
        hull.sortingOrder = site.order;
        // (docked inside a Space body: hidden, only its engine lights show in the launch tell)
        hull.color = site.emerge ? Docked : Haze;
        if (parkedFrames != null) hull.sprite = parkedFrames[0];
        else if (frames != null) hull.sprite = frames[Mathf.Min(Def.cells.Parked, frames.Length - 1)];
        SetOrders(site.order);
        thrust = 0f;
        RenderEngines(0f);
    }

    void BeginLiftOff()
    {
        State = EliteState.LiftOff;
        stateTime = 0f;
        liftClock = 0f;
        liftCheckIn = 0f;
        // where the pilot is NOW (the director chose its join point when it parked)
        if (EliteEvasion.Enabled) liftTo = EliteEvasion.BestJoin(this, liftTo, LiftSeconds, out liftClear);
        liftAim = liftTo;
        claim = liftTo;
        liftFrom = transform.position;
        if (site.emerge) EliteSystem.Fx.DockFlare(liftFrom, Def.EngineColor, site.order, hullTf.localScale.x * Def.cellWorldSize);
        else EliteSystem.Fx.LiftOffDust(liftFrom, site.order, hullTf.localScale.x * Def.cellWorldSize);
    }

    // Docked inside a Space body (LandingSite.emerge): the hull is not drawn.
    public static readonly Color Docked = new Color(Haze.r, Haze.g, Haze.b, 0f);
    // Flying out of it: the share of the lift-off over which it fades in, and
    // how much smaller than the site's scale it starts (out of the door).
    public const float EmergeFadeShare = .3f, EmergeStartScale = .5f;
    public bool IsDocked => State == EliteState.Parked && site.emerge;

    void EnterPlay()
    {
        State = EliteState.Join;
        stateTime = 0f;
        escapeLeft = EscapeWindow;
        gameObject.tag = "Enimey";
        col.enabled = true;
        SetHullScale(1f);
        hull.color = Color.white;
        hull.sortingOrder = PlayOrder;
        SetOrders(PlayOrder);
        target = ClearTarget.Ensure(gameObject);
        target.enabled = true;
        target.SetRadius(Def.hullRadius);
        target.Elite = this;
        claim = transform.position;
        // (elites read the board on different frames: the plans never pile up in one)
        planIn = (Live.IndexOf(this) % 3) * EliteEvasion.ReactionFor(Def) / 3f;
        // (it arrives at flying speed, not at whatever the lift-off's last frame measured)
        if (EliteEvasion.Enabled) velocity = Vector2.ClampMagnitude(velocity, Def.speed);
        footprint = SpawnFootprint.Attach(gameObject, new Vector2(Def.hullRadius, Def.hullRadius));
        SpawnFootprint.Bind(gameObject, this);
        if (heartsView == null)
        {
            heartsView = gameObject.AddComponent<EliteHearts>();
            heartsView.Bind(this);
        }
        Physics2D.SyncTransforms();
        var p = EliteSystem.Player;
        if (p != null) { seen = p.position; lastPlayer = seen; havePlayer = true; lost = false; }
        Codex.Discover(gameObject);
        Brain.OnJoin();
    }

    void SetOrders(int order)
    {
        if (plumes == null) return;
        for (int i = 0; i < plumes.Length; i++)
        {
            plumes[i].sortingOrder = order - 1;
            glows[i].sortingOrder = order + 1;
        }
        if (sight != null) sight.sortingOrder = order - 1;
    }

    // ---- the step ----------------------------------------------------------

    public void Step(float dt)
    {
        if (dt <= 0f || State == EliteState.Dead) return;
        stateTime += dt;
        if (grace > 0f) grace = Mathf.Max(0f, grace - dt);
        if (hitFlash > 0f) hitFlash = Mathf.Max(0f, hitFlash - dt);
        if (blinkCooldown > 0f) blinkCooldown -= dt;
        exhaustClock += dt;

        switch (State)
        {
            case EliteState.Parked: StepParked(dt); break;
            case EliteState.LiftOff: StepLiftOff(dt); break;
            default: StepPlay(dt); break;
        }
        if (State == EliteState.Dead) return;
        lastDt = dt;
        Animate(dt);
        Render();
    }

    void StepParked(float dt)
    {
        if (site.Valid) sitePos = site.Position;
        transform.position = new Vector3(sitePos.x, sitePos.y, 0f);
        bool siteLeaving = !site.Valid || sitePos.y < EliteSystem.ViewBottom + 2f;
        if (stateTime < parkSeconds && !siteLeaving) return;
        // a clear moment to rise into: it waits on its pad while the air it
        // would join is busy (not for ever, and never past its pad leaving)
        if (!siteLeaving && EliteEvasion.Enabled && parkHeld < EliteEvasion.LiftDelayMax)
        {
            liftCheckIn -= dt;
            if (liftCheckIn <= 0f)
            {
                liftCheckIn = EliteEvasion.ReactionFor(Def);
                EliteEvasion.BestJoin(this, liftTo, LiftSeconds, out liftClear);
            }
            if (!liftClear) { parkHeld += dt; return; }
        }
        BeginLiftOff();
    }

    void StepLiftOff(float dt)
    {
        if (site.Valid) liftFrom = site.Position;
        // the second half: it keeps reading the air it is about to join,
        // slides its join point to the nearest clear spot and, if there is
        // none yet, hovers just under the play layer (still out of reach)
        bool hover = false;
        if (EliteEvasion.Enabled && liftClock >= LiftSeconds * .5f)
        {
            liftCheckIn -= dt;
            if (liftCheckIn <= 0f)
            {
                liftCheckIn = EliteEvasion.ReactionFor(Def);
                liftAim = EliteEvasion.BestJoin(this, liftTo, Mathf.Max(0f, LiftSeconds - liftClock), out liftClear);
            }
            liftTo = Vector2.MoveTowards(liftTo, liftAim, EliteEvasion.LiftSlideSpeed * dt);
            claim = liftTo;
            hover = !liftClear && liftClock >= LiftSeconds * HoverShare && liftHeld < EliteEvasion.LiftHoldMax;
        }
        if (hover) liftHeld += dt;
        else liftClock += dt;
        float k = Mathf.Clamp01(liftClock / LiftSeconds);
        // rises straight off the pad first, then arcs out to the join point
        float e = k * k * (3f - 2f * k);
        Vector2 rise = liftFrom + Vector2.up * .6f;
        Vector2 a = Vector2.Lerp(liftFrom, rise, Mathf.Clamp01(k * 2.5f));
        Vector2 p = Vector2.Lerp(a, liftTo, Mathf.Clamp01((e - .15f) / .85f));
        Vector2 prev = transform.position;
        transform.position = new Vector3(p.x, p.y, 0f);
        if (dt > 0f) velocity = (p - prev) / dt;

        float s0 = Mathf.Max(.05f, site.scale > 0f ? site.scale : .3f);
        if (site.emerge)
        {
            // out of the hangar / surface: from smaller than the docked size, fading in
            SetHullScale(Mathf.Lerp(s0 * EmergeStartScale, 1f, e));
            Color c = Color.Lerp(Haze, Color.white, e);
            float f = k / EmergeFadeShare;
            c.a = f < 1f / 3f ? .34f : f < 2f / 3f ? .67f : 1f;   // fades in in hard steps
            hull.color = c;
        }
        else
        {
            SetHullScale(Mathf.Lerp(s0, 1f, e));
            hull.color = Color.Lerp(Haze, Color.white, e);
        }
        int order = k < .45f ? site.order : k < .9f ? -1 : PlayOrder;
        if (hull.sortingOrder != order) { hull.sortingOrder = order; SetOrders(order); }
        thrust = Mathf.Lerp(.2f, 1.2f, Mathf.Clamp01(k * 2f));
        // (a drawn lift-off cell carries its own ignition: no placeholder shimmer)
        if (k < .5f && Def.cells.liftoff < 0 && liftFrames == null && Mathf.Repeat(stateTime, .12f) < dt) EliteSystem.Fx.HeatShimmer(transform.position, order - 1, hullTf.localScale.x * Def.cellWorldSize);
        if (k >= 1f) EnterPlay();
    }

    // The share of the lift-off after which it may hover, waiting for clear
    // air: under .9, so it is still drawn behind the play layer.
    public const float HoverShare = .86f;

    // ---- evasion -----------------------------------------------------------

    // The brain's wish, made safe: every ReactionSeconds it reads the board
    // (EliteEvasion.Plan) and from then on flies to the spot it chose --
    // the wish itself whenever the way there is clear.
    Vector2 Navigate(Vector2 goal, float speedScale, float dt)
    {
        goal = ClampGoal(goal);
        if (!EliteEvasion.Enabled || Brain.Steadfast) { evading = false; hitIn = -1f; claim = goal; return goal; }
        planIn -= dt;
        if (planIn <= 0f)
        {
            planIn = EliteEvasion.ReactionFor(Def);
            bool was = evading;
            evadeTo = EliteEvasion.Plan(this, goal, speedScale, evadeTo, was, out evading, out hitIn);
            if (evading && !was) Evasions++;
        }
        claim = evading ? evadeTo : goal;
        return claim;
    }

    // Nothing it can steer to gets it clear in time (the skirmisher blinks).
    bool Cornered => evading && hitIn >= 0f && hitIn < EliteEvasion.BlinkClearSeconds;

    // May it start (or, `release`, go through with) its attack now? Not a
    // dash down a blocked line -- up to the pilot and a little past, so what
    // is BEHIND the pilot still catches it -- and not a shot through a
    // friendly elite.
    bool MayCommit(bool release)
    {
        if (!EliteEvasion.Enabled || forcedAttack) return true;
        if (Attack.DrivesMovement)
        {
            Vector2 d;
            float reach;
            Attack.DashLine(seen, release, out d, out reach);
            float seconds = Mathf.Min(Def.actionSeconds * .8f, reach / Mathf.Max(.1f, Def.dashSpeed));
            float wait = release ? 0f : Attack.TellSeconds;
            // not into a rail: the whole run, brake included
            Vector2 end = (Vector2)transform.position + d * (Def.dashSpeed * Def.actionSeconds * .9f);
            if (Mathf.Abs(end.x) + Def.hullRadius * .7f > EliteSystem.RailEdge - .05f) return false;
            if (EliteEvasion.Dash(this, wait, d * Def.dashSpeed, seconds, Attack.Ploughs || Def.armored).Hit) return false;
        }
        // a blink attack: only with somewhere safe to land
        if (Attack.Blinks) { BlinkSpot(); if (!BlinkSpotSafe) return false; }
        return !Attack.FriendlyInLine(seen);
    }

    // A wind-up given up: back to following, a short wait, nothing fired.
    void BreakOff()
    {
        BreakOffs++;
        Attack.Cancel();
        State = EliteState.Follow;
        stateTime = 0f;
        attackPhase = 0;
        sight.enabled = false;
        cooldown = EliteEvasion.BreakOffCooldown;
    }

    // A wind-up that holds its ground (a lance, the siege cannon, the ram,
    // the mortar): it stays planted while that is safe, jinks a little out
    // of the way of something coming if a small move is enough, and gives
    // the attack up if it is not.
    void HoldTell(float dt)
    {
        if (!EliteEvasion.Enabled || forcedAttack) { Brake(dt); return; }
        Vector2 nav = Navigate(tellAnchor, .5f, dt);
        if (!evading) { Brake(dt); return; }
        if (hitIn >= 0f || (evadeTo - tellAnchor).sqrMagnitude > EliteEvasion.JinkReach * EliteEvasion.JinkReach) { BreakOff(); return; }
        Steer(nav, 1f, dt);
    }

    void StepPlay(float dt)
    {
        PlaySeconds += dt;
        if (escapeLeft > 0f) escapeLeft -= dt;
        Perceive(dt);

        Vector2 pos = transform.position;
        bool driven = false;
        if (State == EliteState.Join)
        {
            Vector2 goal = ReachGoal(Brain.Goal(seen, dt));
            Steer(Navigate(goal, 1.35f, dt), 1.35f, dt);
            if (stateTime >= JoinSeconds || (goal - pos).sqrMagnitude < .09f) { State = EliteState.Follow; stateTime = 0f; }
        }
        else if (State == EliteState.Follow)
        {
            StepReachRise(dt);
            Vector2 goal = ReachGoal(Brain.Goal(seen, dt));
            Steer(Navigate(goal, Brain.SpeedScale, dt), Brain.SpeedScale, dt);
            if (Brain.DodgesByBlink && blinkCooldown <= 0f && (ThreatSeverity() > .55f || Cornered))
            {
                Vector2 spot = BlinkSpot();
                if (BlinkSpotSafe) Blink(spot, false);   // (never out of one thing into another)
            }
            cooldown -= dt;
            if (cooldown <= 0f && escapeLeft <= 0f && havePlayer && Brain.WantsAttack(seen))
            {
                if (MayCommit(false)) BeginAttack();
                else { cooldown = EliteEvasion.HoldFireSeconds; HeldFire++; }
            }
        }
        else if (State == EliteState.Attack)
        {
            attackClock += dt;
            if (attackPhase == 0)
            {
                Attack.StepTell(dt);
                if (Attack.HoldsDuringTell) HoldTell(dt);
                else Steer(Navigate(ReachGoal(Brain.Goal(seen, dt)), Brain.SpeedScale * .5f, dt), Brain.SpeedScale * .5f, dt);
                if (State == EliteState.Attack && attackClock >= Attack.TellSeconds)
                {
                    if (!MayCommit(true)) BreakOff();
                    else { attackPhase = 1; attackClock = 0f; evading = false; Attack.BeginAction(); }
                }
            }
            else
            {
                bool done = Attack.StepAction(dt);
                driven = Attack.DrivesMovement;
                if (!driven) Steer(Navigate(ReachGoal(Brain.Goal(seen, dt)), Brain.SpeedScale, dt), Brain.SpeedScale, dt);
                else claim = transform.position;
                if (done) EndAttack();
            }
        }

        Vector2 next = (Vector2)transform.position + velocity * dt;
        transform.position = new Vector3(next.x, next.y, 0f);
        Face(dt);
        KeepInView(dt);
        Collide();
        if (State != EliteState.Dead) Attack.Passive(dt);
        thrust = Mathf.Lerp(thrust, Mathf.Clamp01(velocity.magnitude / Mathf.Max(.1f, Def.speed)) * .8f + .25f, 1f - Mathf.Exp(-6f * dt));
    }

    void BeginAttack()
    {
        State = EliteState.Attack;
        attackPhase = 0;
        attackClock = 0f;
        Attacks++;
        tellAnchor = transform.position;
        Attack.BeginTell(seen);
    }

    void EndAttack()
    {
        Attack.End();
        State = EliteState.Follow;
        stateTime = 0f;
        attackPhase = 0;
        forcedAttack = false;
        sight.enabled = false;
        cooldown = Def.attackGap * Mathf.Lerp(1.25f, .6f, Mathf.Clamp01(Def.aggression)) * Random.Range(.8f, 1.2f);
    }

    // ---- the player's reach (HostileReach) --------------------------------------

    // While it is not about to attack it holds no higher than the cap: the
    // reach ceiling (a pause jump lands on it, a shielded ship flies into
    // it), or the standoff above where it thinks the pilot is when that is
    // higher. Only when the pilot is so close under the cap that its brain
    // could not attack from there (MinAttackAbove) does it climb -- just to
    // its brain's own attack height plus EliteRiseMargin, RiseLead before
    // it is ready, at most RiseMaxSeconds if the attack never starts -- and
    // drop back after the attack.
    bool reachRise;
    float reachRisen;
    public bool ReachRising => reachRise;

    // The highest it holds now.
    public float ReachCap
    {
        get
        {
            if (!HostileReach.Enabled || !havePlayer) return float.PositiveInfinity;
            float cap = HostileReach.Cap(seen.y, Def.hullRadius, Def.hullRadius);
            if (reachRise) cap = Mathf.Max(cap, seen.y + Brain.MinAttackAbove + HostileReach.EliteRiseMargin);
            return cap;
        }
    }

    public float ReachY(float y) => Mathf.Min(y, ReachCap);
    public Vector2 ReachGoal(Vector2 g) { g.y = ReachY(g.y); return g; }

    void StepReachRise(float dt)
    {
        bool wants = false;
        if (HostileReach.Enabled && havePlayer && Brain.MinAttackAbove > 0f)
        {
            float cap = HostileReach.Cap(seen.y, Def.hullRadius, Def.hullRadius);
            wants = cap - seen.y < Brain.MinAttackAbove + .05f &&
                    cooldown <= HostileReach.RiseLead && escapeLeft <= HostileReach.RiseLead;
        }
        if (wants && reachRisen < HostileReach.RiseMaxSeconds)
        {
            reachRise = true;
            reachRisen += dt;
            return;
        }
        if (reachRise && reachRisen >= HostileReach.RiseMaxSeconds) cooldown = Mathf.Max(cooldown, Def.attackGap * .5f);
        reachRise = false;
        reachRisen = 0f;
    }

    // Where it thinks the pilot is. It follows every ordinary move, but a
    // pause-teleport (a jump of TeleportFx.MinimumJump or more in one step)
    // loses it: it keeps flying towards the old spot and only finds the
    // ship again at `perception` u/s.
    void Perceive(float dt)
    {
        var p = EliteSystem.Player;
        if (p == null) { havePlayer = false; return; }
        Vector2 actual = ShipDecoy.Active ? (Vector2)ShipDecoy.Position : (Vector2)p.position;
        if (!havePlayer) { seen = lastPlayer = actual; havePlayer = true; lost = false; return; }
        Vector2 jump = actual - lastPlayer;
        lastPlayer = actual;
        if (!lost && jump.magnitude >= TeleportFx.MinimumJump) lost = true;
        if (lost)
        {
            seen = Vector2.MoveTowards(seen, actual, Def.perception * dt);
            if ((seen - actual).sqrMagnitude < .04f) lost = false;
        }
        else seen = actual;
    }

    // Arrive at `goal`, dodging, within accel and turn limits.
    public void Steer(Vector2 goal, float speedScale, float dt)
    {
        goal = ClampGoal(goal);
        // dodging: a little faster and sharper than its cruise, within limits
        bool hot = evading && EliteEvasion.Enabled;
        Vector2 pos = transform.position;
        Vector2 d = goal - pos;
        float max = Def.speed * Mathf.Max(.1f, speedScale);
        float accel = Def.accel;
        if (hot) { max = Mathf.Max(max, EliteEvasion.EvadeSpeedFor(Def)); accel = EliteEvasion.EvadeAccelFor(Def); }
        float dist = d.magnitude;
        float arrive = Mathf.Min(max, dist * (hot ? 6f : 2.4f));
        // (never faster than it can stop in the room left: no overshooting into a rail)
        if (EliteEvasion.Enabled) arrive = Mathf.Min(arrive, Mathf.Sqrt(2f * accel * dist) * .9f);
        Vector2 want = dist > 1e-4f ? d / dist * arrive : Vector2.zero;
        want += Avoid() * (EliteEvasion.Enabled ? EliteEvasion.ReflexWeight : 1f);
        want += Walls();
        if (want.sqrMagnitude > max * max * 2.25f) want = want.normalized * max * 1.5f;
        // (slowing down is never weaker than its evasive thrust: it can always pull up short of a rail)
        if (EliteEvasion.Enabled && want.sqrMagnitude < velocity.sqrMagnitude) accel = Mathf.Max(accel, EliteEvasion.EvadeAccelFor(Def));
        Accelerate(want, accel, dt, hot ? EliteEvasion.EvadeTurnScale : 1f);
    }

    public void Brake(float dt)
    {
        Accelerate(Avoid() * .5f, Def.accel, dt);
    }

    void Accelerate(Vector2 want, float accel, float dt, float turnScale = 1f)
    {
        Vector2 v = Vector2.MoveTowards(velocity, want, accel * dt);
        // turn-rate limit on the heading of travel
        float sp = v.magnitude, old = velocity.magnitude;
        if (sp > .3f && old > .3f)
        {
            float a0 = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            float turn = Mathf.Clamp(Mathf.DeltaAngle(a0, a1), -Def.turnRate * turnScale * dt, Def.turnRate * turnScale * dt);
            float r = (a0 + turn) * Mathf.Deg2Rad;
            v = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * sp;
        }
        velocity = v;
    }

    // Set by attacks that take the stick (a dash, a dive).
    public void Drive(Vector2 v) { velocity = v; }

    public Vector2 ClampGoal(Vector2 g)
    {
        float edge = EliteSystem.RailEdge - Def.hullRadius - .3f;
        g.x = Mathf.Clamp(g.x, -edge, edge);
        g.y = Mathf.Clamp(g.y, EliteSystem.ViewBottom + Def.hullRadius + .5f, EliteSystem.ViewTop - Def.hullRadius - .4f);
        return g;
    }

    // A soft push off the rails (the hard crash is Collide's).
    Vector2 Walls()
    {
        float x = transform.position.x, edge = EliteSystem.RailEdge - Def.hullRadius;
        float near = .6f;
        Vector2 push = Vector2.zero;
        if (x > edge - near) push.x -= (x - (edge - near)) / near * Def.speed * Def.avoidance;
        if (x < -edge + near) push.x += ((-edge + near) - x) / near * Def.speed * Def.avoidance;
        return push;
    }

    // How hard something on the board is about to hit it (0 none .. 1).
    float lastThreat;
    public float ThreatSeverity() { return lastThreat; }

    // Dodging: every hazard it would meet within lookAhead seconds of
    // closing (the board falls at the scroll speed; other elites fly their
    // own velocity) pushes it sideways off the line, harder the sooner and
    // the more squarely. Skill (avoidance) and reaction (lookAhead) are
    // limited, so a fast board, a dash or a tight squeeze still catch it.
    Vector2 Avoid()
    {
        Vector2 pos = transform.position;
        Vector2 push = Vector2.zero;
        float worst = 0f;
        float scroll = EliteSystem.Scroll;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.gameObject == gameObject || t.Mother == this || !ClearTarget.IsHazard(t.gameObject)) continue;
            Vector2 hp = t.transform.position;
            // (what it was measured doing this step, when the sensor has it: a body that holds a
            // station in the world does not ride the scroll)
            Vector2 hv = EliteEvasion.Enabled && EliteEvasion.Measured(t) ? t.SensedVelocity : HazardVelocity(t, scroll);
            Vector2 r = hp - pos;
            Vector2 rv = hv - velocity;
            float R = Def.hullRadius + t.Radius + .15f;
            float rv2 = rv.sqrMagnitude;
            float tca = rv2 > 1e-4f ? -Vector2.Dot(r, rv) / rv2 : 0f;
            if (tca <= 0f)
            {
                if (r.sqrMagnitude < R * R) { push -= r.normalized; worst = Mathf.Max(worst, 1f); }
                continue;
            }
            if (tca > Def.lookAhead) continue;
            Vector2 closest = r + rv * tca;
            float dd = closest.magnitude;
            if (dd >= R) continue;
            Vector2 away;
            if (dd > 1e-3f) away = -closest / dd;
            else
            {
                // dead centre: go towards the middle of the lane
                away = new Vector2(pos.x > 0f ? -1f : 1f, 0f);
            }
            float urgency = (1f - tca / Def.lookAhead) * (1f - dd / R) + .25f;
            push += away * urgency;
            worst = Mathf.Max(worst, urgency);
        }
        lastThreat = worst;
        return push * Def.avoidance * Def.speed * 2.2f;
    }

    static Vector2 HazardVelocity(ClearTarget t, float scroll)
    {
        var live = Live;
        for (int i = 0; i < live.Count; i++)
            if (live[i] != null && live[i].gameObject == t.gameObject) return live[i].velocity;
        if (t.TryGetComponent(out ChaserEnemy _)) return Vector2.zero;
        return new Vector2(0f, -scroll);
    }

    void KeepInView(float dt)
    {
        Vector3 p = transform.position;
        float bottom = EliteSystem.ViewBottom + Def.hullRadius * .6f, top = EliteSystem.ViewTop + 1.5f;
        if (p.y < bottom) { p.y = bottom; if (velocity.y < 0f) velocity.y = 0f; }
        if (p.y > top) { p.y = top; if (velocity.y > 0f) velocity.y = 0f; }
        transform.position = p;
    }

    void Face(float dt)
    {
        float want;
        float? pref = State == EliteState.Attack ? Attack.FaceDeg : null;
        if (pref == null) pref = Brain.FaceDeg(seen);
        if (pref != null) want = pref.Value;
        else if (velocity.sqrMagnitude > .16f) want = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        else want = facing;
        facing = Mathf.MoveTowardsAngle(facing, want, Def.turnRate * 1.4f * dt);
        float b = -Mathf.Clamp(velocity.x / Mathf.Max(.1f, Def.speed), -1f, 1f) * Def.maxBank;
        bank = Mathf.Lerp(bank, b, 1f - Mathf.Exp(-6f * dt));
    }

    // ---- collisions -------------------------------------------------------

    void Collide()
    {
        if (!InPlay) return;
        Vector2 pos = transform.position;

        // the side rails
        float edge = EliteSystem.RailEdge;
        if (Mathf.Abs(pos.x) + Def.hullRadius * .7f > edge)
        {
            float side = Mathf.Sign(pos.x);
            pos.x = side * (edge - Def.hullRadius * .7f - .01f);
            transform.position = new Vector3(pos.x, pos.y, 0f);
            velocity.x = -side * Mathf.Max(1.5f, Mathf.Abs(velocity.x) * .6f);
            if (grace <= 0f)
            {
                Crashes++;
                EliteSystem.Fx.Sparks(new Vector2(side * edge, pos.y), Def.ShotColor, 8);
                HitBy = "rail";
                TakeHit(EliteDamage.Rail, new Vector3(side * edge, pos.y, 0f));
                if (State == EliteState.Dead) return;
            }
        }

        if (grace > 0f) return;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.gameObject == gameObject || t.Mother == this || !ClearTarget.IsHazard(t.gameObject)) continue;
            Vector2 hp = t.transform.position;
            float R = Def.hullRadius * .85f + t.Radius * .8f;
            if ((hp - pos).sqrMagnitude > R * R) continue;
            CrashInto(t.gameObject, hp);
            return;   // one crash a step (the registry changes under a kill)
        }
    }

    void CrashInto(GameObject other, Vector2 at)
    {
        Crashes++;
        Vector2 pos = transform.position;
        Vector2 away = pos - at;
        away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.down;
        var elite = other.GetComponent<EliteShip>();
        if (elite != null)
        {
            if (elite.grace > 0f) return;
            velocity = away * 3f;
            elite.velocity = -away * 3f;
            HitBy = "elite body";
            elite.TakeHit(EliteDamage.Crash, pos);
            HitBy = "elite body";
            TakeHit(EliteDamage.Crash, at);
            return;
        }
        bool rock = other.CompareTag("Astr");
        string what = SourceName(other);
        FriendlyKill(other);
        if (rock && Acting && Attack.Ploughs)
        {
            // a ploughing attack (ice_ram) breaks through rocks, keeps going
            Ploughed++;
            Attack.OnPlough(at);
            return;
        }
        if (Def.armored && rock)
        {
            velocity += away * 1f;
            return;
        }
        velocity = away * 2.5f + velocity * .3f;
        HitBy = what;
        TakeHit(EliteDamage.Crash, at);
    }

    // What a hazard is, for the diagnostics (constant strings).
    public static string SourceName(GameObject go)
    {
        var d = EnemyIdentity.Of(go);
        if (d == null) return go != null && go.CompareTag("Astr") ? "rock body" : "enemy body";
        switch (d.role)
        {
            case EnemyRole.Rock: return "rock body";
            case EnemyRole.Mine: return "mine body";
            case EnemyRole.Chaser: return "chaser body";
            case EnemyRole.Alien: return "alien body";
            case EnemyRole.Big: return "heavy body";
            default: return "fighter body";
        }
    }

    // A hazard destroyed by an elite (a crash, its shots): the blast and the
    // sound, but nothing for the pilot -- no score, no dust, no codex.
    public static void FriendlyKill(GameObject go)
    {
        if (go == null) return;
        FriendlyKills++;
        TargetExplosion.Spawn(go, ShipId.None);
        EnemyDeathAudio.Play(go);
        ClearTarget.Release(go);
        BossUtil.Kill(go);
    }

    // ---- damage -------------------------------------------------------------

    // One heart (two for a shielded ram); false if it was in its grace.
    public bool TakeHit(EliteDamage cause, Vector3 at, int amount = 1)
    {
        string by = HitBy;
        HitBy = null;
        if (State == EliteState.Dead || !InPlay) return false;
        // (Domino: the death crash's wreckage, DeathCrash; Teleport: a pause jump aimed at it always connects)
        if (grace > 0f && cause != EliteDamage.ShieldRam && cause != EliteDamage.Domino && cause != EliteDamage.Teleport) return false;
        // (a shield link or an armour plate soaks it: no heart)
        if (Attack != null && Attack.Absorbs(cause, at)) return false;
        Hearts = Mathf.Max(0, Hearts - Mathf.Max(1, amount));
        LastHitCause = cause;
        LastHitBy = by;
        impactPending = true;
        impactAt = at;
        grace = GraceSeconds;
        hitFlash = HitFlashSeconds;
        Vector2 away = (Vector2)transform.position - (Vector2)at;
        if (away.sqrMagnitude > 1e-6f) velocity += away.normalized * 1.5f;
        if (heartsView != null) heartsView.Refresh();
        EliteSystem.Fx.Sparks(at, Def.HeartColor, 6);
        if (Hearts <= 0) Die(cause);
        return true;
    }

    public bool TakeImpact(out Vector3 at)
    {
        at = impactAt;
        bool had = impactPending;
        impactPending = false;
        return had;
    }

    void Die(EliteDamage cause)
    {
        State = EliteState.Dead;
        Kills++;
        LastKillCause = cause;
        if (cause == EliteDamage.Crash || cause == EliteDamage.Rail || cause == EliteDamage.FriendlyFire) CrashKills++;
        Live.Remove(this);
        col.enabled = false;
        if (target != null) ClearTarget.Release(gameObject);
        if (footprint != null) footprint.enabled = false;
        if (Attack != null) { Attack.OnDeath(); Attack.End(); }
        EliteRewards.Pay(this);
        if (Died != null) Died(this, cause, LastHitBy);
        EliteDeath.Play(this, cause);
        // Brought down by the pilot: it may set off a DEATH COMBO.
        if (cause == EliteDamage.PlayerWeapon || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam)
            DeathCombo.OnPlayerKill(gameObject);
        BossUtil.Kill(gameObject);
    }

    // IShipAttackTarget: every player weapon, the ultimate's homing shots,
    // the red atom's free shot and the secret powers (ShipAttackHits).
    public void TakeShipAttack(int ship, float weight, Vector3 at)
    {
        if (!InPlay || weight <= 0f) return;
        Vector3 blast = Vector3.Lerp(transform.position, at, .5f);
        if (TakeHit(EliteDamage.PlayerWeapon, at))
            TargetExplosion.Spawn(blast, TargetExplosion.KindForWorld(Def != null ? Def.world : null), TargetExplosion.Size.Small, ship);
    }

    // ---- hooks for the pilot's side ----------------------------------------

    // TeleportFx.Strike: a pause jump landed on it -- every heart it has
    // left, like a shielded ram: a guaranteed kill. It resolves on the
    // landing frame, while the world is still frozen (the elite has not
    // moved since the pilot aimed), and its grace window does not refuse
    // it. (Were it ever to survive, it is flung out of the landing blast
    // so the ship doesn't sit inside it.) A jump onto an elite's shot
    // erases the shot. True when handled.
    public static bool TeleportStrike(GameObject go, Vector3 at)
    {
        if (go == null) return false;
        var elite = go.GetComponent<EliteShip>();
        if (elite != null)
        {
            if (!elite.InPlay) return true;
            elite.TakeHit(EliteDamage.Teleport, at, elite.Hearts);
            if (elite != null && elite.State != EliteState.Dead) elite.Shove(at, TeleportFx.BlastRadius + elite.Def.hullRadius + .1f);
            return true;
        }
        // (a rail mine's laser only where the hull lands on it: RailMineLaser.BlinkStrike)
        return EliteShots.EraseHitbox(go) || RailMineLaser.BlinkStrike(go, at);
    }

    // collisionDetection, shielded (blue atom / Cloak): the ram takes both
    // hearts; an elite shot is absorbed. True when handled.
    public static bool ShieldRam(GameObject go, Vector3 shipAt)
    {
        if (go == null) return false;
        var elite = go.GetComponent<EliteShip>();
        if (elite != null)
        {
            elite.TakeHit(EliteDamage.ShieldRam, shipAt, elite.Hearts);
            return true;
        }
        // (a rail mine's laser is absorbed the same way: RailMineLaser)
        return EliteShots.EraseHitbox(go) || RailMineLaser.EraseHitbox(go);
    }

    // collisionDetection, unshielded: the pilot pays a heart as for any
    // enemy; the elite loses one too and is knocked away instead of being
    // destroyed. True for an elite (don't Destroy it); an elite shot is
    // left to the normal path (its hitbox is destroyed, the shot recycles).
    public static bool Rammed(GameObject go, Vector3 shipAt)
    {
        if (go == null) return false;
        var elite = go.GetComponent<EliteShip>();
        if (elite == null) return false;
        elite.TakeHit(EliteDamage.PlayerContact, shipAt);
        if (elite != null && elite.State != EliteState.Dead) elite.Shove(shipAt, elite.Def.hullRadius + .75f);
        return true;
    }

    void Shove(Vector3 from, float distance)
    {
        Vector2 away = (Vector2)transform.position - (Vector2)from;
        away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up;
        Vector2 p = (Vector2)from + away * distance;
        p = ClampGoal(p);
        transform.position = new Vector3(p.x, p.y, 0f);
        velocity = away * 3f;
        Physics2D.SyncTransforms();
    }

    // ---- blink (skirmisher) ---------------------------------------------------

    public Vector2 BlinkSpot()
    {
        Vector2 pos = transform.position;
        Vector2 toPilot = seen - pos;
        Vector2 side = toPilot.sqrMagnitude > 1e-4f ? new Vector2(-toPilot.y, toPilot.x).normalized : Vector2.right;
        Vector2 best = pos;
        float bestScore = float.MinValue;
        BlinkSpotSafe = true;
        if (!EliteEvasion.Enabled)
        {
            for (int k = 0; k < 6; k++)
            {
                float sign = k % 2 == 0 ? 1f : -1f;
                float dist = Def.blinkDistance * (1f - .15f * (k / 2));
                Vector2 c = ClampGoal(pos + side * sign * dist + Vector2.up * (k / 2) * .3f);
                float score = Clearance(c) - Mathf.Abs((c - seen).magnitude - Def.keepDistance) * .3f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
        // either side, three lengths, level / a little up / a little down:
        // never into something that is on its way to that spot
        for (int k = 0; k < 18; k++)
        {
            float sign = k % 2 == 0 ? 1f : -1f;
            float dist = Def.blinkDistance * (1f - .2f * ((k / 2) % 3));
            float lift = k < 6 ? 0f : k < 12 ? .5f : -.5f;
            Vector2 c = ClampGoal(pos + side * sign * dist + Vector2.up * lift);
            float score = Clearance(c) - Mathf.Abs((c - seen).magnitude - Def.keepDistance) * .3f - Mathf.Abs(lift) * .2f;
            var o = EliteEvasion.Hold(this, c, 0f, EliteEvasion.BlinkClearSeconds);
            if (o.Hit) score -= 5f + (EliteEvasion.BlinkClearSeconds - o.hitIn) * 6f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = c;
            BlinkSpotSafe = !o.Hit;
        }
        return best;
    }

    // Whether the last BlinkSpot() found a spot that is.
    public bool BlinkSpotSafe { get; private set; }

    // Distance to the nearest hazard's edge from `at` (capped).
    float Clearance(Vector2 at)
    {
        float best = 3f;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.gameObject == gameObject || !ClearTarget.IsHazard(t.gameObject)) continue;
            float d = ((Vector2)t.transform.position - at).magnitude - t.Radius - Def.hullRadius;
            if (d < best) best = d;
        }
        return best;
    }

    public int Blinks { get; private set; }
    public int Ploughed { get; private set; }

    public void Blink(Vector2 to, bool attack)
    {
        Vector2 from = transform.position;
        EliteSystem.Fx.BlinkBurst(from, Def.ShotColor, Def.cellWorldSize);
        transform.position = new Vector3(to.x, to.y, 0f);
        EliteSystem.Fx.BlinkBurst(to, Def.ShotCore, Def.cellWorldSize * .8f);
        velocity *= .3f;
        blinkCooldown = attack ? 1f : 2.2f;
        planIn = 0f;
        evading = false;
        claim = to;
        Blinks++;
        Physics2D.SyncTransforms();
    }

    // ---- muzzles ------------------------------------------------------------

    public Transform HullTransform => hullTf;

    // The drawing's rotation right now (degrees).
    public float ArtRotation => (Def.turnsToFace ? facing - Def.noseDeg : bank) + lean;

    public Vector2 MuzzleWorld(int i)
    {
        if (Def.muzzles.Length == 0) return transform.position;
        var m = Def.muzzles[Mathf.Clamp(i, 0, Def.muzzles.Length - 1)];
        Vector2 local = Def.PixelToLocal(m.x, m.y) * HullScale;
        return (Vector2)transform.position + Rotate(local, ArtRotation);
    }

    // The way a shot leaves muzzle i (world degrees).
    public float MuzzleDeg(int i)
    {
        if (Def.muzzles.Length == 0) return facing;
        var m = Def.muzzles[Mathf.Clamp(i, 0, Def.muzzles.Length - 1)];
        return m.dir >= 0f ? m.dir + ArtRotation : facing;
    }

    public Vector2 NozzleWorld(int i)
    {
        if (nozzleLocal == null || nozzleLocal.Length == 0) return transform.position;
        return (Vector2)transform.position + Rotate(nozzleLocal[Mathf.Clamp(i, 0, nozzleLocal.Length - 1)] * HullScale, ArtRotation);
    }

    public int NozzleCount => nozzleLocal != null ? nozzleLocal.Length : 0;

    public static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    public bool SightShown => sight != null && sight.enabled;

    // Shows the siege cannon's (or the Rift Lancer's) sight line from `from` along `deg`.
    public void ShowSight(Vector2 from, float deg, float length, bool on) { ShowSight(from, deg, length, on, .06f); }

    // ... `width` world units wide (the Frost elites' bolder sights).
    public void ShowSight(Vector2 from, float deg, float length, bool on, float width)
    {
        if (sight == null) return;
        sight.enabled = on;
        if (!on) return;
        sight.transform.position = new Vector3(from.x, from.y, 0f);
        sight.transform.rotation = Quaternion.Euler(0f, 0f, deg - 90f);
        sight.transform.localScale = new Vector3(width, length, 1f);
        Color c = Def.ShotColor;
        c.a = width > .1f ? 1f : .7f;   // (the bolder sights read over Frost's bright cloud decks)
        sight.color = c;
    }

    // ---- drawing ---------------------------------------------------------------

    void Animate(float dt)
    {
        if (frames == null) return;
        var c = Def.cells;
        int frame;
        if (State == EliteState.Parked)
        {
            if (parkedFrames != null) { SetSprite(parkedFrames[Mathf.FloorToInt(stateTime / (4f * EliteArt.Tick)) % parkedFrames.Length]); CurrentFrame = 0; return; }
            // the launch tell: the lit cell blinks with the engine lights
            frame = c.parkedIdle >= 0 && ParkedLightsOn ? c.parkedIdle : c.Parked;
        }
        else if (State == EliteState.LiftOff && liftFrames != null)
        {
            int f = Mathf.Min(liftFrames.Length - 1, Mathf.FloorToInt(liftClock / LiftSeconds * liftFrames.Length));
            SetSprite(liftFrames[f]);
            CurrentFrame = 0;
            return;
        }
        else if (State == EliteState.LiftOff && c.liftoff >= 0)
            frame = liftClock < LiftSeconds * LiftCellShare ? c.liftoff : FlightFrame(dt);
        else if (hitFlash > 0f && c.hit >= 0) frame = c.hit;
        else if (State == EliteState.Attack && attackPhase == 0 && c.tell >= 0) frame = c.tell;
        else if (State == EliteState.Attack && attackPhase == 1 && c.action >= 0) frame = c.action;
        else frame = FlightFrame(dt);
        CurrentFrame = frame;
        SetSprite(frames[Mathf.Min(frame, frames.Length - 1)]);
    }

    // The share of the lift-off drawn with the lift-off cell (then flight).
    public const float LiftCellShare = .6f;

    // The flying drawing: the flight loop, a bank cell while it slides
    // sideways (by its sideways speed, with a little hysteresis), the
    // damaged cell for good once it has lost a heart.
    int FlightFrame(float dt)
    {
        var c = Def.cells;
        if (Damaged) return c.damaged;
        if (c.Banks)
        {
            float side = velocity.x / Mathf.Max(.1f, Def.speed);
            if (bankCell == 0 && Mathf.Abs(side) > BankEnter) bankCell = side < 0f ? -1 : 1;
            else if (bankCell != 0 && (Mathf.Abs(side) < BankExit || Mathf.Sign(side) != bankCell)) bankCell = 0;
            if (bankCell != 0) return bankCell < 0 ? c.bankLeft : c.bankRight;
        }
        int n = c.flight.Length;
        if (n <= 1) return c.Flight0;
        frameHold -= dt;
        if (frameHold <= 0f)
        {
            idleStep = (idleStep + 1) % n;
            float hold = EliteArt.IdleTicks[idleStep % EliteArt.IdleTicks.Length] * EliteArt.Tick;
            frameHold += hold;
            if (frameHold <= 0f) frameHold = hold;
        }
        return c.flight[idleStep % n];
    }

    public const float BankEnter = .35f, BankExit = .2f;
    // Lost a heart and has a damaged drawing: shown from then on.
    public bool Damaged => Def.cells.damaged >= 0 && (Hearts < Def.hearts || Attack.ShowsDamaged) && InPlay;
    public int BankCell => bankCell;

    void SetSprite(Sprite s)
    {
        if (hull.sprite != s) hull.sprite = s;
    }

    // The launch tell: running lights blink on in its last parked second.
    bool ParkedLightsOn => State == EliteState.Parked && stateTime >= parkSeconds - EngineTellSeconds &&
                           Mathf.FloorToInt(stateTime / (3f * EliteArt.Tick)) % 2 == 0;

    void Render()
    {
        hullTf.localRotation = Quaternion.Euler(0f, 0f, ArtRotation);
        if (State == EliteState.Parked)
        {
            RenderEngines(0f, ParkedLightsOn ? .9f : 0f);
            return;
        }
        RenderEngines(thrust, Mathf.Clamp01(thrust));
        RenderProcedural();
    }

    // ---- procedural tell / action / hit (strips without those cells) ----

    public const float FlashSeconds = .1f, RecoilKick = .14f, SquashMax = .07f, LeanMax = 6f;
    static readonly Color HitTint = new Color(1f, .55f, .95f, 1f);

    public bool ChargeGlowOn { get; private set; }
    public int MuzzleFlashesShown { get; private set; }
    public float RecoilOffset => recoilLen;
    public float Squash { get; private set; }
    public float Lean => lean;

    // EliteAttack.Fire: a shot left muzzle i heading `deg`.
    public void OnFired(int muzzle, float deg)
    {
        if (muzzleFlash == null || muzzleFlash.Length == 0) return;
        muzzleFlash[Mathf.Clamp(muzzle, 0, muzzleFlash.Length - 1)] = FlashSeconds;
        MuzzleFlashesShown++;
        float r = deg * Mathf.Deg2Rad;
        recoilDir = -new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        recoilLen = RecoilKick * Def.cellWorldSize / 1.5f;
    }

    void RenderProcedural()
    {
        var c = Def.cells;
        float dt = lastDt;
        // the tell: a charge glow at the muzzles, growing in hard steps and
        // blinking faster near the end, and a squash and lean into it
        bool charging = Telling && c.tell < 0 && muzzleGlows.Length > 0;
        float k = charging ? Mathf.Clamp01(attackClock / Mathf.Max(.05f, Attack.TellSeconds)) : 0f;
        ChargeGlowOn = false;
        for (int i = 0; i < muzzleGlows.Length; i++)
        {
            var g = muzzleGlows[i];
            if (muzzleFlash[i] > 0f) muzzleFlash[i] = Mathf.Max(0f, muzzleFlash[i] - dt);
            if (muzzleFlash[i] > 0f && c.action < 0)
            {
                g.enabled = true;
                g.color = Def.ShotCore;
                g.transform.localScale = Vector3.one * Def.cellWorldSize * (muzzleFlash[i] > FlashSeconds * .5f ? .34f : .22f);
            }
            else if (charging)
            {
                float step = k < .34f ? .1f : k < .67f ? .16f : .22f;
                bool blinkOn = Mathf.FloorToInt(attackClock / ((k > .7f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
                g.enabled = true;
                Color col = Def.ShotColor;
                col.a = blinkOn ? 1f : .55f;
                g.color = col;
                g.transform.localScale = Vector3.one * Def.cellWorldSize * step;
                ChargeGlowOn = true;
            }
            else g.enabled = false;
        }
        float squash = charging ? SquashMax * (k < .34f ? .4f : k < .67f ? .7f : 1f) : 0f;
        Squash = squash;
        float leanWant = charging ? -Mathf.Clamp(Attack.Aim.x - transform.position.x, -1f, 1f) * LeanMax : 0f;
        lean = Mathf.MoveTowards(lean, leanWant, 40f * dt);
        if (recoilLen > 0f) recoilLen = Mathf.Max(0f, recoilLen - recoilLen * 12f * dt - .05f * dt);
        if (c.tell < 0 || c.action < 0)
        {
            hullTf.localScale = new Vector3(hullBase * (1f + squash), hullBase * (1f - squash), 1f);
            hullTf.localPosition = recoilDir * recoilLen;
        }
        // a hit with no hit cell: the current frame flashes
        if (InPlay && c.hit < 0)
            hull.color = hitFlash > 0f && Mathf.FloorToInt(hitFlash / EliteArt.Tick) % 2 == 0 ? HitTint : Color.white;
    }

    public bool EngineLightsOn { get; private set; }
    public float Thrust => thrust;

    void RenderEngines(float plume) { RenderEngines(plume, plume); }

    void RenderEngines(float plume, float light)
    {
        EngineLightsOn = light > .05f;
        if (plumes == null) return;
        int ticks = Mathf.FloorToInt(exhaustClock * 24f);
        float len = Def.cellWorldSize * Def.exhaustScale * plume;
        for (int i = 0; i < plumes.Length; i++)
        {
            var p = plumes[i];
            bool showPlume = len > .02f && Def.exhaustScale > 0f;
            p.enabled = showPlume;
            if (showPlume)
            {
                var s = ShipExhaust.Frame(Def.exhaustShip, false, ShipExhaust.FrameAt(Def.exhaustShip, ticks + i * 3));
                if (s != null)
                {
                    if (p.sprite != s) p.sprite = s;
                    float sx = len * .42f / Mathf.Max(.01f, s.bounds.size.x), sy = len / Mathf.Max(.01f, s.bounds.size.y);
                    p.transform.localScale = new Vector3(sx, sy, 1f);
                }
                else p.enabled = false;
            }
            var g = glows[i];
            g.enabled = light > .05f;
            if (g.enabled)
            {
                Color c = Def.EngineColor;
                c.a = Mathf.Clamp01(light);
                g.color = c;
                g.transform.localScale = Vector3.one * Def.cellWorldSize * Def.glowScale * (.7f + .5f * light);
            }
        }
    }

    // ---- IMovementFootprint (SpawnSpace) ---------------------------------------

    // Where it may be over the next moments: it holds its place in the
    // world (rising through the board at the scroll speed) plus its own
    // flight. SpawnSpace only reserves SteerHorizon of it; regular spawns
    // keep off that, the elite looks after itself after.
    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        float own = Mathf.Max(Def.speed, Def.dashSpeed) * to;
        return Rect.MinMaxRect(center.x - half.x - own, center.y - half.y - own,
                               center.x + half.x + own, center.y + half.y + own + SpawnSpace.ScrollSpeed * to);
    }

    public bool SelfSteering => true;

    // ISpawnShadow: the column it is flying -- hull-wide, from the ship to
    // where it means to be, and up the board as far as the scroll covers in
    // EliteEvasion.SpawnShadowSeconds. The spawner drops nothing new into it.
    public bool SpawnShadow(out Rect column)
    {
        column = default;
        float seconds = EliteEvasion.Enabled ? EliteEvasion.SpawnShadowSeconds : 0f;
        if (seconds <= 0f || !InPlay) return false;
        Vector2 p = transform.position;
        float pad = Def.hullRadius + EliteEvasion.SpawnShadowPad;
        // (towards where it is heading, but never a wide slab of the lane: a far goal is not a column)
        float toX = p.x + Mathf.Clamp(claim.x - p.x, -EliteEvasion.SpawnShadowLead, EliteEvasion.SpawnShadowLead);
        float toY = p.y + Mathf.Clamp(claim.y - p.y, -EliteEvasion.SpawnShadowLead, EliteEvasion.SpawnShadowLead);
        column = Rect.MinMaxRect(Mathf.Min(p.x, toX) - pad, Mathf.Min(p.y, toY) - Def.hullRadius,
                                 Mathf.Max(p.x, toX) + pad, Mathf.Max(p.y, toY) + Def.hullRadius + SpawnSpace.ScrollSpeed * seconds);
        return true;
    }

    // ---- direct control (tests, previews) ---------------------------------------

    public void ForceLiftOff() { if (State == EliteState.Parked) { stateTime = parkSeconds; } }
    // (a forced attack runs whatever is in its way: no hold, jink or break-off)
    public void ForceAttack() { if (State == EliteState.Follow || State == EliteState.Join) { State = EliteState.Follow; cooldown = 0f; escapeLeft = 0f; forcedAttack = true; BeginAttack(); } }
    public void SetSeen(Vector2 p) { seen = p; lastPlayer = p; havePlayer = true; lost = false; }
    public float ParkSeconds => parkSeconds;
}
