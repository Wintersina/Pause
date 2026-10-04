using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Per-ship damage states and damage FX (ShipHullArt rows 1-2, ShipDamageTable,
// ShipDamageFx):
//   - every ship, stock and every skin, has damaged and critical art that
//     differs from intact and from each other, with intact's exact alpha
//   - each ship has emitters per state, on painted pixels, more when worse
//   - hits advance the state (with a debris burst), healing steps it back
//     and stops the emitters it added
//   - emitters follow the bank / bob pose and turn with a spinning hull
//   - nothing animates at timeScale 0; the particle pool is bounded, stays
//     near the hull, ducks the hearts / ship elements and allocates nothing
//   - the dock, title traffic and codex keep showing the intact hull
public static class ShipDamageTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SD] PASS  " : "[SD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        Mapping();
        foreach (int id in ShipId.All) Sheets(id);
        foreach (int id in ShipId.All) Emitters(id);
        IntactElsewhere();

        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        collisionDetection.MAXLIFE = 3;
        buttonClicks.playerDied = false;
        HitsAndHealing(ShipId.Starter);
        HitsAndHealing(11);   // Ninja, a spinner
        collisionDetection.MAXLIFE = 5;
        HitsAndHealing(7);    // a 5-life ship: damaged for 3 hits, critical on the last life
        collisionDetection.MAXLIFE = 3;
        Posing(ShipId.Starter);
        Spinning(11);
        Frozen(ShipId.Starter);
        Pool(13);
        Ducking(ShipId.Starter);
        Smoke(3);
        for (int max = 2; max <= 5; max++) Retardant(9, max);   // a hull with hearts
        RetardantFromGun(ShipId.Starter);                     // no hearts: the companion gun
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;

        Check("the old shared damage atlas is retired",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/ship_damage_fx_atlas.png") == null);
        Check("the damage FX atlas loads, retardant rows too", ShipDamageFx.Frame(ShipDamageFx.RowSmoke, 0) != null &&
              ShipDamageFx.Frame(ShipDamageFx.RowFoam, 3) != null && ShipDamageFx.Frame(ShipDamageFx.RowSpray, 3) != null);

        Debug.Log("[SD] failures: " + fails);
        return fails;
    }

    static string Label(int id) { return id + " " + ShipId.NameOf(id); }

    const int Intact = 0, Damaged = 1;
    static int Critical { get { return ShipDamageTable.States - 1; } }

    static void Mapping()
    {
        Check("three sheet states: intact, damaged, critical", ShipHullArt.States == ShipDamageTable.States && Critical == 2);
        bool byLivesLeft = true;
        for (int max = 2; max <= 5; max++)
        {
            byLivesLeft &= ShipDamageTable.StateFor(0, max) == Intact && ShipDamageTable.StateFor(-1, max) == Intact;
            byLivesLeft &= ShipDamageTable.StateFor(max - 1, max) == Critical;   // the last life
            byLivesLeft &= ShipDamageTable.StateFor(max, max) == Critical;       // a fatal hit clamps
            for (int hits = 1; hits < max - 1; hits++) byLivesLeft &= ShipDamageTable.StateFor(hits, max) == Damaged;
        }
        Check("state by lives left for 2-5 lives: full -> intact, last life -> critical, between -> damaged",
              byLivesLeft);
        int saved = collisionDetection.MAXLIFE;
        collisionDetection.MAXLIFE = 5;
        Check("the one-arg StateFor reads the ship's max lives (5 lives: 3 hits still damaged)",
              ShipDamageTable.MaxLives() == 5 && ShipDamageTable.StateFor(3) == Damaged && ShipDamageTable.StateFor(4) == Critical);
        collisionDetection.MAXLIFE = saved;
    }

    // lifeCounter for a state on a ship with `max` lives.
    static int HitsFor(int state, int max)
    {
        return state == Intact ? 0 : state == Damaged ? 1 : max - 1;
    }

    static void SetState(int state) { collisionDetection.lifeCounter = HitsFor(state, ShipDamageTable.MaxLives()); }

    // ------------------------------------------------------------- sheets

    static Texture2D Decode(byte[] png)
    {
        if (png == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return t.LoadImage(png) ? t : null;
    }

    static void Sheets(int id)
    {
        string who = Label(id);
        string path = AssetDatabase.GetAssetPath(ShipHullArt.SheetFor(id));
        var stock = File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
        if (stock == null) { Check(who + " stock sheet readable", false); return; }
        try
        {
            Color32[] stockPx = stock.GetPixels32();
            SheetStates(who + " stock", stock, stockPx, null);
            for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
            {
                var tex = Decode(ShipHullArt.SkinPng(id, skin));
                if (tex == null) { Check(who + " skin " + skin + " readable", false); continue; }
                try { SheetStates(who + " skin " + ShipSkins.Get(id, skin).name, tex, tex.GetPixels32(), stockPx); }
                finally { Object.DestroyImmediate(tex); }
            }
        }
        finally { Object.DestroyImmediate(stock); }
    }

    // Rows in the png run top down; GetPixels32 runs bottom up.
    static void SheetStates(string who, Texture2D tex, Color32[] px, Color32[] stockPx)
    {
        int w = tex.width, cell = ShipHullArt.Cell;
        if (tex.height != cell * ShipHullArt.States) { Check(who + " sheet has 3 states", false); return; }
        int Row(int state) { return ShipHullArt.States - 1 - state; }   // bottom-up row
        bool alphaSame = true, stockAlpha = true;
        var changed = new int[3];   // 0: 1 vs 0, 1: 2 vs 0, 2: 2 vs 1 (non-flash columns)
        int opaque = 0;
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < w; x++)
            {
                bool flash = x / cell == ShipHullArt.Hit;
                var c0 = px[(Row(0) * cell + y) * w + x];
                var c1 = px[(Row(1) * cell + y) * w + x];
                var c2 = px[(Row(2) * cell + y) * w + x];
                alphaSame &= c0.a == c1.a && c0.a == c2.a;
                if (stockPx != null)
                    for (int s = 0; s < 3; s++)
                        stockAlpha &= px[(Row(s) * cell + y) * w + x].a == stockPx[(Row(s) * cell + y) * w + x].a;
                if (flash || c0.a < 250) continue;
                opaque++;
                if (!Same(c0, c1)) changed[0]++;
                if (!Same(c0, c2)) changed[1]++;
                if (!Same(c1, c2)) changed[2]++;
            }
        Check(who + ": damaged and critical keep intact's exact alpha", alphaSame);
        if (stockPx != null) Check(who + ": alpha identical to the stock sheet", stockAlpha);
        // per drawing: at least ~1% of a 256 px cell repainted at each step
        int min = 8 * 600;
        Check(who + ": damaged differs from intact (" + changed[0] + " px)", changed[0] > min);
        Check(who + ": critical differs from damaged (" + changed[2] + " px) and is worse than it",
              changed[2] > min && changed[1] > changed[0]);
        // the last life is a wreck: well over twice the damaged hull's
        // repainted area and at least a fifth of the whole hull
        float d1 = changed[0] / (float)Mathf.Max(1, opaque), d2 = changed[1] / (float)Mathf.Max(1, opaque);
        Check(who + ": critical is far more damaged (" + (d1 * 100f).ToString("0") + "% -> " + (d2 * 100f).ToString("0") +
              "% of the hull repainted)", d2 >= .2f && changed[1] >= changed[0] * 2.5f);
    }

    static bool Same(Color32 a, Color32 b)
    {
        return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) <= 24;
    }

    // ------------------------------------------------------------- emitters

    static void Emitters(int id)
    {
        string who = Label(id);
        Check(who + " has damage emitters", ShipDamageTable.Has(id) && ShipDamageTable.Total(id) > 0);
        int c0 = ShipDamageTable.Count(id, 0), c1 = ShipDamageTable.Count(id, 1), c2 = ShipDamageTable.Count(id, 2);
        Check(who + " emitters: none intact, some damaged, more critical (" + c0 + "/" + c1 + "/" + c2 + ")",
              c0 == 0 && c1 >= 2 && c2 > c1 && c2 == ShipDamageTable.Total(id));
        var kinds = new HashSet<DamageEmitterKind>();
        for (int i = 0; i < c2; i++) kinds.Add(ShipDamageTable.Get(id, i).kind);
        Check(who + " critical mixes sparks, arc, smoke, flame, leak and smolder (" + kinds.Count + " kinds)", kinds.Count == 6);
        int smoke1 = 0, smoke2 = 0;
        for (int i = 0; i < c2; i++)
        {
            var k = ShipDamageTable.Get(id, i).kind;
            if (k != DamageEmitterKind.Smoke && k != DamageEmitterKind.Smolder) continue;
            smoke2++;
            if (i < c1) smoke1++;
        }
        Check(who + " smoke from the damaged spots, not just the engine (" + smoke1 + " damaged, " + smoke2 + " critical)",
              smoke1 >= 2 && smoke2 >= smoke1 + 3);
        DamageEmitter burst;
        Check(who + " each worse state has a burst point",
              ShipDamageTable.BurstAt(id, 1, out burst) && burst.state == 1 &&
              ShipDamageTable.BurstAt(id, 2, out burst) && burst.state == 2 && !ShipDamageTable.BurstAt(id, 0, out burst));

        // every point on painted pixels of the frame it's drawn on, every pose
        string path = AssetDatabase.GetAssetPath(ShipHullArt.SheetFor(id));
        var sheet = File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
        if (sheet == null) { Check(who + " sheet readable", false); return; }
        try
        {
            bool onPaint = true;
            int cell = ShipHullArt.Cell;
            for (int i = 0; i < c2; i++)
            {
                var e = ShipDamageTable.Get(id, i);
                for (int col = 0; col < ShipHullArt.Columns; col++)
                {
                    Vector2 p = ShipDamageTable.Posed(e.u, e.v, ShipExhaust.UsesWind(id) && (col == 6 || col == 7) ? 0 : col);
                    for (int s = e.state; s < ShipHullArt.States; s++)
                    {
                        int x = col * cell + (int)(p.x * 2f);
                        int y = (ShipHullArt.States - 1 - s) * cell + (cell - 1 - (int)(p.y * 2f));
                        if (sheet.GetPixel(x, y).a < .8f) { onPaint = false; Debug.Log("[SD] off paint " + who + " #" + i + " col " + col); }
                    }
                }
                // the local point lands inside the hull sprite's rect
                Vector2 local = ShipDamageTable.Local(id, e, 0);
                var b = ShipHullArt.Rest(id).bounds;
                onPaint &= b.Contains(new Vector3(local.x, local.y, b.center.z));
            }
            Check(who + " every emitter sits on painted hull pixels in every pose", onPaint);
        }
        finally { Object.DestroyImmediate(sheet); }
    }

    static void IntactElsewhere()
    {
        bool ok = true;
        foreach (int id in ShipId.All)
            ok &= shopingShips.SpriteFor(id) == ShipHullArt.Get(id, 0, 0) && ShipHullArt.StockRest(id) == ShipHullArt.Get(id, ShipSkins.Stock, 0, 0);
        Check("dock / title traffic / codex art is the intact row", ok);
    }

    // ------------------------------------------------------------- runtime

    static GameObject Fly(int id, out ShipDamageFx fx, out lifeControler life)
    {
        collisionDetection.lifeCounter = 0;
        var go = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        life = go.AddComponent<lifeControler>();
        life.SendMessage("Start");
        fx = go.GetComponent<ShipDamageFx>();
        if (fx != null) fx.Build();
        return go;
    }

    static int EnabledEmitters(ShipDamageFx fx)
    {
        int n = 0;
        for (int i = 0; i < fx.EmitterCount; i++) if (fx.EmitterRenderer(i).enabled) n++;
        return n;
    }

    static void Run(ShipDamageFx fx, lifeControler life, float seconds)
    {
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            life.SendMessage("Update");
            fx.Tick(1f / 60f);
        }
    }

    static void HitsAndHealing(int id)
    {
        string who = Label(id);
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        Check(who + " the live ship gets the damage FX", fx != null);
        if (fx == null) { Object.DestroyImmediate(go); return; }
        var hull = go.GetComponent<SpriteRenderer>();
        Check(who + " one emitter renderer per table emitter, pooled up front",
              fx.EmitterCount == ShipDamageTable.Total(id) && fx.PoolSize == ShipDamageFx.MaxParticles);

        Run(fx, life, .5f);
        Check(who + " intact: no damage FX", fx.State == 0 && fx.ActiveParticles == 0 && EnabledEmitters(fx) == 0);

        SetState(Damaged);
        Run(fx, life, 1f / 60f);
        Check(who + " a hit: damaged state and art", fx.State == 1 && hull.sprite.name.Contains("_1_"));
        Check(who + " the hit sprays a debris burst", fx.Bursts == 1 && fx.ActiveParticles >= 8);
        Run(fx, life, 1.5f);
        int live1 = fx.LiveEmitters;

        SetState(Critical);
        Run(fx, life, 1f / 60f);
        Check(who + " a second hit: critical state and art, another burst",
              fx.State == 2 && hull.sprite.name.Contains("_2_") && fx.Bursts == 2);
        Check(who + " more emitters on the last life (" + live1 + " -> " + fx.LiveEmitters + ")", fx.LiveEmitters > live1);
        bool flameSeen = false;
        for (float t = 0f; t < 1.5f; t += 1f / 60f)
        {
            Run(fx, life, 1f / 60f);
            for (int i = live1; i < fx.EmitterCount; i++) flameSeen |= fx.EmitterRenderer(i).enabled;
        }
        Check(who + " the critical emitters draw", flameSeen);

        SetState(Damaged);   // green atom / Mending
        Run(fx, life, 1f / 60f);
        bool stopped = true;
        for (int i = live1; i < fx.EmitterCount; i++) stopped &= !fx.EmitterRenderer(i).enabled;
        Check(who + " healing steps back to damaged, no burst, and its emitters stop",
              fx.State == 1 && fx.Bursts == 2 && stopped && hull.sprite.name.Contains("_1_"));
        for (float t = 0f; t < 1.5f; t += 1f / 60f)
        {
            Run(fx, life, 1f / 60f);
            for (int i = live1; i < fx.EmitterCount; i++) stopped &= !fx.EmitterRenderer(i).enabled;
        }
        Check(who + " the healed emitters stay off", stopped);

        collisionDetection.lifeCounter = 0;
        Run(fx, life, 1.5f);
        Check(who + " fully healed: every emitter and particle gone",
              fx.State == 0 && EnabledEmitters(fx) == 0 && fx.ActiveParticles == 0);
        Object.DestroyImmediate(go);
    }

    static void Posing(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        go.transform.localScale = Vector3.one * 2f;
        bool follows = true, leans = false;
        for (int i = 0; i < ShipDamageTable.Total(id); i++)
        {
            var e = ShipDamageTable.Get(id, i);
            for (int col = 0; col < ShipHullArt.Columns; col++)
            {
                Vector2 l = ShipDamageTable.Local(id, e, col);
                follows &= (fx.EmitterWorld(i, col) - go.transform.TransformPoint(l)).sqrMagnitude < 1e-8f;
            }
            leans |= (fx.EmitterWorld(i, ShipHullArt.BankLeft) - fx.EmitterWorld(i, 0)).sqrMagnitude > 1e-6f &&
                     (fx.EmitterWorld(i, ShipHullArt.BankRight) - fx.EmitterWorld(i, ShipHullArt.BankLeft)).sqrMagnitude > 1e-6f;
        }
        Check("emitters ride the hull transform for every pose", follows);
        Check("emitters lean with the bank poses", leans);
        var bob = ShipDamageTable.Local(id, 64f, 64f, 1) - ShipDamageTable.Local(id, 64f, 64f, 0);
        Check("emitters bob with the idle drawings", bob.y > 0f && Mathf.Abs(bob.x) < 1e-5f);

        // the hull's own bank pose drives it
        SetState(Critical);
        life.SendMessage("Update");
        fx.Tick(1f / 60f);
        int column = life.HullAnimator.Column;
        Check("the FX read the hull animator's pose", column >= 0 && column < ShipHullArt.Columns);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    static void Spinning(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        SetState(Critical);
        Run(fx, life, .2f);
        int i = ShipDamageTable.Count(id, 1);   // a critical emitter
        Vector3 a = fx.EmitterWorld(i, 0) - go.transform.position;
        go.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        Vector3 b = fx.EmitterWorld(i, 0) - go.transform.position;
        Vector3 turned = Quaternion.Euler(0f, 0f, 90f) * a;
        Check("a spinning hull carries its emitters round", (b - turned).sqrMagnitude < 1e-8f && a.sqrMagnitude > 1e-6f);
        fx.Tick(1f / 60f);
        bool placed = true;
        for (int k = 0; k < ShipDamageTable.Count(id, 2); k++)
        {
            var sr = fx.EmitterRenderer(k);
            Vector3 want = fx.EmitterWorld(k, life.HullAnimator.Column);
            placed &= new Vector2(sr.transform.position.x - want.x, sr.transform.position.y - want.y).sqrMagnitude < 1e-8f;
        }
        Check("the emitter renderers sit on their spinning points", placed);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    static void Frozen(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        SetState(Critical);
        Run(fx, life, .4f);
        var pos = new Vector3[fx.PoolSize];
        var spr = new Sprite[fx.PoolSize + fx.EmitterCount];
        for (int i = 0; i < fx.PoolSize; i++) { pos[i] = fx.ParticlePosition(i); spr[i] = fx.ParticleRenderer(i).sprite; }
        for (int i = 0; i < fx.EmitterCount; i++) spr[fx.PoolSize + i] = fx.EmitterRenderer(i).sprite;
        int active = fx.ActiveParticles;
        for (int k = 0; k < 120; k++) fx.Tick(0f);
        bool still = fx.ActiveParticles == active;
        for (int i = 0; i < fx.PoolSize; i++) still &= pos[i] == fx.ParticlePosition(i) && spr[i] == fx.ParticleRenderer(i).sprite;
        for (int i = 0; i < fx.EmitterCount; i++) still &= spr[fx.PoolSize + i] == fx.EmitterRenderer(i).sprite;
        Check("at timeScale 0 (dt 0) no FX frame, motion or emission advances (" + active + " particles)",
              still && active > 0);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    static void Pool(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        AddHearts(go);   // so the retardant runs in the pool too
        int children = go.GetComponentsInChildren<Transform>(true).Length;
        SetState(Critical);
        int peak = 0;
        bool near = true;
        float reach = ShipDamageFx.MaxReach * fx.HullSize + 1e-4f;
        Run(fx, life, .5f);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 600; k++)
        {
            fx.Tick(1f / 60f);
            peak = Mathf.Max(peak, fx.ActiveParticles);
            for (int i = 0; i < fx.PoolSize; i++)
                if (fx.ParticleAlive(i))
                {
                    var d = fx.ParticlePosition(i) - fx.ParticleOrigin(i);
                    bool retardant = fx.ParticleRow(i) == ShipDamageFx.RowSpray || fx.ParticleRow(i) == ShipDamageFx.RowFoam;
                    float r = retardant ? ShipDamageFx.SprayReach * fx.HullSize + 1e-4f : reach;
                    near &= fx.ParticleReach(i) <= r && new Vector2(d.x, d.y).magnitude <= r;
                }
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        // and a burst at the pool's peak: heal and hit again
        SetState(Damaged); fx.Tick(1f / 60f);
        SetState(Critical); fx.Tick(1f / 60f);
        peak = Mathf.Max(peak, fx.ActiveParticles);
        Check("the particle pool is bounded (peak " + peak + " of " + ShipDamageFx.MaxParticles + ")",
              peak <= ShipDamageFx.MaxParticles && fx.PoolSize == ShipDamageFx.MaxParticles);
        Check("nothing is created while it runs",
              go.GetComponentsInChildren<Transform>(true).Length == children);
        Check("particles stay within " + ShipDamageFx.MaxReach + " hull lengths of their emitter (the retardant " +
              ShipDamageFx.SprayReach + ")", near);
        Check("a running frame allocates nothing (" + allocated + " B over 600 ticks)", allocated == 0);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    static void Ducking(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        SetState(Critical);
        Run(fx, life, .4f);
        var owner = new GameObject("FakeMeter");
        // an element drawn over the whole ship and around it
        ShipUiSlots.Register(go.transform, owner, () => new Bounds(go.transform.position, Vector3.one * 10f));
        bool hidden = true;
        for (int k = 0; k < 30; k++)
        {
            fx.Tick(1f / 60f);
            for (int i = 0; i < fx.PoolSize; i++) hidden &= !fx.ParticleRenderer(i).enabled;
            for (int i = 0; i < fx.EmitterCount; i++) hidden &= !fx.EmitterRenderer(i).enabled;
        }
        Check("nothing draws over a ship element (meter / hearts)", hidden && fx.ActiveParticles > 0);
        ShipUiSlots.Unregister(owner);
        Object.DestroyImmediate(owner);
        bool back = false;
        for (int k = 0; k < 30; k++)
        {
            fx.Tick(1f / 60f);
            for (int i = 0; i < fx.PoolSize; i++) back |= fx.ParticleRenderer(i).enabled;
        }
        Check("they draw again once it's gone", back);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    // ------------------------------------------------------------- smoke

    // Average live smoke puffs (engine + smolder) over a few seconds.
    static float SmokeLevel(ShipDamageFx fx, lifeControler life, int state)
    {
        SetState(state);
        Run(fx, life, 1f);
        int sum = 0, n = 0;
        for (float t = 0f; t < 3f; t += 1f / 60f)
        {
            life.SendMessage("Update");
            fx.Tick(1f / 60f);
            sum += fx.ActiveOfRow(ShipDamageFx.RowSmoke);
            n++;
        }
        return sum / (float)n;
    }

    static void Smoke(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        float damaged = SmokeLevel(fx, life, Damaged);
        float critical = SmokeLevel(fx, life, Critical);
        Check(Label(id) + " smoke rises with the damage (" + damaged.ToString("0.0") + " -> " + critical.ToString("0.0") +
              " puffs)", damaged >= 4f && critical >= damaged * 2f);
        // and comes out of the wounds: every smolder emitter puts out puffs at its own spot
        int live = fx.LiveEmitters, spots = 0, served = 0;
        var near = new bool[fx.EmitterCount];
        for (float t = 0f; t < 2f; t += 1f / 60f)
        {
            life.SendMessage("Update");
            fx.Tick(1f / 60f);
            int column = life.HullAnimator.Column;
            for (int p = 0; p < fx.PoolSize; p++)
            {
                if (!fx.ParticleAlive(p) || fx.ParticleRow(p) != ShipDamageFx.RowSmoke) continue;
                for (int i = 0; i < live; i++)
                {
                    if (ShipDamageTable.Get(id, i).kind != DamageEmitterKind.Smolder) continue;
                    var d = fx.ParticleOrigin(p) - fx.EmitterWorld(i, column);
                    if (new Vector2(d.x, d.y).magnitude < fx.HullSize * .05f) near[i] = true;
                }
            }
        }
        for (int i = 0; i < live; i++)
            if (ShipDamageTable.Get(id, i).kind == DamageEmitterKind.Smolder) { spots++; if (near[i]) served++; }
        Check(Label(id) + " every wound smolders (" + served + " of " + spots + ")", spots >= 3 && served == spots);
        // readable: it never fills the pool
        Check(Label(id) + " the smoke leaves room in the pool (" + fx.ActiveParticles + ")",
              fx.ActiveParticles < ShipDamageFx.MaxParticles * 3 / 4);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    // ------------------------------------------------------------- retardant

    static ShipLivesIndicator AddHearts(GameObject go)
    {
        var hearts = go.AddComponent<ShipLivesIndicator>();
        hearts.BuildHearts();
        return hearts;
    }

    // One frame of the ship, its hearts and its FX, in execution order.
    static void Frame(ShipDamageFx fx, lifeControler life, ShipLivesIndicator hearts)
    {
        life.SendMessage("Update");
        if (hearts != null)
        {
            hearts.SendMessage("Update");
            hearts.Place(1f / 60f, 1f / 60f);
        }
        fx.Tick(1f / 60f);
    }

    static bool Hot(DamageEmitterKind k) { return k != DamageEmitterKind.Leak; }

    // Sprays started over `seconds`, checking every spray frame aims right.
    static int Watch(ShipDamageFx fx, lifeControler life, ShipLivesIndicator hearts, float seconds,
                     ref bool aimed, ref bool fromHeart, ref bool landed, ref bool calmed, ref bool foam)
    {
        int start = fx.Sprays;
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            Frame(fx, life, hearts);
            for (int p = 0; p < fx.PoolSize; p++)
                if (fx.ParticleAlive(p) && fx.ParticleRow(p) == ShipDamageFx.RowFoam) foam = true;
            int target = fx.SprayTarget;
            if (target < 0) continue;
            int column = life.HullAnimator.Column;
            Vector3 to = fx.EmitterWorld(target, column);
            aimed &= target < fx.LiveEmitters && Hot(ShipDamageTable.Get(fx.ShipIdShown, target).kind);
            if (hearts != null)
            {
                int h = fx.SprayHeart;
                fromHeart &= h >= 0 && hearts.Hearts[h].gameObject.activeSelf &&
                             (fx.SprayFrom - hearts.Hearts[h].position).sqrMagnitude < 1e-6f;
            }
            Vector3 dir = to - fx.SprayFrom;
            dir.z = 0f;
            for (int p = 0; p < fx.PoolSize; p++)
            {
                if (!fx.ParticleAlive(p)) continue;
                int row = fx.ParticleRow(p);
                if (row == ShipDamageFx.RowSpray && fx.ParticleVelocity(p).sqrMagnitude > 1e-8f &&
                    (fx.ParticleOrigin(p) - fx.SprayFrom).sqrMagnitude < (fx.HullSize * .06f) * (fx.HullSize * .06f))
                {
                    var v = fx.ParticleVelocity(p);
                    v.z = 0f;
                    aimed &= Vector3.Dot(v.normalized, dir.normalized) > .95f;
                }
                if (row == ShipDamageFx.RowFoam)
                {
                    var o = fx.ParticleOrigin(p) - to;
                    landed |= new Vector2(o.x, o.y).magnitude < fx.HullSize * .08f;
                }
            }
            if (fx.Doused(target) > 0f) calmed = true;
        }
        return fx.Sprays - start;
    }

    static void Retardant(int id, int maxLives)
    {
        collisionDetection.MAXLIFE = maxLives;
        string who = Label(id) + " (" + maxLives + " hearts)";
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        var hearts = AddHearts(go);
        Check(who + " has its hearts", hearts.Hearts != null && hearts.Hearts.Length == maxLives);

        bool aimed = true, fromHeart = true, landed = false, calmed = false, foam = false;
        int intact = Watch(fx, life, hearts, 3f, ref aimed, ref fromHeart, ref landed, ref calmed, ref foam);
        Check(who + " intact: no retardant", intact == 0 && !fx.Spraying && !foam);

        int damaged = 0;
        if (maxLives > 2)
        {
            SetState(Damaged);
            damaged = Watch(fx, life, hearts, 10f, ref aimed, ref fromHeart, ref landed, ref calmed, ref foam);
            Check(who + " damaged: the hearts spray retardant (" + damaged + " sprays in 10 s)", damaged >= 2);
        }
        SetState(Critical);
        int critical = Watch(fx, life, hearts, 10f, ref aimed, ref fromHeart, ref landed, ref calmed, ref foam);
        Check(who + " critical: sprays more often (" + damaged + " -> " + critical + " in 10 s)",
              critical >= 4 && critical > damaged);
        Check(who + " every spray aims at a live hot spot, droplets flying straight at it", aimed);
        Check(who + " the spray comes from a shown heart", fromHeart);
        Check(who + " foam lands on the spot and calms it", foam && landed && calmed);

        // fresh from intact, the first target is the worst spot: the flame
        int flame = -1;
        for (int i = 0; i < ShipDamageTable.Count(id, Critical); i++)
            if (ShipDamageTable.Get(id, i).kind == DamageEmitterKind.Flame) flame = i;
        collisionDetection.lifeCounter = 0;
        for (int k = 0; k < 240; k++) Frame(fx, life, hearts);
        SetState(Critical);
        for (float t = 0f; t < 4f && fx.SprayTarget < 0; t += 1f / 60f) Frame(fx, life, hearts);
        Check(who + " the worst spot (the flame) is foamed first", flame >= 0 && fx.SprayTarget == flame);
        // and a foamed flame dies down
        float wild = 0f, doused = float.MaxValue;
        for (float t = 0f; t < 6f; t += 1f / 60f)
        {
            Frame(fx, life, hearts);
            var sr = fx.EmitterRenderer(flame);
            if (!sr.enabled) continue;
            float s = sr.transform.localScale.x;
            if (fx.Doused(flame) > .5f) doused = Mathf.Min(doused, s); else if (fx.Doused(flame) <= 0f) wild = Mathf.Max(wild, s);
        }
        Check(who + " a foamed flame dies down (" + doused.ToString("0.00") + " vs " + wild.ToString("0.00") + ")",
              wild > 0f && doused < wild * .6f);

        // no garbage while spraying
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 300; k++) fx.Tick(1f / 60f);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check(who + " spraying allocates nothing (" + allocated + " B)", allocated == 0);

        // frozen: the spray holds
        for (float t = 0f; t < 4f && !fx.Spraying; t += 1f / 60f) Frame(fx, life, hearts);
        int sprays = fx.Sprays, target0 = fx.SprayTarget;
        int bits = fx.ActiveOfRow(ShipDamageFx.RowFoam) + fx.ActiveOfRow(ShipDamageFx.RowSpray);
        for (int k = 0; k < 120; k++) fx.Tick(0f);
        Check(who + " at timeScale 0 the spray holds still",
              fx.Spraying && fx.Sprays == sprays && fx.SprayTarget == target0 &&
              fx.ActiveOfRow(ShipDamageFx.RowFoam) + fx.ActiveOfRow(ShipDamageFx.RowSpray) == bits);

        // healed to intact: it stops
        collisionDetection.lifeCounter = 0;
        Frame(fx, life, hearts);
        bool stopped = !fx.Spraying;
        int after = fx.Sprays;
        for (float t = 0f; t < 5f; t += 1f / 60f) { Frame(fx, life, hearts); stopped &= !fx.Spraying; }
        Check(who + " healed to intact: the spraying stops and the foam clears",
              stopped && fx.Sprays == after && fx.ActiveOfRow(ShipDamageFx.RowFoam) == 0 &&
              fx.ActiveOfRow(ShipDamageFx.RowSpray) == 0);
        Object.DestroyImmediate(go);
        collisionDetection.MAXLIFE = 3;
    }

    static void RetardantFromGun(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        var gun = UltimateGun.Attach(go);
        if (gun.transform.childCount == 0) gun.SendMessage("Awake");
        SetState(Critical);
        bool aimed = true, fromHeart = true, landed = false, calmed = false, foam = false;
        int n = Watch(fx, life, null, 8f, ref aimed, ref fromHeart, ref landed, ref calmed, ref foam);
        Check(Label(id) + " no hearts: the companion gun sprays instead (" + n + " sprays)",
              n >= 3 && aimed && foam && landed && calmed);
        bool fromGun = true;
        for (float t = 0f; t < 4f; t += 1f / 60f)
        {
            Frame(fx, life, null);
            if (fx.Spraying) fromGun &= fx.SprayHeart < 0 && (fx.SprayFrom - gun.transform.position).sqrMagnitude < 1e-6f;
        }
        Check(Label(id) + " the gun's spray leaves from the gun", fromGun);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }
}
