using UnityEditor;
using UnityEngine;

// Play mode only: pays a fake pre-registration reward, to see the toast and the dust.
public static class PreRegistrationRewardMenu
{
    [MenuItem("Pause/Billing/Simulate pre-registration reward")]
    static void Simulate()
    {
        if (!Application.isPlaying) { Debug.Log("[PreReg] enter play mode first"); return; }
        Debug.Log("[PreReg] simulate: " + PreRegistrationReward.Simulate());
    }

    [MenuItem("Pause/Billing/Simulate support purchase (+2,500 dust)")]
    static void SimulateSupport()
    {
        if (!Application.isPlaying) { Debug.Log("[Support] enter play mode first"); return; }
        Debug.Log("[Support] simulate: " + SupportPurchase.Simulate());
    }
}
