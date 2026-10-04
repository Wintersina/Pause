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
    public static Rig Build(int id, Vector3 at, bool withPower = true)
    {
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
        }
        rig.hearts = go.AddComponent<ShipLivesIndicator>();
        rig.hearts.BuildHearts();
        return rig;
    }

    public static void Teardown(Rig rig)
    {
        if (rig.power != null) rig.power.SendMessage("OnDestroy");
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
        rig.hearts.Place(0f);
    }

    public static Bounds Visible(Renderer r) => r.bounds;

    public static List<Bounds> HeartBounds(Rig rig)
    {
        var list = new List<Bounds>();
        foreach (var h in rig.hearts.Hearts)
            if (h != null && h.gameObject.activeSelf) list.Add(h.GetComponent<SpriteRenderer>().bounds);
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
        float[] spins = ShipUiSlots.Spins(rig.id) ? new[] { 0f, 30f, 60f, 90f, 135f, 180f, 225f, 270f, 315f } : new[] { 0f };
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
            string hitIndicator = null, hitHull = null, hitGun = null;
            int poses = 0;
            foreach (string pose in Poses(rig))
            {
                poses++;
                var hearts = HeartBounds(rig);
                if (hitIndicator == null && Hits(hearts, view.bounds)) hitIndicator = pose;
                if (hitHull == null && Hits(hearts, hull.bounds)) hitHull = pose;
                if (hitGun == null && Hits(hearts, barrel.bounds)) hitGun = pose;
            }
            string name = ShipId.KeyOf(id) + " (" + id + ", hearts " + rig.hearts.Side + ")";
            var report = ShipUiSlots.Candidates(rig.ship.transform, id, rig.hearts.Request(), rig.hearts);
            string reaches = "";
            foreach (var s in report) reaches += " " + s.side + "=" + s.reach.ToString("F2") + (s.clear ? "" : "x");
            Debug.Log("[HP] slots " + name + ":" + reaches);
            Check(name + ": hearts clear of the charge indicator in idle, charging and ready (" + poses + " poses)" +
                  (hitIndicator != null ? " -- hit while " + hitIndicator : ""), hitIndicator == null);
            Check(name + ": hearts clear of the hull" + (hitHull != null ? " -- hit while " + hitHull : ""), hitHull == null);
            Check(name + ": hearts clear of the gun" + (hitGun != null ? " -- hit while " + hitGun : ""), hitGun == null);
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
            foreach (var spot in spots)
            {
                rig.ship.transform.position = spot;
                rig.hearts.Place(0f);
                var hearts = HeartBounds(rig);
                foreach (var h in hearts)
                    if (!ShipUiSlots.Inside(Gameplay, h)) { offAt = offAt ?? spot + " " + rig.hearts.Side; break; }
                var footprint = ShipUiSlots.ChargeIndicatorFootprint(rig.ship.transform, id);
                if (Hits(hearts, footprint) || Hits(hearts, rig.ship.GetComponent<SpriteRenderer>().bounds))
                    hitAt = hitAt ?? spot + " " + rig.hearts.Side;
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
                        if (Hits(hearts, meter.Footprint)) hitAt = hitAt ?? ShipId.KeyOf(id) + " hearts/meter at " + spot;
                    }
                    if (Hits(hearts, rig.ship.GetComponent<SpriteRenderer>().bounds) ||
                        Hits(hearts, ShipUiSlots.ChargeIndicatorFootprint(rig.ship.transform, id)))
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
