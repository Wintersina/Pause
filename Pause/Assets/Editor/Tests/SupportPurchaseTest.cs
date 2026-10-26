using System;
using UnityEngine;

// "Support the Developer" (repeatable, 2,500 star dust): granted once per
// purchase token, only when Purchased, only for its product, never twice after
// a failed consume, and a new purchase (new token) pays again.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod SupportPurchaseTest.Run
public static class SupportPurchaseTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[SUPP] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static PlayPurchase Buy(string token, PlayPurchaseState state = PlayPurchaseState.Purchased,
                            string product = SupportPurchase.ProductId)
    {
        return new PlayPurchase { productId = product, token = token, state = state };
    }

    static float Dust { get { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); } }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(SupportPurchase.GrantedKey);
        SupportPurchase.ResetSession();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 10f);

        int consumes = 0;
        Action<bool> pendingDone = null;
        Action<string, Action<bool>> autoOk = (t, done) => { consumes++; done(true); };
        Action<string, Action<bool>> failing = (t, done) => { consumes++; done(false); };
        Action<string, Action<bool>> hanging = (t, done) => { consumes++; pendingDone = done; };

        int rewarded = 0;
        Action<int> onRewarded = n => rewarded += n;
        SupportPurchase.Rewarded += onRewarded;

        try
        {
            Check("product id", SupportPurchase.ProductId == "support_developer_2500");
            Check("pays 2500", SupportPurchase.DustAmount == 2500);

            var r = SupportPurchase.Handle(Buy("tok-1"), autoOk);
            Check("purchased is granted", r == SupportPurchase.Result.Granted);
            Check("pays 2,500 star dust", Mathf.Approximately(Dust, 2510f));
            Check("raises Rewarded with the amount", rewarded == 2500);
            Check("consumed once", consumes == 1);
            Check("token forgotten after a confirmed consume", SupportPurchase.GrantedTokens().Count == 0);

            // Repeatable: a second purchase with a new token pays again.
            consumes = 0;
            r = SupportPurchase.Handle(Buy("tok-2"), autoOk);
            Check("a new purchase pays again", r == SupportPurchase.Result.Granted && Mathf.Approximately(Dust, 5010f));
            Check("and is consumed", consumes == 1);

            // Pending waits.
            consumes = 0;
            r = SupportPurchase.Handle(Buy("tok-p", PlayPurchaseState.Pending), autoOk);
            Check("pending is not granted", r == SupportPurchase.Result.Pending);
            Check("pending pays and consumes nothing", Mathf.Approximately(Dust, 5010f) && consumes == 0);
            r = SupportPurchase.Handle(Buy("tok-p", PlayPurchaseState.Unspecified), autoOk);
            Check("unspecified state is ignored", r == SupportPurchase.Result.Ignored && Mathf.Approximately(Dust, 5010f));
            r = SupportPurchase.Handle(Buy("tok-p"), autoOk);
            Check("pays once it becomes Purchased", r == SupportPurchase.Result.Granted && Mathf.Approximately(Dust, 7510f));

            // Wrong product / no token.
            consumes = 0;
            r = SupportPurchase.Handle(Buy("tok-x", PlayPurchaseState.Purchased, PreRegistrationReward.ProductId), autoOk);
            Check("other product is ignored", r == SupportPurchase.Result.Ignored);
            Check("other product pays and consumes nothing", Mathf.Approximately(Dust, 7510f) && consumes == 0);
            r = SupportPurchase.Handle(Buy(""), autoOk);
            Check("no token is ignored", r == SupportPurchase.Result.Ignored);

            // Failed consume: token kept, retry consumes without paying again.
            consumes = 0;
            r = SupportPurchase.Handle(Buy("tok-3"), failing);
            Check("failed-consume purchase is still granted", r == SupportPurchase.Result.Granted && Mathf.Approximately(Dust, 10010f));
            Check("token kept while the consume is unconfirmed", SupportPurchase.GrantedTokens().Contains("tok-3"));
            SupportPurchase.ResetSession();   // next launch
            r = SupportPurchase.Handle(Buy("tok-3"), autoOk);
            Check("retry does not pay again", r == SupportPurchase.Result.AlreadyGranted && Mathf.Approximately(Dust, 10010f));
            Check("retry consumes", consumes == 2);
            Check("token forgotten once the retry consumed", SupportPurchase.GrantedTokens().Count == 0);

            // Duplicate delivery while the consume is in flight.
            consumes = 0;
            SupportPurchase.Handle(Buy("tok-4"), hanging);
            r = SupportPurchase.Handle(Buy("tok-4"), hanging);
            Check("duplicate delivery pays once", r == SupportPurchase.Result.AlreadyGranted && Mathf.Approximately(Dust, 12510f));
            Check("duplicate delivery consumes once", consumes == 1);
            pendingDone(true);
            Check("consume answer clears the token", SupportPurchase.GrantedTokens().Count == 0);

            // The button needs a price and a launcher; buying is one at a time.
            Check("not available without a price", !SupportPurchase.Available && !SupportPurchase.RequestBuy());
            int launches = 0;
            SupportPurchase.LaunchFlow = () => launches++;
            SupportPurchase.SetPrice("$9.99");
            Check("available with price and launcher", SupportPurchase.Available);
            Check("buy launches the flow", SupportPurchase.RequestBuy() && launches == 1);
            Check("second tap while buying does nothing", !SupportPurchase.RequestBuy() && launches == 1);
            SupportPurchase.EndBuy();
            Check("can buy again after the flow ends", SupportPurchase.RequestBuy() && launches == 2);
        }
        finally
        {
            SupportPurchase.Rewarded -= onRewarded;
            PlayerPrefs.DeleteKey(SupportPurchase.GrantedKey);
            SupportPurchase.ResetSession();
        }
        return failures;
    }
}
