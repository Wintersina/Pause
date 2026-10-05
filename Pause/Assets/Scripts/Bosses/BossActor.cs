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
public class BossActor : MonoBehaviour
{
    public enum Mode { Hidden, Arriving, Fighting, Dying, Retreating, Gone }
    enum AttackPhase { Cooldown, Tell, Volleys, Beams }

    // The most parts one attack fires from (the Bloom Queen's six petals).
    public const int MaxParts = 8;

    BossDef boss;
    SpriteRenderer body;
    readonly SpriteRenderer[] charges = new SpriteRenderer[MaxParts];
    readonly SpriteRenderer[] rings = new SpriteRenderer[MaxParts];
    GameObject bodyHit;
    Mode mode = Mode.Hidden;

    // animation
    float animClock;       // world time, idle loop
    float realClock;       // unscaled, arrival
    float flashLeft;       // unscaled
    float fireLeft;        // world time: fire pose / muzzle flash hold
    float tellAge;         // world time inside the current tell
    float outroClock;
    int explosionsFired;

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

    public BossDef Boss => boss;
    public Mode State => mode;
    public BossAttack CurrentAttack => phase == AttackPhase.Cooldown ? null : current;
    public bool Telegraphing => phase == AttackPhase.Tell;
    public GameObject BodyHitbox => bodyHit;
    public int BodyFrame { get; private set; }
    public int AttacksStarted { get; private set; }
    public int VolleysFired => volleysFired;

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

    SpriteRenderer Glow(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void SetFrame(int frame)
    {
        BodyFrame = frame;
        if (body != null) body.sprite = BossArt.Body(boss, frame);
    }

    // ---- where attacks come from ---------------------------------------

    // A body part's muzzle, in world space, in the drawing `frame`.
    public Vector3 Emitter(int part, int frame)
    {
        Vector2 local = BossEmitters.Local(boss, part, frame);
        Vector3 p = transform.position;
        return new Vector3(p.x + local.x, p.y + local.y, 0f);
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
            return current != null && current.fireFrame ? BossArt.Fire : BossArt.Tell(current != null ? current.tell : 0, 1);
        }
    }

    // ---- arrival (intro, real time) ------------------------------------

    // Where the warp-in starts: fully above the visible top (7.6 on the
    // authored view; higher on a tall screen, whose view reaches further up).
    public static float ArrivalY
    {
        get { return Mathf.Max(7.6f, CameraFit.ViewTop + BossConfig.BossWorldSize * .5f + .3f); }
    }

    float arrivalY = 7.6f;

    public void BeginArrival()
    {
        arrivalY = ArrivalY;
        mode = Mode.Arriving;
        realClock = 0f;
        body.enabled = true;
    }

    public void StepArrival(float realDt)
    {
        if (mode != Mode.Arriving) return;
        realClock += realDt;
        float dur = Mathf.Max(.05f, BossConfig.BossArriveSeconds);
        float t = Mathf.Clamp01(realClock / dur);
        // ease-out-back: overshoots past its mark, then settles -- the warp
        // lands hard rather than gliding in.
        const float s = 1.6f;
        float u = t - 1f;
        float e = 1f + (s + 1f) * u * u * u + s * u * u;
        transform.position = Vector3.LerpUnclamped(new Vector3(0f, arrivalY, 0f), basePos, e);

        if (t < .55f)
            SetFrame(BossArt.Retreat0 + BossArt.FrameAt(BossArt.RetreatTicks, realClock, true)); // warp smear
        else if (realClock - dur * .55f < BossArt.TellInTicks * BossArt.Tick * 2f)
            SetFrame(BossArt.Tell(0, 0)); // landing squash
        else
            SetFrame(BossArt.Idle0 + BossArt.FrameAt(BossArt.IdleTicks, realClock, true));
    }

    // ---- fight (world time) --------------------------------------------

    public void BeginFight()
    {
        mode = Mode.Fighting;
        transform.position = basePos;
        moveClock = 0f;
        phase = AttackPhase.Cooldown;
        phaseTimer = BossConfig.FirstAttackDelay;
        attackIndex = -1;
        // Later loops: shorter cooldowns and one more pattern in rotation
        // from the start (LoopRules).
        loop = RunLoop.Index;
        EnsureBodyHitbox();
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
        TickFlash(realDt);
        if (dt <= 0f) { RefreshFrame(); return; }

        animClock += dt;
        // It all but holds still while it aims and while its lasers burn,
        // so a beam's root (and its sight line) doesn't skate about.
        bool steady = phase == AttackPhase.Tell || phase == AttackPhase.Beams;
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
        StepAttacks(dt, player, progress01, pool);
        RefreshFrame();
    }

    void StepAttacks(float dt, Vector3 player, float progress01, BossProjectilePool pool)
    {
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
                return;

            case AttackPhase.Tell:
                tellAge += dt;
                if (current.kind == BossAttackKind.Beam)
                {
                    if (tellAge >= current.tellSeconds) phase = AttackPhase.Beams;
                    return;
                }
                if (phaseTimer > 0f) return;
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
        }
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

    void Volley(Vector3 player, BossProjectilePool pool)
    {
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
        pool.Fire(boss, current.style, from, v, current.rail, current.bounces, gravity, fall);
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
        float t2 = LobFlightTime(from.y, BossConfig.LobTargetY, current.lobUp, current.gravity, current.fallSpeed);
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
        if (flashLeft > 0f) SetFrame(BossArt.Hit);
        else if (fireLeft > 0f && current != null && current.fireFrame && phase == AttackPhase.Volleys) SetFrame(BossArt.Fire);
        else if (phase == AttackPhase.Tell && current != null)
            SetFrame(BossArt.Tell(current.tell, tellAge < BossArt.TellInTicks * BossArt.Tick ? 0 : 1));
        else if ((phase == AttackPhase.Volleys || phase == AttackPhase.Beams) && current != null)
            SetFrame(BossArt.Tell(current.tell, 1));
        else SetFrame(BossArt.Idle0 + BossArt.FrameAt(BossArt.IdleTicks, animClock, true));
        RefreshGlows();
    }

    // The tell's charge gathering at each part about to fire (a ring closing
    // in, a core growing on 2s), and the muzzle flash at each part that just
    // fired -- placed on the drawing now on screen.
    void RefreshGlows()
    {
        bool fighting = mode == Mode.Fighting && current != null && current.parts != null;
        bool telling = fighting && phase == AttackPhase.Tell;
        bool flashing = fighting && phase == AttackPhase.Volleys && fireLeft > 0f;
        int n = fighting ? Mathf.Min(MaxParts, current.parts.Length) : 0;
        float k = telling ? Mathf.Clamp01(tellAge / Mathf.Max(.05f, current.tellSeconds)) : 0f;
        int step = Mathf.FloorToInt(tellAge / (2f * BossArt.Tick));
        for (int i = 0; i < MaxParts; i++)
        {
            var c = charges[i];
            var r = rings[i];
            bool on = i < n && current.parts[i] >= 0 && (telling || (flashing && (firedMask & (1 << i)) != 0));
            c.enabled = on;
            r.enabled = on && telling;
            if (!on) continue;
            Vector2 local = BossEmitters.Local(boss, current.parts[i], BodyFrame);
            var at = new Vector3(local.x, local.y, 0f);
            c.transform.localPosition = at;
            if (telling)
            {
                float size = Mathf.Lerp(BossConfig.ChargeMinSize, BossConfig.ChargeMaxSize, Mathf.Floor(k * 4f) / 4f) *
                             (step % 2 == 0 ? 1f : .86f);
                c.sprite = BossArt.Shot(boss, BossArt.Charge);
                c.transform.localScale = Vector3.one * size;
                // the ring closes in on the part in hard steps
                r.sprite = BossAttackFx.Get(boss, BossAttackFx.Ring);
                r.transform.localPosition = at;
                r.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, .45f, Mathf.Floor(k * 3f) / 3f);
            }
            else
            {
                int f = fireLeft > BossArt.FireTicks * BossArt.Tick * .5f ? 0 : 1;
                c.sprite = BossAttackFx.Get(boss, BossAttackFx.Flash0 + f);
                c.transform.localScale = Vector3.one * BossConfig.MuzzleFlashSize;
            }
        }
    }

    // ---- hit flash (real time) -----------------------------------------

    public void Flash()
    {
        flashLeft = BossArt.HitTicks * BossArt.Tick;
        if (mode == Mode.Fighting) RefreshFrame();
    }

    public bool Flashing => flashLeft > 0f;

    void TickFlash(float realDt)
    {
        if (flashLeft > 0f) flashLeft -= realDt;
    }

    // ---- outro (world time) --------------------------------------------

    public void BeginOutro(bool explode)
    {
        mode = explode ? Mode.Dying : Mode.Retreating;
        outroClock = 0f;
        explosionsFired = 0;
        retreatVy = 0f;
        for (int i = 0; i < MaxParts; i++) { charges[i].enabled = false; rings[i].enabled = false; }
        flashLeft = 0f;
        if (bodyHit != null) { BossUtil.Kill(bodyHit); bodyHit = null; }
    }

    public void StepOutro(float dt, int ship)
    {
        if (mode != Mode.Dying && mode != Mode.Retreating) return;
        if (dt <= 0f) return;
        outroClock += dt;

        if (mode == Mode.Dying)
        {
            int f = BossArt.FrameAt(BossArt.DeathTicks, outroClock, false);
            if (f >= BossArt.DeathFrames) { body.enabled = false; mode = Mode.Gone; return; }
            SetFrame(BossArt.Death(f));
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

    TargetExplosion.Kind ExplosionKind => boss.artKey == "Ember" ? TargetExplosion.Kind.Rock : TargetExplosion.Kind.Metal;
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
        TargetExplosion.Spawn(transform.position + Vector3.down * .35f, TargetExplosion.Kind.Metal,
                              weight >= .5f ? TargetExplosion.Size.Medium : TargetExplosion.Size.Small, ship);
        collisionDetection.PlayExplosion();
        if (encounter != null) encounter.OnShipAttackHit(weight);
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
