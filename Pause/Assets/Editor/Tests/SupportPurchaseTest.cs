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
                            string product = "support_developer_2500")
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
            Check("product ids", SupportPurchase.Large.productId == "support_developer_2500" && SupportPurchase.Small.productId == "support_developer_850");
            Check("pays 2500 and 850", SupportPurchase.Large.dust == 2500 && SupportPurchase.Small.dust == 850);
            Check("thank-you toast shows each pack's amount",
                  SupportPurchase.Large.grantedMessage == "Thank you for supporting the developer! +2,500 star dust" &&
                  SupportPurchase.Small.grantedMessage == "Thank you for supporting the developer! +850 star dust");
            Check("routing finds the right pack", SupportPurchase.ForProduct("support_developer_850") == SupportPurchase.Small &&
                  SupportPurchase.ForProduct("support_developer_2500") == SupportPurchase.Large && SupportPurchase.ForProduct("x") == null);

            var r = SupportPurchase.Handle(Buy("tok-1"), autoOk);
            Check("purchased is granted", r == SupportPurchase.Result.Granted);
            Check("pays 2,500 star dust", Mathf.Approximately(Dust, 2510f));
            Check("raises Rewarded with the amount", rewarded == 2500);
            Check("consumed once", consumes == 1);
            Check("token forgotten after a confirmed consume", SupportPurchase.GrantedTokens().Count == 0);

            // A stale owned-list (queried on the focus return that coincides with the
            // purchase, delivered after the consume) lists the same token again: it
            // must not read as a fresh purchase now that GrantedTokens has let it go.
            r = SupportPurchase.Handle(Buy("tok-1"), autoOk);
            Check("the same token delivered again after its consume does not pay again",
                  r == SupportPurchase.Result.AlreadyGranted && Mathf.Approximately(Dust, 2510f) && rewarded == 2500);
            Check("nor is it consumed again", consumes == 1);

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

            // The 850 pack: same rules, its own amount, tokens shared in one list.
            consumes = 0; rewarded = 0;
            float before = Dust;
            r = SupportPurchase.Handle(Buy("small-1", PlayPurchaseState.Purchased, "support_developer_850"), autoOk);
            Check("850 pack is granted and pays 850", r == SupportPurchase.Result.Granted && Mathf.Approximately(Dust, before + 850f) && rewarded == 850);
            Check("850 pack is consumed", consumes == 1 && SupportPurchase.GrantedTokens().Count == 0);
            r = SupportPurchase.Handle(Buy("small-2", PlayPurchaseState.Purchased, "support_developer_850"), autoOk);
            Check("850 pack repeats and pays again", r == SupportPurchase.Result.Granted && Mathf.Approximately(Dust, before + 1700f));
            r = SupportPurchase.Handle(Buy("small-3", PlayPurchaseState.Purchased, "support_developer_850"), failing);
            Check("850 failed consume still pays", r == SupportPurchase.Result.Granted && Mathf.Approximately(Dust, before + 2550f));
            SupportPurchase.ResetSession();
            r = SupportPurchase.Handle(Buy("small-3", PlayPurchaseState.Purchased, "support_developer_850"), autoOk);
            Check("850 retry does not pay twice", r == SupportPurchase.Result.AlreadyGranted && Mathf.Approximately(Dust, before + 2550f) && SupportPurchase.GrantedTokens().Count == 0);
            r = SupportPurchase.Handle(Buy("small-p", PlayPurchaseState.Pending, "support_developer_850"), autoOk);
            Check("850 pending waits", r == SupportPurchase.Result.Pending && Mathf.Approximately(Dust, before + 2550f));

            // A pack needs a price and a launcher; buying is one at a time, per pack.
            var big = SupportPurchase.Large; var small = SupportPurchase.Small;
            Check("not available without a price", !big.Available && !big.RequestBuy());
            int launches = 0, smallLaunches = 0;
            big.LaunchFlow = () => launches++;
            small.LaunchFlow = () => smallLaunches++;
            big.SetPrice("$9.99");
            Check("available with price and launcher", big.Available && !small.Available);
            Check("buy launches the flow", big.RequestBuy() && launches == 1);
            Check("second tap while buying does nothing", !big.RequestBuy() && launches == 1);
            Check("the open purchase is reported", SupportPurchase.Buying == big);
            big.EndBuy();
            Check("can buy again after the flow ends", big.RequestBuy() && launches == 2 && smallLaunches == 0);
            big.EndBuy();
            small.SetPrice("$3.99");
            Check("small pack launches its own flow", small.RequestBuy() && smallLaunches == 1 && launches == 2 && SupportPurchase.Buying == small);
            small.EndBuy();
            small.SetPrice(null);
            Check("price gone hides the pack", !small.Available);

            // Dock tiles: shown only with a price, tap routes to their own pack.
            var holder = new GameObject("~TileTest");
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var bigTile = DockSupportTile.Create(holder.transform, big, font);
                var smallTile = DockSupportTile.Create(holder.transform, small, font);
                big.EndBuy();
                Check("tile shown only with a price", bigTile.Shown && !smallTile.Shown);
                small.SetPrice("$4.00");
                smallTile.Refresh();
                Check("both tiles shown with both prices", bigTile.Shown && smallTile.Shown);
                Check("tile tap starts its own pack", smallTile.Tap() && smallLaunches == 2 && launches == 2);
                small.EndBuy();
                Check("the 850 icon is drawn smaller", DockSupportTile.IconScale(small) < DockSupportTile.IconScale(big));
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
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
