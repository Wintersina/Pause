using System.Linq;
using UnityEngine;

// COLLECT ALL's star-dust burst must start on the find that was just paid and fly to the dust counter on every
// screen shape: the origin / target the panel hands the burst are checked against the card's and the counter's real
// positions on the canvas, and the burst's own first image (its glow flash) against the card too.
public static class CodexDustWideTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        UnityEngine.Debug.Log((ok ? "[CDW] PASS  " : "[CDW] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static string why;

    public static int Execute()
    {
        fails = 0;
        foreach (var id in new[] { "flip7-1080x2520", "desk-1920x1080", "desk-2560x1080", "tab-1024x768" })
        {
            var device = FitDevice.Find(id);
            why = null;
            using (new TestHarness.Sandbox())
            {
                var shot = ScreenFitRunner.Run(new FitScreen { id = "codex-dust", scene = "startS4", title = "dust", stage = Stage }, device, null, 0);
                if (shot.error != null) why = shot.error;
            }
            Check("collect all dust starts on the card and ends on the counter at " + id + (why == null ? "" : ": " + why), why == null);
        }
        UnityEngine.Debug.Log("[CDW] failures: " + fails);
        return fails;
    }

    static void Stage(ScreenFitRig rig)
    {
        ScreenFitScreens.HomeBase(rig);
        DeveloperUnlocks.SetEnabled(false);
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", Codex.Entries.Where(e => e.category != CodexCategory.Log).Take(12).Select(e => e.id)));
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        Codex.Reload();
        var panel = CodexPanel.Open(null);
        rig.Sync();
        panel.Refresh();
        panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
        var tab = CodexPanel.Tabs.First(t => Codex.PendingIn(t) > 0);
        panel.ShowCategory(tab);
        panel.SkipAnimations();
        panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
        Canvas.ForceUpdateCanvases();
        panel.CollectTabButton.onClick.Invoke();
        if (!panel.CollectSequenceActive) { why = "the sequence did not start"; return; }
        string id = panel.CollectSequenceCurrent;
        int spawned = panel.DustFx.Spawned;
        for (int i = 0; i < 20 && panel.DustFx.Spawned == spawned && panel.CollectSequenceActive; i++)
        {
            panel.TickCollect(.1f);
            panel.SkipAnimations();   // the glide has settled by the time the find is paid
        }
        if (panel.DustFx.Spawned == spawned) { why = "no burst spawned"; return; }

        var rt = panel.Panel;
        int card = -1;
        for (int i = 0; i < panel.VisibleCards; i++) if (panel.CardEntry(i).id == id) card = i;
        if (card < 0) { why = "the paid find has no visible card"; return; }
        Vector2 cardAt = rt.InverseTransformPoint(panel.CardRect(card).TransformPoint(panel.CardRect(card).rect.center));
        var counter = panel.Counter.rectTransform;
        // the number is right-aligned in a header rect that can span the whole window: the burst must land on the digits
        Vector2 inCounter = counter.InverseTransformPoint(rt.TransformPoint(panel.LastDustTo));
        float textW = Mathf.Ceil(panel.Counter.preferredWidth);
        var body = panel.CurrentLayout.body;
        Vector2 want = new Vector2(Mathf.Clamp(cardAt.x, body.xMin, body.xMax), Mathf.Clamp(cardAt.y, body.yMin, body.yMax));

        // the burst's own flash image, where it really is on the canvas, in panel space
        Vector2 glowAt = Vector2.zero; bool found = false;
        for (int s = 0; s < CodexDustFx.Slots; s++)
        {
            var layer = panel.DustFx.Slot(s).LayerRect;
            if (layer == null || !layer.gameObject.activeSelf) continue;
            var glow = layer.Find("Glow") as RectTransform;
            if (glow == null) continue;
            glowAt = rt.InverseTransformPoint(glow.TransformPoint(glow.rect.center)); found = true;
        }
        if (!found) { why = "no live burst layer"; return; }
        if (Vector2.Distance(panel.LastDustFrom, want) > 1f) why = "origin " + panel.LastDustFrom + " is not the card " + want;
        else if (inCounter.x < counter.rect.xMax - textW - 1f || inCounter.x > counter.rect.xMax + 1f || Mathf.Abs(inCounter.y - counter.rect.center.y) > counter.rect.height * .5f)
            why = "target " + panel.LastDustTo + " is not on the counter's number (at " + inCounter + " in the counter, text " + textW + " wide, rect " + counter.rect + ")";
        else if (Vector2.Distance(glowAt, want) > 1f) why = "the flash is drawn at " + glowAt + ", not on the card " + want;
    }
}
