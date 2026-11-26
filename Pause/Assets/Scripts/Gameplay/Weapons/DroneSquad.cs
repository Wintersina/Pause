using UnityEngine;

// Run-owned drone slots and firing modes. Weapon modifiers remain in ShotAtoms, per slot.
// Slot identity is stable: adding a drone never redistributes an existing build.
public enum DroneMode { Dormant = 0, Straight = 1, Alternating = 2, Aimed = 3 }

public static class DroneSquad
{
    public const int MaxStartingSlots = 3, MaxSlots = 12, MaxCoreLevel = 4;
    public static int StartingSlots { get; private set; } = 1;
    public static int UnlockedAtLevel(int level) => level >= ShipWeaponUpgrades.MaxLevel ? 3 : level >= 3 ? 2 : 1;
    public static int StartingFor(int ship) => UnlockedAtLevel(ShipWeaponUpgrades.Level(ship));
    static readonly DroneMode[] modes = new DroneMode[MaxSlots];
    static readonly int[] coreLevels = new int[MaxSlots];
    public static int Slots { get; private set; } = StartingSlots;
    public static int Version { get; private set; }
    public static bool StarterOffered { get; private set; }
    public static int Selected { get; private set; }
    public static DroneMode PreferredMode { get; private set; } = DroneMode.Aimed;
    public static int ActiveCount
    {
        get { int n = 0; for (int i = 0; i < Slots; i++) if (IsActive(i)) n++; return n; }
    }
    public static bool IsActive(int slot) => slot >= 0 && slot < Slots && modes[slot] != DroneMode.Dormant;
    public static DroneMode Mode(int slot) => slot >= 0 && slot < Slots ? modes[slot] : DroneMode.Dormant;
    public static int CoreLevel(int slot) => slot >= 0 && slot < Slots ? coreLevels[slot] : 0;
    public static float RateBonus(int slot) => Mathf.Max(0, CoreLevel(slot) - 1) * .25f;
    public static void BeginRun(int startingSlots = -1)
    {
        StartingSlots = startingSlots > 0 ? Mathf.Clamp(startingSlots, 1, MaxStartingSlots) : StartingFor(ShipId.Equipped());
        Slots = StartingSlots; Selected = 0; PreferredMode = DroneMode.Aimed; StarterOffered = false;
        for (int i = 0; i < MaxSlots; i++) { modes[i] = DroneMode.Dormant; coreLevels[i] = 0; }
        Version++;
    }
    // ShotAtoms and editor fixtures share the same slot limit. Pickups never call this;
    // normal coins cannot grant a locked dock slot.
    public static void SetSlots(int count)
    {
        count = Mathf.Clamp(count, 1, MaxSlots);
        if (Slots == count) return;
        Slots = count; Selected = Mathf.Clamp(Selected, 0, count - 1); Version++;
    }
    public static void Select(int slot)
    {
        if (slot < 0 || slot >= Slots || Selected == slot) return;
        Selected = slot; Version++;
    }
    public static string Name(DroneMode mode)
    {
        switch (mode)
        {
            case DroneMode.Straight: return "STRAIGHT";
            case DroneMode.Alternating: return "SWEEP";
            case DroneMode.Aimed: return "AIMED";
            default: return "DORMANT";
        }
    }
    public static Color Colour(DroneMode mode)
    {
        switch (mode)
        {
            case DroneMode.Straight: return new Color(.35f, .9f, 1f);
            case DroneMode.Alternating: return new Color(1f, .7f, .25f);
            case DroneMode.Aimed: return new Color(.6f, 1f, .4f);
            default: return new Color(.3f, .37f, .44f);
        }
    }
    public static int EmptySlot()
    {
        for (int i = 0; i < Slots; i++) if (!IsActive(i)) return i;
        return -1;
    }
    public static int CoreRecipient(DroneMode mode)
    {
        if (mode == DroneMode.Dormant) return -1;
        int empty = EmptySlot();
        if (empty >= 0) return empty;
        int best = -1, lowest = int.MaxValue;
        for (int i = 0; i < Slots; i++)
            if (modes[i] == mode && coreLevels[i] < MaxCoreLevel && coreLevels[i] < lowest)
            { best = i; lowest = coreLevels[i]; }
        if (best >= 0) return best;
        // A differently labelled core can explicitly reprogram the weakest nonmatching slot;
        // the pickup preview must show the old/new mode before collection.
        for (int i = 0; i < Slots; i++)
            if (modes[i] != mode && coreLevels[i] < lowest)
            { best = i; lowest = coreLevels[i]; }
        return best;
    }
    public struct CoreCatch
    {
        public int slot, beforeLevel, level;
        public DroneMode beforeMode, mode;
        public bool activated, maxed;
    }
    public static CoreCatch CollectCore(DroneMode mode)
    {
        int slot = CoreRecipient(mode);
        var got = new CoreCatch { slot = slot, mode = mode, maxed = slot < 0 };
        if (slot < 0) return got;
        got.beforeMode = modes[slot]; got.beforeLevel = coreLevels[slot];
        got.activated = !IsActive(slot);
        coreLevels[slot] = modes[slot] == mode ? Mathf.Min(MaxCoreLevel, coreLevels[slot] + 1) : 1;
        modes[slot] = mode; got.level = coreLevels[slot];
        PreferredMode = mode; Selected = slot; Version++;
        return got;
    }
    public static int ModifierRecipient(ShotAtom atom)
    {
        for (int i = 0; i < Slots; i++)
            if (!IsActive(i) && ShotAtoms.Level(i, atom) < ShotAtomTuning.MaxLevel) return i;
        int best = -1, lowest = int.MaxValue;
        for (int i = 0; i < Slots; i++)
        {
            int level = ShotAtoms.Level(i, atom);
            if (level < ShotAtomTuning.MaxLevel && level < lowest) { best = i; lowest = level; }
        }
        return best;
    }
    public static bool Activate(int slot)
    {
        if (slot < 0 || slot >= Slots || IsActive(slot)) return false;
        modes[slot] = PreferredMode; coreLevels[slot] = 1; Selected = slot; Version++;
        return true;
    }
    public static void OfferStarter()
    {
        if (StarterOffered || startMenu.youAreInTutorial || buttonClicks.playerDied || !WorldManager.Flying ||
            WorldEntry.Active || WorldTransition.InProgress || BossEncounter.Running) return;
        var battery = FloatieBattery.Current;
        Vector3 at = new Vector3(battery != null ? battery.transform.position.x : 0f, CameraFit.ViewTop + .6f, 0f);
        DroneCorePickup.Spawn(DroneMode.Aimed, at);
        StarterOffered = true;
        AtomSpacing.Released();
    }

    public static int AddBossDrone()
    {
        if (Slots >= MaxSlots) return -1;
        int added = Slots;
        // Give the recruit a useful catch-up floor without weakening or copying the strongest gun.
        for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++)
        {
            int total = 0, active = 0;
            for (int i = 0; i < Slots; i++) if (IsActive(i)) { total += ShotAtoms.Level(i, (ShotAtom)a); active++; }
            int inherited = active == 0 ? 0 : total / (active * 2);
            ShotAtoms.SetLevel(added, (ShotAtom)a, inherited);
        }
        Slots++; ShotAtoms.SetDroneCount(Slots); Activate(added);
        return added;
    }
}
