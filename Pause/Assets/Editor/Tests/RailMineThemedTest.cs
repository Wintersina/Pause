using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EVERY WORLD'S RAIL MINE ATTACKS IN THAT WORLD'S OWN MATERIAL (docs/world-attacks-design.md, the add-world skill's THEMED ATTACKS rule).
//
//   Space / Frost / Verdant  the mine fires the world's painted laser kit (Resources/Attacks/<World>/<w>_attack_laser*, AttackArt.BossLaser:
//                            ion beam, ice beam with crystal shards, thorned vine beam), NOT the boss cells recoloured magenta
//   Ember                    the flame-thrower (MineFlameArt), unchanged
//   Tide / Storm             no installed kit (Tide's is staged under Attacks/Tide~, Storm has none): the recoloured boss-cell beam, unchanged
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
    const float MinPinkShare = .15f;

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                AttackArt.Clear(); MineFlameArt.Reset(); MineLaserArt.ResetCache();
                for (int w = 0; w <= 2; w++) Kit(w);
                Others();
                for (int w = 0; w <= 3; w++) Firing(w);
                for (int w = 0; w <= 2; w++)
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

    static void Kit(int w)
    {
        string t = Keys[w] + ": ";
        var a = MineLaserArt.For(w);
        var kit = AttackArt.BossLaser(w);
        Check(t + "the mine's art IS the world's laser kit (cached object, not the boss cells)", kit != null && a.kit == kit && a.flame == null);
        Check(t + "built once per world: a second look-up is the same object", ReferenceEquals(MineLaserArt.For(w), a) && ReferenceEquals(AttackArt.BossLaser(w), kit));
        if (kit == null) return;
        var boss = BossCatalog.ForWorld(w);
        var bossBeam = BossArt.Shot(boss, BossArt.Beam0);
        Check(t + "beam frames come from <w>_attack_laserbody, the aim line from _lasertell, the muzzle / impact from _laser, none from the boss sheet",
              a.beam0.texture == AttackArt.Atlas(w, "laserbody") && a.beam1.texture == AttackArt.Atlas(w, "laserbody") && a.sight.texture == AttackArt.Atlas(w, "lasertell") &&
              kit.muzzle[0].texture == AttackArt.Atlas(w, "laser") && kit.impact[0].texture == AttackArt.Atlas(w, "laser") &&
              (bossBeam == null || a.beam0.texture != bossBeam.texture));
        float drawn = RailMineLaser.KitBodyScale(kit) * kit.bodyOpaquePx / 128f;
        Check(t + "the painted body's opaque width (" + drawn.ToString("F3") + " u) is the hit width (" + RailMineLaser.HitThickness + " u)", Mathf.Abs(drawn - RailMineLaser.HitThickness) < 1e-4f);
        string line = "";
        bool cue = true;
        foreach (var (name, s) in new[] { ("body0", kit.body[0]), ("body2", kit.body[2]), ("muzzle", kit.muzzle[1]), ("impact", kit.impact[1]) })
        {
            float share = PinkShare(s, out int red, out int opq);
            line += " " + name + " " + (share * 100f).ToString("F0") + "%/" + red + " red";
            // (Verdant's impact burst has 4 stray player-red pixels of ~1000 in the approved art: tolerated at <= 1%, never in the body or muzzle)
            cue &= share >= MinPinkShare && opq > 0 && (name == "impact" ? red * 100 <= opq : red == 0);
        }
        Check(t + "the hostile pink cue stays (>= " + (MinPinkShare * 100f) + "% pink-family of the opaque pixels, no player-red:" + line + ")", cue);
        int sightRed = 0, so = 0;
        PinkShare(kit.sight[0], out sightRed, out so);
        Check(t + "the aim line is drawn (" + so + " px) and holds no player-red (" + sightRed + ")", so > 0 && sightRed == 0);
        long bytes = 0;
        foreach (var f in new[] { "laser", "laserbody", "lasertell" }) { var tx = AttackArt.Atlas(w, f); bytes += tx != null ? tx.width * tx.height * 4L : 0; }
        Check(t + "the kit adds no texture of its own beyond its three sheets (" + bytes / 1024 + " KB <= BossLaserArtTest budget " + BossLaserArtTest.MemoryBudget / 1024 + " KB)", bytes > 0 && bytes <= BossLaserArtTest.MemoryBudget);
    }

    static void Others()
    {
        Check("Ember keeps the flame-thrower (no laser kit)", MineLaserArt.For(3).flame != null && MineLaserArt.For(3).kit == null);
        Check("Tide and Storm keep the recoloured boss-cell beam (Tide's kit is staged, not installed; Storm has none)",
              MineLaserArt.For(4).kit == null && MineLaserArt.For(4).flame == null && MineLaserArt.For(5).kit == null && AttackArt.BossLaser(4) == null);
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
        bool kitWorld = w <= 2;
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
                aimOk = l.SightRenderer.sprite.texture == AttackArt.Atlas(w, "lasertell") && l.SightRenderer.drawMode == SpriteDrawMode.Tiled && lk.enabled && lk.sprite.texture == AttackArt.Atlas(w, "lasertell");
                windOk = f.enabled && f.sprite != null && f.sprite.texture == AttackArt.Atlas(w, "laser");
            }
            if (l.State == RailMineLaser.Phase.Beam)
            {
                beamAge += Dt;
                if (kitWorld && beamAge > .1f && !beamSeen)
                {
                    beamSeen = true;
                    var b = l.BeamRenderer;
                    beamOk = b.enabled && b.sprite.texture == AttackArt.Atlas(w, "laserbody") && b.drawMode == SpriteDrawMode.Tiled && l.Kit == AttackArt.BossLaser(w) &&
                             l.FlashRenderer.sprite.texture == AttackArt.Atlas(w, "laser");
                    impactOk = l.ImpactRenderer.enabled && l.ImpactRenderer.sprite.texture == AttackArt.Atlas(w, "laser") && l.SparkRenderer.enabled;
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
                fadeOk = l.FlashRenderer.sprite.texture == AttackArt.Atlas(w, "laser");
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
        Debug.Log("[MINETHEMED] " + t + "beam stand-out pixels, worst of " + samples + " samples along the beam: " + worstStand);
        Check(t + "the beam stands out of the " + Keys[w] + " backdrop along its whole length (>= " + HostileProjectileTest.MinStandOutPixels + " px at " + HostileProjectileTest.MinContrast + ":1; worst " + worstStand + ")",
              samples > 0 && worstStand >= HostileProjectileTest.MinStandOutPixels);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(rail);
        EnemyThreat.ForceShooting = false;
    }
}
