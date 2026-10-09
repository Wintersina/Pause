using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The roster against the reinforced rails and the wider, taller camera
// (CameraFit.GameplayHalfWidth 3.72; docs/enemy-behaviours.md "Rails and
// view").
//
//   RAIL MINES  ride the DRAWN rail in every world, on both walls, at both
//               phone shapes: the clamp bites the rail art, the body stays
//               within the ship's reach, slide and shove keep it on the rail
//               line, its shots start at the mine and cross the lane, the
//               atlas cells are whole and the clamp is where the code says
//   SCROLL      the rail art falls at the board's rate, so a mine does not
//               slide along its rail's art
//   RAIL EDGE   BossRails.InnerEdge is the drawn rail from the moment the
//               rails are painted, not only after a boss intro
//   COLLIDERS   the rail quads' 3D mesh colliders touch nothing: no 2D
//               collider on the walls, no 3D physics query in the game
//   VIEW        pilots hold the same place on screen, stay as long and their
//               shots take as long to arrive in a 10 u, 13.2 u and 17.4 u
//               view; the threat ceiling follows the view
//   CALM        nothing spawns for 8 s on every world arrival (not only the
//               scene's first), unless the ship arrives fast; no burst after
//   ELITES      a pilot is not stationed over an elite when there is room
public static class RailsVettingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RAILS] PASS  " : "[RAILS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static readonly Vector2Int[] Shapes = { new Vector2Int(1080, 2520), new Vector2Int(1080, 1920) };
    public const float ShipClamp = 2.4f;   // movePlayer's x clamp

    static Camera cam;
    static GameObject[] walls;
    static float ortho0, aspect0;

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
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            startMenu.youAreInTutorial = false;
            EnemyThreat.ForceShooting = true;
            MineArt();
            MinesRideTheDrawnRail();
            MineMotionAndShots();
            MinesOutliveTheirLane();
            RailArtScrollsWithTheBoard();
            RailEdgeFromTheStart();
            RailQuadCollidersTouchNothing();
            PilotsFollowTheView();
            CalmArrivalInEveryWorld();
            PilotsAndElitesShareTheAir();
        }
        finally
        {
            if (cam != null) { cam.orthographicSize = ortho0; cam.aspect = aspect0; }
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            PilotAirspace.Clear();
            SetWorldManager(null);
            BossRails.Reset();
            LoopDifficulty.Reset();
            moveBackGround.speed = 0f;
            Clear();
        }
        Debug.Log("[RAILS] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static void Clear()
    {
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        foreach (var w in Object.FindObjectsByType<WorldManager>(FindObjectsSortMode.None)) Object.DestroyImmediate(w.gameObject);
        EliteSystem.Clear();
        PilotAirspace.Clear();
        EnemyThreat.Reset();
    }

    // The scene as a `shape` phone shows `world`: camera, rails painted and fitted.
    static void Stage(int world, Vector2Int shape)
    {
        cam.aspect = shape.x / (float)shape.y;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, shape.x, shape.y);
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

    static string W(int w) { return WorldManager.Worlds[w].displayName; }

    // ---- 1: the atlas --------------------------------------------------------

    static void MineArt()
    {
        var path = "Assets/Art/Resources/" + RailMineArt.AtlasPath + ".png";
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32();
        int size = tex.width;
        bool whole = true, reachOk = true;
        float minReach = 9f, maxReach = 0f;
        var red = new float[RailMineArt.Worlds];
        for (int w = 0; w < RailMineArt.Worlds; w++)
            for (int c = 0; c < RailMineArt.Columns; c++)
            {
                var r = RailMineArt.PixelRect(w, c);
                Vector2 pivot = RailMineArt.PixelPivot(w, c);
                int left = int.MaxValue, opaque = 0, reddish = 0, onBorder = 0;
                for (int y = r.y; y < r.y + r.height; y++)
                    for (int x = r.x; x < r.x + r.width; x++)
                    {
                        var p = px[(size - 1 - y) * size + x];   // top-left origin
                        if (p.a <= 40) continue;
                        opaque++;
                        left = Mathf.Min(left, x);
                        if (x == r.x || x == r.x + r.width - 1 || y == r.y || y == r.y + r.height - 1) onBorder++;
                        if (HostileGlow.IsPlayerRed(new Color32(p.r, p.g, p.b, 255))) reddish++;
                    }
                whole &= opaque > 20000 && onBorder == 0;
                float reach = (pivot.x - left) / RailMineArt.PixelsPerUnit;
                minReach = Mathf.Min(minReach, reach);
                maxReach = Mathf.Max(maxReach, reach);
                reachOk &= Mathf.Abs(reach - RailMineArt.ClampReach) <= .03f;
                if (c == RailMineArt.Dormant) red[w] = reddish / (float)opaque;
            }
        Object.DestroyImmediate(tex);
        Check("all 16 mine cells (4 worlds x dormant / waking / charging / burst) are whole: nothing cut at a cell's edge", whole);
        Check("in every cell the clamp's outer face is RailMineArt.ClampReach (" + RailMineArt.ClampReach + " u) from the pivot (" +
              minReach.ToString("F3") + " to " + maxReach.ToString("F3") + " u)", reachOk);
        Debug.Log(string.Format("[RAILS] mine art in the player's red band (HostileGlow.IsPlayerRed), dormant cell: Space {0:P0}, Frost {1:P0}, Verdant {2:P0}, Ember {3:P0}",
                                red[0], red[1], red[2], red[3]));
        Check("the Space and Frost mines carry none of the player's red", red[0] < .01f && red[1] < .01f);
        // KNOWN ART GAP (reported, not fixed here: the atlas is approved art):
        // the Ember mine's lava is orange-red, 42% of it inside the red band,
        // and the Verdant mine's magenta thorns touch it (14%).
        Check("(art gap, tracked) the Ember mine's lava sits in the player's red band: " + red[3].ToString("P0") + "; Verdant's thorns: " +
              red[2].ToString("P0"), red[3] > .2f && red[2] < .25f);
    }

    // ---- 2: mount ------------------------------------------------------------

    static void MinesRideTheDrawnRail()
    {
        var bad = new List<string>();
        float colliderHalf = EnemyRoster.ColliderSize(EnemyRole.Mine).x * .5f;
        float lowest = 9f, highest = 0f;
        foreach (var shape in Shapes)
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                Stage(w, shape);
                float halfW = cam.orthographicSize * cam.aspect;
                float inL, outL, inR, outR;
                bool art = WorldPainter.VisibleRailEdges(walls[0], out inL, out outL) & WorldPainter.VisibleRailEdges(walls[1], out inR, out outR);
                float left = enmiesOnBoard.WorldRailX(true), right = enmiesOnBoard.WorldRailX(false);
                float clampFace = right + RailMineArt.ClampReach;
                lowest = Mathf.Min(lowest, right);
                highest = Mathf.Max(highest, right);
                bool ok = art && Mathf.Abs(inL - inR) < .01f && Mathf.Approximately(left, -right) &&
                          // the clamp's outer face is inside the drawn rail, by ClampBite: it grips it
                          Mathf.Abs(clampFace - (inR + RailMineArt.ClampBite)) < .005f && clampFace < outR &&
                          // the sphere itself hangs in the lane, clear of the rail art
                          right + .2f < inR + .12f &&
                          // the whole drawing is on screen
                          clampFace < halfW &&
                          // still a hazard the ship can touch: at its clamp the ship's centre is inside the mine's box
                          right - colliderHalf < ShipClamp && right - colliderHalf < SpawnLane.LaneHalf;
                if (!ok) bad.Add(shape.y + " " + W(w) + "(rail " + inR.ToString("F3") + ", mine " + right.ToString("F3") + ")");
            }
        Check("in all four worlds at 1080x2520 and 1080x1920 a rail mine's clamp sits " + RailMineArt.ClampBite + " u inside the drawn rail, on both " +
              "walls alike, wholly on screen, its body within the ship's reach (mine centre " + lowest.ToString("F3") + " to " + highest.ToString("F3") +
              " u; " + string.Join(", ", bad) + ")", bad.Count == 0);
        Check("that is further out than the old lane-edge mount (" + RailMineArt.FallbackRailX + "), by under 0.15 u",
              lowest > RailMineArt.FallbackRailX && highest < RailMineArt.FallbackRailX + .15f);

        // the real spawner mounts them there, on a rail of the right wall
        Stage(1, Shapes[0]);
        Clear();
        var board = new GameObject("~RailsBoard").AddComponent<enmiesOnBoard>();
        board.transform.position = new Vector3(0f, CameraFit.ViewTop + 2f, 0f);
        board.SendMessage("Start");
        Random.InitState(5);
        var spawnMine = typeof(enmiesOnBoard).GetMethod("spawnMine", Inst);
        int mines = 0, onRail = 0;
        for (int i = 0; i < 12; i++)
        {
            spawnMine.Invoke(board, null);
            foreach (var mount in Object.FindObjectsByType<RailMineMount>(FindObjectsSortMode.None))
            {
                mines++;
                float x = mount.transform.position.x;
                if (Mathf.Approximately(Mathf.Abs(x), enmiesOnBoard.WorldRailX(false)) && mount.IsOnRail() &&
                    mount.GetComponent<SpriteRenderer>().flipX == (x > 0f)) onRail++;
                Object.DestroyImmediate(mount.gameObject);
            }
        }
        Check("the spawner clamps every mine there, mirrored on the right wall (" + onRail + " of " + mines + ")", mines >= 8 && onRail == mines);
        Clear();

        // no rail art (a bare wall, headless tests): the authored lane edge
        BossRails.Reset();
        var bare = new GameObject("~NoWalls");
        foreach (var wall in walls) wall.SetActive(false);
        Check("without the walls a mine keeps the authored lane edge (" + RailMineArt.FallbackRailX + ")",
              Mathf.Approximately(enmiesOnBoard.WorldRailX(false), RailMineArt.FallbackRailX));
        foreach (var wall in walls) wall.SetActive(true);
        Object.DestroyImmediate(bare);
    }

    // ---- 3: motion, shots -----------------------------------------------------

    static void MineMotionAndShots()
    {
        var bad = new List<string>();
        var ship = new GameObject("~RailsShip").transform;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Stage(w, Shapes[0]);
            var def = EnemyRoster.One(w, EnemyRole.Mine);
            var b = def.Behaviour;
            foreach (bool rightSide in new[] { false, true })
            {
                Clear();
                float x = enmiesOnBoard.WorldRailX(!rightSide);
                var rail = new GameObject("RailMineLane");
                rail.transform.position = new Vector3(x, 1f, 0f);
                rail.AddComponent<RailLaneScroller>();
                var go = EnemyFactory.Create(def, new Vector3(x, 1f, 0f), Quaternion.identity);
                var mount = go.AddComponent<RailMineMount>();
                mount.MountTo(rail.transform);
                var brain = go.GetComponent<EnemyBrain>();
                mount.brain = brain;
                brain.TargetOverride = ship;
                ship.position = new Vector3(0f, 0f, 0f);
                float worstX = 0f, worstSlide = 0f, clock = 300f;
                bool shotOk = true, sawShot = false;
                int launched = EliteSystem.Shots.Launched;
                for (int i = 0; i < 60 * 12; i++)
                {
                    clock += Dt;
                    SpawnSpace.ClockOverride = clock;
                    rail.transform.position += Vector3.down * .4f * Dt;
                    if (rail.transform.position.y < -1f) rail.transform.position += Vector3.up * 2f;
                    if (i == 200) mount.Shove = .5f;          // a shockwave slides it along the rail
                    if (i == 400) mount.Shove = -.5f;
                    brain.Step(Dt);
                    mount.SendMessage("LateUpdate");
                    EliteSystem.Step(Dt);
                    worstX = Mathf.Max(worstX, Mathf.Abs(go.transform.position.x - x));
                    worstSlide = Mathf.Max(worstSlide, Mathf.Max(mount.Slide - b.Up, -mount.Slide - b.Down));
                    var laser = brain.Laser;
                    if (laser != null && laser.State == RailMineLaser.Phase.Beam && !sawShot)
                    {
                        // a laser mine: the beam leaves the mine's core at its row and crosses the lane to the far rail's face
                        sawShot = true;
                        shotOk &= Mathf.Abs(laser.Y - go.transform.position.y) < .05f &&
                                  (laser.From - RailMineLaser.MuzzleOf(go.transform, w)).magnitude < .02f &&
                                  Mathf.Abs(laser.From.x) < Mathf.Abs(x) && Mathf.Abs(x) - Mathf.Abs(laser.From.x) < .2f &&
                                  Mathf.Abs(Mathf.Abs(laser.To.x) - BossRails.DrawnInnerEdge) < .01f &&
                                  Mathf.Sign(laser.From.x) == Mathf.Sign(x) && Mathf.Sign(laser.To.x) == -Mathf.Sign(x);
                    }
                    if (EliteSystem.Shots.Launched > launched && !sawShot)
                    {
                        sawShot = true;
                        foreach (var s in EliteSystem.Shots.All)
                        {
                            if (!s.Active || !s.RosterShot) continue;
                            // it starts at the mine, on the lane's side of it, inside the rail, heading across
                            Vector2 at = s.LaunchedAt;
                            shotOk &= Mathf.Abs(at.y - go.transform.position.y) < .05f && Mathf.Abs(at.x) < Mathf.Abs(x) &&
                                      Mathf.Abs(at.x) + s.Radius < BossRails.InnerEdge && Mathf.Sign(s.Velocity.x) == -Mathf.Sign(x) &&
                                      Mathf.Abs(Mathf.Abs(x) - Mathf.Abs(at.x) - b.muzzle) < .01f;
                        }
                    }
                }
                bool ok = worstX < .001f && worstSlide < .001f && (!b.Shoots || (sawShot && shotOk)) && (b.Shoots || !sawShot) &&
                          Mathf.Abs(go.transform.position.y - (rail.transform.position.y + mount.Slide + mount.Shove)) < .001f;
                if (!ok) bad.Add(def.key + (rightSide ? " right" : " left") + "(off rail " + worstX.ToString("F3") + ", slide over " + worstSlide.ToString("F3") +
                                 ", shot " + sawShot + "/" + shotOk + ")");
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(rail);
            }
        }
        Object.DestroyImmediate(ship.gameObject);
        Check("on the drawn rail's line each world's mine never leaves it through 12 s of sliding, a shove either way and its rail scrolling; " +
              "its slide stays in its envelope and adds to the shove; the shooters' shots start at the mine, inside the rail, and head across " +
              "the lane (" + string.Join(", ", bad) + ")", bad.Count == 0);

        // the blast: how much of the lane a bursting mine takes with it
        float reach = enmiesOnBoard.WorldRailX(false) - FriendlyFire.MineBlastRadius;
        Check("a mine's blast (" + FriendlyFire.MineBlastRadius + " u) reaches in to x " + reach.ToString("F2") + ": about a fifth of the lane on its side, never the middle",
              reach > 1.2f && reach < 1.6f);
    }

    // ---- 3b: a mine is never taken by its lane --------------------------------
    //
    // "Sometimes rail mines disappear when you get close to them." The spawner
    // mounts every mine on the NEAREST live lane on its side (one lane per
    // side while it lives), which is usually one spawned seconds earlier and
    // already far down the board; the lane removed itself at y -12 and a mine
    // whose lane is gone destroys itself (RailMineMount: no detached mines).
    // So a mine mounted 10 u above an old lane vanished, without a blast, as
    // it came down past y -2 -- right where the ship flies. The lane now lives
    // while any mine rides it (the mines leave by the Destroyer under the view).
    static void MinesOutliveTheirLane()
    {
        Stage(0, Shapes[1]);
        Clear();
        moveBackGround.speed = 0f;
        var def = EnemyRoster.One(0, EnemyRole.Mine);
        var bad = new List<string>();
        foreach (bool rightSide in new[] { false, true })
        {
            float x = enmiesOnBoard.WorldRailX(!rightSide);
            var lane = new GameObject("RailMineLane");
            lane.transform.position = new Vector3(x, -9f, 0f);
            var scroller = lane.AddComponent<RailLaneScroller>();
            // a mine spawned at the top long after its lane, mounted on it (the nearest live one)
            var go = EnemyFactory.Create(def, new Vector3(x, 1f, 0f), Quaternion.identity);
            var mount = go.AddComponent<RailMineMount>();
            mount.MountTo(lane.transform);
            // the lane passes its old end (-12) with the mine still on screen, 1.2 u under the ship's row
            lane.transform.position = new Vector3(x, -12.2f, 0f);
            bool triedToGo = false;
            Application.LogCallback cb = (m, st, t) => { if (m.Contains("Destroy may not be called from edit mode")) triedToGo = true; };
            Application.logMessageReceived += cb;
            try
            {
                TestHarness.Send(scroller, "Update");
                if (lane != null) TestHarness.Send(mount, "LateUpdate");
            }
            finally { Application.logMessageReceived -= cb; }
            string side = rightSide ? "right" : "left";
            if (triedToGo || lane == null) bad.Add(side + ": the lane went with a mine on it");
            if (go == null || !mount.IsOnRail() || Mathf.Abs(go.transform.position.y - (-2.2f)) > .01f)
                bad.Add(side + ": the mine at y -2.2 went with its lane (" + (go == null ? "gone" : go.transform.position.y.ToString("F2")) + ")");
            // its last mine gone, the lane goes
            if (go != null) Object.DestroyImmediate(go);
            if (lane != null) TestHarness.Send(scroller, "Update");
            if (lane != null) { bad.Add(side + ": an empty lane past the board's end stays"); Object.DestroyImmediate(lane); }
        }
        Clear();
        Check("a lane lives while a mine rides it: one mounted 10 u above its lane stays on the board (and on its rail) as the lane passes " +
              "y -12 and the empty lane then goes, on both rails (" +
              (bad.Count == 0 ? "all" : string.Join("; ", bad)) + ")", bad.Count == 0);
    }

    // ---- 4: scroll -------------------------------------------------------------

    static void RailArtScrollsWithTheBoard()
    {
        bool matches = true, wasSlow = true;
        string detail = "";
        foreach (var shape in Shapes)
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                Stage(w, shape);
                foreach (var wall in walls)
                {
                    var r = wall.GetComponent<Renderer>();
                    float tiles = Mathf.Abs(r.sharedMaterial.mainTextureScale.y), height = r.bounds.size.y;
                    float tileWorld = height / tiles;
                    const float speed = .3f;
                    float artSpeed = moveBackGround.RailTilesPerSecond(speed, tiles, height) * tileWorld;   // world u/s the art falls at
                    matches &= Mathf.Abs(artSpeed - speed * 30f) < .001f;
                    wasSlow &= speed * tileWorld < speed * 30f * .35f;                                      // what it did before
                    if (wall == walls[0] && shape == Shapes[0]) detail += " " + W(w) + " " + tileWorld.ToString("F1") + " u";
                }
            }
        Check("the rail art falls at the board's rate (speed x 30 u/s, SpawnSpace.ScrollSpeed) in every world and view, so a clamped mine does " +
              "not slide along its rail's art", matches && Mathf.Approximately(moveBackGround.BoardScroll * .3f, .3f * 30f));
        Check("... it used to fall at speed x one tile a second, a tile being" + detail + ": under 35% of the board's rate", wasSlow);
        moveBackGround.RailRidesBoard = false;
        Check("RailRidesBoard = false is the old texture-rate scroll", Mathf.Approximately(moveBackGround.RailTilesPerSecond(.3f, 3f, 18f), .3f));
        moveBackGround.RailRidesBoard = true;
        Check("moveBackGround scrolls its wall by that rate",
              File.ReadAllText("Assets/Scripts/Gameplay/moveBackGround.cs").Contains("BoardRoll.RailOffset(Mathf.Abs(wall.mainTextureScale.y), renderer.bounds.size.y)"));
    }

    // ---- 5: rail edge ----------------------------------------------------------

    static void RailEdgeFromTheStart()
    {
        bool ok = true;
        float seen = 0f;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            BossRails.Reset();
            Stage(w, Shapes[0]);   // WorldPainter.Apply: what WorldManager.Start and a portal do
            float inner, outer;
            WorldPainter.VisibleRailEdges(walls[0], out inner, out outer);
            seen = BossRails.InnerEdge;
            ok &= Mathf.Abs(BossRails.InnerEdge - inner) < .005f && Mathf.Abs(EliteSystem.RailEdge - inner) < .005f &&
                  BossRails.InnerEdge > ShipClamp && BossRails.InnerEdge > BossRails.AuthoredInnerEdge;
        }
        Check("painting a world's rails makes BossRails.InnerEdge (elites' RailEdge, where shots break) the drawn rail at once (" + seen.ToString("F3") +
              "), not the authored " + BossRails.AuthoredInnerEdge + " until the first boss intro", ok);
    }

    // ---- 6: colliders ------------------------------------------------------------

    static void RailQuadCollidersTouchNothing()
    {
        bool no2D = true, has3D = false;
        foreach (var wall in walls)
        {
            no2D &= wall.GetComponent<Collider2D>() == null && wall.GetComponent<Rigidbody2D>() == null;
            has3D |= wall.GetComponent<Collider>() != null;
        }
        Check("the rail quads carry no 2D collider or body (the game's physics is 2D): gameplay never touches them" +
              (has3D ? " (their 3D MeshCollider is inert)" : ""), no2D);
        var users = new List<string>();
        foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            if (System.Text.RegularExpressions.Regex.IsMatch(src, @"\bPhysics\.(Raycast|Linecast|SphereCast|BoxCast|CapsuleCast|Overlap|Check)") ||
                System.Text.RegularExpressions.Regex.IsMatch(src, @"\bOnCollisionEnter\s*\(|\bOnTriggerEnter\s*\("))
                users.Add(Path.GetFileName(file));
        }
        Check("no script queries or receives 3D physics, so the padded quads reaching into the lane (to about 1.87 u) block nothing (" +
              string.Join(", ", users) + ")", users.Count == 0);
    }

    // ---- 7: view ---------------------------------------------------------------

    struct Flight { public float stationFraction, inView, shotSeconds, shotSpeed; public bool gone, shotArrived; }

    static Flight FlyWarden(float ortho)
    {
        Clear();
        cam.orthographicSize = ortho;
        moveBackGround.speed = .3f;
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        var ship = new GameObject("~RailsShip").transform;
        ship.position = new Vector3(.3f, bottom + (top - bottom) * .2f, 0f);   // the ship a fifth of the way up the screen
        Random.InitState(77);
        var def = EnemyRoster.Find("space_fighter_4");
        var go = EnemyFactory.Create(def, new Vector3(0f, top + PilotAirspace.WaitAbove, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        brain.TargetOverride = ship;
        var f = new Flight();
        float clock = 700f, firedAt = -1f;
        EliteShot shot = null;
        for (int i = 0; i < 60 * 40 && brain.Stage != EnemyBrain.PilotStage.Gone; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            EliteSystem.Step(Dt);
            if (brain.Stage == EnemyBrain.PilotStage.Gone) break;
            float y = go.transform.position.y;
            if (y <= top && y >= bottom) f.inView += Dt;
            if (brain.Stage == EnemyBrain.PilotStage.Engaging && f.stationFraction == 0f && brain.EngagedSeconds > .5f)
                f.stationFraction = (top - brain.Anchor.y) / (top - bottom);
            if (shot == null && brain.ShotsFired > 0)
            {
                foreach (var s in EliteSystem.Shots.All) if (s.Active && s.RosterShot) shot = s;
                if (shot != null) { firedAt = clock; f.shotSpeed = shot.Velocity.magnitude; }
            }
            if (shot != null && !f.shotArrived && shot.Active && shot.transform.position.y <= ship.position.y)
            {
                f.shotArrived = true;
                f.shotSeconds = clock - firedAt;
            }
        }
        f.gone = brain.Stage == EnemyBrain.PilotStage.Gone;
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(ship.gameObject);
        return f;
    }

    static void PilotsFollowTheView()
    {
        cam.aspect = Shapes[0].x / (float)Shapes[0].y;
        float tall = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2520), mid = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 1920);
        // (the authored station: HostileReach pulls a pilot down into the ship's
        // reach, which follows the ship, not the camera -- HostileReachTest flies
        // that on every aspect; here only the view's scaling is vetted)
        Flight a, b, c;
        HostileReach.Enabled = false;
        try { a = FlyWarden(5f); b = FlyWarden(mid); c = FlyWarden(tall); }
        finally { HostileReach.Enabled = true; }
        Debug.Log(string.Format("[RAILS] Warden in a 10 / {0:F1} / {1:F1} u view: station {2:P0} / {3:P0} / {4:P0} down the screen; in view {5:F1} / {6:F1} / {7:F1} s; " +
                                "shell {8:F1} / {9:F1} / {10:F1} u/s, reaching the ship's row in {11:F2} / {12:F2} / {13:F2} s",
                                2f * mid, 2f * tall, a.stationFraction, b.stationFraction, c.stationFraction, a.inView, b.inView, c.inView,
                                a.shotSpeed, b.shotSpeed, c.shotSpeed, a.shotSeconds, b.shotSeconds, c.shotSeconds));
        Check("a pilot holds the same place on screen whatever the view's height (its station " + a.stationFraction.ToString("P0") + " of the way down)",
              a.gone && b.gone && c.gone && Mathf.Abs(a.stationFraction - b.stationFraction) < .01f && Mathf.Abs(a.stationFraction - c.stationFraction) < .01f &&
              a.stationFraction > .1f && a.stationFraction < .25f);
        Check("... stays as long (" + a.inView.ToString("F1") + " / " + b.inView.ToString("F1") + " / " + c.inView.ToString("F1") + " s in view)",
              Mathf.Abs(a.inView - b.inView) < .4f && Mathf.Abs(a.inView - c.inView) < .4f);
        Check("... and its shot takes as long to reach the ship (" + a.shotSeconds.ToString("F2") + " / " + b.shotSeconds.ToString("F2") + " / " +
              c.shotSeconds.ToString("F2") + " s): it arrives, at a speed scaled with the view",
              a.shotArrived && b.shotArrived && c.shotArrived && Mathf.Abs(a.shotSeconds - c.shotSeconds) < .25f &&
              Mathf.Abs(c.shotSpeed / a.shotSpeed - tall / 5f) < .02f && a.shotSeconds > .8f);

        cam.orthographicSize = 5f;
        float ceiling = EnemyDensity.MaxThreats(20f);
        cam.orthographicSize = tall;
        float ceilingTall = EnemyDensity.MaxThreats(20f);
        Check("the threat ceiling follows the board the view shows (" + ceiling.ToString("F1") + " bodies in a 10 u view, " + ceilingTall.ToString("F1") +
              " in a " + (2f * tall).ToString("F1") + " u one)", ceilingTall > ceiling * 1.4f && ceilingTall < ceiling * 1.7f &&
              Mathf.Approximately(EnemyBrain.ViewScale, tall / 5f));
        Check("nothing a pilot flies by is a fixed view height: the brain reads CameraFit.ViewTop / ViewBottom",
              !File.ReadAllText("Assets/Scripts/Gameplay/Enemies/EnemyBrain.cs").Contains("6.65") &&
              !File.ReadAllText("Assets/Scripts/Gameplay/Enemies/PilotAirspace.cs").Contains("ViewTop + 5"));
        cam.orthographicSize = ortho0;
    }

    // ---- 8: calm arrival ----------------------------------------------------------

    static void CalmArrivalInEveryWorld()
    {
        Clear();
        Stage(0, Shapes[0]);
        var wmGo = new GameObject("~RailsWorld");
        SetWorldManager(wmGo.AddComponent<WorldManager>());
        RunLoop.Reset();
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        moveBackGround.speed = 0f;
        var spawn = typeof(enmiesOnBoard).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        var calm = typeof(enmiesOnBoard).GetMethod("StepCalmArrival", Inst);
        var elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", Inst);
        var select = typeof(enmiesOnBoard).GetMethod("SelectPhase", Inst);
        try
        {
            var board = new GameObject("~RailsBoard").AddComponent<enmiesOnBoard>();
            board.transform.position = new Vector3(0f, CameraFit.ViewTop + 2f, 0f);
            board.SendMessage("Start");
            float t = 0f;
            // enmiesOnBoard.Update, with an explicit dt
            System.Action<float> fly = seconds =>
            {
                for (float until = t + seconds; t < until; t += Dt)
                {
                    elapsed.SetValue(board, t);
                    select.Invoke(board, null);
                    calm.Invoke(board, null);
                    if (!board.InCalmWindow) spawn.Invoke(board, new object[] { Dt });
                }
            };
            Random.InitState(3);
            fly(enmiesOnBoard.CalmArrivalSeconds - .1f);
            Check("a stock ship's first " + enmiesOnBoard.CalmArrivalSeconds + " s in a world: nothing spawned, no pilot admitted, no chaser",
                  board.InCalmWindow && board.SpawnedCount == 0 && PilotAirspace.Count == 0 && ChaserEnemy.Alive == 0 && board.PendingCount == 0);
            fly(.2f);
            int atEnd = board.SpawnedCount;
            fly(1f);
            int firstSecond = board.SpawnedCount;
            fly(1f);
            Check("the window ends on time and the board comes in one enemy at a time, not as a burst (" + atEnd + " at the instant, " + firstSecond +
                  " after 1 s, " + board.SpawnedCount + " after 2 s)", !board.InCalmWindow && atEnd == 0 && firstSecond <= 1 && board.SpawnedCount <= 2 && board.SpawnedCount >= 1);

            // deep into the level, timers all due; then a portal to the next world
            fly(60f);
            int before = board.SpawnedCount, windows = board.CalmWindows;
            Sweep();                                                          // (nothing scrolls in this fixture: the old world's board has gone by)
            moveBackGround.speed = 0f;                                        // WorldManager.Advance resets speed
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 1);            // ... and moves to the next world
            fly(enmiesOnBoard.CalmArrivalSeconds - .1f);
            Check("arriving in the next world through a portal is calm again: " + enmiesOnBoard.CalmArrivalSeconds + " s with nothing spawned (windows " +
                  windows + " -> " + board.CalmWindows + ")", board.CalmWindows == windows + 1 && board.InCalmWindow && board.SpawnedCount == before &&
                  board.PendingCount == 0);
            fly(.2f);
            int burst = board.SpawnedCount - before;
            fly(2f);
            Check("... and no burst when it ends, though every timer was due (" + burst + " at the instant, " + (board.SpawnedCount - before) + " after 2 s)",
                  !board.InCalmWindow && burst == 0 && board.SpawnedCount - before <= 3 && board.SpawnedCount - before >= 1);

            // a loop back to the same world is an arrival too
            before = board.SpawnedCount;
            windows = board.CalmWindows;
            RunLoop.Advance();
            fly(2f);
            Check("a loop back round is an arrival too", board.CalmWindows == windows + 1 && board.SpawnedCount == before);

            // arriving fast (SPEED 10+): no window
            fly(20f);
            windows = board.CalmWindows;
            before = board.SpawnedCount;
            Sweep();
            moveBackGround.speed = enmiesOnBoard.FastArrivalHudSpeed / 100f;
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 2);
            fly(3f);
            Check("a ship arriving at SPEED " + enmiesOnBoard.FastArrivalHudSpeed + "+ skips the window (" + (board.SpawnedCount - before) + " spawns in 3 s)",
                  board.CalmWindows == windows && !board.InCalmWindow && board.SpawnedCount > before);
            Check("elites cannot lift off inside the window either (none before " + EliteDirector.FirstSeconds + " s in a world)",
                  EliteDirector.FirstSeconds > enmiesOnBoard.CalmArrivalSeconds);
            Object.DestroyImmediate(board.gameObject);
        }
        finally
        {
            ShipStartSpeed.EquippedHudOverride = null;
            RunLoop.Reset();
            SetWorldManager(null);
            Object.DestroyImmediate(wmGo);
            moveBackGround.speed = 0f;
            Clear();
        }
    }

    static void Sweep()
    {
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        PilotAirspace.Clear();
    }

    // ---- 9: pilots and elites -----------------------------------------------------

    static void PilotsAndElitesShareTheAir()
    {
        Clear();
        Stage(3, Shapes[0]);
        moveBackGround.speed = .2f;
        var defs = new List<EliteDef>();
        Check("Ember has elites", EliteCatalog.ForWorld(3, defs) > 0);
        if (defs.Count == 0) return;
        // an elite hovering mid-lane
        var elite = EliteShip.CreateInPlay(defs[0], new Vector2(0f, 2f));
        elite.AttackCooldown = 99f;
        Check("the elite is in play", elite.InPlay);
        var fighter = EnemyRoster.Fighter(3, 2);
        float half = PilotAirspace.ColumnHalf(fighter, fighter.Behaviour);
        int admitted = 0, overElite = 0;
        Random.InitState(9);
        for (int i = 0; i < 40; i++)
        {
            float x;
            if (!PilotAirspace.TryAdmit(fighter, fighter.Behaviour, float.NaN, out x)) continue;
            admitted++;
            float ex = elite.transform.position.x, r = elite.Def.hullRadius;
            if (x - half < ex + r && x + half > ex - r) overElite++;
        }
        Check("with room elsewhere a pilot is never stationed over an elite in play (" + overElite + " of " + admitted + " admissions)",
              admitted == 40 && overElite == 0);
        float asked;
        Check("an elite never closes the airspace: a pilot is still admitted when asked for the elite's own column",
              PilotAirspace.TryAdmit(fighter, fighter.Behaviour, elite.transform.position.x, out asked));
    }
}
