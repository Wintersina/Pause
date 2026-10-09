using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// "I keep mistaking enemy bullets for atoms." Atoms (and star dust) against
// every hostile shot -- each world's roster shot in every kind, the world's
// elites' shots, its boss's bolts and shards -- rendered one at a time over
// that world's backdrop at a 1080 px phone's pixel density
// (AtomClarityPreview.PixelsPerUnit), measured from the pixels they change:
//
//   hue        the saturation-weighted circular mean hue of what changed
//              strongly (the drawing, not its fringes)
//   softness   the share of changed pixels changed only a little (a soft
//              halo bleeding out): high for a friendly pickup, low for a
//              hard-outlined shot
//   size       the extent of the pixels changed strongly (the drawing)
//   silhouette the strongly changed pixels, centred and fitted to one
//              square: two shapes' IoU
//   hostile    the share of strongly changed pixels in the hostile family's
//   edge       hot pink (HostileShotPalette, +-HostileEdgeSlack deg): every
//              hostile shot's outline / rim carries it, no pickup does
//
// FAILS when any atom and any hostile shot are too alike: an elite / roster
// shot (whose colours the game controls, HostileShotPalette) must sit
// MinHueGap away from every pickup's hue AND differ on MinShotCues of the
// five cues; a boss shot (painted art, any colour) on MinBossCues. Also:
// each pickup big enough and standing out of every backdrop, the four atoms
// distinct from each other, the friendly / hostile language in place.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites AtomClarityTest
public static class AtomClarityTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATOMCLARITY] PASS  " : "[ATOMCLARITY] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // ---- thresholds ----
    public const float MinHueGap = 35f;          // degrees, pickup vs elite / roster shot
    public const float CueHueGap = 35f;          // a boss shot's hue counts as a cue past this
    public const float CueSoftGap = .15f;        // pickup softness - shot softness
    public const float CueSizeRatio = 1.4f;      // pickup extent / shot extent (either way)
    public const float CueMaxIoU = .55f;         // silhouettes this different
    public const float CuePinkGap = .08f;        // shot's hostile-pink share - pickup's
    public const float HostileEdgeSlack = 6f;
    public const int MinShotCues = 3;            // of 5, for an elite / roster shot (hue among them)
    public const int MinBossCues = 2;            // of 5, for a boss's painted shot
    public const float MinAtomPixels = 46f;      // an atom's drawing, px across on a 1080 px phone
    public const float MinAtomSoftness = .3f;    // every atom wears a soft halo
    public const float MaxShotSoftness = .36f;    // no hostile shot does
    public const int MinStandOut = 60;           // pixels changed strongly over the backdrop
    public const float AtomPairHueGap = 30f;     // the four atoms tell apart by hue ...
    public const float AtomPairMaxIoU = .8f;     // ... and by shape

    const int Win = 170;                          // px window per object (1.17 u)
    const float StrongDiff = .16f, SoftDiff = .035f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };
    static readonly string[] PickupNames = { "blue shield", "red pause", "violet capacitor", "green heal", "bright star dust" };

    struct Look
    {
        public string name;
        public float hue, softness, extent, pink;
        public int strong;
        public bool[] shape;   // 32 x 32
        public bool boss;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Language();
            for (int w = 0; w < Worlds.Length; w++) World(w);
        }
        finally
        {
            EliteSystem.Clear();
            BossEncounter.ResetRun();
            Time.timeScale = 1f;
        }
        Debug.Log("[ATOMCLARITY] failures: " + fails);
        return fails;
    }

    // ---- the friendly / hostile language, structurally ----
    static void Language()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        var kinds = new[] { PickupKind.Shield, PickupKind.Pause, PickupKind.Cooldown, PickupKind.Heal };
        for (int i = 0; i < 4; i++)
        {
            var go = AtomClarityPreview.SpawnPickup(i, new Vector3(i * 2f, 0f, 0f));
            var g = go.GetComponent<PickupGlow>();
            var sr = go.GetComponent<SpriteRenderer>();
            float drawn = Mathf.Max(sr.bounds.size.x, sr.bounds.size.y);
            float hit = 0f;
            var box = go.GetComponent<BoxCollider2D>();
            var circle = go.GetComponent<CircleCollider2D>();
            if (box != null) hit = box.size.x * Mathf.Abs(go.transform.lossyScale.x);
            if (circle != null) hit = circle.radius * 2f * Mathf.Abs(go.transform.lossyScale.x);
            Check(PickupNames[i] + " atom wears the friendly look: a soft halo and an orbit ring in its own colour",
                  g != null && g.Kind == kinds[i] && g.Halo != null && g.Ring != null &&
                  g.Halo.sprite == PickupGlow.HaloSprite && g.Ring.sprite == PickupGlow.RingSprite &&
                  g.Halo.sortingOrder < sr.sortingOrder && g.Ring.sortingOrder < sr.sortingOrder);
            Check("... drawn " + PickupArt.AtomVisualScale + "x bigger (" + drawn.ToString("F3") + " u) with its hitbox unchanged (" +
                  hit.ToString("F3") + " u across, was " + HealAtom.TargetDiameter + ")",
                  Mathf.Abs(drawn - HealAtom.TargetDiameter * PickupArt.AtomVisualScale) < .03f &&
                  Mathf.Abs(hit - HealAtom.TargetDiameter) < .005f);
            // the breath is smooth and freezes with the world
            float s0 = g.Halo.transform.localScale.x;
            g.Advance(0f);
            bool frozen = Mathf.Approximately(s0, g.Halo.transform.localScale.x);
            float lo = float.MaxValue, hi = 0f, worstStep = 0f, last = s0;
            for (int k = 0; k < 120; k++)
            {
                g.Advance(1f / 60f);
                float s = g.Halo.transform.localScale.x;
                lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s);
                worstStep = Mathf.Max(worstStep, Mathf.Abs(s - last) / s0);
                last = s;
            }
            Check("... its halo breathes gently and smoothly (" + (lo / s0).ToString("F2") + ".." + (hi / s0).ToString("F2") +
                  ", biggest frame step " + (worstStep * 100f).ToString("F1") + "%), and holds still while the world is frozen",
                  hi / lo > 1.06f && hi / lo < 1.3f && worstStep < .02f && frozen);
        }
        // hostile shots: the hostile family, hard and pointed
        var styles = new List<(string key, EliteDef def, EliteShots.Kind kind)>();
        foreach (var d in EliteCatalog.All) styles.Add((d.key, d, EliteShots.KindOf(d.shotKind)));
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b != null && (b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Ring || b.attack == EnemyAttack.Cross || b.attack == EnemyAttack.Lob))
                styles.Add((def.key, b.ShotStyle, b.shotKind));
        }
        int inFamily = 0, halos = 0;
        string off = "";
        foreach (var (key, d, kind) in styles)
        {
            var s = EliteSystem.Shots.Fire(null, d, kind, new Vector2(0f, 3f), Vector2.down);
            if (s == null) continue;
            if (HostileShotPalette.InFamily(s.BodyTint) && HostileShotPalette.InFamily(s.ShotTint)) inFamily++;
            else off += " " + key;
            foreach (var r in s.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.sprite == PickupGlow.HaloSprite || r.sprite == PickupGlow.RingSprite || r.sprite == HostileGlow.Halo) halos++;
            s.Recycle();
        }
        Check("every elite and roster shot (" + styles.Count + ") is drawn in the hostile family " + HostileShotPalette.HueMin + ".." +
              HostileShotPalette.HueMax + " deg (" + inFamily + ";" + off + ") and none wears a halo or ring (" + halos + ")",
              inFamily == styles.Count && halos == 0);
        var shot = EliteSystem.Shots.Fire(null, styles[0].def, EliteShots.Kind.Bolt, Vector2.zero, Vector2.down);
        var core = shot.transform.Find("Core").GetComponent<SpriteRenderer>();
        var seen = new HashSet<Color>();
        for (int k = 0; k < 30; k++) { EliteSystem.Shots.Step(1f / 60f); seen.Add(core.color); }
        Check("a hostile shot's core flickers hard (" + seen.Count + " distinct colours over half a second: on / off, never a smooth breath)",
              seen.Count == 2);
        shot.Recycle();
        foreach (var sprite in new[] { EliteFxArt.Bolt, EliteFxArt.Shard, EliteFxArt.Shell, EliteFxArt.Slag })
        {
            float fill = Fill(sprite);
            Check("the " + sprite.name + " drawing is pointed / spiked, not a round blob, capsule or gem (fills " +
                  (fill * 100f).ToString("F0") + "% of its box; a disc fills 79%)", fill < .55f);
        }
    }

    static float Fill(Sprite s)
    {
        var px = s.texture.GetPixels32();
        int n = 0;
        foreach (var p in px) if (p.a > 64) n++;
        return n / (float)px.Length;
    }

    // ---- one world ----
    static void World(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        EliteSystem.Clear();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var cam = AtomClarityPreview.PhoneCamera(Win, Win, 0f);
        var backdrop = AtomClarityPreview.Backdrop(world);
        HazardRuntime.Ensure();
        var rt = new RenderTexture(Win, Win, 24);
        var tex = new Texture2D(Win, Win, TextureFormat.RGB24, false);
        cam.targetTexture = rt;

        // a grid of spots over the lane, 1.4 u apart (beyond one window)
        int slot = 0;
        Vector3 Spot() { int i = slot++; return new Vector3(-2.1f + (i % 4) * 1.4f, 3.6f - (i / 4) * 1.4f, 0f); }

        var pickups = new List<Look>();
        for (int i = 0; i < 5; i++)
        {
            var at = Spot();
            var go = AtomClarityPreview.SpawnPickup(i, at);
            pickups.Add(Measure(cam, rt, tex, go, at, PickupNames[i], false));
        }

        var shots = new List<Look>();
        var roster = AtomClarityPreview.RosterShooter(world);
        if (roster != null)
            foreach (EliteShots.Kind kind in System.Enum.GetValues(typeof(EliteShots.Kind)))
            {
                var at = Spot();
                var s = EliteSystem.Shots.Fire(null, roster.ShotStyle, kind, at, Vector2.down * .01f);
                if (s == null) continue;
                s.AsRosterShot(null, 0f);
                shots.Add(Measure(cam, rt, tex, s.gameObject, at, "roster " + kind, false));
            }
        var elites = new List<EliteDef>();
        EliteCatalog.ForWorld(world, elites);
        foreach (var d in elites)
        {
            var at = Spot();
            var s = EliteSystem.Shots.Fire(null, d, EliteShots.KindOf(d.shotKind), at, Vector2.down * .01f);
            if (s != null) shots.Add(Measure(cam, rt, tex, s.gameObject, at, d.key + " " + d.shotKind, false));
        }
        var pool = new BossProjectilePool(8, 1);
        var boss = BossCatalog.ForWorld(world);
        foreach (var style in new[] { BossShotStyle.Bolt, BossShotStyle.Shard })
        {
            var at = Spot();
            var s = pool.Fire(boss, style, at, Vector2.down * .01f);
            if (s != null) shots.Add(Measure(cam, rt, tex, s.gameObject, at, "boss " + style, true));
        }

        string name = Worlds[world];
        // each pickup: big enough, soft, standing out
        foreach (var p in pickups)
        {
            bool atom = p.name != "bright star dust";
            Check(name + ": " + p.name + " stands out (" + p.strong + " px changed strongly, need " + MinStandOut + ")" +
                  (atom ? ", is " + p.extent.ToString("F0") + " px across (need " + MinAtomPixels + ") with a soft halo (" +
                          (p.softness * 100f).ToString("F0") + "% soft, need " + (MinAtomSoftness * 100f).ToString("F0") + "%)" : ""),
                  p.strong >= MinStandOut && (!atom || (p.extent >= MinAtomPixels && p.softness >= MinAtomSoftness)));
        }
        // the four atoms tell apart
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
            {
                float hue = HueGap(pickups[i].hue, pickups[j].hue), iou = IoU(pickups[i].shape, pickups[j].shape);
                Check(name + ": the " + pickups[i].name + " and " + pickups[j].name + " atoms differ in colour (" + hue.ToString("F0") +
                      " deg) and shape (IoU " + iou.ToString("F2") + ")", hue >= AtomPairHueGap && iou <= AtomPairMaxIoU);
            }
        // every pickup against every hostile shot
        int pairs = 0, bad = 0;
        foreach (var s in shots)
        {
            if (!s.boss)
                Check(name + ": " + s.name + " is hard-edged (" + (s.softness * 100f).ToString("F0") + "% soft, at most " +
                      (MaxShotSoftness * 100f).ToString("F0") + "%)", s.softness <= MaxShotSoftness);
            Check(name + ": " + s.name + " carries the hostile pink edge (" + (s.pink * 100f).ToString("F0") + "% of its pixels, need " +
                  (CuePinkGap * 100f).ToString("F0") + "%)", s.pink >= CuePinkGap);
            foreach (var p in pickups)
            {
                pairs++;
                float hue = p.hue < -500f || s.hue < -500f ? 0f : HueGap(p.hue, s.hue);   // no colour seen: no hue cue
                float soft = p.softness - s.softness;
                float size = Mathf.Max(p.extent, s.extent) / Mathf.Max(1f, Mathf.Min(p.extent, s.extent));
                float iou = IoU(p.shape, s.shape);
                float pink = s.pink - p.pink;
                int cues = (hue >= CueHueGap ? 1 : 0) + (soft >= CueSoftGap ? 1 : 0) + (size >= CueSizeRatio ? 1 : 0) +
                           (iou <= CueMaxIoU ? 1 : 0) + (pink >= CuePinkGap ? 1 : 0);
                bool ok = s.boss ? cues >= MinBossCues : hue >= MinHueGap && cues >= MinShotCues;
                string line = name + ": " + p.name + " vs " + s.name + " -- hue " + hue.ToString("F0") + " deg (" + p.hue.ToString("F0") + " / " + s.hue.ToString("F0") + "), softness gap " +
                              soft.ToString("F2") + ", hostile-pink gap " + pink.ToString("F2") + ", size x" + size.ToString("F2") + ", silhouette IoU " + iou.ToString("F2") +
                              " -> " + cues + " cues";
                if (!ok) { bad++; Check(line, false); }
                else Debug.Log("[ATOMCLARITY] INFO  " + line);
            }
        }
        Check(name + ": every pickup is told apart from every hostile shot (" + (pairs - bad) + " of " + pairs + " pairs; " +
              shots.Count + " shots)", bad == 0 && shots.Count >= 4);

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        pool.Dispose();
        Object.DestroyImmediate(backdrop.gameObject);
    }

    // Renders the window around `at` with `go` hidden, then shown, and
    // measures what it changed.
    static Look Measure(Camera cam, RenderTexture rt, Texture2D tex, GameObject go, Vector3 at, string name, bool boss)
    {
        cam.transform.position = new Vector3(at.x, at.y, -10f);
        go.SetActive(false);
        var bg = Grab(cam, rt, tex);
        go.SetActive(true);
        var fg = Grab(cam, rt, tex);
        string dump = System.Environment.GetEnvironmentVariable("ATOM_CLARITY_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            System.IO.Directory.CreateDirectory(dump);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dump, PlayerPrefs.GetInt(WorldManager.PrefsCurrentWorld) + "-" + name.Replace(' ', '_') + ".png"), tex.EncodeToPNG());
        }
        var look = new Look { name = name, boss = boss, shape = new bool[32 * 32] };
        // only inside the object's own renderers (a backdrop that twinkles
        // on its own between the two renders must not count as the object)
        bool any = false;
        var bounds = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        Vector3 lo = cam.WorldToScreenPoint(bounds.min), hi = cam.WorldToScreenPoint(bounds.max);
        int bx0 = Mathf.FloorToInt(lo.x) - 1, by0 = Mathf.FloorToInt(lo.y) - 1, bx1 = Mathf.CeilToInt(hi.x) + 1, by1 = Mathf.CeilToInt(hi.y) + 1;
        double sx = 0, sy = 0, sw = 0, dx = 0, dy = 0, dw = 0;
        int changed = 0, strong = 0, pinkCount = 0, x0 = Win, y0 = Win, x1 = -1, y1 = -1;
        var strongMask = new bool[Win * Win];
        for (int i = 0; i < fg.Length; i++)
        {
            Color f = fg[i], b = bg[i];
            int ix = i % Win, iy = i / Win;
            if (ix < bx0 || ix > bx1 || iy < by0 || iy > by1) continue;
            float d = Mathf.Max(Mathf.Abs(f.r - b.r), Mathf.Max(Mathf.Abs(f.g - b.g), Mathf.Abs(f.b - b.b)));
            if (d < SoftDiff) continue;
            changed++;
            Color.RGBToHSV(f, out float h, out float s, out float v);
            if (d < StrongDiff) continue;
            // the hue of the drawing itself (strongly changed pixels): soft
            // fringes blended with the backdrop would drag a thin shot's hue
            // towards the sky behind it
            // ... and only the light it adds: a dark outline over a bright
            // nebula is the nebula darkened, not the object's colour
            float w = s * v * d;
            if (s > .25f && v > .25f)
            {
                if (Lum(f) >= Lum(b))
                {
                    sx += Mathf.Cos(h * 2f * Mathf.PI) * w;
                    sy += Mathf.Sin(h * 2f * Mathf.PI) * w;
                    sw += w;
                }
                // fallback for a drawing darker than a bright sky everywhere
                dx += Mathf.Cos(h * 2f * Mathf.PI) * w;
                dy += Mathf.Sin(h * 2f * Mathf.PI) * w;
                dw += w;
            }
            strong++;
            if (s > .3f && v > .3f && h * 360f >= HostileShotPalette.HueMin - HostileEdgeSlack &&
                h * 360f <= HostileShotPalette.HueMax + HostileEdgeSlack) pinkCount++;
            strongMask[i] = true;
            int x = i % Win, y = i / Win;
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        if (sw <= 0) { sx = dx; sy = dy; sw = dw; }
        look.hue = sw > 0 ? Mathf.Repeat(Mathf.Atan2((float)sy, (float)sx) * Mathf.Rad2Deg, 360f) : -999f;
        look.softness = changed > 0 ? (changed - strong) / (float)changed : 0f;
        look.strong = strong;
        look.pink = strong > 0 ? pinkCount / (float)strong : 0f;
        look.extent = x1 >= x0 ? Mathf.Max(x1 - x0 + 1, y1 - y0 + 1) : 0f;
        if (x1 >= x0)
        {
            // centred, fitted to a square of the larger side
            float side = look.extent, cx = (x0 + x1 + 1) * .5f, cy = (y0 + y1 + 1) * .5f;
            for (int v = 0; v < 32; v++)
                for (int u = 0; u < 32; u++)
                {
                    int px = Mathf.FloorToInt(cx - side * .5f + (u + .5f) / 32f * side);
                    int py = Mathf.FloorToInt(cy - side * .5f + (v + .5f) / 32f * side);
                    look.shape[v * 32 + u] = px >= 0 && py >= 0 && px < Win && py < Win && strongMask[py * Win + px];
                }
        }
        go.SetActive(false);   // out of the next windows' way
        return look;
    }

    static Color[] Grab(Camera cam, RenderTexture rt, Texture2D tex)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, Win, Win), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        return tex.GetPixels();
    }

    static float Lum(Color c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;

    static float HueGap(float a, float b)
    {
        float d = Mathf.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    static float IoU(bool[] a, bool[] b)
    {
        int i = 0, u = 0;
        for (int k = 0; k < a.Length; k++)
        {
            if (a[k] && b[k]) i++;
            if (a[k] || b[k]) u++;
        }
        return u > 0 ? i / (float)u : 1f;
    }
}
