// ATOMIC FUSIONS, the pure rules (AtomFusions drives them in a run; the
// tests drive them directly). No Unity calls, no allocation after
// construction: everything is fixed arrays and value types.
//
//   PRIMED SLOTS   every atom caught puts a primed copy of it on the ship,
//                  in one of FusionTuning.MaxSlots slots, until `expires`.
//                  An instant atom (heal, pause, capacitor, Mending) stays
//                  primed FusionTuning.MoteSeconds; a timed one (shield,
//                  magnet, Hard Shell) for as long as its own effect runs --
//                  the caller keeps that expiry current (KeepTimed) and ends
//                  it when the effect ends. A catch with every slot full
//                  replaces the oldest.
//   FUSING         a catch while another atom is primed fuses the two if
//                  {primed, caught} is a recipe in AtomFusionTable (order
//                  never matters): the primed slot is consumed, the caught
//                  atom is not primed, and the fusion becomes CURRENT for its
//                  window. Several primed partners: the newest one wins.
//   CHAINING       a catch of one of the current fusion's atoms inside its
//                  window re-triggers it: chain + 1, the window restarts from
//                  now (a reset, never an addition). No cap on chains.
//   TRIPLES        a catch that completes a triple with the current fusion's
//                  atoms (or with two primed atoms) fires the triple instead
//                  of chaining or pairing -- a triple always beats its pairs.
//   EXTERNAL       VACUUM (magnet + shield) is decided by MagnetPull /
//                  MagnetCombo, which reports each start and re-chain
//                  (OnExternal); its two atoms are consumed from the slots
//                  and a catch reported in the same frame is absorbed.
//   SAME-ATOM      shield + shield, heal + heal (the latter only with
//                  FusionGate.HealAtFullHearts passed in `gates`).
public sealed class AtomFusionState
{
    public enum Outcome
    {
        None,       // nothing happened (disabled, an unknown kind)
        Primed,     // the atom is now primed on the ship
        Fused,      // a fusion fired (first time this current window)
        Chained,    // the current fusion re-triggered: Chain is now 2+
        Absorbed,   // consumed by an external fusion reported this frame
    }

    public struct Result
    {
        public Outcome outcome;
        public FusionDef fusion;   // Fused / Chained
        public int chain;          // 1 on a fresh fusion, 2+ on a chain
        public bool Fired => outcome == Outcome.Fused || outcome == Outcome.Chained;
    }

    public struct Slot
    {
        public bool used;
        public AtomKind kind;
        public float primedAt, expires;
    }

    // The current window of a fusion that has just fired (seconds from now).
    // Must be a cached delegate (no allocation per call).
    public System.Func<FusionDef, float> WindowFor;

    readonly Slot[] slots = new Slot[FusionTuning.MaxSlots];
    FusionDef current;
    float currentUntil;
    int chain;
    float externalAt = float.NegativeInfinity;

    public FusionDef Current => current;
    public float CurrentUntil => currentUntil;
    public int Chain => chain;
    public int SlotCount => slots.Length;
    public Slot SlotAt(int i) { return slots[i]; }

    public int PrimedCount(float now)
    {
        int n = 0;
        for (int i = 0; i < slots.Length; i++) if (Live(i, now)) n++;
        return n;
    }

    public bool IsPrimed(AtomKind k, float now)
    {
        for (int i = 0; i < slots.Length; i++) if (Live(i, now) && slots[i].kind == k) return true;
        return false;
    }

    public bool CurrentActive(float now) { return current != null && now < currentUntil; }

    bool Live(int i, float now) { return slots[i].used && now < slots[i].expires; }

    public void Reset()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = default;
        current = null;
        currentUntil = 0f;
        chain = 0;
        externalAt = float.NegativeInfinity;
    }

    // Drops expired slots and an expired current fusion.
    public void Expire(float now)
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i].used && now >= slots[i].expires) slots[i].used = false;
        if (current != null && now >= currentUntil) { current = null; chain = 0; }
    }

    // A timed atom's effect still runs with `remaining` seconds: its primed
    // slot (if any) stays primed exactly that long (0 ends it now).
    public void KeepTimed(AtomKind k, float remaining, float now)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].used || slots[i].kind != k) continue;
            if (remaining <= 0f) slots[i].used = false;
            else slots[i].expires = now + remaining;
        }
    }

    // The current fusion's window ends early (its shield / magnet ended).
    public void EndCurrent(FusionId id)
    {
        if (current != null && current.id == id) { current = null; chain = 0; currentUntil = 0f; }
    }

    // An atom caught at `now`; `primeSeconds` is how long it would stay
    // primed (MoteSeconds, or the timed effect's length); `gates` are
    // FusionGate bits that hold for this catch.
    public Result OnAtom(AtomKind kind, float now, float primeSeconds, int gates = 0)
    {
        Expire(now);
        var r = new Result();

        // caught in the frame an external fusion (VACUUM) consumed it
        if (now == externalAt && current != null && current.Uses(kind))
        {
            r.outcome = Outcome.Absorbed;
            r.fusion = current;
            r.chain = chain;
            return r;
        }

        // 1. a triple with the current fusion's atoms
        if (CurrentActive(now) && current.atoms.Length == 2)
        {
            var triple = TripleWith(current.atoms[0], current.atoms[1], kind, gates);
            if (triple != null) return Fire(triple, now, 1, -1, -1);
        }
        // 2. a triple with two primed atoms
        int a = -1, b = -1;
        for (int i = 0; i < slots.Length && a < 0; i++)
        {
            if (!Live(i, now)) continue;
            for (int j = i + 1; j < slots.Length; j++)
            {
                if (!Live(j, now)) continue;
                var triple = TripleWith(slots[i].kind, slots[j].kind, kind, gates);
                if (triple != null) { a = i; b = j; return Fire(triple, now, 1, a, b); }
            }
        }
        // 3. chaining the current fusion
        if (CurrentActive(now) && current.Uses(kind) && !current.external && Allowed(current, gates))
        {
            chain++;
            currentUntil = now + Window(current);
            r.outcome = Outcome.Chained;
            r.fusion = current;
            r.chain = chain;
            return r;
        }
        // 4. a pair with the newest primed partner
        int best = -1;
        FusionDef pair = null;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!Live(i, now)) continue;
            var f = AtomFusionTable.Find(AtomFusionTable.Recipe(slots[i].kind, kind));
            if (f == null || f.external || !Allowed(f, gates)) continue;
            if (best < 0 || slots[i].primedAt >= slots[best].primedAt) { best = i; pair = f; }
        }
        if (pair != null) return Fire(pair, now, 1, best, -1);

        // 5. no fusion: prime it
        Prime(kind, now, primeSeconds);
        r.outcome = Outcome.Primed;
        return r;
    }

    // VACUUM (or any external row) started (`chainIn` 1) or re-chained
    // (2+), as its owner counts it. Its atoms are consumed from the slots;
    // with a current pair that completes a triple, the triple fires instead,
    // and a current triple that contains it is re-triggered.
    public Result OnExternal(FusionId id, int chainIn, float now)
    {
        Expire(now);
        var f = AtomFusionTable.Get(id);
        externalAt = now;
        for (int i = 0; i < slots.Length; i++)
            if (Live(i, now) && f.Uses(slots[i].kind)) slots[i].used = false;

        var r = new Result();
        if (CurrentActive(now) && current != f)
        {
            // a current triple holding this pair: re-trigger it
            if (current.Triple && ContainsAll(current, f))
            {
                chain++;
                currentUntil = now + Window(current);
                r.outcome = Outcome.Chained; r.fusion = current; r.chain = chain;
                return r;
            }
            // a current pair that, with this one, makes a triple
            if (current.atoms.Length == 2)
            {
                var triple = TripleOfPairs(current, f);
                if (triple != null) return Fire(triple, now, 1, -1, -1);
            }
        }
        current = f;
        chain = UnityEngine.Mathf.Max(1, chainIn);
        currentUntil = now + Window(f);
        r.outcome = chain > 1 ? Outcome.Chained : Outcome.Fused;
        r.fusion = f;
        r.chain = chain;
        return r;
    }

    Result Fire(FusionDef f, float now, int newChain, int slotA, int slotB)
    {
        if (slotA >= 0) slots[slotA].used = false;
        if (slotB >= 0) slots[slotB].used = false;
        current = f;
        chain = newChain;
        currentUntil = now + Window(f);
        return new Result { outcome = Outcome.Fused, fusion = f, chain = chain };
    }

    void Prime(AtomKind kind, float now, float seconds)
    {
        int free = -1, oldest = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!Live(i, now)) { if (free < 0) free = i; continue; }
            if (slots[i].primedAt < slots[oldest].primedAt || !Live(oldest, now)) oldest = i;
        }
        int at = free >= 0 ? free : oldest;
        slots[at] = new Slot { used = true, kind = kind, primedAt = now, expires = now + UnityEngine.Mathf.Max(0f, seconds) };
    }

    float Window(FusionDef f)
    {
        float w = WindowFor != null ? WindowFor(f) : f.seconds;
        return UnityEngine.Mathf.Max(0f, w);
    }

    static bool Allowed(FusionDef f, int gates)
    {
        return f.gate == FusionGate.None || (gates & (int)f.gate) != 0;
    }

    static FusionDef TripleWith(AtomKind a, AtomKind b, AtomKind c, int gates)
    {
        if (a == b || b == c || a == c) return null;
        var f = AtomFusionTable.Find(AtomFusionTable.Recipe(a, b, c));
        return f != null && Allowed(f, gates) ? f : null;
    }

    static bool ContainsAll(FusionDef outer, FusionDef inner)
    {
        for (int i = 0; i < inner.atoms.Length; i++) if (!outer.Uses(inner.atoms[i])) return false;
        return true;
    }

    // Two pairs sharing one atom make a triple (shield+heal and magnet+shield -> magnet+shield+heal).
    static FusionDef TripleOfPairs(FusionDef p, FusionDef q)
    {
        if (p.SameAtom || q.SameAtom) return null;
        int mask = AtomFusionTable.Bit(p.atoms[0]) | AtomFusionTable.Bit(p.atoms[1]) |
                   AtomFusionTable.Bit(q.atoms[0]) | AtomFusionTable.Bit(q.atoms[1]);
        int bits = 0;
        for (int m = mask; m != 0; m &= m - 1) bits++;
        if (bits != 3) return null;
        return AtomFusionTable.Find(mask | (3 << 8));
    }
}
