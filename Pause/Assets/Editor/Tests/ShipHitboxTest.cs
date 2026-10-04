using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The player ship's hit zones (ShipHitbox, baked by ShipHitboxBaker):
//   - every ShipId gets a tight polygon damage collider: every point of it
//     (vertices, edges, interior) on painted pixels of the rest drawing, at
//     most 24 points (32 at worst, see ShipHitboxBaker), covering >= 80% of
//     the hull's main body
//   - the same polygon for every skin and damage state
//   - built at spawn from the bake: no readback, no path sent on pickup
//   - with a shield up (blue atom or Hard Shell) the enabled collider is the
//     shield zone, which clears the hull collider and the visible shield line
//     by ShieldMargin; the shield ending brings the tight hull straight back;
//     exactly one collider is enabled at any time
//   - a hazard just outside the hull but inside the margin misses an
//     unshielded ship and is absorbed by a shielded one
//   - pickups collect within PickupRadius (and pay once)
//   - spinners' colliders turn with the hull
//   - SpawnLane's ship gap still clears the widest hull hitbox
public static class ShipHitboxTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HB] PASS  " : "[HB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BakeIsCurrent();
        HullPolygonsAreTightAndOnTheArt();
        SameForEverySkinAndDamageState();
        BuiltAtSpawnWithoutReadback();
        ShieldSwapsTheZone();
        HardShellSwapsTheZone();
        HazardInTheMarginOnlyHitsTheShield();
        PickupsCollectAtTheRadius();
        SpinnersTurnTheCollider();
        SpawnLaneGapClearsTheHull();

        Debug.Log("[HB] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------

    static void BakeIsCurrent()
    {
        Check("hull_hitboxes.bytes is baked and matches the art and ShieldMargin", ShipHitboxBaker.IsCurrent());
        ShipHitbox.Invalidate();
        Check("a hitbox for every roster ship (" + ShipHitbox.Count + "/" + ShipId.Count + ")",
              ShipHitbox.Count == ShipId.Count);
        Check("the bake used the current shield margin (" + ShipHitbox.BakedMargin + ")",
              ShipHitbox.BakedMargin == ShipHitbox.ShieldMargin);
        Check("the shield margin is in the asked-for 0.12-0.18 u band",
              ShipHitbox.ShieldMargin >= .12f && ShipHitbox.ShieldMargin <= .18f);
    }

    static bool Painted(Color32[] px, int w, int h, Vector2 pixel)
    {
        int x = Mathf.FloorToInt(pixel.x), y = Mathf.FloorToInt(pixel.y);
        return x >= 0 && y >= 0 && x < w && y < h && px[y * w + x].a >= ShieldContour.AlphaCutoff;
    }

    static bool Simple(Vector2[] p)
    {
        int n = p.Length;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                Vector2 a = p[i], b = p[(i + 1) % n], c = p[j], d = p[(j + 1) % n];
                float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
                if (((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0)) && d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0)
                    return false;
            }
        return true;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    // Points along every edge, every `step` units.
    static List<Vector2> Outline(Vector2[] poly, float step)
    {
        var pts = new List<Vector2>();
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            float len = (poly[i] - poly[j]).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (int k = 0; k < n; k++) pts.Add(Vector2.Lerp(poly[j], poly[i], k / (float)n));
        }
        return pts;
    }

    static void HullPolygonsAreTightAndOnTheArt()
    {
        int good = 0;
        Debug.Log("[HB] ship                 old box u^2   hull poly u^2  (new/old)   shield zone u^2  points  body cover");
        foreach (int id in ShipId.All)
        {
            string who = "ShipId " + id + " " + ShipId.KeyOf(id);
            var shape = ShipHitbox.ShapeFor(id);
            var sprite = ShipHullArt.StockRest(id);
            Check(who + " has a baked hitbox", shape != null && sprite != null);
            if (shape == null || sprite == null) continue;
            Rect r = sprite.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var px = ShieldContour.ReadFromSourceFile(sprite.texture, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h);
            if (px == null) { Check(who + " source pixels readable", false); continue; }
            float ppu = sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot;
            Func<Vector2, Vector2> toPx = p => p * ppu + pivot;
            var hull = shape.hull;

            Check(who + " hull polygon has 3-" + ShipHitboxBaker.MaxHullPoints + " points (" + hull.Length + ")",
                  hull.Length >= 3 && hull.Length <= ShipHitboxBaker.MaxHullPoints);
            Check(who + " hull polygon is simple", Simple(hull));

            // Every point of the collider is on painted pixels: vertices,
            // the edges (sampled every 0.25 px) and every pixel centre inside.
            int total = 0, off = 0;
            foreach (var p in Outline(hull, .25f / ppu)) { total++; if (!Painted(px, w, h, toPx(p))) off++; }
            int inside = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var local = (new Vector2(x + .5f, y + .5f) - pivot) / ppu;
                    if (!ShieldContour.PointInPolygon(hull, local)) continue;
                    total++; inside++;
                    if (px[y * w + x].a < ShieldContour.AlphaCutoff) off++;
                }
            Check(who + " 100% of the hull collider is on painted pixels (" + off + " of " + total + " points off)",
                  off == 0 && inside > 0);

            // Inset: no point of the outline within the inset of the art's edge.
            var silhouette = ShieldContour.MaskOf(px, w, h);
            var dist = ShieldContour.SquaredDistance(Invert(silhouette), w, h);
            int shallow = 0;
            foreach (var p in Outline(hull, .5f / ppu))
            {
                var q = toPx(p);
                int x = Mathf.Clamp(Mathf.FloorToInt(q.x), 0, w - 1), y = Mathf.Clamp(Mathf.FloorToInt(q.y), 0, h - 1);
                if (dist[y * w + x] <= ShipHitboxBaker.InsetPx * ShipHitboxBaker.InsetPx) shallow++;
            }
            Check(who + " the outline sits at least " + ShipHitboxBaker.InsetPx + " px inside the art (" + shallow + " points closer)",
                  shallow == 0);

            float body = ShipHitboxBaker.BodyArea(silhouette, w, h);
            float areaPx = Mathf.Abs(ShieldContour.SignedArea(hull)) * ppu * ppu;
            float cover = areaPx / body;
            Check(who + " covers " + (cover * 100f).ToString("F1") + "% of the main body (>= 80%)", cover >= .8f);

            // The shield zone clears the hull collider and the visible shield
            // line by at least the margin.
            float scale = shopingShips.NormalizedHullScale(sprite);
            float margin = ShipHitbox.ShieldMargin / scale;
            int hullOut = 0;
            foreach (var p in Outline(hull, .5f / ppu))
                if (!ShieldContour.PointInPolygon(shape.shield, p) ||
                    ShipHitboxBaker.DistanceToPolygon(p, shape.shield) < margin) hullOut++;
            var contour = ShieldContour.ForShip(id);
            int lineOut = 0;
            float closest = float.MaxValue;
            foreach (var p in Outline(contour.Polygon, .5f / ppu))
            {
                float d = ShipHitboxBaker.DistanceToPolygon(p, shape.shield);
                closest = Mathf.Min(closest, d);
                if (!ShieldContour.PointInPolygon(shape.shield, p) || d < margin * .999f) lineOut++;
            }
            Check(who + " shield zone strictly contains the hull collider by >= " + ShipHitbox.ShieldMargin + " u (" + hullOut + " closer)",
                  hullOut == 0);
            Check(who + " shield zone clears the visible shield line by >= the margin (closest " +
                  (closest * scale).ToString("F3") + " u, " + lineOut + " closer)", lineOut == 0);
            Check(who + " shield zone has <= " + ShipHitboxBaker.MaxShieldPoints + " points (" + shape.shield.Length + ")",
                  shape.shield.Length >= 3 && shape.shield.Length <= ShipHitboxBaker.MaxShieldPoints && Simple(shape.shield));

            Vector2 bounds = sprite.bounds.size;
            float oldArea = bounds.x * .78f * bounds.y * .78f * scale * scale;
            float newArea = Mathf.Abs(ShieldContour.SignedArea(hull)) * scale * scale;
            float shieldArea = Mathf.Abs(ShieldContour.SignedArea(shape.shield)) * scale * scale;
            Debug.Log(string.Format("[HB] AREA {0,2} {1,-13} old {2:F4}  new {3:F4}  ({4,5:P0})  shield {5:F4}  {6,2} pts  {7:P1}",
                id, ShipId.KeyOf(id), oldArea, newArea, newArea / oldArea, shieldArea, hull.Length, cover));
            if (off == 0 && cover >= .8f && hullOut == 0 && lineOut == 0) good++;
        }
        Check("every ShipId (" + ShipId.Count + ") has a tight, painted, covering hull hitbox (" + good + ")",
              good == ShipId.Count);
    }

    static bool[] Invert(bool[] m)
    {
        var r = new bool[m.Length];
        for (int i = 0; i < m.Length; i++) r[i] = !m[i];
        return r;
    }

    static bool SamePath(PolygonCollider2D c, Vector2[] want)
    {
        if (c == null || c.pathCount != 1) return false;
        var got = c.GetPath(0);
        if (got.Length != want.Length) return false;
        for (int i = 0; i < got.Length; i++) if ((got[i] - want[i]).sqrMagnitude > 1e-12f) return false;
        return true;
    }

    // ------------------------------------------------------------------

    static void SameForEverySkinAndDamageState()
    {
        int checkedShips = 0;
        foreach (int id in ShipId.All)
        {
            var shape = ShipHitbox.ShapeFor(id);
            if (shape == null) continue;
            bool same = true;
            int variants = 0;
            for (int skin = 0; skin < ShipSkins.CountFor(id); skin++)
            {
                // Equip the skin as the dock does, then dress a ship.
                PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
                PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, skin), 1);
                ShipSkins.Equip(id, skin);
                var go = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer), typeof(BoxCollider2D));
                spawnShips.ApplyHull(go, id);
                var hb = ShipHitbox.Of(go);
                var sr = go.GetComponent<SpriteRenderer>();
                same &= hb != null && SamePath(hb.Hull, shape.hull) && SamePath(hb.ShieldZone, shape.shield);
                variants++;
                // Damage states and idle drawings only swap the sprite.
                for (int state = 0; state < ShipHullArt.States; state++)
                {
                    sr.sprite = ShipHullArt.Get(id, skin, state, state == 0 ? 3 : 0);
                    spawnShips.ApplyHull(go, id);
                    hb = ShipHitbox.Of(go);
                    same &= hb != null && SamePath(hb.Hull, shape.hull) && go.GetComponents<PolygonCollider2D>().Length == 2;
                    variants++;
                }
                Object.DestroyImmediate(go);
            }
            ShipSkins.Equip(id, ShipSkins.Stock);
            Check("ShipId " + id + " the same hull collider for every skin and damage state (" + variants + " variants)", same);
            if (same) checkedShips++;
        }
        Check("all " + ShipId.Count + " ships share one collider across skins and states", checkedShips == ShipId.Count);
    }

    // ------------------------------------------------------------------

    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        public ShipHitbox hb;
        public ShipShield shield;
        public Action<Collider2D> trigger;
        public Action tick;

        public int Enabled()
        {
            int n = 0;
            foreach (var c in ship.GetComponents<Collider2D>()) if (c.enabled) n++;
            return n;
        }

        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(ship);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
        }
    }

    // A gameplay ship as spawnShips + collisionDetection.Start leave it.
    static Rig Spawn(int id)
    {
        var r = new Rig();
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
        r.tick = (Action)Delegate.CreateDelegate(typeof(Action), r.cd,
            typeof(collisionDetection).GetMethod("turnTextsOff", Inst));
        r.cd.shield = ShipShield.For(r.ship).Visual;   // collisionDetection.Start
        r.shield = r.ship.GetComponent<ShipShield>();
        r.hb = ShipHitbox.Of(r.ship);
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.cloakTimer = 0f;
        Physics2D.SyncTransforms();
        return r;
    }

    static GameObject Thing(string name, string tag, Vector3 at, float radius)
    {
        var go = new GameObject(name, typeof(CircleCollider2D));
        go.tag = tag;
        go.transform.position = at;
        var c = go.GetComponent<CircleCollider2D>();
        c.radius = radius;
        c.isTrigger = true;
        return go;
    }

    static void BlueAtom(Rig r)
    {
        var atom = Thing("atom3a(Clone)", "pickUp", r.ship.transform.position, .05f);
        r.trigger(atom.GetComponent<Collider2D>());
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        Object.DestroyImmediate(atom);
    }

    static void Expire(Rig r)
    {
        collisionDetection.invTimer = 0f;
        r.tick();
    }

    static void BuiltAtSpawnWithoutReadback()
    {
        int readbacks = ShieldContour.ReadbackCount;
        int good = 0;
        foreach (int id in ShipId.All)
        {
            var r = Spawn(id);
            var shape = ShipHitbox.ShapeFor(id);
            bool built = r.hb != null && shape != null &&
                         r.ship.GetComponent<BoxCollider2D>() == null &&
                         SamePath(r.hb.Hull, shape.hull) && SamePath(r.hb.ShieldZone, shape.shield) &&
                         r.hb.Hull.enabled && !r.hb.ShieldZone.enabled && r.Enabled() == 1 &&
                         r.hb.Hull.isTrigger && r.hb.ShieldZone.isTrigger;
            Check("ShipId " + id + " spawns with its polygon hull collider on, the shield zone built and off, no box", built);
            int writes = ShipHitbox.PathWrites;
            BlueAtom(r);
            Check("ShipId " + id + " a blue atom sends no path and reads nothing back",
                  ShipHitbox.PathWrites == writes && ShieldContour.ReadbackCount == readbacks);
            if (built) good++;
            r.Dispose();
        }
        Check("no GPU readback for any spawn or pickup (" + (ShieldContour.ReadbackCount - readbacks) + ")",
              ShieldContour.ReadbackCount == readbacks);
        Check("every ship spawned with its hitbox (" + good + "/" + ShipId.Count + ")", good == ShipId.Count);
    }

    // ------------------------------------------------------------------

    static void ShieldSwapsTheZone()
    {
        foreach (int id in new[] { ShipId.Starter, 4, 11, 13, 14 })
        {
            var r = Spawn(id);
            string who = "ShipId " + id + " ";
            Check(who + "unshielded: the tight hull is the one collider", r.hb.Active == r.hb.Hull && r.Enabled() == 1);
            BlueAtom(r);
            Check(who + "blue atom: the shield zone is the one enabled collider",
                  collisionDetection.atomCheck && r.hb.Active == r.hb.ShieldZone && r.hb.ShieldZone.enabled &&
                  !r.hb.Hull.enabled && r.Enabled() == 1 && r.shield.ShieldCollider == r.hb.ShieldZone);
            BlueAtom(r);   // a second atom while shielded
            Check(who + "a second atom keeps exactly one collider on", r.hb.Shielded && r.Enabled() == 1);
            collisionDetection.invTimer = .5f;
            r.tick();
            Check(who + "still shielded until the timer runs out", r.hb.Shielded && r.Enabled() == 1);
            Expire(r);
            Check(who + "shield over: straight back to the tight hull",
                  !collisionDetection.atomCheck && r.hb.Active == r.hb.Hull && r.hb.Hull.enabled &&
                  !r.hb.ShieldZone.enabled && r.Enabled() == 1);

            // Cloak changes no collider.
            collisionDetection.BeginCloak(1f);
            Check(who + "Cloak keeps the tight hull (invulnerable regardless)",
                  collisionDetection.Invulnerable && r.hb.Active == r.hb.Hull && r.Enabled() == 1);
            collisionDetection.cloakTimer = 0f;
            r.Dispose();
        }
    }

    static void HardShellSwapsTheZone()
    {
        int id = -1;
        foreach (int s in ShipId.All) if (ShipLoadoutTable.PowerFor(s) == SecretPower.HardShell) { id = s; break; }
        Check("a roster ship carries Hard Shell (" + id + ")", id > 0);
        if (id < 0) return;
        var r = Spawn(id);
        var power = SecretPowerController.Attach(r.ship, id);
        power.SetMeter(SecretPowerController.Full);
        power.Fire();
        Check("Hard Shell up: the shield zone is the one enabled collider",
              power.ShellUp && r.hb.Shielded && r.hb.ShieldZone.enabled && !r.hb.Hull.enabled && r.Enabled() == 1);
        BlueAtom(r);
        Expire(r);
        Check("the blue atom ending under Hard Shell keeps the shield zone",
              power.ShellUp && r.hb.Shielded && r.Enabled() == 1);
        var rock = Thing("rock", "Astr", r.ship.transform.position, .05f);
        r.trigger(rock.GetComponent<Collider2D>());
        Check("the shell eats the hit and the tight hull is back at once",
              !power.ShellUp && collisionDetection.lifeCounter == 0 && !r.hb.Shielded && r.hb.Hull.enabled && r.Enabled() == 1);
        if (rock != null) Object.DestroyImmediate(rock);
        power.SetMeter(SecretPowerController.Full);
        power.Fire();
        power.Step(power.shellSeconds + .1f);
        Check("an unused shell timing out gives the tight hull back", !power.ShellUp && !r.hb.Shielded && r.Enabled() == 1);
        Object.DestroyImmediate(power);
        r.Dispose();
        foreach (var name in new[] { "~PowerFx", "~fx" })
            for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                Object.DestroyImmediate(go);
    }

    // ------------------------------------------------------------------

    // A point just outside the hull polygon (`gap` world units off its
    // rightmost vertex, along +x), in world space.
    static Vector3 JustOutside(Rig r, float gap)
    {
        var hull = ShipHitbox.ShapeFor(r.hb.ShipIndex).hull;
        int best = 0;
        for (int i = 1; i < hull.Length; i++) if (hull[i].x > hull[best].x) best = i;
        return r.ship.transform.TransformPoint(hull[best]) + Vector3.right * gap;
    }

    static void HazardInTheMarginOnlyHitsTheShield()
    {
        const float HazardRadius = .02f;
        int good = 0;
        foreach (int id in ShipId.All)
        {
            string who = "ShipId " + id + " ";
            var r = Spawn(id);
            // Just outside the tight hull, well inside the margin.
            Vector3 at = JustOutside(r, HazardRadius + .03f);
            var rock = Thing("rock", "Astr", at, HazardRadius);
            Physics2D.SyncTransforms();
            var rc = rock.GetComponent<Collider2D>();
            bool missUnshielded = !r.hb.Hull.Distance(rc).isOverlapped;
            bool clearOfHull = !ShieldContour.PointInPolygon(ShipHitbox.ShapeFor(id).hull,
                                    r.ship.transform.InverseTransformPoint(at));
            Check(who + "a hazard " + (.03f).ToString("F2") + " u off the hull edge misses the unshielded ship",
                  missUnshielded && clearOfHull);

            // Sanity: the same hazard on the hull does hit.
            rock.transform.position = r.ship.transform.position;
            Physics2D.SyncTransforms();
            bool hitsOnHull = r.hb.Hull.Distance(rc).isOverlapped;

            rock.transform.position = at;
            BlueAtom(r);
            Physics2D.SyncTransforms();
            bool shieldTouches = r.hb.ShieldZone.Distance(rc).isOverlapped;
            // Physics reports the contact on the shield zone: absorbed.
            r.trigger(rc);
            bool absorbed = collisionDetection.lifeCounter == 0 && r.shield.CrackedCount > 0;
            Check(who + "with the shield up the same hazard touches the shield zone and is absorbed",
                  hitsOnHull && shieldTouches && absorbed);
            // Even a hazard a full margin out from the hull is inside the zone.
            rock.transform.position = JustOutside(r, ShipHitbox.ShieldMargin);
            Physics2D.SyncTransforms();
            bool farTouch = r.hb.ShieldZone.Distance(rc).isOverlapped;
            Check(who + "the shield zone reaches a hazard a full margin (" + ShipHitbox.ShieldMargin + " u) off the hull", farTouch);
            if (missUnshielded && clearOfHull && hitsOnHull && shieldTouches && absorbed && farTouch) good++;
            if (rock != null) Object.DestroyImmediate(rock);
            r.Dispose();
        }
        Check("margin hazards: miss unshielded, absorbed shielded, on all " + ShipId.Count + " ships (" + good + ")",
              good == ShipId.Count);
    }

    // ------------------------------------------------------------------

    static void PickupsCollectAtTheRadius()
    {
        Check("the pickup radius (" + ShipHitbox.PickupRadius + " u) is forgiving: at least the old box's half-width",
              ShipHitbox.PickupRadius >= shopingShips.ReferenceHullSize * .78f * .5f);
        int good = 0;
        foreach (int id in ShipId.All)
        {
            string who = "ShipId " + id + " ";
            var r = Spawn(id);
            bool ok = true;
            // A small star dust at every bearing just inside the radius.
            for (int a = 0; a < 360; a += 45)
            {
                Vector3 dir = Quaternion.Euler(0f, 0f, a) * Vector3.up;
                var dust = Thing("smStar1(Clone)", "pickUp", r.ship.transform.position + dir * (ShipHitbox.PickupRadius - .03f), .02f);
                Physics2D.SyncTransforms();
                int caught = r.hb.CatchPickups();
                bool collected = caught == 1 && !dust.GetComponent<Collider2D>().enabled;
                if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
                ok &= collected;
                Object.DestroyImmediate(dust);
            }
            Check(who + "dust within " + ShipHitbox.PickupRadius + " u is collected at every bearing", ok);

            // Out of reach of both the radius and the hull: not collected.
            var far = Thing("smStar1(Clone)", "pickUp", r.ship.transform.position + Vector3.right * .5f, .02f);
            Physics2D.SyncTransforms();
            bool left = r.hb.CatchPickups() == 0 && far.GetComponent<Collider2D>().enabled &&
                        !r.hb.Hull.Distance(far.GetComponent<Collider2D>()).isOverlapped;
            Check(who + "dust 0.5 u away stays", left);
            Object.DestroyImmediate(far);

            // Both routes in one step pay once.
            int before = collisionDetection.healAtomPickups;
            var heal = Thing(HealAtom.ObjectName + "(Clone)", "pickUp", r.ship.transform.position, .05f);
            Physics2D.SyncTransforms();
            r.hb.CatchPickups();
            r.trigger(heal.GetComponent<Collider2D>());
            if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
            bool once = collisionDetection.healAtomPickups == before + 1;
            Check(who + "a pickup reached by the radius and the hull in one step pays once", once);
            Object.DestroyImmediate(heal);
            if (ok && left && once) good++;
            r.Dispose();
        }
        Check("pickups collect at the documented radius on all " + ShipId.Count + " ships (" + good + ")", good == ShipId.Count);
    }

    // ------------------------------------------------------------------

    static void SpinnersTurnTheCollider()
    {
        foreach (int id in ShipId.All)
        {
            if (!ShipExhaust.UsesSpinDrift(id)) continue;
            var r = Spawn(id);
            var hull = ShipHitbox.ShapeFor(id).hull;
            var sprite = ShipHullArt.StockRest(id);
            float px = 1f / sprite.pixelsPerUnit;
            int mismatched = 0, samples = 0;
            for (int deg = 0; deg < 360; deg += 30)
            {
                r.ship.transform.rotation = Quaternion.Euler(0f, 0f, deg + 7f);
                Physics2D.SyncTransforms();
                Bounds b = sprite.bounds;
                for (float y = b.min.y; y <= b.max.y; y += b.size.y / 24f)
                    for (float x = b.min.x; x <= b.max.x; x += b.size.x / 24f)
                    {
                        var local = new Vector2(x, y);
                        if (ShipHitboxBaker.DistanceToPolygon(local, hull) < 2f * px) continue;   // edge: skip
                        bool want = ShieldContour.PointInPolygon(hull, local);
                        bool got = r.hb.Hull.OverlapPoint(r.ship.transform.TransformPoint(local));
                        samples++;
                        if (want != got) mismatched++;
                    }
            }
            Check("spinner ShipId " + id + ": the hull collider lives on the spinning transform",
                  r.hb.Hull.transform == r.ship.transform && r.hb.ShieldZone.transform == r.ship.transform);
            Check("spinner ShipId " + id + ": collider matches the drawn hull at 12 rotations (" + mismatched + "/" + samples + " off)",
                  samples > 0 && mismatched == 0);
            r.Dispose();
        }
    }

    // ------------------------------------------------------------------

    static void SpawnLaneGapClearsTheHull()
    {
        float widest = 0f;
        int widestId = 0;
        foreach (int id in ShipId.All)
        {
            var shape = ShipHitbox.ShapeFor(id);
            var sprite = ShipHullArt.StockRest(id);
            if (shape == null || sprite == null) continue;
            float scale = shopingShips.NormalizedHullScale(sprite);
            float width;
            if (ShipExhaust.UsesSpinDrift(id))
            {
                // A spinner sweeps its farthest point all the way round.
                float reach = 0f;
                foreach (var p in shape.hull) reach = Mathf.Max(reach, p.magnitude);
                width = 2f * reach * scale;
            }
            else
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var p in shape.hull) { min = Mathf.Min(min, p.x); max = Mathf.Max(max, p.x); }
                width = (max - min) * scale;
            }
            if (width > widest) { widest = width; widestId = id; }
        }
        Debug.Log("[HB] widest hull hitbox: " + widest.ToString("F3") + " u (ShipId " + widestId + "), SpawnLane.ShipGap " +
                  SpawnLane.ShipGap.ToString("F3") + " u");
        Check("SpawnLane's ship gap (" + SpawnLane.ShipGap.ToString("F3") + " u) clears the widest hull hitbox (" +
              widest.ToString("F3") + " u) with >= 30% to spare", SpawnLane.ShipGap >= widest * 1.3f - 1e-4f);
    }
}
