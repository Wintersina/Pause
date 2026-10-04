using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bug: the life hearts floated over the nose, right where the ultimate's
// charge indicator sits (Turtle's hex shell plates and the rest), so the two
// drew on top of each other. The hearts now take the first free ShipUiSlots
// slot -- clear of the hull, its exhaust and every registered ship element in
// every pose -- and stay inside the safe area.
//
// The spinners (Ninja, UFO) instead have their hearts orbit the spinning
// hull, upright, ducking round whatever is drawn on the ring this frame
// (gun, charge indicator, secret meter), on screen at the edges and frozen
// with the world.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HeartsPlacementTest.Run
public static class HeartsPlacementTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HP] PASS  " : "[HP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;

    // Roughly the gameplay view: CameraFit's minimum half width, ortho 5.
    static readonly Rect Gameplay = Rect.MinMaxRect(-2.85f, -5f, 2.85f, 5f);
    static readonly Rect Everywhere = Rect.MinMaxRect(-100f, -100f, 100f, 100f);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        try
        {
            NoIndicatorKeepsTheHeartsAboveTheNose();
            NeverOverlapInAnyPose();
            StayOnScreenAtTheEdges();
            TallScreens();
            AnExtraIndicatorTakesTheNextSlot();
            SpinnersOrbit();
            NonSpinnersStayInTheirSlot();
            OrbitFreezesWithTheWorld();
            EveryHeartCountFits();
        }
        finally
        {
            ShipUiSlots.ScreenOverride = null;
            Time.timeScale = 1f;
        }
        Debug.Log("[HP] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    public class Rig
    {
        public int id;
        public GameObject ship;
        public ShipPowerController power;
        public ChargeIndicator indicator;
        public UltimateGun gun;
        public ShipLivesIndicator hearts;
        public SecretMeter meter;
    }

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        camGo.GetComponent<Camera>().orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
    }

    // A flying hull as gameS1 spawns it (roster art, normalised scale), its
    // ultimate (gun + charge indicator) and its hearts.
    // `hearts` 0 is the ship's own lives (ShipLives.Max), else 2..5.
    public static Rig Build(int id, Vector3 at, bool withPower = true, int hearts = 0)
    {
        collisionDetection.MAXLIFE = hearts > 0 ? hearts : ShipLives.Max(id);
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject(ShipId.ObjectName(id), typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(id);
        sr.sortingOrder = 10;
        float scale = shopingShips.NormalizedHullScale(sr.sprite);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        go.transform.position = at;
        var rig = new Rig { id = id, ship = go };
        if (withPower)
        {
            rig.power = go.AddComponent<ShipPowerController>();
            rig.power.SendMessage("Awake");
            rig.power.SendMessage("Start");
            rig.gun = go.GetComponentInChildren<UltimateGun>();
            if (rig.gun != null) rig.gun.SendMessage("Awake");
            rig.indicator = rig.power.Indicator;
            rig.meter = go.GetComponentInChildren<SecretMeter>();
        }
        rig.hearts = go.AddComponent<ShipLivesIndicator>();
        rig.hearts.BuildHearts(collisionDetection.MAXLIFE);
        return rig;
    }

    public static void Teardown(Rig rig)
    {
        if (rig.power != null) rig.power.SendMessage("OnDestroy");
        if (rig.meter != null) rig.meter.SendMessage("OnDestroy");
        Object.DestroyImmediate(rig.ship);
    }

    static void SetTimer(ShipPowerController c, float seconds) =>
        typeof(ShipPowerController).GetField("timer", Inst).SetValue(c, seconds);

    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Inst).GetValue(o);

    static Vector3 Hover(int id, float t) =>
        (Vector3)typeof(UltimateGun).GetMethod("HoverOffset", Static).Invoke(null, new object[] { id, t });

    // Puts the ultimate into one pose: `extend` 0 is the gun tucked beside the
    // hull (charging), 1 the gun in its firing slot with the indicator on its
    // muzzle (ready), `grow` its scale (fire pop on top of the extension).
    public static void Pose(Rig rig, float secondsLeft, float extend, float hoverT, float grow, float spinDegrees = 0f)
    {
        rig.ship.transform.rotation = Quaternion.Euler(0f, 0f, spinDegrees);
        if (rig.power == null) { rig.hearts.Place(0f); return; }
        SetTimer(rig.power, secondsLeft);
        if (rig.gun != null)
        {
            typeof(UltimateGun).GetField("extend", Inst).SetValue(rig.gun, extend);
            Vector3 rest = Field<Vector3>(rig.gun, "restingOffset");
            Vector3 fire = Field<Vector3>(rig.gun, "firingOffset");
            Vector3 h = Hover(rig.id, hoverT);
            Vector3 at = Vector3.Lerp(rest + h, fire + h * .2f, extend);
            if (ShipUiSlots.Spins(rig.id))
            {
                // a spinner's gun stays upright beside the hull (UltimateGun.Reposition)
                var hull = rig.ship.transform;
                rig.gun.transform.position = hull.position + Vector3.Scale(at, hull.lossyScale);
                rig.gun.transform.rotation = Quaternion.identity;
            }
            else rig.gun.transform.localPosition = at;
            rig.gun.transform.localScale = Vector3.one * grow;
        }
        rig.indicator.Step(.02f, .02f);
        if (rig.meter != null) rig.meter.Step(0f);
        rig.hearts.Place(0f);
    }

    public static Bounds Visible(Renderer r) => r.bounds;

    public static List<Bounds> HeartBounds(Rig rig)
    {
        var list = new List<Bounds>();
        foreach (var h in rig.hearts.Hearts)
            if (h != null && h.gameObject.activeSelf && h.GetComponent<SpriteRenderer>().enabled)
                list.Add(h.GetComponent<SpriteRenderer>().bounds);
        return list;
    }

    public static bool Hits(List<Bounds> hearts, Bounds other)
    {
        foreach (var h in hearts) if (ShipUiSlots.Overlaps(h, other)) return true;
        return false;
    }

    // Every pose worth checking: idle, charging, ready (riding onto the
    // muzzle, at both scales), the squash just before it fires, all through
    // the gun's hover cycle -- and, for a spinner, all the way round.
    public static IEnumerable<string> Poses(Rig rig)
    {
        float cooldown = rig.power != null ? Field<float>(rig.power, "cooldown") : 30f;
        // a spinner's hearts go round once per 1/orbitRate turns of the hull
        var spins = new List<float> { 0f };
        if (ShipUiSlots.Spins(rig.id))
            for (float a = 20f; a < 360f / rig.hearts.orbitRate; a += 20f) spins.Add(a);
        foreach (float spin in spins)
            for (float t = 0f; t < 6.3f; t += .45f)
            {
                Pose(rig, cooldown, 0f, t, 1f, spin);
                yield return "idle";
                Pose(rig, cooldown * .5f, 0f, t, 1f, spin);
                yield return "charging";
                Pose(rig, .9f, .5f, t, 1f + .07f, spin);
                yield return "ready (sliding out)";
                Pose(rig, .5f, 1f, t, 1.14f, spin);
                yield return "ready";
                Pose(rig, .1f, 1f, t, 1.38f, spin);
                yield return "anticipation";
            }
    }

    // ---- cases ---------------------------------------------------------

    // Nothing in front of the nose: the hearts stay where they always were.
    static void NoIndicatorKeepsTheHeartsAboveTheNose()
    {
        FreshScene();
        ShipUiSlots.ScreenOverride = () => Everywhere;
        var rig = Build(15, Vector3.zero, withPower: false);
        rig.hearts.Place(0f);
        Check("with nothing registered the hearts float above the nose",
              rig.hearts.Side == ShipUiSlots.Side.Above &&
              HeartBounds(rig)[0].min.y > rig.ship.GetComponent<SpriteRenderer>().bounds.max.y);
        Teardown(rig);
    }

    static void NeverOverlapInAnyPose()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        var turtleSide = ShipUiSlots.Side.Above;
        foreach (int id in ShipId.All)
        {
            FreshScene();
            var rig = Build(id, new Vector3(0f, -2f, 0f));
            Check("ship " + id + " registers its charge indicator and gun",
                  ShipUiSlots.IsRegistered(rig.indicator) && ShipUiSlots.IsRegistered(rig.gun));
            var barrel = rig.gun.transform.Find("Barrel").GetComponent<SpriteRenderer>();
            var hull = rig.ship.GetComponent<SpriteRenderer>();
            var view = rig.indicator.View.GetComponent<SpriteRenderer>();
            string hitIndicator = null, hitHull = null, hitGun = null, hitMeter = null;
            int poses = 0, shown = 0, slots = 0;
            bool orbit = rig.hearts.Orbiting;
            foreach (string pose in Poses(rig))
            {
                poses++;
                var hearts = HeartBounds(rig);
                shown += hearts.Count;
                slots += rig.hearts.Hearts.Length;
                if (hitIndicator == null && Hits(hearts, view.bounds)) hitIndicator = pose;
                // a spinning hull: the circle its sprite sweeps (its renderer
                // box only grows on the diagonals)
                if (hitHull == null && (orbit ? HitsCircle(hearts, rig.ship.transform.position, HullRadius(rig))
                                              : Hits(hearts, hull.bounds))) hitHull = pose;
                if (hitGun == null && Hits(hearts, orbit ? ShipUiSlots.DrawnBounds(rig.gun.transform) : barrel.bounds)) hitGun = pose;
                if (hitMeter == null && rig.meter != null && Hits(hearts, rig.meter.GetComponent<SpriteRenderer>().bounds))
                    hitMeter = pose + " at spin " + rig.ship.transform.eulerAngles.z.ToString("F0");
            }
            if (orbit)
                Debug.Log("[HP] " + ShipId.KeyOf(id) + " orbit r=" + rig.hearts.OrbitRadius.ToString("F2") +
                          ", hearts shown " + (100f * shown / Mathf.Max(1, slots)).ToString("F0") + "% of the time");
            string name = ShipId.KeyOf(id) + " (" + id + ", hearts " + (rig.hearts.Orbiting ? "orbiting" : rig.hearts.Side.ToString()) + ")";
            var report = ShipUiSlots.Candidates(rig.ship.transform, id, rig.hearts.Request(), rig.hearts);
            string reaches = "";
            foreach (var s in report) reaches += " " + s.side + "=" + s.reach.ToString("F2") + (s.clear ? "" : "x");
            Debug.Log("[HP] slots " + name + ":" + reaches);
            Check(name + ": hearts clear of the charge indicator in idle, charging and ready (" + poses + " poses)" +
                  (hitIndicator != null ? " -- hit while " + hitIndicator : ""), hitIndicator == null);
            Check(name + ": hearts clear of the hull" + (hitHull != null ? " -- hit while " + hitHull : ""), hitHull == null);
            Check(name + ": hearts clear of the gun" + (hitGun != null ? " -- hit while " + hitGun : ""), hitGun == null);
            Check(name + ": hearts clear of the secret meter" + (hitMeter != null ? " -- hit while " + hitMeter : ""), hitMeter == null);
            if (orbit)
                Check(name + ": orbiting hearts still show most of the time, not just duck (" + shown + "/" + slots + ")",
                      shown >= slots * .5f);
            Check(name + ": the indicator's footprint covers every pose it took",
                  FootprintCovers(rig, view));
            if (id == 15) turtleSide = rig.hearts.Side;
            Teardown(rig);
        }
        Check("Turtle's hearts moved off the nose (" + turtleSide + ")", turtleSide != ShipUiSlots.Side.Above);
    }

    static bool FootprintCovers(Rig rig, SpriteRenderer view)
    {
        foreach (string _ in Poses(rig))
        {
            Bounds f = ShipUiSlots.ChargeIndicatorFootprint(rig.ship.transform, rig.id);
            Bounds v = view.bounds;
            if (v.min.x < f.min.x - .001f || v.max.x > f.max.x + .001f ||
                v.min.y < f.min.y - .001f || v.max.y > f.max.y + .001f) return false;
        }
        return true;
    }

    static void StayOnScreenAtTheEdges()
    {
        ShipUiSlots.ScreenOverride = () => Gameplay;
        // movePlayer clamps the ship to x +-2.4, y -4.15..4.5.
        var spots = new[]
        {
            new Vector3(-2.4f, 0f, 0f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, -4.15f, 0f),
            new Vector3(-2.4f, -4.15f, 0f), new Vector3(2.4f, -4.15f, 0f), new Vector3(0f, 4.5f, 0f),
        };
        foreach (int id in ShipId.All)
        {
            FreshScene();
            var rig = Build(id, Vector3.zero);
            string offAt = null, hitAt = null;
            bool orbit = rig.hearts.Orbiting;
            int turns = orbit ? Mathf.CeilToInt(360f / rig.hearts.orbitRate / 15f) : 1;
            foreach (var spot in spots)
                for (int turn = 0; turn < turns; turn++)
                {
                    rig.ship.transform.position = spot;
                    if (orbit) Pose(rig, Field<float>(rig.power, "cooldown") * .5f, 0f, turn * .3f, 1f, turn * 15f);
                    else rig.hearts.Place(0f);
                    var hearts = HeartBounds(rig);
                    string where = spot + (orbit ? " spin " + turn * 15 : " " + rig.hearts.Side);
                    foreach (var h in hearts)
                        if (!ShipUiSlots.Inside(Gameplay, h)) { offAt = offAt ?? where; break; }
                    bool hit = orbit
                        ? HitsCircle(hearts, spot, HullRadius(rig)) || Hits(hearts, rig.indicator.View.GetComponent<SpriteRenderer>().bounds) ||
                          Hits(hearts, ShipUiSlots.DrawnBounds(rig.gun.transform)) ||
                          (rig.meter != null && Hits(hearts, rig.meter.GetComponent<SpriteRenderer>().bounds))
                        : Hits(hearts, ShipUiSlots.ChargeIndicatorFootprint(rig.ship.transform, id)) ||
                          Hits(hearts, rig.ship.GetComponent<SpriteRenderer>().bounds);
                    if (hit) hitAt = hitAt ?? where;
                }
            string name = ShipId.KeyOf(id) + " (" + id + ")";
            Check(name + ": hearts stay on screen at the left, right and bottom edges" +
                  (offAt != null ? " -- off at " + offAt : ""), offAt == null);
            Check(name + ": and still clear of the indicator and hull there" +
                  (hitAt != null ? " -- hit at " + hitAt : ""), hitAt == null);

            // At the left edge a left-hand stack has to flip right, and back.
            if (id == 15)
            {
                rig.ship.transform.position = new Vector3(-2.4f, 0f, 0f);
                rig.hearts.Place(0f);
                var atLeft = rig.hearts.Side;
                rig.ship.transform.position = new Vector3(2.4f, 0f, 0f);
                rig.hearts.Place(0f);
                var atRight = rig.hearts.Side;
                Check("Turtle's hearts flip sides between the left and right edges (" + atLeft + " / " + atRight + ")",
                      atLeft != ShipUiSlots.Side.Left && atRight != ShipUiSlots.Side.Right);
            }
            Teardown(rig);
        }
    }

    // Very tall screens (9:22, 9:24, the Z Fold cover): CameraFit keeps the
    // 2.85 half-width and grows the height, with a cutout / gesture-bar safe
    // area. The hearts and the secret meter stay inside it, off the hull and
    // the charge indicator, wherever the ship can fly.
    static void TallScreens()
    {
        var screens = new[]
        {
            ("9:16 1080x1920", 1080, 1920, 0, 0),
            ("9:22 1080x2640", 1080, 2640, 96, 48),
            ("9:24 1080x2880", 1080, 2880, 96, 48),
            ("Z Fold cover 968x2376", 968, 2376, 90, 40),
        };
        var spots = new[]
        {
            new Vector3(-2.4f, 0f, 0f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, -4.15f, 0f),
            new Vector3(-2.4f, -4.15f, 0f), new Vector3(2.4f, -4.15f, 0f), new Vector3(0f, 4.5f, 0f),
            new Vector3(-2.4f, 4.5f, 0f), new Vector3(2.4f, 4.5f, 0f),
        };
        foreach (var (name, w, h, top, bottom) in screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, w, h);
            float halfW = size * w / h, unit = 2f * size / h;
            Rect safe = Rect.MinMaxRect(-halfW, -size + bottom * unit, halfW, size - top * unit);
            ShipUiSlots.ScreenOverride = () => safe;
            string offAt = null, meterOff = null, hitAt = null;
            int meters = 0;
            foreach (int id in ShipId.All)
            {
                FreshScene();
                var rig = Build(id, Vector3.zero);
                var meter = rig.ship.GetComponentInChildren<SecretMeter>(true);
                foreach (var spot in spots)
                {
                    rig.ship.transform.position = spot;
                    // a few frames: each re-places against the other's footprint
                    for (int f = 0; f < 3; f++)
                    {
                        rig.hearts.Place(0f);
                        if (meter != null) meter.Step(0f);
                    }
                    var hearts = HeartBounds(rig);
                    foreach (var b in hearts)
                        if (!ShipUiSlots.Inside(safe, b)) { offAt = offAt ?? ShipId.KeyOf(id) + " at " + spot; break; }
                    if (meter != null)
                    {
                        meters++;
                        var mb = meter.View.GetComponent<SpriteRenderer>().bounds;
                        if (!ShipUiSlots.Inside(safe, mb)) meterOff = meterOff ?? ShipId.KeyOf(id) + " at " + spot;
                        // orbiting hearts (spinners) only keep clear of the badge as drawn
                        Bounds keep = rig.hearts.Orbiting ? mb : meter.Footprint;
                        if (Hits(hearts, keep)) hitAt = hitAt ?? ShipId.KeyOf(id) + " hearts/meter at " + spot;
                    }
                    bool hit = rig.hearts.Orbiting
                        ? HitsCircle(hearts, spot, HullRadius(rig)) ||
                          Hits(hearts, rig.indicator.View.GetComponent<SpriteRenderer>().bounds)
                        : Hits(hearts, rig.ship.GetComponent<SpriteRenderer>().bounds) ||
                          Hits(hearts, ShipUiSlots.ChargeIndicatorFootprint(rig.ship.transform, id));
                    if (hit)
                        hitAt = hitAt ?? ShipId.KeyOf(id) + " at " + spot;
                }
                Teardown(rig);
            }
            Check(name + ": hearts stay inside the safe area at every edge" + (offAt != null ? " -- off: " + offAt : ""),
                  offAt == null);
            Check(name + ": the secret meter stays inside the safe area (" + meters + " placements)" +
                  (meterOff != null ? " -- off: " + meterOff : ""), meterOff == null && meters > 0);
            Check(name + ": hearts clear of the hull, indicator and meter" + (hitAt != null ? " -- hit: " + hitAt : ""),
                  hitAt == null);
        }
        ShipUiSlots.ScreenOverride = null;
    }

    // The upcoming secret-power meter (or anything else) only has to register
    // a footprint for the hearts to make room.
    static void AnExtraIndicatorTakesTheNextSlot()
    {
        FreshScene();
        ShipUiSlots.ScreenOverride = () => Everywhere;
        var rig = Build(15, Vector3.zero);
        rig.hearts.Place(0f);
        var first = rig.hearts.Side;
        Bounds taken = HeartsArea(rig);

        // A meter sitting where the hearts are, reaching well past them.
        var meter = new GameObject("~DummyMeter");
        Vector2 dir = Direction(first);
        Bounds meterBox = taken;
        meterBox.Encapsulate(taken.center + (Vector3)(dir * 3f));
        meterBox.Expand(.1f);
        ShipUiSlots.Register(rig.ship.transform, meter, () => meterBox);
        rig.hearts.Place(0f);
        var second = rig.hearts.Side;
        Check("a registered meter in the hearts' slot pushes them to another (" + first + " -> " + second + ")",
              second != first);
        Check("the hearts clear the meter", !Hits(HeartBounds(rig), meterBox));
        int firstAt = System.Array.IndexOf(ShipUiSlots.Order, first);
        int secondAt = System.Array.IndexOf(ShipUiSlots.Order, second);
        var slots = ShipUiSlots.Candidates(rig.ship.transform, 15, rig.hearts.Request(), rig.hearts);
        int expected = ShipUiSlots.Choose(slots, rig.ship.transform.position, Everywhere, rig.hearts.nearReach);
        Check("they take the next free slot (snug ones in order above, below, left, right, then the nearest)",
              secondAt == expected && secondAt != firstAt && slots[secondAt].clear && !slots[firstAt].clear);

        ShipUiSlots.Unregister(meter);
        rig.hearts.Place(0f);
        Check("unregistering the meter gives the slot back", rig.hearts.Side == first);

        ShipUiSlots.Register(rig.ship.transform, meter, () => meterBox);
        rig.hearts.Place(0f);
        Object.DestroyImmediate(meter);
        rig.hearts.Place(1.1f); // the periodic refresh
        Check("a destroyed meter drops out of the registry by itself", rig.hearts.Side == first);
        Teardown(rig);
    }

    // ---- spinners -------------------------------------------------------

    static float HullRadius(Rig rig) => ShipUiSlots.HullBounds(rig.ship.transform, rig.id).extents.x;

    // Does any heart box reach into the circle?
    static bool HitsCircle(List<Bounds> hearts, Vector3 c, float r)
    {
        foreach (var h in hearts)
        {
            float dx = Mathf.Max(h.min.x - c.x, 0f, c.x - h.max.x);
            float dy = Mathf.Max(h.min.y - c.y, 0f, c.y - h.max.y);
            if (dx * dx + dy * dy < r * r) return true;
        }
        return false;
    }

    static float Polar(Rig rig, int i)
    {
        Vector3 d = rig.hearts.Hearts[i].position - rig.ship.transform.position;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    // Turns the hull `degrees` over `steps` frames of `dt` gameplay seconds.
    static void Spin(Rig rig, float degrees, int steps, float dt)
    {
        for (int i = 0; i < steps; i++)
        {
            rig.ship.transform.Rotate(0f, 0f, degrees / steps);
            rig.hearts.Place(dt, dt);
        }
    }

    static void SpinnersOrbit()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        foreach (int id in ShipId.All)
        {
            if (!ShipUiSlots.Spins(id)) continue;
            FreshScene();
            var rig = Build(id, Vector3.zero, withPower: false);
            string name = ShipId.KeyOf(id) + " (" + id + ")";
            Check(name + " orbits its hearts", rig.hearts.Orbiting);
            rig.hearts.Place(0f, 0f);
            float ring0 = rig.hearts.OrbitAngle, polar0 = Polar(rig, 0);
            Spin(rig, 90f, 9, 1f / 60f);
            float turned = Mathf.DeltaAngle(ring0, rig.hearts.OrbitAngle);
            float moved = Mathf.DeltaAngle(polar0, Polar(rig, 0));
            float expect = 90f * rig.hearts.orbitRate;
            Check(name + ": a quarter turn of the hull carries the ring round " + turned.ToString("F1") +
                  " deg (expected " + expect.ToString("F1") + ")", Mathf.Abs(turned - expect) < .5f);
            Check(name + ": and the hearts with it (" + moved.ToString("F1") + " deg)", Mathf.Abs(moved - expect) < 4f);

            bool upright = true, even = true, ring = true;
            float r = rig.hearts.OrbitRadius;
            for (int step = 0; step < 24; step++)
            {
                Spin(rig, 45f, 3, 1f / 60f);
                var hs = rig.hearts.Hearts;
                for (int i = 0; i < hs.Length; i++)
                {
                    if (Quaternion.Angle(hs[i].rotation, Quaternion.identity) > .01f) upright = false;
                    float d = ((Vector2)(hs[i].position - rig.ship.transform.position)).magnitude;
                    if (Mathf.Abs(d - r) > ShipLivesIndicator.BobAmplitude + ShipLivesIndicator.PulseAmplitude + .005f) ring = false;
                    float gap = Mathf.DeltaAngle(Polar(rig, i), Polar(rig, (i + 1) % hs.Length));
                    if (Mathf.Abs(Mathf.Abs(gap) - 360f / hs.Length) > 5f) even = false;
                }
            }
            Check(name + ": every heart stays upright as the hull spins", upright);
            Check(name + ": on one ring (r " + r.ToString("F2") + ", hull circle " + HullRadius(rig).ToString("F2") + ")", ring);
            Check(name + ": evenly spaced", even);
            Check(name + ": tight to the ship (ring within a heart and a half of the hull's spin circle)",
                  r - HullRadius(rig) <= rig.hearts.heartSize * 1.6f);

            // One hit hides one heart; the ones left share the ring.
            collisionDetection.lifeCounter = 1;
            rig.hearts.SendMessage("Update");
            int active = 0;
            foreach (var h in rig.hearts.Hearts) if (h.gameObject.activeSelf) active++;
            Check(name + ": a hit hides exactly one heart (" + active + " left)", active == collisionDetection.MAXLIFE - 1);
            Spin(rig, 120f, 60, 1f / 60f);
            float split = Mathf.Abs(Mathf.DeltaAngle(Polar(rig, 0), Polar(rig, 1)));
            float evenSplit = 360f / (collisionDetection.MAXLIFE - 1);
            if (evenSplit > 180f) evenSplit = 360f - evenSplit;
            Check(name + ": the hearts left close up evenly (" + split.ToString("F0") + " deg apart, expected " +
                  evenSplit.ToString("F0") + ")", Mathf.Abs(split - evenSplit) < 5f);
            collisionDetection.lifeCounter = 0;
            rig.hearts.SendMessage("Update");
            Teardown(rig);
        }
    }

    static void NonSpinnersStayInTheirSlot()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        foreach (int id in ShipId.All)
        {
            if (ShipUiSlots.Spins(id)) continue;
            FreshScene();
            var rig = Build(id, Vector3.zero);
            rig.hearts.Place(0f, 1f / 60f);
            var before = new Vector3[rig.hearts.Hearts.Length];
            for (int i = 0; i < before.Length; i++) before[i] = rig.hearts.Hearts[i].position;
            rig.hearts.Place(1f / 60f, 1f / 60f);
            bool line = true, still = true, full = true;
            var hs = rig.hearts.Hearts;
            bool across = rig.hearts.Side == ShipUiSlots.Side.Above || rig.hearts.Side == ShipUiSlots.Side.Below;
            for (int i = 0; i < hs.Length; i++)
            {
                if (across ? Mathf.Abs(hs[i].position.y - hs[0].position.y) > 1e-4f
                           : Mathf.Abs(hs[i].position.x - hs[0].position.x) > 1e-4f) line = false;
                // the slot itself doesn't move (only the bob, vertically)
                if (Mathf.Abs(hs[i].position.x - before[i].x) > 1e-4f) still = false;
                if (!hs[i].GetComponent<SpriteRenderer>().enabled) full = false;
            }
            Check(ShipId.KeyOf(id) + " (" + id + ") keeps its slot: no orbit, a straight " + (across ? "row" : "stack") +
                  ", all hearts drawn", !rig.hearts.Orbiting && line && still && full);
            Teardown(rig);
        }
    }

    static void OrbitFreezesWithTheWorld()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        foreach (int id in ShipId.All)
        {
            if (!ShipUiSlots.Spins(id)) continue;
            FreshScene();
            var rig = Build(id, Vector3.zero);
            Spin(rig, 30f, 3, 1f / 60f);
            Time.timeScale = 0f;
            var before = new Vector3[rig.hearts.Hearts.Length];
            for (int i = 0; i < before.Length; i++) before[i] = rig.hearts.Hearts[i].position;
            // frozen: ShipSpinDrift's spin is on scaled time, so the hull
            // holds still too; only real (unscaled) time passes
            for (int f = 0; f < 30; f++) rig.hearts.Place(.05f, .05f * Time.timeScale);
            float drift = 0f;
            for (int i = 0; i < before.Length; i++) drift = Mathf.Max(drift, (rig.hearts.Hearts[i].position - before[i]).magnitude);
            Check(ShipId.KeyOf(id) + " (" + id + "): at timeScale 0 the orbiting hearts hold still (moved " +
                  drift.ToString("F4") + ")", drift < 1e-5f);
            Time.timeScale = 1f;
            Teardown(rig);
        }
    }

    // Every ship with every heart count the roster uses (2 on the starter ..
    // 5 on Gold Warden), wherever it can fly on the gameplay screen and a
    // tall one: on screen, clear of the hull, the charge indicator and the
    // secret meter (a spinner's ring: clear of its spin circle).
    static void EveryHeartCountFits()
    {
        var screens = new[] { ("9:16", 1080, 1920, 0, 0), ("9:24", 1080, 2880, 96, 48) };
        var spots = new[]
        {
            Vector3.zero, new Vector3(-2.4f, 0f, 0f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, -4.15f, 0f),
            new Vector3(-2.4f, -4.15f, 0f), new Vector3(2.4f, -4.15f, 0f), new Vector3(0f, 4.5f, 0f),
        };
        foreach (var (screen, w, h, top, bottom) in screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, w, h);
            float halfW = size * w / h, unit = 2f * size / h;
            Rect safe = Rect.MinMaxRect(-halfW, -size + bottom * unit, halfW, size - top * unit);
            ShipUiSlots.ScreenOverride = () => safe;
            for (int count = ShipLives.Fewest; count <= ShipLives.Most; count++)
            {
                string offAt = null, hitAt = null, wrong = null;
                foreach (int id in ShipId.All)
                {
                    FreshScene();
                    var rig = Build(id, Vector3.zero, true, count);
                    if (rig.hearts.Hearts.Length != count) wrong = wrong ?? ShipId.KeyOf(id) + " built " + rig.hearts.Hearts.Length;
                    var meter = rig.meter;
                    foreach (var spot in spots)
                    {
                        rig.ship.transform.position = spot;
                        for (int f = 0; f < 3; f++)
                        {
                            if (rig.hearts.Orbiting) Pose(rig, Field<float>(rig.power, "cooldown") * .5f, 0f, f * .3f, 1f, f * 40f);
                            else rig.hearts.Place(0f);
                            if (meter != null) meter.Step(0f);
                        }
                        var hearts = HeartBounds(rig);
                        string where = ShipId.KeyOf(id) + " at " + spot;
                        foreach (var b in hearts)
                            if (!ShipUiSlots.Inside(safe, b)) { offAt = offAt ?? where; break; }
                        bool hit = rig.hearts.Orbiting
                            ? HitsCircle(hearts, spot, HullRadius(rig)) ||
                              Hits(hearts, rig.indicator.View.GetComponent<SpriteRenderer>().bounds)
                            : Hits(hearts, rig.ship.GetComponent<SpriteRenderer>().bounds) ||
                              Hits(hearts, ShipUiSlots.ChargeIndicatorFootprint(rig.ship.transform, id)) ||
                              (meter != null && Hits(hearts, meter.Footprint));
                        if (hit) hitAt = hitAt ?? where;
                    }
                    Teardown(rig);
                }
                string label = screen + ", " + count + " hearts";
                Check(label + ": every ship builds exactly that many" + (wrong != null ? " -- " + wrong : ""), wrong == null);
                Check(label + ": on screen everywhere the ship flies" + (offAt != null ? " -- off: " + offAt : ""), offAt == null);
                Check(label + ": clear of the hull, indicator and meter" + (hitAt != null ? " -- hit: " + hitAt : ""), hitAt == null);
            }
        }
        ShipUiSlots.ScreenOverride = null;
        collisionDetection.MAXLIFE = 3;
    }

    static Bounds HeartsArea(Rig rig)
    {
        var all = HeartBounds(rig);
        var b = all[0];
        foreach (var h in all) b.Encapsulate(h);
        return b;
    }

    static Vector2 Direction(ShipUiSlots.Side side)
    {
        switch (side)
        {
            case ShipUiSlots.Side.Above: return Vector2.up;
            case ShipUiSlots.Side.Below: return Vector2.down;
            case ShipUiSlots.Side.Left: return Vector2.left;
            default: return Vector2.right;
        }
    }
}
