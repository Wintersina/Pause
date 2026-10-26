using System;
using UnityEngine;

// The Google Play pre-registration reward: granted once, only when Purchased,
// only for its product, never twice after a failed consume, no orderId or
// developerPayload needed.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PreRegistrationRewardTest.Run
public static class PreRegistrationRewardTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[PRR] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static PlayPurchase Reward(string token, PlayPurchaseState state = PlayPurchaseState.Purchased,
                               string product = PreRegistrationReward.ProductId)
    {
        return new PlayPurchase { productId = product, token = token, state = state };
    }

    static float Dust { get { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); } }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(PreRegistrationReward.GrantedKey);
        PreRegistrationReward.ResetSession();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 10f);

        int consumes = 0;
        Action<bool> pendingDone = null;
        Action<string, Action<bool>> autoOk = (t, done) => { consumes++; done(true); };
        Action<string, Action<bool>> failing = (t, done) => { consumes++; done(false); };
        Action<string, Action<bool>> hanging = (t, done) => { consumes++; pendingDone = done; };

        int rewarded = 0;
        Action<int> onRewarded = n => rewarded += n;
        PreRegistrationReward.Rewarded += onRewarded;

        try
        {
            // Grants once, consumes, and a repeat of the same purchase pays nothing.
            var r = PreRegistrationReward.Handle(Reward("tok-1"), autoOk);
            Check("purchased reward is granted", r == PreRegistrationReward.Result.Granted);
            Check("pays 100 star dust", Mathf.Approximately(Dust, 110f));
            Check("raises Rewarded with the amount", rewarded == 100);
            Check("toast is due", PreRegistrationReward.ToastDue);
            Check("consumed once", consumes == 1);
            Check("token forgotten after a confirmed consume", PreRegistrationReward.GrantedTokens().Count == 0);

            // A stale owned-list (queried on the focus return that coincides with the
            // delivery, answered after the consume) lists the same token again.
            r = PreRegistrationReward.Handle(Reward("tok-1"), autoOk);
            Check("the same token delivered again after its consume does not pay again",
                  r == PreRegistrationReward.Result.AlreadyGranted && Mathf.Approximately(Dust, 110f) && rewarded == 100 && consumes == 1);

            // Pending is ignored: no dust, no consume.
            PreRegistrationReward.ToastDue = false;
            consumes = 0;
            r = PreRegistrationReward.Handle(Reward("tok-p", PlayPurchaseState.Pending), autoOk);
            Check("pending is not granted", r == PreRegistrationReward.Result.Pending);
            Check("pending pays nothing", Mathf.Approximately(Dust, 110f));
            Check("pending is not consumed", consumes == 0);
            Check("pending raises no toast", !PreRegistrationReward.ToastDue);
            r = PreRegistrationReward.Handle(Reward("tok-p", PlayPurchaseState.Unspecified), autoOk);
            Check("unspecified state is ignored", r == PreRegistrationReward.Result.Ignored && Mathf.Approximately(Dust, 110f));
            r = PreRegistrationReward.Handle(Reward("tok-p"), autoOk);
            Check("the same purchase pays once it becomes Purchased", r == PreRegistrationReward.Result.Granted && Mathf.Approximately(Dust, 210f));

            // Unknown products are ignored.
            consumes = 0;
            r = PreRegistrationReward.Handle(Reward("tok-x", PlayPurchaseState.Purchased, "me.hapticgate.pause.ship_paragon"), autoOk);
            Check("unknown product is ignored", r == PreRegistrationReward.Result.Ignored);
            Check("unknown product pays and consumes nothing", Mathf.Approximately(Dust, 210f) && consumes == 0);
            r = PreRegistrationReward.Handle(Reward(""), autoOk);
            Check("a purchase without a token is ignored", r == PreRegistrationReward.Result.Ignored);

            // A failed consume keeps the token; the retry consumes without paying again.
            consumes = 0;
            r = PreRegistrationReward.Handle(Reward("tok-2"), failing);
            Check("failed-consume purchase is still granted", r == PreRegistrationReward.Result.Granted && Mathf.Approximately(Dust, 310f));
            Check("token kept while the consume is unconfirmed", PreRegistrationReward.GrantedTokens().Contains("tok-2"));
            PreRegistrationReward.ResetSession();   // next launch
            r = PreRegistrationReward.Handle(Reward("tok-2"), autoOk);
            Check("retry does not grant again", r == PreRegistrationReward.Result.AlreadyGranted && Mathf.Approximately(Dust, 310f));
            Check("retry consumes", consumes == 2);
            Check("token forgotten once the retry consumed", PreRegistrationReward.GrantedTokens().Count == 0);

            // A duplicate delivery while the consume is in flight neither pays nor consumes twice.
            consumes = 0;
            PreRegistrationReward.Handle(Reward("tok-3"), hanging);
            r = PreRegistrationReward.Handle(Reward("tok-3"), hanging);
            Check("duplicate delivery pays once", r == PreRegistrationReward.Result.AlreadyGranted && Mathf.Approximately(Dust, 410f));
            Check("duplicate delivery consumes once", consumes == 1);
            pendingDone(true);
            Check("consume answer clears the token", PreRegistrationReward.GrantedTokens().Count == 0);

            // No orderId / developerPayload anywhere in the purchase: the minimal purchase above is all it takes.
            Check("accepted with only product, token and state", Mathf.Approximately(Dust, 410f));

            // Developer-mode simulation.
            r = PreRegistrationReward.Simulate();
            Check("simulate grants in the editor", r == PreRegistrationReward.Result.Granted && Mathf.Approximately(Dust, 510f));
        }
        finally
        {
            PreRegistrationReward.Rewarded -= onRewarded;
            PlayerPrefs.DeleteKey(PreRegistrationReward.GrantedKey);
            PreRegistrationReward.ResetSession();
        }
        return failures;
    }
}
