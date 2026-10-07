using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// "Rails and rail mines roll nicely" (BoardRoll).
//
//   - the board's roll advances once a frame however many callers ask, by the
//     same step for the rail art and every rail lane; nothing rolls on a
//     frame nobody asks (paused, frozen, dead)
//   - the rail art's offset is computed from the total: bounded in [0, 1),
//     continuous, the same on both walls, exact after an hour of flight
//   - drawn on whole screen pixels: the art's step each frame is a whole
//     number of pixels, and a mine is drawn on the same pixel grid
//   - a mine stays registered to the rail art under it frame to frame,
//     through speed changes, a slide and a shove
//   - rendered: with the snap the rail strip (seam included) moves rigidly
//     from one frame to the next; without it the texels crawl
//   - a world change keeps both walls valid and in phase
//   - the tunables reach the other modes (slower rail, legacy tile rate)
public static class RailsRollTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ROLLT] PASS  " : "[ROLLT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const int W = 1080, H = 2520;
    static int frame;
    static Camera cam;
    static GameObject[] walls;
    static float ortho0, aspect0, ppu;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
            cam = Camera.main;
            ortho0 = cam.orthographicSize;
            aspect0 = cam.aspect;
            walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
            cam.aspect = W / (float)H;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
            ppu = H / (2f * cam.orthographicSize);
            BoardRoll.PixelsPerUnitOverride = ppu;
            BoardRoll.FrameOverride = () => frame;
            Fresh();
            OneRollAFrame();
            OffsetIsBoundedContinuousAndInPhase();
            WholePixelSteps();
            MinesStayRegistered();
            RenderedRigidly();
            WorldChange();
            OtherModesReachable();
            SourceWiring();
        }
        finally
        {
            BoardRoll.PixelsPerUnitOverride = 0f;
            BoardRoll.FrameOverride = null;
            BoardRoll.PixelSnap = true;
            BoardRoll.LegacyTileRate = false;
            BoardRoll.RailRate = 1f;
            BoardRoll.Reset();
            if (cam != null) { cam.orthographicSize = ortho0; cam.aspect = aspect0; cam.targetTexture = null; }
            foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
            foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        }
        Debug.Log("[ROLLT] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        BoardRoll.Reset();
        BoardRoll.PixelSnap = true;
        BoardRoll.LegacyTileRate = false;
        BoardRoll.RailRate = 1f;
        frame = 100;
    }

    static void Stage(int world)
    {
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        WorldPainter.Apply(WorldManager.Worlds[world]);
        foreach (var wall in walls)
        {
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
    }

    static float OffsetOf(GameObject wall)
    {
        var r = wall.GetComponent<Renderer>();
        moveBackGround.ApplyRoll(r.sharedMaterial, r);
        return r.sharedMaterial.mainTextureOffset.y;
    }

    // ---- 1 -------------------------------------------------------------------

    static void OneRollAFrame()
    {
        Fresh();
        frame++;
        float a = BoardRoll.Advance(.3f, Dt);          // the left wall
        float b = BoardRoll.Advance(.3f, Dt);          // the right wall
        float c = BoardRoll.Advance(.31f, Dt);         // a rail lane, a hair later in the frame (the ramp has ticked)
        Check("three callers in one frame roll the board once, and all get the same step (" + a.ToString("F4") + " u)",
              Mathf.Approximately(a, .3f * 30f * Dt) && a == b && b == c && System.Math.Abs(BoardRoll.Distance - a) < 1e-9);
        frame++;
        BoardRoll.Advance(.3f, Dt);
        Check("the next frame rolls again", System.Math.Abs(BoardRoll.Distance - 2.0 * a) < 1e-6);
        double before = BoardRoll.Distance;
        for (int i = 0; i < 600; i++) frame++;         // paused: nobody advances
        Check("frames nobody rolls on (paused, frozen, dead) roll nothing, and a lane asking afterwards moves by 0",
              BoardRoll.Distance == before && BoardRoll.StepThisFrame == 0f);
        frame++;
        BoardRoll.Advance(.3f, 0f);                    // timeScale 0
        BoardRoll.Advance(0f, Dt);                     // (same frame)
        Check("a zero-dt frame rolls nothing", BoardRoll.Distance == before && BoardRoll.FrameStep == 0f);
        frame++;
        BoardRoll.Advance(.3f, Dt);
        Check("on resume it carries on from exactly where it stopped (no jump)", System.Math.Abs(BoardRoll.Distance - before - a) < 1e-6);
    }

    // ---- 2 -------------------------------------------------------------------

    static void OffsetIsBoundedContinuousAndInPhase()
    {
        bool bounded = true, inPhase = true, continuous = true;
        float worstJump = 0f;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Stage(w);
            var r = walls[0].GetComponent<Renderer>();
            float tiles = Mathf.Abs(r.sharedMaterial.mainTextureScale.y), height = r.bounds.size.y;
            float tilePx = height / tiles * ppu;
            // a minute in, and an hour in (~ 47,000 u), at the fastest the board
            // ever rolls: a full limit break over the one cap (SpeedRamp: HUD
            // 35 + the boost's 10; the per-world caps 38 / 40 / 42 / 44 are gone)
            foreach (double start in new[] { 40.0, 47000.0 })
            {
                float speed = SpeedRamp.Cap + SpeedRamp.MaxBoost, step = speed * 30f * Dt;
                BoardRoll.SetDistance(start);
                float last = OffsetOf(walls[0]);
                for (int i = 1; i <= 300; i++)
                {
                    BoardRoll.SetDistance(start + i * (double)step);
                    float left = OffsetOf(walls[0]), right = OffsetOf(walls[1]);
                    bounded &= left >= 0f && left < 1f;
                    inPhase &= left == right;
                    // the art moved down by a whole number of pixels close to the true step
                    float moved = Mathf.Repeat(left - last, 1f) * tilePx;
                    float err = Mathf.Abs(moved - step * ppu);
                    worstJump = Mathf.Max(worstJump, err);
                    // (a whole number of pixels, so up to a pixel either side of an uneven true step)
                    continuous &= err < 1.01f && Mathf.Abs(moved - Mathf.Round(moved)) < .03f;
                    last = left;
                }
            }
        }
        Check("the rail art's offset stays in [0, 1) and is the same number on both walls, in every world, a minute and an hour into a run", bounded && inPhase);
        Check("... and never jumps: each frame it moves a whole number of pixels within one pixel of the board's step, even after an hour (worst " + worstJump.ToString("F2") + " px)", continuous);
    }

    // ---- 3 -------------------------------------------------------------------

    static void WholePixelSteps()
    {
        Fresh();
        bool whole = true, small = true;
        float worstLift = 0f;
        double last = BoardRoll.Snapped;
        // up to the cap (35) and a full limit break past it (45)
        foreach (int hud in new[] { 5, 10, 20, 30, 35, 40, 45 })
            for (int i = 0; i < 240; i++)
            {
                frame++;
                BoardRoll.Advance(hud / 100f, Dt * (i % 7 == 0 ? 1.3f : 1f));   // (an uneven frame now and then)
                double px = (BoardRoll.Snapped - last) * ppu;
                whole &= System.Math.Abs(px - System.Math.Round(px)) < 1e-3;
                float lift = Mathf.Abs(BoardRoll.SnapLift) * ppu;
                worstLift = Mathf.Max(worstLift, lift);
                small &= lift <= .5f + 1e-3f;
                last = BoardRoll.Snapped;
            }
        Check("at HUD 5 to 45 (the cap and a limit break) the drawn roll advances a whole number of screen pixels every frame", whole);
        Check("... and what is drawn is never more than half a pixel from the true roll (worst " + worstLift.ToString("F2") + " px)", small);
    }

    // ---- 4 -------------------------------------------------------------------

    static void MinesStayRegistered()
    {
        string bad = "";
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
            foreach (bool right in new[] { false, true })
            {
                Stage(w);
                Fresh();
                BoardRoll.SetDistance(500.25);
                float x = enmiesOnBoard.WorldRailX(!right), y0 = CameraFit.ViewTop + 1f;
                var rail = new GameObject("RailMineLane");
                rail.transform.position = new Vector3(x, y0, 0f);
                var def = EnemyRoster.One(w, EnemyRole.Mine);
                var mine = EnemyFactory.Create(def, new Vector3(x, y0, 0f), Quaternion.identity);
                var mount = mine.AddComponent<RailMineMount>();
                mount.MountTo(rail.transform);
                var wallR = walls[right ? 1 : 0].GetComponent<Renderer>();
                float tiles = Mathf.Abs(wallR.sharedMaterial.mainTextureScale.y), height = wallR.bounds.size.y, tilePx = height / tiles * ppu;
                double artStart = BoardRoll.Snapped;
                float mineStart = float.NaN, worst = 0f, worstStep = 0f, lastMinePx = 0f, lastOffset = OffsetOf(walls[right ? 1 : 0]);
                float extra = 0f;   // slide + shove: its own movement along the rail, on purpose
                for (int i = 0; i < 600; i++)
                {
                    frame++;
                    float speed = Mathf.Lerp(.05f, SpeedRamp.Cap, i / 600f);   // the ramp to the cap, and a blue atom's +0.05 past it (a limit break)
                    if (i > 300 && i < 360) speed += .05f;
                    float step = BoardRoll.Advance(speed, Dt);                                   // what moveBackGround does ...
                    rail.transform.position += Vector3.down * BoardRoll.Advance(speed, Dt);      // ... and RailLaneScroller, same frame
                    if (i == 200) { mount.Slide = .37f; extra = mount.Slide + mount.Shove; }
                    if (i == 400) { mount.Shove = -.21f; extra = mount.Slide + mount.Shove; }
                    mount.SendMessage("LateUpdate");
                    float offset = OffsetOf(walls[right ? 1 : 0]);
                    // the mine's drawn position against the art's drawn roll, in pixels: constant unless it slides itself
                    float minePx = (y0 - (mine.transform.position.y - extra)) * ppu;
                    float artPx = (float)((BoardRoll.Snapped - artStart) * ppu);
                    if (float.IsNaN(mineStart)) mineStart = minePx - artPx;
                    worst = Mathf.Max(worst, Mathf.Abs(minePx - artPx - mineStart));
                    // and the art's own offset moved by the same whole pixels the mine did
                    float artMoved = Mathf.Repeat(offset - lastOffset, 1f) * tilePx;
                    if (i > 0 && i != 200 && i != 400) worstStep = Mathf.Max(worstStep, Mathf.Abs(artMoved - (minePx - lastMinePx)));
                    lastOffset = offset;
                    lastMinePx = minePx;
                    if (Mathf.Abs(mine.transform.position.x - x) > 1e-4f) worst = 99f;
                }
                if (worst > .05f || worstStep > .05f) bad += " " + def.key + (right ? "/right" : "/left") + "(" + worst.ToString("F3") + " px, step " + worstStep.ToString("F3") + " px)";
                Object.DestroyImmediate(mine);
                Object.DestroyImmediate(rail);
            }
        Check("every world's mine, on either wall, stays on the same pixels of the rail art under it for 600 frames of ramping speed and a speed boost, " +
              "to within 0.05 px, moving by exactly the art's whole-pixel step each frame; a slide or a shove moves it along the rail and nothing else" + bad,
              bad.Length == 0);
    }

    // ---- 5 -------------------------------------------------------------------

    static Color32[] Render(RenderTexture rt, Texture2D tex)
    {
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        return tex.GetPixels32();
    }

    // Mean abs difference over the left rail's strip between frame b and frame a moved down `shift` pixels.
    static float Residual(Color32[] a, Color32[] b, int shift)
    {
        long sum = 0;
        int n = 0;
        for (int y = 200; y < H - 200; y += 2)
            for (int x = 24; x < 140; x += 2)
            {
                var p = a[(y + shift) * W + x];
                var q = b[y * W + x];
                sum += Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
                n += 3;
            }
        return sum / (float)n;
    }

    static void RenderedRigidly()
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        // nothing but the rails: the backdrop animates on its own clock
        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && r.gameObject != walls[0] && r.gameObject != walls[1]) { r.enabled = false; hidden.Add(r); }
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in canvases) c.enabled = false;
        try
        {
            bool rigid = true, crawls = false;
            string detail = "";
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                Stage(w);
                float step = .2f * 30f * Dt;              // HUD 20: 14.52 px a frame
                var res = new float[2];
                for (int mode = 0; mode < 2; mode++)
                {
                    BoardRoll.PixelSnap = mode == 1;
                    float worst = 0f;
                    Color32[] prev = null;
                    double prevDrawn = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        BoardRoll.SetDistance(321.4 + i * (double)step);
                        OffsetOf(walls[0]);
                        OffsetOf(walls[1]);
                        var px = (Color32[])Render(rt, tex).Clone();
                        if (prev != null)
                        {
                            int shift = (int)System.Math.Round((BoardRoll.Snapped - prevDrawn) * ppu);
                            worst = Mathf.Max(worst, Residual(prev, px, -shift) < Residual(prev, px, shift) ? Residual(prev, px, -shift) : Residual(prev, px, shift));
                        }
                        prev = px;
                        prevDrawn = BoardRoll.Snapped;
                    }
                    res[mode] = worst;
                }
                BoardRoll.PixelSnap = true;
                detail += " " + WorldManager.Worlds[w].displayName + " " + res[0].ToString("F1") + " -> " + res[1].ToString("F2");
                rigid &= res[1] < 1.5f;
                crawls |= res[0] > res[1] * 2f + 1f;
            }
            Check("rendered at 1080x2520, HUD 20: on whole pixels the rail strip, tile seam and crossfade included, is the same image moved down " +
                  "from one frame to the next (mean difference under 1.5 of 255 in every world; unsnapped -> snapped:" + detail + ")", rigid);
            Check("... and unsnapped the texels crawl (the reason for the snap)", crawls);
        }
        finally
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            foreach (var c in canvases) if (c != null) c.enabled = true;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }

    // ---- 6 -------------------------------------------------------------------

    static void WorldChange()
    {
        Fresh();
        BoardRoll.SetDistance(888.8);
        bool ok = true;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Stage(w);   // a portal: new texture, new tile height
            float left = OffsetOf(walls[0]), right = OffsetOf(walls[1]);
            ok &= left == right && left >= 0f && left < 1f;
            double before = BoardRoll.Distance;
            frame++;
            BoardRoll.Advance(.2f, Dt);
            ok &= System.Math.Abs(BoardRoll.Distance - before - .2 * 30 * Dt) < 1e-6;
        }
        Check("through a world change (new rail texture, new tile height) both walls stay in phase with a valid offset and the roll carries on", ok);
    }

    // ---- 7 -------------------------------------------------------------------

    static void OtherModesReachable()
    {
        Stage(0);
        var r = walls[0].GetComponent<Renderer>();
        float tiles = Mathf.Abs(r.sharedMaterial.mainTextureScale.y), height = r.bounds.size.y, tilePx = height / tiles * ppu;
        Fresh();
        BoardRoll.PixelSnap = false;
        BoardRoll.SetDistance(10.0);
        float a = OffsetOf(walls[0]);
        BoardRoll.SetDistance(10.0 + .123);
        float moved = Mathf.Repeat(OffsetOf(walls[0]) - a, 1f) * tilePx;
        Check("PixelSnap = false draws the true roll (" + moved.ToString("F2") + " px for a 17.86 px step)", Mathf.Abs(moved - .123f * ppu) < .02f);
        BoardRoll.RailRate = .25f;
        BoardRoll.SetDistance(20.0);
        a = OffsetOf(walls[0]);
        BoardRoll.SetDistance(20.0 + 1.0);
        moved = Mathf.Repeat(OffsetOf(walls[0]) - a, 1f) * tilePx;
        Check("RailRate = 0.25 is a slower, parallax rail (a quarter of the board's step)", Mathf.Abs(moved - .25f * ppu) < .05f);
        BoardRoll.RailRate = 1f;
        BoardRoll.LegacyTileRate = true;
        BoardRoll.SetDistance(30.0);
        a = OffsetOf(walls[0]);
        BoardRoll.SetDistance(30.0 + 3.0);   // a tenth of a second at speed 1: a tenth of a tile, the pre-vetting rate
        Check("LegacyTileRate is the old scroll: one texture tile per unit of speed", Mathf.Abs(Mathf.Repeat(OffsetOf(walls[0]) - a, 1f) - .1f) < 1e-4f);
        Fresh();
    }

    static void SourceWiring()
    {
        string walls = File.ReadAllText("Assets/Scripts/Gameplay/moveBackGround.cs");
        string board = File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs");
        Check("the walls and the rail lanes roll by the one BoardRoll step, and the mount draws the mine on the art's pixel",
              walls.Contains("BoardRoll.Advance(speed, Time.deltaTime)") && walls.Contains("BoardRoll.RailOffset(") &&
              board.Contains("Vector3.down * BoardRoll.Advance(moveBackGround.speed, Time.deltaTime)") && board.Contains("+ BoardRoll.SnapLift"));
        Check("the rail art keeps rolling in the ultimate's slow motion, when the lanes do",
              walls.Contains("Time.timeScale = ShipPowerController.CinematicTimeScale;") && walls.IndexOf("moveBackground();") < walls.IndexOf("if (TouchInput.IsPressed"));
    }
}
