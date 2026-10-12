using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EVERY WORLD'S RAIL MINE ATTACKS IN THAT WORLD'S OWN MATERIAL (docs/world-attacks-design.md, the add-world skill's THEMED ATTACKS rule).
//
//   Space                    the mine fires the world's painted laser kit (Resources/Attacks/Space/space_attack_laser*, AttackArt.BossLaser:
//                            ion beam, its pink remapped to ion white-blue by MineCue), NOT the boss cells
//   Ember                    the flame-thrower (ember_attack_mineflame.png, MineFlameArt)
//   Frost / Verdant /        their own custom attack in the same shape (<w>_attack_minebeam.png, AttackArt.MineBeam, MineFlameArt.ForWorld):
//   Tide / Storm             a cryo laser, a thorned vine lash, a pressurised water jet, a lightning arc: pilot flare, aim line, beam loop,
//                            muzzle burst, rail impact and haze, all painted, none of it pink (user, 2026-10-11): each carries its world's own warning colours
//
// Per world: the beam's / aim line's / muzzle's / impact's sprites really come from that kit's atlases (live sprites of a firing mine, not
// just the cache); the kit is built once and cached; the painted body's opaque width is the hit width; the kit still carries the hostile
// pink cue (pink-family share of its opaque pixels) and none of the player's red; the beam stands out of the world's real backdrop
// (HostileProjectileTest.StandOut, MinStandOutPixels at MinContrast, along the whole beam); friendly fire still burns rocks and enemies once.
public static class RailMineThemedTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MINETHEMED] PASS  " : "[MINETHEMED] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const int W = 540, H = 960;
    static readonly string[] Keys = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm" };
    const float MaxPinkShare = .01f;   // NO PINK on a rail mine (user, 2026-10-11)
    const float MinCueShare = .03f;    // ... and the world's own cue colours are there instead

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                AttackArt.Clear(); MineFlameArt.Reset(); MineLaserArt.ResetCache();
                Kit(0);
                Fallback();
                for (int w = 1; w <= 5; w++) BeamAtlas(w);
                for (int w = 0; w <= 5; w++) Firing(w);
                for (int w = 0; w <= 5; w++)
                    Check(Keys[w] + ": friendly fire still works through the themed beam (rock + fighter on the row burn once; off in the tutorial)", RailMineLaserTest.FriendlyFireOfWorld(w) == 0);
            }
            finally
            {
                MineLaserArt.NoKit = false; MineLaserArt.NoFlame = false; MineLaserArt.ResetCache(); AttackArt.Clear();
                RailMineLaser.AngleOverride = null; EnemyThreat.ForceShooting = false; EnemyThreat.Reset();
                BossEncounter.ResetRun(); EliteSystem.Clear(); EliteSystem.PlayerOverride = null; SpawnSpace.ClockOverride = null;
                startMenu.youAreInTutorial = false;
            }
        }
        Debug.Log("[MINETHEMED] failures: " + fails);
        return fails;
    }

    // ---- the kit as the mine resolves it ----

    static float PinkShare(Sprite s, out int red, out int opaque)
    {
        red = opaque = 0;
        var px = ShieldContour.ReadPixels(s, out int w, out int h);
        if (px == null) return 0f;
        int pink = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a < 128) continue;
            opaque++;
            if (HostileGlow.IsPlayerRed(px[i])) { red++; continue; }
            Color.RGBToHSV(px[i], out float hh, out float ss, out float vv);
            float d = hh * 360f;
            if (d >= 285f && d <= 345f && ss > .22f && vv > .55f) pink++;
        }
        return opaque == 0 ? 0f : pink / (float)opaque;
    }

    static float CueShare(Sprite s, int w)
    {
        var px = ShieldContour.ReadPixels(s, out int pw, out int ph);
        if (px == null) return 0f;
        int n = 0, cue = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a < 128) continue;
            n++;
            Color.RGBToHSV(px[i], out float hh, out float ss, out float vv);
            if (ss > .08f && vv > .5f && Mathf.Abs(Mathf.DeltaAngle(hh * 360f, MineCue.Hue[MineCue.Slot(w)])) <= 30f) cue++;
        }
        return n == 0 ? 0f : cue / (float)n;
    }

    static void Kit(int w)
    {
        string t = Keys[w] + ": ";
        var a = MineLaserArt.For(w);
        var src = AttackArt.BossLaser(w);
        Check(t + "the mine's art is the world's laser kit with its pink remapped (a cached copy, not the boss cells, not the boss's own kit)", src != null && a.kit != null && a.kit != src && a.flame == null &&
              a.kit.body[0] != src.body[0] && a.kit.bodyOpaquePx == src.bodyOpaquePx && a.kit.impactBasePx == src.impactBasePx);
        Check(t + "built once per world: a second look-up is the same object", ReferenceEquals(MineLaserArt.For(w), a));
        if (a.kit == null) return;
        var kit = a.kit;
        float drawn = RailMineLaser.KitBodyScale(kit) * kit.bodyOpaquePx / 128f;
        Check(t + "the painted body's opaque width (" + drawn.ToString("F3") + " u) is the hit width (" + RailMineLaser.HitThickness + " u)", Mathf.Abs(drawn - RailMineLaser.HitThickness) < 1e-4f);
        string line = "";
        bool cue = true;
        foreach (var (name, s) in new[] { ("body0", kit.body[0]), ("body2", kit.body[2]), ("muzzle", kit.muzzle[1]), ("windup", kit.windup[6]), ("impact", kit.impact[1]), ("sight", kit.sight[0]), ("lock", kit.lockOn[0]) })
        {
            float share = PinkShare(s, out int red, out int opq);
            line += " " + name + " " + (share * 100f).ToString("F1") + "%/" + red + " red";
            cue &= opq > 0 && share <= MaxPinkShare && red == 0;
        }
        Check(t + "no pink (<= " + (MaxPinkShare * 100f) + "% pink-family) and no player-red in any cell:" + line, cue);
        Check(t + "the world's cue colour (hue " + MineCue.Hue[MineCue.Slot(w)] + ") is in the beam body (" + (CueShare(kit.body[0], w) * 100f).ToString("F0") + "%)", CueShare(kit.body[0], w) >= MinCueShare);
    }

    // a world without its own atlas (Desert): the boss's laser cells, their hot pink remapped to the sun-white cue
    static void Fallback()
    {
        var a = MineLaserArt.For(6);
        float worst = 0f;
        foreach (var s in new[] { a.sight, a.beam0, a.beam1, a.flash[0], a.flash[1], a.spark[0], a.spark[1] })
            worst = Mathf.Max(worst, s == null ? 1f : PinkShare(s, out int r, out int o));
        Check("Desert (no mine atlas yet): the boss-cell beam carries no pink either (worst " + (worst * 100f).ToString("F1") + "%) and has its own cache slot", worst <= MaxPinkShare && a.flame == null && a.kit == null && !ReferenceEquals(a, MineLaserArt.For(5)));
    }

    const int PilotCells = 4, BeamCells = 6;

    // The painted mine beam of Ember (flame) and of Frost / Verdant / Tide / Storm: slots, file, cue, loops.
    static void BeamAtlas(int w)
    {
        string t = Keys[w] + ": ";
        string file = w == 3 ? "mineflame" : AttackArt.MineBeamFile;
        var a = MineLaserArt.For(w);
        var f = MineFlameArt.ForWorld(w);
        var tex = AttackArt.Atlas(w, file);
        Check(t + "the atlas " + AttackArt.PathOf(w, file) + " exists, 768 x 768", tex != null && tex.width == 768 && tex.height == 768);
        Check(t + "the mine's art IS that atlas (flame slot, no laser kit), every cell painted and Alive",
              f != null && f.painted && f.Alive && f.Complete && a.flame == f && a.kit == null && f.beam[0].texture == tex && f.pilot[0].texture == tex &&
              f.sight.texture == tex && f.flash[3].texture == tex && f.spark[2].texture == tex && f.haze[1].texture == tex);
        if (f == null) return;
        Check(t + "built once: a second look-up is the same object", ReferenceEquals(MineFlameArt.ForWorld(w), f) && ReferenceEquals(MineLaserArt.For(w), a));
        Check(t + BeamCells + " beam bodies of 1 u x 3 u (128 x 384), " + PilotCells + " pilot, 4 burst, 3 impact, 2 haze", f.beam.Length == BeamCells && f.pilot.Length == PilotCells && f.flash.Length == 4 && f.spark.Length == 3 && f.haze.Length == 2 &&
              System.Array.TrueForAll(f.beam, s => s != null && Mathf.Approximately(s.bounds.size.x, 1f) && Mathf.Approximately(s.bounds.size.y, 3f)));
        string line = "";
        bool cue = true;
        var cells = new List<(string, Sprite, bool)>();
        for (int i = 0; i < f.beam.Length; i++) cells.Add(("beam" + i, f.beam[i], true));
        for (int i = 0; i < f.pilot.Length; i++) cells.Add(("pilot" + i, f.pilot[i], false));
        for (int i = 0; i < f.flash.Length; i++) cells.Add(("burst" + i, f.flash[i], false));
        for (int i = 0; i < f.spark.Length; i++) cells.Add(("impact" + i, f.spark[i], false));
        cells.Add(("aim", f.sight, false));
        foreach (var (name, s, strict) in cells)
        {
            float share = PinkShare(s, out int red, out int opq);
            if (name == "beam0" || name == "beam3" || name == "pilot3" || name == "burst1" || name == "impact0") line += " " + name + " " + (share * 100f).ToString("F0") + "%/" + red + " red";
            // Ember's flame has an exempt red-orange edge (MineFlameArt, RailMineLaserTest measures it); the others carry no player-red at all, and a pink cue in
            // every beam body and every effect cell (aim line: dashes in the material, pink fringe optional)
            bool ok = opq > 0 && (w == 3 || red == 0) && share <= MaxPinkShare;
            if (!ok) line += " [" + name + " " + (share * 100f).ToString("F0") + "% pink, " + red + " red]";
            cue &= ok;
        }
        Check(t + "no pink in any cell (<= " + (MaxPinkShare * 100f) + "%) and none of the player's red (Ember's red-orange edge excepted):" + line, cue);
        Check(t + "the world's cue colour (hue " + MineCue.Hue[MineCue.Slot(w)] + ") is in the beam (" + (CueShare(f.beam[0], w) * 100f).ToString("F0") + "%) and the burst (" + (CueShare(f.flash[2], w) * 100f).ToString("F0") + "%)",
              CueShare(f.beam[0], w) >= MinCueShare && CueShare(f.flash[2], w) >= MinCueShare);
        long bytes = tex != null ? tex.width * tex.height * 4L : 0;
        Check(t + "one 2.25 MB sheet (" + bytes / 1024 + " KB), no other texture of its own", bytes == 768L * 768 * 4);
    }

    // ---- a firing mine in the real backdrop ----

    static Camera cam;
    static RenderTexture rt;
    static Texture2D tex;
    static Transform pilot;

    static Camera Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun(); BossRails.Reset(); EliteSystem.Clear(); EnemyThreat.Reset(); RailMineLasers.Clear();
        FriendlyFire.ResetCounters(); FriendlyFire.OnSceneLoaded("gameS1");
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false; score.pauseCounter = 0; startMenu.youAreInTutorial = false; moveBackGround.speed = 0f; Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var bgGo = new GameObject("~Backdrop");
        var backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Keys[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(Dt);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(-.5f, -1.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure();
        if (rt == null) rt = new RenderTexture(W, H, 24);
        if (tex == null) tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        return cam;
    }

    static Color[] Grab()
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        return tex.GetPixels();
    }

    static void Firing(int w)
    {
        string t = Keys[w] + ": ";
        MineLaserArt.ResetCache();
        RailMineLaser.AngleOverride = 14f;
        Scene(w);
        EnemyThreat.ForceShooting = true;
        float clock = 100f;
        SpawnSpace.ClockOverride = clock;
        var def = EnemyRoster.One(w, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(false);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, .5f, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, .5f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = pilot;
        var fb = go.GetComponent<EnemyFlipbook>();
        bool aimOk = false, aimSeen = false, windOk = false, beamSeen = false, beamOk = false, fadeOk = false, coolSeen = false, impactOk = false;
        int worstStand = int.MaxValue, samples = 0;
        RailMineLaser seen = null;
        float beamAge = 0f;
        bool kitWorld = w == 0, flameWorld = w >= 1;
        string file = w == 3 ? "mineflame" : AttackArt.MineBeamFile;
        bool pilotOk = false, pilotSeen = false, flameBeamOk = false, flameBeamSeen = false, flameFxOk = false;
        for (int i = 0; i < 60 * 10; i++)
        {
            clock += Dt; SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            mount.SendMessage("LateUpdate");
            if (fb != null) fb.Advance(Dt);
            EliteSystem.Step(Dt);
            var l = brain.Laser ?? seen;
            if (l == null) continue;
            seen = l;
            l.Place();
            if (l.State == RailMineLaser.Phase.Aim && l.SightShown && kitWorld)
            {
                aimSeen = true;
                var f = l.FlashRenderer; var lk = l.LockRenderer;
                aimOk = l.SightRenderer.sprite == l.Kit.sight[0] || l.SightRenderer.sprite == l.Kit.sight[1]; aimOk &= l.SightRenderer.drawMode == SpriteDrawMode.Tiled && lk.enabled && lk.sprite != null;
                windOk = f.enabled && f.sprite != null && System.Array.IndexOf(l.Kit.windup, f.sprite) >= 0;
            }
            if (l.State == RailMineLaser.Phase.Aim && flameWorld && l.PilotRenderer.enabled && l.State == RailMineLaser.Phase.Aim)
            {
                pilotSeen = true;
                pilotOk = l.PilotRenderer.sprite.texture == AttackArt.Atlas(w, file) && l.Flame == MineFlameArt.ForWorld(w);
            }
            if (l.State == RailMineLaser.Phase.Beam)
            {
                beamAge += Dt;
                if (flameWorld && beamAge > .1f && !flameBeamSeen)
                {
                    flameBeamSeen = true;
                    var tx = AttackArt.Atlas(w, file);
                    flameBeamOk = l.BeamRenderer.enabled && l.BeamRenderer.sprite.texture == tx && l.Kit == null;
                    flameFxOk = l.FlashRenderer.enabled && l.FlashRenderer.sprite.texture == tx && l.ImpactRenderer.enabled && l.ImpactRenderer.sprite.texture == tx &&
                                l.HazeRenderer.enabled && l.HazeRenderer.sprite.texture == tx;
                }
                if (kitWorld && beamAge > .1f && !beamSeen)
                {
                    beamSeen = true;
                    var b = l.BeamRenderer;
                    beamOk = b.enabled && System.Array.IndexOf(l.Kit.body, b.sprite) >= 0 && b.drawMode == SpriteDrawMode.Tiled && l.Kit == MineLaserArt.For(w).kit &&
                             System.Array.IndexOf(l.Kit.muzzle, l.FlashRenderer.sprite) >= 0;
                    impactOk = l.ImpactRenderer.enabled && System.Array.IndexOf(l.Kit.impact, l.ImpactRenderer.sprite) >= 0 && l.SparkRenderer.enabled;
                }
                if (beamAge >= .12f && samples == 0)
                {
                    // the beam against its own backdrop: with it, and with the lasers hidden
                    var root = GameObject.Find("~RailMineLasers");
                    var fg = Grab();
                    root.SetActive(false);
                    var bg = Grab();
                    root.SetActive(true);
                    float ppu = H / (cam.orthographicSize * 2f);
                    for (int k = 2; k <= 8; k++)
                    {
                        Vector2 p = l.From + l.Direction * (l.Length * k / 10f);
                        Vector3 c = cam.WorldToScreenPoint(new Vector3(p.x, p.y, 0f));
                        int n = HostileProjectileTest.StandOut(fg, bg, c, RailMineLaser.HitThickness * .5f * ppu);
                        worstStand = Mathf.Min(worstStand, n);
                        samples++;
                    }
                }
            }
            if (l.State == RailMineLaser.Phase.Cool && kitWorld && l.FlashRenderer.enabled)
            {
                coolSeen = true;
                fadeOk = System.Array.IndexOf(l.Kit.fade, l.FlashRenderer.sprite) >= 0;
            }
            if (l.State == RailMineLaser.Phase.Cool && !kitWorld) coolSeen = true;
            if (coolSeen && !l.Active) break;
        }
        if (kitWorld)
        {
            Check(t + "the aim line (lasertell, tiled) with its lock reticle, and the windup cells (laser sheet) at the muzzle are on screen", aimSeen && aimOk && windOk);
            Check(t + "the live beam is the laserbody strip tiled down its length, muzzle cells over its root, impact burst and sparks at the far rail", beamSeen && beamOk && impactOk);
            Check(t + "the cool-down plays the kit's fade cells", coolSeen && fadeOk);
        }
        if (flameWorld)
        {
            Check(t + "the pilot flare at the muzzle grows through the windup from the world's atlas", pilotSeen && pilotOk);
            Check(t + "the live beam, its muzzle burst, rail impact and haze are all the world's own atlas cells (no kit, no boss cells)", flameBeamSeen && flameBeamOk && flameFxOk);
            Check(t + "the cool-down thins it out", coolSeen);
        }
        Debug.Log("[MINETHEMED] " + t + "beam stand-out pixels, worst of " + samples + " samples along the beam: " + worstStand);
        Check(t + "the beam stands out of the " + Keys[w] + " backdrop along its whole length (>= " + HostileProjectileTest.MinStandOutPixels + " px at " + HostileProjectileTest.MinContrast + ":1; worst " + worstStand + ")",
              samples > 0 && worstStand >= HostileProjectileTest.MinStandOutPixels);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(rail);
        EnemyThreat.ForceShooting = false;
    }
}
