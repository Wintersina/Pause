using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The damage art as the REAL gameplay ship shows it, for every ship in every
// skin (15 x 5): gameS1, the ship spawned by spawnShips (its real prefab,
// every component started), the skin equipped through ShipSkins, the lives
// set by collisionDetection.Start (ShipLives: 1-9), hits and heals through
// collisionDetection's own trigger. After every hit and heal, over the idle
// loop, both bank poses (or a full turn of a spinner) and the hit flash:
//   - the hull shows that skin's sheet, in the row ShipDamageTable.StateFor
//     gives for the hits taken (a 2-life ship goes intact -> critical)
//   - the damaged / critical drawings differ from intact in the pixels
//     actually drawn (GPU read-back), and no frame of the flipbook falls back
//     to the intact row while damaged
//   - healing steps it back down to intact
//
// SkinDamageGameplayTest.Preview renders the same flights to a contact sheet
// (-out <dir>): every ship x skin x state as the camera sees it, FX included.
public static class SkinDamageGameplayTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SG] PASS  " : "[SG] FAIL  ") + what);
        if (!ok) fails++;
    }

    static int passed;
    static void Quiet(string what, bool ok) { if (ok) passed++; else Check(what, false); }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    public static int Execute()
    {
        fails = 0;
        passed = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var lives = new HashSet<int>();
        foreach (int id in ShipId.All)
            for (int skin = 0; skin < ShipSkins.PerShip; skin++)
                lives.Add(FlyAndCheck(id, skin));
        Check("the matrix covers 2-, 3-, 4- and 5-life ships",
              lives.Contains(2) && lives.Contains(3) && lives.Contains(4) && lives.Contains(5));
        Cleanup();
        Debug.Log("[SG] " + passed + " quiet checks passed");
        Debug.Log("[SG] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------- the rig

    static void Prepare(int id, int skin)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        ShipSkins.ClearPreview();
        foreach (int s in ShipId.All)
        {
            PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(s));
            PlayerPrefs.DeleteKey(ShipSkins.DeveloperEquippedKey(s));
            for (int n = 0; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(s, n));
        }
        PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
        if (skin != ShipSkins.Stock) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, skin), 1);
        ShipSkins.Equip(id, skin);
        ShipId.Equip(id);
        ShipHullArt.ReleaseUnusedSkins();
        collisionDetection.lifeCounter = 0;
        collisionDetection.MAXLIFE = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.invTimer = 0f;
        buttonClicks.playerDied = false;
        // collisionDetection.Start hides the scene's boost holder; the next
        // ship's Start looks it up by tag again.
        if (GameObject.FindGameObjectWithTag("boost") == null) new GameObject("~boost").tag = "boost";
    }

    // Spawns and starts the real gameplay ship (null if it couldn't).
    static GameObject Spawn(int id, int skin)
    {
        Prepare(id, skin);
        var spawner = Object.FindFirstObjectByType<spawnShips>();
        if (spawner == null) spawner = new GameObject("~spawner").AddComponent<spawnShips>();
        var go = spawner.Spawn(id);
        if (go == null) return null;
        var life = go.GetComponent<lifeControler>();
        if (life == null) return go;
        TestHarness.Send(life, "Awake");
        TestHarness.Send(life, "Start");
        return go;
    }

    static void Frame(GameObject go)
    {
        var life = go.GetComponent<lifeControler>();
        TestHarness.Send(life, "Update");
        TestHarness.Send(life, "LateUpdate");
    }

    static void Hit(GameObject go)
    {
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.invTimer = 0f;
        PlayerInvuln.Reset();   // each hit lands after the last one's post-hit window
        var rock = new GameObject("~rock", typeof(CircleCollider2D));
        rock.tag = "Astr";
        rock.transform.position = go.transform.position + Vector3.up * .2f;
        Trigger.Invoke(go.GetComponent<collisionDetection>(), new object[] { rock.GetComponent<Collider2D>() });
        if (rock != null) Object.DestroyImmediate(rock);
    }

    static void Heal(GameObject go)
    {
        var atom = new GameObject(HealAtom.ObjectName + "(Clone)", typeof(CircleCollider2D));
        atom.tag = "pickUp";
        Trigger.Invoke(go.GetComponent<collisionDetection>(), new object[] { atom.GetComponent<Collider2D>() });
        if (atom != null) Object.DestroyImmediate(atom);
    }

    static void Cleanup()
    {
        foreach (var name in new[] { "~rock", "~boost", "~spawner" })
            for (var g = GameObject.Find(name); g != null; g = GameObject.Find(name)) Object.DestroyImmediate(g);
        collisionDetection.lifeCounter = 0;
        collisionDetection.MAXLIFE = 0;
        buttonClicks.playerDied = false;
        ShipSkins.ClearPreview();
        ShipHullArt.ReleaseUnusedSkins();
    }

    static void Despawn(GameObject go)
    {
        if (go != null) Object.DestroyImmediate(go);
        // anything the run left lying about (explosions, bursts)
        foreach (var r in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (r != null && r.parent == null && r.name.EndsWith("(Clone)") && r.GetComponent<collisionDetection>() == null &&
                (r.name.Contains("xp") || r.name.Contains("Burst") || r.name.Contains("plosion")))
                Object.DestroyImmediate(r.gameObject);
        collisionDetection.lifeCounter = 0;
    }

    // ------------------------------------------------------------- checks

    // Flies ship `id` in `skin` through every hit down to its last life and
    // heals it back; returns its max lives.
    static int FlyAndCheck(int id, int skin)
    {
        string who = id + " " + ShipId.NameOf(id) + " / " + ShipSkins.Get(id, skin).name;
        var go = Spawn(id, skin);
        if (go == null) { Check(who + ": spawned", false); return 0; }
        var life = go.GetComponent<lifeControler>();
        var sr = go.GetComponent<SpriteRenderer>();
        int max = collisionDetection.MAXLIFE;
        Check(who + ": flies with its ShipLives max (" + max + ")", max == ShipLives.Max(id) && max >= 1 && max <= ShipLives.Most);
        var animator = go.GetComponent<Animator>();
        Quiet(who + ": the prefab's 2016 animator can't overwrite the hull", animator == null || !animator.enabled);
        string sheet = skin == ShipSkins.Stock ? ShipId.KeyOf(id) : ShipSkins.SheetName(id, skin);

        var seenStates = new HashSet<int>();
        for (int hits = 0; hits < max; hits++)
        {
            if (hits > 0) Hit(go);
            Quiet(who + ": hit " + hits + " lands (lifeCounter " + collisionDetection.lifeCounter + ")",
                  collisionDetection.lifeCounter == hits && go != null);
            if (go == null) break;
            int state = ShipDamageTable.StateFor(hits, max);
            seenStates.Add(state);
            Flight(who + " hits " + hits, go, life, sr, id, skin, state, sheet);
        }
        bool wantsDamaged = max >= 3;
        bool wantsCritical = max >= 2;   // a 1-life ship dies on its first hit: only ever intact
        Check(who + ": " + max + " lives show " + (wantsDamaged ? "intact, damaged and critical" : wantsCritical ? "intact and critical" : "intact only"),
              seenStates.Contains(0) && seenStates.Contains(2) == wantsCritical && seenStates.Contains(1) == wantsDamaged);

        // heal back down
        for (int hits = max - 2; hits >= 0 && go != null; hits--)
        {
            Heal(go);
            Quiet(who + ": heal to " + hits, collisionDetection.lifeCounter == hits);
            Flight(who + " healed to " + hits, go, life, sr, id, skin, ShipDamageTable.StateFor(hits, max), sheet);
        }
        Quiet(who + ": healed back to intact", go != null && sr.sprite == ShipHullArt.Get(id, skin, 0, life.HullAnimator.Column));
        Despawn(go);
        return max;
    }

    // A stretch of flight in one damage state: the idle loop, a bank each
    // way (a spinner turns a full circle instead), the hit flash, with every
    // frame showing that state's drawing of the skin.
    static void Flight(string who, GameObject go, lifeControler life, SpriteRenderer sr, int id, int skin, int state,
                       string sheet)
    {
        var anim = life.HullAnimator;
        bool ok = true, sheetOk = true, damagedEverywhere = true;
        var columns = new HashSet<int>();
        void Look()
        {
            Frame(go);
            int col = anim.Column;
            columns.Add(col);
            ok &= sr.sprite == ShipHullArt.Get(id, skin, state, col);
            sheetOk &= sr.sprite != null && sr.sprite.texture != null && sr.sprite.texture.name == sheet;
            if (state > 0 && col != ShipHullArt.Hit) damagedEverywhere &= sr.sprite != ShipHullArt.Get(id, skin, 0, col);
        }
        int lifeNow = collisionDetection.lifeCounter;
        // idle loop (scaled time, 24 fps ticks)
        for (int t = 0; t < ShipHullArt.IdleLoopTicks + 2; t++) { anim.Step(1f / 24f, 1f / 24f, lifeNow); Look(); }
        bool spinner = ShipExhaust.UsesWind(id);
        if (!spinner)
        {
            foreach (float dir in new[] { 1f, -1f })
            {
                for (int k = 0; k < 4; k++)
                {
                    go.transform.position += Vector3.right * dir * .05f;
                    anim.Step(1f / 60f, 1f / 60f, lifeNow);
                    Look();
                }
                for (int k = 0; k < 12; k++) { anim.Step(1f / 60f, 1f / 60f, lifeNow); Look(); }
            }
            Quiet(who + ": banked both ways", columns.Contains(ShipHullArt.BankLeft) && columns.Contains(ShipHullArt.BankRight));
        }
        else
        {
            for (int deg = 0; deg < 360; deg += 30)
            {
                go.transform.rotation = Quaternion.Euler(0f, 0f, deg);
                anim.Step(1f / 60f, 1f / 60f, lifeNow);
                Look();
            }
            Quiet(who + ": a spinner never banks", !columns.Contains(ShipHullArt.BankLeft) && !columns.Contains(ShipHullArt.BankRight));
        }
        // the hit flash (a one-shot on the hull animator when damage rises)
        anim.Step(0f, 0f, lifeNow + 1);
        Look();
        anim.Step(1f / 60f, 1f, lifeNow);   // flash over
        anim.Step(0f, 0f, lifeNow);         // back in step with the real count
        Look();
        bool idleAll = true;
        for (int c = 0; c < ShipHullArt.IdleDrawings; c++) idleAll &= columns.Contains(c);
        Quiet(who + ": every idle drawing shown", idleAll);
        Check(who + ": state " + state + " drawn on every frame (idle, " + (spinner ? "spin" : "banks") + ", flash)", ok);
        Quiet(who + ": the hull is the skin's own sheet (" + sheet + ")", sheetOk);
        if (state > 0)
        {
            Check(who + ": no frame falls back to intact while damaged", damagedEverywhere);
            int diff = ShownDiff(ShipHullArt.Get(id, skin, state, 0), ShipHullArt.Get(id, skin, 0, 0));
            Check(who + ": state " + state + " drawn pixels differ from intact (" + diff + " px)", diff > (state == 1 ? 400 : 4000));
        }
    }

    // ------------------------------------------------------------- pixels

    // The sprite's pixels as the GPU samples them (skin sheets are
    // compressed and not CPU readable).
    public static Color32[] Shown(Sprite sprite, out int w, out int h)
    {
        var tex = sprite.texture;
        var r = sprite.textureRect;
        w = (int)r.width; h = (int)r.height;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
        read.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = read.GetPixels32();
        Object.DestroyImmediate(read);
        return px;
    }

    static int ShownDiff(Sprite a, Sprite b)
    {
        if (a == null || b == null) return 0;
        var pa = Shown(a, out _, out _);
        var pb = Shown(b, out _, out _);
        if (pa.Length != pb.Length) return int.MaxValue;
        int n = 0;
        for (int i = 0; i < pa.Length; i++)
        {
            if (pa[i].a < 128) continue;
            if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 48) n++;
        }
        return n;
    }

    // ------------------------------------------------------------- preview

    // -executeMethod SkinDamageGameplayTest.Preview -out <dir>
    // skindmg-contact.png: rows = ships, columns = skin x (intact, damaged,
    // critical) as the gameplay camera sees the flying ship, FX included.
    public static void Preview()
    {
        string dir = Arg("-out") ?? "Builds/skindamage";
        Directory.CreateDirectory(dir);
        int code = 0;
        try
        {
            using var sandbox = new TestHarness.Sandbox();
            EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
            const int Tile = 160;
            var ids = new List<int>(ShipId.All);
            var sheet = new Texture2D(Tile * ShipSkins.PerShip * 3, Tile * ids.Count, TextureFormat.RGBA32, false);
            var rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("~previewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(14, 16, 30, 255);
            cam.cullingMask = 1 << 31;
            cam.targetTexture = rt;
            for (int row = 0; row < ids.Count; row++)
            {
                int id = ids[row];
                for (int skin = 0; skin < ShipSkins.PerShip; skin++)
                {
                    var go = Spawn(id, skin);
                    if (go == null) continue;
                    go.transform.position = new Vector3(500f, 500f, 0f);
                    int max = collisionDetection.MAXLIFE;
                    int[] hitsFor = { 0, max >= 3 ? 1 : -1, max >= 2 ? max - 1 : -1 };   // a 1-life ship dies on its first hit
                    for (int state = 0; state < 3; state++)
                    {
                        int x = (skin * 3 + state) * Tile, y = (ids.Count - 1 - row) * Tile;
                        if (hitsFor[state] < 0)
                        {
                            Fill(sheet, x, y, Tile, new Color32(40, 20, 28, 255));   // a 2-life ship has no damaged state
                            continue;
                        }
                        while (collisionDetection.lifeCounter < hitsFor[state]) Hit(go);
                        go.transform.position = new Vector3(500f, 500f, 0f);
                        go.transform.rotation = Quaternion.identity;
                        var life = go.GetComponent<lifeControler>();
                        var fx = go.GetComponent<ShipDamageFx>();
                        for (int f = 0; f < 50; f++)
                        {
                            life.HullAnimator.Step(1f / 60f, 1f, collisionDetection.lifeCounter);
                            Frame(go);
                            if (fx != null) fx.Tick(1f / 60f);
                        }
                        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                        var sr = go.GetComponent<SpriteRenderer>();
                        var b = sr.bounds;
                        cam.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - 10f);
                        cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y) * 1.25f;
                        cam.Render();
                        RenderTexture.active = rt;
                        var read = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
                        read.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
                        read.Apply();
                        RenderTexture.active = null;
                        sheet.SetPixels32(x, y, Tile, Tile, read.GetPixels32());
                        Object.DestroyImmediate(read);
                    }
                    Despawn(go);
                }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "skindmg-contact.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(camGo);
            rt.Release();
            Cleanup();
            Debug.Log("[SG] preview written to " + dir);
        }
        catch (Exception e) { Debug.LogException(e); code = 1; }
        UnityEditor.EditorApplication.Exit(code);
    }

    static void Fill(Texture2D t, int x, int y, int size, Color32 c)
    {
        var px = new Color32[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels32(x, y, size, size, px);
    }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }
}
