using UnityEngine;

// Home-screen traffic, part 3: every ship flies all of its skins.
//
// Each flight wears a random one of its ship's ShipSkins.PerShip skins
// (Stock or a colourway, owned or not -- the home screen is a showroom, so
// an unbought skin flying past is a teaser). The hull, its exhaust plumes or
// spin ring (ExhaustRemap in that skin) and its crash fragments (the skin's
// wreck drawing, cut by DeathCrash.CutFragments) all follow the flight's
// skin; the ultimates are drawn per ship (WeaponStyleTable), as in play.
//
// Skin sheets are big (one PNG per ship and skin, decoded and compressed by
// ShipHullArt), so the traffic keeps a small wardrobe: each ship has at most
// one decoded colourway ("resident") besides its stock sheet. They are
// decoded one at a time on a slow schedule -- a warm-up pass at the start,
// then every RotateEvery seconds one ship that is not in the air swaps its
// colourway for another -- so over a minute or two every ship shows several
// skins, memory stays at one extra sheet per ship, and a frame never
// decodes more than one sheet. Everything is cut and cached when it is
// decoded; a launch only picks from what is already loaded, so spawning
// allocates nothing. Leaving the home screen frees the wardrobe
// (ShipHullArt.ReleaseUnusedSkins on the scene load, and OnDestroy here).
public partial class TitleScreenTraffic
{
    [Tooltip("Seconds between wardrobe swaps (one ship's colourway decoded) once warmed up.")]
    public float skinRotateEvery = 6f;
    public const float SkinWarmStep = .35f;     // between decodes while warming up
    public const float SkinWarmStart = 1.2f;    // let the screen settle first
    public const float ResidentShare = .75f;    // flights wearing the colourway, not stock

    float nextSkinWorkAt;
    readonly bool[] skinSeen = new bool[(ShipId.Count + 1) * ShipSkins.PerShip];

    // Heavy wardrobe steps so far (a decode or a fragment cut). Tests use it
    // to tell those scheduled frames apart from ordinary ones.
    public int SkinWork { get; private set; }
    public int SkinSwaps { get; private set; }
    public bool SkinWorkPaused { get; set; }

    // Flights launched in (ship, skin), since Init.
    public bool SkinFlown(int id, int skin)
    {
        int k = id * ShipSkins.PerShip + skin;
        return k >= 0 && k < skinSeen.Length && skinSeen[k];
    }

    void StartSkins()
    {
        nextSkinWorkAt = SkinWarmStart;
    }

    int PickSkin(Flyer f)
    {
        int skin = f.resident > 0 && Random.value < ResidentShare ? f.resident : ShipSkins.Stock;
        if (!ShipSkins.Has(f.id, skin)) skin = ShipSkins.Stock;
        int k = f.id * ShipSkins.PerShip + skin;
        if (k >= 0 && k < skinSeen.Length) skinSeen[k] = true;
        return skin;
    }

    // One heavy step at most per call, on its schedule.
    void TickSkins()
    {
        if (SkinWorkPaused || now < nextSkinWorkAt) return;
        nextSkinWorkAt = now + SkinWarmStep;

        // warm-up 1: every ship's stock wreck cut
        for (int i = 0; i < pool.Length; i++)
            if (pool[i].stockFrags == null) { CutStock(pool[i]); return; }

        // warm-up 2: a colourway for every ship
        int n = pool.Length, start = Random.Range(0, n);
        for (int k = 0; k < n; k++)
        {
            var f = pool[(start + k) % n];
            if (f.resident == 0 && ShipSkins.CountFor(f.id) > 1) { Wear(f, RandomColourway(f.id, 0)); return; }
        }

        // then: one ship on the ground swaps its colourway
        nextSkinWorkAt = now + skinRotateEvery;
        for (int k = 0; k < n; k++)
        {
            var f = pool[(start + k) % n];
            if (f.active || f.resident == 0 || now < f.fragsBusyUntil || ShipSkins.CountFor(f.id) <= 2) continue;
            Wear(f, RandomColourway(f.id, f.resident));
            SkinSwaps++;
            return;
        }
    }

    static int RandomColourway(int id, int not)
    {
        int count = ShipSkins.CountFor(id);
        if (count <= 1) return ShipSkins.Stock;
        for (int tries = 0; tries < 8; tries++)
        {
            int s = Random.Range(1, count);
            if (s != not) return s;
        }
        return not == 1 && count > 2 ? 2 : 1;
    }

    void CutStock(Flyer f)
    {
        SkinWork++;
        Warm(f.id, ShipSkins.Stock);
        f.stockFrags = Cut(f.id, ShipSkins.Stock);
    }

    // Every drawing the traffic can show of (ship, skin), cut now: idle and
    // bank poses, healthy and wrecked (a stricken ship flies its wreck).
    static void Warm(int id, int skin)
    {
        for (int c = 0; c < ShipHullArt.Columns - 1; c++)
        {
            ShipHullArt.Get(id, skin, 0, c);
            ShipHullArt.Get(id, skin, ShipHullArt.States - 1, c);
        }
    }

    // Decodes skin `skin` of f's ship (ShipHullArt), cuts every drawing the
    // traffic shows from it, and its wreck; drops the previous colourway.
    void Wear(Flyer f, int skin)
    {
        SkinWork++;
        int old = f.resident;
        var oldFrags = f.residentFrags;
        f.resident = skin;
        Warm(f.id, skin);
        f.residentFrags = Cut(f.id, skin);
        if (oldFrags != null) oldFrags.Destroy();
        // a parked hull still holding a drawing of the old sheet takes its
        // rest drawing in the new one before that sheet goes
        if (f.skin == old && !f.active) { f.skin = skin; f.hull.sprite = ShipHullArt.Get(f.id, skin, 0, 0); }
        if (old > 0 && old != skin) Unload(f.id, old);
    }

    static DeathCrash.FragmentSet Cut(int id, int skin)
    {
        // the critical (wrecked) drawing, the one the gameplay death breaks up
        var wreck = ShipHullArt.Get(id, skin, ShipHullArt.States - 1, 0);
        return DeathCrash.CutFragments(wreck, id, Random.Range(0, DeathCrash.Variants));
    }

    // A skin sheet the player's own ship is showing stays (it's theirs).
    static void Unload(int id, int skin)
    {
        if (skin == ShipSkins.Stock || ShipSkins.Shown(id) == skin) return;
        ShipHullArt.Release(id, skin);
    }

    // The flight's wreck pieces; cut now if the warm-up hasn't got to them.
    DeathCrash.FragmentSet FragsFor(Flyer f)
    {
        var set = f.Frags;
        if (set != null) return set;
        CutStock(f);
        return f.stockFrags;
    }

    void ReleaseSkins()
    {
        if (pool == null) return;
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (f.stockFrags != null) { f.stockFrags.Destroy(); f.stockFrags = null; }
            if (f.residentFrags != null) { f.residentFrags.Destroy(); f.residentFrags = null; }
            if (f.resident > 0) Unload(f.id, f.resident);
            f.resident = 0;
        }
    }
}
