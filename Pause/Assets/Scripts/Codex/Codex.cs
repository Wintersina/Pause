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
// Star dust: nothing pays by itself. A find is a pending reward (CodexPayout) until COLLECT claims it (see below).
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

    // UNLOCK REWARDS. Every find (entries, ships, worlds, logs, combos, songs, backdrops) earns CodexPayout star
    // dust, claimed with COLLECT in the Codex. The saved state is one id list, PaidKey: the finds whose reward was
    // collected (entry ids, "fusion_<key>", "music_..." and "bd_..." pay ids). A find is "pending" while it is
    // found and not in PaidKey, so the pending state derives from the save and survives restarts. Older saves
    // simply have their finds pending (ids already in PaidKey stay collected). Developer mode and the tutorial
    // show no rewards and cannot collect (their discoveries are not real).
    public const string PaidKey = "codexPaid";
    // The Credits song's flat reward (a track is not an entry). Entries pay CodexPayout.For(entry).
    public const int UnlockReward = 5;
    // BackPaidKey: retired flag of the old one-time lump back-pay. Nothing sets it any more; it stays readable
    // and synced with the cloud save (ProgressSnapshot codexBackPaid, merged by OR) for old saves and merges.
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
        PrefsSaver.MarkDirty();
        bool worth = CodexPayout.ForExtra(trackId) > 0;   // a found song / backdrop is now a pending reward (COLLECT)
        if (worth) RaiseNewChanged();
        if (worth && Application.isPlaying && SceneManager.GetActiveScene().name == "gameS1")
            CodexToast.Announce(CodexToast.EntryHeading, trackId.StartsWith(CodexBackdrops.Prefix, StringComparison.Ordinal) ? "NEW BACKDROP" : "NEW MUSIC", null);
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
    // Star dust rewards: every find is PENDING until the player COLLECTs it
    // ---------------------------------------------------------------------
    //
    // Nothing pays by itself. A find (a contact entry, a ship owned, a world reached, a log, a combo fired,
    // a song heard, a backdrop seen) only marks the save; its CodexPayout dust waits as a pending reward, shown
    // by a yellow dot and counted by the home bubble, until COLLECT (per item, per tab, COLLECT ALL) adds its
    // id to PaidKey and grants the dust in the same write. Pending is derived from the save (found and not in
    // PaidKey), so it survives restarts and cloud restores, and PaidKey (only ever unioned) is the one guard
    // against paying anything twice.

    static HashSet<string> Paid()
    {
        if (paid == null) paid = ReadSet(PaidKey);
        return paid;
    }

    static bool RewardsActive { get { return !DeveloperUnlocks.Enabled; } }

    // The non-entry finds that pay dust: tabs of their own (MUSIC, COMBOS) or part of one (backdrops: WORLDS).
    public enum ExtraKind { Music, Combos, Backdrops }

    // The free starter ship is never a find: it counts as discovered on every save and never pays.
    static bool IsStarterShip(CodexEntry e)
    {
        return e.category == CodexCategory.Ships && CodexCatalogue.ShipIndex(e.id) == ShipId.Starter;
    }

    static bool EntryPending(CodexEntry e)
    {
        if (e == null || !RewardsActive || IsStarterShip(e)) return false;
        if (e.id == KillCamArchive.CodexId && !KillCamArchive.HasRecord) return false;   // Last Stand: once a replay is kept
        if (!IsDiscovered(e)) return false;
        return !Paid().Contains(e.id);
    }

    static bool ExtraFound(string id)
    {
        if (id.StartsWith(CodexPayout.FusionPrefix, StringComparison.Ordinal))
        {
            var f = AtomFusionTable.FindKey(id.Substring(CodexPayout.FusionPrefix.Length));
            return f != null && FusionDiscoveryStore.IsDiscovered(f.id);
        }
        EnsureLoaded();
        return seen.Contains(id);
    }

    static bool IsExtraId(string id)
    {
        return id.StartsWith("music_", StringComparison.Ordinal) || id.StartsWith(CodexBackdrops.Prefix, StringComparison.Ordinal) ||
               id.StartsWith(CodexPayout.FusionPrefix, StringComparison.Ordinal);
    }

    static bool IsExtraOfKind(string id, ExtraKind kind)
    {
        switch (kind)
        {
            case ExtraKind.Music: return id.StartsWith("music_", StringComparison.Ordinal);
            case ExtraKind.Combos: return id.StartsWith(CodexPayout.FusionPrefix, StringComparison.Ordinal);
            default: return id.StartsWith(CodexBackdrops.Prefix, StringComparison.Ordinal);
        }
    }

    // A combo / song / backdrop pay id ("fusion_bloom", "music_ember", "bd_frost_2") found and not collected.
    public static bool IsExtraPending(string payId)
    {
        if (string.IsNullOrEmpty(payId) || !RewardsActive || CodexPayout.ForExtra(payId) <= 0) return false;
        return ExtraFound(payId) && !Paid().Contains(payId);
    }

    public static bool IsFusionPending(FusionDef f) { return f != null && IsExtraPending(CodexPayout.FusionPayId(f)); }

    // The entry earns a reward the player has not collected yet.
    public static bool IsRewardPending(CodexEntry entry) { return EntryPending(entry); }

    // An entry id or a combo / song / backdrop pay id.
    public static bool IsRewardPending(string id)
    {
        if (id == null) return false;
        var e = Find(id);
        return e != null ? EntryPending(e) : IsExtraPending(id);
    }

    public static bool IsRewardCollected(string id)
    {
        EnsureLoaded();
        return Paid().Contains(id);
    }

    // Star dust waiting in one entry / one pay id.
    public static int PendingDust(CodexEntry entry) { return EntryPending(entry) ? CodexPayout.For(entry) : 0; }
    public static int PendingDust(string id)
    {
        if (!IsRewardPending(id)) return 0;
        var e = Find(id);
        return e != null ? CodexPayout.For(e) : CodexPayout.ForExtra(id);
    }

    // Every pending reward (entries, combos, songs, backdrops) the pick accepts: their ids go into `ids` (may be
    // null), the dust they pay into `dust`; returns how many.
    static int Gather(Func<string, bool> pick, List<string> ids, out int dust)
    {
        dust = 0;
        if (!RewardsActive) return 0;
        EnsureLoaded();
        int n = 0;
        foreach (var e in Entries)
        {
            if (!EntryPending(e) || !pick(e.id)) continue;
            n++;
            dust += CodexPayout.For(e);
            if (ids != null) ids.Add(e.id);
        }
        foreach (var f in AtomFusionTable.All)
        {
            string id = CodexPayout.FusionPayId(f);
            if (!IsExtraPending(id) || !pick(id)) continue;
            n++;
            dust += CodexPayout.ForExtra(id);
            if (ids != null) ids.Add(id);
        }
        foreach (string id in seen)
        {
            if (id.StartsWith(CodexPayout.FusionPrefix, StringComparison.Ordinal) || !IsExtraId(id)) continue;
            if (!IsExtraPending(id) || !pick(id)) continue;
            n++;
            dust += CodexPayout.ForExtra(id);
            if (ids != null) ids.Add(id);
        }
        return n;
    }

    // An id belongs to a category's tab: its entries, plus the backdrops under WORLDS.
    static bool InCategory(string id, CodexCategory category)
    {
        var e = Find(id);
        if (e != null) return e.category == category;
        return category == CodexCategory.Worlds && IsExtraOfKind(id, ExtraKind.Backdrops);
    }

    // Uncollected rewards in one entry tab (WORLDS also counts its backdrops).
    public static int PendingIn(CodexCategory category)
    {
        int dust;
        return Gather(id => InCategory(id, category), null, out dust);
    }

    public static int PendingDustIn(CodexCategory category)
    {
        int dust;
        Gather(id => InCategory(id, category), null, out dust);
        return dust;
    }

    // Uncollected rewards of the MUSIC tab, the COMBOS tab or the backdrops (all worlds).
    public static int PendingInExtra(ExtraKind kind)
    {
        int dust;
        return Gather(id => IsExtraOfKind(id, kind), null, out dust);
    }

    public static int PendingDustInExtra(ExtraKind kind)
    {
        int dust;
        Gather(id => IsExtraOfKind(id, kind), null, out dust);
        return dust;
    }

    // Uncollected rewards across every tab (the home bubble counts these).
    public static int PendingTotal
    {
        get
        {
            int dust;
            return Gather(id => true, null, out dust);
        }
    }

    // Collects one reward (an entry id or a combo / song / backdrop pay id). Returns the star dust paid (0: nothing pending).
    public static int CollectReward(string id)
    {
        return CollectWhere(x => x == id);
    }

    // Collects every pending reward of a tab (WORLDS includes its backdrops).
    public static int CollectRewards(CodexCategory category)
    {
        return CollectWhere(id => InCategory(id, category));
    }

    // Collects every pending combo, song or backdrop.
    public static int CollectExtras(ExtraKind kind)
    {
        return CollectWhere(id => IsExtraOfKind(id, kind));
    }

    // Collects every pending reward: entries, combos, songs and backdrops.
    public static int CollectAllRewards()
    {
        return CollectWhere(id => true);
    }

    // Marks the finds paid and grants the dust in the same write batch, then saves once: a kill in between
    // can neither give dust without the mark nor the mark without the dust (both are in PlayerPrefs before the
    // one save), and a second call finds nothing pending.
    static int CollectWhere(Func<string, bool> pick)
    {
        if (!RewardsActive || InTutorial()) return 0;
        var ids = new List<string>();
        int dust;
        Gather(pick, ids, out dust);
        if (ids.Count == 0 || dust <= 0) return 0;
        var done = Paid();
        foreach (string id in ids) done.Add(id);
        WritePaid(done);
        StarDustLedger.Grant(dust);
        PrefsSaver.SaveNow();
        RaiseNewChanged();
        return dust;
    }

    // A combo (atomic fusion) fired for the first time: it is now a pending reward (the toast announces it;
    // the dust is collected in the COMBOS tab).
    public static void OnFusionFound(FusionDef f)
    {
        if (f == null || !RewardsActive || InTutorial()) return;
        RaiseNewChanged();
        if (Application.isPlaying) CodexToast.Announce(CodexToast.EntryHeading, f.name + " COMBO", null);
    }

    static void WritePaid(HashSet<string> done)
    {
        var sorted = new List<string>(done);
        sorted.Sort(StringComparer.Ordinal);
        PlayerPrefs.SetString(PaidKey, string.Join(",", sorted));
    }

    // Retired: the one-time lump back-pay no longer exists (older saves' finds are simply pending). The flag key
    // stays readable so snapshots and cloud merges of older saves keep working; nothing sets it any more.
    public static bool BackPayDone { get { return PlayerPrefs.GetInt(BackPaidKey, 0) != 0; } }

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
