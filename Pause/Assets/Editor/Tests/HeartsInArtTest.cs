using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "elite hearts orange, boss hearts 10, hearts spin round them in 3D
// like the player's, and they must not disappear into big ships' art".
//
// Every elite def and every boss: the hearts are neon orange (never the
// player's red), spin in the player's 3D style, orbit OUTSIDE the hull's real
// opaque silhouette (re-measured here from the PNG on disk, not from the
// runtime measure), and -- the bug -- at sampled phases of the orbit (the
// planes precess, so a long sweep) no heart is ever fully hidden: its centre is
// off every opaque pixel of the drawn hull, or it is drawn ABOVE the hull
// (dimmed) instead of under it.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HeartsInArtTest.Run
public static class HeartsInArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HEARTSART] PASS  " : "[HEARTSART] FAIL  ") + what);
        if (!ok) fails++;
    }
    static void Info(string what) { Debug.Log("[HEARTSART] INFO  " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly Color PlayerRed = new Color(254f / 255f, 62f / 255f, 78f / 255f);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            OrangeEverywhere();
            EliteHeartsClearTheArt();
            BossHeartsClearTheArt();
        }
        finally
        {
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            Time.timeScale = 1f;
        }
        Debug.Log("[HEARTSART] failures: " + fails);
        return fails;
    }

    static float HueGap(Color a, Color b)
    {
        float ha, hb, s, v;
        Color.RGBToHSV(a, out ha, out s, out v);
        Color.RGBToHSV(b, out hb, out s, out v);
        float d = Mathf.Abs(ha - hb) * 360f;
        return Mathf.Min(d, 360f - d);
    }

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        Time.timeScale = 1f;
    }

    // A readable copy of a PNG under Assets/Art/Resources (null: missing).
    static Texture2D Load(string resourcePath)
    {
        string path = "Assets/Art/Resources/" + resourcePath + ".png";
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2);
        return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
    }

    static void OrangeEverywhere()
    {
        Check("the elite orange is warm and saturated (" + ColorUtility.ToHtmlStringRGB(EliteHearts.Orange) + ")",
              EliteHearts.Orange.r > .95f && EliteHearts.Orange.g > .4f && EliteHearts.Orange.g < .7f && EliteHearts.Orange.b < .2f);
        Check("... 30+ degrees from the player's red (" + HueGap(EliteHearts.Orange, PlayerRed).ToString("F0") + ")",
              HueGap(EliteHearts.Orange, PlayerRed) > 30f);
        bool all = true;
        foreach (var d in EliteCatalog.All) all &= d.HeartColor == EliteHearts.Orange;
        Check("all " + EliteCatalog.All.Length + " elites resolve to the orange, whatever their json heartColor", all);
        bool bosses = true;
        foreach (var b in BossCatalog.All) bosses &= b.heartColor == EliteHearts.Orange;
        Check("... and so do all " + BossCatalog.All.Length + " bosses", bosses);
        Check("a boss has 10 hearts and 10 hit points", BossConfig.Hearts == 10 && BossConfig.HitPoints == 10);
    }

    // ---- elites ----------------------------------------------------------

    static void EliteHeartsClearTheArt()
    {
        FreshScene();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -3.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        int ghostedSamples = 0, samples = 0;
        foreach (var def in EliteCatalog.All)
        {
            var tex = Load(EliteArt.StripPath(def));
            if (tex == null) { Check(def.key + ": strip readable", false); continue; }
            int cells = Mathf.Max(1, tex.width / tex.height), cell = tex.width / cells, ch = tex.height;
            var px = tex.GetPixels32();
            // the real silhouette radius: the furthest opaque pixel of any drawing
            float pw = def.cellWorldSize / ch, far = 0f;
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < tex.width; x++)
                {
                    if (px[y * tex.width + x].a < 128) continue;
                    float dx = (x % cell) + .5f - cell * .5f, dy = y + .5f - ch * .5f;
                    far = Mathf.Max(far, Mathf.Sqrt(dx * dx + dy * dy));
                }
            float silhouette = far * pw;

            var ship = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
            var eh = ship.GetComponent<EliteHearts>();
            var hullTf = ship.Hull.transform;
            float scale = hullTf.lossyScale.x;
            Check(def.key + ": runtime silhouette " + EliteSilhouette.Radius(def).ToString("F3") + " = the PNG's " + silhouette.ToString("F3"),
                  Mathf.Abs(EliteSilhouette.Radius(def) - silhouette) < .01f);
            float visible = BossHearts.VisibleHalf(eh.heartSize);
            Check(def.key + ": orbit " + eh.OrbitRadii.x.ToString("F2") + " passes outside the art (" + (silhouette * scale).ToString("F2") + " + heart " + visible.ToString("F2") + ")",
                  eh.OrbitRadii.x >= silhouette * scale + visible - 1e-3f);
            bool ok = eh.Style == HeartStyle.Atom && eh.Hearts.Length == def.hearts;
            var r0 = eh.Hearts[0].GetComponent<SpriteRenderer>();
            ok &= HueGap(r0.color, PlayerRed) > 30f && r0.sprite.name.Contains("eliteHeart");
            Check(def.key + ": " + def.hearts + " orange hearts in the player's Atom 3D spin", ok);

            int frame = 0;
            string nm = ship.Hull.sprite != null ? ship.Hull.sprite.name : "";
            int us = nm.LastIndexOf('_');
            if (us >= 0) int.TryParse(nm.Substring(us + 1), out frame);
            frame = Mathf.Clamp(frame, 0, cells - 1);
            bool visibleAlways = true, sawBehind = false;
            int bad = 0;
            for (int step = 0; step < 900; step++)   // 15 s: the planes precess
            {
                eh.Place(Dt, Dt);
                if (step % 9 != 0) continue;
                for (int i = 0; i < eh.Hearts.Length; i++)
                {
                    if (!eh.Hearts[i].gameObject.activeSelf) continue;
                    Vector3 lp = hullTf.InverseTransformPoint(eh.Hearts[i].position);
                    int x = Mathf.RoundToInt(cell * .5f + lp.x / def.cellWorldSize * ch) + frame * cell;
                    int y = Mathf.RoundToInt(ch * .5f + lp.y / def.cellWorldSize * ch);
                    bool over = x >= frame * cell && x < (frame + 1) * cell && y >= 0 && y < ch && px[y * tex.width + x].a >= 128;
                    samples++;
                    if (eh.Depth(i) < 0f) sawBehind = true;
                    if (eh.Ghosted(i)) ghostedSamples++;
                    if (over && !eh.InFront(i)) { visibleAlways = false; bad++; }
                }
            }
            Check(def.key + ": no heart ever hidden under the art over 15 s (" + bad + " bad samples)", visibleAlways && sawBehind);
            Object.DestroyImmediate(tex);
            EliteSystem.Clear();
            ship = null;
            FreshPilot(ref pilot);
        }
        Info("elite samples " + samples + ", of them " + ghostedSamples + " were back hearts over the art, drawn above");
        EliteSystem.PlayerOverride = null;
    }

    static void FreshPilot(ref Transform pilot)
    {
        if (pilot != null) return;
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -3.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
    }

    // ---- bosses ----------------------------------------------------------

    static void BossHeartsClearTheArt()
    {
        foreach (var boss in BossCatalog.All)
        {
            FreshScene();
            var tex = Load("Bosses/" + boss.artKey);
            if (tex == null) { Check(boss.artKey + ": atlas readable", false); continue; }
            int cell = tex.width / BossArt.BodyColumns, rows = tex.height / cell;
            var px = tex.GetPixels32();
            var actor = BossActor.Spawn(boss);
            actor.BeginFight();
            var h = actor.Hearts;
            Check(boss.artKey + ": 10 orange hearts in a tilted (3D) ring",
                  h.Hearts.Length == 10 && h.OrbitParams.tilt >= 30f && h.OrbitParams.precession > 0f && HueGap(h.Hearts[0].GetComponent<SpriteRenderer>().color, PlayerRed) > 30f);
            // the first idle drawing of the body, exactly as the boss shows it at rest
            int f = BossArt.HasExpandedCombat(boss) ? BossArt.SpaceIdle0 : BossArt.Idle0;
            int col = f % BossArt.BodyColumns, row = f / BossArt.BodyColumns, x0 = col * cell, y0 = (rows - 1 - row) * cell;
            float s = BossConfig.BossWorldSize / cell;
            Vector3 centre = actor.transform.position;
            bool never = true, sawBehind = false;
            int bad = 0, over = 0;
            for (int step = 0; step < 1200; step++)   // 20 s: four revolutions, the plane well round
            {
                h.Step(Dt, Dt);
                if (step % 7 != 0) continue;
                for (int i = 0; i < h.Hearts.Length; i++)
                {
                    Vector3 p = h.Hearts[i].position;
                    int x = Mathf.RoundToInt((p.x - centre.x) / s + cell * .5f - .5f), y = Mathf.RoundToInt((p.y - centre.y) / s + cell * .5f - .5f);
                    bool onArt = x >= 0 && x < cell && y >= 0 && y < cell && px[(y0 + y) * tex.width + x0 + x].a >= 128;
                    if (h.Depth(i) < 0f) sawBehind = true;
                    if (onArt) { over++; if (!h.InFront(i)) { never = false; bad++; } }
                }
            }
            Check(boss.artKey + ": no heart ever hidden under the body (" + over + " samples over the art, all drawn above; " + bad + " bad)", never && sawBehind);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(actor.gameObject);
        }
    }
}
