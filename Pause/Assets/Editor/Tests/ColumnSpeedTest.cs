using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// Base START SPD by dock column (ShipStartSpeed.BaseHud: column 1 = 0,
// column 2 = 5, column 3 = 10, from SpaceDock.ColumnOf), with each colour
// bought adding +5 on top as before; the dock's START SPD text and a run /
// new-world start use it.
public static class ColumnSpeedTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[COL] PASS  " : "[COL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            ShipStartSpeed.EquippedHudOverride = null;
            Columns();
            Upgrades();
            Texts();
            RunStart();
        }
        finally
        {
            ShipStartSpeed.EquippedHudOverride = null;
        }
        Debug.Log("[COL] failures: " + fails);
        return fails;
    }

    static void Columns()
    {
        Check("the grid has exactly 3 columns", DockLayout.MinColumns == 3 && DockLayout.MaxColumns == 3);
        var expect = new[] { 0, 5, 10 };
        bool all = true, derived = true;
        var per = new int[4];
        foreach (int id in ShipId.All)
        {
            int col = ShipStartSpeed.Column(id);
            all &= col >= 1 && col <= 3 && ShipStartSpeed.BaseHud(id) == expect[col - 1] && ShipStartSpeed.HudFor(id, 0) == expect[col - 1];
            derived &= col == SpaceDock.SlotOf(id) % 3 + 1;
            if (col >= 1 && col <= 3) per[col]++;
        }
        Check("every ship has a column 1..3 and its stock base is 0 / 5 / 10", all);
        Check("the column comes from the berth slot", derived);
        Check("five ships per column (" + string.Join(",", per.Skip(1)) + ")", per.Skip(1).All(c => c == 5));
        Check("Neon Comet (column 1) starts at 0", ShipStartSpeed.HudFor(ShipId.Starter, 0) == 0);
        Check("no ship: stock", ShipStartSpeed.BaseHud(0) == 0);
    }

    static void Upgrades()
    {
        bool ok = true;
        foreach (int id in ShipId.All)
            for (int skin = 0; skin < ShipSkins.CountFor(id); skin++)
                ok &= ShipStartSpeed.HudFor(id, skin) == ShipStartSpeed.BaseHud(id) + ShipStartSpeed.StepHud * skin;
        Check("each colour adds +5 on top of the column base, every ship", ok);
        bool cap = true;
        foreach (int id in ShipId.All)
            for (int skin = 0; skin < ShipSkins.CountFor(id); skin++)
                cap &= ShipStartSpeed.SpeedFor(id, skin) < SpeedRamp.Cap;
        Check("the fastest start (column 3, colour 5) is 30, under the cap", cap && ShipStartSpeed.HudFor(ShipId.FromKey("GoldWarden"), 4) == 30);
    }

    static void Texts()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var anchor = new GameObject("~Anchor").transform;
        var popup = DockPopup.Create(null, font, null);
        bool ok = true;
        foreach (int id in ShipId.All)
        {
            PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
            popup.Show(id, anchor, .3f, true, false, 0f, 0f);
            popup.ShowSkins(id, 0, 0f);
            ok &= popup.StartSpeedText == "START SPD " + ShipStartSpeed.BaseHud(id);
            popup.ShowSkins(id, 2, 0f);
            ok &= popup.StartSpeedText == "START SPD " + (ShipStartSpeed.BaseHud(id) + 10);
        }
        Check("the dock's START SPD line shows column base (+5 per colour)", ok);
        string lore = CodexCatalogue.ColoursLore(ShipId.FromKey("GoldWarden"));
        Check("codex: Gold Warden START SPEED 10 / 15 / 20 / 25 / 30", lore.Contains("START SPEED  10 / 15 / 20 / 25 / 30"));
        Object.DestroyImmediate(popup.gameObject);
        Object.DestroyImmediate(anchor.gameObject);
    }

    static void RunStart()
    {
        foreach (var (key, skin) in new[] { ("NeonComet", 0), ("VoltViper", 0), ("Lightning", 0), ("GoldWarden", 2) })
        {
            int id = ShipId.FromKey(key);
            PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
            ShipId.Equip(id);
            if (skin > 0) for (int n = 1; n <= skin; n++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            ShipSkins.Equip(id, skin);
            float want = ShipStartSpeed.HudFor(id, skin) / 100f;
            Check(key + ": a run / new world starts at " + (want * 100f) + " (" + WorldManager.RunStartSpeed(0f) + ")",
                  Mathf.Approximately(WorldManager.RunStartSpeed(0f), want) && Mathf.Approximately(ShipStartSpeed.EquippedSpeed(), want));
        }
        ShipId.Equip(ShipId.Starter);
        ShipSkins.Equip(ShipId.Starter, 0);
    }
}
