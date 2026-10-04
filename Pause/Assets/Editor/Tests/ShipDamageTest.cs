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
        Posing(ShipId.Starter);
        Spinning(11);
        Frozen(ShipId.Starter);
        Pool(13);
        Ducking(ShipId.Starter);
        collisionDetection.lifeCounter = 0;

        Check("the old shared damage atlas is retired",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/ship_damage_fx_atlas.png") == null);
        Check("the damage FX atlas loads", ShipDamageFx.Frame(ShipDamageFx.RowSmoke, 0) != null);

        Debug.Log("[SD] failures: " + fails);
        return fails;
    }

    static string Label(int id) { return id + " " + ShipId.NameOf(id); }

    static void Mapping()
    {
        Check("every ship has MAXLIFE 3: 3 sheet states", ShipHullArt.States == 3 && ShipDamageTable.States == 3);
        Check("no hits -> intact, 1 hit -> damaged, 2 hits (last life) -> critical",
              ShipDamageTable.StateFor(0) == 0 && ShipDamageTable.StateFor(1) == 1 && ShipDamageTable.StateFor(2) == 2);
        Check("a fatal hit clamps to critical", ShipDamageTable.StateFor(3) == 2 && ShipDamageTable.StateFor(-1) == 0);
    }

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
        Check(who + " critical mixes sparks, arc, smoke, flame and leak (" + kinds.Count + " kinds)", kinds.Count == 5);
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

        collisionDetection.lifeCounter = 1;
        Run(fx, life, 1f / 60f);
        Check(who + " a hit: damaged state and art", fx.State == 1 && hull.sprite.name.Contains("_1_"));
        Check(who + " the hit sprays a debris burst", fx.Bursts == 1 && fx.ActiveParticles >= 8);
        Run(fx, life, 1.5f);
        int live1 = fx.LiveEmitters;

        collisionDetection.lifeCounter = 2;
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

        collisionDetection.lifeCounter = 1;   // green atom / Mending
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
        collisionDetection.lifeCounter = 2;
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
        collisionDetection.lifeCounter = 2;
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
        collisionDetection.lifeCounter = 2;
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
        int children = go.GetComponentsInChildren<Transform>(true).Length;
        collisionDetection.lifeCounter = 2;
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
                    near &= new Vector2(d.x, d.y).magnitude <= reach;
                }
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        // and a burst at the pool's peak: heal and hit again
        collisionDetection.lifeCounter = 1; fx.Tick(1f / 60f);
        collisionDetection.lifeCounter = 2; fx.Tick(1f / 60f);
        peak = Mathf.Max(peak, fx.ActiveParticles);
        Check("the particle pool is bounded (peak " + peak + " of " + ShipDamageFx.MaxParticles + ")",
              peak <= ShipDamageFx.MaxParticles && fx.PoolSize == ShipDamageFx.MaxParticles);
        Check("nothing is created while it runs",
              go.GetComponentsInChildren<Transform>(true).Length == children);
        Check("particles stay within " + ShipDamageFx.MaxReach + " hull lengths of their emitter", near);
        Check("a running frame allocates nothing (" + allocated + " B over 600 ticks)", allocated == 0);
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    static void Ducking(int id)
    {
        ShipDamageFx fx; lifeControler life;
        var go = Fly(id, out fx, out life);
        collisionDetection.lifeCounter = 2;
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
}
