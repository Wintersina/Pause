using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// Base hearts by dock row (ShipLives.Base = the ship's row in the ship grid,
// 1..5 top to bottom, derived from the berth layout: SpaceDock.RowOf), with
// the colour step and the all-skins bonus on top; 1-heart and 5-heart ships
// survive exactly 1 / 5 hits; the dock texts show the numbers.
//
//   scripts/unity-batch.sh -projectPath <p> -executeMethod AllTests.RunSuites -suites RowHeartsTest
public static class RowHeartsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ROW] PASS  " : "[ROW] FAIL  ") + what);
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
            Clear();
            Rows();
            BonusOnTop();
            Survival();
            DockTexts();
        }
        finally
        {
            Clear();
            collisionDetection.MAXLIFE = 3;
            collisionDetection.lifeCounter = 0;
        }
        Debug.Log("[ROW] failures: " + fails);
        return fails;
    }

    static void Clear()
    {
        foreach (int id in ShipId.All)
            for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
    }

    static void Own(int id, int count)
    {
        for (int n = 1; n < ShipSkins.CountFor(id); n++)
        {
            if (n <= count) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
    }

    static void Rows()
    {
        Check("the dock grid has 3 columns", DockLayout.MinColumns == 3 && DockLayout.MaxColumns == 3);
        Check("15 ships make exactly 5 rows (" + ShipLives.Rows + ")", ShipLives.Rows == 5 && ShipLives.MostBase == 5);
        bool all = true;
        var counts = new int[6];
        foreach (int id in ShipId.All)
        {
            int slot = SpaceDock.SlotOf(id);
            int row = ShipLives.Row(id);
            all &= row >= 1 && row <= 5 && row == slot / 3 + 1 && ShipLives.Base(id) == row && ShipLives.Max(id) == row;
            if (row >= 1 && row <= 5) counts[row]++;
        }
        Check("every ship has a row 1..5 and its base hearts are that row", all);
        Check("three ships on each row (" + string.Join(",", counts.Skip(1)) + ")", counts.Skip(1).All(c => c == 3));
        Check("the row follows the price order the player sees: Neon Comet row 1, Gold Warden row 5",
              ShipLives.Row(ShipId.Starter) == 1 && ShipLives.Row(ShipId.FromKey("GoldWarden")) == 5);
        Check("hearts never go down as the price goes up",
              ShipId.All.All(a => ShipId.All.All(b => shopingShips.CostFor(a) >= shopingShips.CostFor(b) || ShipLives.Base(a) <= ShipLives.Base(b))));
        Check("an unknown ship has the fewest (" + ShipLives.Base(0) + ")", ShipLives.Base(0) == ShipLives.Fewest && ShipLives.Fewest == 1);
        Check("the most: row 5 + colours + set = " + ShipLives.Most, ShipLives.Most == 5 + SkinHearts.MostFromColours + SkinHearts.AllSkinsBonus && ShipLives.Most == 9);
    }

    static void BonusOnTop()
    {
        Clear();
        bool table = true;
        foreach (int id in ShipId.All)
            for (int n = 0; n < ShipSkins.CountFor(id); n++)
            {
                Own(id, n);
                table &= ShipLives.Max(id) == ShipLives.Row(id) + SkinHearts.ByColoursOwned[n];
                if (n == ShipSkins.CountFor(id) - 1) Own(id, 0);   // (the last ship at 4 colours would complete the set)
            }
        Check("colour step on top of the row for 0..4 colours, every ship", table);
        foreach (int id in ShipId.All) Own(id, ShipSkins.PerShip - 1);
        bool set = ShipId.All.All(id => ShipLives.Max(id) == ShipLives.Row(id) + 4);
        Check("full set: row + 2 + 2 on every ship (row 1 -> 5, row 5 -> 9)", set);
        Clear();
    }

    static void Survival()
    {
        foreach (int key in new[] { 1, 5 })
        {
            int id = key == 1 ? ShipId.Starter : ShipId.FromKey("GoldWarden");
            var r = new ShipLivesTest.Rig(id);
            Check(ShipId.KeyOf(id) + " (row " + key + ") runs with " + key + " heart(s)", collisionDetection.MAXLIFE == key && ShipLives.Left == key);
            for (int i = 0; i < key - 1; i++) r.Hit();
            Check("  survives " + (key - 1) + " hit(s)", !buttonClicks.playerDied && ShipLives.Left == 1);
            Check("  window after a hit (for ships with hearts to spare)", key == 1 || PlayerInvuln.Active);
            r.Hit();
            Check("  the next hit kills", buttonClicks.playerDied);
            r.Dispose();
        }
    }

    static void DockTexts()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var anchor = new GameObject("~Anchor").transform;
        var popup = DockPopup.Create(null, font, null);
        foreach (int id in ShipId.All)
        {
            popup.Show(id, anchor, .3f, false, false, shopingShips.CostFor(id), 0f);
            if (popup.LivesShown != ShipLives.Row(id)) Check(ShipId.KeyOf(id) + " badge shows its row", false);
        }
        Check("every ship's dock badge shows its row's hearts", true);
        Check("HEARTS line for a row-3 ship with a colour", RowLine());
        Check("1 heart reads HEART", SkinHearts.HeartsWord(1) == "HEART");
        string lore = CodexCatalogue.LivesLore(ShipId.Starter);
        Check("codex: the starter's lore says 1 HEARTS... (" + lore.Replace("\n", " / ") + ")", lore.Contains("HULL  1 HEART"));
        Object.DestroyImmediate(popup.gameObject);
        Object.DestroyImmediate(anchor.gameObject);
    }

    static bool RowLine()
    {
        int id = ShipId.All.First(i => ShipLives.Row(i) == 3);
        Own(id, 1);
        bool ok = SkinHearts.HeartsLine(id) == "HEARTS 4: 3 +1 COLOURS";
        Clear();
        return ok && SkinHearts.HeartsLine(id) == "HEARTS 3";
    }
}
