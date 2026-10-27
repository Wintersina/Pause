using System;
using System.Collections.Generic;

// The Space Dock's news ticker content and scroll math (DockNewsTicker draws it).
//
// Every line lives in the one Items table below: an id, a `when` condition
// (owned / not owned / store has a price ...), the text (built from Play's
// localized price when it is loaded) and what a tap does. Add a line by adding
// a row. Purchasable lines carry their store gate in `when`, so iOS, the editor
// and a device without Play only ever see non-purchase news (none yet: the
// ticker hides itself when nothing is showable).
//
// All of it is allocation-free except Text, which is only called when a line
// is (re)built, never per frame.
public static class DockNews
{
    public sealed class Item
    {
        public string id;
        public Func<bool> when;
        public Func<string> text;
        public Action tap;
    }

    public const string ParagonId = "paragon";

    // Paragon, then one line per support pack (SupportPurchase.Packs), so a new
    // pack gets its ticker line with its table row.
    public static readonly Item[] Items = BuildItems();

    static Item[] BuildItems()
    {
        var items = new List<Item>
        {
            new Item
            {
                id = ParagonId,
                when = () => ParagonPurchase.Available && !ShipId.IsOwned(PremiumShips.Paragon) && !ShipUnlocks.IsLocked(PremiumShips.Paragon),
                text = () => "GET THE PARAGON SHIP, THE PREMIUM PILOT" + PriceSuffix(ParagonPurchase.Price),
                tap = () => ParagonPurchase.RequestBuy(),
            },
        };
        foreach (var pack in SupportPurchase.Packs)
        {
            var p = pack;
            items.Add(new Item
            {
                id = p.id,
                when = () => p.Available,
                text = () => "SUPPORT THE DEV: " + p.label + PriceSuffix(p.Price),
                tap = () => p.RequestBuy(),
            });
        }
        return items.ToArray();
    }

    // "  ·  $4.99", or nothing while the store price is not loaded.
    static string PriceSuffix(string price)
    {
        return string.IsNullOrEmpty(price) ? "" : "  ·  " + price;
    }

    // Appends the indices of the items showable right now to `into` (cleared first).
    public static void Fill(List<int> into)
    {
        into.Clear();
        for (int i = 0; i < Items.Length; i++)
            if (Items[i].when == null || Items[i].when()) into.Add(i);
    }

    public static int IndexOf(string id)
    {
        for (int i = 0; i < Items.Length; i++) if (Items[i].id == id) return i;
        return -1;
    }

    // A tap on item `index`: opens its purchase. False for a bad index.
    public static bool Tap(int index)
    {
        if (index < 0 || index >= Items.Length || Items[index].tap == null) return false;
        Items[index].tap();
        return true;
    }

    // ---------------------------------------------------------------- scroll

    public const float Speed = 45f;        // canvas units per second
    public const float MinPingPong = 6f;   // seconds a short line sways for

    // Text wider than the view scrolls right to left, entering at the right
    // edge and gone past the left; a line that fits sways between the edges.
    public static bool Scrolls(float textW, float viewW) { return textW > viewW; }

    // Seconds the item stays: one full pass, or one sway.
    public static float Duration(float textW, float viewW, float speed)
    {
        if (speed <= 0f) return MinPingPong;
        if (Scrolls(textW, viewW)) return (viewW + textW) / speed;
        float travel = viewW - textW;
        float cycle = 2f * travel / speed;
        return cycle < MinPingPong ? MinPingPong : cycle;
    }

    // The text's left edge (relative to the view's left) `t` seconds in.
    public static float Position(float t, float textW, float viewW, float speed)
    {
        if (Scrolls(textW, viewW))
        {
            float x = viewW - t * speed;
            float end = -textW;
            return x < end ? end : x;
        }
        float travel = viewW - textW;
        if (travel <= 0f || speed <= 0f) return 0f;
        float period = 2f * travel;
        float d = (t * speed) % period;
        if (d < 0f) d += period;
        return d <= travel ? d : period - d;
    }
}
