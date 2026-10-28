using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

// Pooling of the pickups (PrefabPool / PooledObject / Pooled): star dust, the five atoms and the
// green atom are built once and reused. What this pins:
//   1  the pool itself: the same instance comes back; every field of a used object (transform,
//      colour, sprite, flags, collider, body, enabled scripts, children, added components) is back
//      to the snapshot of a fresh one; no double release; deferred release; the cap; a damaged or
//      destroyed instance is never handed out; a scene load leaves nothing behind
//   2  the pickups: a reused star / atom equals a freshly built one (scale, colliders, halo, ring,
//      footprint, spin, flipbook, wander, glow age), its registries follow it in and out, and a
//      refund mark never survives
//   3  the game: collecting a pickup through collisionDetection scores / drops / counts exactly the
//      same for a fresh, a pooled and a reused pickup, and its burst still plays
//   4  source audit: no pickup-type Destroy / Instantiate outside the funnels
public static class PickupPoolTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[POOL] PASS  " : "[POOL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PrefabPool.ClearAndDestroy();
        PrefabPool.ForceInEditMode = true;
        try
        {
            SameInstanceComesBack();
            EveryFieldIsReset();
            NoDoubleRelease();
            DeferredRelease();
            CapAndDamagedAndStale();
            SceneLoadLeavesNothing();
            RealPickupsEqualFreshOnes();
            RealPickupLifecycle();
            GreenAtomPool();
            RefundMarkNeverSurvives();
            ScoringAndDropsMatchThePlainPath();
            PrewarmBuildsNothingLater();
            MagnetCompactSkipsPooledGone();
            SourceAudit();
        }
        finally
        {
            PrefabPool.ClearAndDestroy();
            PrefabPool.ForceInEditMode = false;
            PrefabPool.ForceDeferred = false;
            buttonClicks.playerDied = false;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.lifeCounter = 0;
        }
        Debug.Log("[POOL] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------
    // a synthetic pickup-like prefab

    sealed class Probe : MonoBehaviour, IPoolReset
    {
        public int value, resets;
        public void OnPoolAcquire() { resets++; value = 0; }
    }

    static GameObject synthetic;
    static Sprite sprite;

    static GameObject Template()
    {
        if (synthetic != null) return synthetic;
        var tex = new Texture2D(4, 4);
        sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100f);
        synthetic = new GameObject("synthPickup", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D));
        synthetic.tag = "pickUp";
        synthetic.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        synthetic.GetComponent<SpriteRenderer>().sprite = sprite;
        synthetic.GetComponent<CircleCollider2D>().isTrigger = true;
        synthetic.GetComponent<CircleCollider2D>().radius = .2f;
        synthetic.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        synthetic.AddComponent<Probe>();
        var kid = new GameObject("kid", typeof(SpriteRenderer));
        kid.transform.SetParent(synthetic.transform, false);
        kid.transform.localPosition = new Vector3(.1f, .2f, 0f);
        kid.GetComponent<SpriteRenderer>().sprite = sprite;
        return synthetic;
    }

    static readonly Action<GameObject> Registered = go => { ClearTarget.Ensure(go); SpawnFootprint.Attach(go, Vector2.one * .2f, SpawnLayer.Pickup); };

    static GameObject Get(Vector3 at) { return PrefabPool.Get(Template(), at, Quaternion.identity, Registered); }

    static void Fresh()
    {
        PrefabPool.ClearAndDestroy();
        PrefabPool.ForceDeferred = false;
    }

    static void SameInstanceComesBack()
    {
        Fresh();
        var a = Get(new Vector3(1f, 2f, 0f));
        Check("a first get builds one instance", PrefabPool.Created == 1 && PrefabPool.Reused == 0 && PrefabPool.InUse == 1);
        Check("the instance is a pooled one at the asked place", a.GetComponent<PooledObject>() != null && a.transform.position == new Vector3(1f, 2f, 0f));
        Check("a pickup-type object is registered while out (ClearTarget, footprint)", ClearTarget.Live.Contains(a.GetComponent<ClearTarget>()));
        Pooled.Destroy(a);
        Check("destroy returns it: inactive, off every registry, one free", !a.activeSelf && PrefabPool.FreeCount(Template()) == 1 &&
              !ClearTarget.Live.Contains(a.GetComponent<ClearTarget>()) && PrefabPool.InUse == 0);
        var b = Get(new Vector3(-3f, 4f, 0f));
        Check("the next get is the SAME instance (no new build)", ReferenceEquals(a, b) && PrefabPool.Created == 1 && PrefabPool.Reused == 1);
        Check("...at the new place, active and registered again", b.activeSelf && b.transform.position == new Vector3(-3f, 4f, 0f) &&
              ClearTarget.Live.Contains(b.GetComponent<ClearTarget>()));
        Check("...with its script reset hook called once per reuse", b.GetComponent<Probe>().resets == 1);
        // many cycles keep reusing one body
        for (int i = 0; i < 50; i++) { Pooled.Destroy(b); b = Get(Vector3.zero); }
        Check("50 more cycles build nothing", PrefabPool.Created == 1 && PrefabPool.Reused == 51);
        Pooled.Destroy(b);
    }

    static void EveryFieldIsReset()
    {
        Fresh();
        var go = Get(new Vector3(0f, 5f, 0f));
        var po = go.GetComponent<PooledObject>();
        var sr = go.GetComponent<SpriteRenderer>();
        var col = go.GetComponent<CircleCollider2D>();
        var rb = go.GetComponent<Rigidbody2D>();
        var kid = go.transform.Find("kid");
        // a whole life of damage
        go.transform.rotation = Quaternion.Euler(0f, 0f, 77f);
        go.transform.localScale = Vector3.one * 9f;
        go.name = "renamed";
        go.tag = "Enimey";
        go.layer = 5;
        sr.color = new Color(1f, 0f, 0f, .1f);
        sr.sprite = null;
        sr.flipX = true;
        sr.enabled = false;
        sr.sortingOrder = 99;
        col.enabled = false;
        col.radius = 3f;
        col.isTrigger = false;
        rb.linearVelocity = new Vector2(3f, 4f);
        rb.angularVelocity = 8f;
        go.GetComponent<Probe>().value = 12;
        go.GetComponent<Probe>().enabled = false;
        go.GetComponent<ClearTarget>().enabled = false;
        kid.localPosition = new Vector3(5f, 5f, 0f);
        kid.gameObject.SetActive(false);
        go.AddComponent<RefundMark>();
        go.AddComponent<AtomSpin>();
        new GameObject("addedChild").transform.SetParent(go.transform, false);
        new GameObject("addedGrandChild").transform.SetParent(kid, false);
        Pooled.Destroy(go);
        Check("release removed what was added (components and children)", go.GetComponent<RefundMark>() == null && go.GetComponent<AtomSpin>() == null &&
              go.transform.Find("addedChild") == null && kid.Find("addedGrandChild") == null);
        var again = Get(new Vector3(2f, 6f, 0f));
        Check("it is the same instance", ReferenceEquals(go, again));
        Check("snapshot equal after reuse (" + (po.Diff() ?? "no difference") + ")", po.Diff() == null);
        Check("tag / layer / name back", again.CompareTag("pickUp") && again.layer == 0 && again.name == "synthPickup(Clone)");
        Check("scale and rotation back, placed where asked", again.transform.localScale == new Vector3(1.5f, 1.5f, 1f) &&
              again.transform.rotation == Quaternion.identity && again.transform.position == new Vector3(2f, 6f, 0f));
        Check("colour, sprite, flip, enabled and order back", sr.color == Color.white && sr.sprite == sprite && !sr.flipX && sr.enabled && sr.sortingOrder == 0);
        Check("collider back (enabled, trigger, radius)", col.enabled && col.isTrigger && Mathf.Approximately(col.radius, .2f));
        Check("body velocity zero", rb.linearVelocity == Vector2.zero && rb.angularVelocity == 0f);
        Check("script flags back (enabled, state reset)", go.GetComponent<Probe>().enabled && go.GetComponent<Probe>().value == 0 && go.GetComponent<ClearTarget>().enabled);
        Check("child back (active, place)", kid.gameObject.activeSelf && kid.localPosition == new Vector3(.1f, .2f, 0f));
        Check("registered again", ClearTarget.Live.Contains(go.GetComponent<ClearTarget>()));
        Pooled.Destroy(again);
    }

    static void NoDoubleRelease()
    {
        Fresh();
        var a = Get(Vector3.zero);
        Pooled.Destroy(a);
        Pooled.Destroy(a);
        a.GetComponent<PooledObject>().Release();
        Check("releasing twice is counted and ignored (" + PrefabPool.DoubleReleases + ")", PrefabPool.DoubleReleases == 2 && PrefabPool.FreeCount(Template()) == 1);
        var x = Get(Vector3.zero);
        var y = Get(Vector3.zero);
        Check("so two gets give two different bodies", !ReferenceEquals(x, y) && ReferenceEquals(x, a));
        Pooled.Destroy(x); Pooled.Destroy(y);
        Check("both come back once each", PrefabPool.FreeCount(Template()) == 2 && PrefabPool.InUse == 0);
        // a plain object is simply destroyed
        var plain = new GameObject("plain");
        Pooled.Remove(plain);
        Check("a non-pooled object is destroyed as before", plain == null);
    }

    static void DeferredRelease()
    {
        Fresh();
        PrefabPool.ForceDeferred = true;
        var a = Get(Vector3.zero);
        Pooled.Destroy(a);
        Check("a deferred release leaves it active and out until the flush (like Destroy)", a.activeSelf && a.GetComponent<PooledObject>().IsPending &&
              PrefabPool.PendingCount == 1 && PrefabPool.FreeCount(Template()) == 0 && PrefabPool.InUse == 1);
        var other = Get(Vector3.zero);
        Check("a get meanwhile builds another (never the pending one)", !ReferenceEquals(a, other));
        Pooled.Destroy(a);
        Check("a second request while pending is ignored", PrefabPool.DoubleReleases == 1 && PrefabPool.PendingCount == 1);
        PrefabPool.Flush();
        Check("the flush returns it", !a.activeSelf && PrefabPool.FreeCount(Template()) == 1 && PrefabPool.PendingCount == 0 && PrefabPool.InUse == 1);
        var back = Get(Vector3.zero);
        Check("and it is the one reused", ReferenceEquals(back, a) && back.activeSelf);
        PrefabPool.ForceDeferred = false;
        Pooled.Destroy(back); Pooled.Destroy(other);
        Check("immediate again when not deferred", PrefabPool.InUse == 0 && PrefabPool.FreeCount(Template()) == 2);
    }

    static void CapAndDamagedAndStale()
    {
        Fresh();
        var all = new List<GameObject>();
        for (int i = 0; i < PrefabPool.MaxFreePerKey + 7; i++) all.Add(Get(Vector3.zero));
        foreach (var g in all) Pooled.Destroy(g);
        Check("the pool keeps at most MaxFreePerKey; the surplus is destroyed", PrefabPool.FreeCount(Template()) == PrefabPool.MaxFreePerKey && PrefabPool.Discarded == 7 && PrefabPool.InUse == 0);

        Fresh();
        var a = Get(Vector3.zero);
        Pooled.Destroy(a);
        UnityEngine.Object.DestroyImmediate(a.GetComponent<Probe>());   // damaged from outside
        var b = Get(Vector3.zero);
        Check("a damaged instance is never handed out again", !ReferenceEquals(a, b) && a == null && PrefabPool.Created == 2 && b.GetComponent<Probe>() != null);

        Fresh();
        var c = Get(Vector3.zero);
        Pooled.Destroy(c);
        UnityEngine.Object.DestroyImmediate(c);                         // gone with its scene
        var d = Get(Vector3.zero);
        Check("a destroyed idle instance is skipped (a fresh one is built)", d != null && PrefabPool.Created == 2 && PrefabPool.Reused == 0);
        Pooled.Destroy(d);
        // destroying a pooled body from outside while it is out does not wedge the pool
        var e = Get(Vector3.zero);
        UnityEngine.Object.DestroyImmediate(e);
        Pooled.Destroy(e);
        Check("releasing an already destroyed object is harmless", true);
    }

    static void SceneLoadLeavesNothing()
    {
        Fresh();
        var live = new List<GameObject>();
        for (int i = 0; i < 20; i++) live.Add(Get(Vector3.zero));
        for (int i = 0; i < 10; i++) Pooled.Destroy(live[i]);
        PrefabPool.ForceDeferred = true;
        Pooled.Destroy(live[10]);
        PrefabPool.ForceDeferred = false;
        typeof(PrefabPool).GetMethod("OnSceneLoaded", Stat).Invoke(null, new object[] { default(Scene), LoadSceneMode.Single });
        Check("a scene load clears the pool: nothing in use, free or pending", PrefabPool.InUse == 0 && PrefabPool.FreeCount(Template()) == 0 && PrefabPool.PendingCount == 0);
        Check("...and the counters start over", PrefabPool.Created == 0 && PrefabPool.Reused == 0);
        // an additive load keeps it
        var a = Get(Vector3.zero);
        typeof(PrefabPool).GetMethod("OnSceneLoaded", Stat).Invoke(null, new object[] { default(Scene), LoadSceneMode.Additive });
        Check("an additive load leaves the pool alone", PrefabPool.InUse == 1);
        Pooled.Destroy(a);
        // the real thing: a new scene destroys the old objects, the stale entries are skipped
        var b = Get(Vector3.zero);
        Pooled.Destroy(b);
        synthetic = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var c = Get(Vector3.zero);
        Check("after a real scene change the next get is a clean, live object", c != null && c.activeSelf && c.GetComponent<PooledObject>().Diff() == null);
        Pooled.Destroy(c);
    }

    // ------------------------------------------------------------------
    // the real pickups

    static GameObject Prefab(string path)
    {
        var p = Resources.Load<GameObject>(path);
        if (p == null) Debug.Log("[POOL] missing prefab " + path);
        return p;
    }

    sealed class Fixture : IDisposable
    {
        public spawnGoodStuff spawner;
        public GameObject root;
        public GameObject smStar, midStar, blue, red, violet, magnet;
        public Fixture()
        {
            root = new GameObject("~TestSpawner");
            spawner = root.AddComponent<spawnGoodStuff>();
            smStar = spawner.smStar = Prefab("prefabs/smStar_1");
            midStar = spawner.midStar = Prefab("prefabs/LargeStar_1");
            blue = spawner.Atom = Prefab("prefabs/atom3a");
            red = spawner.redAtom = Prefab("prefabs/pauseAtom");
            violet = spawner.cooldownAtom = Prefab("prefabs/cooldownAtom");
            magnet = spawner.magnetAtom = Prefab("prefabs/magnetAtom");
        }
        public IEnumerable<GameObject> All { get { return new[] { smStar, midStar, blue, red, violet, magnet }; } }
        public GameObject Place(GameObject prefab, Vector3 at)
        {
            return (GameObject)typeof(spawnGoodStuff).GetMethod("Place", Inst).Invoke(spawner, new object[] { prefab, at });
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(root); }
    }

    static string Describe(GameObject go)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(go.tag).Append('|').Append(go.layer).Append('|').Append(go.transform.localScale.ToString("F4")).Append('|');
        var comps = new List<string>();
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c is PooledObject) continue;
            var s = c.GetType().Name + "@" + (c.transform == go.transform ? "root" : c.transform.name);
            var sr = c as SpriteRenderer;
            if (sr != null) s += ":" + (sr.sprite != null ? sr.sprite.name : "-") + ":" + sr.sortingOrder + ":" + sr.color + ":" + sr.transform.localScale.ToString("F3");
            var box = c as BoxCollider2D; if (box != null) s += ":" + box.size.ToString("F4") + box.offset.ToString("F4");
            var circle = c as CircleCollider2D; if (circle != null) s += ":" + circle.radius.ToString("F4") + circle.offset.ToString("F4");
            var spin = c as AtomSpin; if (spin != null) s += ":" + spin.degreesPerSecond;
            var fp = c as SpawnFootprint; if (fp != null) s += ":" + fp.layer + fp.half.ToString("F4");
            var pg = c as PickupGlow; if (pg != null) s += ":" + pg.Kind + pg.Age;
            if (c is Behaviour b) s += ":" + b.enabled;
            comps.Add(s);
        }
        comps.Sort(StringComparer.Ordinal);
        sb.Append(string.Join(";", comps));
        return sb.ToString();
    }

    static void RealPickupsEqualFreshOnes()
    {
        Fresh();
        using var fx = new Fixture();
        bool prefabs = true;
        foreach (var p in fx.All) prefabs &= p != null;
        Check("all six pickup prefabs load", prefabs);
        int checkedTypes = 0, equalFirst = 0, equalReused = 0;
        foreach (var prefab in fx.All)
        {
            // the plain path (pool off): what the game built before
            PrefabPool.ForceInEditMode = false;
            var plain = fx.Place(prefab, new Vector3(.5f, 3f, 0f));
            var reference = Describe(plain);
            UnityEngine.Object.DestroyImmediate(plain);
            PrefabPool.ForceInEditMode = true;

            var first = fx.Place(prefab, new Vector3(.5f, 3f, 0f));
            if (Describe(first) == reference) equalFirst++;
            else Debug.Log("[POOL] first differs for " + prefab.name + "\n" + reference + "\n" + Describe(first));
            // age it, then reuse it
            var po = first.GetComponent<PooledObject>();
            var glow = first.GetComponent<PickupGlow>();
            if (glow != null) glow.Advance(.7f);
            first.transform.position += Vector3.down * 3f;
            Pooled.Destroy(first);
            var second = fx.Place(prefab, new Vector3(.5f, 3f, 0f));
            if (ReferenceEquals(first, second) && Describe(second) == reference && po.Diff() == null) equalReused++;
            else Debug.Log("[POOL] reuse differs for " + prefab.name + "\n" + reference + "\n" + Describe(second) + "\n" + po.Diff());
            Pooled.Destroy(second);
            checkedTypes++;
        }
        Check("a pooled pickup equals the old Instantiate + decorate one, for all six types (" + equalFirst + "/" + checkedTypes + ")", checkedTypes == 6 && equalFirst == 6);
        Check("...and so does a reused one, the same instance (" + equalReused + "/" + checkedTypes + ")", equalReused == 6);
        Check("building decorated each once: six types, six builds (" + PrefabPool.Created + "), twelve gets, six reuses", PrefabPool.Created == 6 && PrefabPool.Reused == 6);
    }

    static void RealPickupLifecycle()
    {
        Fresh();
        using var fx = new Fixture();
        foreach (var prefab in new[] { fx.blue, fx.smStar })
        {
            var go = fx.Place(prefab, new Vector3(0f, 3f, 0f));
            bool atom = prefab == fx.blue;
            var mover = go.GetComponent<moveItemEnmInStrightLine>();
            var glow = go.GetComponent<PickupGlow>();
            var book = go.GetComponent<PickupFlipbook>();
            // a life: moves, breathes, animates, is locked by a magnet
            for (int i = 0; i < 8; i++) { mover.Step(.05f, .2f, 5f, -5f); glow.Advance(.05f); book.Advance(.05f); }
            mover.enabled = false;
            var movedTo = go.transform.position;
            bool wandered = atom && mover.Wander != null;
            Check((atom ? "atom" : "dust") + ": it lived (moved, breathed, animated" + (atom ? ", wandered" : "") + ")",
                  movedTo.y < 3f && glow.Age > .35f && book.Frame != 0 && (!atom || wandered));
            Pooled.Destroy(go);
            var again = fx.Place(prefab, new Vector3(1f, 3f, 0f));
            mover = again.GetComponent<moveItemEnmInStrightLine>();
            glow = again.GetComponent<PickupGlow>();
            book = again.GetComponent<PickupFlipbook>();
            Check((atom ? "atom" : "dust") + ": reused as new (mover on, wander and glow age and flipbook reset)",
                  ReferenceEquals(go, again) && mover.enabled && mover.Wander == null && Mathf.Approximately(glow.Age, 0f) && book.Frame == 0 &&
                  again.transform.position == new Vector3(1f, 3f, 0f));
            // it behaves like the first: a first step on the new life rolls a fresh wander (atoms) or scrolls (dust)
            mover.Step(.05f, .2f, 5f, -5f);
            Check((atom ? "atom" : "dust") + ": its first step on the new life starts from the new place",
                  Mathf.Abs(again.transform.position.x - 1f) < .1f && again.transform.position.y < 3f && (!atom || mover.Wander != null));
            Check((atom ? "atom" : "dust") + ": its footprint is registered once while out and not while in the pool",
                  again.GetComponent<SpawnFootprint>() != null && again.GetComponent<SpawnFootprint>().isActiveAndEnabled);
            Pooled.Destroy(again);
            Check((atom ? "atom" : "dust") + ": ... and off after it (inactive: footprint unregistered)", !again.activeSelf && !again.GetComponent<SpawnFootprint>().isActiveAndEnabled);
            // the ship attack / ClearTarget side: a released body is not a target any more
            var ct = again.GetComponent<ClearTarget>();
            Check((atom ? "atom" : "dust") + ": no lock-on target while pooled", ct == null || !ShipTargets.IsPickup(ct));
        }
    }

    static void GreenAtomPool()
    {
        Fresh();
        var a = HealAtom.SpawnPooled(new Vector3(0f, 6f, 0f));
        Check("the green atom is pooled with its footprint", a.GetComponent<PooledObject>() != null && a.GetComponent<SpawnFootprint>() != null &&
              a.name == HealAtom.ObjectName && a.CompareTag("pickUp"));
        var plain = HealAtom.Spawn(new Vector3(0f, 6f, 0f));
        SpawnFootprint.Attach(plain, HealAtom.FootprintHalf, SpawnLayer.Pickup);
        Check("it equals the plain (tutorial) green atom", Describe(a) == Describe(plain));
        Check("the tutorial's green atom is not pooled", plain.GetComponent<PooledObject>() == null);
        var tut = plain;
        Pooled.Remove(tut);
        Check("and is destroyed as before", tut == null);
        a.GetComponent<PickupGlow>().Advance(1f);
        Pooled.Destroy(a);
        var b = HealAtom.SpawnPooled(new Vector3(1f, 6f, 0f));
        Check("reused: same instance, new life", ReferenceEquals(a, b) && Mathf.Approximately(b.GetComponent<PickupGlow>().Age, 0f) &&
              b.GetComponent<PooledObject>().Diff() == null && b.transform.position == new Vector3(1f, 6f, 0f));
        // the below-view despawn goes through the funnel
        b.transform.position = new Vector3(0f, -100f, 0f);
        typeof(HealAtom).GetMethod("Update", Inst).Invoke(b.GetComponent<HealAtom>(), null);
        Check("falling out of view releases it to the pool", !b.activeSelf && PrefabPool.FreeCount(HealAtom.PoolKey) == 1);
    }

    static void RefundMarkNeverSurvives()
    {
        Fresh();
        using var fx = new Fixture();
        var atom = fx.Place(fx.red, Vector3.zero);
        atom.AddComponent<RefundMark>().kind = PowerUpRefund.Kind.Pause;
        Pooled.Destroy(atom);
        Check("collected / gone: the mark is removed with the release (its OnDisable already ran in play)", atom.GetComponent<RefundMark>() == null);
        var again = fx.Place(fx.red, Vector3.zero);
        Check("reused as an ordinary atom: no refund mark, nothing outstanding", ReferenceEquals(atom, again) && again.GetComponent<RefundMark>() == null);
        Pooled.Destroy(again);
    }

    // ------------------------------------------------------------------
    // collecting through the real collision code

    sealed class Rig : IDisposable
    {
        public GameObject ship;
        public collisionDetection cd;
        public Action<Collider2D> trigger;
        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(ship);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.lifeCounter = 0;
        }
    }

    static Rig NewRig()
    {
        var r = new Rig();
        int id = ShipId.Starter;
        r.ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer), typeof(BoxCollider2D));
        r.ship.GetComponent<BoxCollider2D>().isTrigger = true;
        r.ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        spawnShips.ApplyHull(r.ship, id);
        r.cd = r.ship.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = r.ship.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);
        r.trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), r.cd,
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
        r.cd.shield = ShipShield.For(r.ship).Visual;
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 1;       // a heart to repair
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        return r;
    }

    static string Tally()
    {
        return string.Join("|", score.dustPickups, score.tutorialCurrency.ToString("F3"), score.totalCurrency.ToString("F3"), score.pauseCounter,
            collisionDetection.lifeCounter, collisionDetection.atomCheck, collisionDetection.healAtomPickups, collisionDetection.shieldAtomPickups,
            collisionDetection.pauseAtomPickups, collisionDetection.cooldownAtomPickups, collisionDetection.magnetAtomPickups);
    }

    // Collects `pickup` and reports what changed: the tally delta, the burst, the pickup's state.
    static string Collect(Rig r, GameObject pickup, out bool burstPlayed)
    {
        string before = Tally();
        var lastBurst = PickupBurst.LastPlayed;
        r.trigger(pickup.GetComponent<Collider2D>());
        burstPlayed = PickupBurst.LastPlayed != null && PickupBurst.LastPlayed.gameObject.activeSelf;
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        string after = Tally();
        // the effect, not the absolute: reset the counters for the next collection
        var d = new List<string>();
        var a = before.Split('|'); var b = after.Split('|');
        for (int i = 0; i < a.Length; i++) d.Add(a[i] == b[i] ? "=" : a[i] + ">" + b[i]);
        return string.Join("|", d);
    }

    static void ResetTallies()
    {
        score.dustPickups = 0;
        score.tutorialCurrency = 0f;
        score.totalCurrency = 0f;
        score.pauseCounter = 3;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.lifeCounter = 1;
        collisionDetection.healAtomPickups = collisionDetection.shieldAtomPickups = collisionDetection.pauseAtomPickups =
            collisionDetection.cooldownAtomPickups = collisionDetection.magnetAtomPickups = 0;
    }

    static void ScoringAndDropsMatchThePlainPath()
    {
        Fresh();
        var oldTut = score.tutorialCurrency;
        AtomFusions.EnabledOverride = false;   // (fusions chain across catches; this pins the pickup path itself)
        using var fx = new Fixture();
        using var rig = NewRig();
        int types = 0, same = 0, burstsOk = 0, spent = 0;
        var kinds = new List<KeyValuePair<string, Func<Vector3, GameObject>>>
        {
            new KeyValuePair<string, Func<Vector3, GameObject>>("small star", p => fx.Place(fx.smStar, p)),
            new KeyValuePair<string, Func<Vector3, GameObject>>("large star", p => fx.Place(fx.midStar, p)),
            new KeyValuePair<string, Func<Vector3, GameObject>>("blue atom", p => fx.Place(fx.blue, p)),
            new KeyValuePair<string, Func<Vector3, GameObject>>("red atom", p => fx.Place(fx.red, p)),
            new KeyValuePair<string, Func<Vector3, GameObject>>("violet atom", p => fx.Place(fx.violet, p)),
            new KeyValuePair<string, Func<Vector3, GameObject>>("magnet atom", p => fx.Place(fx.magnet, p)),
            new KeyValuePair<string, Func<Vector3, GameObject>>("green atom", p => HealAtom.SpawnPooled(p)),
        };
        foreach (var kind in kinds)
        {
            types++;
            string plainEffect, firstEffect, reusedEffect;
            bool plainBurst, firstBurst, reusedBurst;
            // plain (pool off)
            PrefabPool.ForceInEditMode = false;
            ResetTallies();
            var plain = kind.Value(Vector3.zero);
            plainEffect = Collect(rig, plain, out plainBurst);
            if (plain != null) UnityEngine.Object.DestroyImmediate(plain);
            ClearMagnet(rig);
            PrefabPool.ForceInEditMode = true;
            // pooled, first life
            ResetTallies();
            var first = kind.Value(Vector3.zero);
            firstEffect = Collect(rig, first, out firstBurst);
            bool firstSpent = !first.GetComponent<Collider2D>().enabled;   // (its collider goes off as it is collected)
            ClearMagnet(rig);
            // pooled, second life: the SAME instance after a full lifecycle
            ResetTallies();
            var second = kind.Value(Vector3.zero);
            bool reusedSame = ReferenceEquals(first, second);
            reusedEffect = Collect(rig, second, out reusedBurst);
            bool secondSpent = !second.GetComponent<Collider2D>().enabled;
            ClearMagnet(rig);
            // and a third, to catch a flag that only the second life leaves behind
            ResetTallies();
            var third = kind.Value(Vector3.zero);
            bool thirdEffectSame;
            { bool b3; string e3 = Collect(rig, third, out b3); thirdEffectSame = e3 == plainEffect && b3 == plainBurst; }
            ClearMagnet(rig);
            if (plainEffect == firstEffect && firstEffect == reusedEffect && reusedSame && thirdEffectSame) same++;
            else Debug.Log("[POOL] " + kind.Key + " plain=" + plainEffect + " first=" + firstEffect + " reused=" + reusedEffect + " sameInstance=" + reusedSame + " third=" + thirdEffectSame);
            if (plainBurst && firstBurst && reusedBurst) burstsOk++;
            else Debug.Log("[POOL] " + kind.Key + " burst plain=" + plainBurst + " first=" + firstBurst + " reused=" + reusedBurst);
            if (firstSpent && secondSpent) spent++;
            Pooled.Remove(third);
            PrefabPool.Flush();
        }
        Check("collecting any of the seven pickups scores / drops / counts the same for a plain, a pooled and a reused pickup (" + same + "/" + types + ")", same == types);
        Check("the pickup burst plays for the plain, the first and the reused pickup of every type (" + burstsOk + "/" + types + ")", burstsOk == types);
        Check("a collected pickup is spent (collider off) whichever life it is on (" + spent + "/" + types + ")", spent == types);
        score.tutorialCurrency = oldTut;
    }

    static void ClearMagnet(Rig r)
    {
        var pull = r.ship.GetComponent<MagnetPull>();
        if (pull != null) { pull.End(); UnityEngine.Object.DestroyImmediate(pull); }
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
    }

    // ------------------------------------------------------------------

    static void PrewarmBuildsNothingLater()
    {
        Fresh();
        using var fx = new Fixture();
        typeof(spawnGoodStuff).GetMethod("Prewarm", Inst).Invoke(fx.spawner, null);
        int built = PrefabPool.Created;
        Check("scene start prewarms a modest pool of every type (" + built + " instances)", built >= 12 + 6 + 4 + 1 && built <= 40 &&
              PrefabPool.FreeCount(fx.smStar) == 12 && PrefabPool.FreeCount(fx.midStar) == 6 && PrefabPool.FreeCount(fx.blue) == 1 && PrefabPool.FreeCount(HealAtom.PoolKey) == 1);
        Check("prewarmed instances are off (inactive, unregistered)", PrefabPool.InUse == 0);
        var go = fx.Place(fx.smStar, Vector3.zero);
        var cluster = new List<GameObject> { go };
        for (int i = 0; i < 7; i++) cluster.Add(fx.Place(fx.smStar, new Vector3(0f, i, 0f)));
        Check("a star cluster after the prewarm builds nothing", PrefabPool.Created == built && PrefabPool.Reused == 8);
        foreach (var c in cluster) Pooled.Destroy(c);
        Check("...and comes back whole", PrefabPool.FreeCount(fx.smStar) == 12 && PrefabPool.InUse == 0);
    }

    static void MagnetCompactSkipsPooledGone()
    {
        Fresh();
        var a = Get(Vector3.zero);
        var b = Get(Vector3.zero);
        var list = new Behaviour[] { a.GetComponent<Probe>(), b.GetComponent<Probe>(), null };
        Pooled.Destroy(a);
        var compact = typeof(MagnetPull).GetMethod("Compact", Stat);
        int n = (int)compact.Invoke(null, new object[] { list, 2 });
        Check("the magnet's list drops a pickup that went back to the pool and keeps a live one", n == 1 && list[0] == (Behaviour)b.GetComponent<Probe>() && list[1] == null);
        Pooled.Destroy(b);
    }

    // ------------------------------------------------------------------
    // source audit

    static string Src(string path) { return TestHarness.StripComments(File.ReadAllText("Assets/Scripts/" + path)); }

    // Raw Destroy( / DestroyImmediate( calls: not Pooled.Destroy, not OnDestroy, not a method named Destroy.
    static int RawDestroys(string src)
    {
        return Regex.Matches(src, @"(?<![\w.])(?:Object\.)?(?:Destroy|DestroyImmediate)\(").Count;
    }

    static void SourceAudit()
    {
        string col = Src("Ship/collisionDetection.cs");
        int start = col.IndexOf("else if (hit.gameObject.CompareTag(\"pickUp\"))", StringComparison.Ordinal);
        int end = start < 0 ? -1 : col.IndexOf("public void RaiseShield", start, StringComparison.Ordinal);
        string pickups = start >= 0 && end > start ? col.Substring(start, end - start) : "";
        Check("the pickup branch of collisionDetection is found", pickups.Length > 1000);
        Check("every pickup in it is removed through Pooled.Destroy (7 sites, none raw)", RawDestroys(pickups) == 0 && Regex.Matches(pickups, @"Pooled\.Destroy\(hit\.gameObject\)").Count == 7);
        Check("the generic destroyers (below-camera strip, spawn overlap) release through the funnel", RawDestroys(Src("Gameplay/destoryAnything.cs")) == 0 &&
              RawDestroys(Src("Gameplay/Spawning/spawnCorrectly.cs")) == 0 && Src("Gameplay/destoryAnything.cs").Contains("Pooled.Destroy(") && Src("Gameplay/Spawning/spawnCorrectly.cs").Contains("Pooled.Destroy("));
        Check("the green atom and the atom mover remove themselves through the funnel", RawDestroys(Src("Gameplay/Pickups/HealAtom.cs")) == 0 && RawDestroys(Src("Gameplay/moveItemEnmInStrightLine.cs")) == 0);
        string place = Src("Gameplay/Pickups/spawnGoodStuff.cs");
        Check("the main game's pickup spawner never calls Instantiate (PrefabPool.Get only)", !place.Contains("Instantiate(") && place.Contains("PrefabPool.Get("));
        Check("the green atom's spawners use the pooled spawn", Src("Gameplay/Pickups/HealAtomSpawner.cs").Contains("HealAtom.SpawnPooled(") && !Src("Gameplay/Pickups/HealAtomSpawner.cs").Contains("HealAtom.Spawn("));
        // nothing but the pool itself switches a pooled object on or off or parks it
        Check("Pooled.Destroy has the single release path (PooledObject.Release)", Src("Core/PrefabPool.cs").Contains("po.Release()"));
        // every IPoolReset implementer is on a pickup component that actually holds state
        Check("the components with their own state reset on reuse (flipbook, glow, mover, footprint, clear target)",
              Src("Gameplay/Pickups/PickupFlipbook.cs").Contains("IPoolReset") && Src("Gameplay/Pickups/PickupGlow.cs").Contains("IPoolReset") &&
              Src("Gameplay/moveItemEnmInStrightLine.cs").Contains("IPoolReset") && Src("Gameplay/Spawning/SpawnFootprint.cs").Contains("IPoolReset") &&
              Src("Gameplay/Weapons/ClearTarget.cs").Contains("IPoolReset"));
        // the tutorial keeps raw spawns (scripted pickups are not pooled)
        Check("the tutorial's scripted pickups are untouched (no pool there)", !Src("Tutorial/spawnGoodStuffTut.cs").Contains("PrefabPool"));
        // enemies stay on Instantiate / Destroy: nothing in the enemy factory or spawner is pooled
        Check("enemies are not pooled in this change", !Src("Gameplay/Spawning/enmiesOnBoard.cs").Contains("PrefabPool") && !Src("Gameplay/Enemies/EnemyFactory.cs").Contains("PrefabPool"));
    }
}
