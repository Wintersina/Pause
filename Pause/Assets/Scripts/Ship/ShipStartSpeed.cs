using System;
using UnityEngine;

// The speed a run starts at, by ship and equipped colour (ShipSkins).
//
// Speeds here are the HUD numbers the player sees ("SPEED 15" is
// moveBackGround.speed 0.15). Colour 1 is ShipSkins.Stock (skin index 0),
// colours 2..5 are the extra skins in ShipSkins' table order.
//
// The base is the ship's COLUMN in the dock grid (SpaceDock.ColumnOf: left
// to right as the player sees it; the same layout that gives hearts by row):
//
//   column 1 = 0, column 2 = 5, column 3 = 10        (ColumnHud)
//
// and each colour bought adds StepHud on top, as before:
//
//                      colour 1   2    3    4    5
//   column 1 ships          0     5   10   15   20     (0 = gameS1's own start)
//   column 2 ships          5    10   15   20   25
//   column 3 ships         10    15   20   25   30
//
// The fastest start is 30, under the speed cap (SpeedRamp.Cap, HUD 35), and
// WorldManager clamps to it regardless.
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

    // Base start by grid column (index 0 = column 1).
    public static readonly int[] ColumnHud = { StockHud, 5, 10 };

    // The ship's column in the dock grid, 1..DockLayout.MaxColumns.
    public static int Column(int id) { return SpaceDock.ColumnOf(id); }

    // The ship's base HUD start, before any colour.
    public static int BaseHud(int id)
    {
        int col = Column(id);
        return col <= 0 ? StockHud : ColumnHud[Mathf.Min(col, ColumnHud.Length) - 1];
    }

    // Test hook: the HUD start of the equipped ship/colour.
    public static Func<int> EquippedHudOverride;

    // HUD start speed of ship `id` in colour `skin` (0 = stock): its column's
    // base plus StepHud per colour.
    public static int HudFor(int id, int skin)
    {
        if (!ShipId.IsValid(id)) return StockHud;
        if (!ShipSkins.Has(id, skin)) skin = ShipSkins.Stock;
        return Mathf.Max(StockHud, BaseHud(id) + StepHud * skin);
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
