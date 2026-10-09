using UnityEngine;

// The player-facing START WORLD setting (Options > START <WORLD>).
//
// Once the player has finished a level -- beaten a world's boss and flown on,
// which is what raises highestWorld above Space -- they can start every run
// from any world they have reached (index <= highestWorld) instead of always
// the furthest one. The choice is a PlayerPrefs int (playerStartWorld) that is
// also synced in the cloud progress snapshot.
//
// Unset means "the furthest planet reached" (the rule before this setting
// existed). Choosing the furthest world in Options clears the key again, so a
// newly unlocked world is picked up automatically; choosing an earlier world
// pins it. A stored value above highestWorld (a progress restore or cloud sync
// that lowered it) is clamped when read.
//
// Run start priority (WorldManager.RunStartWorld):
//   replay pin > developer pick (mode on) > this choice > furthest planet.
// Starting early never lowers highestWorld (CurrentIndex only raises it).
public static class PlayerStartWorld
{
    public const string Key = "playerStartWorld";

    // The furthest world reached; the real value, since developer mode parks
    // the real one in a backup while it overwrites highestWorld.
    public static int Highest
    {
        get
        {
            int last = WorldManager.Worlds.Length - 1;
            return Mathf.Clamp(PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0), 0, last);
        }
    }

    // A level is finished once a world after the first has been reached: the
    // portal (or a planetfall) only opens after the world's boss.
    public static bool Unlocked { get { return Highest >= 1; } }

    public static bool HasChoice { get { return PlayerPrefs.HasKey(Key); } }

    // The world a run starts on: the saved choice, clamped to what is reached;
    // the furthest world when none is saved.
    public static int Selected
    {
        get
        {
            int highest = Highest;
            return HasChoice ? Mathf.Clamp(PlayerPrefs.GetInt(Key, highest), 0, highest) : highest;
        }
    }

    // The world to start on, or -1 when this setting has no say (nothing
    // finished yet, or no choice saved: the normal rule applies).
    public static int Chosen()
    {
        if (!Unlocked || !HasChoice) return -1;
        return Selected;
    }

    public static void Select(int index)
    {
        int clamped = Mathf.Clamp(index, 0, Highest);
        if (clamped >= Highest) PlayerPrefs.DeleteKey(Key);   // follow the furthest world
        else PlayerPrefs.SetInt(Key, clamped);
        PlayerPrefs.Save();
    }

    // Steps through the reached worlds, wrapping like the developer picker.
    public static void Step(int direction)
    {
        int count = Highest + 1;
        Select((Selected + direction + count) % count);
    }

    public static string Label(int world)
    {
        return "START  " + WorldManager.Worlds[Mathf.Clamp(world, 0, WorldManager.Worlds.Length - 1)]
            .displayName.ToUpperInvariant();
    }

    public const string LockedLabel = "START WORLD  LOCKED";
    public const string LockedHint = "BEAT A WORLD'S BOSS TO UNLOCK";
}
