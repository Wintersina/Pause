using System;
using UnityEngine;

// The speed a run starts at, by ship and equipped colour (ShipSkins).
//
// Speeds here are the HUD numbers the player sees ("SPEED 15" is
// moveBackGround.speed 0.15). Colour 1 is ShipSkins.Stock (skin index 0),
// colours 2..5 are the extra skins in ShipSkins' table order.
//
//                      colour 1   2    3    4    5
//   regular ships           0     5   10   15   20     (0 = gameS1's own start)
//   high-end ships         10    15   20   25   30
//
// High-end ships are the priciest four: Ion Lancer (3200), Jade Phantom
// (4400), Gold Warden (5800) and Turtle (3000) -- the same tier that gets the
// extra hearts. Every start is under the speed cap (SpeedRamp.Cap, HUD 35),
// and WorldManager clamps to it regardless.
//
// The run ramps up from here as usual (SpeedRamp), and every world is
// arrived in at this speed again. Since no ship can pass the cap, the start
// speed is the lasting advantage: worlds are a fixed distance
// (WorldManager.BaselineWorldSeconds), so a fast start reaches every boss
// sooner -- more worlds and loops per run -- and the score's speed
// multiplier (ScoreRules.SpeedTierHud: x1.25 from HUD 20, x2 at the cap)
// reads the live speed, so it reaches its tiers sooner too.
//
// Developer mode needs nothing special: ShipSkins.Equipped already answers
// with the developer's equipped colour. The tutorial keeps its own start.
public static class ShipStartSpeed
{
    // gameS1's walls start at 0: colour 1 on a regular ship changes nothing.
    public const int StockHud = 0;
    public const int StepHud = 5;

    // By colour (skin index). A table longer than these continues +StepHud.
    public static readonly int[] RegularHud = { StockHud, 5, 10, 15, 20 };
    public static readonly int[] HighEndHud = { 10, 15, 20, 25, 30 };

    // ShipId: Ion Lancer, Jade Phantom, Gold Warden, Turtle.
    public static readonly int[] HighEndShips = { 5, 6, 7, 15 };

    // Test hook: the HUD start of the equipped ship/colour.
    public static Func<int> EquippedHudOverride;

    public static bool IsHighEnd(int id)
    {
        return Array.IndexOf(HighEndShips, id) >= 0;
    }

    // HUD start speed of ship `id` in colour `skin` (0 = stock).
    public static int HudFor(int id, int skin)
    {
        if (!ShipId.IsValid(id)) return StockHud;
        if (!ShipSkins.Has(id, skin)) skin = ShipSkins.Stock;
        int[] table = IsHighEnd(id) ? HighEndHud : RegularHud;
        int hud = skin < table.Length
            ? table[skin]
            : table[table.Length - 1] + StepHud * (skin - table.Length + 1);
        return Mathf.Max(StockHud, hud);
    }

    public static float SpeedFor(int id, int skin) { return HudFor(id, skin) / 100f; }

    // The ship and colour the next run flies in.
    public static int EquippedHud()
    {
        if (EquippedHudOverride != null) return EquippedHudOverride();
        int id = ShipId.Equipped();
        return HudFor(id, ShipSkins.Equipped(id));
    }

    public static float EquippedSpeed() { return EquippedHud() / 100f; }

    // The dock popup's line: "START SPD 15".
    public static string Label(int hud) { return "START SPD " + hud; }
}
