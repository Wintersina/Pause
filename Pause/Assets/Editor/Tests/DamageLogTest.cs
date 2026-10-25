using UnityEngine;

// What hurt and killed the pilot (DamageLog): the saved "killed you / hurt you
// N times" tallies, this run's HURT BY / KILLED BY lines, old saves, corrupt
// prefs, the cloud merge, and the codex line. Pure logic: no scene needed.
public static class DamageLogTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DLG] PASS  " : "[DLG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(DamageLog.KilledKey);
        PlayerPrefs.DeleteKey(DamageLog.HurtKey);
        DamageLog.Reload();
        DamageLog.BeginRun();

        // two real entries to count against
        string a = Codex.Entries[Codex.Entries.Length - 1].id, b = Codex.Entries[1].id;

        // ---- an old save has neither key: no counts, nothing to migrate ----
        Check("old save: no counts", DamageLog.KilledCount(a) == 0 && DamageLog.HurtCount(a) == 0 &&
              DamageLog.TotalKilled() == 0 && DamageLog.ShareOfDeaths(a) == -1);

        // ---- parse / format ----
        var parsed = DamageLog.Parse("x:3,junk,y:0,z:-2,w:abc,:5,q:2");
        Check("parse keeps only positive counts with an id", parsed.Count == 2 && parsed["x"] == 3 && parsed["q"] == 2);
        Check("format is sorted and round-trips", DamageLog.Format(DamageLog.Parse("q:2,x:3")) == "q:2,x:3" && DamageLog.Format(DamageLog.Parse("x:3,q:2")) == "q:2,x:3");
        Check("a corrupt pref reads as empty, never throws", DamageLog.Parse("::,,,:").Count == 0 && DamageLog.Parse(null).Count == 0);
        Check("merge takes the larger count per id", DamageLog.MergeMax("a:2,b:5", "a:4,c:1") == "a:4,b:5,c:1");
        Check("merge with an empty side keeps the other", DamageLog.MergeMax("a:2", "") == "a:2" && DamageLog.MergeMax("", "") == "");

        // ---- recording ----
        DamageLog.Record(a, false, true);
        DamageLog.Record(a, false, true);
        DamageLog.Record(b, false, true);
        DamageLog.Record(a, true, true);
        Check("hurt counts the non-fatal hits", DamageLog.HurtCount(a) == 2 && DamageLog.HurtCount(b) == 1);
        Check("killed counts the fatal hit only", DamageLog.KilledCount(a) == 1 && DamageLog.KilledCount(b) == 0);
        Check("the run remembers its killer", DamageLog.RunKiller == a);
        Check("saved as one pref per tally", PlayerPrefs.GetString(DamageLog.KilledKey) == a + ":1" && PlayerPrefs.GetString(DamageLog.HurtKey).Contains(a + ":2"));
        DamageLog.Reload();
        Check("tallies survive a reload", DamageLog.HurtCount(a) == 2 && DamageLog.KilledCount(a) == 1);
        // the codex VERSUS YOU table: hurt you / killed you / share of every death on record
        Check("codex table reads both", string.Join(",", CodexPanel.TallyCells(a)) == "2,1,100%");
        Check("codex table reads one", string.Join(",", CodexPanel.TallyCells(b)) == "1,0,0%");
        Check("every death is counted once in the total", DamageLog.TotalKilled() == 1);

        // ---- unknown sources and unsaved runs ----
        PlayerPrefs.SetString(DamageLog.HurtKey, "");
        DamageLog.Reload();
        DamageLog.Record(null, true, true);
        DamageLog.Record("", false, true);
        Check("an unknown source counts nothing", DamageLog.KilledCount(a) == 1 && DamageLog.RunKiller == a && PlayerPrefs.GetString(DamageLog.HurtKey) == "");
        DamageLog.BeginRun();
        Check("a new run clears the run tally", DamageLog.RunKiller == null && DamageLog.RunHurtKinds == 0 && DamageLog.RunHurtLine(34) == null);
        DamageLog.Record(b, false, false);
        DamageLog.Record(b, true, false);
        Check("unsaved (tutorial / developer) runs keep the run tally only", DamageLog.RunKiller == b && DamageLog.RunHurtKinds == 1
              && DamageLog.HurtCount(b) == 0 && DamageLog.KilledCount(b) == 0);

        // ---- the run line ----
        DamageLog.BeginRun();
        DamageLog.Record(a, false, false);
        DamageLog.Record(b, false, false);
        DamageLog.Record(b, false, false);
        string an = DamageLog.NameOf(a), bn = DamageLog.NameOf(b);
        Check("run line lists the most frequent first with counts", DamageLog.RunHurtLine(60) == bn + " x2, " + an);
        Check("run line folds the rest into +N more", DamageLog.RunHurtLine(1) == bn + " x2 +1 more");

        // recency breaks ties; an unresolved source is still a hit; the fatal blow is not listed
        DamageLog.BeginRun();
        DamageLog.Record(a, false, false);
        DamageLog.Record(b, false, false);
        Check("equal counts: the most recent hit first", DamageLog.RunHurtLine(60) == bn + ", " + an);
        DamageLog.Record(null, false, false);
        Check("an unresolved earlier hit shows as Unknown hazard, not NO OTHER HITS",
              DamageLog.RunHurtLine(80) == "Unknown hazard, " + bn + ", " + an && DamageLog.RunHurtKinds == 3);
        Check("an unresolved hit is never saved", DamageLog.HurtCount(null) == 0);
        DamageLog.Record(a, true, false);
        Check("the fatal blow is not in HURT BY", DamageLog.RunKiller == a && DamageLog.RunHurtKinds == 3);
        Check("a tight budget folds into +N more", DamageLog.RunHurtLine(5).EndsWith(" +2 more"));
        DamageLog.BeginRun();
        DamageLog.Record(null, true, false);
        Check("only a fatal blow: genuinely no other hits, killer unknown", DamageLog.RunHurtLine(34) == null && DamageLog.RunKiller == null);
        var lbl = new DeathPanelView.Results();
        Check("panel: NO OTHER HITS only when none", DeathPanelView.HurtByLabel(lbl) == "NO OTHER HITS");
        lbl.hurtBy = "Mine x2, Laser";
        Check("panel: names upper-cased", DeathPanelView.HurtByLabel(lbl) == "MINE X2, LASER");
        Check("names resolve through the codex (an unknown id has none)", an != null && DamageLog.NameOf("nope") == null);

        // ---- the cloud save ----
        PlayerPrefs.SetString(DamageLog.KilledKey, a + ":3");
        PlayerPrefs.SetString(DamageLog.HurtKey, a + ":4");
        var snap = ProgressSnapshot.Fresh(1);
        snap.codexKilled = a + ":1," + b + ":2";
        snap.codexHurt = "";
        var local = ProgressSnapshot.Capture(2);
        Check("capture carries the tallies", local.codexKilled == a + ":3" && local.codexHurt == a + ":4");
        var merged = ProgressMerge.Merge(local, snap);
        Check("merge keeps the larger tally per id", merged.codexKilled == DamageLog.MergeMax(a + ":3," + b + ":2", "") && merged.codexHurt == a + ":4");
        merged.Apply();
        DamageLog.Reload();
        Check("apply never lowers a tally", DamageLog.KilledCount(a) == 3 && DamageLog.KilledCount(b) == 2 && DamageLog.HurtCount(a) == 4);
        Check("the share of deaths splits every death on record", DamageLog.TotalKilled() == 5 &&
              DamageLog.ShareOfDeaths(a) == 60 && DamageLog.ShareOfDeaths(b) == 40);

        // ---- a cloud blob from before the feature ----
        ProgressSnapshot old;
        var result = ProgressSnapshot.TryParse("{\"schemaVersion\":" + ProgressSnapshot.CurrentSchemaVersion + ",\"currency\":5}", out old);
        Check("an old blob parses with empty tallies", result == ProgressSnapshot.ParseResult.Ok && old.codexKilled == "" && old.codexHurt == "");

        return fails;
    }
}
