using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Every gameplay hook funnels here. Most subscribe to events that already
// exist (Codex.Discovered, EliteShip.Died, DeathCrash.MegaDominoStarted,
// RunScore.Scored, SocialBridge.SignedIn); the rest are one-line calls placed
// at the site that knows (collisionDetection, WorldManager, BossEncounter,
// shopingShips, ShipSkins, score, movePlayer, ...).
//
// Everything is O(1), cached-key and allocation free, except the rare events
// (a codex discovery, a purchase) that recount a set.
public static class AchievementTracker
{
    // The blink-dodge probe (pause_perfect_dodge): a hostile shot within
    // DodgeRadius of where the ship blinked FROM, the ship not hurt for
    // DodgeSurviveSeconds of running world time afterwards. Switch off if it
    // proves noisy.
    public static bool DodgeProbeEnabled = true;
    public const float DodgeRadius = .6f, DodgeSurviveSeconds = 1f;

    // World time for the dodge probe (a test seam).
    public static System.Func<float> Clock = () => Time.time;

    static bool enabled;
    static bool bossHurt;
    static int pausesThisWorld;
    static float dodgeAt = -1f;

    static readonly string[] WorldReached = { null, "world_frost_reached", "world_verdant_reached", "world_ember_reached", "world_tide_reached" };
    static readonly string[] BossIds = { "boss_space", "boss_frost", "boss_verdant", "boss_ember", "boss_tide" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        Enable();
        AchievementMigration.RunIfNeeded();
    }

    // Idempotent; tests call it to wire the subscriptions without Play mode.
    public static void Enable()
    {
        Disable();
        enabled = true;
        Codex.Discovered += OnCodexDiscovered;
        EliteShip.Died += OnEliteDied;
        DeathCrash.MegaDominoStarted += OnMegaDomino;
        RunScore.Scored += OnScored;
        SocialBridge.SignedIn += OnSignedIn;
        AchievementEvents.PlayerHurt += OnPlayerHurt;
        AchievementStore.Unlocked += OnUnlockedToast;
    }

    public static void Disable()
    {
        if (!enabled) return;
        enabled = false;
        Codex.Discovered -= OnCodexDiscovered;
        EliteShip.Died -= OnEliteDied;
        DeathCrash.MegaDominoStarted -= OnMegaDomino;
        RunScore.Scored -= OnScored;
        SocialBridge.SignedIn -= OnSignedIn;
        AchievementEvents.PlayerHurt -= OnPlayerHurt;
        AchievementStore.Unlocked -= OnUnlockedToast;
    }

    public static bool Enabled { get { return enabled; } }

    // A new run: per-run latches start clean.
    public static void ResetRun()
    {
        bossHurt = false;
        pausesThisWorld = 0;
        dodgeAt = -1f;
    }

    // ---- kills ----

    // Everything the pilot destroys (collisionDetection.AwardDestroyedTarget).
    public static void OnKill(GameObject target, int bonusPoints)
    {
        if (target == null || !Achievements.Real) return;
        if (target.name.StartsWith("Boss", System.StringComparison.Ordinal)) return;   // a boss part or shot, not an enemy
        if (target.CompareTag("Enimey"))
        {
            AchievementStore.AddCounter(AchievementCatalog.CKills, 1);
            if (IsMine(target)) AchievementStore.AddCounter(AchievementCatalog.CMines, 1);
        }
        else if (target.CompareTag("Astr")) AchievementStore.AddCounter(AchievementCatalog.CRocks, 1);
        if (bonusPoints > 0) AchievementStore.Unlock(AchievementCatalog.Find("pause_blink_kill"));
    }

    static bool IsMine(GameObject go)
    {
        var identity = go.GetComponent<EnemyIdentity>();
        if (identity != null && identity.Def != null) return identity.Def.role == EnemyRole.Mine;
        return PrefabName.Is(go, "mine");
    }

    // RunScore.OnKill, after the chain updates.
    public static void OnChain(int chain)
    {
        if (Achievements.Real) AchievementStore.SetCounterAtLeast(AchievementCatalog.CChain, chain);
    }

    static void OnScored(int points, Vector3 at, RunScore.Source source)
    {
        if (!Achievements.Real) return;
        OnScore(RunScore.Total);
    }

    // A run's score so far (also when the run is banked).
    public static void OnScore(long total)
    {
        if (!Achievements.Real) return;
        AchievementStore.SetCounterAtLeast(AchievementCatalog.CScore, (int)System.Math.Min(total, int.MaxValue));
    }

    public static void OnMegaDomino(Vector3 at)
    {
        if (Achievements.Real) AchievementStore.Unlock(AchievementCatalog.Find("mega_domino"));
    }

    // ---- elites ----

    static bool PilotKill(EliteDamage cause)
    {
        return cause == EliteDamage.PlayerWeapon || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam ||
               cause == EliteDamage.PlayerContact || cause == EliteDamage.Combo;
    }

    static void OnEliteDied(EliteShip elite, EliteDamage cause, string killer)
    {
        if (elite != null) OnEliteKilled(elite.Def, cause);
    }

    // An elite went down (EliteShip.Died); only the pilot's own kills count.
    public static void OnEliteKilled(EliteDef def, EliteDamage cause)
    {
        if (!Achievements.Real || !PilotKill(cause)) return;
        AchievementStore.AddCounter(AchievementCatalog.CElites, 1);
        if (def != null && !string.IsNullOrEmpty(def.codexId) && AchievementStore.MarkElite(def.codexId) &&
            def.WorldIndex >= 0 && def.WorldIndex < AchievementCatalog.EliteWorlds)
            AchievementStore.SetCounterAtLeast(AchievementCatalog.EliteCounter(def.WorldIndex),
                                               AchievementStore.ElitesDestroyedIn(def.WorldIndex));
        if (cause == EliteDamage.Teleport)
        {
            AchievementStore.Unlock(AchievementCatalog.Find("elite_blink"));
            AchievementStore.Unlock(AchievementCatalog.Find("pause_blink_kill"));
        }
    }

    // ---- bosses ----

    public static void OnBossFightStart() { bossHurt = false; }

    public static void OnPlayerHurt() { bossHurt = true; }

    // BossEncounter.BeginOutro: `destroyed` false means it warped away.
    public static void OnBossOutro(int world, bool destroyed)
    {
        if (!destroyed || !Achievements.Real || world < 0 || world >= BossIds.Length) return;
        AchievementStore.Unlock(AchievementCatalog.Find(BossIds[world]));
        if (AchievementStore.MarkBoss(world))
            AchievementStore.SetCounterAtLeast(AchievementCatalog.CBosses, AchievementStore.BossesDestroyed());
        if (!bossHurt) AchievementStore.Unlock(AchievementCatalog.Find("boss_no_hit"));
    }

    // ---- worlds & loops ----

    // WorldManager.Start / Advance: the run is now in `index`.
    public static void OnWorldEntered(int index)
    {
        pausesThisWorld = 0;
        if (!Achievements.Real || index <= 0 || index >= WorldReached.Length) return;
        AchievementStore.Unlock(AchievementCatalog.Find(WorldReached[index]));
    }

    // WorldManager.Advance, before the world changes: a world was cleared.
    public static void OnWorldCleared()
    {
        if (Achievements.Real && pausesThisWorld == 0)
            AchievementStore.Unlock(AchievementCatalog.Find("pause_no_pause_world"));
    }

    // WorldManager.Advance loop branch: the run is on pass `loopIndex`.
    public static void OnLoop(int loopIndex)
    {
        if (Achievements.Real) AchievementStore.SetCounterAtLeast(AchievementCatalog.CLoop, loopIndex);
    }

    // ---- pickups, deaths ----

    public static void OnStar()
    {
        if (Achievements.Real) AchievementStore.AddCounter(AchievementCatalog.CStars, 1);
    }

    public static void OnDeath()
    {
        if (Achievements.Real) AchievementStore.AddCounter(AchievementCatalog.CDeaths, 1);
    }

    public static void OnSecretPower()
    {
        if (Achievements.Real) AchievementStore.Unlock(AchievementCatalog.Find("secret_power_first"));
    }

    public static void OnSpeedMilestone(int reached)
    {
        if (!Achievements.Real) return;
        string id = reached == 1 ? "speed_flash" : reached == 2 ? "speed_speedster" : "speed_super_sonic";
        AchievementStore.Unlock(AchievementCatalog.Find(id));
    }

    // ---- pauses ----

    // score.pauseCounterFunction: a pause was spent.
    public static void OnPauseSpent()
    {
        if (!Achievements.Real) return;
        pausesThisWorld++;
        AchievementStore.Unlock(AchievementCatalog.Find("pause_first"));
    }

    // score.incromentPause: the pause count after a pickup.
    public static void OnPauseCount(int count)
    {
        if (count >= 15 && Achievements.Real) AchievementStore.Unlock(AchievementCatalog.Find("pause_hoarder"));
    }

    static readonly Collider2D[] dodgeHits = new Collider2D[16];
    static ContactFilter2D dodgeFilter = new ContactFilter2D { useTriggers = true };

    // movePlayer: the ship blinked from `from` to `to`.
    public static void OnTeleport(Vector3 from, Vector3 to)
    {
        if (!Achievements.Real) return;
        AchievementStore.AddCounter(AchievementCatalog.CBlinks, 1);
        if (!DodgeProbeEnabled || AchievementStore.IsUnlocked(AchievementCatalog.Find("pause_perfect_dodge")) || dodgeAt >= 0f) return;
        int n = Physics2D.OverlapCircle(from, DodgeRadius, dodgeFilter, dodgeHits);
        for (int i = 0; i < n; i++)
        {
            var c = dodgeHits[i];
            dodgeHits[i] = null;
            if (c == null || !RunScore.IsHostileShot(c.gameObject)) continue;
            // It must be a shot the blink left behind, not one it landed on.
            if (Vector2.Distance(c.transform.position, to) <= DodgeRadius * 1.5f) continue;
            dodgeAt = Clock();
            AchievementRunner.Wake();
            return;
        }
    }

    // Called every frame by AchievementRunner only while a dodge is pending.
    public static bool DodgePending { get { return dodgeAt >= 0f; } }

    public static void TickDodge()
    {
        if (dodgeAt < 0f) return;
        if (AchievementEvents.LastHurtAt >= dodgeAt) { dodgeAt = -1f; return; }   // hurt since: not a clean dodge
        if (Clock() - dodgeAt < DodgeSurviveSeconds) return;
        dodgeAt = -1f;
        if (Achievements.Real) AchievementStore.Unlock(AchievementCatalog.Find("pause_perfect_dodge"));
    }

    // ---- shop (menu side) ----

    public static void OnShipBought(int index, float cost)
    {
        if (!Achievements.Menu) return;
        AchievementStore.AddCounter(AchievementCatalog.CSpent, Mathf.RoundToInt(cost));
        if (index != shopingShips.StarterShip) AchievementStore.Unlock(AchievementCatalog.Find("ship_first"));
        AchievementStore.SetCounterAtLeast(AchievementCatalog.CShips, OwnedShips());
        RefreshCodex();
    }

    public static int OwnedShips()
    {
        int n = 0;
        for (int i = ShipId.First; i <= ShipId.Last; i++)
            if (PlayerPrefs.GetString(ShipId.OwnedKey(i), "") == "True") n++;
        return n;
    }

    public static void OnSkinBought(int shipId, int skin, float price)
    {
        if (!Achievements.Menu) return;
        AchievementStore.AddCounter(AchievementCatalog.CSpent, Mathf.RoundToInt(price));
        EvaluateSkins();
    }

    // skin_first / skin_special / skin_full_set from the owned skins.
    public static void EvaluateSkins()
    {
        bool any = false, special = false, full = false;
        for (int id = 1; id < shopingShips.shipTotal; id++)
        {
            int owned = 0;
            for (int n = 1; n < ShipSkins.PerShip; n++)
            {
                if (!ShipSkins.Has(id, n) || !ShipSkins.IsOwnedReal(id, n)) continue;
                owned++;
                any = true;
                if (ShipSkins.Get(id, n).kind == ShipSkinKind.Special) special = true;
            }
            if (owned >= ShipSkins.PerShip - 1) full = true;
        }
        if (any) AchievementStore.Unlock(AchievementCatalog.Find("skin_first"));
        if (special) AchievementStore.Unlock(AchievementCatalog.Find("skin_special"));
        if (full) AchievementStore.Unlock(AchievementCatalog.Find("skin_full_set"));
    }

    // ---- codex ----

    public static void OnCodexDiscovered(CodexEntry entry) { RefreshCodex(); }

    // codex_10/50/complete (the discovered count) and codex_field_guide.
    public static void RefreshCodex()
    {
        if (!Achievements.Menu) return;
        AchievementStore.SetCounterAtLeast(AchievementCatalog.CCodex, Codex.DiscoveredCount);
        if (!AchievementStore.IsUnlocked(AchievementCatalog.Find("codex_field_guide")) && AnyWorldCatalogued())
            AchievementStore.Unlock(AchievementCatalog.Find("codex_field_guide"));
    }

    // Every enemy and hazard entry of some world is discovered (bosses and elites have their own achievements).
    public static bool AnyWorldCatalogued()
    {
        int worlds = EnemyRoster.WorldKeys.Length;
        var total = new int[worlds];
        var found = new int[worlds];
        foreach (var e in Codex.Entries)
        {
            if (e.category != CodexCategory.Enemies && e.category != CodexCategory.Hazards) continue;
            if (BossCatalog.Find(e.id) != null || EliteCatalog.FindByCodexId(e.id) != null) continue;
            var def = EnemyRoster.FindByCodexId(e.id);
            if (def == null || def.world < 0 || def.world >= worlds) continue;
            total[def.world]++;
            if (Codex.IsDiscovered(e)) found[def.world]++;
        }
        for (int w = 0; w < worlds; w++) if (total[w] > 0 && found[w] == total[w]) return true;
        return false;
    }

    // ---- meta ----

    // Tutorial finished (or skipped): not behind the in-run guard, since it happens in the tutorial scene.
    public static void OnTutorialDone()
    {
        if (!DeveloperUnlocks.Enabled)
            AchievementStore.Unlock(AchievementCatalog.Find("meta_first_flight"));
    }

    public static void OnSignedIn()
    {
        AchievementStore.Unlock(AchievementCatalog.Find("meta_logged_on"));
        AchievementSync.ResyncAll();
    }

    // ---- toast ----

    public const string ToastHeading = "ACHIEVEMENT UNLOCKED";

    static void OnUnlockedToast(AchievementDef def)
    {
        // Only during a real run; menu unlocks light the Codex button's dot instead.
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "gameS1") return;
        CodexToast.Announce(ToastHeading, def.title, AchievementArt.For(def));
    }
}
