using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The unified leaderboard (docs/leaderboards.md, "Unified panel" and "Season reset"):
//   1  the score tag: run columns round trip, old tags still parse, the worst case stays under 64 characters
//   2  the panel: two tabs, three sort headers, a column switch loads THAT board (never a client re-sort), a stale
//      answer is ignored, dashes where the tag has nothing, a disabled board's header is not offered, tap targets
//   3  the season reset: a no-op at the current value, a bump clears marks and bests, backfill cannot offer old bests
public static class UnifiedLeaderboardTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ULB] PASS  " : "[ULB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string ScoreAndroid = "CgkUScore", DustAndroid = "CgkUDust", WorldsAndroid = "CgkUWorlds", BoostedAndroid = "CgkUBoosted";
    static float now;
    static readonly Vector2 Phone = new Vector2(1080f, 2340f);
    static readonly Rect PhoneSafe = new Rect(0f, 63f, 1080f, 2202f);

    // All four boards live on Android; on iOS only Top Score (the shipped situation).
    static LeaderboardBoard[] FullTable()
    {
        return new[]
        {
            new LeaderboardBoard(LeaderboardBoards.TopScore, ScoreAndroid, AchievementIds.IosPrefix + "top_score", "Top Score", "s",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatScore, r => r.score, 1),
            new LeaderboardBoard(LeaderboardBoards.RunStarDust, DustAndroid, "", "Star Dust", "d",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatHundredths, r => (long)System.Math.Round(r.starDust * 100.0), 1),
            new LeaderboardBoard(LeaderboardBoards.FurthestWorld, WorldsAndroid, "", "Worlds", "w",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatWorlds, r => System.Math.Max(0, r.worldsCleared), 1),
            new LeaderboardBoard(LeaderboardBoards.BoostedRuns, BoostedAndroid, "", "Power-Ups", "b",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatScore, r => r.score, 1, true),
        };
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        LeaderboardBoards.OverrideForTests(FullTable());
        try
        {
            Tags();
            Panel();
            Season();
        }
        finally
        {
            LeaderboardBoards.OverrideForTests(null);
            LeaderboardBoards.OverrideSeasonForTests(null);
            LeaderboardPanel.Close();
        }
        Debug.Log("[ULB] failures: " + failures);
        return failures;
    }

    static void Context()
    {
        foreach (string k in new[]
        {
            LeaderboardService.PendingKey, LeaderboardService.SubmittedKey, LeaderboardService.NormalBestKey, LeaderboardService.BoostedBestKey,
            LeaderboardService.BoundPlayerKey, LeaderboardService.SeasonKey, RunScore.BestScoreKey, AccountLink.DisconnectedKey,
        }) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        LeaderboardPanel.Close();
    }

    static LeaderboardService Service(FakeLeaderboards fake, bool ios = false, string player = null)
    {
        now = 0f;
        var service = new LeaderboardService(fake, () => now) { Ios = ios, PlayerId = () => player };
        LeaderboardService.Instance = service;
        return service;
    }

    // ---- 1 tags ----

    static void Tags()
    {
        int ship, hearts, end, pauses, skin, worlds, powerUps;
        long dust, score;

        string tag = LeaderboardRunTag.Encode(7, 4, 2, 3, 2, 123456, 12, 0, 987654);
        Check("tag: a normal run's columns are d, w and r", tag == "s7h4e2p3k2d123456w12r987654");
        Check("tag: round trip", LeaderboardRunTag.TryParse(tag, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps, out score)
              && ship == 7 && hearts == 4 && end == 2 && pauses == 3 && skin == 2 && dust == 123456 && worlds == 12 && powerUps == 0 && score == 987654);
        Check("tag: a score of 0 is recorded as 0, not unknown", LeaderboardRunTag.Encode(7, 4, -1, -1, 0, 0, 0, 0, 0) == "s7h4d0w0r0"
              && LeaderboardRunTag.HasRunScore("s7h4d0w0r0"));

        // Old tags: no r, and the d / w / u of a Power-Ups tag parse as before.
        string[] old = { "s7h4", "s7h4e2", "s7h4e2p3", "s7h4e2p3k2", "s7h4e2p3k2d123456w12u3" };
        bool oldOk = true;
        foreach (string o in old)
            oldOk &= LeaderboardRunTag.TryParse(o, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps, out score)
                     && ship == 7 && hearts == 4 && score == -1 && !LeaderboardRunTag.HasRunScore(o);
        Check("tag: old tags still parse, with no run score", oldOk);
        Check("tag: the Power-Ups tag is unchanged and carries no r",
              LeaderboardRunTag.Encode(7, 4, 2, 3, 2, 123456, 12, LeaderboardRunTag.PowerAll) == "s7h4e2p3k2d123456w12u3");
        // An older build's parser (the 9-argument one, and any reader that skips unknown letters) still reads the new tag.
        Check("tag: the older parse overloads still read a tag with r",
              LeaderboardRunTag.TryParse(tag, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps)
              && ship == 7 && dust == 123456 && worlds == 12
              && LeaderboardRunTag.TryParse("s7h4e2r500", out ship, out hearts, out end) && ship == 7 && end == 2
              && LeaderboardRunTag.TryParse("s7h4e2r500q9", out ship, out hearts, out end) && end == 2);
        Check("tag: Paragon is never encoded with a score", LeaderboardRunTag.Encode(PremiumShips.Paragon, 14, 1, 1, 0, 5, 1, 0, 5) == "");
        Check("tag: an absurd r is clamped, an over-long one reads as the maximum",
              LeaderboardRunTag.Encode(7, 4, -1, -1, 0, -1, -1, 0, long.MaxValue) == "s7h4r" + LeaderboardRunTag.MostScore
              && LeaderboardRunTag.TryParse("s7h4r99999999999999", out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps, out score)
              && score == LeaderboardRunTag.MostScore);

        // Worst case: every field at its maximum (a real tag never has both u and r, so this is an upper bound).
        int maxShip = 1;
        for (int i = 1; i <= shopingShips.RosterTotal; i++) if (ShipId.IsValid(i) && !PremiumShips.IsPremium(i)) maxShip = i;
        string worst = LeaderboardRunTag.Encode(maxShip, ShipLives.MostHearts, ShipLives.MostHearts, LeaderboardRunTag.MostPauses, LeaderboardRunTag.MostSkin,
                                               long.MaxValue, int.MaxValue, LeaderboardRunTag.PowerAll, long.MaxValue);
        Check("tag: worst-case length " + worst.Length + " is within the 64-character Play Games limit", worst.Length <= LeaderboardRunTag.MaxLength);
        Check("tag: the worst case parses back",
              LeaderboardRunTag.TryParse(worst, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps, out score)
              && dust == LeaderboardRunTag.MostDust && worlds == LeaderboardRunTag.MostWorlds && score == LeaderboardRunTag.MostScore);
        // The widest string the parser accepts at all (4-digit ship / hearts / ...) is under the limit too.
        string widest = "s9999h9999e9999p9999k9999d" + LeaderboardRunTag.MostDust + "w9999u3r" + LeaderboardRunTag.MostScore;
        Check("tag: the widest string the parser accepts is " + widest.Length + " characters, under 64", widest.Length < LeaderboardRunTag.MaxLength);
        // And what a real run's stats write.
        var big = new LeaderboardRunStats
        {
            score = long.MaxValue, starDust = 1e9f, dustEarned = 1e9f, worldsCleared = int.MaxValue, startShip = maxShip, startHearts = ShipLives.MostHearts,
            startSkin = LeaderboardRunTag.MostSkin, hasEnd = true, endHearts = ShipLives.MostHearts, hasPauses = true, endPauses = 99999,
        };
        Check("tag: a maxed normal run's tag fits (" + big.Tag.Length + ")", big.Tag.Length > 0 && big.Tag.Length <= LeaderboardRunTag.MaxLength
              && System.Text.RegularExpressions.Regex.IsMatch(big.Tag, "^[a-z0-9]+$"));
        big.powerUps = LeaderboardRunTag.PowerAll;
        Check("tag: a maxed Power-Ups run's tag fits (" + big.Tag.Length + ")", big.Tag.Length > 0 && big.Tag.Length <= LeaderboardRunTag.MaxLength);

        // Stats -> tag: normal runs carry the ranked dust (no booster doubling) and the score; powered-up runs the dust earned.
        var run = new LeaderboardRunStats { score = 4200, starDust = 2.5f, dustEarned = 5f, worldsCleared = 3, startShip = 7, startHearts = 4 };
        Check("stats: a normal run writes d250 w3 r4200", run.Tag == "s7h4d250w3r4200");
        run.powerUps = LeaderboardRunTag.PowerDust;
        Check("stats: a powered-up run writes the dust it earned and its power-ups, no r", run.Tag == "s7h4d500w3u1");

        // A tagless old row is upgraded by a tie that carries the run columns (the row can now show them).
        Context();
        var marks = new LeaderboardService.ScoreTable();
        marks.Set(LeaderboardBoards.FurthestWorld, 5, "s7h4e1p2");
        marks.Save(LeaderboardService.SubmittedKey);
        var service = Service(new FakeLeaderboards());
        Check("tag: a tie that adds the run columns replaces an older tag", service.Offer(LeaderboardBoards.FurthestWorld, 5, "s7h4e1p2d100w5r900"));
        Check("tag: a tie with nothing new is ignored", !service.Offer(LeaderboardBoards.FurthestWorld, 5, "s7h4e1p2"));
    }

    // ---- 2 panel ----

    static string Cell(Transform root, string row, string board)
    {
        var t = root.Find(row + "/Cell_" + board);
        return t != null ? t.GetComponent<Text>().text : null;
    }

    static LeaderboardEntry Tagged(int rank, string name, long value, long score, long dustHundredths, int worlds, bool local = false)
    {
        return FakeLeaderboards.Entry(rank, name, value, local, 7, 4, 2, 3, 0, dustHundredths, worlds, 0, score);
    }

    static void Panel()
    {
        Context();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string score = ScoreAndroid, dust = DustAndroid, worlds = WorldsAndroid;
        var fake = new FakeLeaderboards();
        // The same pilots, ranked differently on each board: sorting must show each store board's own order.
        fake.SetBoard(score,
            Tagged(1, "ALPHA", 9000, 9000, 12000, 7),
            Tagged(2, "BRAVO", 8000, 8000, 99000, 12),
            FakeLeaderboards.Entry(3, "OLDIE", 7000));
        fake.SetBoard(dust,
            Tagged(1, "BRAVO", 99000, 8000, 99000, 12),
            Tagged(2, "ALPHA", 12000, 9000, 12000, 7),
            FakeLeaderboards.Entry(3, "OLDIE", 500));
        fake.SetBoard(worlds,
            Tagged(1, "BRAVO", 12, 8000, 99000, 12),
            FakeLeaderboards.Entry(2, "OLDIE", 9),
            Tagged(3, "ALPHA", 7, 9000, 12000, 7));
        fake.SetBoard(BoostedAndroid, Tagged(1, "BOOST", 6000, -1, 4000, 3));
        var service = Service(fake);
        var panel = LeaderboardPanel.Open(service, null, Phone, PhoneSafe);
        var root = panel.PanelRoot;

        Check("panel: two tabs, MAIN then Power-Ups", panel.Tabs.Count == 2 && panel.TabBoards[0] == LeaderboardPanel.MainTab && panel.TabBoards[1] == LeaderboardBoards.BoostedRuns);
        Check("panel: three sort headers, SCORE DUST WORLDS in that order",
              panel.Headers.Count == 3 && panel.HeaderBoards[0] == LeaderboardBoards.TopScore && panel.HeaderBoards[1] == LeaderboardBoards.RunStarDust
              && panel.HeaderBoards[2] == LeaderboardBoards.FurthestWorld);
        Check("panel: opens on the main tab sorted by SCORE",
              panel.SelectedTab == LeaderboardPanel.MainTab && panel.SortedBoard == LeaderboardBoards.TopScore && panel.CurrentState == LeaderboardPanel.State.Populated);
        Check("panel: the header labels", root.Find("Header_" + LeaderboardBoards.TopScore + "/Label").GetComponent<Text>().text == "SCORE"
              && root.Find("Header_" + LeaderboardBoards.RunStarDust + "/Label").GetComponent<Text>().text == "DUST"
              && root.Find("Header_" + LeaderboardBoards.FurthestWorld + "/Label").GetComponent<Text>().text == "WORLDS");

        // Score sort: score board order; the row carries all three numbers from its tag.
        var rows = root.Find("List/Rows");
        Check("score sort: ranks and names are the Top Score board's", rows.Find("Row0/Name").GetComponent<Text>().text == "ALPHA" && rows.Find("Row1/Name").GetComponent<Text>().text == "BRAVO");
        Check("score sort: the row shows score, dust and worlds from one run",
              Cell(rows, "Row0", LeaderboardBoards.TopScore) == "9,000" && Cell(rows, "Row0", LeaderboardBoards.RunStarDust) == "120"
              && Cell(rows, "Row0", LeaderboardBoards.FurthestWorld) == "7");
        Check("score sort: an old row (no tag) shows the board's value and dashes",
              Cell(rows, "Row2", LeaderboardBoards.TopScore) == "7,000" && Cell(rows, "Row2", LeaderboardBoards.RunStarDust) == LeaderboardPanel.Dash
              && Cell(rows, "Row2", LeaderboardBoards.FurthestWorld) == LeaderboardPanel.Dash);
        Check("score sort: the active header is marked (red plate), the others are not",
              HeaderFill(panel, 0) == AkiraPalette.Red && HeaderFill(panel, 1) == AkiraPalette.Card && HeaderFill(panel, 2) == AkiraPalette.Card);

        // Column switch: loads THAT board, with its own ranks (not a re-sort of what was loaded).
        panel.Headers[1].onClick.Invoke();
        rows = root.Find("List/Rows");
        Check("dust sort: loads the Star Dust board", panel.SelectedBoard == LeaderboardBoards.RunStarDust && panel.SortedBoard == LeaderboardBoards.RunStarDust);
        Check("dust sort: the board's own order (BRAVO is #1, who is #2 by score)",
              rows.Find("Row0/Name").GetComponent<Text>().text == "BRAVO" && rows.Find("Row0/Rank").GetComponent<Text>().text == "#1"
              && rows.Find("Row1/Name").GetComponent<Text>().text == "ALPHA" && rows.Find("Row1/Rank").GetComponent<Text>().text == "#2");
        Check("dust sort: the sorted cell is the board's own value, the others come from the tag",
              Cell(rows, "Row0", LeaderboardBoards.RunStarDust) == "990" && Cell(rows, "Row0", LeaderboardBoards.TopScore) == "8,000"
              && Cell(rows, "Row0", LeaderboardBoards.FurthestWorld) == "12");
        Check("dust sort: the old row has its dust value and dashes elsewhere (no merge across boards)",
              Cell(rows, "Row2", LeaderboardBoards.RunStarDust) == "5" && Cell(rows, "Row2", LeaderboardBoards.TopScore) == LeaderboardPanel.Dash
              && Cell(rows, "Row2", LeaderboardBoards.FurthestWorld) == LeaderboardPanel.Dash);
        Check("dust sort: the header mark moved", HeaderFill(panel, 1) == AkiraPalette.Red && HeaderFill(panel, 0) == AkiraPalette.Card);

        panel.Headers[2].onClick.Invoke();
        rows = root.Find("List/Rows");
        Check("worlds sort: the Worlds board's order and plain numbers",
              panel.SelectedBoard == LeaderboardBoards.FurthestWorld && rows.Find("Row0/Name").GetComponent<Text>().text == "BRAVO"
              && Cell(rows, "Row0", LeaderboardBoards.FurthestWorld) == "12" && rows.Find("Row1/Name").GetComponent<Text>().text == "OLDIE"
              && Cell(rows, "Row1", LeaderboardBoards.FurthestWorld) == "9" && rows.Find("Row0/Value") == null);

        // The native UI button opens the board that is currently selected.
        panel.Headers[1].onClick.Invoke();
        panel.ViewAllButton.onClick.Invoke();
        Check("view all: opens the currently selected (sorted) board", fake.NativeUICalls.Count == 1 && fake.NativeUICalls[0] == dust);

        // Power-Ups tab: unchanged behaviour, headers hidden; MAIN returns to the last sort.
        panel.Tabs[1].onClick.Invoke();
        Check("power-ups tab: loads the Boosted board and hides the sort headers",
              panel.SelectedBoard == LeaderboardBoards.BoostedRuns && panel.SortedBoard == null && !panel.Headers[0].gameObject.activeSelf
              && root.Find("List/Rows/Row0/Stats") != null && root.Find("List/Rows/Row0/Value").GetComponent<Text>().text == "6,000");
        panel.Tabs[0].onClick.Invoke();
        Check("main tab again: back on the last sort (dust)", panel.SelectedBoard == LeaderboardBoards.RunStarDust && panel.Headers[0].gameObject.activeSelf);

        // A stale response is ignored: tapping a header while an earlier load is still out.
        panel.Headers[0].onClick.Invoke();
        fake.DeferLoads = true;
        panel.Headers[1].onClick.Invoke();                // request A (dust): waits
        fake.DeferLoads = false;
        panel.Headers[2].onClick.Invoke();                // request B (worlds): answers at once
        fake.CompleteLoads();                             // A answers late
        rows = root.Find("List/Rows");
        Check("stale: the late dust answer does not overwrite the worlds table",
              panel.SelectedBoard == LeaderboardBoards.FurthestWorld && panel.CurrentState == LeaderboardPanel.State.Populated
              && rows.Find("Row0/Name").GetComponent<Text>().text == "BRAVO" && Cell(rows, "Row0", LeaderboardBoards.FurthestWorld) == "12");

        // Own row: filled from the device record, dashes where it has nothing.
        Context();
        var mine = new FakeLeaderboards();
        mine.SetBoard(dust, FakeLeaderboards.Entry(4, "KANEDA", 4200, true));
        var kept = new LeaderboardService.ScoreTable();
        kept.Set(LeaderboardBoards.RunStarDust, 4200, "s7h4e2d4200w5r1500");
        kept.Save(LeaderboardService.SubmittedKey);
        panel = LeaderboardPanel.Open(Service(mine), null, Phone, PhoneSafe);
        panel.SelectBoard(LeaderboardBoards.RunStarDust);
        var own = panel.PanelRoot.Find("PlayerRow");
        Check("own row: the device record fills score and worlds on the dust sort",
              Cell(own, "Row", LeaderboardBoards.TopScore) == "1,500" && Cell(own, "Row", LeaderboardBoards.FurthestWorld) == "5"
              && Cell(own, "Row", LeaderboardBoards.RunStarDust) == "42");
        var bare = new FakeLeaderboards();
        bare.SetBoard(dust, FakeLeaderboards.Entry(4, "KANEDA", 4200, true));
        Context();
        panel = LeaderboardPanel.Open(Service(bare), null, Phone, PhoneSafe);
        panel.SelectBoard(LeaderboardBoards.RunStarDust);
        own = panel.PanelRoot.Find("PlayerRow");
        Check("own row: without a record the other cells are dashes",
              Cell(own, "Row", LeaderboardBoards.TopScore) == LeaderboardPanel.Dash && Cell(own, "Row", LeaderboardBoards.FurthestWorld) == LeaderboardPanel.Dash);

        // Stores that return no tags and have no Dust / Worlds ids (iOS): only the SCORE header is offered.
        Context();
        var ios = new FakeLeaderboards();
        string iosScore = LeaderboardBoards.Get(LeaderboardBoards.TopScore).PlatformId(true);
        ios.SetBoard(iosScore, FakeLeaderboards.Entry(1, "TETSUO", 5000), FakeLeaderboards.Entry(2, "KANEDA", 4000, true));
        panel = LeaderboardPanel.Open(Service(ios, true), null, Phone, PhoneSafe);
        Check("iOS: only the headers whose board is enabled are offered (just SCORE), and no Power-Ups tab",
              panel.Headers.Count == 1 && panel.HeaderBoards[0] == LeaderboardBoards.TopScore && panel.Tabs.Count == 1);
        Check("iOS: other players' rows show the score and nothing else; one column", panel.CurrentState == LeaderboardPanel.State.Populated
              && Cell(panel.PanelRoot.Find("List/Rows"), "Row0", LeaderboardBoards.TopScore) == "5,000"
              && panel.PanelRoot.Find("List/Rows/Row0/Cell_" + LeaderboardBoards.RunStarDust) == null);

        // A table where the Star Dust board has no id here: no DUST header.
        Context();
        var table = FullTable();
        LeaderboardBoards.OverrideForTests(new[] { table[0], new LeaderboardBoard(LeaderboardBoards.RunStarDust, "", "", "Star Dust", "d", LeaderboardSort.HigherIsBetter,
            LeaderboardBoards.FormatHundredths, r => 0, 1), table[2], table[3] });
        panel = LeaderboardPanel.Open(Service(new FakeLeaderboards()), null, Phone, PhoneSafe);
        Check("a disabled board's header is not offered (no DUST)",
              panel.Headers.Count == 2 && panel.HeaderBoards[0] == LeaderboardBoards.TopScore && panel.HeaderBoards[1] == LeaderboardBoards.FurthestWorld);
        LeaderboardBoards.OverrideForTests(FullTable());

        // Tap targets, type floors and column fit on the full panel.
        Context();
        panel = LeaderboardPanel.Open(Service(fake), null, Phone, PhoneSafe);
        bool taps = true, pads = true;
        foreach (var h in panel.Headers)
        {
            taps &= ((RectTransform)h.transform).rect.height + 2f * LeaderboardPanel.HeaderTouchMargin >= LeaderboardPanel.MinTapUnits - .1f;
            pads &= h.GetComponent<CelShape>().raycastPadding.y <= -LeaderboardPanel.HeaderTouchMargin + .01f;
        }
        Check("headers: 100-unit tap targets (art + touch margin)", taps && pads);
        taps = true;
        foreach (var b in panel.Tabs) taps &= ((RectTransform)b.transform).rect.height + 2f * LeaderboardPanel.TabTouchMargin >= LeaderboardPanel.MinTapUnits - .1f;
        Check("tabs: still 100-unit tap targets", taps);
        bool floors = true;
        foreach (var t in panel.PanelRoot.GetComponentsInChildren<Text>(true))
        {
            bool relevant = t.name.StartsWith("Cell_") || (t.name == "Label" && (t.transform.parent.name.StartsWith("Header_") || t.transform.parent.name.StartsWith("Tab_")));
            if (!relevant) continue;
            int smallest = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
            if (smallest < 16) { floors = false; Debug.Log("[ULB] under 16: " + t.name + " " + smallest); }
        }
        Check("type: cells, headers and tabs never under 16 units", floors);
        float narrow = 626.7f - 80f;   // the 320 dp phone, as the panel's own fit notes
        Check("fit: the name column at the narrowest panel keeps >= 90 units with three columns", narrow - LeaderboardPanel.NameLeft - 14f - 6f - 3f * LeaderboardPanel.ColWidth >= 90f);
        Check("fit: the design-width name column is wide", panel.MainNameWidth >= 150f);
        var ten = new FakeLeaderboards();
        var tenRows = new LeaderboardEntry[10];
        for (int i = 0; i < 10; i++) tenRows[i] = Tagged(i + 1, "PILOT" + i, 9000 - i, 9000 - i, 1000 + i, 3);
        ten.SetBoard(ScoreAndroid, tenRows);
        panel = LeaderboardPanel.Open(Service(ten), null, Phone, PhoneSafe);
        Check("fit: 10 rows fill the list at the design height without scrolling", panel.RowCount == 10 && !panel.ListScrolls);
        LeaderboardPanel.Close();
    }

    static Color HeaderFill(LeaderboardPanel panel, int i) { return panel.Headers[i].GetComponent<CelShape>().fill; }

    // ---- 3 season ----

    static void Season()
    {
        const int Old = 5000;
        System.Action seed = () =>
        {
            Context();
            PlayerPrefs.SetInt(RunScore.BestScoreKey, Old);
            PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, Old);
            PlayerPrefs.SetInt(LeaderboardService.BoostedBestKey, 3000);
            var sub = new LeaderboardService.ScoreTable();
            sub.Set(LeaderboardBoards.TopScore, Old, "s7h4d100w3r5000");
            sub.Set(LeaderboardBoards.RunStarDust, 900, "s7h4d900w3r5000");
            sub.Set(LeaderboardBoards.BoostedRuns, 3000, "s7h4d200w3u1");
            sub.Save(LeaderboardService.SubmittedKey);
            var pend = new LeaderboardService.ScoreTable();
            pend.Set(LeaderboardBoards.FurthestWorld, 9, "s7h4d100w9r6000");
            pend.Save(LeaderboardService.PendingKey);
        };

        // The constant as shipped: Install changes nothing for an existing player.
        Check("season: the shipped constant is the first season (a stored season of none means it)",
              LeaderboardBoards.CurrentSeason == LeaderboardBoards.FirstSeason && LeaderboardBoards.Season == LeaderboardBoards.CurrentSeason);
        seed();
        LeaderboardService.MigrateSeason();
        Check("season: at the current value nothing is cleared and no season is stored",
              LeaderboardService.ScoreTable.Load(LeaderboardService.SubmittedKey).entries.Count == 3
              && LeaderboardService.ScoreTable.Load(LeaderboardService.PendingKey).entries.Count == 1
              && PlayerPrefs.GetInt(LeaderboardService.NormalBestKey, -1) == Old && PlayerPrefs.GetInt(LeaderboardService.BoostedBestKey, -1) == 3000
              && !PlayerPrefs.HasKey(LeaderboardService.SeasonKey));

        // A bump (an override of the constant).
        LeaderboardBoards.OverrideSeasonForTests(2);
        LeaderboardService.MigrateSeason();
        Check("season bump: submitted and pending tables of every board are cleared",
              LeaderboardService.ScoreTable.Load(LeaderboardService.SubmittedKey).entries.Count == 0
              && LeaderboardService.ScoreTable.Load(LeaderboardService.PendingKey).entries.Count == 0);
        Check("season bump: normal and boosted local bests are 0 (present, so they are not re-seeded)",
              PlayerPrefs.HasKey(LeaderboardService.NormalBestKey) && PlayerPrefs.GetInt(LeaderboardService.NormalBestKey, -1) == 0
              && PlayerPrefs.HasKey(LeaderboardService.BoostedBestKey) && PlayerPrefs.GetInt(LeaderboardService.BoostedBestKey, -1) == 0
              && LeaderboardService.LocalBest(false) == 0 && LeaderboardService.LocalBest(true) == 0);
        LeaderboardService.SeedLocalBests();
        Check("season bump: seeding does not bring the old best back", LeaderboardService.LocalBest(false) == 0);
        Check("season bump: the personal best (RunScore.SavedBest) is kept, the season is stored",
              RunScore.SavedBest == Old && PlayerPrefs.GetInt(LeaderboardService.SeasonKey, 0) == 2);

        // Runs once: new marks written after the bump survive another Install.
        var after = new LeaderboardService.ScoreTable();
        after.Set(LeaderboardBoards.TopScore, 123, "s7h4r123");
        after.Save(LeaderboardService.SubmittedKey);
        PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, 123);
        LeaderboardService.MigrateSeason();
        Check("season bump: runs once (a later Install keeps the new season's marks and best)",
              LeaderboardService.ScoreTable.Load(LeaderboardService.SubmittedKey).entries.Count == 1 && PlayerPrefs.GetInt(LeaderboardService.NormalBestKey, -1) == 123);

        // The backfill cannot offer anything from before: a signed-in player binds, and nothing is sent.
        seed();
        LeaderboardBoards.OverrideSeasonForTests(2);
        LeaderboardService.MigrateSeason();
        var fake = new FakeLeaderboards();
        var service = Service(fake, false, "gp-A");
        service.Flush();
        now += 100f;
        service.Tick();
        long v;
        Check("season bump: the account-bind backfill sends nothing (no old best, no old marks)",
              fake.Submissions.Count == 0 && !service.HasPending(LeaderboardBoards.TopScore, out v)
              && PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "gp-A");
        // ... while a run of the new season still goes out and counts as the best.
        Check("season bump: a new-season run is accepted", service.SubmitRun(new LeaderboardRunStats { score = 40, starDust = 1f, worldsCleared = 1, startShip = 7, startHearts = 4 }) == 3);

        // A reinstall with only the cloud save restored: the local best key is gone, SavedBest comes back from the cloud.
        seed();
        PlayerPrefs.DeleteKey(LeaderboardService.NormalBestKey);
        PlayerPrefs.DeleteKey(LeaderboardService.BoostedBestKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.BoundPlayerKey);
        Check("reinstall (before the migration): the normal best would be re-seeded from SavedBest", LeaderboardService.LocalBest(false) == Old);
        LeaderboardBoards.OverrideSeasonForTests(2);
        LeaderboardService.MigrateSeason();
        LeaderboardService.SeedLocalBests();
        Check("reinstall after a bump: the normal best starts at 0 and SavedBest is untouched",
              LeaderboardService.LocalBest(false) == 0 && RunScore.SavedBest == Old);
        fake = new FakeLeaderboards();
        service = Service(fake, false, "gp-A");
        service.Flush();
        Check("reinstall after a bump: the backfill offers nothing", fake.Submissions.Count == 0);

        // A fresh install at the first season: no wipe, the normal best seeds from the saved best exactly as before.
        LeaderboardBoards.OverrideSeasonForTests(null);
        Context();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, Old);
        LeaderboardService.MigrateSeason();
        LeaderboardService.SeedLocalBests();
        Check("fresh install at the first season: unchanged behaviour (best seeds from SavedBest, no season key)",
              LeaderboardService.LocalBest(false) == Old && !PlayerPrefs.HasKey(LeaderboardService.SeasonKey));

        // A fresh install at a later season ends correct too: bests 0, season stored.
        Context();
        LeaderboardBoards.OverrideSeasonForTests(3);
        LeaderboardService.MigrateSeason();
        Check("fresh install at a later season: bests 0 and the season stored",
              LeaderboardService.LocalBest(false) == 0 && PlayerPrefs.GetInt(LeaderboardService.SeasonKey, 0) == 3);
        // A downgrade (stored season ahead of the constant) never wipes.
        PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, 77);
        LeaderboardBoards.OverrideSeasonForTests(2);
        LeaderboardService.MigrateSeason();
        Check("a stored season ahead of the constant is left alone", PlayerPrefs.GetInt(LeaderboardService.NormalBestKey, 0) == 77);
        LeaderboardBoards.OverrideSeasonForTests(null);
    }
}
