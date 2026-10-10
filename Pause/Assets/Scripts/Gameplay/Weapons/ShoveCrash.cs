using UnityEngine;

// SHOVE COLLISIONS. A body the shield's shockwave has shoved (ShieldShockwave /
// EnemyShove) is a battering ram for ShoveCarrySeconds (the push, PushSeconds
// .28 s, plus a short carry): if its body overlaps another enemy it deals CRASH
// DAMAGE to it and takes a smaller recoil. Friendly fire among enemies, done
// for the pilot: a kill it causes is the PILOT'S kill.
//
// SIZE CLASS (Weight): light 1, medium 2, heavy 3.
//     fighter / alien / chaser, a small rock (scale <= .8)   1
//     rail mine, an ordinary rock                            2
//     big (EnemyRole.Big), a large rock (scale >= 1.2)       3
//   An elite takes a heart from a Weight >= 2 crash (EliteMinWeight), no more
//   than one heart a hit (EliteDamage.ShoveCrash, the usual grace/flash), and
//   counts as Weight 3 when it recoils the shover.
// THE NUMBERS. Damage dealt = the shover's Weight. A victim dies when the
//   damage >= its Weight (a rock or a big breaks anything; a fighter cannot
//   break a rock, it only sparks). Recoil = the victim's Weight - 1; the shover
//   dies when recoil >= its own Weight (a fighter driven into a rock breaks,
//   rock into rock or into a fighter does not). Nothing keeps HP between hits.
// RULES. One crash per pair per release (MaxPairs); a victim is crash-hit at
//   most every VictimCooldown (.15 s); at most MaxKillsPerWave (6) crash kills
//   and MaxHitsPerWave (12) crashes per release, so one shockwave cannot wipe
//   the board. A shover that is itself killed stops carrying.
// VICTIMS. Roster enemies, rocks, mines, chasers and elites in play. NOT:
//   bosses and anything of theirs (body, shots, lasers, support), parked or
//   lifting elites, shot hitboxes, pickups, anything outside the view. The ship
//   is never hit (this only looks at the hazard registry); hostile shots are
//   not victims and not shoved.
// PAY. A non-elite crash kill goes through the same path as any weapon kill
//   (TargetExplosion, EnemyDeathAudio, collisionDetection.AwardDestroyedTarget:
//   score + chain, kills / rocks / mines counters, dust, codex, secret meter),
//   but never starts a DEATH COMBO (a shockwave must not chain past the cap).
//   An elite brought down pays its own reward and counts for the elite
//   achievements (AchievementTracker.PilotKill).
// FEEL. A few hard-pixel sparks at the contact point (EliteFx), the shield
//   whump at low volume (ThudVolume), a tiny camera kick once a release.
// COST. Fixed arrays, O(shoved set x live), nothing allocated.
public static class ShoveCrash
{
    public static bool Enabled = true;
    public const float ShoveCarrySeconds = .42f;   // from the release: PushSeconds + a short carry
    public const float VictimCooldown = .15f;
    public const int MaxKillsPerWave = 6;
    public const int MaxHitsPerWave = 12;
    public const int EliteMinWeight = 2;
    public const float ThudVolume = .22f;
    public const float KickAmount = .004f;
    public const int SparkCount = 4;
    public static readonly Color SparkColor = new Color(1f, .66f, .35f, 1f);

    // counters (tests, previews)
    public static int Hits, Kills, EliteHits, Recoils;
    public static bool Killing { get; private set; }   // a crash kill is being paid
    public static int WaveKills => waveKills;
    public static int WaveHits => waveHits;
    public static int Carriers => count;

    const int Capacity = EnemyShove.Capacity;
    const int MaxPairs = 32;

    struct Carrier
    {
        public ClearTarget target;
        public int weight;
        public float left;
    }
    static readonly Carrier[] carriers = new Carrier[Capacity];
    static int count;
    static readonly int[] pairA = new int[MaxPairs], pairB = new int[MaxPairs];
    static int pairs;
    static readonly int[] coolId = new int[MaxPairs];
    static readonly float[] coolUntil = new float[MaxPairs];
    static int coolNext;
    static int waveKills, waveHits;
    static float clock;
    static bool kicked;

    public static void ResetCounters() { Hits = Kills = EliteHits = Recoils = 0; }

    public static void Clear()
    {
        for (int i = 0; i < count; i++) carriers[i] = default;
        count = 0; pairs = 0; coolNext = 0; waveKills = waveHits = 0; clock = 0f; kicked = false;
        for (int i = 0; i < MaxPairs; i++) { coolId[i] = 0; coolUntil[i] = 0f; }
    }

    // A release begins: the caps start again.
    public static void BeginWave() { pairs = 0; waveKills = waveHits = 0; kicked = false; }

    // The size class of a hazard body.
    public static int Weight(GameObject go)
    {
        if (go == null) return 1;
        if (go.TryGetComponent(out EliteShip _)) return 3;
        var def = EnemyIdentity.Of(go);
        if (go.CompareTag("Astr") || (def != null && def.role == EnemyRole.Rock))
        {
            float s = EnemyIdentity.ScaleOf(go);
            return s >= 1.2f ? 3 : (s <= .8f ? 1 : 2);
        }
        if (def == null) return 1;
        switch (def.role)
        {
            case EnemyRole.Big: return 3;
            case EnemyRole.Mine: return 2;
            default: return 1;
        }
    }

    // Would a crash from `shover` break `other` (ShieldShockwave.KeepApart lets
    // such a pair meet; every other pair is still kept apart)?
    public static bool WouldBreak(GameObject shover, GameObject other)
    {
        if (!Enabled || other == null || !Victim(other)) return false;
        if (other.TryGetComponent(out EliteShip _)) return false;
        return Weight(shover) >= Weight(other);
    }

    // `body` was just shoved: it carries for ShoveCarrySeconds.
    public static void Carry(Transform body)
    {
        if (!Enabled || body == null || !body.TryGetComponent(out ClearTarget t)) return;
        int w = Weight(body.gameObject);
        for (int i = 0; i < count; i++)
            if (carriers[i].target == t) { carriers[i].left = ShoveCarrySeconds; carriers[i].weight = w; return; }
        if (count >= Capacity) return;
        carriers[count++] = new Carrier { target = t, weight = w, left = ShoveCarrySeconds };
    }

    static bool Allowed => Enabled && !buttonClicks.playerDied && !DeathCrash.Running && !startMenu.youAreInTutorial;

    // Can this be crash-hit at all (before any cooldown / pair rule)?
    static bool Victim(GameObject go)
    {
        if (!ClearTarget.IsHazard(go) || FriendlyFire.Immune(go) || RamKill.NotAHazardBody(go)) return false;
        if (RunScore.IsHostileShot(go)) return false;
        if (go.TryGetComponent(out BossProjectile _) || go.TryGetComponent(out BossBeam _) || go.TryGetComponent(out BossActor _)) return false;
        if (go.GetComponentInParent<BossActor>() != null) return false;
        if (go.TryGetComponent(out EliteShip elite) && !elite.InPlay) return false;
        return true;
    }

    static bool Cooling(int id)
    {
        for (int i = 0; i < MaxPairs; i++) if (coolId[i] == id && coolUntil[i] > clock) return true;
        return false;
    }

    static void Cool(int id)
    {
        coolId[coolNext] = id; coolUntil[coolNext] = clock + VictimCooldown;
        coolNext = (coolNext + 1) % MaxPairs;
    }

    static bool Paired(int a, int b)
    {
        for (int i = 0; i < pairs; i++)
            if ((pairA[i] == a && pairB[i] == b) || (pairA[i] == b && pairB[i] == a)) return true;
        return false;
    }

    // One running frame of world time (EnemyShove.Step).
    public static void Step(float dt)
    {
        if (count == 0) return;
        clock += dt;
        bool allowed = Allowed;
        Rect view = ShipTargets.View();
        for (int ci = count - 1; ci >= 0; ci--)
        {
            var c = carriers[ci];
            if (c.target == null || !c.target.isActiveAndEnabled) { RemoveAt(ci); continue; }
            c.left -= dt;
            carriers[ci].left = c.left;
            if (c.left <= 0f) { RemoveAt(ci); continue; }
            if (!allowed || waveKills >= MaxKillsPerWave || waveHits >= MaxHitsPerWave) continue;
            Resolve(ci, c, view);
        }
    }

    static void RemoveAt(int i)
    {
        count--;
        carriers[i] = carriers[count];
        carriers[count] = default;
    }

    static void Resolve(int ci, Carrier c, Rect view)
    {
        var self = c.target;
        Vector2 pa = self.transform.position;
        int idA = self.GetInstanceID();
        var live = ClearTarget.Live;
        for (int j = 0; j < live.Count; j++)
        {
            var t = live[j];
            if (t == null || t == self || !t.isActiveAndEnabled) continue;
            Vector2 pb = t.transform.position;
            float r = (self.Radius + t.Radius) * FriendlyFire.CrashOverlap;
            if ((pa - pb).sqrMagnitude > r * r || !view.Contains(pb)) continue;
            int idB = t.GetInstanceID();
            if (Cooling(idB) || Paired(idA, idB)) continue;
            var go = t.gameObject;
            if (!Victim(go)) continue;
            Crash(ci, self, t, c.weight, Vector2.Lerp(pa, pb, .5f));
            return;   // one crash a step (the registry changes under a kill)
        }
    }

    static void Crash(int ci, ClearTarget shover, ClearTarget victim, int damage, Vector2 at)
    {
        var vgo = victim.gameObject;
        var sgo = shover.gameObject;
        int vw = Weight(vgo);
        bool victimDies;
        if (vgo.TryGetComponent(out EliteShip elite))
        {
            if (damage < EliteMinWeight || elite.Grace > 0f) return;
            victimDies = false;   // (its own death plays through TakeHit)
        }
        else victimDies = damage >= vw;

        if (pairs < MaxPairs) { pairA[pairs] = shover.GetInstanceID(); pairB[pairs] = victim.GetInstanceID(); pairs++; }
        Cool(victim.GetInstanceID());
        waveHits++;
        Hits++;
        Feel(at);

        if (elite != null)
        {
            EliteHits++;
            EliteShip.HitBy = "shoved body";
            elite.TakeHit(EliteDamage.ShoveCrash, at);
            if (elite.State == EliteState.Dead) { waveKills++; Kills++; }
        }
        else if (victimDies) { KillPaid(vgo); waveKills++; Kills++; }

        // recoil
        if (vw - 1 >= Weight(sgo) && shover.isActiveAndEnabled && waveKills < MaxKillsPerWave)
        {
            Recoils++;
            KillPaid(sgo);
            waveKills++; Kills++;
        }
    }

    static void Feel(Vector2 at)
    {
        SparksAt(at);
        if (!Application.isPlaying && !EnemyDeathAudio.Simulate) return;
        EnemyDeathAudio.PlayAuthored(ShieldShockwave.SoundKey, ThudVolume);
        if (!kicked) { kicked = true; CameraKick.Kick(KickAmount); }
    }

    static void SparksAt(Vector2 at)
    {
        var fx = EliteSystem.Fx;
        if (fx != null) fx.Sparks(at, SparkColor, SparkCount);
    }

    // The pilot's kill: the blast and sound as for any kill, then the normal
    // pay (score, counters, dust, codex), no DEATH COMBO.
    static void KillPaid(GameObject go)
    {
        if (go == null) return;
        Killing = true;
        try
        {
            TargetExplosion.Spawn(go, ShipId.Equipped());
            EnemyDeathAudio.Play(go);
            collisionDetection.AwardDestroyedTarget(go);
        }
        finally { Killing = false; }
        ClearTarget.Release(go);
        if (Application.isPlaying) Object.Destroy(go);
        else Object.DestroyImmediate(go);
    }
}
