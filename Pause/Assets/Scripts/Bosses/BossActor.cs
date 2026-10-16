using UnityEngine;

// The boss on screen: its flipbook, its drift near the top of the screen,
// its attack scheduler and its arrival / death / retreat sequences.
//
// Driven entirely by BossEncounter.Step, never by its own Update, so a test
// can run a whole fight frame by frame. Two clocks:
//   dt      world time (real dt * timeScale) -- the fight, the drift, the
//           idle loop and every attack freeze whenever the world does;
//   realDt  unscaled -- the warp-in during the frozen intro, and the hit
//           flash (which lands during the ultimate's 0.06x slow motion).
//
// Attacks come out of the boss's body (BossCatalog, BossEmitters): every
// shot starts at its part's muzzle pixel in the drawing on screen the
// moment it fires, every laser is rooted there for as long as it burns, and
// the tell's charge gathers at those same parts.
public class BossActor : MonoBehaviour, IMusicPatternHost
{
    public enum Mode { Hidden, Arriving, Fighting, Dying, Retreating, Gone }
    const float MaxAlignHold = 2.5f;
    double alignBeat = -1.0;   // beat position the pattern in its tell starts on (beat-synced only)
    float alignHold;
    enum AttackPhase { Cooldown, Tell, Volleys, Beams, Hazards, Pattern }

    // The most parts one attack fires from (the Bloom Queen's six petals).
    public const int MaxParts = 8;

    BossDef boss;
    SpriteRenderer body;
    SpriteRenderer smoke, arcs;   // battle damage over the body (BossArt damage fx)
    SpriteRenderer white;         // the hit flash: an additive white silhouette of the body (BossImpact)
    float whiteLeft, whitePeak;   // unscaled
    int damageStage;              // hearts lost, 0..BossArt.DamageStages
    readonly SpriteRenderer[] charges = new SpriteRenderer[MaxParts];
    readonly SpriteRenderer[] rings = new SpriteRenderer[MaxParts];
    GameObject bodyHit;
    BossHearts hearts;
    Mode mode = Mode.Hidden;

    // animation
    float animClock;       // world time, idle loop
    float realClock;       // unscaled, arrival
    float flashLeft;       // unscaled
    float fireLeft;        // world time: fire pose / muzzle flash hold
    float tellAge;         // world time inside the current tell
    float outroClock;
    int explosionsFired;
    bool hasDeathArt;      // the outro plays the boss's death strip over the body

    // movement
    float moveClock;
    Vector3 basePos;
    float retreatVy;

    // attacks
    AttackPhase phase = AttackPhase.Cooldown;
    BossAttack current;
    int attackIndex = -1;
    float phaseTimer;
    int volleysFired;
    int firedMask;          // parts that fired in the last volley (muzzle flashes)
    float bodyRespawn;
    readonly int[] laneOrder = new int[16];
    // The themed area hazards of the attack in progress (BossExecutors), and how long the boss has waited on them.
    readonly AttackHazard[] armed = new AttackHazard[BossExecutors.MaxHazards];
    int armedCount;
    float hazardClock;
    // Safety: a hazard nobody steps (a scene without the elite system) must not hold the boss forever.
    public const float MaxHazardSeconds = 9f;

    public BossDef Boss => boss;
    public Mode State => mode;
    public BossAttack CurrentAttack => phase == AttackPhase.Cooldown ? null : current;
    public bool Telegraphing => phase == AttackPhase.Tell;
    public GameObject BodyHitbox => bodyHit;
    // Its health, spinning round it (null until the fight starts).
    public BossHearts Hearts => hearts;
    // The body frame index on screen (emitters are measured on it); with
    // battle damage the idle drawing shown is a damaged hull instead.
    public int BodyFrame { get; private set; }
    public int DamageStage => damageStage;
    public SpriteRenderer DamageSmoke => smoke;
    public SpriteRenderer DamageArcs => arcs;
    public int AttacksStarted { get; private set; }
    public int VolleysFired => volleysFired;
    public bool HazardPhase => phase == AttackPhase.Hazards;
    public int ArmedHazards => armedCount;
    // Bullet-hell patterns (BossPatternRunner): the runner, the telegraph markers, and what the host answers while one plays.
    readonly BossPatternRunner runner = new BossPatternRunner();
    BossMusicDirector musicDirector;
    BossAttack musicAttack;
    bool musicRunning;
    public BossMusicDirector MusicDirector => musicDirector;
    BossPatternMarkers markers;
    Vector3 playerNow;
    BossProjectilePool poolNow;
    int patternSeed = 11;
    public BossPatternRunner Runner => runner;
    // Tests: drive the fight from this clock instead of the boss song's (BossBeat.Source).
    public IBeatSource BeatOverride;
    // The musical director's last fire: which pose's fire drawing is up (the source that fired).
    int musicFirePose;
    float fireStamp = -1f;
    public BossPatternMarkers Markers => markers;
    public bool PatternPhase => phase == AttackPhase.Pattern;
    public AttackHazard ArmedHazard(int i) => i >= 0 && i < armedCount ? armed[i] : null;
    // The world this boss belongs to (its art key's index: Space 0 ... Tide 4).
    public int World => Mathf.Max(0, BossEmitters.World(boss));

    // Tests / previews: the next attack picked is this one (index into
    // boss.attacks), whatever is unlocked; -1 for the normal rotation.
    public int ForcedAttack = -1;

    public static BossActor Spawn(BossDef def)
    {
        var go = new GameObject("~Boss_" + def.artKey);
        var actor = go.AddComponent<BossActor>();
        actor.Build(def);
        return actor;
    }

    void Build(BossDef def)
    {
        boss = def;
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(transform, false);
        bodyGo.transform.localScale = Vector3.one * BossConfig.BossWorldSize;
        body = bodyGo.AddComponent<SpriteRenderer>();
        body.sortingOrder = -3;
        white = Overlay("HitFlash", 0);
        var flashShader = Resources.Load<Shader>("BossFlash");
        if (flashShader != null) white.sharedMaterial = new Material(flashShader) { name = "BossFlash" };
        if (BossArt.HasDamageArt(def))
        {
            smoke = Overlay("DamageSmoke", -2);
            arcs = Overlay("DamageArcs", -1);
        }

        for (int i = 0; i < MaxParts; i++)
        {
            charges[i] = Glow("Charge" + i, 29);
            rings[i] = Glow("Ring" + i, 28);
        }

        transform.position = new Vector3(0f, ArrivalY, 0f);
        basePos = new Vector3(0f, BossConfig.BossY, 0f);
        SetFrame(BossArt.Idle0);
        body.enabled = false;
    }

    void OnDestroy() { ClearArmed(); }

    SpriteRenderer Glow(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // Smoke / arcs: same scale and registration as the body.
    SpriteRenderer Overlay(string name, int order)
    {
        var sr = Glow(name, order);
        sr.transform.localScale = Vector3.one * BossConfig.BossWorldSize;
        return sr;
    }

    void SetFrame(int frame)
    {
        BodyFrame = frame;
        if (body == null) return;
        int cell = BossArt.DamageIdleCell(boss, frame, damageStage, animClock);
        Sprite sprite = cell >= 0 ? BossArt.DamageBody(boss, cell) : null;
        // a tell / fire pose keeps the boss's damage too (BossArt.AttackDamageBody)
        if (sprite == null) sprite = BossArt.AttackDamageBody(boss, frame, damageStage);
        body.sprite = sprite != null ? sprite : BossArt.Body(boss, frame);
    }

    // ---- battle damage -------------------------------------------------

    void SetDamage(int heartsLeft)
    {
        damageStage = BossArt.DamageStage(Mathf.Max(1, BossConfig.Hearts), heartsLeft);
    }

    // Smoke and arcs on the world clock (they freeze with the fight); only
    // while it is fighting.
    void RefreshDamage()
    {
        if (smoke == null) return;
        bool alive = mode == Mode.Fighting && body.enabled;
        float sa = alive ? BossArt.SmokeAlpha(boss, damageStage) : 0f;
        float aa = alive ? BossArt.ArcAlpha(damageStage, animClock) : 0f;
        smoke.enabled = sa > 0f;
        arcs.enabled = aa > 0f;
        if (smoke.enabled)
        {
            smoke.sprite = BossArt.DamageFx(boss, BossArt.SmokeCell(animClock));
            smoke.color = new Color(1f, 1f, 1f, sa);
        }
        if (arcs.enabled)
        {
            arcs.sprite = BossArt.DamageFx(boss, BossArt.ArcCell(damageStage, animClock));
            arcs.color = new Color(1f, 1f, 1f, aa);
        }
    }

    void HideDamage()
    {
        if (smoke != null) smoke.enabled = false;
        if (arcs != null) arcs.enabled = false;
    }

    // ---- where attacks come from ---------------------------------------

    // A body part's muzzle, in world space, in the drawing `frame`.
    public Vector3 Emitter(int part, int frame)
    {
        Vector2 local = BossEmitters.Local(boss, part, frame);
        Vector3 p = transform.position;
        return new Vector3(p.x + local.x, p.y + local.y, 0f);
    }

    // The muzzle of `part` for a themed hazard of `attack`: in the drawing the attack's tell ends on (world space; `local`
    // is the same point relative to the boss, for a hazard that rides it).
    public Vector2 TellMuzzle(BossAttack attack, int part, out Vector2 local)
    {
        local = BossEmitters.Local(boss, part, BossArt.TellFrame(boss, attack.tell, 1f));
        Vector3 p = transform.position;
        return new Vector2(p.x + local.x, p.y + local.y);
    }

    // ... in the drawing on screen now.
    public Vector3 Emitter(int part) => Emitter(part, BodyFrame);

    // The drawing on screen the instant an attack fires: the hit flash wins,
    // then the drawn Fire pose (whose burst is at the attack's part), else
    // the attack's tell pose held.
    int FiringFrame
    {
        get
        {
            if (flashLeft > 0f) return BossArt.Hit;
            return BossArt.AttackFrame(boss, current);
        }
    }

    // ---- arrival (intro, real time) ------------------------------------
    //
    // The entrance (BossEntrance): the boss rises out of the backdrop at
    // `origin` (BossEmergence: a set piece's landing site, or a far point in
    // the sky) -- that small, hazed like the far scenery and drawn behind the
    // nearer backdrop layers (clouds, mist, dust pass over it) -- and grows
    // into the play field on a perspective curve (slow, then rushing in),
    // arcing up to its resting place. Its draw order steps from the site's
    // up through the backdrop to the play layer; at the end it lands
    // (BossConfig.LandAt: the shockwave), squashes, and idles until the
    // fight. Nothing of it can be hit until then (the hitbox is built in
    // BeginFight).

    // The legacy warp-in's start, fully above the visible top (7.6 on the
    // authored view; higher on a tall screen). Kept as the reference for
    // "off screen above" (ScreenFit / TallScreen tests).
    public static float ArrivalY
    {
        get { return Mathf.Max(7.6f, CameraFit.ViewTop + BossConfig.BossWorldSize * .5f + .3f); }
    }

    public const int PlayOrder = -3;          // the body's order in the fight (overlays -2 / -1)
    public const int ApproachOrder = -10;     // above every backdrop layer, under the play layer
    public const float BackdropShare = .5f;   // of the flight drawn at the site's own order
    public const float PlayShare = .85f;      // from here on it is in the play layer
    public const float LandOvershoot = .07f;  // the slam: a touch bigger just before it settles

    BossEmergence.Origin origin;
    Vector3 fromPos;

    public BossEmergence.Origin ArrivalOrigin => origin;
    public float ArrivalProgress01 => mode == Mode.Arriving ? Mathf.Clamp01(realClock / Mathf.Max(.05f, BossConfig.BossArriveSeconds)) : mode == Mode.Hidden ? 0f : 1f;
    public bool ArrivalLanded => mode == Mode.Arriving && realClock >= Mathf.Max(.05f, BossConfig.BossArriveSeconds);
    public int BodySortingOrder => body != null ? body.sortingOrder : PlayOrder;
    public Color BodyColor => body != null ? body.color : Color.white;
    public float ArrivalScale => transform.localScale.x;

    // ShipReach: how far above its resting ceiling the ship's ceiling is
    // while the boss flies in. Wide open while it is still in the backdrop,
    // closing as it comes into the play layer, so a ship parked high is eased
    // down by the arriving boss, not snapped.
    public float ArrivalCeilingLift
    {
        get
        {
            float k = ArrivalProgress01;
            float open = 1f - BossEntrance.Smooth((k - BackdropShare) / (1f - BackdropShare));
            return open * 30f + Mathf.Max(0f, transform.position.y - basePos.y);
        }
    }

    public void BeginArrival() { BeginArrival(BossEmergence.Pick()); }

    public void BeginArrival(BossEmergence.Origin from)
    {
        origin = from;
        fromPos = from.position;
        mode = Mode.Arriving;
        realClock = 0f;
        body.enabled = true;
        HideDamage();
        transform.position = new Vector3(fromPos.x, fromPos.y, 0f);
        PlaceArrival(0f);
        SetFrame(BossArt.Idle0);
        if (Application.isPlaying)
        {
            float size = from.scale * BossConfig.BossWorldSize;
            if (from.emerge) EliteSystem.Fx.DockFlare(fromPos, boss.flash, from.order, size);
            else EliteSystem.Fx.LiftOffDust(fromPos, from.order, size);
        }
    }

    public void StepArrival(float realDt)
    {
        if (mode != Mode.Arriving) return;
        realClock += realDt;
        // the site rides the backdrop's scroll: it rises from where the piece is now
        if (origin.hasSite && origin.site.Valid && ArrivalProgress01 < BackdropShare) fromPos = origin.site.Position;
        float dur = Mathf.Max(.05f, BossConfig.BossArriveSeconds);
        PlaceArrival(Mathf.Clamp01(realClock / dur));

        if (realClock < dur)
            SetFrame(BossArt.Idle0 + BossArt.FrameAt(BossArt.IdleTicks, realClock, true));
        else if (realClock - dur < BossArt.TellInTicks * BossArt.Tick * 2f)
            SetFrame(BossArt.Tell(0, 0)); // landing squash
        else
            SetFrame(BossArt.Idle0 + BossArt.FrameAt(BossArt.IdleTicks, realClock, true));
    }

    // Where and how it is drawn `k` (0..1) of the way through the flight.
    void PlaceArrival(float k)
    {
        float s0 = Mathf.Clamp(origin.scale > 0f ? origin.scale : BossEmergence.FallbackScale, .01f, 1f);
        // perspective: the scale's log runs on k^1.8 -- it hangs far off, then rushes in
        float grow = Mathf.Pow(k, 1.8f);
        float scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(s0), 0f, grow));
        if (k > PlayShare) scale *= 1f + LandOvershoot * Mathf.Sin(Mathf.PI * (k - PlayShare) / (1f - PlayShare));
        transform.localScale = Vector3.one * scale;

        // up off its pad, then an arc out to its resting place
        float u = BossEntrance.Smooth(k);
        Vector3 c = new Vector3(fromPos.x * .4f + basePos.x * .6f, Mathf.Max(fromPos.y, basePos.y) + .6f, 0f);
        Vector3 p = (1f - u) * (1f - u) * fromPos + 2f * u * (1f - u) * c + u * u * basePos;
        transform.position = new Vector3(p.x, p.y, 0f);

        // hazed like the scenery it comes out of, clearing as it comes close;
        // out of a hangar / a surface it fades in, in hard steps
        Color col = Color.Lerp(EliteShip.Haze, Color.white, BossEntrance.Smooth((k - .1f) / .75f));
        if (origin.emerge && k < .15f) col.a = k < .05f ? .34f : k < .1f ? .67f : 1f;
        body.color = col;
        body.sortingOrder = k < BackdropShare ? origin.order : k < PlayShare ? ApproachOrder : PlayOrder;
    }

    // ---- fight (world time) --------------------------------------------

    public void BeginFight()
    {
        mode = Mode.Fighting;
        transform.position = basePos;
        // whatever the entrance left: play size, play layer, full colour
        transform.localScale = Vector3.one;
        if (body != null) { body.sortingOrder = PlayOrder; body.color = Color.white; body.enabled = true; }
        moveClock = 0f;
        phase = AttackPhase.Cooldown;
        phaseTimer = BossConfig.FirstAttackDelay;
        attackIndex = -1;
        // Later loops: shorter cooldowns and one more pattern in rotation
        // from the start (LoopRules).
        loop = RunLoop.Index;
        EnsureBodyHitbox();
        if (markers == null) { markers = new BossPatternMarkers(); markers.Build(transform, boss); }
        markers.Clear();
        runner.Stop();
        musicRunning = false;
        musicDirector = new BossMusicDirector(World, boss, this);
        // its parts are every muzzle the song's waves leave from (BossWaveSources): the charge and flash slots
        musicAttack = new BossAttack { name = "song arrangement", kind = BossAttackKind.Pattern,
            parts = musicDirector.Parts, emitters = boss.DefaultAttacks[0].emitters,
            tellSeconds = .9f, fireFrame = true };
        // Its hearts pop in round it now it has landed.
        if (hearts == null) hearts = BossHearts.Attach(this, Mathf.Max(1, BossConfig.Hearts));
        SetDamage(hearts != null ? hearts.Left : BossConfig.Hearts);   // a fresh fight starts pristine
    }

    // BossEncounter: `left` hearts are left after a hit at `at`.
    public void SetHearts(int left, Vector3 at)
    {
        int before = hearts != null ? hearts.Left : left;
        if (hearts != null) hearts.SetLeft(left, at);
        SetDamage(left);
        // a blow that costs a heart (not the last one: BeginOutro plays the death cue) is heard on the boss's own material
        if (mode == Mode.Fighting && left < before && left > 0 && boss != null) EnemyDeathAudio.PlayBossHurt(boss.artKey);
        if (mode == Mode.Fighting) RefreshFrame();
    }

    int loop;
    public float CooldownScale => LoopRules.BossCooldownScale(loop);
    public float PatternHeadStart => LoopRules.BossHeadStart(loop);

    void EnsureBodyHitbox()
    {
        if (bodyHit != null) return;
        bodyHit = BossHitbox.Box(transform, "BossBody", BossConfig.BodyHitbox);
        bodyHit.AddComponent<BossTarget>();
        // Registered as a hazard target so every ship attack (rail, beam,
        // cone, orbit disc, ...) sees it, not just the screen-clear volley.
        ClearTarget.Ensure(bodyHit).SetRadius(Mathf.Max(BossConfig.BodyHitbox.x, BossConfig.BodyHitbox.y) * .5f);
    }

    public void StepFight(float dt, float realDt, Vector3 player, float progress01, BossProjectilePool pool)
    {
        if (mode != Mode.Fighting) return;
        BossBeat.Tick();
        TickFlash(realDt);
        if (dt <= 0f)
        {
            // Musical cues expire with the song even while gameplay is frozen.
            if (musicRunning && markers != null) markers.Step(realDt);
            RefreshFrame(); if (hearts != null) hearts.Step(realDt, 0f); return;
        }

        animClock += dt;
        // It all but holds still while it aims and while its lasers burn,
        // so a beam's root (and its sight line) doesn't skate about.
        bool steady = phase == AttackPhase.Tell || phase == AttackPhase.Beams || phase == AttackPhase.Hazards || phase == AttackPhase.Pattern;
        moveClock += dt * (steady ? .35f : 1f);
        float x = boss.swayX * Mathf.Sin(moveClock * boss.freqX * Mathf.PI * 2f);
        float y = BossConfig.BossY + boss.swayY * Mathf.Sin(moveClock * boss.freqY * Mathf.PI * 2f);
        transform.position = new Vector3(x, y, 0f);

        if (bodyHit == null)
        {
            bodyRespawn -= dt;
            if (bodyRespawn <= 0f) EnsureBodyHitbox();
        }
        else bodyRespawn = BossConfig.BodyRespawnSeconds;

        if (fireLeft > 0f) fireLeft -= dt;
        StepAttacks(dt, realDt, player, progress01, pool, BeatOverride ?? BossBeat.Source);
        if (markers != null) markers.Step(musicRunning ? realDt : dt);
        BossTelegraphStyle.LiveIntensity = BossTelegraphStyle.ForcedIntensity >= 0f ? BossTelegraphStyle.ForcedIntensity : musicRunning && musicDirector != null ? musicDirector.Intensity : BossTelegraphStyle.NonMusicIntensity;
        RefreshFrame();
        if (hearts != null) hearts.Step(realDt, dt);   // after the move: the ring is round where it is now
    }

    void StepAttacks(float dt, float realDt, Vector3 player, float progress01, BossProjectilePool pool, IBeatSource beatSource)
    {
        // All mapped boss songs use the continuous director. Timed rotations remain only
        // for missing music and explicit developer attack previews.
        if (beatSource is IMusicBeatSource && beatSource.Active && BossBeatTuning.For(World).enabled && ForcedAttack < 0)
        {
            if (!musicRunning)
            {
                runner.Stop(); ClearArmed(); markers.Clear();
                musicRunning = true; current = musicAttack; AttacksStarted++;
            }
            playerNow = player; poolNow = pool;
            musicDirector.Step(beatSource, dt, realDt, progress01);
            // the pose on screen is the one of the body part about to fire (its tell on the beat before), or of the
            // part that just fired while its fire drawing is up
            current.tell = fireLeft > 0f ? musicFirePose : musicDirector.TellPose;
            tellAge += dt;
            phase = musicDirector.Preparing ? AttackPhase.Tell : AttackPhase.Pattern;
            return;
        }
        if (musicRunning)
        {
            musicRunning = false; musicDirector.Suspend(); markers.Clear();
            phase = AttackPhase.Cooldown; phaseTimer = 0;
        }
        phaseTimer -= dt;
        switch (phase)
        {
            case AttackPhase.Cooldown:
                if (phaseTimer > 0f) return;
                int unlocked = Mathf.Max(1, BossCatalog.UnlockedAttacks(boss, progress01 + PatternHeadStart));
                attackIndex = ForcedAttack >= 0 && ForcedAttack < boss.attacks.Length
                    ? ForcedAttack : (attackIndex + 1) % unlocked;
                current = boss.attacks[attackIndex];
                phase = AttackPhase.Tell;
                phaseTimer = current.tellSeconds;
                tellAge = 0f;
                volleysFired = 0;
                firedMask = 0;
                AttacksStarted++;
                if (current.kind == BossAttackKind.Beam) phaseTimer = current.tellSeconds + SpawnBeams(player, pool);
                else if (current.kind == BossAttackKind.Pattern)
                {
                    playerNow = player;
                    poolNow = pool;
                    // With the boss song playing the pattern waits for a bar line / beat and fires on the grid; the
                    // telegraphs last until then (BossBeatTuning). Without it: the old timers, unchanged.
                    var tune = BossBeatTuning.For(World);
                    var src = tune.enabled ? beatSource : null;
                    float tell = current.tellSeconds;
                    alignBeat = -1.0;
                    alignHold = 0f;
                    if (src != null)
                    {
                        alignBeat = BeatAlign.Target(src, tell, tune.barWaitMax);
                        tell = Mathf.Max(tell, (float)((alignBeat - src.BeatNow) * src.SecondsPerBeat));
                    }
                    runner.Begin(current.pattern, this, BossLadder.RateAt(World, progress01), patternSeed++, tell, src, tune);
                    runner.BounceFraction = BossBounce.FractionFor(World);
                }
                else
                {
                    var exec = BossExecutors.For(current.kind);
                    if (exec != null) { ClearArmed(); armedCount = exec.Arm(this, current, player, armed); }
                }
                return;

            case AttackPhase.Tell:
                tellAge += dt;
                if (current.kind == BossAttackKind.Beam)
                {
                    if (tellAge >= current.tellSeconds) phase = AttackPhase.Beams;
                    return;
                }
                if (phaseTimer > 0f) return;
                if (current.kind == BossAttackKind.Pattern)
                {
                    // holding for the bar line: the boss keeps its tell pose until the music gets there (never longer
                    // than MaxAlignHold, and not at all once the music stops)
                    var clk = alignBeat >= 0.0 && beatSource != null && beatSource.Active ? beatSource : null;
                    if (clk != null && clk.BeatNow - alignBeat > realDt / clk.SecondsPerBeat + 1e-6)
                    {
                        // The song continued during a pause or slow motion. Wait for its next
                        // beat instead of launching at the expired tell target between beats.
                        alignBeat = BeatAlign.Target(clk, 0, 0f);
                        alignHold = 0f;
                    }
                    if (clk != null && clk.BeatNow < alignBeat && (alignHold += dt) < MaxAlignHold) return;
                    if (clk != null) runner.StartOnBeat(alignBeat);
                    alignBeat = -1.0;
                    phase = AttackPhase.Pattern;
                    Fire();
                    playerNow = player;
                    poolNow = pool;
                    runner.Step(0f, realDt); // emit on this frame's beat, not the next frame
                    return;
                }
                if (BossExecutors.IsHazardKind(current.kind))
                {
                    // the hazards ignite on their own tell; the boss holds its pose until the last one is over
                    phase = AttackPhase.Hazards;
                    hazardClock = 0f;
                    Fire();
                    return;
                }
                phase = AttackPhase.Volleys;
                volleysFired = 0;
                Volley(player, pool);
                phaseTimer = current.volleyGap;
                return;

            case AttackPhase.Volleys:
                if (phaseTimer > 0f) return;
                if (volleysFired < current.volleys)
                {
                    Volley(player, pool);
                    phaseTimer = current.volleyGap;
                    return;
                }
                EndAttack(progress01);
                return;

            case AttackPhase.Beams:
                if (phaseTimer <= 0f) EndAttack(progress01);
                return;

            case AttackPhase.Pattern:
                playerNow = player;
                poolNow = pool;
                if (runner.Step(dt, realDt)) { runner.Stop(); EndAttack(progress01); }
                return;

            case AttackPhase.Hazards:
                hazardClock += dt;
                if (HazardsBusy() && hazardClock < MaxHazardSeconds) return;
                ClearArmed();
                EndAttack(progress01);
                return;
        }
    }

    // Some hazard this attack armed has not ended yet (a taken-back pool item that someone else re-armed does not count).
    bool HazardsBusy()
    {
        for (int i = 0; i < armedCount; i++)
        {
            var h = armed[i];
            if (h != null && h.State != AttackHazard.Phase.Off && ReferenceEquals(h.Shooter, gameObject)) return true;
        }
        return false;
    }

    // Forgets (and, still in their tell or burning, ends) the hazards of the attack in progress.
    void ClearArmed()
    {
        for (int i = 0; i < armedCount; i++)
        {
            var h = armed[i];
            if (h != null && ReferenceEquals(h.Shooter, gameObject) && h.State != AttackHazard.Phase.Off) h.Cancel();
            armed[i] = null;
        }
        armedCount = 0;
    }

    void EndAttack(float progress01)
    {
        phase = AttackPhase.Cooldown;
        float scale = BossCatalog.FinalPhase(progress01) ? BossConfig.FinalPhaseCooldownScale : 1f;
        phaseTimer = current.cooldown * scale * CooldownScale;
    }

    // The fire pose (and the muzzle flashes) for a few ticks.
    void Fire()
    {
        fireLeft = BossArt.FireTicks * BossArt.Tick;
    }

    static Vector2 Heading(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    // Which side of the boss a part is on: -1 left, +1 right, 0 middle.
    int Side(int part, int frame)
    {
        float x = BossEmitters.Local(boss, part, frame).x;
        return x < -.15f ? -1 : x > .15f ? 1 : 0;
    }

    // ---- IPatternHost: what a bullet-hell pattern asks of the boss that plays it ----
    Vector2 IPatternHost.Emitter(int part) => (Vector2)Emitter(part);
    Vector2 IPatternHost.Player => playerNow;
    int IMusicPatternHost.ActiveMusicShots => poolNow != null ? poolNow.ActiveShots : 0;
    int IMusicPatternHost.MusicShotCapacity => poolNow != null ? poolNow.Capacity : BossConfig.ProjectilePoolMax;
    float IPatternHost.ShotSpeedScale => BossConfig.ShotScale;
    float IPatternHost.RailEdge => BossRails.InnerEdge;
    float IPatternHost.ShipHalfWidth => ShipReach.HalfWidth;
    float IPatternHost.SpawnTop => CameraFit.ViewTop + .15f;
    float IPatternHost.ZoneBottom => ShipReach.BottomFor(PlayField.Live);
    float IPatternHost.ZoneTop => Mathf.Min(ShipReach.TopFor(PlayField.Live), BossConfig.ShipCeilingFor(PlayField.Live, boss));

    // Shots that started outside the view (tests read it; it must stay 0: every shot leaves a muzzle on the boss).
    public static int OffscreenSpawns;

    void IPatternHost.Fire(Vector2 at, Vector2 velocity, ref PShot shot)
    {
        if (poolNow == null) return;
        // a boss shot starts on the boss, never off the screen (a muzzle above the view would be a bug in the table
        // or the boss's rest height): counted, and pulled back to the view's edge so it is at least seen leaving
        float top = CameraFit.ViewTop - .05f;
        if (at.y > top || Mathf.Abs(at.x) > CameraFit.GameplayHalfWidth)
        {
            if (OffscreenSpawns++ == 0) Debug.LogWarning("[BOSS] shot spawned outside the view at " + at + " (BossWaveTest asserts this never happens)");
            at = new Vector2(Mathf.Clamp(at.x, -CameraFit.GameplayHalfWidth, CameraFit.GameplayHalfWidth), Mathf.Min(at.y, top));
        }
        // straight shots reach the ship's rows in the same time on every screen (BossConfig.ShotScale); a bomb's flight is
        // already worked out to its spot, and a mine stands still
        var then = shot.then;
        if (!(shot.fuse > 0f)) { velocity *= BossConfig.ShotScale; shot.then *= BossConfig.ShotScale; }
        poolNow.FireShot(boss, new Vector3(at.x, at.y, 0f), velocity, ref shot);
        shot.then = then;
    }

    // One part fired (the musical director's waves): its muzzle flashes, the fire drawing of its pose is held for at
    // least `holdSeconds` (the Drake's breath is a stream), and the slot of every other part goes dark.
    void IPatternHost.EmittedFrom(int part, float holdSeconds)
    {
        // the first part to fire in a frame names the drawing (a wave before an accent on the same sixteenth); a new
        // firing ends the last one's flashes and held drawing
        if (fireStamp != animClock) { fireStamp = animClock; musicFirePose = BossWaveSources.PoseOf(boss, part); fireLeft = 0f; firedMask = 0; }
        fireLeft = Mathf.Max(fireLeft, Mathf.Max(BossArt.FireTicks * BossArt.Tick, holdSeconds));
        var parts = current != null ? current.parts : null;
        if (parts == null) return;
        for (int i = 0; i < parts.Length && i < MaxParts; i++) if (parts[i] == part) firedMask |= 1 << i;
    }

    // Test reads: the slots (indices into CurrentAttack.parts) flashing / charging now.
    public int FiredMask => fireLeft > 0f ? firedMask : 0;
    public float FireLeft => fireLeft;

    void IPatternHost.Emitted()
    {
        Fire();
        int n = current != null && current.parts != null ? Mathf.Min(MaxParts, current.parts.Length) : 0;
        firedMask = n >= 31 ? -1 : (1 << n) - 1;
    }

    void IPatternHost.Mark(Vector2 a, Vector2 b, float halfWidth, float seconds)
    {
        if (markers == null) return;
        // only a MAJOR attack gets a lane / line / ring overlay; ordinary bullet waves are read from their muzzles
        if (!BossTelegraphStyle.OverlayAllowed(current)) { markers.Suppressed++; return; }
        markers.Intensity = musicRunning && musicDirector != null ? musicDirector.Intensity : BossTelegraphStyle.NonMusicIntensity;
        markers.Add(a, b, halfWidth, seconds);
    }

    void Volley(Vector3 player, BossProjectilePool pool)
    {
        playerNow = player;
        volleysFired++;
        Fire();
        int frame = FiringFrame;
        var parts = current.parts;
        int n = parts == null ? 0 : parts.Length;
        firedMask = 0;
        if (n == 0) return;
        int first = 0, last = n;
        if (current.alternate) { first = (volleysFired - 1) % n; last = first + 1; }
        for (int k = first; k < last; k++)
        {
            int part = parts[k];
            if (part < 0) continue;
            firedMask |= 1 << k;
            if (pool == null) continue;
            Vector3 from = Emitter(part, frame);
            switch (current.kind)
            {
                case BossAttackKind.Aimed: Aimed(pool, from, player); break;
                case BossAttackKind.Fan: Fan(pool, from, part, frame); break;
                case BossAttackKind.Lob: Lob(pool, from); break;
            }
        }
    }

    void Shoot(BossProjectilePool pool, Vector3 from, Vector2 v, float gravity = 0f, float fall = 0f)
    {
        BossConfig.FitShot(ref v, ref gravity, ref fall);   // the same time to the ship on every screen
        if (gravity <= 0f && current.rail == BossRailMode.Absorb && RicochetPick(from, v)) { pool.FireRicochet(boss, current.style, from, v); return; }
        pool.Fire(boss, current.style, from, v, current.rail, current.bounces, gravity, fall);
    }

    // The timed volleys' ricochets (BossBounce): the same pick and the same rule as BossPatternRunner's timed streams --
    // a straight shot that meets a rail, picked by the running fraction, its ricochet's sweep through the ship's band
    // kept BossPatternRunner.TimedClear u clear of the ship.
    BounceSelector volleyBounce;
    public BounceSelector VolleyBounce => volleyBounce;

    bool RicochetPick(Vector2 from, Vector2 v)
    {
        var host = (IPatternHost)this;
        float r = current.style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
        float edge = BossRails.InnerEdge;
        if (!BossBounce.MeetsRail(from, v, r, edge, host.ZoneBottom - 1f)) return false;
        if (!volleyBounce.Offer(BossBounce.FractionFor(World))) return false;
        float R = PatternSim.ShipRadius + r + BossBounce.ShadowMargin;
        if (BossBounce.Trace(from, v, r, edge, host.ZoneBottom - R, host.ZoneTop + R, out _, out float t1, out float x0, out float x1))
        {
            float pad = BossBounce.ShadowPad(r, t1), px = playerNow.x;
            if (x1 + pad > px - BossPatternRunner.TimedClear && x0 - pad < px + BossPatternRunner.TimedClear) { volleyBounce.Veto(); return false; }
        }
        volleyBounce.Take();
        return true;
    }

    void Aimed(BossProjectilePool pool, Vector3 from, Vector3 player)
    {
        int n = Mathf.Max(1, current.count);
        Vector2 to = (Vector2)(player - from);
        float aim = to.sqrMagnitude < .0001f ? -90f : Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
        for (int i = 0; i < n; i++)
        {
            float off = n == 1 ? 0f : -current.spreadDeg * .5f + current.spreadDeg * i / (n - 1);
            Shoot(pool, from, Heading(aim + off) * current.speed);
        }
    }

    // An even fan around the part's heading: straight down, or (radial)
    // out along the line from the radial part through this one, bent down.
    void Fan(BossProjectilePool pool, Vector3 from, int part, int frame)
    {
        int n = Mathf.Max(1, current.count);
        float baseDeg = -90f;
        if (current.radialPart >= 0)
            baseDeg = RadialHeading(BossEmitters.Local(boss, current.radialPart, frame),
                                    BossEmitters.Local(boss, part, frame), current.radialBend);
        float rotate = current.rotateDeg * ((volleysFired % 2 == 0) ? 1f : -1f);
        for (int i = 0; i < n; i++)
        {
            float off = n == 1 ? 0f : -current.spreadDeg * .5f + current.spreadDeg * i / (n - 1);
            Shoot(pool, from, Heading(baseDeg + off + rotate) * current.speed);
        }
    }

    // The heading out from `centre` through `tip`, its swing away from
    // straight down scaled by `bend` (so upper petals fling sideways, never
    // up off the screen).
    public static float RadialHeading(Vector2 centre, Vector2 tip, float bend)
    {
        Vector2 d = tip - centre;
        float deg = d.sqrMagnitude < 1e-6f ? -90f : Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float fromDown = Mathf.DeltaAngle(-90f, deg);
        return -90f + fromDown * Mathf.Clamp01(bend);
    }

    // Lobbed: up out of the part, then falling onto `count` of the columns
    // (never all of them: one is always clear, a different one each volley).
    void Lob(BossProjectilePool pool, Vector3 from)
    {
        int slots = Mathf.Clamp(BossConfig.LaneSlots, 2, laneOrder.Length);
        int hits = Mathf.Clamp(current.count, 1, slots - 1);
        for (int i = 0; i < slots; i++) laneOrder[i] = i;
        for (int i = 0; i < slots - 1; i++)
        {
            int j = Random.Range(i, slots);
            int t = laneOrder[i]; laneOrder[i] = laneOrder[j]; laneOrder[j] = t;
        }
        float s = BossConfig.ShotScale;   // Shoot stretches the lob by this (BossConfig.FitShot)
        float t2 = LobFlightTime(from.y, BossConfig.LobTargetY, current.lobUp * s, current.gravity * s, current.fallSpeed * s);
        for (int k = 0; k < hits; k++)
        {
            float x = LaneX(laneOrder[k]) + Random.Range(-BossConfig.LobJitter, BossConfig.LobJitter);
            float vx = (x - from.x) / Mathf.Max(.1f, t2);
            Shoot(pool, from, new Vector2(vx, current.lobUp), current.gravity, current.fallSpeed);
        }
    }

    // Seconds for a shot thrown up at `up` (gravity `g`, falling no faster
    // than `fall`) to get from y0 down to y1.
    public static float LobFlightTime(float y0, float y1, float up, float g, float fall)
    {
        g = Mathf.Max(.01f, g);
        fall = Mathf.Max(.01f, fall);
        float t1 = (up + fall) / g;                      // until it reaches its fall speed
        float yAt = y0 + up * t1 - .5f * g * t1 * t1;
        if (yAt <= y1)
        {
            // reaches y1 still accelerating: y0 + up t - g t^2 / 2 = y1
            float disc = up * up + 2f * g * (y0 - y1);
            return (up + Mathf.Sqrt(Mathf.Max(0f, disc))) / g;
        }
        return t1 + (yAt - y1) / fall;
    }

    public static float LaneX(int slot)
    {
        int slots = Mathf.Max(2, BossConfig.LaneSlots);
        float w = BossConfig.LaneHalfWidth * 2f / slots;
        return -BossConfig.LaneHalfWidth + w * (slot + .5f);
    }

    public static float LaneWidth => BossConfig.LaneHalfWidth * 2f / Mathf.Max(2, BossConfig.LaneSlots);

    // Starts every laser of the attack (their sight lines run through the
    // tell) and returns how long after the tell the last one is gone.
    float SpawnBeams(Vector3 player, BossProjectilePool pool)
    {
        var parts = current.parts;
        int n = parts == null ? 0 : parts.Length;
        int frame = BossArt.Tell(current.tell, 1);
        for (int k = 0; k < n; k++)
        {
            int part = parts[k];
            if (part < 0) continue;
            Vector3 from = Emitter(part, frame);
            int side = Side(part, frame);
            float start, sweepSign;
            if (current.aim == BossBeamAim.AtShip)
            {
                Vector2 to = (Vector2)(player - from);
                start = to.sqrMagnitude < .0001f ? -90f : Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
                // A part on a side sweeps outward; a middle one rakes away
                // from the ship's side of the arena, across to the other.
                sweepSign = side != 0 ? side : (player.x >= 0f ? -1f : 1f);
            }
            else
            {
                start = -90f + (side == 0 ? 0f : side) * current.aimDeg;
                sweepSign = side == 0 ? 1f : side;
            }
            // never up or flat: lasers burn down the screen
            start = Mathf.Clamp(start, -170f, -10f);
            if (pool != null)
                pool.Beam(boss, this, part, from, start, sweepSign * current.sweepDeg,
                          current.tellSeconds, current.hold, current.beamWidth);
        }
        return current.hold + BossConfig.BeamFadeSeconds;
    }

    void RefreshFrame()
    {
        // a damaged boss keeps its pose through a hit (the white flash is the hit now): the
        // pristine Hit drawing would read as it healing for two ticks per blow
        if (flashLeft > 0f && damageStage <= 0) SetFrame(BossArt.Hit);
        else if (musicRunning && current != null)
        {
            // the song's waves: the source's tell drawings on the beat before (the first two: the third of an expanded
            // atlas is its fire drawing), the fire drawing the instant the bullets leave (held for a breath), idle between
            if (fireLeft > 0f) SetFrame(BossArt.FireFrame(boss, musicFirePose));
            else if (musicDirector.Telling) SetFrame(BossArt.TellFrame(boss, musicDirector.TellPose, musicDirector.TellProgress * .66f));
            else SetFrame(BossArt.IdleFrame(boss, animClock));
        }
        else if (fireLeft > 0f && current != null && current.fireFrame && (phase == AttackPhase.Volleys || phase == AttackPhase.Pattern)) SetFrame(BossArt.FireFrame(boss, current.tell));
        else if (phase == AttackPhase.Tell && current != null)
            SetFrame(BossArt.TellFrame(boss, current.tell, tellAge / Mathf.Max(.01f, current.tellSeconds)));
        else if ((phase == AttackPhase.Volleys || phase == AttackPhase.Beams || phase == AttackPhase.Hazards || phase == AttackPhase.Pattern) && current != null)
            SetFrame(BossArt.TellFrame(boss, current.tell, 1f));
        else SetFrame(BossArt.IdleFrame(boss, animClock));
        RefreshGlows();
        RefreshDamage();
        RefreshWhite();
    }

    void RefreshWhite()
    {
        if (white == null) return;
        bool on = whiteLeft > 0f && body.enabled && mode == Mode.Fighting;
        white.enabled = on;
        if (!on) return;
        white.sprite = body.sprite;
        white.color = new Color(1f, 1f, 1f, whitePeak * Mathf.Clamp01(whiteLeft / BossImpact.WhiteSeconds));
    }

    // Test / codex reads.
    public float WhiteAlpha => white != null && white.enabled ? white.color.a : 0f;

    // The tell's charge gathering at each part about to fire (a ring closing
    // in, a core growing on 2s), and the muzzle flash at each part that just
    // fired -- placed on the drawing now on screen.
    void RefreshGlows()
    {
        bool fighting = mode == Mode.Fighting && current != null && current.parts != null;
        bool music = fighting && musicRunning && musicDirector != null;
        bool flashing = fighting && (phase == AttackPhase.Volleys || phase == AttackPhase.Pattern) && fireLeft > 0f;
        bool telling = fighting && (music ? musicDirector.Telling && !flashing : phase == AttackPhase.Tell);
        int n = fighting ? Mathf.Min(MaxParts, current.parts.Length) : 0;
        float k = !telling ? 0f : music ? musicDirector.TellProgress : Mathf.Clamp01(tellAge / Mathf.Max(.05f, current.tellSeconds));
        int chargeMask = music ? musicDirector.TellMask(current.parts) : -1;
        int step = Mathf.FloorToInt(tellAge / (2f * BossArt.Tick));
        for (int i = 0; i < MaxParts; i++)
        {
            var c = charges[i];
            var r = rings[i];
            bool on = i < n && current.parts[i] >= 0 && ((telling && (chargeMask & (1 << i)) != 0) || (flashing && (firedMask & (1 << i)) != 0));
            c.enabled = on;
            r.enabled = on && telling;
            if (!on) continue;
            float inten = music ? musicDirector.Intensity : BossTelegraphStyle.NonMusicIntensity;
            float glow = BossTelegraphStyle.MuzzleAlpha(inten), glowSize = BossTelegraphStyle.MuzzleSize(inten);
            c.color = new Color(1f, 1f, 1f, glow);
            r.color = new Color(1f, 1f, 1f, glow);
            Vector2 local = BossEmitters.Local(boss, current.parts[i], BodyFrame);
            var at = new Vector3(local.x, local.y, 0f);
            c.transform.localPosition = at;
            if (telling)
            {
                float size = Mathf.Lerp(BossConfig.ChargeMinSize, BossConfig.ChargeMaxSize, Mathf.Floor(k * 4f) / 4f) *
                             (step % 2 == 0 ? 1f : .86f);
                c.sprite = BossArt.Shot(boss, BossArt.Charge);
                c.transform.localScale = Vector3.one * size * glowSize;
                // the ring closes in on the part in hard steps
                r.sprite = BossAttackFx.Get(boss, BossAttackFx.Ring);
                r.transform.localPosition = at;
                r.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, .45f, Mathf.Floor(k * 3f) / 3f);
            }
            else
            {
                int f = fireLeft > BossArt.FireTicks * BossArt.Tick * .5f ? 0 : 1;
                c.sprite = BossAttackFx.Get(boss, BossAttackFx.Flash0 + f);
                c.transform.localScale = Vector3.one * BossConfig.MuzzleFlashSize * glowSize;
            }
        }
    }

    // ---- hit flash (real time) -----------------------------------------

    public void Flash() { Flash(1f); }

    // `strength` 0..1 scales the white flash (a light hit is a dimmer pop).
    public void Flash(float strength)
    {
        flashLeft = BossArt.HitTicks * BossArt.Tick;
        whiteLeft = BossImpact.WhiteSeconds;
        whitePeak = Mathf.Clamp01(strength);
        if (mode == Mode.Fighting) RefreshFrame();
    }

    public bool Flashing => flashLeft > 0f;

    void TickFlash(float realDt)
    {
        if (flashLeft > 0f) flashLeft -= realDt;
        if (whiteLeft > 0f) whiteLeft -= realDt;
    }

    // ---- outro (world time) --------------------------------------------

    public void BeginOutro(bool explode)
    {
        if (markers != null) markers.Clear();
        runner.Stop();
        musicDirector?.Suspend();
        mode = explode ? Mode.Dying : Mode.Retreating;
        outroClock = 0f;
        explosionsFired = 0;
        retreatVy = 0f;
        hasDeathArt = explode && BossArt.HasDeathArt(boss);
        if (explode && boss != null) EnemyDeathAudio.PlayBossDeath(boss.artKey);
        if (hasDeathArt && body != null) body.sprite = BossArt.DeathStrip(boss, 0);
        for (int i = 0; i < MaxParts; i++) { charges[i].enabled = false; rings[i].enabled = false; }
        ClearArmed();   // a hazard of the attack in progress goes with the boss
        HideDamage();
        flashLeft = 0f;
        whiteLeft = 0f;
        if (white != null) white.enabled = false;
        if (bodyHit != null) { BossUtil.Kill(bodyHit); bodyHit = null; }
        // Retreating: its hearts go with it at once. Dying: the last one's
        // crumble plays out over the blasts (StepOutro), then all of it goes.
        if (hearts != null && !explode) hearts.Hide();
    }

    public void StepOutro(float dt, int ship)
    {
        if (mode != Mode.Dying && mode != Mode.Retreating) return;
        if (dt <= 0f) return;
        outroClock += dt;
        if (hearts != null && mode == Mode.Dying) hearts.Step(dt, dt);

        if (mode == Mode.Dying)
        {
            int f = BossArt.FrameAt(BossArt.DeathTicks, outroClock, false);
            // A death strip outlasts the atlas frames if it must, never cuts them short.
            if (hasDeathArt && outroClock < BossArt.DeathStripSeconds) f = Mathf.Min(f, BossArt.DeathFrames - 1);
            if (f >= BossArt.DeathFrames) { body.enabled = false; mode = Mode.Gone; if (hearts != null) hearts.Hide(); return; }
            SetFrame(BossArt.Death(f));
            if (hasDeathArt) body.sprite = BossArt.DeathStrip(boss, BossArt.DeathStripCell(outroClock));
            // Cartoon blasts on the hull as it breaks up.
            var at = BlastTimes;
            while (explosionsFired < at.Length && outroClock >= at[explosionsFired])
            {
                Vector3 off = explosionsFired == 0 ? Vector3.zero
                    : new Vector3((explosionsFired % 2 == 0 ? .75f : -.8f), .25f - explosionsFired * .12f, 0f);
                TargetExplosion.Spawn(transform.position + off, ExplosionKind, TargetExplosion.Size.Large, ship);
                if (explosionsFired == 0) collisionDetection.PlayExplosion();
                explosionsFired++;
            }
            return;
        }

        // Retreat: a held dip (anticipation), then it warps up and away.
        if (outroClock < 4 * BossArt.Tick)
        {
            SetFrame(BossArt.Tell(0, 0));
            transform.position += Vector3.down * .5f * dt;
            return;
        }
        retreatVy = Mathf.Min(16f, retreatVy + 40f * dt);
        transform.position += Vector3.up * retreatVy * dt;
        SetFrame(BossArt.Retreat0 + BossArt.FrameAt(BossArt.RetreatTicks, outroClock, true));
        if (transform.position.y > Mathf.Max(8f, CameraFit.ViewTop + BossConfig.BossWorldSize * .5f + .3f))
        { body.enabled = false; mode = Mode.Gone; }
    }

    static readonly float[] BlastTimes = { 0f, .12f, .26f, .4f };

    // the boss bursts in its world's material (Space metal, Frost ice, Verdant spore, Ember magma)
    public TargetExplosion.Kind ExplosionKind => TargetExplosion.KindForWorld(boss.artKey);
}

// Marks the boss's body hitbox as the target of the ship's attacks. It is a
// registered hazard (ClearTarget), so the screen-clear volley and every
// directional attack find it; all of them hit it through ShipAttackHits,
// which hands the hit here instead of destroying it.
//
// Weighting (IShipAttackTarget): one firing of any attack is worth at most
// one full ultimate hit, split over its contacts -- a rail slug, a fireball
// blast or one homing shot of the volley is 1; a gatling shell 1/14, a beam
// or cone tick 0.2, an orbit-disc pass 0.25, a seeker eye 1/3. So weak,
// spread-out hits shave proportionally less of the fight than big ones.
public class BossTarget : MonoBehaviour, IShipAttackTarget
{
    // The screen-clear volley's own entry point: one full hit.
    public static bool Intercept(GameObject target, int ship)
    {
        var boss = target != null ? target.GetComponent<BossTarget>() : null;
        if (boss == null) return false;
        boss.TakeShipAttack(ship, 1f, target.transform.position);
        return true;
    }

    public void TakeShipAttack(int ship, float weight, Vector3 at)
    {
        var encounter = BossEncounter.Instance;
        var actor = GetComponentInParent<BossActor>();
        TargetExplosion.Spawn(transform.position + Vector3.down * .35f,
                              actor != null ? actor.ExplosionKind : TargetExplosion.Kind.Metal,
                              weight >= .5f ? TargetExplosion.Size.Medium : TargetExplosion.Size.Small, ship);
        collisionDetection.PlayExplosion();
        if (encounter != null) encounter.OnShipAttackHit(weight, at);
    }
}

public static class BossUtil
{
    // Destroy that also works from edit-mode tests.
    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
