using System.Collections.Generic;
using UnityEngine;

// The death crash's domino: what the flying wreckage does to the board.
//
// Slow motion. On death the world stops as it always has (playerDied: the
// spawners, the player's input, the boss, the distance score; timeScale 0),
// and nothing about that changes -- every system that keys off playerDied
// keeps doing so. Instead the crash steps the board itself, on its own clock
// at SlowMo (x0.2): at the fatal hit it takes a snapshot of the movers on
// screen (the ClearTarget registry: enemies, rocks, mines, chasers, elites,
// pickups) and each step drives their own movers by hand -- moveEnimes'
// weave (on the board clock plus this slow-motion clock), the straight
// scroll, the chaser, AsteroidSpin, the EnemyFlipbook drawing; an elite
// drifts on its velocity (its AI stays off). The boss stays frozen with its
// aborted fight.
//
// Domino. A flying piece (hull fragment, drone, the tumbling killer, or a
// piece of something it already broke) that touches an enemy, rock, mine or
// elite on screen destroys it -- the ship's own wreckage only
// DeathCombo.DeathHitChance (40%) of the time, rolled once per enemy (a lost
// roll glances off and spares it): a blast, and the enemy breaks into 2-4
// pieces cut from its own drawing (DeathCrash's Voronoi cut, CountFor its
// size, cached per drawing), which fly their own arcs into the rails. Each
// chain kill rolls DeathCombo.DeathChainChance (40%): won, its pieces can hit
// more (a chain, up to MaxGeneration deep); lost, they are harmless debris
// over the board. (Before, every touch killed and every kill's pieces were
// live, so a busy board was nearly always wiped.) A piece may ricochet off what it
// hit (RicochetChance, at most MaxRicochets) and fly on. An elite dies
// through its own damage API (EliteShip.TakeHit, EliteDamage.Domino): its own
// death plays and it pays its own elite reward as well as the domino points.
// The boss body is never destroyed -- a piece sparks off it and bounces;
// a boss or elite shot (or a resin pool) it touches is cleared, unscored.
//
// MEGA DOMINO (MegaChance of deaths, with at least MegaMinTargets on
// screen): the death blast throws a shockwave of shrapnel that takes every
// enemy, rock, mine and elite on screen, nearest first, one after another
// (MegaInterval apart; compressed only if the whole screen could not
// otherwise go before the ceiling).
//
// Timing is dynamic: the sequence ends when nothing still in the air can
// hit anything and no cascade kill is pending; then DEATH COMBO lands and the
// panel follows SettleTime later. No new kill or ricochet after LastKillAt,
// so everything lands by CeilingEnd (ExtraCeiling past the longest ordinary
// death) at the latest; ForceLand is the safety net. A tap after SkipAfter
// plays the rest of the chain out at once, unseen (FastForward), so the score
// is what it would have been.
//
// Scoring: the n-th chain kill earns its base kill points (ScoreRules) x n,
// capped at ScoreRules.DominoMaxMultiplier ("DOMINO x3" popups, ScoreHud);
// the total (+ MegaDominoBonus for a MEGA DOMINO) is added once, when the
// chain ends, as RunScore's deathCombo ("DEATH COMBO +N", its own death
// panel row). The run is settled only after that (score.cs waits for
// PanelReady). Chain kills count for the codex and the kill achievements
// like any kill; they pay no star dust (elites aside) and don't touch the
// in-run kill chain. The tutorial scores nothing, as ever.
public partial class DeathCrash
{
    // ---- slow motion ----
    public const float SlowMo = .2f;

    // ---- domino ----
    public const int MaxTargets = 64;
    public const int MaxChainPieces = 40;
    public const int MaxGeneration = 6;
    public const int MaxRicochets = 2;
    public const int MaxChainKills = 32;     // an ordinary chain; a MEGA DOMINO takes the whole screen
    public const float DebrisFlightMin = .5f, DebrisFlightMax = .78f;
    public const float RicochetFlightMin = .42f, RicochetFlightMax = .65f;
    public const float RicochetChance = .6f;
    // A chain kill earns the banner a beat on screen before the panel.
    public const float ComboHold = .45f;
    // The chain may add at most this much to the longest ordinary death.
    public const float ExtraCeiling = 10f;
    public const float CeilingEnd = MaxTotal + ExtraCeiling;
    // No kill or ricochet after this: whatever it throws still lands and
    // settles before CeilingEnd.
    public const float LastKillAt = CeilingEnd - DebrisFlightMax - Settle - ComboHold - .1f;

    // ---- MEGA DOMINO ----
    public const float MegaChance = .12f;
    public const int MegaMinTargets = 3;
    public const float MegaStart = HitStop + .12f, MegaInterval = .22f;
    // Tests / previews: force a MEGA DOMINO on (true) or off (false); null rolls.
    public static bool? ForceMega;
    // Tests: the old crash, with no chain at all.
    public static bool DominoEnabled = true;

    // Shot: a boss or elite shot / resin pool -- a piece clears it (no score).
    public enum TargetKind { Prop, Enemy, Elite, Boss, Shot }

    // A chain kill: where, its multiplier, its points (ScoreHud's popup).
    public static event System.Action<Vector3, int, int> DominoKill;
    // A MEGA DOMINO began (the banner), at the ship.
    public static event System.Action<Vector3> MegaDominoStarted;
    // The chain is over: its total (what the run was given), kills, mega.
    public static event System.Action<int, int, bool> DeathCombo;

    struct Actor
    {
        public GameObject go;
        public Transform tf;
        public SpriteRenderer sr;
        public TargetKind kind;
        public bool alive;
        public bool rolled, spared;   // the wreckage's DeathCombo.DeathHitChance roll, once
        public float radius, megaAt;
        public moveEnimes weave;
        public moveItemEnmInStrightLine line;
        public ChaserEnemy chaser;
        public AsteroidSpin spin;
        public EnemyFlipbook flip;
        public EliteShip elite;
        public EliteShot eliteShot;
        public BossProjectile bossShot;
        public FragmentSet set;
    }

    readonly Actor[] actors = new Actor[MaxTargets];
    readonly int[] megaOrder = new int[MaxTargets];
    readonly float[] megaDistance = new float[MaxTargets];
    int shotsCleared;
    int actorCount, megaCount, megaNext, dominoKills, bestDominoMultiplier, deepest, comboTotal, comboAwarded, ricochets;
    long dominoPoints;
    bool mega, comboAnnounced, fastForward;
    float worldClock, lastKillTime;
    Vector3 megaFrom;

    // ---- read-outs (tests, previews) ----
    public int ActorCount => actorCount;
    public int Targets
    {
        get { int n = 0; for (int i = 0; i < actorCount; i++) if (actors[i].kind == TargetKind.Enemy || actors[i].kind == TargetKind.Elite) n++; return n; }
    }
    public int TargetsAlive
    {
        get
        {
            int n = 0;
            for (int i = 0; i < actorCount; i++)
                if ((actors[i].kind == TargetKind.Enemy || actors[i].kind == TargetKind.Elite) && actors[i].alive && actors[i].go != null) n++;
            return n;
        }
    }
    public int DominoKills => dominoKills;
    public int ShotsCleared => shotsCleared;
    public long DominoPoints => dominoPoints;
    public int BestDominoMultiplier => bestDominoMultiplier;
    public int DeepestGeneration => deepest;
    public int Ricochets => ricochets;
    public bool Mega => mega;
    public bool ComboAnnounced => comboAnnounced;
    public int ComboTotal => comboTotal;
    public int ComboAwarded => comboAwarded;
    public float WorldClock => worldClock;
    public float LastKillTime => lastKillTime;
    public bool CascadePending => mega && megaNext < megaCount;
    public int GenerationOf(int i) => pieces[i].gen;
    public bool Flying(int i) => pieces[i].active && !pieces[i].landed && elapsed >= pieces[i].t0;
    public bool AnyFlying
    {
        get { for (int i = 0; i < pieceCount; i++) if (pieces[i].active && !pieces[i].landed) return true; return false; }
    }
    // Where piece i's current arc is at progress u (0-1).
    public Vector3 PathAt(int i, float u)
    {
        ref var p = ref pieces[i];
        u = Mathf.Clamp01(u);
        return Bezier(p.start, p.control, p.target, u * (.55f + .45f * u));
    }
    public float ProgressOf(int i) => pieces[i].dur > 0f ? Mathf.Clamp01((elapsed - pieces[i].t0) / pieces[i].dur) : 1f;
    float SettleTime => dominoKills > 0 ? Settle + ComboHold : Settle;

    // ---- setup (the fatal hit) ----

    void ResetDomino()
    {
        for (int i = 0; i < actorCount; i++) actors[i] = default;
        actorCount = megaCount = megaNext = dominoKills = bestDominoMultiplier = deepest = 0;
        comboTotal = comboAwarded = ricochets = shotsCleared = 0;
        dominoPoints = 0;
        mega = comboAnnounced = fastForward = false;
        worldClock = lastKillTime = 0f;
    }

    void BeginDomino(GameObject killer, Vector3 shipAt)
    {
        ResetDomino();
        if (!DominoEnabled) return;
        Gather(killer);

        // Cut every target's drawing now (cached per drawing), so a kill in
        // the chain costs no GPU read-back.
        if (targetSets.Count > MaxCachedTargetSets)
        {
            foreach (var old in targetSets.Values) if (old != null) old.Destroy();
            targetSets.Clear();
        }
        int cuts = 0;
        for (int i = 0; i < actorCount; i++)
        {
            ref var a = ref actors[i];
            if (a.kind != TargetKind.Enemy && a.kind != TargetKind.Elite) continue;
            if (a.sr == null || a.sr.sprite == null) continue;
            a.set = TargetSet(a.sr.sprite, CountFor(a.radius), ref cuts);
        }

        int killable = Targets;
        bool roll = ForceMega ?? (Random.value < MegaChance);
        if (roll && killable >= (ForceMega == true ? 1 : MegaMinTargets)) StartMega(shipAt);
    }

    // The movers and targets on (or just off) screen, from the ClearTarget
    // registry (no scene scan).
    void Gather(GameObject killer)
    {
        var live = ClearTarget.Live;
        float xIn = edge + .4f, yLo = viewBottom - .25f, yHi = viewTop + .25f;
        Transform killerTf = killer != null ? killer.transform : null;
        Transform shipTf = ship != null ? ship.transform : null;
        for (int i = live.Count - 1; i >= 0 && actorCount < MaxTargets; i--)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            var go = t.gameObject;
            var tf = go.transform;
            if (killerTf != null && (tf == killerTf || tf.IsChildOf(killerTf) || killerTf.IsChildOf(tf))) continue;
            if (shipTf != null && tf.IsChildOf(shipTf)) continue;
            if (go.GetComponentInParent<EliteShot>() != null) continue;   // shots: below
            Vector3 pos = tf.position;
            if (Mathf.Abs(pos.x) > xIn + 2f || pos.y < yLo - 2f || pos.y > yHi + 2f) continue;
            bool onScreen = Mathf.Abs(pos.x) <= xIn && pos.y >= yLo && pos.y <= yHi;

            var elite = go.GetComponent<EliteShip>();
            TargetKind kind = TargetKind.Prop;
            if (ClearTarget.IsHazard(go))
            {
                var k = Classify(go);
                if (k == KillerKind.BossBody) kind = TargetKind.Boss;
                else if (k == KillerKind.Projectile) kind = TargetKind.Prop;
                else if (elite != null) kind = elite.InPlay && onScreen ? TargetKind.Elite : TargetKind.Prop;
                else kind = onScreen ? TargetKind.Enemy : TargetKind.Prop;
            }

            ref var a = ref actors[actorCount++];
            a = default;
            a.go = go;
            a.tf = tf;
            a.kind = kind;
            a.alive = true;
            a.radius = t.Radius;
            a.elite = elite;
            if (elite != null) a.sr = elite.Hull;
            else
            {
                a.sr = go.GetComponent<SpriteRenderer>();
                if (a.sr == null || a.sr.sprite == null) a.sr = go.GetComponentInChildren<SpriteRenderer>();
            }
            if (kind == TargetKind.Boss) continue;   // frozen with its fight
            a.weave = go.GetComponent<moveEnimes>();
            a.line = go.GetComponent<moveItemEnmInStrightLine>();
            a.chaser = go.GetComponent<ChaserEnemy>();
            a.spin = go.GetComponent<AsteroidSpin>();
            a.flip = go.GetComponent<EnemyFlipbook>();
        }

        // Boss and elite shots (and the resin pools) on screen: not
        // ClearTargets, so found once here. A piece clears them; no score.
        foreach (var s in Object.FindObjectsByType<EliteShot>(FindObjectsSortMode.None))
        {
            if (actorCount >= MaxTargets) break;
            if (s == null || !s.Active || !s.gameObject.activeInHierarchy) continue;
            if (killerTf != null && killerTf.IsChildOf(s.transform)) continue;   // it's what killed the ship
            if (AddShot(s.gameObject, Mathf.Max(.12f, s.Radius), xIn, yLo, yHi)) actors[actorCount - 1].eliteShot = s;
        }
        foreach (var b in Object.FindObjectsByType<BossProjectile>(FindObjectsSortMode.None))
        {
            if (actorCount >= MaxTargets) break;
            if (b == null || !b.Active || !b.gameObject.activeInHierarchy) continue;
            if (killerTf != null && killerTf.IsChildOf(b.transform)) continue;
            if (AddShot(b.gameObject, ClearTarget.MeasureRadius(b.gameObject), xIn, yLo, yHi)) actors[actorCount - 1].bossShot = b;
        }
    }

    bool AddShot(GameObject go, float radius, float xIn, float yLo, float yHi)
    {
        Vector3 pos = go.transform.position;
        if (Mathf.Abs(pos.x) > xIn || pos.y < yLo || pos.y > yHi) return false;
        ref var a = ref actors[actorCount++];
        a = default;
        a.go = go;
        a.tf = go.transform;
        a.kind = TargetKind.Shot;
        a.alive = true;
        a.radius = radius;
        return true;
    }

    // ---- per step ----

    void StepDomino(float dt)
    {
        if (actorCount == 0) return;
        StepWorld(dt * SlowMo);
        if (CascadePending) StepMega();
        if (elapsed >= CeilingEnd - SettleTime) ForceLand();
    }

    // The board in slow motion: each mover stepped by hand on this clock.
    void StepWorld(float wdt)
    {
        worldClock += wdt;
        float clock = SpawnSpace.Clock + worldClock;
        for (int i = 0; i < actorCount; i++)
        {
            ref var a = ref actors[i];
            if (!a.alive) continue;
            if (a.go == null) { a.alive = false; continue; }
            if (a.kind == TargetKind.Boss || a.kind == TargetKind.Shot) continue;   // frozen with their fight
            if (a.elite != null)
            {
                Vector2 v = a.elite.Velocity;
                a.tf.position += new Vector3(v.x, v.y, 0f) * wdt;
                continue;
            }
            if (a.weave != null && a.weave.enabled) a.weave.Step(wdt, clock);
            else if (a.line != null && a.line.enabled && !a.line.Step(wdt)) { a.alive = false; continue; }
            if (a.chaser != null && a.chaser.enabled) a.chaser.Step(wdt);
            if (a.spin != null && a.spin.enabled) a.spin.Advance(wdt);
            if (a.flip != null && a.flip.enabled) a.flip.Advance(wdt);
        }
    }

    // A flying piece against the board.
    void Collide(int i)
    {
        if (!running || actorCount == 0) return;
        ref var p = ref pieces[i];
        Vector3 pos = p.tf.position;
        for (int j = 0; j < actorCount; j++)
        {
            ref var a = ref actors[j];
            if (!a.alive || a.kind == TargetKind.Prop || j == p.lastHit) continue;
            if (a.go == null) { a.alive = false; continue; }
            Vector3 d = a.tf.position - pos;
            float r = p.radius * .8f + a.radius * .85f;
            if (d.x * d.x + d.y * d.y > r * r) continue;
            Hit(i, j);
            return;
        }
    }

    bool CanKill(int gen) => elapsed < LastKillAt && gen < MaxGeneration && (mega || dominoKills < MaxChainKills);
    bool CanBounce(ref Piece p) => p.ricochets < MaxRicochets && elapsed < LastKillAt;

    void Hit(int i, int j)
    {
        ref var p = ref pieces[i];
        ref var a = ref actors[j];
        Vector3 at = a.tf.position;
        p.lastHit = j;
        if (a.kind == TargetKind.Shot)
        {
            // A shot or pool in the way is cleared; the piece flies on.
            a.alive = false;
            shotsCleared++;
            Sparks(at, 4, 1.8f);
            if (a.eliteShot != null) a.eliteShot.Recycle();
            else if (a.bossShot != null) a.bossShot.Recycle();
            return;
        }
        if (a.kind == TargetKind.Boss)
        {
            // The boss shrugs it off: a spark, and the piece glances away.
            Sparks(p.tf.position, 5, 2.4f);
            if (CanBounce(ref p)) Ricochet(i, at);
            return;
        }
        if (p.dud || !CanKill(p.gen)) return;   // a dud, or past the caps: it flies on through
        if (p.gen == 0)
        {
            // The ship's own wreckage breaks it DeathHitChance of the time
            // (rolled once); otherwise it glances off and the wreckage spares it.
            if (!a.rolled) { a.rolled = true; a.spared = !global::DeathCombo.RollDeathHit(); if (a.spared) Sparks(p.tf.position, 4, 2f); }
            if (a.spared) return;
        }
        Vector3 push = p.tf.position - p.prev;
        Kill(j, p.gen + 1, push);
        if (CanBounce(ref p) && Random.value < RicochetChance) Ricochet(i, at);
    }

    // Off what it hit and on: a new arc, away from it, to a rail.
    void Ricochet(int i, Vector3 from)
    {
        ref var p = ref pieces[i];
        Vector3 pos = p.tf.position;
        Vector3 n = pos - from;
        n.z = 0f;
        if (n.sqrMagnitude < 1e-6f) n = Random.insideUnitCircle;
        n.Normalize();
        Vector3 v = pos - p.prev;
        v.z = 0f;
        Vector3 dir = v.sqrMagnitude > 1e-8f ? Vector3.Reflect(v.normalized, n) : n;
        dir = (dir + n).normalized;
        if (dir.sqrMagnitude < 1e-6f) dir = n;
        int side = Mathf.Abs(dir.x) < .2f ? -p.side : (dir.x < 0f ? -1 : 1);
        Plan(ref p, pos, dir, side, float.NaN);
        p.t0 = elapsed;
        p.dur = Random.Range(RicochetFlightMin, RicochetFlightMax);
        p.spin = -p.spin * 1.15f;
        p.ricochets++;
        ricochets++;
        Sparks(pos, 4, 2f);
    }

    // A chain kill: scored, blown up, broken into its own flying pieces.
    void Kill(int j, int gen, Vector3 push)
    {
        ref var a = ref actors[j];
        a.alive = false;
        var go = a.go;
        Vector3 at = a.tf.position;
        var blast = TargetExplosion.KindFor(go);

        dominoKills++;
        int mult = ScoreRules.DominoMultiplier(dominoKills);
        int basePoints = a.kind == TargetKind.Elite ? ScoreRules.DominoEliteBase : RunScore.BasePoints(go);
        int points = basePoints * mult;
        dominoPoints += points;
        if (mult > bestDominoMultiplier) bestDominoMultiplier = mult;
        if (gen > deepest) deepest = gen;
        lastKillTime = elapsed;

        // its pieces, from where its drawing is now; they carry the chain on
        // only if this death wins its DeathCombo roll
        bool live = global::DeathCombo.RollDeathChain();   // the class, not this event
        int made = 0;
        if (a.set != null && a.sr != null)
        {
            var stf = a.sr.transform;
            Vector3 scale = Abs(stf.lossyScale);
            float worldScale = Mathf.Max(scale.x, scale.y);
            Vector3 heading = push;
            heading.z = 0f;
            heading = heading.sqrMagnitude > 1e-8f ? heading.normalized : Vector3.zero;
            for (int k = 0; k < a.set.count; k++)
            {
                Vector3 start = stf.TransformPoint(new Vector3(a.set.local[k].x, a.set.local[k].y, 0f));
                Vector3 outward = start - at;
                outward.z = 0f;
                if (outward.sqrMagnitude < 1e-6f) outward = Random.insideUnitCircle;
                outward = (outward.normalized + heading * .7f).normalized;
                if (outward.sqrMagnitude < 1e-6f) outward = Vector3.up;
                int side = Mathf.Abs(outward.x) < .2f ? (Random.value < .5f ? -1 : 1) : (outward.x < 0f ? -1 : 1);
                int at2 = Add(PieceKind.Debris, a.set.sprites[k], start, stf.rotation, scale, a.set.radius[k] * worldScale,
                              side, false, outward, a.sr.flipX, a.sr.flipY, blast, float.NaN);
                if (at2 < 0) break;
                pieces[at2].gen = gen;
                pieces[at2].dud = !live;
                pieces[at2].lastHit = j;
                made++;
            }
        }

        if (a.kind == TargetKind.Elite && a.elite != null)
        {
            // Its own death: blast, debris, and its elite reward.
            a.elite.TakeHit(EliteDamage.Domino, at, Mathf.Max(1, a.elite.Hearts));
        }
        else
        {
            Codex.Discover(go);
            collisionDetection.RecordKillAchievement(go);
            EnemyDeathAudio.Play(go);
            if (!fastForward) EnemyDeathFlipbook.Spawn(go);   // its death pose, on the crash clock
            ClearTarget.Release(go);
            BossUtil.Kill(go);
        }
        a.go = null;

        Blast(at, blast, .75f, mega || gen <= 1);
        shake = Mathf.Max(shake, .05f);
        Sparks(at, 7, 3f);
        if (made == 0)
            for (int s = 0; s < 5; s++)
                Emit(s % 2 == 0 ? ShipDamageFx.RowScrap : ShipDamageFx.RowChunk, Random.Range(0, 4), at,
                     Random.insideUnitCircle * Random.Range(1.2f, 2.4f), Random.Range(.45f, .7f), .1f, .08f, -6f,
                     Random.Range(-720f, 720f), .6f, Color.white, SparkOrder);

        if (!fastForward) Raise(DominoKill, at, mult, points);
    }

    void Sparks(Vector3 at, int count, float speed)
    {
        for (int s = 0; s < count; s++)
            Emit(ShipDamageFx.RowSpark, Random.Range(0, 4), at, Random.insideUnitCircle.normalized * Random.Range(speed * .5f, speed),
                 Random.Range(.2f, .34f), .1f, .04f, -4f, 0f, 1.4f, Color.white, SparkOrder);
    }

    // ---- MEGA DOMINO ----

    void StartMega(Vector3 shipAt)
    {
        mega = true;
        megaCount = 0;
        for (int j = 0; j < actorCount; j++)
        {
            var kind = actors[j].kind;
            if (kind != TargetKind.Enemy && kind != TargetKind.Elite) continue;
            Vector3 d = actors[j].tf.position - shipAt;
            float dist = d.x * d.x + d.y * d.y;
            // nearest first (insertion)
            int k = megaCount++;
            while (k > 0 && megaDistance[k - 1] > dist)
            {
                megaOrder[k] = megaOrder[k - 1];
                megaDistance[k] = megaDistance[k - 1];
                k--;
            }
            megaOrder[k] = j;
            megaDistance[k] = dist;
        }
        float interval = MegaInterval;
        float window = LastKillAt - MegaStart;
        if (megaCount > 1 && (megaCount - 1) * interval > window) interval = window / (megaCount - 1);
        for (int k = 0; k < megaCount; k++) actors[megaOrder[k]].megaAt = MegaStart + k * interval;
        megaNext = 0;
        megaFrom = shipAt;

        // the shockwave: a big blast and a ring of shrapnel
        Blast(shipAt, TargetExplosion.Kind.Metal, 1.6f, true);
        shake = Mathf.Max(shake, .09f);
        for (int s = 0; s < 18; s++)
        {
            float ang = s * (Mathf.PI * 2f / 18f);
            Emit(ShipDamageFx.RowSpark, s % 4, shipAt, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(4f, 5.5f),
                 Random.Range(.3f, .45f), .13f, .05f, 0f, 0f, 1.6f, Color.white, SparkOrder);
        }
        Raise(MegaDominoStarted, shipAt);
    }

    // Each target on its turn: a streak of shrapnel from the last kill (or
    // the ship) to it, and it goes.
    void StepMega()
    {
        while (megaNext < megaCount)
        {
            int j = megaOrder[megaNext];
            ref var a = ref actors[j];
            if (!a.alive || a.go == null) { megaNext++; continue; }   // the chain got there first
            if (elapsed < a.megaAt) break;
            Vector3 at = a.tf.position;
            Shrapnel(megaFrom, at);
            Kill(j, 1, at - megaFrom);
            megaFrom = at;
            megaNext++;
        }
    }

    void Shrapnel(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.z = 0f;
        Vector2 dir = d.sqrMagnitude > 1e-6f ? (Vector2)d.normalized : Vector2.up;
        for (int s = 0; s < 6; s++)
        {
            Vector3 p = Vector3.Lerp(from, to, (s + 1f) / 7f);
            Emit(ShipDamageFx.RowSpark, s % 4, p, dir * Random.Range(3f, 5f), Random.Range(.14f, .22f),
                 .11f, .04f, 0f, 0f, 1f, Color.white, SparkOrder);
        }
    }

    // ---- the end ----

    // The safety net at the ceiling: everything still flying lands now.
    void ForceLand()
    {
        megaNext = megaCount;
        for (int i = 0; i < pieceCount; i++)
            if (pieces[i].active && !pieces[i].landed)
            {
                pieces[i].tf.position = pieces[i].target;
                Land(i);
            }
    }

    // A tap: the rest of the chain plays out at once, unseen.
    void FastForward()
    {
        if (actorCount == 0) return;
        fastForward = true;
        for (int guard = 0; guard < 2000 && running; guard++)
        {
            bool flying = false;
            for (int i = 0; i < pieceCount; i++)
                if (pieces[i].active && !pieces[i].landed) { flying = true; break; }
            if (!flying && !CascadePending) break;
            Step(MaxStep);
        }
        fastForward = false;
    }

    // The chain's total lands on the run, once.
    void AnnounceCombo()
    {
        if (comboAnnounced) return;
        comboAnnounced = true;
        if (dominoKills <= 0) return;
        long total = dominoPoints + (mega ? ScoreRules.MegaDominoBonus : 0);
        comboTotal = (int)System.Math.Min(total, int.MaxValue);
        comboAwarded = RunScore.OnDeathCombo(comboTotal, dominoKills, mega);
        if (DeathCombo == null) return;
        try { DeathCombo(comboTotal, dominoKills, mega); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    static void Raise(System.Action<Vector3, int, int> e, Vector3 at, int a, int b)
    {
        if (e == null) return;
        try { e(at, a, b); }
        catch (System.Exception ex) { Debug.LogException(ex); }
    }

    static void Raise(System.Action<Vector3> e, Vector3 at)
    {
        if (e == null) return;
        try { e(at); }
        catch (System.Exception ex) { Debug.LogException(ex); }
    }

    // ---- enemy fragments ----

    static readonly Dictionary<long, FragmentSet> targetSets = new Dictionary<long, FragmentSet>();
    const int MaxCachedTargetSets = 64, MaxCutsPerDeath = 16;

    // How many pieces a target of this radius breaks into.
    public static int CountFor(float radius) => radius < .28f ? 2 : radius < .55f ? 3 : 4;

    static FragmentSet TargetSet(Sprite sprite, int count, ref int cuts)
    {
        if (sprite == null || sprite.texture == null) return null;
        long key = ((long)sprite.GetInstanceID() << 4) ^ count;
        if (targetSets.TryGetValue(key, out var cached) && Alive(cached)) return cached;
        if (cuts >= MaxCutsPerDeath) return null;   // a debris burst instead
        cuts++;
        var set = Cut(sprite, -1, 0, count);
        if (set != null) targetSets[key] = set;
        return set;
    }

    // Any drawing cut into `count` (1-6) pieces, cached per drawing: each
    // piece's sprite (pivoted on its centre of mass), its pivot's offset from
    // the drawing's pivot (sprite-local units) and its rough radius. The
    // arrays are the cache's own -- read them, don't change them. False when
    // the drawing can't be cut. (A cosmetic helper for anything that wants
    // to break a drawing up, as the death crash does.)
    public static bool FragmentsOf(Sprite sprite, int count, out Sprite[] sprites, out Vector2[] local, out float[] radius)
    {
        int cuts = 0;
        var set = TargetSet(sprite, Mathf.Clamp(count, 1, MaxFragments), ref cuts);
        sprites = set != null ? set.sprites : null;
        local = set != null ? set.local : null;
        radius = set != null ? set.radius : null;
        return set != null;
    }
}
