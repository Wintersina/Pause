using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// The pilot's codex: which entries of CodexCatalogue have been discovered.
//
// Gameplay reports interactions with one-line calls --
//   Codex.Discover(hit.gameObject);          // collided with / collected / destroyed it
//   Codex.Discover(Codex.WorldId(index));    // entered a world
// -- and the codex works out the entry from the object's prefab name.
//
// Discoveries persist in PlayerPrefs under one key, PrefsKey, as a
// comma-separated id list, written through the batched PrefsSaver. The cloud
// save syncs it with NewKey and AckKey (ProgressSnapshot / ProgressMerge).
//
// Some entries need no discovery event:
//   * the Pilot's Log entries are unlocked from the start;
//   * a ship is discovered while the player owns it (shop "boughtship" keys);
//   * worlds up to the furthest one reached (and the portal, once used) are
//     discovered for players who got there before the codex existed.
// Those are derived from the live progress keys rather than copied into
// PrefsKey, so they can never drift from what the shop and WorldManager say.
//
// The tutorial is practice: nothing met in tutorialS5 counts.
//
// Star dust: CodexPayout.For(entry) is paid on the first discovery and, for older saves, once in a lump (PayBackOnce).
//
// Developer mode (DeveloperUnlocks.Enabled) shows every entry as discovered
// but never writes PrefsKey; turning it off brings back the real set.
public static class Codex
{
    public const string PrefsKey = "codexSeen";
    // NEW markers: ids discovered but whose detail view has not been opened yet, and
    // the ids whose TAB dot the player has dismissed by opening the tab. Both are
    // comma-separated id lists; an install that already had discoveries starts with
    // neither key, so nothing old lights up (only discoveries made from now on).
    public const string NewKey = "codexNew", AckKey = "codexNewAck";
    public const string AchievementAckPrefix = "ach:";

    // UNLOCK REWARDS. Discovering a codex entry (not the Pilot's Log, which is free from the start) earns
    // CodexPayout star dust, paid on the first discovery (see below); the leftovers are COLLECTed in the Codex. The saved state is one id
    // list, PaidKey: the entries whose reward was collected. An entry is "pending" while it is in the real
    // discovered set (PrefsKey) and not in PaidKey, so the pending state derives from the save and survives
    // restarts. Saves that predate the rewards have no PaidKey: everything they already discovered is pending
    // once (each old entry is claimable one time; nothing is paid automatically). Developer mode
    // shows no rewards and cannot collect (its discoveries are not real).
    public const string PaidKey = "codexPaid";
    // The Credits song's flat reward (a track is not an entry). Entries pay CodexPayout.For(entry).
    public const int UnlockReward = 5;
    // First-discovery payout: a new entry now pays its CodexPayout straight away (Discover), and is marked in
    // PaidKey in the same write, so PaidKey stays the one guard against paying an entry twice (a re-contact, a
    // cloud merge, a restore: paid ids are only ever unioned). What is still pending (discovered, unpaid) is only
    // what no payment has reached: entries arriving through a cloud restore from an older build, or saves that
    // predate payouts and are not back-paid yet; those stay claimable with COLLECT.
    // BackPaidKey: set (1) once the one-time back-pay below ran, synced with the cloud save (ProgressSnapshot
    // codexBackPaid, merged by OR), never cleared by a restore.
    public const string BackPaidKey = "codexBackPaid";

    // Raised whenever a NEW marker appears or is cleared (the home bubble listens).
    public static event Action NewChanged;

    // Raised once per entry, the first time it is discovered.
    public static event Action<CodexEntry> Discovered;

    // Loaded lazily; Reload() re-reads PlayerPrefs (e.g. after a cloud restore).
    static List<string> seenOrder;
    static HashSet<string> seen;
    static Dictionary<string, CodexEntry> byId;
    // Raw GameObject name -> entry id (null when the object isn't in the codex),
    // so a busy run doesn't re-normalise "smStar_1(Clone)" on every pickup.
    static Dictionary<string, string> idByName;

    public static CodexEntry[] Entries { get { return CodexCatalogue.All; } }

    // Secret entries (the bosses) only count once they are listed.
    public static int Total
    {
        get
        {
            int n = 0;
            foreach (var e in Entries) if (IsListed(e)) n++;
            return n;
        }
    }

    // Every entry the player can discover (Completionist's target).
    public static int ReachableCount { get { return Entries.Length; } }

    public static int DiscoveredCount
    {
        get
        {
            int n = 0;
            foreach (var e in Entries) if (IsListed(e) && IsDiscovered(e)) n++;
            return n;
        }
    }

    public static int DiscoveredIn(CodexCategory category, out int total)
    {
        int n = 0;
        total = 0;
        foreach (var e in Entries)
        {
            if (e.category != category || !IsListed(e)) continue;
            total++;
            if (IsDiscovered(e)) n++;
        }
        return n;
    }

    // Every entry of a category, listed or not (card capacity).
    public static int CapacityIn(CodexCategory category)
    {
        int n = 0;
        foreach (var e in Entries) if (e.category == category) n++;
        return n;
    }

    // Whether the codex shows the entry at all: a secret entry stays off the
    // list -- not even a "???" card -- until it is discovered (developer mode
    // discovers, so lists, everything).
    public static bool IsListed(CodexEntry entry)
    {
        if (entry == null) return false;
        return !entry.secret || IsDiscovered(entry);
    }

    public static CodexEntry Find(string id)
    {
        if (id == null) return null;
        if (byId == null)
        {
            byId = new Dictionary<string, CodexEntry>();
            foreach (var e in Entries) byId[e.id] = e;
        }
        CodexEntry entry;
        return byId.TryGetValue(id, out entry) ? entry : null;
    }

    public static string WorldId(int worldIndex)
    {
        var ids = CodexCatalogue.WorldIds;
        return worldIndex >= 0 && worldIndex < ids.Length ? ids[worldIndex] : null;
    }

    // ---------------------------------------------------------------------
    // Discovery
    // ---------------------------------------------------------------------

    // One-line gameplay hook. Unknown objects (the ship's own shield, an
    // explosion, ...) are ignored. Returns true only on a first discovery.
    public static bool Discover(GameObject go)
    {
        return Discover(IdFor(go));
    }

    public static bool Discover(string id)
    {
        if (id == null || Find(id) == null) return false;
        // Already known -- nearly every contact in a run: nothing else to
        // check (the scene-name lookup below allocates).
        EnsureLoaded();
        if (seen.Contains(id)) return false;
        if (InTutorial()) return false;
        // Developer mode shows everything already, and its runs are not the
        // player's real progress: nothing is recorded and no toast fires.
        if (DeveloperUnlocks.Enabled) return false;

        EnsureLoaded();
        if (!seen.Add(id)) return false;
        seenOrder.Add(id);
        PlayerPrefs.SetString(PrefsKey, string.Join(",", seenOrder));
        var found = Find(id);
        // Star dust for the first discovery, marked paid in the same write (see PaidKey).
        PayDiscovery(found);
        if (found.category != CodexCategory.Log)
        {
            LoadNew();
            if (unseen.Add(id)) { acked.Remove(id); SaveNew(); }
        }
        PrefsSaver.MarkDirty();
        RaiseNewChanged();

        var handler = Discovered;
        if (handler != null) handler(Find(id));
        return true;
    }

    // ---------------------------------------------------------------------
    // Music tracks (the codex MUSIC tab, see CodexMusic)
    // ---------------------------------------------------------------------
    //
    // A track is "heard" once the player has played it in a real run. It is
    // stored as one more id ("music_ember") in the same PrefsKey set as the
    // discovered entries, so it persists, batches and cloud-syncs the same way.
    // Track ids are not CodexEntry rows, so they never touch Total,
    // DiscoveredCount, the NEW markers or the completionist target.
    // The tutorial is practice (nothing counts) and developer mode shows every
    // track without writing, exactly like entries.

    public static bool IsTrackHeard(string trackId)
    {
        if (string.IsNullOrEmpty(trackId)) return false;
        if (DeveloperUnlocks.Enabled) return true;
        EnsureLoaded();
        return seen.Contains(trackId);
    }

    // Returns true only the first time. Cheap when already heard (called on every world / boss music start).
    public static bool HearTrack(string trackId)
    {
        if (string.IsNullOrEmpty(trackId)) return false;
        EnsureLoaded();
        if (seen.Contains(trackId)) return false;
        if (InTutorial() || DeveloperUnlocks.Enabled) return false;
        seen.Add(trackId);
        seenOrder.Add(trackId);
        PlayerPrefs.SetString(PrefsKey, string.Join(",", seenOrder));
        int dust = PayExtra(trackId);   // star dust for a first song / backdrop (CodexPayout.ForExtra)
        PrefsSaver.MarkDirty();
        if (dust > 0 && Application.isPlaying && SceneManager.GetActiveScene().name == "gameS1")
            CodexToast.Announce(CodexToast.EntryHeading + "   +" + dust + " STAR DUST", trackId.StartsWith(CodexBackdrops.Prefix, StringComparison.Ordinal) ? "NEW BACKDROP" : "NEW MUSIC", null);
        return true;
    }

    // ---------------------------------------------------------------------
    // Backdrops (the codex BACKDROPS tab, see CodexBackdrops)
    // ---------------------------------------------------------------------
    //
    // A backdrop is "seen" once WorldBackdrop showed it in a real run. Same storage and rules as the
    // music tracks: one more id ("bd_frost_2") in the PrefsKey set, never a CodexEntry, so it never
    // touches Total, DiscoveredCount, NEW markers or Completionist. Tutorial and developer mode
    // record nothing; developer mode shows every backdrop.

    public static bool IsBackdropSeen(string backdropId)
    {
        return IsTrackHeard(backdropId);
    }

    // Returns true only the first time.
    public static bool SeeBackdrop(string backdropId)
    {
        return HearTrack(backdropId);
    }

    public static bool IsDiscovered(string id)
    {
        var entry = Find(id);
        return entry != null && IsDiscovered(entry);
    }

    public static bool IsDiscovered(CodexEntry entry)
    {
        if (entry == null) return false;
        if (entry.category == CodexCategory.Log) return true;
        // Developer mode reveals every entry without touching PrefsKey, so
        // switching it off returns the codex to the real discoveries (the same
        // way DeveloperUnlocks restores real ships and worlds).
        if (DeveloperUnlocks.Enabled) return true;

        EnsureLoaded();
        if (seen.Contains(entry.id)) return true;

        if (entry.category == CodexCategory.Ships)
        {
            int index = CodexCatalogue.ShipIndex(entry.id);
            return index > 0 && ShipId.IsOwned(index);
        }
        if (entry.category == CodexCategory.Worlds)
        {
            int highest = PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0);
            if (entry.id == CodexCatalogue.PortalId) return highest > 0;
            // Space itself is discovered by flying in it (WorldManager's
            // hook); a planet reached is proof the space before it was flown.
            int index = CodexCatalogue.WorldIndex(entry.id);
            return index >= 0 && index <= highest && highest > 0;
        }
        return false;
    }

    // What the player is allowed to see: locked entries keep their secrets.
    public const string LockedName = "???";

    public static string DisplayName(CodexEntry entry)
    {
        return IsDiscovered(entry) ? entry.name : LockedName;
    }

    public static string DisplayLore(CodexEntry entry)
    {
        return IsDiscovered(entry) ? entry.lore : string.Empty;
    }

    public static void Reload()
    {
        seen = null;
        seenOrder = null;
        unseen = null;
        acked = null;
        paid = null;
        DamageLog.Reload();
    }

    // The saved sets were rewritten by a cloud restore: re-read them and let
    // the home bubble / tab dots redraw.
    public static void OnExternalChange()
    {
        Reload();
        RaiseNewChanged();
    }

    // ---------------------------------------------------------------------
    // NEW markers
    // ---------------------------------------------------------------------

    static HashSet<string> unseen, acked, paid;

    static void LoadNew()
    {
        if (unseen != null) return;
        unseen = ReadSet(NewKey);
        acked = ReadSet(AckKey);
    }

    static HashSet<string> ReadSet(string key)
    {
        var set = new HashSet<string>();
        string raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(raw)) return set;
        foreach (string part in raw.Split(','))
        {
            string id = part.Trim();
            if (id.Length > 0) set.Add(id);
        }
        return set;
    }

    static void SaveNew()
    {
        PlayerPrefs.SetString(NewKey, string.Join(",", unseen));
        PlayerPrefs.SetString(AckKey, string.Join(",", acked));
        PrefsSaver.MarkDirty();
    }

    static void RaiseNewChanged()
    {
        var handler = NewChanged;
        if (handler != null) handler();
    }

    // Developer mode shows every entry already: no markers while it is on.
    static bool NewActive { get { return !DeveloperUnlocks.Enabled; } }

    // The entry is discovered and its detail view has not been opened since.
    public static bool IsNew(string id)
    {
        if (id == null || !NewActive) return false;
        LoadNew();
        var e = Find(id);
        return e != null && e.category != CodexCategory.Log && unseen.Contains(id);
    }

    // Entries of a category still marked NEW (cards show their dot).
    public static int NewIn(CodexCategory category)
    {
        if (!NewActive) return 0;
        LoadNew();
        int n = 0;
        foreach (string id in unseen) { var e = Find(id); if (e != null && e.category == category) n++; }
        return n;
    }

    // The category's tab shows a dot: something NEW in it, and the tab not opened since.
    public static bool TabHasNew(CodexCategory category)
    {
        if (!NewActive) return false;
        LoadNew();
        foreach (string id in unseen) { var e = Find(id); if (e != null && e.category == category && !acked.Contains(id)) return true; }
        return false;
    }

    // NEW entries whose tab has not been opened: what the home bubble adds to the claimable count.
    public static int UnackedNewCount
    {
        get
        {
            if (!NewActive) return 0;
            LoadNew();
            int n = 0;
            foreach (string id in unseen) { var e = Find(id); if (e != null && !acked.Contains(id)) n++; }
            return n;
        }
    }

    // The player opened the entry's detail view.
    public static void MarkSeen(string id)
    {
        if (id == null) return;
        LoadNew();
        if (!unseen.Remove(id)) return;
        acked.Remove(id);
        SaveNew();
        RaiseNewChanged();
    }

    // The player opened the category's tab: its tab dot (and its share of the home count) goes.
    public static void AckTab(CodexCategory category)
    {
        LoadNew();
        bool changed = false;
        foreach (string id in unseen) { var e = Find(id); if (e != null && e.category == category && acked.Add(id)) changed = true; }
        if (!changed) return;
        SaveNew();
        RaiseNewChanged();
    }

    // The ACHIEVEMENTS tab shows a dot while something claimable has not been seen by opening it.
    public static bool AchievementsTabHasNew()
    {
        if (!NewActive) return false;
        LoadNew();
        foreach (var d in AchievementCatalog.All)
            if (AchievementStore.IsClaimable(d) && !acked.Contains(AchievementAckPrefix + d.id)) return true;
        return false;
    }

    // Opening ACHIEVEMENTS clears its tab dot; the claimable count (home bubble) stays until claimed.
    public static void AckAchievements()
    {
        LoadNew();
        bool changed = false;
        foreach (var d in AchievementCatalog.All)
            if (AchievementStore.IsClaimable(d) && acked.Add(AchievementAckPrefix + d.id)) changed = true;
        if (!changed) return;
        SaveNew();
        RaiseNewChanged();
    }

    // ---------------------------------------------------------------------
    // Unlock rewards (5 star dust per discovered entry, collected by the player)
    // ---------------------------------------------------------------------

    static HashSet<string> Paid()
    {
        if (paid == null) paid = ReadSet(PaidKey);
        return paid;
    }

    static bool RewardsActive { get { return !DeveloperUnlocks.Enabled; } }

    // The entry earns a reward the player has not collected yet.
    public static bool IsRewardPending(CodexEntry entry)
    {
        if (entry == null || entry.category == CodexCategory.Log || !RewardsActive) return false;
        EnsureLoaded();
        return seen.Contains(entry.id) && !Paid().Contains(entry.id);
    }

    public static bool IsRewardPending(string id) { return IsRewardPending(Find(id)); }

    public static bool IsRewardCollected(string id)
    {
        EnsureLoaded();
        return Paid().Contains(id);
    }

    // Uncollected rewards in one tab.
    public static int PendingIn(CodexCategory category)
    {
        if (!RewardsActive) return 0;
        EnsureLoaded();
        var done = Paid();
        int n = 0;
        foreach (var e in Entries)
            if (e.category == category && e.category != CodexCategory.Log && seen.Contains(e.id) && !done.Contains(e.id)) n++;
        return n;
    }

    // Uncollected rewards across every tab (the home bubble counts these).
    public static int PendingTotal
    {
        get
        {
            int n = 0;
            foreach (CodexCategory c in Enum.GetValues(typeof(CodexCategory))) n += PendingIn(c);
            return n;
        }
    }

    // Collects one entry's reward. Returns the star dust paid (0: nothing pending).
    public static int CollectReward(string id)
    {
        var entry = Find(id);
        if (!IsRewardPending(entry)) return 0;
        return CollectWhere(e => e == entry);
    }

    // Collects every pending reward of a tab.
    public static int CollectRewards(CodexCategory category)
    {
        return CollectWhere(e => e.category == category);
    }

    // Collects every pending reward.
    public static int CollectAllRewards()
    {
        return CollectWhere(e => true);
    }

    // Marks the entries paid and grants the dust in the same write batch, then saves once: a kill in between
    // can neither give dust without the mark nor the mark without the dust (both are in PlayerPrefs before the
    // one save), and a second call finds nothing pending.
    static int CollectWhere(Func<CodexEntry, bool> pick)
    {
        if (!RewardsActive) return 0;
        EnsureLoaded();
        var done = Paid();
        int dust = 0;
        foreach (var e in Entries)
        {
            if (e.category == CodexCategory.Log || !seen.Contains(e.id) || done.Contains(e.id) || !pick(e)) continue;
            done.Add(e.id);
            dust += CodexPayout.For(e);
        }
        if (dust == 0) return 0;
        WritePaid(done);
        StarDustLedger.Grant(dust);
        PrefsSaver.SaveNow();
        RaiseNewChanged();
        return dust;
    }

    // Credits one newly discovered entry: the mark and the dust go into PlayerPrefs together (the caller saves).
    static void PayDiscovery(CodexEntry entry)
    {
        int dust = CodexPayout.For(entry);
        if (dust <= 0) return;
        var done = Paid();
        if (!done.Add(entry.id)) return;
        WritePaid(done);
        StarDustLedger.Grant(dust);
    }

    // Pays a non-entry find (a combo, a song, a backdrop) once: the id goes into PaidKey in the same write as the
    // dust (the caller saves). 0 when already paid, not worth anything, in the tutorial or in developer mode.
    static int PayExtra(string payId)
    {
        int dust = CodexPayout.ForExtra(payId);
        if (dust <= 0 || !RewardsActive || InTutorial()) return 0;
        var done = Paid();
        if (!done.Add(payId)) return 0;
        WritePaid(done);
        StarDustLedger.Grant(dust);
        return dust;
    }

    // A combo (atomic fusion) fired for the first time: its dust, once, with the popup. Returns the dust paid.
    public static int PayFusion(FusionDef f)
    {
        if (f == null) return 0;
        int dust = PayExtra(CodexPayout.FusionPayId(f));
        if (dust > 0)
        {
            PrefsSaver.MarkDirty();
            if (Application.isPlaying) CodexToast.Announce(CodexToast.EntryHeading + "   +" + dust + " STAR DUST", f.name + " COMBO", null);
        }
        return dust;
    }

    // Everything found that has not been paid yet and has no other payment route, once each (the paid list is
    // the guard, so running it again pays nothing): the Log entries, the ships owned and the worlds reached
    // (derived from progress, so a ship bought after the one-time back-pay lands here), the combos found, the
    // songs heard and the backdrops seen (older saves). Entries discovered by contact are NOT touched: they pay
    // in Discover, or wait for COLLECT. Runs on the home screen. Returns the dust paid.
    public static int PaySweep()
    {
        if (!RewardsActive || InTutorial()) return 0;
        EnsureLoaded();
        var done = Paid();
        int dust = 0;
        foreach (var e in Entries)
        {
            bool log = e.category == CodexCategory.Log;
            if (done.Contains(e.id) || seen.Contains(e.id) || !IsDiscovered(e)) continue;
            if (log && e.id == KillCamArchive.CodexId && !KillCamArchive.HasRecord) continue;   // Last Stand: once a replay is kept
            done.Add(e.id);
            dust += CodexPayout.For(e);
        }
        foreach (var f in AtomFusionTable.All)
        {
            string id = CodexPayout.FusionPayId(f);
            if (!FusionDiscoveryStore.IsDiscovered(f.id) || done.Contains(id)) continue;
            done.Add(id);
            dust += CodexPayout.ForExtra(id);
        }
        foreach (string id in new List<string>(seen))
        {
            int v = CodexPayout.ForExtra(id);
            if (v <= 0 || done.Contains(id)) continue;
            done.Add(id);
            dust += v;
        }
        if (dust == 0) return 0;
        WritePaid(done);
        StarDustLedger.Grant(dust);
        PrefsSaver.SaveNow();
        RaiseNewChanged();
        return dust;
    }

    static void WritePaid(HashSet<string> done)
    {
        var sorted = new List<string>(done);
        sorted.Sort(StringComparer.Ordinal);
        PlayerPrefs.SetString(PaidKey, string.Join(",", sorted));
    }

    // Star dust waiting in one entry / one tab / everywhere (derived from the save).
    public static int PendingDust(CodexEntry entry) { return IsRewardPending(entry) ? CodexPayout.For(entry) : 0; }

    public static int PendingDustIn(CodexCategory category)
    {
        if (!RewardsActive) return 0;
        EnsureLoaded();
        var done = Paid();
        int n = 0;
        foreach (var e in Entries)
            if (e.category == category && e.category != CodexCategory.Log && seen.Contains(e.id) && !done.Contains(e.id)) n += CodexPayout.For(e);
        return n;
    }

    // ---------------------------------------------------------------------
    // One-time back-pay
    // ---------------------------------------------------------------------

    public static bool BackPayDone { get { return PlayerPrefs.GetInt(BackPaidKey, 0) != 0; } }

    // First launch after payouts exist: pays, in one lump, every entry the save already discovered (including the
    // ones derived from progress: owned ships, worlds reached) that has not been paid, then sets BackPaidKey so it
    // never runs again. A fresh save pays 0 (and still sets the flag). Developer mode and the tutorial neither pay
    // nor set the flag (their discoveries are not real). Returns the dust paid (0: nothing, or already done).
    public static int PayBackOnce()
    {
        if (BackPayDone || DeveloperUnlocks.Enabled || InTutorial()) return 0;
        EnsureLoaded();
        var done = Paid();
        int dust = 0;
        foreach (var e in Entries)
        {
            if (e.category == CodexCategory.Log || done.Contains(e.id) || !IsDiscovered(e)) continue;
            done.Add(e.id);
            dust += CodexPayout.For(e);
        }
        if (dust > 0)
        {
            WritePaid(done);
            StarDustLedger.Grant(dust);
        }
        PlayerPrefs.SetInt(BackPaidKey, 1);
        PrefsSaver.SaveNow();
        RaiseNewChanged();
        return dust;
    }

    public const string BackPayHeading = "CODEX REWARD";

    static string BackPayText(int dust) { return "+" + dust + " STAR DUST FOR YOUR CODEX"; }

    static void EnsureLoaded()
    {
        if (seen != null) return;
        seen = new HashSet<string>();
        seenOrder = new List<string>();
        string raw = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(raw)) return;
        foreach (string part in raw.Split(','))
        {
            string id = part.Trim();
            // Unknown ids (an entry renamed or removed since) are kept, so a
            // cloud save from a newer build never loses anything here.
            if (id.Length > 0 && seen.Add(id)) seenOrder.Add(id);
        }
    }

    static bool InTutorial()
    {
        return SceneManager.GetActiveScene().name == score.TutorialScene;
    }

    // Picks up PlayerPrefs changed by anything else (cloud restore, reset)
    // at the next scene change.
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Reload();
        Prewarm();
        // The one-time back-pay, on the home screen, with a single popup for the lump sum.
        if (Application.isPlaying && scene.name == BackNavigator.HomeScene)
        {
            int dust = PayBackOnce() + PaySweep();
            if (dust > 0) CodexToast.Announce(BackPayHeading, BackPayText(dust), null);
        }
    }

    // Builds the catalogue lookup and parses the saved discoveries now (scene
    // load) instead of on the run's first collision or pickup.
    public static void Prewarm()
    {
        Find(string.Empty);
        EnsureLoaded();
    }

    // ---------------------------------------------------------------------
    // Object -> entry
    // ---------------------------------------------------------------------

    public static string IdFor(GameObject go)
    {
        if (go == null) return null;

        // Roster enemies know exactly which entry they are (aliens and mines
        // share their legacy object names across worlds).
        var roster = EnemyIdentity.Of(go);
        if (roster != null) return roster.codexId;
        // A chaser built from any other hull is still the Space chaser.
        ChaserEnemy chaser;
        if (go.TryGetComponent(out chaser)) return "enemy_chaser";

        string raw = PrefabName.NameOf(go);   // shared with the collision checks this frame
        if (idByName == null) idByName = new Dictionary<string, string>();
        string id;
        if (idByName.TryGetValue(raw, out id)) return id;

        id = IdForName(raw);
        idByName[raw] = id;
        return id;
    }

    public static string IdForName(string rawName)
    {
        string key = Normalise(rawName);
        if (key.Length == 0) return null;
        // Exact names first (aestroid_brown vs aestroid_brown_1 are different
        // art), then prefixes for numbered families like kn_enemyBlack1-5.
        foreach (var e in Entries)
            foreach (string m in e.matches)
                if (key == m) return e.id;
        foreach (var e in Entries)
            foreach (string m in e.matches)
                if (key.StartsWith(m, StringComparison.Ordinal)) return e.id;
        return null;
    }

    // Same rules as PrefabName: ignore "(Clone)", case, spaces, underscores
    // and hyphens, so "kn_enemyBlack3(Clone)" -> "knenemyblack3".
    public static string Normalise(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        int clone = raw.IndexOf("(Clone)", StringComparison.Ordinal);
        if (clone >= 0) raw = raw.Substring(0, clone);
        var sb = new StringBuilder(raw.Length);
        foreach (char ch in raw)
        {
            if (ch == ' ' || ch == '_' || ch == '-') continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
