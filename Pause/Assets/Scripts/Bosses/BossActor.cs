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
public class BossActor : MonoBehaviour
{
    public enum Mode { Hidden, Arriving, Fighting, Dying, Retreating, Gone }
    enum AttackPhase { Cooldown, Tell, Volleys, Lanes }

    BossDef boss;
    SpriteRenderer body, charge;
    GameObject bodyHit;
    Mode mode = Mode.Hidden;

    // animation
    float animClock;       // world time, idle loop
    float realClock;       // unscaled, arrival
    float flashLeft;       // unscaled
    float fireLeft;        // world time: fire pose hold
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
    float bodyRespawn;
    readonly int[] laneOrder = new int[16];

    public BossDef Boss => boss;
    public Mode State => mode;
    public BossAttack CurrentAttack => phase == AttackPhase.Cooldown ? null : current;
    public bool Telegraphing => phase == AttackPhase.Tell;
    public GameObject BodyHitbox => bodyHit;
    public int BodyFrame { get; private set; }
    public int AttacksStarted { get; private set; }

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

        var chargeGo = new GameObject("Charge");
        chargeGo.transform.SetParent(transform, false);
        charge = chargeGo.AddComponent<SpriteRenderer>();
        charge.sortingOrder = 29;
        charge.enabled = false;

        transform.position = new Vector3(0f, 7.6f, 0f);
        basePos = new Vector3(0f, BossConfig.BossY, 0f);
        SetFrame(BossArt.Idle0);
        body.enabled = false;
    }

    void SetFrame(int frame)
    {
        BodyFrame = frame;
        if (body != null) body.sprite = BossArt.Body(boss, frame);
    }

    // ---- arrival (intro, real time) ------------------------------------

    public void BeginArrival()
    {
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
        transform.position = Vector3.LerpUnclamped(new Vector3(0f, 7.6f, 0f), basePos, e);

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
        EnsureBodyHitbox();
    }

    void EnsureBodyHitbox()
    {
        if (bodyHit != null) return;
        bodyHit = BossHitbox.Box(transform, "BossBody", BossConfig.BodyHitbox);
        bodyHit.AddComponent<BossTarget>();
    }

    public void StepFight(float dt, float realDt, Vector3 player, float progress01, BossProjectilePool pool)
    {
        if (mode != Mode.Fighting) return;
        TickFlash(realDt);
        if (dt <= 0f) { RefreshFrame(); return; }

        animClock += dt;
        moveClock += dt * (phase == AttackPhase.Tell ? .35f : 1f);
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
                int unlocked = Mathf.Max(1, BossCatalog.UnlockedAttacks(boss, progress01));
                attackIndex = (attackIndex + 1) % unlocked;
                current = boss.attacks[attackIndex];
                phase = AttackPhase.Tell;
                phaseTimer = current.tellSeconds;
                tellAge = 0f;
                AttacksStarted++;
                if (current.kind == BossAttackKind.Lanes) phaseTimer = current.tellSeconds + SpawnLanes(pool);
                return;

            case AttackPhase.Tell:
                tellAge += dt;
                if (current.kind == BossAttackKind.Lanes)
                {
                    if (tellAge >= current.tellSeconds) { phase = AttackPhase.Lanes; Fire(); }
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

            case AttackPhase.Lanes:
                if (phaseTimer <= 0f) EndAttack(progress01);
                return;
        }
    }

    void EndAttack(float progress01)
    {
        phase = AttackPhase.Cooldown;
        float scale = BossCatalog.FinalPhase(progress01) ? BossConfig.FinalPhaseCooldownScale : 1f;
        phaseTimer = current.cooldown * scale;
    }

    void Fire()
    {
        fireLeft = BossArt.FireTicks * BossArt.Tick;
    }

    Vector3 Muzzle => transform.position + new Vector3(current.muzzle.x, current.muzzle.y, 0f);

    void Volley(Vector3 player, BossProjectilePool pool)
    {
        volleysFired++;
        Fire();
        if (pool == null) return;
        Vector3 from = Muzzle;
        int n = Mathf.Max(1, current.count);
        if (current.kind == BossAttackKind.Aimed)
        {
            Vector2 to = (Vector2)(player - from);
            float aim = to.sqrMagnitude < .0001f ? -90f : Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                float off = n == 1 ? 0f : -current.spreadDeg * .5f + current.spreadDeg * i / (n - 1);
                pool.Fire(boss, current.style, from, Heading(aim + off) * current.speed);
            }
        }
        else
        {
            float rotate = current.rotateDeg * ((volleysFired % 2 == 0) ? 1f : -1f);
            for (int i = 0; i < n; i++)
            {
                float off = n == 1 ? 0f : -current.spreadDeg * .5f + current.spreadDeg * i / (n - 1);
                pool.Fire(boss, current.style, from, Heading(-90f + off + rotate) * current.speed);
            }
        }
    }

    static Vector2 Heading(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    public static float LaneX(int slot)
    {
        int slots = Mathf.Max(2, BossConfig.LaneSlots);
        float w = BossConfig.LaneHalfWidth * 2f / slots;
        return -BossConfig.LaneHalfWidth + w * (slot + .5f);
    }

    public static float LaneWidth => BossConfig.LaneHalfWidth * 2f / Mathf.Max(2, BossConfig.LaneSlots);

    // Telegraphs every lane of the attack and returns how long after the
    // tell the last beam stays live. There is always at least one lane that
    // never fires.
    float SpawnLanes(BossProjectilePool pool)
    {
        int slots = Mathf.Clamp(BossConfig.LaneSlots, 2, laneOrder.Length);
        for (int i = 0; i < slots; i++) laneOrder[i] = i;
        int lanes;
        if (current.sweep)
        {
            int safe = Random.Range(0, slots);
            bool leftToRight = Random.value < .5f;
            lanes = 0;
            for (int i = 0; i < slots; i++)
            {
                int slot = leftToRight ? i : slots - 1 - i;
                if (slot != safe) laneOrder[lanes++] = slot;
            }
        }
        else
        {
            lanes = Mathf.Clamp(current.count, 1, slots - 1);
            for (int i = 0; i < slots - 1; i++)
            {
                int j = Random.Range(i, slots);
                int t = laneOrder[i]; laneOrder[i] = laneOrder[j]; laneOrder[j] = t;
            }
        }

        float top = transform.position.y + current.muzzle.y;
        float last = 0f;
        for (int k = 0; k < lanes; k++)
        {
            float delay = current.sweep ? k * current.stagger : 0f;
            if (pool != null)
                pool.Lane(boss, LaneX(laneOrder[k]), LaneWidth, top, current.tellSeconds + delay, current.laneHold);
            last = Mathf.Max(last, delay + current.laneHold);
        }
        return last;
    }

    void RefreshFrame()
    {
        // The muzzle charge: the tell's telegraph for shots, growing on 2s.
        bool showCharge = mode == Mode.Fighting && phase == AttackPhase.Tell && current != null;
        charge.enabled = showCharge;
        if (showCharge)
        {
            float k = Mathf.Clamp01(tellAge / Mathf.Max(.05f, current.tellSeconds));
            int step = Mathf.FloorToInt(tellAge / (2f * BossArt.Tick));
            float size = Mathf.Lerp(.3f, .8f, Mathf.Floor(k * 4f) / 4f) * (step % 2 == 0 ? 1f : .86f);
            charge.sprite = BossArt.Shot(boss, BossArt.Charge);
            charge.transform.localPosition = new Vector3(current.muzzle.x, current.muzzle.y, 0f);
            charge.transform.localScale = Vector3.one * size;
        }

        if (flashLeft > 0f) { SetFrame(BossArt.Hit); return; }
        if (fireLeft > 0f) { SetFrame(BossArt.Fire); return; }
        if (phase == AttackPhase.Tell && current != null)
        {
            SetFrame(BossArt.Tell(current.tell, tellAge < BossArt.TellInTicks * BossArt.Tick ? 0 : 1));
            return;
        }
        if (phase == AttackPhase.Lanes && current != null) { SetFrame(BossArt.Tell(current.tell, 1)); return; }
        SetFrame(BossArt.Idle0 + BossArt.FrameAt(BossArt.IdleTicks, animClock, true));
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
        charge.enabled = false;
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
        if (transform.position.y > 8f) { body.enabled = false; mode = Mode.Gone; }
    }

    static readonly float[] BlastTimes = { 0f, .12f, .26f, .4f };

    TargetExplosion.Kind ExplosionKind => boss.artKey == "Ember" ? TargetExplosion.Kind.Rock : TargetExplosion.Kind.Metal;
}

// Marks the boss's body hitbox as the ultimate's boss target. CinematicClear
// finds it by its "Enimey" tag like any hazard; ShipPowerController.HitTarget
// asks Intercept first, so a homing shot that lands on the boss shortens the
// fight and flashes it instead of destroying it.
public class BossTarget : MonoBehaviour
{
    public static bool Intercept(GameObject target, int ship)
    {
        if (target == null || target.GetComponent<BossTarget>() == null) return false;
        var encounter = BossEncounter.Instance;
        TargetExplosion.Spawn(target.transform.position + Vector3.down * .35f, TargetExplosion.Kind.Metal,
                              TargetExplosion.Size.Medium, ship);
        collisionDetection.PlayExplosion();
        if (encounter != null) encounter.OnUltimateHit();
        return true;
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
