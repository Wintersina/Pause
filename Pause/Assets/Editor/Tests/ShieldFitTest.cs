using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The blue-atom shield wraps each hull like a glove (ShipShield +
// ShieldContour) instead of the old round bubble:
//   - every roster ship gets an outline that covers all of its hull pixels
//     and hugs them (bounded padding, every outline point close to the hull)
//   - the outline is cached per ship type, never rebuilt per frame/activation
//   - activation, hit and expiry sequences play; the shatter pool is bounded
//   - nothing animates at timeScale 0
//   - collisionDetection's absorb rules and 5.8 s timer are unchanged
public static class ShieldFitTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SF] PASS  " : "[SF] FAIL  ") + what);
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

        RosterContoursHugEveryHull();
        ContourIsCachedPerShipType();
        SequencesPlay();
        FrozenAtTimeScaleZero();
        ShardPoolIsBounded();
        HitAbsorbRulesUnchanged();

        Debug.Log("[SF] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------

    static void RosterContoursHugEveryHull()
    {
        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            string name = "ship" + i + " " + shopingShips.NameFor(i);
            Sprite sprite = shopingShips.SpriteFor(i, 0);
            Check(name + " has hull art", sprite != null);
            if (sprite == null) continue;

            var contour = ShieldContour.For(sprite);
            Check(name + " gets a contour shield", contour != null && contour.Polygon.Length >= 3);
            if (contour == null) continue;

            Rect r = sprite.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var truth = ShieldContour.ReadFromSourceFile(sprite.texture,
                Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), w, h);
            Check(name + " source pixels readable for the check", truth != null);
            if (truth == null) continue;

            // The runtime readback (GPU) sees the same silhouette as the PNG.
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                int rw, rh;
                var runtime = ShieldContour.ReadPixels(sprite, out rw, out rh);
                int mismatch = 0;
                if (runtime == null || runtime.Length != truth.Length) mismatch = -1;
                else
                    for (int k = 0; k < truth.Length; k++)
                        if ((truth[k].a >= ShieldContour.AlphaCutoff) != (runtime[k].a >= ShieldContour.AlphaCutoff)) mismatch++;
                Check(name + " runtime pixel readback matches the art (" + mismatch + " px differ)",
                      mismatch >= 0 && mismatch <= truth.Length / 100);
            }

            float ppu = sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot;
            var hullPts = new List<Vector2>();
            int uncovered = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (truth[y * w + x].a < ShieldContour.AlphaCutoff) continue;
                    hullPts.Add(new Vector2((x + .5f - pivot.x) / ppu, (y + .5f - pivot.y) / ppu));
                    for (int c = 0; c < 4; c++)
                    {
                        float cx = x + (c % 2 == 0 ? .02f : .98f), cy = y + (c < 2 ? .02f : .98f);
                        var q = new Vector2((cx - pivot.x) / ppu, (cy - pivot.y) / ppu);
                        if (!ShieldContour.PointInPolygon(contour.Polygon, q)) { uncovered++; break; }
                    }
                }
            Check(name + " shield covers every hull pixel (" + uncovered + " of " + hullPts.Count + " outside)",
                  hullPts.Count > 0 && uncovered == 0);

            // Hug: outline bounds within 16% of the sprite's longest edge of
            // the visible hull bounds on every side.
            float maxDim = Mathf.Max(w, h) / ppu;
            float allowed = .18f * maxDim;
            Rect vb = contour.VisibleBounds, ob = contour.OutlineBounds;
            float worst = Mathf.Max(Mathf.Max(vb.xMin - ob.xMin, ob.xMax - vb.xMax),
                                    Mathf.Max(vb.yMin - ob.yMin, ob.yMax - vb.yMax));
            Check(name + " outline bounds within 18% padding of the hull (" +
                  (worst / maxDim * 100f).ToString("F1") + "%)", worst <= allowed && worst > 0f);

            // Glove, not bubble: every outline sample sits close to the hull.
            float maxGap = 0f, sumGap = 0f;
            foreach (var s in contour.SamplePoint)
            {
                float best = float.MaxValue;
                foreach (var p in hullPts) best = Mathf.Min(best, (p - s).sqrMagnitude);
                float d = Mathf.Sqrt(best) / contour.Unit;
                maxGap = Mathf.Max(maxGap, d);
                sumGap += d;
            }
            float meanGap = sumGap / contour.SamplePoint.Length;
            Check(name + " outline follows the hull (mean " + meanGap.ToString("F1") + "U, max " +
                  maxGap.ToString("F1") + "U from it)",
                  meanGap <= ShieldContour.DilateU + 1.5f && maxGap <= ShieldContour.DilateU + 5f);

            // A bubble round the same hull would be far bigger.
            float bubbleR = Mathf.Max(vb.width, vb.height) * .5f * 1.35f;
            float bubbleArea = Mathf.PI * bubbleR * bubbleR;
            Check(name + " shield area is under the old bubble's",
                  Mathf.Abs(ShieldContour.SignedArea(contour.Polygon)) < bubbleArea);

            Check(name + " has plates, line and ink geometry",
                  contour.PlateCount >= 6 && contour.QuadCount > contour.PlateCount &&
                  contour.Triangles.Length % 3 == 0);
        }
    }

    static GameObject MakeShip(int index)
    {
        var go = new GameObject("ship" + index + "(Clone)", typeof(SpriteRenderer));
        var sprite = shopingShips.SpriteFor(index, 0);
        go.GetComponent<SpriteRenderer>().sprite = sprite;
        float s = shopingShips.NormalizedHullScale(sprite);
        go.transform.localScale = new Vector3(s, s, 1f);
        return go;
    }

    static void ContourIsCachedPerShipType()
    {
        var sprite = shopingShips.SpriteFor(4, 0);
        var first = ShieldContour.For(sprite);
        int builds = ShieldContour.BuildCount;
        Check("asking again returns the cached contour",
              ReferenceEquals(ShieldContour.For(sprite), first) && ShieldContour.BuildCount == builds);

        var ship = MakeShip(4);
        var shield = ShipShield.For(ship);
        for (int round = 0; round < 3; round++)
        {
            shield.remainingOverride = 5f;
            shield.Show();
            for (int f = 0; f < 90; f++) shield.Tick(1f / 60f);
            shield.Hide();
        }
        Check("3 activations x 90 frames build nothing new (" + (ShieldContour.BuildCount - builds) + " builds)",
              ShieldContour.BuildCount == builds);
        Check("the ship's shield uses the shared per-type contour", ReferenceEquals(shield.Contour, first));

        // A second ship of the same type shares it too.
        var twin = MakeShip(4);
        var twinShield = ShipShield.For(twin);
        twinShield.Show();
        Check("a second ship of the same type reuses the contour",
              ReferenceEquals(twinShield.Contour, first) && ShieldContour.BuildCount == builds);
        Object.DestroyImmediate(twin);
        Object.DestroyImmediate(ship);
    }

    static int Count(ShipShield s, byte kind, Color32 c)
    {
        var ct = s.Contour; int n = 0;
        for (int q = 0; q < ct.QuadCount; q++)
        {
            if (ct.QuadKind[q] != kind) continue;
            var v = s.Colors[ct.FillVertexCount + q * 4];
            if (v.r == c.r && v.g == c.g && v.b == c.b && v.a == c.a) n++;
        }
        return n;
    }

    static int Total(ShipShield s, byte kind)
    {
        int n = 0;
        foreach (var k in s.Contour.QuadKind) if (k == kind) n++;
        return n;
    }

    static void SequencesPlay()
    {
        var ship = MakeShip(10);   // Paranoid: outboard engine pods
        var shield = ShipShield.For(ship);
        shield.remainingOverride = 5f;
        const float dt = 1f / 60f;

        // Activation.
        shield.Show();
        Check("activation starts with the anticipation flash at the nose",
              shield.CurrentPhase == ShipShield.Phase.Anticipation && shield.SparkShowing &&
              Count(shield, ShieldContour.KindInk, ShieldArt.Ink) == 0);
        float t = 0f;
        while (shield.CurrentPhase == ShipShield.Phase.Anticipation && t < 1f) { shield.Tick(dt); t += dt; }
        for (int i = 0; i < 4; i++) { shield.Tick(dt); t += dt; }
        int inkMid = Count(shield, ShieldContour.KindInk, ShieldArt.Ink);
        int inkAll = Total(shield, ShieldContour.KindInk);
        Check("mid-zip the shield is partly wrapped (" + inkMid + "/" + inkAll + " ink segments)",
              shield.CurrentPhase == ShipShield.Phase.Zip && inkMid > 0 && inkMid < inkAll &&
              Count(shield, ShieldContour.KindLine, ShieldArt.Hot) > 0);
        while (shield.CurrentPhase != ShipShield.Phase.Idle && t < 1f) { shield.Tick(dt); t += dt; }
        Check("the zip closes in about 0.25 s (" + t.ToString("F2") + " s)", t >= .2f && t <= .32f);
        Check("fully wrapped once idle",
              Count(shield, ShieldContour.KindInk, ShieldArt.Ink) == inkAll && !shield.SparkShowing);

        // Idle: marching dashes.
        for (int i = 0; i < 20; i++) shield.Tick(dt);
        var before = (Color32[])shield.Colors.Clone();
        for (int i = 0; i < 6; i++) shield.Tick(dt);
        bool marched = false;
        for (int i = 0; i < before.Length && !marched; i++)
            marched = before[i].r != shield.Colors[i].r || before[i].g != shield.Colors[i].g;
        Check("idle highlight dashes march round the outline",
              marched && Count(shield, ShieldContour.KindLine, ShieldArt.Dash) > 0);

        // Hit.
        Vector3 right = ship.transform.TransformPoint(new Vector3(shield.Contour.OutlineBounds.xMax, 0f, 0f));
        shield.Absorb(right);
        Check("a hit shows the impact frame (line flashes white) and the impact flipbook",
              shield.FlashShowing &&
              Count(shield, ShieldContour.KindLine, ShieldArt.Hot) == Total(shield, ShieldContour.KindLine));
        Check("the plates at the hit crack (" + shield.CrackedCount + ")", shield.CrackedCount >= 1);
        for (int i = 0; i < 6; i++) shield.Tick(dt);
        int rippleHot = Count(shield, ShieldContour.KindLine, ShieldArt.Hot);
        Check("a hard ripple runs along the outline (" + rippleHot + " hot segments)",
              rippleHot > 0 && rippleHot < Total(shield, ShieldContour.KindLine) / 2);
        for (int i = 0; i < 40; i++) shield.Tick(dt);
        Check("the impact flipbook finishes, the cracks stay",
              !shield.FlashShowing && shield.CrackedCount >= 1);

        // Expiry: blink with an accelerating rhythm.
        var toggles = new List<float>();
        bool last = shield.MeshVisible;
        float remaining = 2f;
        while (remaining > 0f)
        {
            shield.remainingOverride = remaining;
            shield.Tick(dt);
            if (shield.MeshVisible != last) { toggles.Add(remaining); last = shield.MeshVisible; }
            remaining -= dt;
        }
        Check("no blinking before the last 1.5 s", toggles.Count > 0 && toggles[0] <= ShipShield.ExpireWindow + dt);
        int early = 0, late = 0;
        foreach (var r in toggles) { if (r > .75f) early++; else late++; }
        Check("the expiry blink accelerates (" + early + " toggles in 1.5-0.75 s, " + late + " in the last 0.75 s)",
              early >= 2 && late > early);

        // End: shatter.
        for (int i = 0; i < 60; i++) ShieldShards.Instance.Tick(dt);
        int activeBefore = ShieldShards.Instance.ActiveCount;
        shield.Hide();
        Check("ending shatters the plates into shards (" + (ShieldShards.Instance.ActiveCount - activeBefore) + ")",
              ShieldShards.Instance.ActiveCount > activeBefore && !shield.Visual.activeSelf && !shield.IsUp);
        for (int i = 0; i < 60; i++) ShieldShards.Instance.Tick(dt);
        Check("shards are gone after their short life", ShieldShards.Instance.ActiveCount == 0);
        Object.DestroyImmediate(ship);
    }

    static void FrozenAtTimeScaleZero()
    {
        var ship = MakeShip(13);
        var shield = ShipShield.For(ship);
        shield.remainingOverride = 1f;   // inside the blink window too
        shield.Show();
        for (int i = 0; i < 30; i++) shield.Tick(1f / 60f);
        shield.Absorb(ship.transform.position + Vector3.up);

        float saved = Time.timeScale;
        Time.timeScale = 0f;
        Check("scaled delta is zero while frozen", ShipShield.ScaledDelta() == 0f);
        float clock = shield.Clock;
        var colors = (Color32[])shield.Colors.Clone();
        bool visible = shield.MeshVisible;
        var update = typeof(ShipShield).GetMethod("Update", Inst);
        for (int i = 0; i < 30; i++) update.Invoke(shield, null);
        bool same = shield.Clock == clock && shield.MeshVisible == visible;
        for (int i = 0; i < colors.Length && same; i++)
            same = colors[i].r == shield.Colors[i].r && colors[i].g == shield.Colors[i].g &&
                   colors[i].b == shield.Colors[i].b && colors[i].a == shield.Colors[i].a;
        Check("no shield animation advances at timeScale 0", same && shield.FlashShowing);

        shield.Hide();
        var pool = ShieldShards.Instance;
        var shard = pool.transform.childCount > 0 ? pool.transform.GetChild(0) : null;
        Vector3 at = shard != null ? shard.position : Vector3.zero;
        var poolUpdate = typeof(ShieldShards).GetMethod("Update", Inst);
        for (int i = 0; i < 30; i++) poolUpdate.Invoke(pool, null);
        Check("shards hang frozen at timeScale 0",
              shard != null && shard.position == at && pool.ActiveCount > 0);
        Time.timeScale = saved;
        for (int i = 0; i < 60; i++) pool.Tick(1f / 60f);
        Object.DestroyImmediate(ship);
    }

    static void ShardPoolIsBounded()
    {
        var ship = MakeShip(6);
        var shield = ShipShield.For(ship);
        for (int round = 0; round < 12; round++)
        {
            shield.Show();
            shield.Tick(.4f);
            shield.Hide();
        }
        var pool = ShieldShards.Instance;
        Check("12 back-to-back shatters stay inside the pool (" + pool.Created + " created, cap " +
              ShieldShards.Capacity + ")",
              pool.Created <= ShieldShards.Capacity && pool.transform.childCount <= ShieldShards.Capacity &&
              pool.ActiveCount <= ShieldShards.Capacity);
        Object.DestroyImmediate(ship);
    }

    // collisionDetection's rules, driven through its real trigger handler:
    // a blue atom gives the 5.8 s shield; while it is up hazards are destroyed
    // without damage (and the shield shows the hit); without it they damage
    // the hull; the timer running out drops the shield.
    static void HitAbsorbRulesUnchanged()
    {
        var ship = MakeShip(1);
        ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var box = ship.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        var cd = ship.AddComponent<collisionDetection>();
        cd.explosionAnimation = new GameObject("~TestExplosion");
        cd.boostSound = ship.AddComponent<AudioSource>();
        cd.atomTimerText = new GameObject("~atomText", typeof(RectTransform)).AddComponent<Text>();
        cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        cd.boost = new GameObject("~boost");
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        var trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);
        var tick = typeof(collisionDetection).GetMethod("turnTextsOff", Inst);

        var atom = new GameObject("atom3a(Clone)", typeof(CircleCollider2D));
        atom.tag = "pickUp";
        trigger.Invoke(cd, new object[] { atom.GetComponent<Collider2D>() });
        var shield = ship.GetComponent<ShipShield>();
        Check("a blue atom still grants the 5.8 s shield",
              collisionDetection.atomCheck && Mathf.Approximately(collisionDetection.invTimer, 5.8f) &&
              shield != null && shield.IsUp);
        Check("while shielded the hit zone is the shield outline (box handed over)",
              shield != null && shield.ShieldCollider != null && shield.ShieldCollider.enabled && !box.enabled);
        if (shield != null && shield.ShieldCollider != null)
        {
            var path = shield.ShieldCollider.GetPath(0);
            Vector2 c = box.offset;
            Vector2 e = box.size * .5f - Vector2.one * (shield.Contour.Unit * .3f);
            int boxOut = 0;
            for (int k = 0; k < 4; k++)
                if (!ShieldContour.PointInPolygon(path, c + new Vector2(k % 2 == 0 ? e.x : -e.x, k < 2 ? e.y : -e.y)))
                    boxOut++;
            int outlineOut = 0;
            var ct = shield.Contour;
            for (int k = 0; k < ct.SamplePoint.Length; k++)
                if (!ShieldContour.PointInPolygon(path, ct.SamplePoint[k] - ct.SampleNormal[k] * ct.Unit))
                    outlineOut++;
            Check("the shielded hit zone contains both the normal hitbox and the visible shield (" +
                  boxOut + " box corners, " + outlineOut + "/" + ct.SamplePoint.Length + " outline points outside)",
                  boxOut == 0 && outlineOut == 0);
        }

        var rock = new GameObject("rock", typeof(CircleCollider2D));
        rock.tag = "Astr";
        rock.transform.position = ship.transform.position + Vector3.right * .3f;
        trigger.Invoke(cd, new object[] { rock.GetComponent<Collider2D>() });
        Check("a hazard hitting the shield does no damage", collisionDetection.lifeCounter == 0);
        Check("and the shield plays its hit", shield != null && shield.FlashShowing && shield.CrackedCount > 0);

        collisionDetection.invTimer = 0f;
        tick.Invoke(cd, null);
        Check("the timer running out drops the shield and gives the box back",
              !collisionDetection.atomCheck && shield != null && !shield.IsUp && box.enabled &&
              !shield.ShieldCollider.enabled);

        var rock2 = new GameObject("rock2", typeof(CircleCollider2D));
        rock2.tag = "Astr";
        trigger.Invoke(cd, new object[] { rock2.GetComponent<Collider2D>() });
        Check("without the shield a hazard damages the hull", collisionDetection.lifeCounter == 1);

        collisionDetection.lifeCounter = 0;
        foreach (var go in new[] { atom, rock, rock2, cd.explosionAnimation, cd.boost,
                                   cd.atomTimerText.gameObject, cd.boostText.gameObject, cd.hypeText.gameObject })
            if (go != null) Object.DestroyImmediate(go);
        Object.DestroyImmediate(ship);
    }
}
