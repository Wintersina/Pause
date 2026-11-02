using UnityEngine;

// Existing-buyer ad-free grant (ExistingBuyer): pure rules, the once-only marker, iOS never granted, the cloud rule
// for pre-ads-era saves, and the adsEra field through the cloud merge.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ExistingBuyerTest.Run
public static class ExistingBuyerTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[EXISTINGBUYER] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static void Clear()
    {
        PlayerPrefs.DeleteKey(AdFree.Key);
        PlayerPrefs.DeleteKey(ExistingBuyer.MarkerKey);
        PlayerPrefs.DeleteKey(ExistingBuyer.ToastDueKey);
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool? oldForce = ExistingBuyer.ForcePlatformSoldPaid;
        Clear();
        try
        {
            const long now = 1000, past = 500, future = 2000;

            // ---- pure local rule ----
            var fresh = ProgressSnapshot.Fresh(0);
            Check("empty snapshot is not a buyer", !ExistingBuyer.IsExistingBuyer(fresh, now, 0));
            Check("null snapshot is not a buyer", !ExistingBuyer.IsExistingBuyer(null, now, 0));
            Check("starter ship only is not a buyer",
                !ExistingBuyer.IsExistingBuyer(new ProgressSnapshot { boughtShips = new[] { shopingShips.StarterShip } }, now, 0));
            Check("tutorial done is a buyer", ExistingBuyer.IsExistingBuyer(new ProgressSnapshot { hasDoneTut = true }, now, 0));
            Check("star dust is a buyer", ExistingBuyer.IsExistingBuyer(new ProgressSnapshot { currency = 5f }, now, 0));
            Check("a ship beyond the starter is a buyer",
                ExistingBuyer.IsExistingBuyer(new ProgressSnapshot { boughtShips = new[] { shopingShips.StarterShip + 1 } }, now, 0));
            Check("already ad-free is a buyer", ExistingBuyer.IsExistingBuyer(new ProgressSnapshot { adFree = true }, now, 0));
            Check("paid-era clause off (0): empty install not granted", !ExistingBuyer.IsExistingBuyer(fresh, now, 0));
            Check("paid-era clause on, before the end: empty install granted", ExistingBuyer.IsExistingBuyer(fresh, now, future));
            Check("paid-era clause on, after the end: empty install not granted", !ExistingBuyer.IsExistingBuyer(fresh, now, past));

            // ---- cloud rule ----
            Check("no cloud save does not grant", !ExistingBuyer.CloudSaveGrants(null));
            Check("legacy cloud save with progress grants", ExistingBuyer.CloudSaveGrants(new ProgressSnapshot { currency = 10f }));
            Check("legacy cloud save with only the tutorial grants", ExistingBuyer.CloudSaveGrants(new ProgressSnapshot { hasDoneTut = true }));
            Check("legacy empty cloud save does not grant", !ExistingBuyer.CloudSaveGrants(new ProgressSnapshot()));
            Check("cloud save from the ads era without adFree does not grant",
                !ExistingBuyer.CloudSaveGrants(new ProgressSnapshot { adsEra = true, currency = 10f, hasDoneTut = true }));

            // ---- once-only marker, Android ----
            ExistingBuyer.ForcePlatformSoldPaid = true;
            Clear();
            PlayerPrefs.DeleteKey("HasDoneTut");
            PlayerPrefs.SetFloat(ProgressSnapshot.CurrencyKey, 0f);
            Check("new install: nothing granted", !ExistingBuyer.EnsureEvaluated(now) && !AdFree.IsActive);
            Check("new install: marker written", ExistingBuyer.Evaluated);
            PlayerPrefs.SetFloat(ProgressSnapshot.CurrencyKey, 50f);
            PlayerPrefs.SetString("HasDoneTut", "true");
            Check("progress made later never grants (marker holds)", !ExistingBuyer.EnsureEvaluated(now) && !AdFree.IsActive);

            Clear();
            PlayerPrefs.SetFloat(ProgressSnapshot.CurrencyKey, 50f);
            Check("existing buyer: granted once", ExistingBuyer.EnsureEvaluated(now) && AdFree.IsActive);
            Check("existing buyer: toast due", PlayerPrefs.GetInt(ExistingBuyer.ToastDueKey, 0) == 1);
            Check("second run does not grant again", !ExistingBuyer.EnsureEvaluated(now));
            Check("toast only on the home scene", !ExistingBuyer.TryToast("somewhere") && PlayerPrefs.GetInt(ExistingBuyer.ToastDueKey, 0) == 1);

            // ---- iOS / other platforms ----
            ExistingBuyer.ForcePlatformSoldPaid = false;
            Clear();
            PlayerPrefs.SetFloat(ProgressSnapshot.CurrencyKey, 50f);
            Check("iOS: local rule never grants", !ExistingBuyer.EnsureEvaluated(now) && !AdFree.IsActive && !ExistingBuyer.Evaluated);
            Check("iOS: cloud rule never grants", !ExistingBuyer.ApplyCloud(new ProgressSnapshot { currency = 10f }) && !AdFree.IsActive);

            // ---- cloud apply, Android ----
            ExistingBuyer.ForcePlatformSoldPaid = true;
            Clear();
            Check("cloud apply: ads-era save does not grant", !ExistingBuyer.ApplyCloud(new ProgressSnapshot { adsEra = true, currency = 10f }) && !AdFree.IsActive);
            Check("cloud apply: legacy save with progress grants", ExistingBuyer.ApplyCloud(new ProgressSnapshot { currency = 10f }) && AdFree.IsActive);
            Check("cloud apply: second time is a no-op", !ExistingBuyer.ApplyCloud(new ProgressSnapshot { currency = 10f }));

            // ---- snapshot field ----
            Clear();
            Check("capture marks the ads era", ProgressSnapshot.Capture(1).adsEra);
            var merged = ProgressMerge.Merge(new ProgressSnapshot { adsEra = true }, new ProgressSnapshot());
            Check("merge keeps adsEra if either side has it", merged.adsEra);
            Check("merge of two legacy saves stays legacy", !ProgressMerge.Merge(new ProgressSnapshot(), new ProgressSnapshot()).adsEra);
            var json = JsonUtility.FromJson<ProgressSnapshot>("{\"schemaVersion\":1,\"currency\":3}");
            Check("an old blob without the field reads as legacy", !json.adsEra && ExistingBuyer.CloudSaveGrants(json));
        }
        finally
        {
            ExistingBuyer.ForcePlatformSoldPaid = oldForce;
            Clear();
        }
        return failures;
    }
}
