using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Space's elite hull LIGHTS and launch DOCKS:
//
//   lights   each Space elite's <key>_lights strip (the hull's animated lights, an additive overlay) loads, is cell-aligned with
//            the hull strip (same cell count and size), is drawn by EliteShip on an additive layer over the hull wearing the
//            SAME cell every frame (burn cycle, the damaged cell, the pilot-light cell, lift-off) with the same transform; it keeps
//            going while damaged (stuttering) and while hiding; it is off while parked engines-off and comes on with the pilot
//            lights; an elite with no _lights strip is untouched
//   docks    the four drifting backdrop docks (gantry, carrier, gate, derelict: SpaceDocks.cs) each have their 6-cell launch strip,
//            a pad point inside the drawing, a LandingSite of their own kind at it, play the launch strip through the elite's
//            launch (idle, blink, open, flare, open, close), launch the right elite, and fit the Space texture budget
//   homes    each Space elite launches from one of its own kinds when one is free (gate / station, carrier / asteroid, gantry /
//            station, derelict / planet), any free body when none is, and the director brings one of its dock bodies into view
//            a few seconds before it is due
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceDockTest.Run
public static class SpaceDockTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SDK] PASS  " : "[SDK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    const string AtlasPng = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/docks.png";
    static Transform pilot;

    // key, its home dock (the first kind of its launchFrom), the dock's DockDefs index
    static readonly (string key, string launchFrom, LandingKind home, int dock)[] Elites =
    {
        ("space_elite_rift_lancer", "gate,station", LandingKind.Gate, 2),
        ("space_elite_orbit_reaver", "carrier,asteroid", LandingKind.Carrier, 1),
        ("space_elite_eventide_bastion", "gantry,station", LandingKind.Gantry, 0),
        ("space_elite_singularity_hauler", "derelict,planet", LandingKind.Derelict, 3),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            LightStrips();
            LightsFollowHull();
            LightsParkedAndLaunch();
            LightsDamagedAndHiding();
            NoLightsUnaffected();
            DockArt();
            DockSitesAndLaunch();
            HomesSynthetic();
            HomesBroughtIntoView();
            Budget();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            EliteDirector.WorldOverride = -1;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[SDK] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as SpaceEliteTest's) -----------------------------------------------

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = speed;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        EliteDirector.WorldOverride = -1;
        Random.InitState(2468);
    }

    static EliteShip InPlay(EliteDef def, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(def, at);
        e.AttackCooldown = 999f;
        return e;
    }

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    static LandingSite Body(Vector2 at, LandingKind kind, int id, bool emerge = true)
    {
        var anchor = new GameObject("~Body" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind, emerge = emerge };
    }

    // ---- lights ---------------------------------------------------------------------------

    static void LightStrips()
    {
        foreach (var x in Elites)
        {
            var d = EliteCatalog.Find(x.key);
            var hull = EliteArt.Frames(d);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            Check(x.key + ": the _lights strip is installed and loads (" + (lights != null ? lights.Length : 0) + " cells)", lights != null && lights.Length > 0 && EliteArt.HasExtra(d, EliteArt.Extra.Lights));
            if (lights == null || hull == null) continue;
            bool aligned = lights.Length == hull.Length && lights[0].texture.width == hull[0].texture.width && lights[0].texture.height == hull[0].texture.height;
            for (int i = 0; i < Mathf.Min(lights.Length, hull.Length); i++)
                aligned &= Mathf.Approximately(lights[i].rect.width, hull[i].rect.width) && Mathf.Approximately(lights[i].rect.height, hull[i].rect.height) &&
                           Mathf.Approximately(lights[i].textureRect.x, hull[i].textureRect.x) && Mathf.Approximately(lights[i].textureRect.y, hull[i].textureRect.y) &&
                           Mathf.Approximately(lights[i].pixelsPerUnit, hull[i].pixelsPerUnit) && lights[i].pivot == hull[i].pivot;
            Check(x.key + ": cell-aligned with the hull strip (" + lights.Length + " vs " + hull.Length + " cells of " + lights[0].rect.width + " px, " +
                  lights[0].texture.width + "x" + lights[0].texture.height + " vs " + hull[0].texture.width + "x" + hull[0].texture.height + ")", aligned);
            Check(x.key + ": its lights are bright small points over a clear strip (not a second hull)", Coverage(lights[3]) > 0f && Coverage(lights[3]) < .08f);
        }
    }

    // Share of a cell's pixels with any alpha (read from the PNG: the texture is not readable).
    static readonly Dictionary<Texture, Color32[]> pixelCache = new Dictionary<Texture, Color32[]>();
    static float Coverage(Sprite s)
    {
        string path = "Assets/Art/Resources/Elites/Space/" + s.texture.name + ".png";
        if (!File.Exists(path)) return 0f;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        Rect r = s.textureRect;
        var px = tex.GetPixels32();
        int w = tex.width, lit = 0, n = 0;
        for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
            for (int x = (int)r.x; x < (int)(r.x + r.width); x++) { n++; if (px[y * w + x].a > 0) lit++; }
        Object.DestroyImmediate(tex);
        return n > 0 ? lit / (float)n : 0f;
    }

    static void LightsFollowHull()
    {
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var hull = EliteArt.Frames(d);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            var e = InPlay(d, new Vector2(0f, 1f));
            Check(x.key + ": the ship draws its lights on a child layer of the hull with the additive shader",
                  e.HasLights && e.Lights.transform.parent == e.HullTransform && e.Lights.sharedMaterial != null && e.Lights.sharedMaterial.shader.name == "Pause/BackdropAdditive" &&
                  e.Lights.sortingOrder > e.Hull.sortingOrder);
            var cells = new HashSet<int>();
            bool same = true, sprites = true, transform = true, shown = true;
            string bad = "";
            // (sampled after each step: Animate / Render have run for that frame)
            for (int i = 0; i < 4 * 30; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f) * 1.6f, -2.5f, 0f);
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                int cell = e.CurrentFrame;
                cells.Add(cell);
                same &= e.LightCell == cell;
                sprites &= e.Lights.sprite == lights[cell] && e.Hull.sprite == hull[cell];
                transform &= e.Lights.transform.lossyScale == e.HullTransform.lossyScale && e.Lights.transform.rotation == e.HullTransform.rotation &&
                             e.Lights.transform.position == e.HullTransform.position && e.Lights.flipX == e.Hull.flipX && e.Lights.flipY == e.Hull.flipY;
                shown &= e.Lights.enabled;
                if ((!same || !sprites || !transform || !shown) && bad.Length < 120) bad += "[cell " + cell + " lit " + e.LightCell + "] ";
            }
            Check(x.key + ": the lights wear the hull's cell every frame (" + cells.Count + " burn-cycle cells seen " + bad + ")", Alive(e) && cells.Count >= 6 && same && sprites);
            Check(x.key + ": ... with the hull's own transform, flip and bank, and always on in flight", transform && shown);
            // a hit shows the hit flash on the hull: the lights go on following
            e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .3f);
            Step(.2f);
            Check(x.key + ": ... still on its cell after a hit (" + e.LightCell + " = " + e.CurrentFrame + ")", e.LightCell == e.CurrentFrame);
            EliteSystem.Clear();
        }
    }

    static void LightsParkedAndLaunch()
    {
        // parked on a body that shows its hull (not docked inside one): engines off, lights off; the pilot lights blink on in the
        // launch tell and bring the pilot-light cell's lights with them; the lift-off wears its cell's lights
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            var site = Body(new Vector2(.8f, 3f), LandingKind.Station, 1, false);
            var e = EliteShip.Create(d, site, 2.5f, new Vector2(-1.4f, 1.4f));
            bool offEarly = true, sawPilot = false, offBlink = false, lateOnWithEngine = true;
            int guard = 0;
            while (e.State == EliteState.Parked && guard++ < 400)
            {
                EliteSystem.Step(Dt);
                if (e.State != EliteState.Parked) break;
                bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds;
                if (!tell && e.StateTime > Dt * 2) offEarly &= e.LightCell == -1 && !e.Lights.enabled;
                if (tell)
                {
                    if (e.EngineLightsOn) { sawPilot |= e.LightCell == d.cells.parkedIdle && e.Lights.enabled && e.Lights.sprite == lights[d.cells.parkedIdle]; lateOnWithEngine &= e.LightCell == d.cells.parkedIdle; }
                    else offBlink |= e.LightCell == -1;
                }
            }
            Check(x.key + ": parked with its engines off the lights are off", offEarly);
            Check(x.key + ": the pilot lights come on in the launch tell with the pilot-light cell's lights (" + d.cells.parkedIdle + ")", sawPilot && lateOnWithEngine);
            Check(x.key + ": ... and blink off again with them", offBlink);
            bool lift = true;
            int liftFrames = 0;
            guard = 0;
            while (e.State == EliteState.LiftOff && guard++ < 300)
            {
                EliteSystem.Step(Dt);
                if (e.State != EliteState.LiftOff) break;
                liftFrames++;
                lift &= e.LightCell == e.CurrentFrame && e.Lights.enabled;
            }
            Check(x.key + ": through the lift-off the lights wear the hull's cell (" + liftFrames + " frames)", liftFrames > 5 && lift);
            EliteSystem.Clear();
        }
        // docked INSIDE a body the hull is hidden: nothing of it shows, lights included
        Fresh(.05f);
        var def = EliteCatalog.Find(Elites[0].key);
        var inside = EliteShip.Create(def, Body(new Vector2(.8f, 3f), LandingKind.Gate, 2), 2.5f, new Vector2(-1.4f, 1.4f));
        bool dark = true;
        int g2 = 0;
        while (inside.State == EliteState.Parked && g2++ < 400) { EliteSystem.Step(Dt); if (inside.State == EliteState.Parked) dark &= !inside.Lights.enabled; }
        Check("docked inside a body: no lights show (the hull is hidden)", dark);
        bool fading = false;
        g2 = 0;
        while (inside.State == EliteState.LiftOff && g2++ < 300)
        {
            EliteSystem.Step(Dt);
            if (inside.State != EliteState.LiftOff) break;
            if (inside.Lights.enabled) fading |= Mathf.Approximately(inside.Lights.color.a, inside.Hull.color.a);
        }
        Check("... they come out with the hull, fading in with it", fading);
        EliteSystem.Clear();
    }

    static void LightsDamagedAndHiding()
    {
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            var e = InPlay(d, new Vector2(0f, 1f));
            Step(.5f);
            e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .3f);
            if (!Alive(e)) { Check(x.key + ": survives a hit", false); continue; }
            bool hid = e.Hidden;
            // (a hurt-hider flies off and waits; the lights keep going all the while)
            var alphas = new HashSet<float>();
            bool onCell = true, on = true;
            int n = 0;
            float minA = 9f, maxA = 0f;
            Step(hid ? 3f : 2f, () =>
            {
                n++;
                if (n < 3) return;
                onCell &= e.LightCell == e.CurrentFrame && e.CurrentFrame == d.cells.damaged;
                on &= e.Lights.enabled;
                float a = e.Lights.color.a;
                alphas.Add(a);
                minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
            });
            Check(x.key + ": damaged" + (hid ? " and hiding" : "") + ": the lights wear the damaged cell (" + d.cells.damaged + ") and stay on", e.Damaged && onCell && on && e.Lights.sprite == lights[d.cells.damaged]);
            Check(x.key + ": ... and keep animating over the one held hull cell: they stutter (" + alphas.Count + " brightness steps " + minA.ToString("0.00") + ".." + maxA.ToString("0.00") + ")",
                  alphas.Count >= 3 && minA > .1f && maxA >= .99f);
            if (hid)
            {
                Step(14f);   // back from hiding
                Check(x.key + ": after the hide it is back, still lit", e.InPlay && e.LightCell == e.CurrentFrame && e.Lights.enabled);
            }
            // a retreat flies home: lights on the way
            if (Alive(e) && e.InPlay) { e.EngageLeft = 0f; Step(1.5f); }
            if (Alive(e) && e.State == EliteState.Retreat)
            {
                bool retreatLit = true;
                int r = 0;
                Step(2f, () => { r++; if (r > 2 && e.State == EliteState.Retreat) retreatLit &= e.Lights.enabled && e.LightCell == e.CurrentFrame; });
                Check(x.key + ": retreating, the lights keep going", retreatLit && r > 3);
            }
            EliteSystem.Clear();
        }
    }

    static void NoLightsUnaffected()
    {
        Fresh(.05f);
        int checkedDefs = 0;
        bool none = true, clean = true;
        foreach (var d in EliteCatalog.All)
        {
            if (d.world == "space") continue;
            none &= !EliteArt.HasExtra(d, EliteArt.Extra.Lights);
            var e = InPlay(d, new Vector2(0f, 1f));
            clean &= !e.HasLights && e.Lights == null && e.LightCell == -1 && e.HullTransform.Find("Lights") == null;
            Step(.4f);
            clean &= Alive(e) && e.Hull.sprite != null && e.LightCell == -1;
            checkedDefs++;
            EliteSystem.Clear();
            if (checkedDefs >= 6) break;
        }
        Check("elites without a _lights strip are unaffected: no lights layer, nothing drawn (" + checkedDefs + " elites of other worlds)", checkedDefs > 0 && none && clean);
        var fake = new EliteDef { key = "no_such_elite", world = "space", cellWorldSize = 1.5f };
        Check("a missing strip is just null", EliteArt.ExtraFrames(fake, EliteArt.Extra.Lights) == null && !EliteArt.HasExtra(fake, EliteArt.Extra.Lights));
    }

    // ---- docks ----------------------------------------------------------------------------------

    static Texture2D atlasTex;
    static Color32[] atlasPx;

    static void LoadAtlasPixels()
    {
        if (atlasPx != null) return;
        atlasTex = new Texture2D(2, 2);
        atlasTex.LoadImage(File.ReadAllBytes(AtlasPng));
        atlasPx = atlasTex.GetPixels32();
    }

    static SpaceDirector NewSpace(out WorldBackdrop wb)
    {
        wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        return wb.Current.Director as SpaceDirector;
    }

    // The side (px) of the biggest all-opaque, all-near-black square in a cell (a flat door panel is ~60 px; drawing detail stays under 30).
    static int FlatDarkBlock(Rect r)
    {
        int best = 0, w = atlasTex.width;
        for (int y = (int)r.y; y < r.yMax - 4; y += 3)
            for (int x = (int)r.x; x < r.xMax - 4; x += 3)
            {
                int n = 0;
                while (n < 80 && x + n < r.xMax && y + n < r.yMax)
                {
                    bool ok = true;
                    for (int k = 0; k <= n && ok; k += 2)
                    {
                        var a = atlasPx[(y + n) * w + x + k]; var b = atlasPx[(y + k) * w + x + n];
                        ok = a.a > 150 && Mathf.Max(a.r, Mathf.Max(a.g, a.b)) < 26 && b.a > 150 && Mathf.Max(b.r, Mathf.Max(b.g, b.b)) < 26;
                    }
                    if (!ok) break;
                    n++;
                }
                if (n > best) best = n;
            }
        return best;
    }

    static void DockArt()
    {
        Fresh(.3f);
        var wb = default(WorldBackdrop);
        var sd = NewSpace(out wb);
        Check("Space builds its dock pool", sd != null && sd.Docks != null && sd.Docks.Capacity >= 2);
        LoadAtlasPixels();
        Check("the docks atlas is the PNG on disk (" + atlasTex.width + "x" + atlasTex.height + ")", atlasTex.width > 1000 && atlasTex.height > 600);
        var seenKinds = new HashSet<LandingKind>();
        for (int t = 0; t < SpaceDirector.DockDefs.Length; t++)
        {
            var d = SpaceDirector.DockDefs[t];
            var cells = sd.DockFrames(t);
            Check(d.tag + ": its " + SpaceDirector.DockCells + "-cell launch strip loads (" + (cells != null ? cells.Length : 0) + ")", cells != null && cells.Length == SpaceDirector.DockCells);
            if (cells == null) continue;
            bool equal = true;
            for (int i = 1; i < cells.Length; i++) equal &= Mathf.Approximately(cells[i].rect.width, cells[0].rect.width) && Mathf.Approximately(cells[i].rect.height, cells[0].rect.height);
            Check(d.tag + ": every cell the same size (" + cells[0].rect.width + "x" + cells[0].rect.height + ")", equal);
            Vector2 pad = d.Pad;
            Check(d.tag + ": its pad point (" + pad.x.ToString("0.000") + ", " + pad.y.ToString("0.000") + ") is inside the drawing's bounds", Mathf.Abs(pad.x) < .5f && Mathf.Abs(pad.y) < .5f);
            // the pad sits on the drawing: the dark opening is opaque, in every cell but the doors-open ones it has opaque pixels all round
            Rect r0 = cells[0].textureRect;
            int px = Mathf.RoundToInt(r0.x + (pad.x + .5f) * r0.width), py = Mathf.RoundToInt(r0.y + (pad.y + .5f) * r0.height);
            int opaque = 0, ring = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    opaque += atlasPx[(py + dy) * atlasTex.width + px + dx].a >= 200 ? 1 : 0;
                    ring++;
                }
            // (the bay's flat door panel is gone: the opening may be see-through, framed by the structure round it)
            int wide = 0, wideAll = 0;
            for (int dy = -40; dy <= 40; dy += 2)
                for (int dx = -40; dx <= 40; dx += 2) { wide += atlasPx[(py + dy) * atlasTex.width + px + dx].a >= 200 ? 1 : 0; wideAll++; }
            Check(d.tag + ": the pad sits on the drawing: opaque at it or framed by the structure round it (" + opaque + "/" + ring + ", " + wide + "/" + wideAll + ")",
                  (atlasPx[py * atlasTex.width + px].a >= 200 && opaque >= ring * .9f) || wide >= wideAll * .35f);
            // no black box: no cell has a flat opaque dark panel (the closed door Codex painted as a rectangle; the ring gate's round void is its portal)
            if (d.kind != LandingKind.Gate)
                for (int c = 0; c < cells.Length; c++)
                {
                    Rect rc = cells[c].textureRect;
                    int box = FlatDarkBlock(rc);
                    Check(d.tag + " cell " + c + ": no flat dark panel left on the dock (largest " + box + " px square)", box < 30);
                }
            // the pad is far enough from the drawing's edge that an elite fits in the opening: not within 12% of the cell's border
            Check(d.tag + ": ... well inside the cell, not on its rim", Mathf.Abs(pad.x) < .38f && Mathf.Abs(pad.y) < .38f);
            // the launch strip is the right set of drawings: the doors open (more light / less opaque at the pad) by cell 4
            seenKinds.Add(d.kind);
            Check(d.tag + ": a LandingKind of its own that its launchFrom word reaches (" + d.kind + ")",
                  LandingSite.KindOf(d.launchKey) == d.kind && LandingSite.Accepts(d.launchKey, d.kind) && !LandingSite.Accepts(d.launchKey, LandingKind.Station));
        }
        Check("four distinct dock kinds", seenKinds.Count == 4);
        Object.DestroyImmediate(wb.gameObject);
        Object.DestroyImmediate(atlasTex);
        atlasTex = null; atlasPx = null;
    }

    // Bring dock `t` in (BringHome) and run the backdrop until it is offered as a site; the site, or false.
    static bool WaitForSite(WorldBackdrop wb, SpaceDirector sd, LandingKind kind, out LandingSite site, out BackdropPiece piece, float maxSeconds = 90f)
    {
        var sites = new List<LandingSite>();
        site = default;
        piece = null;
        for (float t = 0f; t < maxSeconds; t += 1f / 15f)
        {
            wb.Step(1f / 15f);
            LandingSites.Collect(sites);
            foreach (var s in sites)
                if (s.kind == kind)
                {
                    site = s;
                    foreach (var p in sd.Docks.items) if (p.active && p.root == s.anchor) piece = p;
                    return piece != null;
                }
        }
        return false;
    }

    static void DockSitesAndLaunch()
    {
        foreach (var x in Elites)
        {
            Fresh(.3f);
            WorldBackdrop wb;
            var sd = NewSpace(out wb);
            var d = EliteCatalog.Find(x.key);
            var def = SpaceDirector.DockDefs[x.dock];
            string k = def.tag + " dock: ";
            Check(k + "BringHome(" + x.launchFrom + ") brings one in", sd.BringHome(x.launchFrom) && sd.Docks.ActiveCount == 1);
            LandingSite site;
            BackdropPiece piece;
            bool offered = WaitForSite(wb, sd, def.kind, out site, out piece);
            Check(k + "it drifts into view and offers a " + def.kind + " site", offered);
            if (!offered) { Object.DestroyImmediate(wb.gameObject); continue; }
            Check(k + "the site is an emerge site inside the dock, behind the gameplay, small, at the dock's pad",
                  site.emerge && site.Valid && site.order < EliteShip.PlayOrder - 2 && site.scale > .05f && site.scale < .5f && site.kind == def.kind);
            Bounds b = piece.sr.sprite.bounds;
            Vector3 want = piece.root.TransformPoint(new Vector3(b.center.x + def.Pad.x * b.size.x, b.center.y + def.Pad.y * b.size.y, 0f));
            Check(k + "the site is the manifest's pad point (" + Vector3.Distance(want, site.Position).ToString("0.000") + " u off)", Vector3.Distance(want, site.Position) < .005f);
            float width = b.size.x * piece.root.lossyScale.x;
            Check(k + "drawn " + width.ToString("0.00") + " u wide, sorted with the structures", width >= SpaceDirector.DockWidthMin - .01f && width <= SpaceDirector.DockWidthMax + .01f && piece.sr.sortingOrder < EliteShip.PlayOrder - 2);
            Check(k + "upright, never turning", Mathf.Abs(piece.root.eulerAngles.z) < .01f || Mathf.Abs(piece.root.eulerAngles.z - 360f) < .01f);

            // the launch: its elite parks in it; the strip plays through the launch tell and the lift-off
            Vector3 pos0 = site.Position;
            var e = EliteShip.Create(d, site, 2.4f, EliteDirector.JoinPoint(d, EliteBrains.Create(d.brain).JoinFrom, site.Position));
            var order = new List<int>();
            var phaseOfCell = new Dictionary<int, (float lo, float hi)>();
            int lastCell = -1;
            bool rides = true, idleFirst = false, closed = false;
            float afterJoin = 0f;
            for (int i = 0; i < 30 * 14; i++)
            {
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                int cell = sd.DockCellOf(piece);
                float ph = Alive(e) ? e.LaunchPhase : 2f;
                if (e.State == EliteState.Parked) rides &= Vector2.Distance(e.transform.position, site.Position) < .02f || !site.Valid;
                if (cell != lastCell) { order.Add(cell); lastCell = cell; }
                if (!phaseOfCell.ContainsKey(cell)) phaseOfCell[cell] = (ph, ph);
                else { var v = phaseOfCell[cell]; phaseOfCell[cell] = (Mathf.Min(v.lo, ph), Mathf.Max(v.hi, ph)); }
                if (e.State != EliteState.Parked && e.State != EliteState.LiftOff) { afterJoin += Dt; if (afterJoin > DockCloseWait) break; }
                if (!piece.active) break;
            }
            idleFirst = order.Count > 0 && order[0] == SpaceDirector.DockIdle;
            closed = order.Count > 0 && order[order.Count - 1] == SpaceDirector.DockIdle && order.Contains(SpaceDirector.DockClosing);
            // first appearance order of the cells 1..5
            int pos = 0, lastIdx = -1;
            bool inOrder = true;
            foreach (int c in new[] { SpaceDirector.DockOpening, SpaceDirector.DockOpen, SpaceDirector.DockClosing })
            {
                int idx = order.IndexOf(c);
                if (idx < 0 || idx < lastIdx) { inOrder = false; break; }
                lastIdx = idx;
                pos++;
            }
            Check(k + "its launch strip plays idle, opening, open, closing, idle -- the blink and flare cells are skipped (" + string.Join(">", order) + ")",
                  idleFirst && inOrder && closed && !order.Contains(SpaceDirector.DockBlink) && !order.Contains(SpaceDirector.DockFlare));
            Check(k + "the doors open in the launch tell (open cell phases " + Range(phaseOfCell, SpaceDirector.DockOpening) + ")",
                  phaseOfCell.ContainsKey(SpaceDirector.DockOpening) && phaseOfCell[SpaceDirector.DockOpening].lo < 1f);
            Check(k + "the elite rode the dock and came out into play (" + e.State + ")", rides && e.InPlay);
            // after the launch nothing of it is left on the dock: only the dock's own sprite draws there, and it shows no flat panel
            int helpers = 0;
            foreach (var r in piece.root.GetComponentsInChildren<SpriteRenderer>()) if (r.enabled && r != piece.sr) helpers++;
            Check(k + "after the launch no helper renderer is left on the dock (" + helpers + "), the elite's launch light is out",
                  helpers == 0 && (!Alive(e) || !e.LaunchLightOn));
            Object.DestroyImmediate(wb.gameObject);
            EliteSystem.Clear();
        }
        // the cell function
        Check("the launch cells by phase", SpaceDirector.DockCellAt(.05f) == SpaceDirector.DockIdle && SpaceDirector.DockCellAt(.6f) == SpaceDirector.DockOpening &&
              SpaceDirector.DockCellAt(1.05f) == SpaceDirector.DockOpen && SpaceDirector.DockCellAt(1.5f) == SpaceDirector.DockOpen);
    }

    const float DockCloseWait = SpaceDirector.DockCloseSeconds + .8f;

    static string Range(Dictionary<int, (float lo, float hi)> m, int cell)
    {
        return m.ContainsKey(cell) ? m[cell].lo.ToString("0.00") + ".." + m[cell].hi.ToString("0.00") : "-";
    }

    // ---- homes -------------------------------------------------------------------------------------

    static void HomesSynthetic()
    {
        // one body of every kind: each elite takes one of its own kinds
        Fresh(.05f);
        var kinds = new[] { LandingKind.Station, LandingKind.Planet, LandingKind.Asteroid, LandingKind.Gantry, LandingKind.Carrier, LandingKind.Gate, LandingKind.Derelict };
        var pads = new List<LandingSite>();
        for (int i = 0; i < kinds.Length; i++) pads.Add(Body(new Vector2(-1.5f + i * .5f, 3f + i * .1f), kinds[i], i + 1));
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        var perKey = new Dictionary<string, int>();
        bool own = true;
        string bad = "";
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            if (dir.SpawnGroup(0, 1) != 1) { own = false; continue; }
            var e = EliteShip.Live[0];
            perKey[e.Def.key] = perKey.ContainsKey(e.Def.key) ? perKey[e.Def.key] + 1 : 1;
            bool pref = LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            if (!pref && bad.Length < 120) bad += e.Def.key + " from " + e.Site.kind + "; ";
            own &= pref;
        }
        Check("each Space elite launches from one of its own kinds when every kind is free (" + perKey.Count + " elites, " + bad + ")", own && perKey.Count == 4);
        foreach (var x in Elites)
            Check(x.key + ": launched at least once, from " + x.launchFrom, perKey.ContainsKey(x.key) && perKey[x.key] > 0);

        // its home dock among bodies it does not launch from: that one; then none of its kinds in view: any free body
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var only = new List<LandingSite>();
            for (int i = 0; i < kinds.Length; i++)
                if (!LandingSite.Accepts(d.launchFrom, kinds[i]) || kinds[i] == x.home) only.Add(Body(new Vector2(-1.5f + i * .5f, 3f + i * .1f), kinds[i], i + 1));
            LandingSites.Override = list => list.AddRange(only);
            dir = new GameObject("~Dir").AddComponent<EliteDirector>();
            int mine = 0;
            bool home = true;
            for (int i = 0; i < 40 && mine < 3; i++)
            {
                EliteSystem.Clear();
                if (dir.SpawnGroup(0, 1) != 1) continue;
                var live = EliteShip.Live[0];
                if (live.Def != d) continue;
                mine++;
                home &= live.Site.kind == x.home;
            }
            Check(x.key + ": among " + only.Count + " bodies it takes its " + x.home + " (" + mine + " launches)", mine >= 1 && home);
            only.RemoveAll(sd => LandingSite.Accepts(d.launchFrom, sd.kind));
            bool any = true, saw = false;
            for (int i = 0; i < 40 && !saw; i++)
            {
                EliteSystem.Clear();
                any &= dir.SpawnGroup(0, 1) == 1;
                saw |= EliteShip.Live.Count > 0 && EliteShip.Live[0].Def == d;
            }
            Check(x.key + ": with none of its kinds in view it takes any free body (" + only.Count + " left, saw it " + saw + ")", any && saw);
            Object.DestroyImmediate(dir.gameObject);
        }
        LandingSites.Override = null;
        EliteSystem.Clear();
    }

    static void HomesBroughtIntoView()
    {
        // the director reads the deck's next elite and asks the backdrop for its home a few seconds ahead
        Fresh(.05f);
        var only = new List<LandingSite> { Body(new Vector2(-1.2f, 3f), LandingKind.Planet, 1) };
        LandingSites.Override = list => list.AddRange(only);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        var peek = dir.PeekDef(0);
        Check("the director can read the next elite without dealing it (" + (peek != null ? peek.key : "none") + ")", peek != null && dir.PeekDef(0) == peek && dir.NextDef(0) == peek && dir.NextDef(0) != peek);
        EliteDirector.WorldOverride = 0;
        int asks0 = LandingSites.HomeRequests;
        float t = 0f;
        for (int i = 0; i < 30 * 60 && EliteShip.Live.Count == 0; i++) { dir.Tick(Dt); t += Dt; }
        Check("a Space launch comes (after " + t.ToString("0.0") + " s) and the home bodies were asked for ahead of it, and again once at the due time at most (" + (LandingSites.HomeRequests - asks0) + ")",
              EliteShip.Live.Count == 1 && LandingSites.HomeRequests - asks0 >= 1 && LandingSites.HomeRequests - asks0 <= 2);
        Check("... a fixed set of sites cannot grow one, so it launches from what is free (" + (EliteShip.Live.Count > 0 ? EliteShip.Live[0].Site.kind.ToString() : "-") + ")",
              EliteShip.Live.Count == 1 && EliteShip.Live[0].Site.kind == LandingKind.Planet);
        Object.DestroyImmediate(dir.gameObject);
        EliteSystem.Clear();
        LandingSites.Override = null;
        EliteDirector.WorldOverride = -1;

        // the real backdrop: whichever elite is next, it launches from one of its own kinds
        Fresh(.3f);
        WorldBackdrop wb;
        var sd = NewSpace(out wb);
        EliteDirector.WorldOverride = 0;
        dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int launches = 0, preferred = 0, docks = 0;
        string log = "";
        float time = 0f;
        EliteShip lastShip = null;
        // (the first elite is due 20-28 s in; each next one is a gap later: keep the world busy for the whole run)
        for (int i = 0; i < 15 * 60 * 9 && launches < 5; i++)
        {
            wb.Step(1f / 15f);
            dir.Tick(1f / 15f);
            EliteSystem.Step(1f / 15f);
            time += 1f / 15f;
            var live = EliteShip.Live;
            if (live.Count == 1 && live[0] != lastShip && live[0].State == EliteState.Parked)
            {
                var e = live[0];
                lastShip = e;
                launches++;
                bool pref = LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
                if (pref) preferred++;
                if (e.Site.kind == LandingKind.Gantry || e.Site.kind == LandingKind.Carrier || e.Site.kind == LandingKind.Gate || e.Site.kind == LandingKind.Derelict) docks++;
                log += e.Def.key.Replace("space_elite_", "") + " from " + e.Site.kind + (pref ? "" : " (NOT its own)") + " @" + time.ToString("0") + "s; ";
                dir.Tick(1f / 15f);            // (it counts as alive: the gap timer starts the tick after it is gone)
                EliteSystem.Clear();           // done with it: the next gap runs
            }
        }
        Check("on the real Space backdrop each elite launches from one of its own kinds (" + preferred + "/" + launches + "; " + log + ")", launches >= 4 && preferred == launches);
        Check("... and the backdrop's own docks were among them (" + docks + ")", docks >= 1);
        Object.DestroyImmediate(dir.gameObject);
        Object.DestroyImmediate(wb.gameObject);
        EliteSystem.Clear();
        EliteDirector.WorldOverride = -1;
    }

    // ---- texture budget ------------------------------------------------------------------------------

    static void Budget()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPng);
        var imp = AssetImporter.GetAtPath(AtlasPng) as TextureImporter;
        Check("docks.png imports compressed, no mipmaps, not clamped below its size",
              tex != null && imp != null && !imp.mipmapEnabled && imp.textureCompression != TextureImporterCompression.Uncompressed && imp.maxTextureSize >= Mathf.Max(tex.width, tex.height));
        long astc6 = tex != null ? ((tex.width + 5) / 6) * ((tex.height + 5) / 6) * 16L : 0;
        // the Space budget (WorldBackdropTest.SpaceTextureBudgetBytes) leaves this much over the other Space art
        Check("the docks atlas is " + (astc6 / 1024) + " KB on a phone (ASTC 6x6): well under 1 MB of the Space budget", tex != null && astc6 > 0 && astc6 <= 800L * 1024);
        long full = 4L * 6144 * 936;
        Check("... against ~" + (full / 1024 / 1024) + " MB for one of the 6144-px masters (downscaled 4x)", tex != null && tex.width <= 1600 && astc6 < full / 20);
        Check("the Space texture budget itself is WorldBackdropTest's (SpaceTextureBudgetBytes " + (WorldBackdropTest.SpaceTextureBudgetBytes / 1024) + " KB, measured over every Space PNG, docks included)", WorldBackdropTest.SpaceTextureBudgetBytes > astc6);
    }
}
