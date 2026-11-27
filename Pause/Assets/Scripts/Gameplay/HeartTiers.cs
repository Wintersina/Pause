using UnityEngine;

// THREE-TIER ENEMY HEARTS, DETERMINISTIC (user, 2026-11-27: "it shouldn't be chance based, it should create exponential power growth for
// enemies as the player's ship gets more and more powerful"). An enemy, elite or boss carries up to three bars of hearts, outermost first:
//
//     CRYSTAL  ->  BLUE  ->  RED
//
// Damage removes the outer bar first; it dies only when the RED bar is empty. The bars are the VISUAL STAGES of one number, its toughness:
//
//   PlayerPower.Levels (P)   how strong the pilot's ship is right now: drones out, their core levels and fitted shot atoms, the ship's
//                            weapon upgrades and the shared rapid-fire level, each at its weight (HeartTierTuning.W*). 0 for a fresh ship.
//   total hearts             = red x G^P  (G per kind, capped at a multiple of red), recomputed for every enemy AT SPAWN (one already alive
//                            never changes). No rolls: the same P always gives the same hearts.
//   filling order            RED holds the base hearts (exactly today's), BLUE the next chunk up to its bar size (= the red count), CRYSTAL
//                            everything beyond; when that is more than the bar can draw, each crystal pip stands for several hearts
//                            (HeartShell.crystalPip), so the display stays readable.
//
// A weak ship meets red-only enemies, a mid ship blue+red, a strong ship always crystal. The red hearts are the owner's own quarter pool
// (EnemyHearts quarters, EliteShip.heartQuarters, BossEncounter damage/hp); blue and crystal are a HeartShell in front of it. A hit's quarter
// units go through shell.Absorb first and only the LEFTOVER reaches the red pool (OVERFLOW CARRIES). Loops multiply on top (LoopDifficulty,
// capped at loop 6). EVERY number lives in HeartTierTuning.
public enum HeartLayer { Red = 0, Blue = 1, Crystal = 2 }
public enum HeartTierKind { Enemy = 0, Elite = 1, Boss = 2 }

public static class HeartTierTuning
{
    // ---- PlayerPower weights: one power LEVEL per ... (the table; P = 0 for a fresh ship) ----
    public const float WDrone = 1f;          // each active drone
    public const float WCore = .125f;        // each core level of an active drone (max 4 each)
    public const float WAtom = .02f;         // each shot-atom level fitted to an active drone (Twin, Split, Ricochet, Pierce, Arc: max 10 each)
    public const float WShip = .15f;         // each weapon-upgrade level of the equipped ship (ShipWeaponUpgrades)
    public const float WRapid = .1f;         // each level of the shared rapid-fire atom (max 10)
    public const int CoreLevelMax = 4;
    public const float PowerFull = 16f;      // P of a maxed ship: 6 drones (6) + their cores (3) + atoms (6) + ship (~.6) + rapid (1); RunPower.Score01 = P / this

    // ---- toughness: total hearts = red x G^P, per kind (a boss or elite a little steeper), capped at a multiple of red ----
    public const float GEnemy = 1.14f, GElite = 1.15f, GBoss = 1.16f;     // maxed ship (P ~16.6): enemy x8.8, elite x10.5 -> capped, boss capped
    public const float EnemyMultCap = 12f, EliteMultCap = 3f, BossMultCap = 2.5f;
    // (an elite's and a boss's red bar is already 6..13 hearts, so their multiple is capped low: the fight stays inside its clock)

    // ---- threat: ThreatScale(P) = T^P, capped. Read by the enemy-threat code (fire rate, shot speed, extras on crystal hulls). ----
    public const float ThreatPerLevel = 1.07f, ThreatCap = 3f;            // maxed ship ~x3
    public const float ThreatFireCap = 1.6f;                              // the enemy volley gap is divided by min(ThreatScale, this)

    // ---- hazards scale with power (HazardScaling): rocks and mines spawn up to HazardRateMax more often (x1.5 at full power),
    // and the whole spawn-rate multiplier (PowerCreep x this) is capped at HazardRateCap so a dodge line always exists. Hazards have
    // no hearts (one hit kills them), so only their rate scales. Rails, rail mines on rides and elite docks are authored: untouched. ----
    public const float HazardRateMax = .5f, HazardRateCap = 2.4f;

    // ---- shield atoms thin out as the squad grows: x1 with no drone bonus, ShieldMin (never zero) at a full squad ----
    public const float ShieldMin = .4f;

    // ---- heals thin out as power rises (HealScaling): the green atom's chance and clock, the bank's size and the healing drone's
    // restore delay all follow one rate, x1 at no power down to HealMin (never zero) at full power ----
    public const float HealMin = .35f;

    // ---- hazards hit harder at high power (HazardDamage): a hazard hit costs 1 heart at no power, rising to 1 + HazardExtraHearts on
    // average at full power (carried in quarter hearts, so the sequence is 1,1,2,1,2,... never random), rocks fall up to
    // HazardSpeedMax faster, and a rock slot adds a second rock with a chance up to HazardBurstMax. Every rock still goes through the
    // spawn-space check, so a dodge gap always exists; rail mines, rides and elite docks are authored and untouched. ----
    public const float HazardExtraHearts = .75f, HazardSpeedMax = .2f, HazardBurstMax = .35f;

    // ---- LOOPS (LoopDifficulty): from the second pass on everything gets harder, exponentially: D(index) = LoopBase ^ index, index 0 (the
    // first pass) = exactly 1, so the first pass is identical to before. Every use below is CAPPED, and nothing grows past loop 6
    // (index 5): a late loop is brutal, never impossible and never a frame-rate problem. ----
    public const float LoopBase = 1.4f;
    public const int LoopMaxIndex = 5;
    public const int EnemyHeartCap = 8;                  // red hearts on an ordinary enemy (the orbit draws at most 8)
    public const float FireRateCap = 2f;                 // enemy volley gap divided by min(D, this)
    public const float MinVolleyGap = .15f;              // seconds; nothing shortens a volley gap below it
    public const float ShotSpeedCap = 1.4f;              // enemy shot speed x min(sqrt(D), this)
    public const int ShotBonusPerD = 3, ShotAliveCap = 14;   // extra shots alive = round((D-1) x 3), never more than 14 alive in all
    public const float DensityCap = 1.8f;               // extra spawn density = min(sqrt(D), this); EnemyDensity.MaxThreats still caps enemies alive
    public const float BossRateCap = 2f, BossCooldownMin = .3f;   // boss cooldowns divided by min(D, 2), never under x0.3
    public const float HazardLoopRateMax = 1.8f, HazardLoopRateCap = 3.2f;   // extra hazard rate cap by loop; total rate cap from index 1
    public const float HazardCostPerLoop = .15f, HazardCostCap = 2f;       // hearts a hazard hit costs on average: capped at 2
    public const float RecoverMin = .2f;                // heals and shields x max(this, 1/D): never zero
    public const float KillScorePerLoop = .5f; public const int KillScoreLoopCap = 5;   // kill points x (1 + .5 x index)

    // ---- drone kills pay less as the squad grows (DroneScoring): DroneScoreOne with one drone, DroneScoreMin at DampFullSquad drones. Drone
    // kills extend the kill chain but cannot lift its multiplier past the top step by themselves (a squad of 2+ needs
    // DroneChainUnlockKills of the pilot's own kills in the chain first). The player's own ship kills are unchanged. ----
    public const int DampFullSquad = 6;
    public const float DroneScoreOne = .5f, DroneScoreMin = .2f;
    public const int DroneChainUnlockKills = 2;

    public static float GrowthFor(HeartTierKind kind) { return kind == HeartTierKind.Boss ? GBoss : kind == HeartTierKind.Elite ? GElite : GEnemy; }
    public static float MultCap(HeartTierKind kind) { return kind == HeartTierKind.Boss ? BossMultCap : kind == HeartTierKind.Elite ? EliteMultCap : EnemyMultCap; }
}

// The pilot's ship strength as POWER LEVELS (0 for a fresh ship), and what grows exponentially from it.
public static class PlayerPower
{
    // Tests: stand in for P (negative: measure the ship).
    public static float Override = -1f;

    public static float Levels
    {
        get
        {
            if (Override >= 0f) return Override;
            if (startMenu.youAreInTutorial || !HeartTierRun.Live) return 0f;
            int active = 0, cores = 0, atoms = 0;
            for (int d = 0; d < DroneSquad.Slots; d++)
            {
                if (!DroneSquad.IsActive(d)) continue;
                active++;
                cores += Mathf.Min(DroneSquad.CoreLevel(d), HeartTierTuning.CoreLevelMax);
                for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) atoms += ShotAtoms.Level(d, (ShotAtom)a);
            }
            return LevelsFor(active, cores, atoms, ShipWeaponUpgrades.Level(ShipId.Equipped()), ShotAtoms.Level(ShotAtom.Rapid));
        }
    }

    // The pure sum (tests): active drones, their core level sum, their fitted atom level sum, the ship's weapon level, the rapid level.
    public static float LevelsFor(int drones, int coreSum, int atomSum, int shipLevel, int rapidLevel)
    {
        return HeartTierTuning.WDrone * Mathf.Max(0, drones) + HeartTierTuning.WCore * Mathf.Max(0, coreSum) + HeartTierTuning.WAtom * Mathf.Max(0, atomSum) +
               HeartTierTuning.WShip * Mathf.Max(0, shipLevel) + HeartTierTuning.WRapid * Mathf.Max(0, rapidLevel);
    }

    // Exponential growth of toughness: G^P (uncapped; see TotalHearts for the caps).
    public static float Growth(float levels, HeartTierKind kind) { return Mathf.Pow(HeartTierTuning.GrowthFor(kind), Mathf.Max(0f, levels)); }

    // PUBLIC, STABLE: the shared threat factor T^P, capped (1 for a fresh ship). The enemy-threat code reads this for fire rate, shot
    // speed and so on, and keys its crystal extras off EnemyHearts.HasCrystal / HeartShell.Tier (what the enemy actually carries).
    public static float ThreatScaleFor(float levels) { return Mathf.Min(HeartTierTuning.ThreatCap, Mathf.Pow(HeartTierTuning.ThreatPerLevel, Mathf.Max(0f, levels))); }
    public static float ThreatScale { get { return ThreatScaleFor(Levels); } }
    // Enemy shot speed multiplier from threat (sqrt, capped like the loop one).
    public static float ShotSpeedScale { get { return Mathf.Min(Mathf.Sqrt(ThreatScale), HeartTierTuning.ShotSpeedCap); } }

    // Total hearts (red + blue + crystal) of an entity with `red` red hearts at power `levels`: red x G^P, never under red, capped.
    // `loopD` (LoopDifficulty.Current) multiplies an elite's or boss's growth on a later loop; an ordinary enemy's red hearts already carry it.
    public static int TotalHearts(HeartTierKind kind, int red, float levels, float loopD = 1f)
    {
        red = Mathf.Max(1, red);
        float loop = kind == HeartTierKind.Enemy ? 1f : Mathf.Max(1f, loopD);
        float want = red * Mathf.Min(HeartTierTuning.MultCap(kind), Growth(levels, kind) * loop);
        return Mathf.Max(red, Mathf.RoundToInt(want));
    }
}

// 0..1 views of the same strength, for the systems that scale smoothly with it (hazards, heals, shields).
public static class RunPower
{
    // Tests: stand in for the 0..1 score (negative: measure the run).
    public static float Override = -1f;

    public static float Score01
    {
        get
        {
            if (Override >= 0f) return Mathf.Clamp01(Override);
            return Mathf.Clamp01(PlayerPower.Levels / HeartTierTuning.PowerFull);
        }
    }

    // Public, stable (read by other systems): the squad's drone count, and its 0..1 term (what thins the shield atoms).
    public static int ActiveDrones { get { return DroneSquad.ActiveCount; } }
    public static float Drones01
    {
        get
        {
            if (Override >= 0f) return Mathf.Clamp01(Override);
            if (startMenu.youAreInTutorial || !HeartTierRun.Live) return 0f;
            return Mathf.Clamp01(DroneSquad.ActiveCount / (float)HeartTierTuning.DampFullSquad);
        }
    }
}

// What the run's power does outside the hearts (all numbers in HeartTierTuning).
public static class HazardScaling
{
    // Extra hazard spawn rate at this power (>= 1), before the cap.
    public static float RateFor(float power01) { return 1f + HeartTierTuning.HazardRateMax * Mathf.Clamp01(power01); }

    // The factor rock and mine spawn delays are divided by on top of PowerCreep.SpawnScale: never pushes the two together past HazardRateCap.
    public static float ExtraRate(float power01, float existingScale) { return ExtraRate(power01, existingScale, 1f, 0); }

    public static float ExtraRate(float power01, float existingScale, float loopD, int loopIndex)
    {
        float want = RateFor(power01) * Mathf.Min(Mathf.Max(1f, loopD), HeartTierTuning.HazardLoopRateMax);
        float room = (loopIndex > 0 ? HeartTierTuning.HazardLoopRateCap : HeartTierTuning.HazardRateCap) / Mathf.Max(1f, existingScale);
        return Mathf.Max(1f, Mathf.Min(want, room));
    }

    public static float ExtraRateNow { get { return ExtraRate(RunPower.Score01, PowerCreep.SpawnScale, LoopDifficulty.Current, RunLoop.Index); } }

    // Hazard fall speed multiplier, burst chance (a second rock in a rock slot) at this power.
    public static float SpeedFor(float power01) { return 1f + HeartTierTuning.HazardSpeedMax * Mathf.Clamp01(power01); }
    public static float BurstChanceFor(float power01) { return HeartTierTuning.HazardBurstMax * Mathf.Clamp01(power01); }
    public static float SpeedNow { get { return SpeedFor(RunPower.Score01); } }
    public static float BurstChanceNow { get { return BurstChanceFor(RunPower.Score01); } }

    // Shield atom spawn clock multiplier: 1 with no squad, ShieldMin with a full one, never zero.
    public static float ShieldRateFor(float drones01) { return Mathf.Lerp(1f, HeartTierTuning.ShieldMin, Mathf.Clamp01(drones01)); }
    public static float ShieldRate { get { return ShieldRateFor(RunPower.Drones01) * LoopDifficulty.RecoverScale; } }
}

// Heals (the green atom, the heal bank, the healing drone) get rarer and slower as the run's power rises.
public static class HealScaling
{
    public static float RateFor(float power01) { return Mathf.Lerp(1f, HeartTierTuning.HealMin, Mathf.Clamp01(power01)); }
    public static float Rate { get { return RateFor(RunPower.Score01) * LoopDifficulty.RecoverScale; } }
    // The heal bank's size now (never below 1, HealBank.Max at no power).
    public static int BankMax(int max, float power01) { return Mathf.Max(1, Mathf.CeilToInt(max * RateFor(power01) - 1e-4f)); }
    // The healing drone waits this much longer to restore a heart (x1 at no power).
    public static float DelayScale { get { return 1f / Mathf.Max(.05f, Rate); } }
}

// What a hazard (rock or mine) costs the ship's hearts. Whole hearts, carried in quarters.
public static class HazardDamage
{
    static int bankQuarters;
    public static void Reset() { bankQuarters = 0; }

    // Hearts one hazard hit costs at this power, on average (1 .. 1 + HazardExtraHearts).
    public static float HeartsFor(float power01) { return HeartsFor(power01, 0); }

    // On a later loop a hazard hits harder still, but never past HazardCostCap (2) hearts on average.
    public static float HeartsFor(float power01, int loopIndex)
    {
        float h = 1f + HeartTierTuning.HazardExtraHearts * Mathf.Clamp01(power01);
        if (loopIndex > 0) h = Mathf.Min(HeartTierTuning.HazardCostCap, h * (1f + HeartTierTuning.HazardCostPerLoop * loopIndex));
        return h;
    }

    // The cost of this hit: always at least 1, the extra quarters carried until they make a whole heart. Not a hazard: 1.
    public static int Cost(bool isHazard, float power01) { return Cost(isHazard, power01, 0); }

    public static int Cost(bool isHazard, float power01, int loopIndex)
    {
        if (!isHazard) return 1;
        bankQuarters += Mathf.RoundToInt(HeartsFor(power01, loopIndex) * HeartDamage.PerHeart);
        int cost = Mathf.Max(1, bankQuarters / HeartDamage.PerHeart);
        bankQuarters -= cost * HeartDamage.PerHeart;
        return cost;
    }

    public static bool IsHazard(GameObject go)
    {
        if (go == null) return false;
        var def = EnemyIdentity.Of(go);
        return def != null ? def.IsHazard : go.CompareTag("Astr");
    }

    public static int Cost(GameObject go) { return Cost(IsHazard(go), RunPower.Score01, RunLoop.Index); }
}

// A big squad pays less per kill and cannot lift the chain to the top multiplier on its own.
public static class DroneScoring
{
    // Set by ShipAttackHits around a kill made by a drone's shot (HitKind.Drone).
    public static bool InProgress;

    public static float Squad01(int active) { return Mathf.Clamp01((active - 1) / (float)(HeartTierTuning.DampFullSquad - 1)); }
    // Points scale of a drone kill (elites too): DroneScoreOne with one drone, falling in a line to DroneScoreMin at DampFullSquad
    // (1 -> .5, 2 -> .44, 3 -> .38, 4 -> .32, 5 -> .26, 6 -> .2). The pilot's own kills are never scaled.
    public static float Scale(int active) { return Mathf.Lerp(HeartTierTuning.DroneScoreOne, HeartTierTuning.DroneScoreMin, Squad01(active)); }
    public static bool CapsChain(int active) { return active >= 2; }

    // The chain length a drone-extended chain is scored at: capped just under the top step until the pilot's own kills in it unlock it.
    public static int EffectiveChain(int chain, int ownKills, int active)
    {
        if (!CapsChain(active) || ownKills >= HeartTierTuning.DroneChainUnlockKills) return chain;
        var t = ScoreRules.ComboThresholds;
        return Mathf.Min(chain, t[t.Length - 1] - 1);
    }
}

// Decides the bars of each new entity. DETERMINISTIC: no rolls.
public static class HeartTierRun
{
    // Tests: force a stage for every plan (negative: from PlayerPower): 1 = blue bar full, 2 = blue and crystal bars full.
    public static int ForceTier = -1;

    // Only a real run has power (set by BeginRun; the tutorial, menus and editor-mode fixtures stay red-only unless a test forces it).
    public static bool Live;
    public static void BeginRun(bool live = true) { HazardDamage.Reset(); Live = live && !startMenu.youAreInTutorial; }

    // The shell for a new entity of `kind` with `red` red hearts, at the current power and loop.
    public static HeartShell Plan(HeartTierKind kind, int red)
    {
        red = Mathf.Max(1, red);
        if (ForceTier >= 0) return HeartShell.FromTotal(red, red * (1 + Mathf.Clamp(ForceTier, 0, 2)));
        return HeartShell.FromTotal(red, PlayerPower.TotalHearts(kind, red, PlayerPower.Levels, LoopDifficulty.Current));
    }
}

// The blue and crystal bars in front of an entity's red hearts.
public struct HeartShell
{
    public int blueMax, crystalMax;      // hearts per bar (0: no such bar)
    public int blueQ, crystalQ;          // quarter units left (true health)
    public int crystalPip;               // hearts one drawn crystal pip stands for (1 unless the bar is bigger than the red count)

    public int Tier { get { return crystalMax > 0 ? 2 : blueMax > 0 ? 1 : 0; } }
    public bool Any { get { return blueQ + crystalQ > 0; } }
    public HeartLayer Layer { get { return crystalQ > 0 ? HeartLayer.Crystal : blueQ > 0 ? HeartLayer.Blue : HeartLayer.Red; } }
    public int QuartersLeft { get { return blueQ + crystalQ; } }
    // Quarter units left in the bar now showing, in TRUE health (0 when only red is left).
    public int LayerQuarters { get { return crystalQ > 0 ? crystalQ : blueQ; } }
    // ... and as DRAWN: a crystal pip is worth crystalPip hearts, so the orbit shows ceil(units / pip).
    public int DisplayQuarters { get { return crystalQ > 0 ? (crystalQ + Mathf.Max(1, crystalPip) - 1) / Mathf.Max(1, crystalPip) : blueQ; } }
    public int LayerMaxHearts { get { return crystalQ > 0 ? crystalMax : blueQ > 0 ? blueMax : 0; } }

    // Fills red (the base), then blue up to the red count, then crystal with the rest. total <= red: red only.
    public static HeartShell FromTotal(int red, int total)
    {
        var s = new HeartShell { crystalPip = 1 };
        int extra = Mathf.Max(0, total - Mathf.Max(1, red));
        s.blueMax = Mathf.Min(extra, Mathf.Max(1, red));
        s.crystalMax = extra - s.blueMax;
        s.blueQ = s.blueMax * HeartDamage.PerHeart;
        s.crystalQ = s.crystalMax * HeartDamage.PerHeart;
        s.crystalPip = s.crystalMax > Mathf.Max(1, red) ? Mathf.CeilToInt(s.crystalMax / (float)Mathf.Max(1, red)) : 1;
        return s;
    }

    // Spends `units` on the outer bars first and returns what is left over for the red hearts. `broken` gets the number of bars
    // emptied by this hit (0..2).
    public int Absorb(int units, out int broken)
    {
        broken = 0;
        if (units <= 0) return 0;
        if (crystalQ > 0)
        {
            int take = Mathf.Min(units, crystalQ);
            crystalQ -= take; units -= take;
            if (crystalQ == 0) broken++;
        }
        if (units > 0 && blueQ > 0)
        {
            int take = Mathf.Min(units, blueQ);
            blueQ -= take; units -= take;
            if (blueQ == 0) broken++;
        }
        return units;
    }

    // Colour of the break flash for a layer.
    public static Color FlashColour(int layer)
    {
        return layer == 2 ? new Color(.86f, .88f, 1f, 1f) : layer == 1 ? new Color(.35f, .75f, 1f, 1f) : Color.white;
    }
}

// The heart art, loaded by name from Resources/Vfx/Hearts: heart_<red|blue|crystal>_idle_0..5 (a looping flipbook), heart_<tier>_break_0..5
// (played once, from a pool, when a whole heart of that bar is lost or the bar breaks) and heart_empty. Missing frames fall back to the
// player's lifeHeart so nothing ever draws blank. (The blue and crystal frames shipped first are PLACEHOLDER recolours.)
public static class HeartArt
{
    public const string Folder = "Vfx/Hearts/";
    public const string FallbackPath = "Vfx/lifeHeart";
    public const int Frames = 6;
    public const float IdleFps = 8f;
    public static readonly string[] Tiers = { "red", "blue", "crystal" };
    static Sprite[][] idle, brk;
    static Sprite empty, fallback;

    static Sprite Fallback { get { if (fallback == null) fallback = Resources.Load<Sprite>(FallbackPath); return fallback; } }

    static Sprite[] Load(string kind, int layer)
    {
        var a = new Sprite[Frames];
        for (int i = 0; i < Frames; i++)
        {
            a[i] = Resources.Load<Sprite>(Folder + "heart_" + Tiers[layer] + "_" + kind + "_" + i);
            if (a[i] == null) a[i] = Fallback;
        }
        return a;
    }

    public static Sprite[] Idle(int layer)
    {
        if (idle == null) idle = new Sprite[3][];
        layer = Mathf.Clamp(layer, 0, 2);
        if (idle[layer] == null) idle[layer] = Load("idle", layer);
        return idle[layer];
    }

    public static Sprite[] Break(int layer)
    {
        if (brk == null) brk = new Sprite[3][];
        layer = Mathf.Clamp(layer, 0, 2);
        if (brk[layer] == null) brk[layer] = Load("break", layer);
        return brk[layer];
    }

    public static Sprite Empty { get { if (empty == null) empty = Resources.Load<Sprite>(Folder + "heart_empty"); return empty; } }
    public static Sprite Full(int layer) { return Idle(layer)[0]; }
    public const float BreakSeconds = .42f;
    public static int BreakFrame(float age) { return Mathf.Clamp(Mathf.FloorToInt(age / BreakSeconds * Frames), 0, Frames - 1); }
    public static int IdleFrame(float clock, int heart) { return ((int)(clock * IdleFps) + heart) % Frames; }

    // Builds every layer's frames, outlines and quarter sets now (a run start), so no first break stalls.
    public static void Warm()
    {
        for (int l = 0; l < 3; l++)
        {
            var f = Idle(l); Break(l);
            HeartQuarterFrames.For(f[0]);
        }
    }
}
