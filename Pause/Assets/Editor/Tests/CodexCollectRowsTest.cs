using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

// COLLECT ALL in the Codex: every burst starts on the row that is being paid (entry card, achievement card), the
// ACHIEVEMENTS list scrolls so that row is whole in view, ENEMIES (roster enemies, elites, bosses) wait for COLLECT and
// pay the table value once, and the total, the saved paid list and the ledger agree at every screen shape.
public static class CodexCollectRowsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CCR] PASS  " : "[CCR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly (string name, Rect safe)[] Screens =
    {
        ("minimum 800x1280", new Rect(-400f, -640f, 800f, 1280f)),
        ("9:24 phone 1080x2880", new Rect(-400f, -1067f, 800f, 2133f)),
        ("Z Flip cover (wide and short)", new Rect(-600f, -400f, 1200f, 800f)),
        ("iPad 1536x2048", new Rect(-480f, -640f, 960f, 1280f)),
        ("landscape Mac 1600x900", new Rect(-1138f, -640f, 2276f, 1280f)),
    };

    static float Dust() { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var esGo = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        try
        {
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            foreach (var s in Screens)
            {
                panel.ApplyLayout(s.safe);
                EnemiesPayOnCollect(panel, s.name);
                EnemiesCollectAll(panel, s.name);
                AchievementsCollectAll(panel, s.name);
            }
            SingleCollectStillWorks(panel);
        }
        finally
        {
            if (panel != null) { panel.Close(); panel.SkipAnimations(); Object.DestroyImmediate(panel.gameObject); }
            Object.DestroyImmediate(esGo);
            AchievementStore.ResetAll();
            AchievementStores.Current = null;
        }
        Debug.Log("[CCR] failures: " + fails);
        return fails;
    }

    static void ResetCodex(string seen, float dust)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString(Codex.PrefsKey, seen);
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        PlayerPrefs.SetInt(Codex.BackPaidKey, 1);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, dust);
        Codex.Reload();
    }

    static List<CodexEntry> SomeEnemies()
    {
        var list = Codex.Entries.Where(e => e.category == CodexCategory.Enemies && !e.secret && BossCatalog.Find(e.id) == null && EliteCatalog.FindByCodexId(e.id) == null).Take(7).ToList();
        list.Add(Codex.Entries.First(e => BossCatalog.Find(e.id) != null));
        var elite = Codex.Entries.FirstOrDefault(e => e.category == CodexCategory.Enemies && EliteCatalog.FindByCodexId(e.id) != null);
        if (elite != null) list.Add(elite);
        return list;
    }

    // ---- ENEMIES: discovering pays nothing yet, the entries wait for COLLECT ----

    static void EnemiesPayOnCollect(CodexPanel panel, string screen)
    {
        var enemies = SomeEnemies();
        ResetCodex("", 100f);
        panel.Refresh();
        foreach (var e in enemies) Codex.Discover(e.id);
        int sum = enemies.Sum(e => CodexPayout.For(e));
        Check(screen + ": discovering enemies, elites and bosses pays nothing yet", Dust() == 100f);
        Check(screen + ": all of them are pending with the table dust waiting",
              Codex.PendingIn(CodexCategory.Enemies) == enemies.Count && enemies.All(e => Codex.IsRewardPending(e) && Codex.PendingDust(e) == CodexPayout.For(e)) && Codex.PendingDustIn(CodexCategory.Enemies) == sum);
        Check(screen + ": the tab and the home bubble count them", Codex.PendingTotal >= enemies.Count);
        Check(screen + ": COLLECT ALL of the tab pays the sum once, nothing the second time",
              Codex.CollectRewards(CodexCategory.Enemies) == sum && Dust() == 100f + sum && Codex.CollectRewards(CodexCategory.Enemies) == 0 && Dust() == 100f + sum);
        Codex.Reload();
        Check(screen + ": after a reload they stay paid and cleared", Codex.PendingIn(CodexCategory.Enemies) == 0 && enemies.All(e => Codex.IsRewardCollected(e.id)));
        string paid = PlayerPrefs.GetString(Codex.PaidKey);
        Check(screen + ": the paid list is saved", enemies.All(e => paid.Split(',').Contains(e.id)));
    }

    // ---- the panel's COLLECT ALL: a burst from each row, in order ----

    static void EnemiesCollectAll(CodexPanel panel, string screen)
    {
        var enemies = SomeEnemies();
        ResetCodex(string.Join(",", enemies.Select(e => e.id)), 100f);
        panel.Refresh();
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        Canvas.ForceUpdateCanvases();
        var expectOrder = Codex.PendingIdsIn(CodexCategory.Enemies);
        int sum = expectOrder.Sum(id => CodexPayout.For(Codex.Find(id)));
        panel.CollectTabButton.onClick.Invoke();
        Check(screen + ": the collect sequence starts", panel.CollectSequenceActive);
        var order = new List<string>();
        bool atRow = true; string why = null;
        for (int i = 0; i < 400 && panel.CollectSequenceActive; i++)
        {
            string current = panel.CollectSequenceCurrent;
            int spawned = panel.DustFx.Spawned;
            panel.TickCollect(.05f);
            if (panel.DustFx.Spawned == spawned) continue;
            order.Add(current);
            int card = -1;
            for (int c = 0; c < panel.VisibleCards; c++) if (panel.CardEntry(c).id == current) card = c;
            if (card < 0) { atRow = false; why = current + " has no card"; continue; }
            Vector2 want = RowCentreInDust(panel, panel.CardRect(card));
            var body = panel.CurrentLayout.body;
            want = new Vector2(Mathf.Clamp(want.x, body.xMin, body.xMax), Mathf.Clamp(want.y, body.yMin, body.yMax));
            if (Vector2.Distance(want, panel.LastDustFrom) > 1f) { atRow = false; why = current + ": burst at " + panel.LastDustFrom + ", row at " + want; }
        }
        Check(screen + ": every enemy burst starts on its own card" + (why == null ? "" : " (" + why + ")"), atRow && order.Count > 0);
        Check(screen + ": the bursts came in the list's order", order.SequenceEqual(expectOrder.Where(order.Contains)));
        Check(screen + ": total paid is the table sum, saved, nothing pending",
              Dust() == 100f + sum && Codex.PendingIn(CodexCategory.Enemies) == 0 && expectOrder.All(Codex.IsRewardCollected));
        Check(screen + ": the sequence ended", !panel.CollectSequenceActive);
    }

    static Vector2 RowCentreInDust(CodexPanel panel, RectTransform row)
    {
        return panel.DustLayer.InverseTransformPoint(row.TransformPoint(row.rect.center));
    }

    // ---- ACHIEVEMENTS: the list follows the payout ----

    static void AchievementsCollectAll(CodexPanel panel, string screen)
    {
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        panel.ShowTab(CodexPanel.AchievementsTab);
        panel.SkipAnimations();
        var v = panel.Achievements;
        // one claimable badge every few cards, so the list has to travel
        var chosen = new List<AchievementDef>();
        for (int i = 0; i < v.VisibleCards; i += 5)
        {
            var d = v.CardDef(i);
            if (d.hidden) continue;
            AchievementStore.Unlock(d);
            if (AchievementStore.IsClaimable(d)) chosen.Add(d);
        }
        panel.ShowTab(CodexPanel.AchievementsTab);
        v.PaintAll();
        Canvas.ForceUpdateCanvases();
        int sum = chosen.Sum(d => d.Reward);
        Check(screen + ": several badges are waiting (" + chosen.Count + ", " + sum + " dust)", chosen.Count >= 4 && AchievementStore.ClaimableCount == chosen.Count);
        var expectOrder = v.ClaimableIds();
        v.SetScrollY(0f);
        v.CollectAll.button.onClick.Invoke();
        Check(screen + ": COLLECT ALL starts a sequence instead of paying at once", panel.CollectSequenceActive && Dust() == 100f);

        var seenY = new HashSet<int> { Mathf.RoundToInt(v.ScrollY) };
        var order = new List<string>();
        bool atRow = true, visible = true; string why = null;
        int total = 0;
        for (int i = 0; i < 600 && panel.CollectSequenceActive; i++)
        {
            string current = panel.CollectSequenceCurrent;
            int spawned = panel.DustFx.Spawned;
            float before = Dust();
            panel.TickCollect(.05f);
            seenY.Add(Mathf.RoundToInt(v.ScrollY));
            if (panel.DustFx.Spawned == spawned) continue;
            order.Add(current);
            int card = v.CardIndexOf(current);
            total += Mathf.RoundToInt(Dust() - before);
            if (!v.CardWhollyVisible(card)) { visible = false; why = current + " is not in view at scroll " + v.ScrollY; }
            Vector2 want = RowCentreInDust(panel, v.CardRect(card));
            var body = panel.CurrentLayout.body;
            want = new Vector2(Mathf.Clamp(want.x, body.xMin, body.xMax), Mathf.Clamp(want.y, body.yMin, body.yMax));
            if (Vector2.Distance(want, panel.LastDustFrom) > 1f) { atRow = false; why = current + ": burst at " + panel.LastDustFrom + ", card at " + want; }
        }
        Check(screen + ": the row being collected is whole in view when it pays" + (why == null ? "" : " (" + why + ")"), visible && order.Count == expectOrder.Count);
        Check(screen + ": every burst starts on its own badge card" + (why == null ? "" : " (" + why + ")"), atRow);
        Check(screen + ": paid in list order", order.SequenceEqual(expectOrder));
        Check(screen + ": the list scrolled to reach them (" + seenY.Count + " positions)", v.MaxScroll < 1f || seenY.Count >= 2);
        Check(screen + ": total paid once, saved, nothing left", total == sum && Dust() == 100f + sum && AchievementStore.ClaimableCount == 0 && !panel.CollectSequenceActive);
        Check(screen + ": the cards show collected and COLLECT ALL is off", !v.CollectAll.button.interactable && chosen.All(d => !AchievementStore.IsClaimable(d)));
        v.CollectAll.button.onClick.Invoke();
        Check(screen + ": a second COLLECT ALL pays nothing", Dust() == 100f + sum);

        // skip: a second tap credits everything left at once, no loss, no double pay
        AchievementStore.ResetAll();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        foreach (var d in chosen) AchievementStore.Unlock(d);
        v.PaintAll();
        v.SetScrollY(0f);
        v.CollectAll.button.onClick.Invoke();
        panel.TickCollect(.05f);
        v.CollectAll.button.onClick.Invoke();
        Check(screen + ": a second tap skips and pays the rest once", !panel.CollectSequenceActive && Dust() == 100f + sum && AchievementStore.ClaimableCount == 0);
        panel.ShowTab(CodexPanel.AchievementsTab + 1);
    }

    // ---- one tap on one row still works ----

    static void SingleCollectStillWorks(CodexPanel panel)
    {
        var e = Codex.Entries.First(x => x.category == CodexCategory.Enemies && !x.secret && BossCatalog.Find(x.id) == null && EliteCatalog.FindByCodexId(x.id) == null);
        ResetCodex(e.id, 100f);
        Check("single collect: pays that enemy only, once", Codex.CollectOne(e.id) == CodexPayout.For(e) && Dust() == 100f + CodexPayout.For(e) && Codex.CollectOne(e.id) == 0);
    }
}
