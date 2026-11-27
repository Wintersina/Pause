using System.IO;
using UnityEngine;

// POWER CREEP (PowerCreep.cs): the shot atoms' summed levels (PowerLevel, 0-60) scale the ordinary enemy spawn rate and the
// elite gap. Both are exactly today's values at power 0, monotonic, capped, and bosses / the one-elite rule are untouched.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PowerCreepTest.Run
public static class PowerCreepTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CREEP] PASS  " : "[CREEP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PowerCreep.PowerOverride = -1;
        try
        {
            ShotAtoms.BeginRun();
            Check("PowerLevel is the sum of the atoms' levels: 0 on a fresh run", ShotAtoms.PowerLevel == 0 && PowerCreep.PowerLevel == 0);
            ShotAtoms.SetLevel(ShotAtom.Twin, 4);
            ShotAtoms.SetLevel(ShotAtom.Rapid, 7);
            Check("... 11 with TWIN 4 and RAPID 7", ShotAtoms.PowerLevel == 11 && PowerCreep.PowerLevel == 11);
            for (int i = 0; i < ShotAtomTuning.Count; i++) ShotAtoms.SetLevel((ShotAtom)i, ShotAtomTuning.MaxLevel);
            Check("... 60 with all six maxed", PowerCreep.PowerLevel == 60 && PowerCreep.MaxPower == 60);
            ShotAtoms.BeginRun(3);
            DroneSquad.CollectCore(DroneMode.Aimed);
            ShotAtoms.Collect(ShotAtom.Twin);
            ShotAtoms.Collect(ShotAtom.Twin);
            Check("opening pickups count the firepower of three active drones", ShotAtoms.PowerLevel >= 8 && PowerCreep.SpawnScale >= 1.2f);
            ShotAtoms.BeginRun();

            Check("at power 0 both multipliers are exactly 1 (today's values)", PowerCreep.SpawnScaleFor(0) == 1f && PowerCreep.EliteGapScaleFor(0) == 1f);
            bool mono = true, capped = true;
            float prevS = 1f, prevE = 1f;
            for (int p = 1; p <= 70; p++)
            {
                float s = PowerCreep.SpawnScaleFor(p), e = PowerCreep.EliteGapScaleFor(p);
                if (p <= 60) mono &= s > prevS && e < prevE;
                capped &= s <= 1f + PowerCreep.MaxSpawnBonus + 1e-5f && e >= 1f - PowerCreep.MaxEliteGapCut - 1e-5f;
                prevS = s; prevE = e;
            }
            Check("the spawn rate rises and the elite gap falls with every power level", mono);
            Check("... both are capped: x" + PowerCreep.SpawnScaleFor(60).ToString("0.00") + " enemies and x" + PowerCreep.EliteGapScaleFor(60).ToString("0.00") + " elite gap at 60, flat beyond it (and below 0)", capped &&
                  Mathf.Abs(PowerCreep.SpawnScaleFor(60) - 2f) < 1e-4f && Mathf.Abs(PowerCreep.EliteGapScaleFor(60) - .5f) < 1e-4f &&
                  PowerCreep.SpawnScaleFor(99) == PowerCreep.SpawnScaleFor(60) && PowerCreep.SpawnScaleFor(-5) == 1f);
            Check("... the curve responds to early upgrades: half the power gives over half the bonus (x" + PowerCreep.SpawnScaleFor(30).ToString("0.00") + " at 30)", PowerCreep.SpawnScaleFor(30) > 1f + PowerCreep.MaxSpawnBonus * .5f);
            Check("the overall spawn bonus is within +90..100% and the elite gap cut within 40..50%", PowerCreep.MaxSpawnBonus >= .9f && PowerCreep.MaxSpawnBonus <= 1f &&
                  PowerCreep.MaxEliteGapCut >= .4f && PowerCreep.MaxEliteGapCut <= .5f);

            // the elite director: today's gap at power 0, shorter at 60, still one elite at a time
            PowerCreep.PowerOverride = 0;
            bool zero = true;
            for (int i = 0; i < 50; i++) { float g = EliteDirector.NextGap(); zero &= g >= EliteDirector.GapSeconds.x / LoopScale() - 1e-3f && g <= EliteDirector.GapSeconds.y / LoopScale() + 1e-3f; }
            PowerCreep.PowerOverride = 60;
            bool full = true;
            for (int i = 0; i < 50; i++)
            {
                float g = EliteDirector.NextGap();
                float lo = Mathf.Max(EliteDirector.MinGapSeconds, EliteDirector.GapSeconds.x / LoopScale() * .5f);
                full &= g >= lo - 1e-3f && g <= Mathf.Max(EliteDirector.MinGapSeconds, EliteDirector.GapSeconds.y / LoopScale() * .5f) + 1e-3f;
            }
            // monotone in between, and the floor holds at every power and loop depth
            bool monoGap = true, floorOk = true;
            float prevMean = float.MaxValue;
            for (int p = 0; p <= 60; p += 6)
            {
                PowerCreep.PowerOverride = p;
                float sum = 0f;
                for (int i = 0; i < 200; i++) { float g = EliteDirector.NextGap(); sum += g; floorOk &= g >= EliteDirector.MinGapSeconds - 1e-4f; }
                float mean = sum / 200f;
                monoGap &= mean <= prevMean + 1.5f;   // (random draws: allow noise; the means trend down)
                prevMean = mean;
            }
            PowerCreep.PowerOverride = 60;
            float meanFull = 0f; for (int i = 0; i < 400; i++) meanFull += EliteDirector.NextGap(); meanFull /= 400f;
            PowerCreep.PowerOverride = 0;
            float meanZero = 0f; for (int i = 0; i < 400; i++) meanZero += EliteDirector.NextGap(); meanZero /= 400f;
            Check("the mean elite gap falls with power (x" + (meanFull / meanZero).ToString("0.00") + " at 60: about twice the encounters) and never drops under " + EliteDirector.MinGapSeconds + " s",
                  monoGap && floorOk && meanFull / meanZero < .6f && meanFull / meanZero > .4f);
            PowerCreep.PowerOverride = -1;
            Check("EliteDirector.NextGap is the old 16-32 s range at power 0 and 50% shorter at power 60 (floored at MinGapSeconds)", zero && full);
            Check("... one elite alive at a time still holds (MaxAlive 1)", EliteDirector.MaxAlive == 1);

            string spawner = File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs");
            string director = File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteDirector.cs");
            Check("the spawner divides its rolled delays by PowerCreep.SpawnScale (rails excluded) and the director scales NextGap; bosses read neither",
                  spawner.Contains("(adaptive ? PowerCreep.SpawnScale : 1f)") && director.Contains("PowerCreep.EliteGapScale") &&
                  !File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("PowerCreep"));
        }
        finally
        {
            PowerCreep.PowerOverride = -1;
            ShotAtoms.BeginRun();
        }
        Debug.Log("[CREEP] failures: " + fails);
        return fails;
    }

    static float LoopScale() { return Mathf.Min(1.5f, 1f + EliteDirector.GapLoopScale * Mathf.Max(0, RunLoop.Index)); }
}
