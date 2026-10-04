using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the real Flight Complete panel from gameS1's own Texts and Buttons
// (edit mode, no Play mode needed), jumps it to its settled end state and
// checks the layout invariants the old panel broke: no overlapping cards,
// everything inside the frame, nothing left over from the scene's layout
// groups, properly sized equal-width buttons, no stray particles, and
// animation driven by unscaled time.
public static class DeathPanelTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DPT] PASS  " : "[DPT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        CheckStaticLayout();
        CheckFit();
        CheckUnscaledTime();

        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        CheckBuiltPanel();

        Debug.Log("[DPT] failures: " + fails);
        return fails;
    }

    // ---- The layout constants themselves ----

    static void CheckStaticLayout()
    {
        var panel = DeathPanelView.PanelRect;
        var inner = Inset(panel, 16f);

        var rows = new List<KeyValuePair<string, Rect>>
        {
            new KeyValuePair<string, Rect>("header", DeathPanelView.HeaderRect),
            new KeyValuePair<string, Rect>("divider", DeathPanelView.DividerRect),
        };
        for (int i = 0; i < DeathPanelView.CardRects.Length; i++)
            rows.Add(new KeyValuePair<string, Rect>("card" + i, DeathPanelView.CardRects[i]));
        rows.Add(new KeyValuePair<string, Rect>("replay", DeathPanelView.ReplayRect));
        rows.Add(new KeyValuePair<string, Rect>("menu", DeathPanelView.MenuRect));

        foreach (var row in rows)
            Check(row.Key + " sits inside the panel with padding", Contains(inner, row.Value));

        for (int i = 0; i < rows.Count; i++)
            for (int j = i + 1; j < rows.Count; j++)
                Check(rows[i].Key + " does not overlap " + rows[j].Key, !rows[i].Value.Overlaps(rows[j].Value));

        var cards = DeathPanelView.CardRects;
        for (int i = 0; i + 1 < cards.Length; i++)
            Check("card" + i + " / card" + (i + 1) + " gap is >= 12", cards[i].yMin - cards[i + 1].yMax >= 12f);
        bool sameColumn = true;
        foreach (var c in cards) sameColumn &= Mathf.Approximately(c.xMin, cards[0].xMin) && Mathf.Approximately(c.width, cards[0].width);
        Check("cards share one column (same x and width)", sameColumn);
        Check("cards are horizontally centred", Mathf.Abs(cards[0].center.x) < .01f);

        var replay = DeathPanelView.ReplayRect;
        var menu = DeathPanelView.MenuRect;
        Check("buttons have equal width", Mathf.Approximately(replay.width, menu.width));
        Check("buttons have equal height", Mathf.Approximately(replay.height, menu.height));
        Check("buttons share one row", Mathf.Approximately(replay.center.y, menu.center.y));
        Check("button pair is centred", Mathf.Abs(replay.center.x + menu.center.x) < .01f);
        Check("button pair spans the card column",
              Mathf.Abs(replay.xMin - cards[0].xMin) < .01f && Mathf.Abs(menu.xMax - cards[0].xMax) < .01f);
        // 96 canvas units ~= 44pt on a 375pt-wide phone (canvas is 800 units wide).
        Check("buttons meet the ~44pt tap target (>= 96 units)", replay.height >= 96f && replay.width >= 96f);
        Check("intro is short (<= 1.25s)", DeathPanelView.IntroDuration <= 1.25f);
    }

    // ---- Fitting to screens / safe areas / the top-right quick actions ----

    static void CheckFit()
    {
        var cases = new[]
        {
            ("tall phone 1080x2340", new Rect(-400f, -866f, 800f, 1733f), (Rect?)null),
            ("9:22 phone 1080x2640", new Rect(-400f, -978f, 800f, 1956f), (Rect?)null),
            ("9:24 phone 1080x2880", new Rect(-400f, -1067f, 800f, 2133f), (Rect?)null),
            ("Z Fold cover 968x2376", new Rect(-400f, -982f, 800f, 1964f), (Rect?)null),
            ("phone with notch + home bar", new Rect(-400f, -820f, 800f, 1640f), (Rect?)null),
            ("iPad 1536x2048", new Rect(-400f, -533f, 800f, 1066f), (Rect?)null),
            ("landscape Mac window", new Rect(-480f, -300f, 960f, 600f), (Rect?)null),
            ("iPad with quick actions top-right", new Rect(-400f, -533f, 800f, 1066f),
                (Rect?)new Rect(220f, 340f, 170f, 180f)),
        };
        foreach (var (name, safe, blocker) in cases)
        {
            DeathPanelView.ComputeFit(safe, blocker, out var centre, out var scale);
            float w = (DeathPanelView.Width + 2f * DeathPanelView.GlowMargin) * scale;
            float h = (DeathPanelView.Height + 2f * DeathPanelView.GlowMargin) * scale;
            var visual = new Rect(centre.x - w * .5f, centre.y - h * .5f, w, h);
            Check(name + ": panel and glow fit the safe area", Contains(safe, visual));
            Check(name + ": never scaled above 1", scale <= 1f);
            if (blocker.HasValue)
                Check(name + ": clears the quick actions", !blocker.Value.Overlaps(visual));
            if (safe.height > 1000f && !blocker.HasValue)
                Check(name + ": portrait phone keeps full size (tap targets intact)", Mathf.Approximately(scale, 1f));
        }
    }

    // ---- Source checks: everything animated on unscaled time ----

    static void CheckUnscaledTime()
    {
        var scaled = new Regex(@"Time\.(time|deltaTime|fixedDeltaTime|smoothDeltaTime)\b|WaitForSeconds\(");
        foreach (var path in new[] { "Assets/Scripts/UI/DeathPanelView.cs", "Assets/Scripts/UI/DeathPanelPress.cs",
                                     "Assets/Scripts/Gameplay/playerIsDead.cs" })
        {
            string src = File.ReadAllText(path);
            Check(System.IO.Path.GetFileName(path) + " never reads scaled time", !scaled.IsMatch(src));
        }
        Check("DeathPanelView animates on Time.unscaledTime",
              File.ReadAllText("Assets/Scripts/UI/DeathPanelView.cs").Contains("Time.unscaledTime"));
        Check("DeathPanelPress springs on Time.unscaledDeltaTime",
              File.ReadAllText("Assets/Scripts/UI/DeathPanelPress.cs").Contains("Time.unscaledDeltaTime"));
    }

    // ---- The real panel, built from gameS1's objects ----

    static void CheckBuiltPanel()
    {
        var canvas = SceneUtil.FindAny("PopUpCanvas");
        var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        var run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var replay = SceneUtil.FindAny("Replay").GetComponent<Button>();
        var menu = SceneUtil.FindAny("MainMenu").GetComponent<Button>();

        // Wide numbers on purpose: the layout has to hold up at its worst.
        var parts = new RunScore.Breakdown
        {
            distance = 999999, kills = 999999, dust = 99999, atoms = 9999, teleports = 9999,
            bosses = 99999, worlds = 99999,
            killCount = 999, dustCount = 999, atomCount = 99, teleportCount = 99, bossCount = 9, worldCount = 9,
        };
        var view = DeathPanelView.Build(canvas.transform, best, run, dust, replay, menu, new DeathPanelView.Results
        {
            score = 9999999, bestScore = 9999999, newBest = true, ranked = true, parts = parts,
            bestSpeed = 999, runSpeed = 999, dustAtStart = 99987.65f, dustWon = 12.34f,
        });
        view.Skip();
        Canvas.ForceUpdateCanvases();

        var panel = view.Panel;
        Check("intro skip lands on the end state", view.IntroFinished);
        Check("old scene dialog (Model Panel) is switched off", !canvas.transform.Find("Model Panel").gameObject.activeSelf);
        Check("panel sits at full intro scale after skip",
              Mathf.Abs(panel.localScale.x - panel.localScale.y) < .001f && panel.localScale.x > .5f);

        // Values: final numbers, formatted on separate lines for star dust.
        Check("the score headline shows its final value (got '" + best.text + "')", best.text == "9,999,999");
        Check("the speed line shows this run's and the best speed (got '" + run.text + "')",
              run.text == "SPEED 999  /  BEST 999");
        var labels = DeathPanelView.BreakdownLabels;
        var points = DeathPanelView.BreakdownPoints(parts);
        Check("the breakdown lists distance, kills, star dust, atoms, teleports, bosses, worlds",
              string.Join(",", labels) == "DISTANCE,KILLS,STAR DUST,ATOMS,TELEPORTS,BOSSES,WORLDS");
        for (int i = 0; i < labels.Length; i++)
        {
            var row = panel.Find("Card1/Row" + i);
            var pts = row != null ? row.Find("Points").GetComponent<Text>() : null;
            Check("breakdown row " + labels[i] + " shows its points",
                  pts != null && pts.text == "+" + RunScore.Format(points[i]) &&
                  row.Find("Label").GetComponent<Text>().text == labels[i]);
        }
        Check("star dust earned shows +12.34 (got '" + dust.text + "')", dust.text == "+12.34");
        var total = dust.transform.parent.Find("Total").GetComponent<Text>();
        Check("total sits on its own line (got '" + total.text + "')", total.text == "TOTAL  99999.99" && !dust.text.Contains("\n"));

        var panelRect = DeathPanelView.PanelRect;
        foreach (var t in new[] { best, run, dust })
        {
            Check(t.name + " moved into the new panel", t.transform.IsChildOf(panel));
            Check(t.name + " is active", t.gameObject.activeInHierarchy || !canvas.activeInHierarchy);
            Check(t.name + " font is readable (>=20)", t.fontSize >= 20);
            Check(t.name + " is no longer driven by a layout group", t.GetComponentInParent<LayoutGroup>() == null);
        }

        // Every visible element sits inside the panel body (glows excepted,
        // they are meant to bloom past the edge).
        foreach (var g in panel.GetComponentsInChildren<Graphic>(false))
        {
            if (g.name == "Frame" || g.name == "Glow" || g.name == "NewBestGlow" ||
                g.name == "DustBurst" || g.name == "BestBurst") continue;
            var r = PanelSpace(panel, g.rectTransform);
            Check(NodePath(g.transform, panel) + " is inside the panel", Contains(Inset(panelRect, -0.5f), r));
            if (g is Text text && text.horizontalOverflow == HorizontalWrapMode.Overflow)
                Check(NodePath(g.transform, panel) + " text fits its rect ('" + text.text + "')",
                      text.preferredWidth <= r.width + 1f);
        }

        // Cards: laid out where the constants say, not overlapping, and the
        // label column never runs into the value column.
        var cardRects = new List<Rect>();
        for (int i = 0; i < 3; i++)
        {
            var card = (RectTransform)panel.Find("Card" + i);
            var r = PanelSpace(panel, card);
            cardRects.Add(r);
            Check("Card" + i + " is where the layout puts it", Near(r, DeathPanelView.CardRects[i]));
            var label = card.Find("Label").GetComponent<Text>();
            var value = i == 0 ? best : i == 1 ? run : dust;
            float labelRight = PanelSpace(panel, label.rectTransform).xMin + label.preferredWidth;
            float valueLeft = PanelSpace(panel, value.rectTransform).xMax - value.preferredWidth;
            Check("Card" + i + " label and value keep apart", labelRight + 16f <= valueLeft);
        }
        // Breakdown rows: label, count and points never run into each other.
        for (int i = 0; i < DeathPanelView.BreakdownLabels.Length; i++)
        {
            var row = panel.Find("Card1/Row" + i);
            if (row == null) { Check("breakdown row " + i + " exists", false); continue; }
            var l = row.Find("Label").GetComponent<Text>();
            var c = row.Find("Count").GetComponent<Text>();
            var p = row.Find("Points").GetComponent<Text>();
            float lRight = PanelSpace(panel, l.rectTransform).xMin + l.preferredWidth;
            float cLeft = PanelSpace(panel, c.rectTransform).xMax - c.preferredWidth;
            float cRight = PanelSpace(panel, c.rectTransform).xMax;
            float pLeft = PanelSpace(panel, p.rectTransform).xMax - p.preferredWidth;
            Check("breakdown row " + i + " label / count / points keep apart",
                  lRight + 12f <= cLeft && cRight + 12f <= pLeft);
            Check("breakdown row " + i + " sits inside its card",
                  Contains(cardRects[1], PanelSpace(panel, (RectTransform)row)));
        }
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                Check("Card" + i + " does not overlap Card" + j, !cardRects[i].Overlaps(cardRects[j]));

        var pill = panel.Find("Card0/NewBest");
        Check("NEW BEST badge is shown for a record run", pill != null && pill.gameObject.activeSelf && pill.localScale.x > .9f);

        // Buttons: the scene's own Buttons, re-skinned, still wired.
        var rr = PanelSpace(panel, (RectTransform)replay.transform);
        var mr = PanelSpace(panel, (RectTransform)menu.transform);
        Check("Replay button is the scene button, inside the panel", replay.transform.IsChildOf(panel));
        Check("buttons have equal width (" + rr.width + " / " + mr.width + ")", Mathf.Abs(rr.width - mr.width) < .5f);
        Check("buttons are >= 96 units tall", rr.height >= 96f && mr.height >= 96f);
        Check("buttons are not scaled down by the scene's old 0.44 scale",
              Mathf.Abs(replay.transform.localScale.x - 1f) < .01f && Mathf.Abs(menu.transform.localScale.x - 1f) < .01f);
        Check("buttons don't overlap", !rr.Overlaps(mr));
        foreach (var c in cardRects) Check("buttons clear the cards", !rr.Overlaps(c) && !mr.Overlaps(c));
        Check("Replay still calls buttonClicks.replay",
              replay.onClick.GetPersistentEventCount() > 0 && replay.onClick.GetPersistentMethodName(0) == "replay");
        Check("Menu still calls buttonClicks.mainMenuButton",
              menu.onClick.GetPersistentEventCount() > 0 && menu.onClick.GetPersistentMethodName(0) == "mainMenuButton");
        Check("buttons have press feedback", replay.GetComponent<DeathPanelPress>() != null && menu.GetComponent<DeathPanelPress>() != null);
        Check("Replay shows the quick actions' replay glyph", GlyphName(replay) == "QuickAction_replay_glyph");
        Check("Menu shows the quick actions' home glyph", GlyphName(menu) == "QuickAction_home_glyph");
        foreach (var b in new[] { replay, menu })
            foreach (var img in b.GetComponentsInChildren<Image>(true))
                Check(b.name + " no longer uses the old clip-art (" + img.name + ")",
                      img.sprite == null || (img.sprite.name != "redo-512" && img.sprite.name != "taxes-menu-icon"));

        // Sprites came from the SVG-sourced set.
        var frame = panel.Find("Frame").GetComponent<Image>();
        Check("frame uses the 9-sliced dp_panel sprite",
              frame.sprite != null && frame.sprite.name == "dp_panel" && frame.sprite.border.x > 0f && frame.type == Image.Type.Sliced);

        // No stray particles once the intro has settled.
        int liveSparkles = 0;
        foreach (var g in panel.GetComponentsInChildren<Graphic>(false))
            if (g.name == "DustBurst" || g.name == "BestBurst") liveSparkles++;
        Check("no sparkle particles linger after the intro (" + liveSparkles + ")", liveSparkles == 0);
        Check("old dust-bit squares are gone", SceneUtil.FindAny("DustBit") == null && SceneUtil.FindAny("DeathResultsBadge") == null);

        // Mid-intro the panel is still animating and can be skipped again.
        view.ApplyAt(.3f);
        Check("mid-intro state is not final", !view.IntroFinished);
        view.Skip();
        Check("a second skip settles again", view.IntroFinished && dust.text == "+12.34" && best.text == "9,999,999");

        // The quick-action hit test must still be callable.
        bool threw = false;
        try { PauseQuickActions.IsScreenPointOnAction(Vector2.zero); } catch (System.Exception) { threw = true; }
        Check("PauseQuickActions.IsScreenPointOnAction still works", !threw);
    }

    // ---- helpers ----

    static Rect PanelSpace(RectTransform panel, RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = panel.InverseTransformPoint(corners[0]);
        Vector2 max = panel.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    static string NodePath(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    static string GlyphName(Button b)
    {
        var icon = b.transform.Find("Icon");
        var img = icon != null ? icon.GetComponent<Image>() : null;
        return img != null && img.sprite != null ? img.sprite.name : "(none)";
    }

    static Rect Inset(Rect r, float by) { return Rect.MinMaxRect(r.xMin + by, r.yMin + by, r.xMax - by, r.yMax - by); }
    static bool Contains(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin - .01f && inner.xMax <= outer.xMax + .01f &&
               inner.yMin >= outer.yMin - .01f && inner.yMax <= outer.yMax + .01f;
    }
    static bool Near(Rect a, Rect b)
    {
        return Mathf.Abs(a.xMin - b.xMin) < .5f && Mathf.Abs(a.yMin - b.yMin) < .5f &&
               Mathf.Abs(a.width - b.width) < .5f && Mathf.Abs(a.height - b.height) < .5f;
    }
}
