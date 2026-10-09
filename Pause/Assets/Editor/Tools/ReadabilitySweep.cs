using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// READABILITY SWEEP: does everything hostile read over every backdrop?
//
// For each world and each of its backdrop variants (Frost / Verdant: every
// installed v1..v4; Space / Ember: the one set), at 2 s (the cloud
// ceiling), 10 s and 40 s of the
// backdrop's own clock (as drawn: its grade, set pieces, clouds), it renders
// at gameplay scale (a 21:9 phone view, CameraFit):
//   * every roster enemy of that world, as built (EnemyFactory: its strip's
//     idle cell), and
//   * every hostile shot that world fires: its roster shooters' shots, its
//     elites' shots, every elite shot kind (gunship: bolt .. orb), its
//     boss's bolt and shard -- each in its own outline / rim,
// in two layouts (each item in two different screen places), and measures
// each item against the backdrop right behind it, outline-aware: its
// footprint is every pixel it covers at least half (art AND outline:
// rendered alone over black and over white), its LOCAL BACKDROP the
// backdrop's pixels under that footprint's edge and 3 px round it. Then
//   stand-out   footprint pixels whose luminance is 3:1 (WCAG) clear of
//               the local backdrop's mean -- HostileProjectileTest's rule;
//   edge ring   the contrast of the footprint's outermost 2 px (for an
//               outlined shot: its keyline) against the local backdrop,
//               and the share of those edge pixels 3:1 clear of the
//               backdrop under each.
// FLAGS: LOW = fewer than the test's 16 px (scaled to this view) stand out;
// WEAK = under 10% of the footprint stands out AND its edge ring is under
// 3:1 (it reads only by a few bright details). Pixel-art enemies carry a
// dark outline of their own, so over dark ground their edge ring is low by
// design: the stand-out share is the number to watch for them.
//
// Writes readability.csv (one row per item per placement), worst.csv (each
// item's worst case), every frame (flagged items boxed red) and a contact
// sheet per world (rows: variants, columns: 2 s / 10 s / 40 s x enemies / shots).
//
//   scripts/unity-batch.sh -executeMethod ReadabilitySweep.Run
//   (writes to $READABILITY_DIR, else Builds/Readability; READABILITY_STANDARD=1
//   forces the standard shot outline / rim everywhere: the look before the
//   bright worlds' bold one, for a before / after)
public static class ReadabilitySweep
{
    const int Width = 540, Height = 1260;     // a 21:9 phone at half resolution
    const float Dt = 1f / 60f;
    public const float MinContrast = 3f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };
    static readonly float[] Moments = { 2f, 10f, 40f };   // 2 s: the opening cloud ceiling at its thickest

    sealed class Item
    {
        public string kind, name;     // "enemy" / "shot"
        public EnemyDef enemy;
        public EliteDef def; public EliteShots.Kind shotKind; public bool roster;
        public BossDef boss; public BossShotStyle bossStyle;
    }

    struct Row
    {
        public string world, item, kind; public int variant, pass; public float at, ring, share; public int standOut, area; public Vector2 where;
        public float StandShare => area > 0 ? standOut / (float)area : 0f;
    }

    // The flags: LOW -- fewer than MinStandOut px of it stand 3:1 clear of
    // its local backdrop (HostileProjectileTest's rule: 16 px at its 96 px a
    // world unit, scaled to this view); WEAK -- under WeakShare of its
    // footprint does, and its edge ring is under 3:1 too (it reads only by a
    // few bright details).
    public const int TestPixelsPerUnit = 96, TestMinStandOut = 16;
    public const float WeakShare = .1f;
    static float MinStandOut;
    static string Flag(Row r)
    {
        if (r.standOut < MinStandOut) return "LOW";
        if (r.StandShare < WeakShare && r.ring < MinContrast) return "WEAK";
        return "";
    }

    static Camera cam;
    static RenderTexture rt;
    static Texture2D grab;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("READABILITY_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/Readability";
        Directory.CreateDirectory(dir);
        var rows = new List<Row>();
        if (System.Environment.GetEnvironmentVariable("READABILITY_STANDARD") == "1") { ShotOutline.Bold = false; BossArt.ShotRimBold = false; }
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < Worlds.Length; w++) SweepWorld(dir, w, rows);
            }
            finally
            {
                BossEncounter.ResetRun();
                EliteSystem.Clear();
                EliteSystem.PlayerOverride = null;
                ShotOutline.Bold = null;
                BossArt.ShotRimBold = null;
                foreach (var wn in Worlds) BackdropVariants.For(wn).Reset();
                if (rt != null) Object.DestroyImmediate(rt);
            }
        }
        Write(dir, rows);
        EditorApplication.Exit(0);
    }

    static void Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -40f, 0f);   // far below: nothing aims or reacts
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure().ClearAll();
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (grab == null) grab = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static List<Item> Items(int w)
    {
        var list = new List<Item>();
        foreach (var def in EnemyRoster.All)
            if (def.world == w) list.Add(new Item { kind = "enemy", name = def.key, enemy = def });
        foreach (var def in EnemyRoster.All)
        {
            if (def.world != w) continue;
            var b = EnemyBehaviours.For(def.key);
            if (b != null && (b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Ring || b.attack == EnemyAttack.Cross || b.attack == EnemyAttack.Lob))
                list.Add(new Item { kind = "shot", name = def.key + "/" + b.shotKind, def = b.ShotStyle, shotKind = b.shotKind, roster = true });
        }
        string wk = EnemyRoster.WorldKeys[w];
        EliteDef gun = null;
        foreach (var d in EliteCatalog.All)
        {
            if (d.brain == "gunship" && gun == null) gun = d;
            if (d.world == wk) list.Add(new Item { kind = "shot", name = "elite_" + d.key + "/" + d.shotKind, def = d, shotKind = EliteShots.KindOf(d.shotKind) });
        }
        foreach (EliteShots.Kind k in System.Enum.GetValues(typeof(EliteShots.Kind)))
            list.Add(new Item { kind = "shot", name = "gunship/" + k, def = gun, shotKind = k });
        var boss = BossCatalog.ForWorld(w);
        list.Add(new Item { kind = "shot", name = boss.artKey + "/bolt", boss = boss, bossStyle = BossShotStyle.Bolt });
        list.Add(new Item { kind = "shot", name = boss.artKey + "/shard", boss = boss, bossStyle = BossShotStyle.Shard });
        return list;
    }

    static void SweepWorld(string dir, int w, List<Row> rows)
    {
        string world = Worlds[w];
        var spec = BackdropCatalog.For(world);
        var picker = BackdropVariants.For(world);
        var items = Items(w);
        var enemies = items.FindAll(i => i.kind == "enemy");
        var shots = items.FindAll(i => i.kind == "shot");
        var thumbs = new List<Texture2D>();
        int variantsDone = 0;
        for (int v = spec.variantSets > 0 ? 1 : 0; v <= spec.variantSets; v++)
        {
            if (v > 0 && !picker.Installed(v)) continue;
            variantsDone++;
            Scene(w);
            picker.Force = v;
            var bgGo = new GameObject("~Backdrop");
            var wb = bgGo.AddComponent<WorldBackdrop>();
            wb.Show(world, false);
            float clock = 0f;
            foreach (float at in Moments)
            {
                while (clock < at) { wb.Step(Dt); clock += Dt; }
                for (int pass = 0; pass < 2; pass++)
                {
                    var t1 = Layout(dir, world, v, at, pass, enemies, 3, 5, rows);
                    var t2 = Layout(dir, world, v, at, pass, shots, 4, 8, rows);
                    if (pass == 0) { thumbs.Add(t1); thumbs.Add(t2); }
                    else { Object.DestroyImmediate(t1); Object.DestroyImmediate(t2); }
                }
            }
            Object.DestroyImmediate(bgGo);
            picker.Force = 0;
        }
        Sheet(Path.Combine(dir, "sheet-" + world.ToLower() + ".png"), thumbs, Moments.Length * 2);
        foreach (var t in thumbs) Object.DestroyImmediate(t);
        Debug.Log("[SWEEP] " + world + ": " + variantsDone + " variant(s), " + enemies.Count + " enemies, " + shots.Count + " shot kinds");
    }

    // One layout: `items` spread over a cols x rowsN grid (rotated by `pass`
    // so each item lands somewhere else), the backdrop grabbed without and
    // with them, each item measured in its own cell. Returns a thumbnail.
    static Texture2D Layout(string dir, string world, int variant, float at, int pass, List<Item> items, int cols, int rowsN,
                            List<Row> rows)
    {
        float hh = cam.orthographicSize, hw = hh * cam.aspect;
        float cw = 2f * hw / cols, ch = 2f * hh / rowsN;
        var made = new List<(Item item, GameObject go, Component shot, Vector3 pos)>();
        var pool = new BossProjectilePool(16, 1);
        int cells = cols * rowsN;
        for (int n = 0; n < items.Count && n < cells; n++)
        {
            // spread over the whole grid (stride), the second pass shifted half a screen
            int stride = Mathf.Max(1, cells / Mathf.Max(1, items.Count));
            int cell = (n * stride + pass * (cells / 2 + 1)) % cells;
            int cx = cell % cols, cy = cell / cols;
            var pos = new Vector3(-hw + cw * (cx + .5f), -hh + ch * (cy + .5f), 0f);
            var it = items[n];
            if (it.enemy != null)
            {
                var go = EnemyFactory.Create(it.enemy, pos, Quaternion.identity);
                if (go != null)
                {
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;   // the idle cell, still
                    made.Add((it, go, null, pos));
                }
            }
            else if (it.boss != null)
            {
                var b = pool.Fire(it.boss, it.bossStyle, pos, Vector2.zero);
                if (b != null) made.Add((it, null, b, pos));
            }
            else
            {
                var s = EliteSystem.Shots.Fire(null, it.def, it.shotKind, pos, Vector2.zero);
                if (s == null) continue;
                if (it.roster) s.AsRosterShot(null, 0f);
                made.Add((it, null, s, pos));
            }
        }
        // the backdrop alone, then with the items; then the items alone
        // (their own layer) over black and over white: their footprint
        Show(made, pool, false);
        var bg = Grab();
        Show(made, pool, true);
        var fg = Grab();
        foreach (var m in made) SetLayer(m.go != null ? m.go.transform : m.shot.transform, ItemLayer);
        int mask = cam.cullingMask;
        cam.cullingMask = 1 << ItemLayer;
        cam.backgroundColor = Color.black;
        var onBlack = Grab();
        cam.backgroundColor = Color.white;
        var onWhite = Grab();
        cam.backgroundColor = Color.black;
        cam.cullingMask = mask;

        float ppu = Height / (2f * hh);
        MinStandOut = TestMinStandOut * (ppu / TestPixelsPerUnit) * (ppu / TestPixelsPerUnit);
        var flagged = new List<RectInt>();
        foreach (var (item, go, shot, pos) in made)
        {
            Vector3 c = cam.WorldToScreenPoint(pos);
            var box = new RectInt(Mathf.Max(0, (int)(c.x - cw * ppu * .5f) + 1), Mathf.Max(0, (int)(c.y - ch * ppu * .5f) + 1),
                                  (int)(cw * ppu) - 2, (int)(ch * ppu) - 2);
            box.width = Mathf.Min(box.width, Width - box.x);
            box.height = Mathf.Min(box.height, Height - box.y);
            float ring, share;
            int fx0, fy0, fx1, fy1, stand, area;
            Measure(fg, bg, onBlack, onWhite, box, out ring, out share, out stand, out area, out fx0, out fy0, out fx1, out fy1);
            var row = new Row { world = world, variant = variant, at = at, pass = pass, kind = item.kind, item = item.name,
                                ring = ring, share = share, standOut = stand, area = area, where = new Vector2(pos.x, pos.y) };
            rows.Add(row);
            if (Flag(row) != "" && fx1 >= fx0) flagged.Add(new RectInt(fx0 - 3, fy0 - 3, fx1 - fx0 + 7, fy1 - fy0 + 7));
        }
        // the frame, flagged items boxed red
        foreach (var r in flagged) Box(fg, r, Color.red);
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.SetPixels(fg);
        tex.Apply();
        string kind = items.Count > 0 ? items[0].kind : "none";
        File.WriteAllBytes(Path.Combine(dir, string.Format("{0}-v{1}-{2:00}s-{3}-p{4}.png", world.ToLower(), variant, at, kind, pass)), tex.EncodeToPNG());

        foreach (var m in made)
        {
            if (m.go != null) Object.DestroyImmediate(m.go);
            else if (m.shot is EliteShot es) es.Recycle();
            else if (m.shot is BossProjectile bp) bp.Recycle();
        }
        pool.Dispose();
        HazardRuntime.Ensure().ClearAll();
        return tex;
    }

    const int ItemLayer = 31;

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }

    static void Show(List<(Item item, GameObject go, Component shot, Vector3 pos)> made, BossProjectilePool pool, bool on)
    {
        pool.Root.SetActive(on);
        foreach (var m in made)
        {
            if (m.go != null) m.go.SetActive(on);
            else if (m.shot is EliteShot) m.shot.gameObject.SetActive(on);
        }
    }

    static Color[] Grab()
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        grab.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        grab.Apply();
        RenderTexture.active = old;
        return grab.GetPixels();
    }

    // The edge-ring measure inside `box` (see the header).
    static void Measure(Color[] fg, Color[] bg, Color[] onBlack, Color[] onWhite, RectInt box, out float ring, out float share,
                        out int stand, out int area, out int fx0, out int fy0, out int fx1, out int fy1)
    {
        int bw = box.width, bh = box.height;
        var mask = new bool[bw * bh];
        fx0 = fy0 = int.MaxValue; fx1 = fy1 = int.MinValue;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                int i = (box.y + y) * Width + box.x + x;
                Color a = onBlack[i], b = onWhite[i];
                // the item's own coverage: 1 - (over white - over black); half or more counts
                float alpha = 1f - ((b.r - a.r) + (b.g - a.g) + (b.b - a.b)) / 3f;
                if (alpha >= .5f)
                {
                    mask[y * bw + x] = true;
                    fx0 = Mathf.Min(fx0, box.x + x); fx1 = Mathf.Max(fx1, box.x + x);
                    fy0 = Mathf.Min(fy0, box.y + y); fy1 = Mathf.Max(fy1, box.y + y);
                }
            }
        // Chebyshev distance to the other side, capped at 4 (both ways)
        var din = Dist(mask, bw, bh, true);
        var dout = Dist(mask, bw, bh, false);
        double le = 0, lb = 0; int ne = 0, nb = 0, clear = 0;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                int k = y * bw + x, i = (box.y + y) * Width + box.x + x;
                bool edge = mask[k] && din[k] <= 2;
                bool around = !mask[k] && dout[k] <= 3;
                if (edge)
                {
                    float lf = Lum(fg[i]), lbk = Lum(bg[i]);
                    le += lf; ne++;
                    lb += lbk; nb++;
                    if (Ratio(lf, lbk) >= MinContrast) clear++;
                }
                if (around) { lb += Lum(bg[i]); nb++; }
            }
        stand = area = 0;
        if (ne == 0) { ring = 0f; share = 0f; return; }
        float local = (float)(lb / Mathf.Max(1, nb));
        ring = Ratio((float)(le / ne), local);
        share = clear / (float)ne;
        // the stand-out count: footprint pixels 3:1 clear of the local backdrop's mean
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                if (!mask[y * bw + x]) continue;
                area++;
                if (Ratio(Lum(fg[(box.y + y) * Width + box.x + x]), local) >= MinContrast) stand++;
            }
    }

    static int[] Dist(bool[] mask, int w, int h, bool inside)
    {
        var d = new int[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = mask[i] == inside ? 99 : 0;
        for (int pass = 0; pass < 4; pass++)
        {
            var nd = (int[])d.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = y * w + x;
                    if (d[k] == 0) continue;
                    int best = d[k];
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            best = Mathf.Min(best, d[yy * w + xx] + 1);
                        }
                    nd[k] = best;
                }
            d = nd;
        }
        return d;
    }

    static void Box(Color[] px, RectInt r, Color c)
    {
        for (int t = 0; t < 2; t++)
        {
            for (int x = r.xMin; x <= r.xMax; x++) { Put(px, x, r.yMin + t, c); Put(px, x, r.yMax - t, c); }
            for (int y = r.yMin; y <= r.yMax; y++) { Put(px, r.xMin + t, y, c); Put(px, r.xMax - t, y, c); }
        }
    }

    static void Put(Color[] px, int x, int y, Color c)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height) px[y * Width + x] = c;
    }

    // Thumbnails (Width/3 x Height/3), `perRow` across.
    static void Sheet(string path, List<Texture2D> thumbs, int perRow)
    {
        if (thumbs.Count == 0) return;
        int tw = Width / 3, th = Height / 3, gap = 4;
        int rowsN = (thumbs.Count + perRow - 1) / perRow;
        int W = perRow * (tw + gap) + gap, H = rowsN * (th + gap) + gap;
        var sheet = new Texture2D(W, H, TextureFormat.RGB24, false);
        var fill = new Color[W * H];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color(.15f, .15f, .15f);
        for (int n = 0; n < thumbs.Count; n++)
        {
            var src = thumbs[n].GetPixels();
            int col = n % perRow, row = rowsN - 1 - n / perRow;
            int ox = gap + col * (tw + gap), oy = gap + row * (th + gap);
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    // max-pool the red flags so the boxes survive the shrink, else average
                    Color sum = Color.black; bool red = false;
                    for (int dy = 0; dy < 3; dy++)
                        for (int dx = 0; dx < 3; dx++)
                        {
                            var p = src[(y * 3 + dy) * Width + x * 3 + dx];
                            if (p.r > .99f && p.g < .01f && p.b < .01f) red = true;
                            sum += p;
                        }
                    fill[(oy + y) * W + ox + x] = red ? Color.red : sum / 9f;
                }
        }
        sheet.SetPixels(fill);
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }

    static void Write(string dir, List<Row> rows)
    {
        var sb = new StringBuilder("world,variant,seconds,kind,item,pass,x,y,ring_contrast,edge_share_3to1,standout_px,footprint_px,standout_share,flag\n");
        foreach (var r in rows)
            sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6:F2},{7:F2},{8:F2},{9:F2},{10},{11},{12:F3},{13}\n", r.world, r.variant, r.at, r.kind, r.item, r.pass,
                            r.where.x, r.where.y, r.ring, r.share, r.standOut, r.area, r.StandShare, Flag(r));
        File.WriteAllText(Path.Combine(dir, "readability.csv"), sb.ToString());

        // each item's worst case per world
        var worst = new Dictionary<string, Row>();
        foreach (var r in rows)
        {
            string key = r.world + "|" + r.item;
            Row prev;
            if (!worst.TryGetValue(key, out prev) || r.StandShare < prev.StandShare) worst[key] = r;
        }
        var list = new List<Row>(worst.Values);
        list.Sort((a, b) => a.StandShare.CompareTo(b.StandShare));
        var wb = new StringBuilder("world,kind,item,worst_standout_share,standout_px,footprint_px,ring_contrast,edge_share_3to1,variant,seconds,x,y,flag\n");
        foreach (var r in list)
            wb.AppendFormat("{0},{1},{2},{3:F3},{4},{5},{6:F2},{7:F2},{8},{9},{10:F2},{11:F2},{12}\n", r.world, r.kind, r.item, r.StandShare, r.standOut, r.area,
                            r.ring, r.share, r.variant, r.at, r.where.x, r.where.y, Flag(r));
        File.WriteAllText(Path.Combine(dir, "worst.csv"), wb.ToString());
        int low = 0, weak = 0;
        foreach (var r in rows) { string f = Flag(r); if (f == "LOW") low++; else if (f == "WEAK") weak++; }
        Debug.Log("[SWEEP] " + rows.Count + " measurements: " + low + " LOW (< " + MinStandOut.ToString("F1") + " px 3:1 clear), " + weak +
                  " WEAK (< " + WeakShare + " of it, edge ring < 3:1); worst first:");
        for (int i = 0; i < Mathf.Min(30, list.Count); i++)
        {
            var r = list[i];
            Debug.Log("[SWEEP] WORST " + r.world + " v" + r.variant + " @" + r.at + "s " + r.kind + " " + r.item + ": " + r.standOut + " of " + r.area +
                      " px 3:1 clear (" + (r.StandShare * 100f).ToString("F1") + "%), edge ring " + r.ring.ToString("F2") + ":1 " + Flag(r));
        }
    }

    static float Lin(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
    static float Lum(Color c) => .2126f * Lin(c.r) + .7152f * Lin(c.g) + .0722f * Lin(c.b);
    static float Ratio(float a, float b) => (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);
}
