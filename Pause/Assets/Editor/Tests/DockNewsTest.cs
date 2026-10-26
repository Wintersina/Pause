using System;
using System.Collections.Generic;
using UnityEngine;

// The dock news ticker: which lines show (ownership, store availability), the
// scroll / ping-pong math, tap routing to the right purchase, and no garbage
// in the per-frame path.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod DockNewsTest.Run
public static class DockNewsTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[NEWS] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static bool Near(float a, float b) { return Mathf.Abs(a - b) < .001f; }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));
        ParagonPurchase.ResetSession();
        SupportPurchase.ResetSession();
        int paragon = DockNews.IndexOf(DockNews.ParagonId), support = DockNews.IndexOf(DockNews.SupportId);
        var list = new List<int>();

        try
        {
            Check("both items are in the table", paragon >= 0 && support >= 0 && paragon != support);

            // No store (editor / iOS): nothing purchasable shows.
            DockNews.Fill(list);
            Check("no store, no items", list.Count == 0);

            int paragonBuys = 0, supportBuys = 0;
            ParagonPurchase.LaunchFlow = () => paragonBuys++;
            SupportPurchase.LaunchFlow = () => supportBuys++;
            ParagonPurchase.SetPrice("$4.99");
            SupportPurchase.SetPrice("$10.00");
            DockNews.Fill(list);
            Check("store loaded: both show", list.Contains(paragon) && list.Contains(support));
            Check("paragon text carries Play's price", DockNews.Items[paragon].text().Contains("$4.99"));
            Check("support text carries price and payout", DockNews.Items[support].text().Contains("$10.00") && DockNews.Items[support].text().Contains("2,500"));

            // Owning Paragon hides its line, support stays.
            PlayerPrefs.SetString(ShipId.OwnedKey(PremiumShips.Paragon), "True");
            DockNews.Fill(list);
            Check("owned Paragon is not advertised", !list.Contains(paragon) && list.Contains(support));
            PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));

            // Without a price the line has no price text; no price means no purchase line.
            SupportPurchase.SetPrice(null);
            DockNews.Fill(list);
            Check("no price, no support line", !list.Contains(support));
            SupportPurchase.SetPrice("$10.00");

            // Tap routing.
            Check("tapping paragon launches paragon", DockNews.Tap(paragon) && paragonBuys == 1 && supportBuys == 0);
            ParagonPurchase.EndBuy();
            Check("tapping support launches support", DockNews.Tap(support) && supportBuys == 1 && paragonBuys == 1);
            SupportPurchase.EndBuy();
            Check("bad index taps nothing", !DockNews.Tap(-1) && !DockNews.Tap(99));

            // Scroll: a long line enters at the right and leaves past the left.
            const float view = 300f, speed = 50f;
            Check("long text scrolls", DockNews.Scrolls(500f, view));
            Check("starts at the right edge", Near(DockNews.Position(0f, 500f, view, speed), view));
            Check("moves left", Near(DockNews.Position(2f, 500f, view, speed), view - 100f));
            float d = DockNews.Duration(500f, view, speed);
            Check("one pass takes (view+text)/speed", Near(d, 16f));
            Check("ends fully off the left", Near(DockNews.Position(d, 500f, view, speed), -500f));
            Check("never overshoots", Near(DockNews.Position(d + 50f, 500f, view, speed), -500f));
            // Seamless: the next item starts at the right edge again.
            Check("loop restarts at the right edge", Near(DockNews.Position(0f, 120f, view, speed), view));

            // Short line: ping-pong between the edges.
            Check("short text sways", !DockNews.Scrolls(100f, view));
            Check("sway starts at the left", Near(DockNews.Position(0f, 100f, view, speed), 0f));
            Check("sway reaches the right edge", Near(DockNews.Position(4f, 100f, view, speed), 200f));
            Check("sway comes back", Near(DockNews.Position(6f, 100f, view, speed), 100f));
            Check("sway returns to the start", Near(DockNews.Position(8f, 100f, view, speed), 0f));
            for (float t = 0f; t < 30f; t += .37f)
            {
                float x = DockNews.Position(t, 100f, view, speed);
                if (x < -.001f || x > 200.001f) { Check("sway stays inside the view", false); break; }
            }
            Check("short line stays at least the minimum", DockNews.Duration(280f, view, speed) >= DockNews.MinPingPong);

            // No garbage in the per-frame path.
            var sink = 0f;
            for (int i = 0; i < 10; i++) { sink += DockNews.Position(i * .1f, 500f, view, speed); DockNews.Fill(list); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                sink += DockNews.Position(i * .016f, i % 2 == 0 ? 500f : 100f, view, speed);
                sink += DockNews.Duration(500f, view, speed);
                DockNews.Fill(list);
            }
            long grown = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("scroll math and filtering allocate nothing (" + grown + " B)", grown == 0 && sink != float.MinValue);
        }
        finally
        {
            ParagonPurchase.ResetSession();
            SupportPurchase.ResetSession();
        }
        return failures;
    }
}
