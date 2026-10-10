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
// comma-separated id list, written through the batched PrefsSaver. Cloud
// saves should sync that key alongside the rest of player progress.
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

    public static int DiscoveredCount
    {
        get
        {
            int n = 0;
            foreach (var e in Entries) if (IsDiscovered(e)) n++;
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
    // The entries of a world that is not live yet (Tide, until WorldManager.TideEnabled) stay
    // off the list the same way, so the codex never shows a world the loop cannot reach.
    public static bool IsListed(CodexEntry entry)
    {
        return entry != null && ((!entry.secret && !InFutureWorld(entry)) || IsDiscovered(entry));
    }

    // True for a world entry, roster enemy / hazard or elite of a world past WorldManager.LiveWorldCount.
    public static bool InFutureWorld(CodexEntry entry)
    {
        if (entry == null || WorldManager.LiveWorldCount >= WorldManager.Worlds.Length) return false;
        int world = Array.IndexOf(CodexCatalogue.WorldIds, entry.id);
        if (world < 0)
        {
            var def = EnemyRoster.FindByCodexId(entry.id);
            if (def != null) world = def.world;
            else
            {
                var elite = EliteCatalog.FindByCodexId(entry.id);
                if (elite != null) world = elite.WorldIndex;
            }
        }
        return world >= WorldManager.LiveWorldCount;
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
    }

    // ---------------------------------------------------------------------
    // NEW markers
    // ---------------------------------------------------------------------

    static HashSet<string> unseen, acked;

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
