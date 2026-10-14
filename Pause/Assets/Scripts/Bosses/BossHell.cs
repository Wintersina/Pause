using System.Collections.Generic;
using UnityEngine;

// Builds each boss's bullet-hell rotation (BossDef.hell) from BossPatternTables: a pattern slot becomes a BossAttack of
// kind Pattern, a legacy slot copies the boss's existing attack of that name (a laser, a vine lash, a flame sweep...) with
// the slot's phase and the ladder's pace. All numbers live in BossPatternTables / BossLadder.
public static class BossHell
{
    public static BossAttack[] TableFor(BossDef boss, int world)
    {
        var slots = BossPatternTables.For(world);
        if (slots == null || slots.Length == 0) return null;   // no bullet-hell rotation yet: the boss keeps its old table
        var table = new BossAttack[slots.Length];
        float pace = BossLadder.CooldownFor(world);
        for (int i = 0; i < slots.Length; i++)
        {
            var sl = slots[i];
            if (sl.pattern != null)
            {
                var names = new List<string>();
                foreach (var st in sl.pattern.streams)
                    foreach (var n in st.emitters) if (!names.Contains(n)) names.Add(n);
                foreach (var st in sl.pattern.streams) st.parts = BossEmitters.Resolve(boss, st.emitters);
                table[i] = new BossAttack
                {
                    name = sl.name, kind = BossAttackKind.Pattern, tell = sl.pose, tellSeconds = sl.tell, fireFrame = sl.fireFrame,
                    emitters = names.ToArray(), pattern = sl.pattern, cooldown = sl.cooldown * pace, minPhase = sl.minPhase,
                };
            }
            else
            {
                var src = Find(boss, sl.legacy);
                if (src == null) throw new System.InvalidOperationException("boss " + boss.artKey + ": no legacy attack named " + sl.legacy);
                var a = src.Copy();
                a.minPhase = sl.minPhase;
                a.tellSeconds = sl.tell;
                a.cooldown = a.cooldown * pace;
                table[i] = a;
            }
        }
        return table;
    }

    static BossAttack Find(BossDef boss, string name)
    {
        if (boss.themed != null) foreach (var a in boss.themed) if (a.name == name) return a;
        foreach (var a in boss.DefaultAttacks) if (a.name == name) return a;
        return null;
    }
}
