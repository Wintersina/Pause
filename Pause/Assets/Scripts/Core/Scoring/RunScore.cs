using System;
using UnityEngine;

// The run score: what the pilot did this run, in points (ScoreRules has every
// value). It accumulates for the whole run -- across worlds, portals and
// bosses -- and only a new run (BeginRun) starts it again from zero.
//
// Like StarDustLedger, it is a static record with a run token, so it survives
// whatever order scenes are torn down in:
//
//   score.Awake     BeginRun (tutorial: not scoring; developer mode: scoring,
//                   but never saved as best)
//   score.Update    Tick (distance + the kill-chain clock) on running frames;
//                   EndRun the moment the player dies
//   score.OnDestroy EndRun (Menu, Replay, Back, any scene change)
//   backgrounding   Stage (PrefsSaverRunner): the best so far is written in
//                   case the OS kills the app; the run stays open
//
// Banking writes BestScore = max(saved, this run), so banking again -- or a
// Stage before it -- can never count a run twice. EndRun banks once per run
// (BankCount) and later calls with the same token do nothing.
//
// Gameplay reports events through the On* methods; each is a no-op when no
// run is scoring (menus, the tutorial, edit-mode tests that never began one).
public static class RunScore
{
    public const string BestScoreKey = "BestScore";

    public enum Source { Distance, Kill, Dust, Atom, Teleport, Boss, World, Elite }

    // Totals stay small on purpose: see ScoreRules (speed multiplier on
    // flight + kills, loop scaling on boss + world bonuses).

    // Points and counts per source, for the death panel's breakdown.
    public struct Breakdown
    {
        public long distance, kills, dust, atoms, teleports, bosses, worlds;
        public int killCount, dustCount, atomCount, teleportCount, bossCount, worldCount;
        public int bestChain;
        // Times LOOP BACK's portal was flown (RunLoop.Index at the end).
        public int loops;
        // The highest multiplier any points were earned at: speed on flight,
        // chain x speed on kills (within ScoreRules.MaxTotalMultiplier).
        public float bestMultiplier;

        public long Total { get { return distance + kills + dust + atoms + teleports + bosses + worlds; } }
    }

    // A scoring event worth showing at its source (HUD "+N" popups).
    // `at` is a world position, or NaN for "middle of the screen".
    public static event Action<int, Vector3, Source> Scored;

    static int runId;
    static bool scoring;      // this run earns points at all (not the tutorial)
    static bool savesBest;    // ... and may be saved as best / ranked (not developer mode)
    static bool ended;
    static int bestAtStart;
    static double distance;   // fractional distance points
    static Breakdown parts;
    static int chain;
    static float chainLeft;
    static int teleportsThisWorld;

    // Run ends banked (tests, diagnostics).
    public static int BankCount { get; private set; }

    public static bool Scoring { get { return scoring; } }
    public static bool SavesBest { get { return savesBest; } }
    public static bool Ended { get { return ended; } }
    public static int RunId { get { return runId; } }
    public static int BestAtStart { get { return bestAtStart; } }
    public static int SavedBest { get { return PlayerPrefs.GetInt(BestScoreKey, 0); } }

    public static long Total
    {
        get
        {
            var b = parts;
            b.distance = (long)Math.Floor(distance);
            return b.Total;
        }
    }

    public static Breakdown Parts
    {
        get
        {
            var b = parts;
            b.distance = (long)Math.Floor(distance);
            return b;
        }
    }

    // The live chain; none once the run has ended (the HUD badge clears on death).
    public static int Chain { get { return !ended && chainLeft > 0f ? chain : 0; } }
    public static int Multiplier { get { return ScoreRules.MultiplierFor(Chain); } }
    // The speed tier right now (HUD badge); x1 once the run has ended.
    public static float SpeedMultiplier
    {
        get { return Live ? ScoreRules.SpeedMultiplierFor(moveBackGround.speed) : 1f; }
    }
    // 1 -> 0 as the chain window runs out (HUD fade).
    public static float ChainLeft01
    {
        get { return ScoreRules.ComboWindowSeconds <= 0f ? 0f : Mathf.Clamp01(chainLeft / ScoreRules.ComboWindowSeconds); }
    }

    // A new best this run (only ever true for a run that may save one).
    public static bool IsNewBest { get { return savesBest && Total > bestAtStart; } }

    // ---- run lifecycle ----

    // Starts a run and returns its token. A run still open (the next scene's
    // Awake beat the old scene's OnDestroy) is banked first.
    public static int BeginRun(bool scores, bool mayBeBest)
    {
        if (!ended && runId > 0) Bank();
        runId++;
        scoring = scores;
        savesBest = scores && mayBeBest;
        ended = false;
        bestAtStart = SavedBest;
        distance = 0d;
        parts = new Breakdown();
        chain = 0;
        chainLeft = 0f;
        teleportsThisWorld = 0;
        // The loop is part of the run: a new run is always on its first pass.
        RunLoop.Reset();
        return runId;
    }

    // Final bank for this run; later calls (and a stale token from an old
    // scene) do nothing. The score stays readable until the next BeginRun.
    public static void EndRun(int token)
    {
        if (token != runId || ended) return;
        Bank();
    }

    // Backgrounding / periodic: write the best so far, keep the run open.
    public static void Stage()
    {
        if (ended || !savesBest) return;
        WriteBest(false);
    }

    static void Bank()
    {
        ended = true;
        BankCount++;
        if (savesBest) WriteBest(true);
    }

    static void WriteBest(bool flush)
    {
        long total = Total;
        if (total <= SavedBest) return;
        PlayerPrefs.SetInt(BestScoreKey, (int)Math.Min(total, int.MaxValue));
        if (flush) PrefsSaver.SaveNow();
        else PrefsSaver.MarkDirty();
    }

    static bool Live { get { return scoring && !ended; } }

    // ---- per frame ----

    // `dt` is the world's (scaled) delta for a running frame, 0 while the
    // world is frozen; `speed` is moveBackGround.speed.
    public static void Tick(float dt, float speed)
    {
        if (!Live || dt <= 0f) return;
        float m = ScoreRules.SpeedMultiplierFor(speed);
        distance += ScoreRules.DistancePoints(speed, dt) * m;
        if (speed > 0f) NoteMultiplier(m);
        if (chainLeft > 0f)
        {
            chainLeft -= dt;
            if (chainLeft <= 0f) { chainLeft = 0f; chain = 0; }
        }
    }

    // ---- events ----

    // Something the pilot destroyed (collisionDetection.AwardDestroyedTarget:
    // weapons, secret powers, the ultimate, ramming while shielded, blinking
    // onto it with the pause-teleport). `bonusPoints` is added to the base
    // before the multipliers (ScoreRules.TeleportKillBonus); boss parts ignore it.
    public static int OnKill(GameObject target, int bonusPoints = 0)
    {
        if (!Live || target == null) return 0;
        string name = target.name;
        if (name.StartsWith("Boss"))
        {
            // Boss parts: a shot-down projectile is worth a little; the
            // body hitbox and lane beams nothing.
            if (!name.StartsWith("BossShot")) return 0;
            parts.kills += ScoreRules.BossShot;
            return ScoreRules.BossShot;
        }

        int basePoints = BasePoints(target) + Mathf.Max(0, bonusPoints);
        chain = chainLeft > 0f ? chain + 1 : 1;
        chainLeft = ScoreRules.ComboWindowSeconds;
        parts.bestChain = Mathf.Max(parts.bestChain, chain);
        float m = ScoreRules.Combined(ScoreRules.MultiplierFor(chain),
                                      ScoreRules.SpeedMultiplierFor(moveBackGround.speed));
        NoteMultiplier(m);
        int points = Mathf.RoundToInt(basePoints * m);
        parts.kills += points;
        parts.killCount++;
        Raise(points, target.transform.position, Source.Kill);
        return points;
    }

    // An elite ship went down (EliteShip.Die), whatever brought it down:
    // flat `points` (ScoreRules.EliteDown) counted as a kill, no chain or
    // speed multiplier, with the "ELITE DOWN" popup at `at`.
    public static int OnElite(Vector3 at, int points)
    {
        if (!Live || points <= 0) return 0;
        parts.kills += points;
        parts.killCount++;
        Raise(points, at, Source.Elite);
        return points;
    }

    public static int BasePoints(GameObject target)
    {
        var def = EnemyIdentity.Of(target);
        if (def != null) return ScoreRules.KillPoints(def.role, def.tier);
        if (target.CompareTag("Astr")) return ScoreRules.Rock;
        if (PrefabName.Is(target, EnemyRoster.AlienObjectName)) return ScoreRules.Alien;
        if (PrefabName.Is(target, EnemyRoster.MineObjectName)) return ScoreRules.Mine;
        return ScoreRules.UnknownEnemy;
    }

    // `at`: where it was caught (for the HUD popup); none -> no popup.
    public static int OnDust(bool large, Vector3? at = null)
    {
        if (!Live) return 0;
        int points = large ? ScoreRules.LargeDust : ScoreRules.SmallDust;
        parts.dust += points;
        parts.dustCount++;
        if (at.HasValue) Raise(points, at.Value, Source.Dust);
        return points;
    }

    public enum Atom { Heal, Shield, Pause, Cooldown }

    public static int OnAtom(Atom kind, Vector3? at = null)
    {
        if (!Live) return 0;
        int points = kind == Atom.Heal ? ScoreRules.HealAtom
                   : kind == Atom.Shield ? ScoreRules.ShieldAtom : ScoreRules.PauseAtom;
        parts.atoms += points;
        parts.atomCount++;
        if (at.HasValue) Raise(points, at.Value, Source.Atom);
        return points;
    }

    public static int OnTeleport(Vector3 from, Vector3 to)
    {
        if (!Live) return 0;
        if (teleportsThisWorld >= ScoreRules.TeleportsScoredPerWorld) return 0;
        int points = ScoreRules.TeleportPoints(Vector2.Distance(from, to));
        if (points <= 0) return 0;
        teleportsThisWorld++;
        parts.teleports += points;
        parts.teleportCount++;
        return points;
    }

    // The boss encounter's outcome (BossEncounter.BeginOutro).
    public static int OnBoss(bool destroyed, float secondsLeft, bool hitPointsRule, Vector3 at)
    {
        if (!Live) return 0;
        int points = ScoreRules.BossPoints(destroyed, secondsLeft, hitPointsRule, RunLoop.Index);
        parts.bosses += points;
        parts.bossCount++;
        Raise(points, at, Source.Boss);
        return points;
    }

    // Flew through the portal out of `clearedWorld` (WorldManager.Advance).
    public static int OnWorldCleared(int clearedWorld)
    {
        if (!Live) return 0;
        int points = ScoreRules.WorldClearedPoints(clearedWorld, RunLoop.Index);
        parts.worlds += points;
        parts.worldCount++;
        teleportsThisWorld = 0;
        Raise(points, new Vector3(float.NaN, float.NaN, 0f), Source.World);
        return points;
    }

    // LOOP BACK's portal was flown: the run is now on pass `loopIndex`
    // (the death panel's LOOPS line). Like every event, only while live.
    public static void OnLoop(int loopIndex)
    {
        if (!Live) return;
        parts.loops = Mathf.Max(parts.loops, loopIndex);
        teleportsThisWorld = 0;
    }

    static void NoteMultiplier(float m)
    {
        if (m > parts.bestMultiplier) parts.bestMultiplier = m;
    }

    static void Raise(int points, Vector3 at, Source source)
    {
        var handler = Scored;
        if (handler == null) return;
        try { handler(points, at, source); }
        catch (Exception e) { Debug.LogException(e); }   // a HUD problem never costs points
    }

    // Thousands separators, culture-independent: 1234567 -> "1,234,567".
    public static string Format(long points)
    {
        return points.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
