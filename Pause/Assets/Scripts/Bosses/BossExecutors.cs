using UnityEngine;

// BOSS ATTACK EXECUTORS (plan phase 1g): how a BossAttackKind that is not a projectile or a laser is fired.
//
// An executor is registered for a kind (BossExecutors.Register) and given the boss, the attack and the ship's
// position at the START of the tell: it arms pooled hazards (Gameplay/Enemies/Attacks), each with the attack's tell,
// and puts them in `into`. The hazards ignite themselves when their own tell is up (so a later volley, armed with a
// longer tell, comes on its own); BossActor waits in its Hazards phase until every one has ended, then starts the
// cooldown. Adding a kind (the Lash, a Roll) is one class here, one enum value and one Register line -- the actor,
// the scheduler (minPhase) and the dodge bot's boss rows need nothing else.
//
//   Blast   one ring per volley from the first part (the crack of every other ring on the other side of the ship)
//   Strike  `count` lanes round the ship's x at its row, `spacing` apart
//   Jet     one jet per part per volley, each aimed at the ship's x +- aimSpreadX towards its own side
//   Wave    one band per volley falling from the first part; the gap on alternate sides
//
// The muzzle of a part comes from BossEmitters in the drawing the tell ends on (BossActor.TellMuzzle), so it is the
// real mouth / nozzle of the real art; a ring and a jet ride the boss until the last stretch of their tell.
public interface IBossAttackExecutor
{
    // Arms the attack's hazards; returns how many are in `into` (at most into.Length).
    int Arm(BossActor boss, BossAttack attack, Vector3 player, AttackHazard[] into);
}

public static class BossExecutors
{
    public const int MaxHazards = 8;

    static IBossAttackExecutor[] table;

    static IBossAttackExecutor[] Table
    {
        get
        {
            if (table != null) return table;
            table = new IBossAttackExecutor[System.Enum.GetValues(typeof(BossAttackKind)).Length + 8];
            Register(BossAttackKind.Blast, new BlastExecutor());
            Register(BossAttackKind.Strike, new StrikeExecutor());
            Register(BossAttackKind.Jet, new JetExecutor());
            Register(BossAttackKind.Wave, new WaveExecutor());
            return table;
        }
    }

    public static void Register(BossAttackKind kind, IBossAttackExecutor executor)
    {
        int i = (int)kind;
        if (table == null) { var forced = Table; }   // (builds the defaults first, then replaces)
        if (i >= table.Length) System.Array.Resize(ref table, i + 8);
        table[i] = executor;
    }

    // The executor of a kind, or null for the projectile / laser kinds BossActor fires itself.
    public static IBossAttackExecutor For(BossAttackKind kind)
    {
        int i = (int)kind;
        var t = Table;
        return i >= 0 && i < t.Length ? t[i] : null;
    }

    public static bool IsHazardKind(BossAttackKind kind) => For(kind) != null;

    // ---- the shared body: volleys ----

    abstract class VolleyExecutor : IBossAttackExecutor
    {
        public int Arm(BossActor boss, BossAttack a, Vector3 player, AttackHazard[] into)
        {
            int n = 0;
            int volleys = Mathf.Max(1, a.volleys);
            for (int k = 0; k < volleys && n < into.Length; k++)
                ArmVolley(boss, a, k, a.tellSeconds + k * Mathf.Max(0f, a.volleyGap), player, into, ref n);
            return n;
        }

        protected abstract void ArmVolley(BossActor boss, BossAttack a, int k, float tell, Vector3 player, AttackHazard[] into, ref int n);

        protected static void Add(AttackHazard[] into, ref int n, AttackHazard h)
        {
            if (h != null && n < into.Length) into[n++] = h;
        }

        protected static int PartOf(BossAttack a, int k)
        {
            var parts = a.parts;
            if (parts == null || parts.Length == 0) return -1;
            return parts[k % parts.Length];
        }
    }

    sealed class BlastExecutor : VolleyExecutor
    {
        protected override void ArmVolley(BossActor boss, BossAttack a, int k, float tell, Vector3 player, AttackHazard[] into, ref int n)
        {
            int part = PartOf(a, 0);
            if (part < 0) return;
            Vector2 local;
            Vector2 muzzle = boss.TellMuzzle(a, part, out local);
            var spec = a.blast;
            spec.world = boss.World;
            // the crack of every other ring on the other side of the ship, so one place is not safe for both
            Vector2 target = player;
            if (k % 2 == 1) target.x = Mathf.Clamp(player.x + (player.x >= 0f ? -1f : 1f) * 1.8f, -2.1f, 2.1f);
            var blast = AttackBlast.Arm(spec, muzzle, target, tell, boss.gameObject);
            if (blast != null) blast.Follow(boss.transform, local);
            Add(into, ref n, blast);
        }
    }

    sealed class StrikeExecutor : VolleyExecutor
    {
        static readonly float[] lanes = new float[4];

        protected override void ArmVolley(BossActor boss, BossAttack a, int k, float tell, Vector3 player, AttackHazard[] into, ref int n)
        {
            var spec = a.strike;
            spec.world = boss.World;
            spec.ride = 0f;
            int count = StrikeLanes.Pick(player.x, Mathf.Clamp(a.count, 1, lanes.Length), a.spacing, BossRails.DrawnInnerEdge,
                                         spec.hitHalf > 0f ? spec.hitHalf : .18f, lanes);
            for (int i = 0; i < count; i++) Add(into, ref n, AttackStrike.Arm(spec, lanes[i], player.y, tell, boss.gameObject));
        }
    }

    sealed class JetExecutor : VolleyExecutor
    {
        protected override void ArmVolley(BossActor boss, BossAttack a, int k, float tell, Vector3 player, AttackHazard[] into, ref int n)
        {
            var parts = a.parts;
            if (parts == null) return;
            for (int i = 0; i < parts.Length; i++)
            {
                int part = parts[i];
                if (part < 0) continue;
                Vector2 local;
                Vector2 muzzle = boss.TellMuzzle(a, part, out local);
                var spec = a.jet;
                spec.world = boss.World;
                spec.ride = 0f;
                Vector2 target = player;
                if (a.aimSpreadX != 0f && parts.Length > 1) target.x += (local.x < 0f ? -1f : 1f) * a.aimSpreadX;
                var jet = AttackJet.Arm(spec, muzzle, target, tell, boss.gameObject);
                if (jet != null) jet.Follow(boss.transform, local);
                Add(into, ref n, jet);
            }
        }
    }

    sealed class WaveExecutor : VolleyExecutor
    {
        protected override void ArmVolley(BossActor boss, BossAttack a, int k, float tell, Vector3 player, AttackHazard[] into, ref int n)
        {
            int part = PartOf(a, 0);
            Vector2 local = Vector2.zero;
            Vector2 muzzle = part >= 0 ? boss.TellMuzzle(a, part, out local) : (Vector2)boss.transform.position;
            var spec = a.wave;
            spec.world = boss.World;
            spec.ride = 0f;
            // the gap of every other band on the other side of the lane
            Vector2 target = player;
            if (k % 2 == 1) target.x = -player.x;
            Add(into, ref n, AttackWave.Arm(spec, muzzle, target, tell, boss.gameObject));
        }
    }
}
