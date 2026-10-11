using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// "Darken the rails' edges, move the rails out where the screen has room,
// the ship 35% bigger in the main game, star dust 25% bigger".
//
//   SHIP SCALE  ShipScale.Main is 1.35 in gameS1 and 1 everywhere else; a
//               ship dressed in gameS1 is 1.35x the normalised hull, one in
//               any other scene is not; the hull / shield hit polygons keep
//               the same ratio to the art; the pickup reach, the ship's
//               drawing above / below its centre and the spawn rows' ship
//               gap grow with it
//   INSET       RailInset moves the rails out on tall phones and tablets and
//               leaves 16:9 (and 16:10, landscape) exactly as before; the
//               drawn inner edge, BossRails.InnerEdge and the lanes follow
//   REACH       at 9:16, 9:19.5, 9:21 and 3:4 in every world the ship's
//               side reaches the rails' inner edge and never crosses it;
//               the lamp column stays on screen
//   MINES       rail mines ride the drawn rail at every aspect (the mount
//               x follows the rail inset) and a lane re-anchors when the
//               inset changes mid-run
//   EDGE        the rail shader's edge parameters exist and are set per
//               world; rendered, the inner band is darker than without the
//               treatment, its neon lamps keep their light, the outer edge
//               is untouched, and the right rail is its mirror image
//   DUST        star dust spawned in the game is exactly 1.25x its prefab
//               (art, trigger box, footprint); the prefabs are untouched
public static class RailsShipSizeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RSS] PASS  " : "[RSS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Aspects = { "and-1080x1920", "iphone-13", "flip7-1080x2520", "ipad-9" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1");
            ShipScaleInTheMainSceneOnly();
            InsetOnlyWhereThereIsRoom();
            ReachAndMinesOnEveryAspect();
            EdgeTreatment();
            StarDust();
        }
        finally
        {
            ScreenInfo.ClearOverride();
            BossRails.Reset();
            PlayField.Reset();
            WorldPainter.Apply(WorldManager.Worlds[0]);
        }
        Debug.Log("[RSS] failures: " + fails);
        return fails;
    }

    static string F(float v) { return v.ToString("F3"); }

    // ---- ship scale -----------------------------------------------------------------

    static void ShipScaleInTheMainSceneOnly()
    {
        Check("ShipScale.Main is 1.35 (" + ShipScale.Main + ")", ShipScale.Main == 1.35f);
        Check("1.35 in gameS1 and the tutorial, 1 in the title, dock, codex and shop scenes",
              ShipScale.For("gameS1") == 1.35f && ShipScale.For("tutorialS5") == 1.35f && ShipScale.For("titleS0") == 1f &&
              ShipScale.For("shop") == 1f && ShipScale.For("CodexS9") == 1f && ShipScale.For("") == 1f);
        Check("the active scene (gameS1) reads 1.35", ShipScale.Live == 1.35f);

        var game = SceneManager.GetActiveScene();
        var other = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(game);
        float widest = 0f, widestMain = 0f, deepest = 0f, deepestMain = 0f;
        try
        {
            int bad = 0, ratioBad = 0;
            string detail = "";
            for (int id = 0; id < shopingShips.shipTotal; id++)
            {
                if (!ShipId.IsValid(id)) continue;
                var main = Dressed(id, default(Scene));
                var menu = Dressed(id, other);
                var sprite = main.GetComponent<SpriteRenderer>().sprite;
                float k = shopingShips.NormalizedHullScale(sprite);
                if (Mathf.Abs(main.transform.localScale.x - k * 1.35f) > 1e-4f || Mathf.Abs(menu.transform.localScale.x - k) > 1e-4f)
                {
                    bad++;
                    detail += " " + id + ":" + F(main.transform.localScale.x) + "/" + F(menu.transform.localScale.x);
                }
                // the hit polygons: same share of the art at both sizes
                var hm = ShipHitbox.Of(main);
                var hn = ShipHitbox.Of(menu);
                if (hm != null && hn != null)
                {
                    Physics2D.SyncTransforms();
                    Vector2 am = main.GetComponent<SpriteRenderer>().bounds.size, an = menu.GetComponent<SpriteRenderer>().bounds.size;
                    Vector2 cm = hm.Hull.bounds.size, cn = hn.Hull.bounds.size;
                    float rx = (cm.x / am.x) / Mathf.Max(1e-5f, cn.x / an.x), ry = (cm.y / am.y) / Mathf.Max(1e-5f, cn.y / an.y);
                    bool sizeOk = Mathf.Abs(cm.x / cn.x - 1.35f) < .01f && Mathf.Abs(cm.y / cn.y - 1.35f) < .01f;
                    if (Mathf.Abs(rx - 1f) > .01f || Mathf.Abs(ry - 1f) > .01f || !sizeOk)
                    {
                        ratioBad++;
                        detail += " hitbox " + id + " " + F(rx) + "," + F(ry);
                    }
                    if (cn.x > widest) { widest = cn.x; widestMain = cm.x; }
                    if (cn.y > deepest) { deepest = cn.y; deepestMain = cm.y; }
                    if (id == ShipId.Starter)
                        Debug.Log("[RSS] starter hull hitbox " + F(cn.x) + " x " + F(cn.y) + " u -> " + F(cm.x) + " x " + F(cm.y) +
                                  " u (art " + F(an.x) + " x " + F(an.y) + " -> " + F(am.x) + " x " + F(am.y) + "); pickup reach " +
                                  F(hn.Radius) + " -> " + F(hm.Radius));
                    Check(id + ": pickup reach x1.35 in the game only (" + F(hn.Radius) + " -> " + F(hm.Radius) + ")",
                          Mathf.Abs(hm.Radius - ShipHitbox.PickupRadius * 1.35f) < 1e-5f && Mathf.Abs(hn.Radius - ShipHitbox.PickupRadius) < 1e-5f);
                }
                Object.DestroyImmediate(main);
                Object.DestroyImmediate(menu);
            }
            Check("every hull: 1.35x the normalised size in gameS1, the normalised size elsewhere" + detail, bad == 0);
            Check("every baked hitbox keeps its ratio to the art at 1.35x (and is 1.35x as big)", ratioBad == 0);
        }
        finally
        {
            EditorSceneManager.CloseScene(other, true);
        }
        Debug.Log("[RSS] widest hull hitbox over every ship " + F(widest) + " -> " + F(widestMain) + " u, tallest " + F(deepest) + " -> " + F(deepestMain) + " u");
        Check("the guaranteed spawn-row gap (" + F(SpawnLane.ShipGap) + " u) still clears the widest 1.35x hitbox (" + F(widestMain) + " u)",
              widestMain > 0f && widestMain < SpawnLane.ShipGap);

        Check("the ship's drawing above / below its centre grows with it (" + F(ShipReach.HullAbove) + " / " + F(ShipReach.HullBelow) + ")",
              Mathf.Approximately(ShipReach.HullAbove, ShipReach.AuthoredHullAbove * 1.35f) &&
              Mathf.Approximately(ShipReach.HullBelow, ShipReach.AuthoredHullBelow * 1.35f));
        // spawn tuning unchanged: the guaranteed row gap stays the normalised ship's
        Check("spawn rows keep the same gap (" + F(SpawnLane.ShipGap) + " u, unchanged)",
              Mathf.Approximately(SpawnLane.ShipGap, shopingShips.ReferenceHullSize * 1.3f));

        // lifeControler's re-normalisation keeps the factor (it used to overwrite it)
        var src = File.ReadAllText("Assets/Scripts/Gameplay/lifeControler.cs");
        Check("lifeControler normalises with the same ShipScale factor", src.Contains("ShipScale.ForScene(gameObject.scene)"));
        // menus render their own hulls: none of them reads ShipScale
        int menuUses = 0;
        foreach (var f in new[] { "Assets/Scripts/Shop/shopingShips.cs", "Assets/Scripts/Shop/DockBay.cs", "Assets/Scripts/UI/TitleScreenTraffic.cs" })
            if (File.Exists(f) && File.ReadAllText(f).Contains("ShipScale")) menuUses++;
        Check("the dock, shop and title traffic never read ShipScale", menuUses == 0);
    }

    // A ship dressed by spawnShips.ApplyHull in `scene` (default: the active one).
    static GameObject Dressed(int id, Scene scene)
    {
        var prefab = Resources.Load<GameObject>(spawnShips.PrefabPathFor(id));
        var go = Object.Instantiate(prefab);
        go.name = ShipId.ObjectName(id) + "(Clone)";
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
        spawnShips.ApplyHull(go, id);
        return go;
    }

    // ---- inset ----------------------------------------------------------------------

    static void InsetOnlyWhereThereIsRoom()
    {
        float s916 = RailInset.ShiftFor(1080, 1920), sSE = RailInset.ShiftFor(750, 1334), s1610 = RailInset.ShiftFor(1600, 2560);
        float sLand = RailInset.ShiftFor(1920, 1080), s195 = RailInset.ShiftFor(1170, 2532), s21 = RailInset.ShiftFor(1080, 2520);
        float sIpad = RailInset.ShiftFor(1620, 2160), s20 = RailInset.ShiftFor(1080, 2160);
        Debug.Log("[RSS] rail shift per side: 9:16 " + F(s916) + ", iPhone SE " + F(sSE) + ", 16:10 " + F(s1610) + ", 18:9 " + F(s20) +
                  ", 19.5:9 " + F(s195) + ", 21:9 " + F(s21) + ", 3:4 " + F(sIpad) + ", landscape " + F(sLand));
        float B = RailInset.BaseShift, Full = RailInset.BaseShift + RailInset.MaxShift;
        Check("16:9 phones and 16:10 tablets get the base widening only (" + F(B) + ") and landscape none", Mathf.Approximately(s916, B) && Mathf.Approximately(sSE, B) && Mathf.Approximately(s1610, B) && sLand == 0f);
        Check("20:9 moves them out fully too (" + F(RailInset.ShiftFor(1080, 2400)) + ")", Mathf.Approximately(RailInset.ShiftFor(1080, 2400), Full));
        Check("19.5:9, 21:9 and 3:4 move the rails out by the full " + Full + " u",
              Mathf.Approximately(s195, Full) && Mathf.Approximately(s21, Full) && Mathf.Approximately(sIpad, Full));
        // the widened flight lane: 8-12% wider than the old layout on every portrait screen
        float oldHalf = BossRails.ReinforcedInnerEdge;
        Check("lane widening vs the pre-widen layout is 8-12% on 16:9 (" + F(B / oldHalf * 100f) + "%) and on tall phones (" + F(RailInset.BaseShift / (oldHalf + RailInset.MaxShift) * 100f) + "%)",
              B / oldHalf >= .08f && B / oldHalf <= .12f && B / (oldHalf + RailInset.MaxShift) >= .08f && B / (oldHalf + RailInset.MaxShift) <= .12f);
        Check("18:9 eases in between (" + F(s20) + ")", s20 > B && s20 < Full);
    }

    // gameS1 as device `d` shows it, painted as `theme`.
    static void Stage(FitDevice d, WorldTheme theme)
    {
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        var cam = Camera.main;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        BossRails.Reset();
        PlayField.Reset();
        WorldPainter.Apply(theme);
    }

    static void ReachAndMinesOnEveryAspect()
    {
        var shipGo = new GameObject("~RssSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        var sr = shipGo.GetComponent<SpriteRenderer>();
        foreach (var id in Aspects)
        {
            var d = FitDevice.Find(id);
            float shift = RailInset.ShiftFor(d.w, d.h);
            foreach (var theme in WorldManager.Worlds)
            {
                Stage(d, theme);
                var cam = Camera.main;
                float halfW = cam.orthographicSize * cam.aspect;
                float inner = BossRails.InnerEdge;
                WorldPainter.VisibleRailEdges(GameObject.Find("leftPipe"), out float inL, out float outL);
                WorldPainter.VisibleRailEdges(GameObject.Find("rightPipe"), out float inR, out float outR);
                string tag = id + " " + theme.displayName;
                Check(tag + ": the drawn inner edge is the authored one + the inset (" + F(inner) + " = " + F(BossRails.ReinforcedInnerEdge) + " + " + F(shift) + ")",
                      Mathf.Abs(inner - (BossRails.ReinforcedInnerEdge + shift)) < .003f && Mathf.Abs(inL - inR) < .003f);
                Check(tag + ": the rails are further out than today (" + F(inner) + " > " + F(BossRails.ReinforcedInnerEdge) + ")", inner > BossRails.ReinforcedInnerEdge + .2f);
                // the lamp column (20% - 45% of the rail in from its inner edge) stays on screen
                float lampOuter = inner + .45f * (outR - inR);
                Check(tag + ": the rail's lamp column stays on screen (to " + F(lampOuter) + " of " + F(halfW) + "); rail shows " +
                      (100f * (Mathf.Min(outR, halfW) - inR) / (2f * halfW)).ToString("F1") + "% of the width",
                      lampOuter < halfW - .02f);

                // the ship at both clamps: its side on the rail's inner edge, never over it
                float hx = ShipReach.HalfWidth;
                shipGo.transform.position = new Vector3(ShipReach.ClampX(9f), 0f, 0f);
                float right = sr.bounds.max.x;
                shipGo.transform.position = new Vector3(ShipReach.ClampX(-9f), 0f, 0f);
                float left = sr.bounds.min.x;
                Check(tag + ": the ship reaches the rails and stays off them (centre +/-" + F(hx) + ", hull " + F(left) + " .. " + F(right) +
                      ", rails at +/-" + F(inner) + ")",
                      right <= inner + 1e-3f && left >= -inner - 1e-3f &&
                      Mathf.Abs(hx + ShipScale.HullHalfWidth - inner) < 1e-3f);

                // rail mines ride the drawn rail
                float mx = enmiesOnBoard.WorldRailX(false), mxl = enmiesOnBoard.WorldRailX(true);
                Check(tag + ": rail mines mount on the drawn rail at +/-" + F(mx) + " (inner " + F(inner) + " + bite - reach)",
                      Mathf.Abs(mx - RailMineArt.MountX(inner)) < 1e-3f && Mathf.Abs(mx + mxl) < 1e-4f &&
                      mx + RailMineArt.ClampReach > inner && mx + RailMineArt.ClampReach < outR);
                // the lanes follow
                Check(tag + ": enemy and pickup lanes widen with the rails (" + F(SpawnLane.LaneHalf) + ", " + F(RailInset.PickupLaneHalf) + ")",
                      Mathf.Abs(SpawnLane.LaneHalf - (SpawnLane.AuthoredLaneHalf + shift)) < 1e-4f &&
                      Mathf.Abs(RailInset.PickupLaneHalf - (RailInset.AuthoredPickupLane + shift)) < 1e-4f &&
                      Mathf.Abs(AtomWander.LaneHalfWidth - (AtomWander.AuthoredLaneHalfWidth + shift)) < 1e-4f);
            }
        }
        Object.DestroyImmediate(shipGo);
        foreach (var s in Object.FindObjectsByType<spawnShips>(FindObjectsSortMode.None)) Object.DestroyImmediate(s.gameObject);

        // a fold mid-run: the rails move out, a lane already on the board follows them
        Stage(FitDevice.Find("and-1080x1920"), WorldManager.Worlds[0]);
        var lane = new GameObject("RailMineLane");
        lane.transform.position = new Vector3(enmiesOnBoard.WorldRailX(false), 3f, 0f);
        var scroller = lane.AddComponent<RailLaneScroller>();
        score.pauseCounter = 1;   // held: no scrolling
        TestHarness.Send(scroller, "Update");
        float before = lane.transform.position.x;
        ScreenInfo.ClearOverride();
        var flip = FitDevice.Find("flip7-1080x2520");
        ScreenInfo.Override(flip.w, flip.h, flip.Safe, flip.Cutouts, flip.ReportedDpi, flip.ios);
        WorldPainter.RefreshInset();
        TestHarness.Send(scroller, "Update");
        float after = lane.transform.position.x;
        Check("a rail-mine lane re-anchors when the rails move out mid-run (" + F(before) + " -> " + F(after) + ", rail mount " + F(enmiesOnBoard.WorldRailX(false)) + ")",
              Mathf.Abs(after - enmiesOnBoard.WorldRailX(false)) < 1e-4f && after > before + .2f);
        Object.DestroyImmediate(lane);
        score.pauseCounter = 0;
    }

    // ---- the dark edge --------------------------------------------------------------

    static void EdgeTreatment()
    {
        ScreenInfo.ClearOverride();
        foreach (var theme in WorldManager.Worlds)
        {
            WorldPainter.Apply(theme);
            var live = GameObject.Find("leftPipe").GetComponent<Renderer>().sharedMaterial;
            var liveR = GameObject.Find("rightPipe").GetComponent<Renderer>().sharedMaterial;
            var e = WorldPainter.EdgeFor(theme.displayName);
            bool props = live.HasProperty("_EdgeDark") && live.HasProperty("_EdgeWidth") && live.HasProperty("_EdgeShadow") &&
                         live.HasProperty("_EdgeLampKeep") && live.HasProperty("_EdgeInnerU");
            Check(theme.displayName + ": both rails carry the edge treatment (dark " + e.dark + ", band " + e.width + " of the rail, shadow " + e.shadow + ")",
                  props && live.GetFloat("_EdgeDark") == e.dark && liveR.GetFloat("_EdgeDark") == e.dark &&
                  live.GetFloat("_EdgeWidth") == e.width && e.dark >= .45f && e.dark <= .6f && e.width >= .1f && e.width <= .18f);
            if (props) RenderedEdge(live, theme);
        }
    }

    static void RenderedEdge(Material live, WorldTheme theme)
    {
        string world = theme.displayName;
        string folder = string.IsNullOrEmpty(theme.resourceFolder) ? world : theme.resourceFolder;
        string path = "Assets/Art/Resources/Worlds/" + folder + "/" + WorldPainter.RailTextureName(world) + ".png";
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(File.ReadAllBytes(path));
        source.filterMode = FilterMode.Point;
        int w = source.width, h = 256;   // a strip of the rail is plenty
        var px = source.GetPixels32();
        float innerU = live.GetFloat("_EdgeInnerU"), outerU = live.GetFloat("_EdgeOuterU");
        float band = live.GetFloat("_EdgeWidth");

        Color32[] on = Render(live, source, h, false, true), off = Render(live, source, h, false, false);
        Color32[] onR = Render(live, source, h, true, true);
        int bandN = 0, lampN = 0, outerN = 0, outerSame = 0, gapN = 0;
        double bandOn = 0, bandOff = 0, lampOn = 0, lampOff = 0, gapOn = 0, gapOff = 0;
        int mirrorBad = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // the strip renders texture rows [0, h)
                var s = px[y * w + x];
                float u = (x + .5f) / w;
                float dd = (innerU - u) / (innerU - outerU);
                int i = y * w + x;
                float lOn = Lum(on[i]), lOff = Lum(off[i]);
                int max = Mathf.Max(s.r, Mathf.Max(s.g, s.b)), min = Mathf.Min(s.r, Mathf.Min(s.g, s.b));
                bool lamp = s.a > 250 && max > 220 && max - min > 120;
                if (s.a > 250 && max > 8 && dd >= 0f && dd < band * .5f && !lamp) { bandN++; bandOn += lOn; bandOff += lOff; }
                if (lamp && dd >= 0f && dd < band) { lampN++; lampOn += lOn; lampOff += lOff; }
                if (lamp && dd >= .2f && dd < .45f) { }
                if (s.a > 250 && max > 8 && dd > .5f) { outerN++; if (Mathf.Abs(lOn - lOff) < 2f / 255f) outerSame++; }
                if (s.a < 8 && dd < 0f && dd > -.03f) { gapN++; gapOn += lOn; gapOff += lOff; }
                // the right wall (UV x scale -1) draws the same pixels mirrored
                var a = on[i]; var b = onR[y * w + (w - 1 - x)];
                if (Mathf.Abs(a.r - b.r) > 3 || Mathf.Abs(a.g - b.g) > 3 || Mathf.Abs(a.b - b.b) > 3) mirrorBad++;
            }
        double bandK = bandN > 0 ? bandOn / Math.Max(1e-6, bandOff) : 1, lampK = lampN > 0 ? lampOn / Math.Max(1e-6, lampOff) : 1;
        double gapK = gapN > 0 ? gapOn / Math.Max(1e-6, gapOff) : 1;
        Debug.Log("[RSS] " + world + " rendered edge: inner band " + bandN + " px at " + (bandK * 100).ToString("F0") + "% of their old light, lamps in the band " +
                  lampN + " px at " + (lampK * 100).ToString("F0") + "%, gaps / strip past the edge " + gapN + " px at " + (gapK * 100).ToString("F0") +
                  "%, outer half " + outerSame + "/" + outerN + " untouched, mirror mismatches " + mirrorBad);
        Check(world + ": the inner edge band renders darker (" + (bandK * 100).ToString("F0") + "% of before, " + bandN + " px)", bandN > 100 && bandK < .75);
        Check(world + ": the neon lamps keep their light (" + (lampK * 100).ToString("F0") + "%, " + lampN + " px)", lampN == 0 || lampK > .85);
        // no near-black shadow fill any more: transparent art stays transparent (RailTransparencyTest)
        Check(world + ": the gaps and the strip past the edge are not shadowed (" + (gapK * 100).ToString("F0") + "% of before)", gapN > 50 && gapK > .98);
        Check(world + ": the outer half of the rail (against the screen edge) is untouched (" + outerSame + "/" + outerN + ")", outerN > 0 && outerSame == outerN);
        Check(world + ": the right rail is the left one mirrored (" + mirrorBad + " px differ)", mirrorBad < w * h / 500);
        Object.DestroyImmediate(source);
    }

    static float Lum(Color32 c) { return (.2126f * c.r + .7152f * c.g + .0722f * c.b) / 255f; }

    // `live` drawn over a mid-blue background, texel for texel: the rail's
    // bottom `rows` texture rows. mirror: the right wall's UV scale (-1).
    static Color32[] Render(Material live, Texture2D source, int rows, bool mirror, bool edge)
    {
        int w = source.width;
        var mat = new Material(live);
        mat.mainTexture = source;
        mat.mainTextureScale = new Vector2(mirror ? -1f : 1f, rows / (float)source.height);
        mat.mainTextureOffset = Vector2.zero;
        mat.SetFloat("_Overlap", 0f);
        if (!edge) { mat.SetFloat("_EdgeDark", 0f); mat.SetFloat("_EdgeShadow", 0f); }
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.layer = 31;
        quad.transform.position = new Vector3(0, 0, -100);
        quad.transform.localScale = new Vector3(w, rows, 1);
        quad.GetComponent<Renderer>().sharedMaterial = mat;
        var go = new GameObject("~RssProbe", typeof(Camera));
        var cam = go.GetComponent<Camera>();
        cam.cullingMask = 1 << 31;
        cam.orthographic = true;
        cam.orthographicSize = rows * .5f;
        cam.aspect = w / (float)rows;
        cam.transform.position = new Vector3(0, 0, -110);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.2f, .4f, .6f, 1);
        var rt = new RenderTexture(w, rows, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(w, rows, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, w, rows), 0, 0);
        read.Apply();
        RenderTexture.active = old;
        var result = read.GetPixels32();
        cam.targetTexture = null;
        Object.DestroyImmediate(read);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(mat);
        Object.DestroyImmediate(quad);
        Object.DestroyImmediate(go);
        return result;
    }

    // ---- star dust ------------------------------------------------------------------

    static void StarDust()
    {
        Check("PickupArt.StarDustScale is 1.25", PickupArt.StarDustScale == 1.25f);
        var spawner = Object.FindFirstObjectByType<spawnGoodStuff>();
        Check("gameS1 has its pickup spawner", spawner != null);
        if (spawner == null) return;
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (var (method, field, name, drawn) in new[] { ("spawnSmStar", "smStar", "Star Dust (small)", .064f), ("spawnMidStar", "midStar", "Bright Star (large)", .256f) })
        {
            var prefab = (GameObject)typeof(spawnGoodStuff).GetField(field, Inst).GetValue(spawner);
            var beforeSet = new HashSet<GameObject>();
            foreach (var c in Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None)) beforeSet.Add(c.gameObject);
            typeof(spawnGoodStuff).GetMethod(method, Inst).Invoke(spawner, new object[] { 0, new Vector3(0f, 40f, 0f) });
            GameObject star = null;
            foreach (var c in Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
                if (!beforeSet.Contains(c.gameObject) && PrefabName.Is(c.gameObject, PrefabName.Is(prefab, "smStar1") ? "smStar1" : "LargeStar1")) star = c.gameObject;
            Check(name + ": spawned", star != null);
            if (star == null) continue;
            Physics2D.SyncTransforms();
            var ps = prefab.transform.localScale;
            var ss = star.transform.localScale;
            var box = star.GetComponent<BoxCollider2D>();
            var pbox = prefab.GetComponent<BoxCollider2D>();
            Vector2 worldBox = Vector2.Scale(box.size, (Vector2)star.transform.lossyScale);
            Vector2 prefabBox = Vector2.Scale(pbox.size, (Vector2)ps);
            float art = star.GetComponent<SpriteRenderer>().bounds.size.x;
            var fp = star.GetComponent<SpawnFootprint>();
            Debug.Log("[RSS] " + name + ": scale " + F(ps.x) + " -> " + F(ss.x) + ", drawn " + F(drawn) + " -> " + F(art) + " u, trigger box " +
                      F(prefabBox.x) + " x " + F(prefabBox.y) + " -> " + F(worldBox.x) + " x " + F(worldBox.y) + " u" +
                      (fp != null ? ", footprint half " + F(fp.half.x) + " x " + F(fp.half.y) : ""));
            Check(name + ": exactly 1.25x its prefab in the game (scale " + F(ps.x) + " -> " + F(ss.x) + ", art " + F(art) + " u, box " + F(worldBox.x) + " u)",
                  Mathf.Abs(ss.x - ps.x * 1.25f) < 1e-5f && Mathf.Abs(ss.y - ps.y * 1.25f) < 1e-5f &&
                  Mathf.Abs(worldBox.x - prefabBox.x * 1.25f) < 1e-4f && Mathf.Abs(worldBox.y - prefabBox.y * 1.25f) < 1e-4f &&
                  Mathf.Abs(art - drawn * 1.25f) < drawn * .02f);
            Check(name + ": its spawn footprint is the bigger box", fp == null || Mathf.Abs(fp.half.x - prefabBox.x * .625f) < 1e-3f);
            Check(name + ": the prefab itself is untouched (tutorial, credits, codex)", Mathf.Abs(prefab.transform.localScale.x - (field == "smStar" ? .05f : .1f)) < 1e-6f);
            Check(name + ": centre inside the pickup lane (" + F(star.transform.position.x) + ")", Mathf.Abs(star.transform.position.x) <= RailInset.PickupLaneHalf + 1e-4f);
            Object.DestroyImmediate(star);
        }
        string tut = File.ReadAllText("Assets/Scripts/Tutorial/spawnGoodStuffTut.cs");
        Check("the tutorial's star dust keeps its size", !tut.Contains("ApplyInGameScale"));
    }
}
