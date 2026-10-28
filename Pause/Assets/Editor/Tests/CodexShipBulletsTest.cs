using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The Codex SHIPS page is a bullet list, not a paragraph: every ship (the whole roster, Paragon included)
// has 2..12 bullets, none empty, over-long or repeated, whose facts come from the same tables the game
// plays by (ShipLoadoutTable, ShipLives, ShipSkins, ShipStartSpeed, ShipWeaponUpgrades). The bullets are
// drawn as small Image markers in a left gutter (CodexBulletMarks), so wrapped lines hang under the text,
// no glyph is needed from the font, and the page's text still fits its card at the codex's readable floor
// on phone, tall phone, tablet and landscape canvases.
public static class CodexShipBulletsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CSB] PASS  " : "[CSB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const int MinBullets = 2, MaxBullets = 12, MaxBulletLength = 170;

    static readonly (string name, Rect safe)[] Screens =
    {
        ("minimum 800x1280", new Rect(-400f, -640f, 800f, 1280f)),
        ("16:9 phone", new Rect(-400f, -711f, 800f, 1422f)),
        ("tall phone 1080x2340", new Rect(-400f, -866f, 800f, 1733f)),
        ("21:9 phone", new Rect(-400f, -978f, 800f, 1956f)),
        ("iPad 1536x2048", new Rect(-480f, -640f, 960f, 1280f)),
        ("landscape Mac", new Rect(-1138f, -640f, 2276f, 1280f)),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetString("HasDoneTut", "true");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var es = new GameObject("TestEventSystem", typeof(EventSystem));
        DeveloperUnlocks.SetEnabled(true);   // every ship discovered
        Codex.Reload();
        CodexPanel panel = null;
        try
        {
            Data();
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            Page(panel);
        }
        finally
        {
            DeveloperUnlocks.SetEnabled(false);
            Codex.Reload();
            if (panel != null) Object.DestroyImmediate(panel.gameObject);
            Object.DestroyImmediate(es);
        }
        Debug.Log("[CSB] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void Data()
    {
        Check("the roster has Paragon (id 16) and 16+ ships", ShipId.Count >= 16 && System.Linq.Enumerable.Contains(ShipId.All, 16));
        foreach (int ship in ShipId.All)
        {
            string key = ShipId.KeyOf(ship);
            var e = Codex.Find(CodexCatalogue.ShipPrefix + key);
            Check(key + ": entry exists and is bulleted", e != null && CodexCatalogue.IsBulleted(e));
            if (e == null) continue;
            var bullets = e.lore.Split('\n');
            Check(key + ": " + bullets.Length + " bullets within " + MinBullets + ".." + MaxBullets,
                  bullets.Length >= MinBullets && bullets.Length <= MaxBullets);
            var seen = new HashSet<string>();
            bool nonEmpty = true, shortEnough = true, unique = true, noGlyph = true;
            foreach (string b in bullets)
            {
                nonEmpty &= b.Trim().Length > 0;
                shortEnough &= b.Length <= MaxBulletLength;
                unique &= seen.Add(b);
                noGlyph &= b.IndexOf('•') < 0;   // the marker is an Image, never a text glyph
            }
            Check(key + ": no empty bullet", nonEmpty);
            Check(key + ": every bullet <= " + MaxBulletLength + " chars", shortEnough);
            Check(key + ": no duplicate bullet", unique);
            Check(key + ": bullets are plain text (markers are Images, no font glyph needed)", noGlyph);

            var l = ShipLoadoutTable.For(ship);
            Check(key + ": ATTACK bullet names the loadout's attack", HasBullet(bullets, "ATTACK  ", l.attackName));
            Check(key + ": SECRET POWER bullet names the loadout's power", HasBullet(bullets, "SECRET POWER  ", l.powerName));
            Check(key + ": HULL bullet has the ship's hearts",
                  HasBullet(bullets, "HULL  ", ShipLives.Base(ship) + " "));
            var colours = new List<string>();
            var speeds = new List<string>();
            for (int skin = 0; skin < ShipSkins.CountFor(ship); skin++)
            {
                colours.Add(ShipSkins.Get(ship, skin).DisplayName);
                speeds.Add(ShipStartSpeed.HudFor(ship, skin).ToString());
            }
            Check(key + ": COLOURS bullet lists every colour", HasBullet(bullets, "COLOURS  ", colours.ToArray()));
            Check(key + ": START SPEED bullet is the per-colour speeds", HasBullet(bullets, "START SPEED  ", string.Join(" / ", speeds)));
            var steps = new List<string>();
            for (int level = 1; level <= ShipWeaponUpgrades.MaxLevel; level++) steps.Add(ShipWeaponUpgrades.Step(ship, level).label);
            Check(key + ": UPGRADES bullet lists each colour's upgrade", HasBullet(bullets, "UPGRADES  ", steps.ToArray()));
        }
        // other pages stay paragraphs
        var enemy = Codex.Find("enemy_space_fighter_1");
        Check("only ships are bulleted", enemy != null && !CodexCatalogue.IsBulleted(enemy));
    }

    static bool HasBullet(string[] bullets, string prefix, params string[] parts)
    {
        foreach (string b in bullets)
        {
            if (!b.StartsWith(prefix)) continue;
            foreach (string p in parts) if (!b.Contains(p)) return false;
            return true;
        }
        return false;
    }

    static void Page(CodexPanel panel)
    {
        foreach (var (name, safe) in Screens)
        {
            panel.ApplyLayout(safe);
            int worstSize = 99;
            var bad = new List<string>();
            foreach (int ship in ShipId.All)
            {
                var e = Codex.Find(CodexCatalogue.ShipPrefix + ShipId.KeyOf(ship));
                panel.ShowDetail(e);
                Canvas.ForceUpdateCanvases();
                var t = panel.DetailLore;
                var marks = panel.LoreMarks;
                int count = e.lore.Split('\n').Length;
                int size = FitSize(t);
                worstSize = Mathf.Min(worstSize, size);
                if (size < CodexPanel.ReadableLore) bad.Add(ShipId.KeyOf(ship) + " text size " + size);
                if (marks.Shown != count) bad.Add(ShipId.KeyOf(ship) + " markers " + marks.Shown + "/" + count);
                // the gutter keeps the text clear of the markers; each marker sits inside the lore card
                var card = panel.DetailLoreCard.rectTransform.rect;
                for (int i = 0; i < marks.Shown; i++)
                {
                    var m = marks.Mark(i).rectTransform;
                    Vector3 p = panel.DetailLoreCard.rectTransform.InverseTransformPoint(m.position);
                    if (p.x < card.xMin || p.x > card.xMax || p.y < card.yMin || p.y > card.yMax) bad.Add(ShipId.KeyOf(ship) + " marker " + i + " outside the card");
                }
                if (t.rectTransform.offsetMin.x < 30f + CodexBulletMarks.Gutter - .5f) bad.Add(ShipId.KeyOf(ship) + " gutter missing");
                panel.ShowGrid();
            }
            Check(name + ": every ship page fits at >= " + CodexPanel.ReadableLore + " (smallest " + worstSize + ") with its markers" +
                  (bad.Count > 0 ? " (" + string.Join("; ", bad) + ")" : ""), bad.Count == 0);

            // a paragraph page gets no markers and the normal inset
            panel.ShowDetail(Codex.Find("enemy_space_fighter_1"));
            Canvas.ForceUpdateCanvases();
            Check(name + ": a non-ship page has no markers and no gutter",
                  panel.LoreMarks.Shown == 0 && Mathf.Abs(panel.DetailLore.rectTransform.offsetMin.x - 30f) < .5f);
            panel.ShowGrid();
        }
    }

    // The largest best-fit size (canvas units) at which the lore fits its rect.
    static int FitSize(Text t)
    {
        var rect = t.rectTransform.rect.size;
        var gen = new TextGenerator();
        for (int size = t.resizeTextMaxSize; size >= t.resizeTextMinSize; size--)
        {
            var s = t.GetGenerationSettings(new Vector2(rect.x, 100000f));
            s.scaleFactor = 1f; s.resizeTextForBestFit = false; s.fontSize = size;
            s.verticalOverflow = VerticalWrapMode.Overflow; s.generateOutOfBounds = true;
            if (gen.GetPreferredHeight(t.text, s) <= rect.y + .5f) return size;
        }
        return 0;
    }
}
