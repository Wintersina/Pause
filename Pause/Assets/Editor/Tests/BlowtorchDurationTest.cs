using UnityEngine;

// The Ligher's blowtorch burns 5 s with no upgrades and longer with each flame step, up to 7.5 s.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BlowtorchDurationTest.Run
public static class BlowtorchDurationTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BT] PASS  " : "[BT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        const int id = 9;
        float prev = 0f;
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            var l = ShipWeaponUpgrades.LoadoutAt(id, lv);
            Check("level " + lv + " duration " + l.duration, lv == 0 ? Mathf.Approximately(l.duration, 5f) : l.duration >= prev);
            if (lv == 1 || lv == 3) Check("level " + lv + " grows", l.duration > prev + .01f);
            prev = l.duration;
        }
        Check("max level is 7.5 s", Mathf.Approximately(ShipWeaponUpgrades.LoadoutAt(id, ShipWeaponUpgrades.MaxLevel).duration, 7.5f));
        Check("range and width kept at level 0", Mathf.Approximately(ShipWeaponUpgrades.LoadoutAt(id, 0).range, 2.7f)
              && Mathf.Approximately(ShipWeaponUpgrades.LoadoutAt(id, 0).width, 24f));
        return fails;
    }
}
