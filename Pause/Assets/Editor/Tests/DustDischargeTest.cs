using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Star Dust (small star) cuts 0.4 s and a Bright Star (large star) 0.8 s off
// the weapon charge timer; cuts clamp at zero and leave a ready weapon alone.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod DustDischargeTest.Run
public static class DustDischargeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DD] PASS  " : "[DD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        var go = new GameObject("~DustDischarge");
        var c = go.AddComponent<ShipPowerController>();
        var timer = typeof(ShipPowerController).GetField("timer", Inst);

        Check("Star Dust cut is 0.4 s", Mathf.Approximately(c.secondsPerDust, 0.4f));
        Check("Bright Star cut is 0.8 s", Mathf.Approximately(c.secondsPerBrightStar, 0.8f));

        timer.SetValue(c, 10f);
        c.ReduceTimer(c.secondsPerDust);
        Check("Star Dust: 10 -> 9.6", Mathf.Approximately((float)timer.GetValue(c), 9.6f));
        c.ReduceTimer(c.secondsPerBrightStar);
        Check("Bright Star: 9.6 -> 8.8", Mathf.Approximately((float)timer.GetValue(c), 8.8f));

        timer.SetValue(c, 0.3f);
        c.ReduceTimer(c.secondsPerBrightStar);
        Check("cut clamps at zero", (float)timer.GetValue(c) == 0f);
        c.ReduceTimer(c.secondsPerBrightStar);
        c.ReduceTimer(c.secondsPerDust);
        Check("ready weapon stays at zero", (float)timer.GetValue(c) == 0f);

        Object.DestroyImmediate(go);
        return fails;
    }
}
