using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Enemy pooling (EnemyFactory over PrefabPool) and the per-frame fixes that ride with it (perf/enemy-pool):
//   1  a reused enemy equals a freshly built one: components, hearts, identity, colour, scale, collider, body, layer, tag, name;
//      nothing a weapon or the weather added to the last life survives (a stun, a tint, a marker)
//   2  a released enemy is out of every live list (ClearTarget.Live, SpawnSpace) and inactive
//   3  the pool is bounded (PrefabPool.MaxFreePerKey) and a run end / scene load clears it
//   4  what is not pooled stays plain: mines, chasers, anything built with pooled: false (elites, the tutorial)
//   5  ShipTargets.View is read once per frame; the weather's per-body lookups are cached and invalidated on reuse;
//      the stun refreshes a frozen body in place; the floatie bullet pool counts instead of scanning
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites EnemyPoolTest
public static class EnemyPoolTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ENEMYPOOL] PASS  " : "[ENEMYPOOL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var made = new List<Object>();
        PrefabPool.ClearAndDestroy();
        PrefabPool.ForceInEditMode = true;
        PrefabPool.ForceDeferred = false;
        try
        {
            ReusedEqualsFresh();
            ReleasedAreUnregistered();
            PoolIsBoundedAndClears();
            NonPooledStayPlain();
            RocksKeepTheirSize();
            ViewIsReadOncePerFrame(made);
            WeatherBodyCache();
            FloatieFreeList();
            LifeAndFollowers();
            ReuseCooldown();
            SourceAudit();
        }
        finally
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            PrefabPool.ClearAndDestroy();
            PrefabPool.ForceInEditMode = false;
            PrefabPool.ForceDeferred = false;
            ShipTargets.ForceViewCache = false;
            PrefabPool.CooldownClock = null;
            ShipTargets.ResetViewCache();
            EnemyHeartsView.ResetLive();
        }
        Debug.Log("[ENEMYPOOL] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------

    static EnemyDef Find(System.Predicate<EnemyDef> pick)
    {
        foreach (var d in EnemyRoster.All) if (pick(d)) return d;
        return null;
    }

    // Everything that makes an enemy "the same enemy" apart from what is rolled at random (art step, armed chance, lane clocks).
    static string Fingerprint(GameObject go)
    {
        var sb = new StringBuilder();
        var types = new List<string>();
        foreach (var c in go.GetComponentsInChildren<Component>(true)) if (c != null && !(c is PooledObject)) types.Add(c.GetType().Name);
        types.Sort();
        sb.Append(string.Join(",", types)).Append('|');
        sb.Append(go.name).Append('|').Append(go.tag).Append('|').Append(go.layer).Append('|').Append(go.activeSelf).Append('|');
        sb.Append(go.transform.localScale).Append('|').Append(go.transform.childCount).Append('|');
        var sr = go.GetComponent<SpriteRenderer>();
        sb.Append(sr.color).Append(sr.flipX).Append(sr.flipY).Append(sr.sortingOrder).Append(sr.enabled).Append('|');
        var col = go.GetComponent<BoxCollider2D>();
        sb.Append(col.size).Append(col.isTrigger).Append(col.enabled).Append(col.offset).Append('|');
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null) sb.Append(rb.bodyType).Append(rb.linearVelocity).Append(rb.angularVelocity).Append(rb.gravityScale);
        sb.Append('|');
        var id = go.GetComponent<EnemyIdentity>();
        sb.Append(id.Key).Append(id.Scale).Append(id.Variant).Append('|');
        var hearts = go.GetComponent<EnemyHearts>();
        if (hearts != null) sb.Append(hearts.Max).Append(hearts.QuartersLeft).Append(hearts.Strikes).Append(hearts.IgnoredHits);
        sb.Append('|');
        var print = go.GetComponent<SpawnFootprint>();
        if (print != null) sb.Append(print.half).Append(print.layer).Append(print.SoftX).Append(print.SoftV);
        sb.Append('|');
        var target = go.GetComponent<ClearTarget>();
        if (target != null) sb.Append(target.Scenery).Append(target.PlayfieldSeconds).Append(target.SensedStep);
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) sb.Append('|').Append(brain.State).Append(brain.ShotsFired).Append(brain.Volleys).Append(brain.Windups).Append(brain.Ordered).Append(brain.Stage);
        return sb.ToString();
    }

    static GameObject Fresh(EnemyDef def, Vector3 at, float size = 1f)
    {
        return EnemyFactory.Create(def, at, Quaternion.identity, size, true, false);
    }

    static void ReusedEqualsFresh()
    {
        var defs = new List<EnemyDef>();
        foreach (var role in new[] { EnemyRole.Fighter, EnemyRole.Big, EnemyRole.Alien, EnemyRole.Rock })
        {
            var d = Find(x => x.role == role && x.world == 0);
            if (d != null) defs.Add(d);
        }
        Check("the roster has poolable defs to test", defs.Count >= 3);
        foreach (var def in defs)
        {
            var at = new Vector3(0.5f, 3f, 0f);
            var plain = Fresh(def, at);
            string fresh = Fingerprint(plain);
            Object.DestroyImmediate(plain);

            var first = EnemyFactory.Create(def, at, Quaternion.identity);
            Check(def.key + ": a pooled build carries a PooledObject", first.GetComponent<PooledObject>() != null);
            Check(def.key + ": the first pooled build equals a plain one", Fingerprint(first) == fresh);

            // a hard life: stunned, tinted, hurt, scaled, moved, ordered away, a marker on it, a child, a push
            var sr = first.GetComponent<SpriteRenderer>();
            FrozenState.Freeze(first, 2f);
            sr.color = Color.red; sr.flipX = true; sr.sortingOrder = 99;
            first.transform.localScale = new Vector3(3f, 3f, 1f);
            first.transform.position = new Vector3(-2f, -7f, 0f);
            first.layer = 5; first.name = "renamed"; first.tag = "Untagged";
            first.GetComponent<BoxCollider2D>().enabled = false;
            first.GetComponent<BoxCollider2D>().size = new Vector2(9f, 9f);
            var rb = first.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = new Vector2(4f, 4f);
            var hearts = first.GetComponent<EnemyHearts>();
            if (hearts != null) hearts.Strike(first.transform.position + Vector3.down, 1f);
            var brain = first.GetComponent<EnemyBrain>();
            if (brain != null) brain.Order();
            first.AddComponent<BoxCollider>();
            new GameObject("marker").transform.SetParent(first.transform, false);
            first.GetComponent<ClearTarget>().Scenery = true;

            Pooled.Remove(first);
            Check(def.key + ": release deactivates the body", !first.activeSelf);
            Check(def.key + ": a released body is idle in its pool", PrefabPool.FreeCount("enemy:" + def.key) == 1);

            var again = EnemyFactory.Create(def, at, Quaternion.identity);
            Check(def.key + ": the same instance comes back", ReferenceEquals(again, first));
            Check(def.key + ": a reused enemy equals a fresh one", Fingerprint(again) == fresh);
            Check(def.key + ": it is where it was told", again.transform.position == at && again.transform.rotation == Quaternion.identity);
            Check(def.key + ": no stun survives", again.GetComponent<FrozenState>() == null);
            Check(def.key + ": it is active again", again.activeSelf);
            Pooled.Remove(again);
            PrefabPool.ClearAndDestroy();
        }
    }

    static void ReleasedAreUnregistered()
    {
        var def = Find(x => x.role == EnemyRole.Fighter && x.world == 0);
        int clearBefore = ClearTarget.Live.Count, printsBefore = SpawnSpace.Live(SpawnLayer.Enemy).Count;
        var go = EnemyFactory.Create(def, new Vector3(0f, 4f, 0f), Quaternion.identity);
        var target = go.GetComponent<ClearTarget>();
        var print = go.GetComponent<SpawnFootprint>();
        Check("a live enemy is in ClearTarget.Live", ClearTarget.Live.Contains(target));
        Check("a live enemy holds its SpawnSpace footprint", SpawnSpace.Live(SpawnLayer.Enemy).Contains(print));
        Pooled.Remove(go);
        Check("a released enemy left ClearTarget.Live", !ClearTarget.Live.Contains(target) && ClearTarget.Live.Count == clearBefore);
        Check("a released enemy left SpawnSpace", SpawnSpace.Live(SpawnLayer.Enemy).Count == printsBefore);
        var again = EnemyFactory.Create(def, new Vector3(1f, 4f, 0f), Quaternion.identity);
        var ct2 = again.GetComponent<ClearTarget>();
        Check("the reused enemy registers exactly once, afresh", ClearTarget.Live.Contains(ct2) && ClearTarget.Live.Count == clearBefore + 1 && ct2 != target);
        Check("the reused enemy is not already marked hit", !ShipAttackHits.AlreadyHit(again));
        Pooled.Remove(again);
        PrefabPool.ClearAndDestroy();
    }

    static void PoolIsBoundedAndClears()
    {
        var def = Find(x => x.role == EnemyRole.Fighter && x.world == 0);
        string key = "enemy:" + def.key;
        var all = new List<GameObject>();
        for (int i = 0; i < PrefabPool.MaxFreePerKey + 12; i++) all.Add(EnemyFactory.Create(def, new Vector3(0f, 4f, 0f), Quaternion.identity));
        foreach (var g in all) Pooled.Remove(g);
        Check("the pool keeps at most MaxFreePerKey idle bodies", PrefabPool.FreeCount(key) == PrefabPool.MaxFreePerKey);
        Check("nothing is left in use", PrefabPool.InUse == 0);
        PrefabPool.ClearAndDestroy();
        Check("a run end / scene load empties the pool", PrefabPool.FreeCount(key) == 0);
        Check("the live lists hold no enemy after the pool is gone", EnemyFinder.Count() == 0);

        EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
        PrefabPool.Clear();
        Check("Clear forgets the idle and in-use bookkeeping", PrefabPool.FreeCount(key) == 0 && PrefabPool.InUse == 0);
        foreach (var t in new List<ClearTarget>(ClearTarget.Live)) if (t != null) Object.DestroyImmediate(t.gameObject);

        // prewarm: builds idle bodies one step at a time, then stops
        int steps = 0;
        while (EnemyFactory.PrewarmStep(def.world) && steps < 200) steps++;
        Check("prewarm finishes", steps > 0 && steps < 200);
        Check("prewarm leaves PrewarmPerType idle bodies per type", PrefabPool.FreeCount(key) >= EnemyFactory.PrewarmPerType);
        Check("prewarmed bodies are inactive", EnemyFinder.Count() == 0);
        var used = EnemyFactory.Create(def, new Vector3(0f, 4f, 0f), Quaternion.identity);
        Check("the first spawn after a prewarm builds nothing", PrefabPool.Reused > 0 && used.activeSelf);
        Pooled.Remove(used);
        PrefabPool.ClearAndDestroy();
    }

    static class EnemyFinder
    {
        public static int Count()
        {
            int n = 0;
            foreach (var t in ClearTarget.Live) if (t != null && t.isActiveAndEnabled && ClearTarget.IsHazard(t.gameObject)) n++;
            return n;
        }
    }

    static void NonPooledStayPlain()
    {
        var mine = Find(x => x.role == EnemyRole.Mine);
        var chaser = Find(x => x.role == EnemyRole.Chaser);
        var fighter = Find(x => x.role == EnemyRole.Fighter && x.world == 0);
        if (mine != null) { var m = EnemyFactory.Create(mine, Vector3.zero, Quaternion.identity); Check("a mine is not pooled", m.GetComponent<PooledObject>() == null); Object.DestroyImmediate(m); }
        if (chaser != null) { var c = EnemyFactory.Create(chaser, Vector3.zero, Quaternion.identity); Check("a chaser is not pooled", c.GetComponent<PooledObject>() == null); Object.DestroyImmediate(c); }
        var e = EnemyFactory.Create(fighter, Vector3.zero, Quaternion.identity, 1f, true, false);
        Check("pooled: false builds a plain object (elites, tutorial)", e.GetComponent<PooledObject>() == null);
        Pooled.Remove(e);
        Check("removing a plain enemy still destroys it", e == null);
        Check("plain objects never reach the pool", PrefabPool.FreeCount("enemy:" + fighter.key) == 0);
        var tut = EnemyFactory.Create(fighter, Vector3.zero, Quaternion.identity, 1f, false);
        Check("a one-hit enemy has no hearts, pooled or not", tut.GetComponent<EnemyHearts>() == null);
        Pooled.Remove(tut);
        PrefabPool.ClearAndDestroy();
    }

    static void RocksKeepTheirSize()
    {
        var rock = Find(x => x.role == EnemyRole.Rock && x.world == 0);
        if (rock == null) { Check("a rock def exists", false); return; }
        var big = EnemyFactory.Create(rock, Vector3.zero, Quaternion.identity, HazardSize.Varies(rock) ? 1.6f : 1f);
        float bigScale = big.transform.localScale.x;
        Pooled.Remove(big);
        var small = EnemyFactory.Create(rock, Vector3.zero, Quaternion.identity, 1f);
        Check("a reused rock is drawn at the size it was drawn at, not its last size", Mathf.Approximately(small.transform.localScale.x, 1f));
        var ref1 = Fresh(rock, Vector3.zero, 1f);
        Check("a reused rock's footprint and hit radius match a fresh one", small.GetComponent<SpawnFootprint>().half == ref1.GetComponent<SpawnFootprint>().half
            && Mathf.Approximately(small.GetComponent<ClearTarget>().Radius, ref1.GetComponent<ClearTarget>().Radius));
        Check("(the large draw was applied at all, or the rock does not vary)", bigScale >= 1f);
        Object.DestroyImmediate(ref1);
        Pooled.Remove(small);
        PrefabPool.ClearAndDestroy();
    }

    // ------------------------------------------------------------------ part B

    static void ViewIsReadOncePerFrame(List<Object> made)
    {
        var camGo = new GameObject("poolTestCam") { tag = "MainCamera" };
        made.Add(camGo);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(1f, 2f, -10f);

        ShipTargets.ForceViewCache = true;
        ShipTargets.ResetViewCache();
        int before = ShipTargets.ViewReads;
        Rect a = ShipTargets.View(), b = ShipTargets.View(), c = ShipTargets.View(.5f);
        Check("the view is read from the camera once for many asks in one frame", ShipTargets.ViewReads == before + 1);
        Check("every ask sees the same view", a == b && Mathf.Approximately(c.width, a.width + 1f) && Mathf.Approximately(a.height, 10f) && Mathf.Approximately(a.center.x, 1f) && Mathf.Approximately(a.center.y, 2f));
        camGo.transform.position = new Vector3(3f, 2f, -10f);
        Check("within the frame the cached view is kept", ShipTargets.View().center.x == 1f);
        ShipTargets.ResetViewCache();
        Check("a new frame (cache reset) reads the camera again", Mathf.Approximately(ShipTargets.View().center.x, 3f) && ShipTargets.ViewReads == before + 2);
        ShipTargets.ForceViewCache = false;
        int reads = ShipTargets.ViewReads;
        ShipTargets.View(); ShipTargets.View();
        Check("outside play (no frames) every ask reads fresh, as before", ShipTargets.ViewReads == reads + 2);
        Object.DestroyImmediate(camGo);
        Rect open = ShipTargets.View();
        Check("no camera: the unbounded view", open.width > 1e4f);
    }

    static void WeatherBodyCache()
    {
        var def = Find(x => x.role == EnemyRole.Fighter && x.world == 0);
        var go = Fresh(def, new Vector3(0f, 1f, 0f));
        var t = go.GetComponent<ClearTarget>();
        Check("a plain enemy is not a boss or an elite", !t.IsBossOrElite);
        Check("its footprint comes from the cache", t.BodyFootprint == go.GetComponent<SpawnFootprint>() && t.BodyFootprint != null);
        Check("a fighter has no rail mount", t.BodyMount == null);
        var f1 = t.Stun(2f);
        Check("the first stun freezes it", f1 != null && f1.IsFrozen);
        var f2 = t.Stun(2f);
        Check("a frozen body is refreshed in place (same state, one component)", ReferenceEquals(f1, f2) && go.GetComponents<FrozenState>().Length == 1 && f2.IsFrozen);
        f2.Thaw();
        var f3 = t.Stun(2f);
        Check("a thawed body is frozen again", f3 != null && f3.IsFrozen);
        t.InvalidateBody();
        Check("an invalidated cache looks the body up again", t.BodyFootprint != null && t.Stun(2f) == f3);
        Object.DestroyImmediate(go);

        // pooled: the cache does not leak into the next life
        var pooled = EnemyFactory.Create(def, new Vector3(0f, 1f, 0f), Quaternion.identity);
        var pt = pooled.GetComponent<ClearTarget>();
        var pfp = pt.BodyFootprint;
        pt.Stun(2f);
        Pooled.Remove(pooled);
        var again = EnemyFactory.Create(def, new Vector3(0f, 1f, 0f), Quaternion.identity);
        var nt = again.GetComponent<ClearTarget>();
        Check("a reused body's weather cache is its own", nt.BodyFootprint == again.GetComponent<SpawnFootprint>() && nt.BodyFootprint != pfp && again.GetComponent<FrozenState>() == null);
        Pooled.Remove(again);
        PrefabPool.ClearAndDestroy();
    }

    static void FloatieFreeList()
    {
        var ship = new GameObject("poolTestShip");
        var battery = ship.AddComponent<FloatieBattery>();
        try
        {
            FloatieBullets.ReleaseAll();
            int baseline = FloatieBullets.Active;
            var held = new List<FloatieBullet>();
            for (int i = 0; i < 6; i++)
            {
                var b = FloatieBullets.Take(true);
                if (b == null) break;
                var mods = FloatieBullet.Mods.Plain;
                mods.source = i % 2;
                b.Launch(battery, 0, new Vector3(i, 0f, 0f), Vector3.up, Color.white, default(FloatieAim.Pick), mods);
                held.Add(b);
            }
            Check("six bullets launched", held.Count == 6);
            Check("the live count follows the launches", FloatieBullets.Active == baseline + 6);
            Check("per-floatie live counts follow too", battery.LiveBy(0) == 3 && battery.LiveBy(1) == 3 && battery.LiveBy(2) == 0);
            int size = FloatieBullets.Size;
            held[0].Release();
            Check("a release drops the counts", FloatieBullets.Active == baseline + 5 && battery.LiveBy(0) == 2);
            held[0].Release();
            Check("a second release changes nothing", FloatieBullets.Active == baseline + 5 && battery.LiveBy(0) == 2);
            var again = FloatieBullets.Take(true);
            Check("a free bullet is handed out from the free list, nothing is built", again == held[0] && FloatieBullets.Size == size);
            again.Launch(battery, 0, Vector3.zero, Vector3.up, Color.white, default(FloatieAim.Pick), FloatieBullet.Mods.Plain);
            Check("relaunching a reused bullet counts it once", FloatieBullets.Active == baseline + 6 && battery.LiveBy(0) == 3);
            again.Launch(battery, 0, Vector3.zero, Vector3.up, Color.white, default(FloatieAim.Pick), FloatieBullet.Mods.Plain);
            Check("launching one that is still in the air does not double count", FloatieBullets.Active == baseline + 6 && battery.LiveBy(0) == 3);
            FloatieBullets.ReleaseAll(battery);
            Check("ReleaseAll(owner) empties the counts", FloatieBullets.Active == baseline && battery.LiveBy(0) == 0 && battery.LiveBy(1) == 0);
            int built = FloatieBullets.Size;
            for (int i = 0; i < 6; i++) FloatieBullets.Take(true);
            Check("takes after a release reuse the pool (no growth)", FloatieBullets.Size == built);
        }
        finally
        {
            FloatieBullets.ReleaseAll();
            Object.DestroyImmediate(ship);
        }
    }


    // ------------------------------------------------------------------ stale holders

    static void LifeAndFollowers()
    {
        var def = Find(x => x.role == EnemyRole.Fighter && x.world == 0);
        var go = EnemyFactory.Create(def, new Vector3(0f, 4f, 0f), Quaternion.identity);
        int stamp = EnemyLife.Stamp(go);
        Check("a live pooled enemy has a life stamp", stamp > 0 && EnemyLife.Same(go, stamp) && !EnemyLife.Gone(go));
        Check("a plain (unpooled) object is never gone while it exists", !EnemyLife.Gone(new GameObject("plain")));
        Check("null is gone", EnemyLife.Gone((GameObject)null));

        // representative followers: a homing shot's Transform target, a shove entry
        var follow = go.transform;
        EnemyShove.Add(follow, null, new Vector2(1f, 0f), .3f, 1f);
        Check("a shoved body is listed", EnemyShove.IsShoved(follow));
        Pooled.Remove(go);
        Check("a released body is gone for every holder", EnemyLife.Gone(go) && EnemyLife.Gone(follow));
        Check("a released body's stamp no longer matches", !EnemyLife.Same(go, stamp));
        EnemyShove.Step(.016f);
        Check("a shove stops following a released body", !EnemyShove.IsShoved(follow));

        var again = EnemyFactory.Create(def, new Vector3(0f, 4f, 0f), Quaternion.identity);
        Check("the body came back (pool reuse)", ReferenceEquals(again, go));
        Check("the reused enemy is a new life", EnemyLife.Stamp(again) != stamp && !EnemyLife.Same(again, stamp) && !EnemyLife.Gone(again));
        int second = EnemyLife.Stamp(again);
        Pooled.Remove(again);
        var third = EnemyFactory.Create(def, new Vector3(0f, 4f, 0f), Quaternion.identity);
        EnemyShove.Clear();
        Check("every life gets its own stamp", EnemyLife.Stamp(third) != second && EnemyLife.Stamp(third) != stamp);
        Pooled.Remove(third);
        PrefabPool.ClearAndDestroy();
    }

    static void ReuseCooldown()
    {
        var def = Find(x => x.role == EnemyRole.Fighter && x.world == 0);
        string key = "enemy:" + def.key;
        float now = 100f;
        PrefabPool.CooldownClock = () => now;
        // the key's cooldown is registered by the factory on first use
        var a = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
        Pooled.Remove(a);
        Check("a released body is counted idle while it rests", PrefabPool.FreeCount(key) == 1);
        var b = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
        Check("it is not handed out again during its cooldown", !ReferenceEquals(a, b));
        now += EnemyFactory.ReuseCooldownSeconds + .1f;
        Pooled.Remove(b);
        var c = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
        Check("after the cooldown it is reused", ReferenceEquals(c, a));
        Pooled.Remove(c);
        PrefabPool.ClearAndDestroy();
        Check("cooling bodies are cleared with the pool", PrefabPool.FreeCount(key) == 0);
        PrefabPool.CooldownClock = null;
        // prewarmed bodies are ready at once
        PrefabPool.Clear();
        int steps = 0;
        while (EnemyFactory.PrewarmStep(def.world) && steps < 200) steps++;
        PrefabPool.CooldownClock = () => now;
        var p = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
        Check("a prewarmed body is usable immediately", PrefabPool.Reused > 0 && p.activeSelf);
        Pooled.Remove(p);
        PrefabPool.ClearAndDestroy();
    }

    static void SourceAudit()
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "Scripts"));
        string factory = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Gameplay/Enemies/EnemyFactory.cs"));
        Check("EnemyFactory builds through the pool", factory.Contains("PrefabPool.Get("));
        foreach (var rel in new[] { "Gameplay/Weapons/ShipAttackTarget.cs", "Gameplay/Weapons/DeathCombo.cs", "Gameplay/Weapons/ShoveCrash.cs", "Boosters/HazardClear.cs", "Ship/TeleportFx.cs" })
        {
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(root, rel));
            Check(rel + ": enemies leave through Pooled.Remove", src.Contains("Pooled.Remove("));
        }
        string hits = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Ship/collisionDetection.cs"));
        Check("collisionDetection kills leave through Pooled.Destroy", !hits.Contains("                Destroy(hit.gameObject);") && hits.Contains("if (fatal) Pooled.Destroy(hit.gameObject);"));
        string boss = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Bosses/BossActor.cs"));
        Check("BossUtil.Kill routes through the pool funnel", boss.Contains("Pooled.Remove(go); return;"));
    }
}
