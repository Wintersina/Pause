using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The life hearts orbit every ship (ShipHeartStyles): slowly and
// continuously, all the way round the hull, passing behind it (drawn under
// it, smaller and dimmer) and in front (over it, larger); each heart on its
// own plane and phase. They whip past the thumb (a hull-length under the
// ship) and the companion gun's resting spot rather than linger, keep apart
// from each other, stay inside the safe area on every supported screen, and
// hold still while the world is frozen.
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

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        try
        {
            EveryShipOrbitsAllRound();
            StayOnScreenOnEveryScreen();
            OrbitFreezesWithTheWorld();
            ReSpaceAfterALoss();
            FlourishesHappen();
        }
        finally
        {
            ShipUiSlots.ScreenOverride = null;
            Time.timeScale = 1f;
            collisionDetection.lifeCounter = 0;
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
    // The hearts fly on `orbitSeconds` round their orbits.
    public static void Pose(Rig rig, float secondsLeft, float extend, float hoverT, float grow, float spinDegrees = 0f,
                            float orbitSeconds = 0f)
    {
        rig.ship.transform.rotation = Quaternion.Euler(0f, 0f, spinDegrees);
        if (rig.power != null)
        {
            SetTimer(rig.power, secondsLeft);
            if (rig.gun != null) PoseGun(rig, extend, hoverT, grow);
            rig.indicator.Step(.02f, .02f);
            if (rig.meter != null) rig.meter.Step(0f);
        }
        rig.hearts.Place(0f, 0f);
        for (float t = 0f; t < orbitSeconds - 1e-4f; t += Dt) rig.hearts.Place(0f, Mathf.Min(Dt, orbitSeconds - t));
    }

    static void PoseGun(Rig rig, float extend, float hoverT, float grow)
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
    // the gun's hover cycle -- the hearts flying on round their orbits
    // between poses (and, for a spinner, the hull all the way round).
    public static IEnumerable<string> Poses(Rig rig)
    {
        float cooldown = rig.power != null ? Field<float>(rig.power, "cooldown") : 30f;
        var spins = new List<float> { 0f };
        if (ShipUiSlots.Spins(rig.id))
            for (float a = 45f; a < 360f; a += 45f) spins.Add(a);
        foreach (float spin in spins)
            for (float t = 0f; t < 6.3f; t += .45f)
            {
                Pose(rig, cooldown, 0f, t, 1f, spin, .23f);
                yield return "idle";
                Pose(rig, cooldown * .5f, 0f, t, 1f, spin, .23f);
                yield return "charging";
                Pose(rig, .9f, .5f, t, 1f + .07f, spin, .23f);
                yield return "ready (sliding out)";
                Pose(rig, .5f, 1f, t, 1.14f, spin, .23f);
                yield return "ready";
                Pose(rig, .1f, 1f, t, 1.38f, spin, .23f);
                yield return "anticipation";
            }
    }

    // The thumb: movePlayer flies the ship 1 unit above the finger; a thumb
    // pad covers about this radius round it (and everything under it).
    public const float FingerBelow = ShipLivesIndicator.ThumbBelow, FingerRadius = ShipLivesIndicator.ThumbRadius;

    public static bool UnderThumb(Bounds h, Vector3 ship)
    {
        var c = ship + Vector3.down * FingerBelow;
        if (h.max.y < c.y) return true;   // below the pad: under the thumb itself
        float dx = Mathf.Max(h.min.x - c.x, 0f, c.x - h.max.x);
        float dy = Mathf.Max(h.min.y - c.y, 0f, c.y - h.max.y);
        return dx * dx + dy * dy < FingerRadius * FingerRadius;
    }

    public static bool UnderThumb(List<Bounds> hearts, Vector3 ship)
    {
        foreach (var h in hearts) if (UnderThumb(h, ship)) return true;
        return false;
    }

    static string Name(Rig rig) =>
        ShipId.KeyOf(rig.id) + " (" + rig.id + ", " + rig.hearts.Hearts.Length + " hearts, " + rig.hearts.Style + ")";

    // Longest unbroken run of `true` (frames) and how many in all.
    class Streak
    {
        public int run, longest, total;
        public void Add(bool on)
        {
            if (on) { run++; total++; longest = Mathf.Max(longest, run); }
            else run = 0;
        }
    }

    // ---- cases ---------------------------------------------------------

    // Every ship, every heart count: over 24 seconds each heart goes all the
    // way round the hull, behind it (under, smaller, dimmer) and in front
    // (over, larger); never lingers under the thumb or on the gun's resting
    // spot, and no two hearts sit on each other for long.
    static void EveryShipOrbitsAllRound()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        const int Frames = 24 * 60;
        foreach (int id in ShipId.All)
            for (int count = ShipLives.Fewest; count <= ShipLives.Most; count++)
            {
                FreshScene();
                var rig = Build(id, Vector3.zero, true, count);
                Pose(rig, 20f, 0f, 1.3f, 1f);
                var gunDrawn = rig.gun != null ? ShipUiSlots.DrawnBounds(rig.gun.transform) : default(Bounds);
                var hullBox = ShipUiSlots.HullBounds(rig.ship.transform, id);
                var hs = rig.hearts.Hearts;
                var sectors = new int[count];        // bitmask of 8 polar sectors visited
                var behind = new bool[count];
                var inFront = new bool[count];
                var thumb = new Streak[count];
                var onGun = new Streak[count];
                for (int i = 0; i < count; i++) { thumb[i] = new Streak(); onGun[i] = new Streak(); }
                var overlap = new Streak();
                bool depthReads = true, moved = true, near = true;
                float farthest = 0f, minFront = float.MaxValue, maxBack = 0f;
                var last = new Vector3[count];
                for (int i = 0; i < count; i++) last[i] = hs[i].position;
                int still = 0;
                for (int f = 0; f < Frames; f++)
                {
                    rig.hearts.Place(Dt, Dt);
                    bool anyOverlap = false;
                    for (int i = 0; i < count; i++)
                    {
                        var sr = hs[i].GetComponent<SpriteRenderer>();
                        Vector2 d = (Vector2)(hs[i].position - hullBox.center);
                        float a = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 360f);
                        sectors[i] |= 1 << Mathf.Clamp((int)(a / 45f), 0, 7);
                        farthest = Mathf.Max(farthest, d.magnitude - rig.hearts.OrbitRadius);
                        bool front = sr.sortingOrder > rig.ship.GetComponent<SpriteRenderer>().sortingOrder;
                        float z = rig.hearts.Depth(i);
                        if (front) inFront[i] = true; else behind[i] = true;
                        // behind: under the hull, smaller and dimmer; in front: over, larger
                        if (front != (z >= 0f)) depthReads = false;
                        if (z < -.3f && (sr.color.r > .95f || sr.sortingOrder != rig.hearts.BackOrder)) depthReads = false;
                        if (z > .3f) minFront = Mathf.Min(minFront, hs[i].localScale.x);
                        if (z < -.3f) maxBack = Mathf.Max(maxBack, hs[i].localScale.x);
                        if ((hs[i].position - last[i]).sqrMagnitude < 1e-10f) still++;
                        last[i] = hs[i].position;
                        thumb[i].Add(UnderThumb(sr.bounds, rig.ship.transform.position));
                        onGun[i].Add(rig.gun != null && gunDrawn.Contains(new Vector3(hs[i].position.x, hs[i].position.y, gunDrawn.center.z)));
                        for (int j = i + 1; j < count; j++)
                            if (((Vector2)(hs[i].position - hs[j].position)).magnitude < rig.hearts.heartSize * .5f) anyOverlap = true;
                    }
                    overlap.Add(anyOverlap);
                }
                if (still > Frames * count / 20) moved = false;
                if (maxBack >= minFront) depthReads = false;
                if (farthest > ShipLivesIndicator.LoopRadius * 2f + .02f) near = false;
                string name = Name(rig);
                bool allRound = true, depth = true;
                for (int i = 0; i < count; i++)
                {
                    if (sectors[i] != 0xFF) allRound = false;
                    if (!behind[i] || !inFront[i]) depth = false;
                }
                Check(name + ": every heart keeps moving and goes all the way round the hull", moved && allRound);
                Check(name + ": every heart passes behind (under the hull) and in front (over it)", depth && depthReads);
                Check(name + ": on its orbit round the hull (furthest out " + farthest.ToString("F2") + " past r " +
                      rig.hearts.OrbitRadius.ToString("F2") + ", radii " + rig.hearts.OrbitRadii.ToString("F2") + ")", near);
                int thumbWorst = 0, thumbLongest = 0, gunWorst = 0, gunLongest = 0;
                for (int i = 0; i < count; i++)
                {
                    thumbWorst = Mathf.Max(thumbWorst, thumb[i].total); thumbLongest = Mathf.Max(thumbLongest, thumb[i].longest);
                    gunWorst = Mathf.Max(gunWorst, onGun[i].total); gunLongest = Mathf.Max(gunLongest, onGun[i].longest);
                }
                Check(name + ": passes the thumb without lingering (" + (100f * thumbWorst / Frames).ToString("F0") + "% of the time, longest " +
                      (thumbLongest * Dt).ToString("F2") + "s)", thumbWorst < Frames * .12f && thumbLongest * Dt < .5f);
                Check(name + ": crosses the gun without parking on it (" + (100f * gunWorst / Frames).ToString("F0") + "%, longest " +
                      (gunLongest * Dt).ToString("F2") + "s)", gunWorst < Frames * .15f && gunLongest * Dt < .45f);
                // (crossing orbits pass each other -- a quick pass-by reads as
                // 3D -- but no two hearts sit half on each other for long)
                Check(name + ": hearts only pass each other, never sit on each other (" + (100f * overlap.total / Frames).ToString("F0") +
                      "% of the time two half overlap, longest " + (overlap.longest * Dt).ToString("F2") + "s)",
                      overlap.total < Frames * .2f && overlap.longest * Dt < .5f);
                Teardown(rig);
            }
    }

    // Every supported screen (TallScreenTest's sizes, a cut-out / gesture
    // bar safe area on the tall ones), everywhere the ship can fly: every
    // heart stays inside the safe area all round its orbit.
    static void StayOnScreenOnEveryScreen()
    {
        // movePlayer clamps the ship to x +-2.4, y -4.15..4.5.
        var spots = new[]
        {
            Vector3.zero, new Vector3(-2.4f, 0f, 0f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, -4.15f, 0f),
            new Vector3(-2.4f, -4.15f, 0f), new Vector3(2.4f, -4.15f, 0f), new Vector3(0f, 4.5f, 0f),
            new Vector3(-2.4f, 4.5f, 0f), new Vector3(2.4f, 4.5f, 0f),
        };
        var screens = new List<(string, Rect)> { ("gameplay", Gameplay) };
        foreach (var (name, w, h) in TallScreenTest.Screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, w, h);
            float halfW = size * w / h, unit = 2f * size / h;
            bool tall = (float)h / w > 2f;
            screens.Add((name, Rect.MinMaxRect(-halfW, -size + (tall ? 48 : 0) * unit, halfW, size - (tall ? 96 : 0) * unit)));
        }
        foreach (var (name, safe) in screens)
        {
            ShipUiSlots.ScreenOverride = () => safe;
            string offAt = null;
            foreach (int id in ShipId.All)
                foreach (int count in new[] { ShipLives.Max(id), ShipLives.Most })
                {
                    FreshScene();
                    var rig = Build(id, Vector3.zero, true, count);
                    Pose(rig, 20f, 0f, 1.3f, 1f);
                    foreach (var spot in spots)
                    {
                        // movePlayer keeps the ship inside the safe area
                        var at = new Vector3(Mathf.Clamp(spot.x, safe.xMin + .45f, safe.xMax - .45f),
                                             Mathf.Clamp(spot.y, safe.yMin + .85f, safe.yMax - .5f), 0f);
                        rig.ship.transform.position = at;
                        for (int f = 0; f < 300 && offAt == null; f++)
                        {
                            if (ShipUiSlots.Spins(id)) rig.ship.transform.Rotate(0f, 0f, 6f);
                            rig.hearts.Place(Dt, Dt);
                            foreach (var b in HeartBounds(rig))
                                if (!ShipUiSlots.Inside(safe, b)) { offAt = Name(rig) + " at " + at + " frame " + f; break; }
                        }
                    }
                    Teardown(rig);
                }
            Check(name + ": every ship's hearts stay inside the safe area all round the orbit, at every edge" +
                  (offAt != null ? " -- off: " + offAt : ""), offAt == null);
        }
        ShipUiSlots.ScreenOverride = null;
    }

    // At timeScale 0 (paused) nothing moves: only unscaled time passes.
    static void OrbitFreezesWithTheWorld()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        foreach (int id in ShipId.All)
        {
            FreshScene();
            var rig = Build(id, Vector3.zero);
            Pose(rig, 20f, 0f, 1.3f, 1f, 0f, 1.5f);
            Time.timeScale = 0f;
            var before = new Vector3[rig.hearts.Hearts.Length];
            for (int i = 0; i < before.Length; i++) before[i] = rig.hearts.Hearts[i].position;
            for (int f = 0; f < 60; f++) rig.hearts.Place(.05f, .05f * Time.timeScale);
            float drift = 0f;
            for (int i = 0; i < before.Length; i++) drift = Mathf.Max(drift, (rig.hearts.Hearts[i].position - before[i]).magnitude);
            Check(Name(rig) + ": at timeScale 0 the orbiting hearts hold still (moved " + drift.ToString("F5") + ")", drift < 1e-5f);
            Time.timeScale = 1f;
            Teardown(rig);
        }
    }

    // A lost heart's place closes up: the hearts left re-space (a ring
    // evenly, a train at its spacing, crossing orbits fan over the new
    // count), without jumping.
    static void ReSpaceAfterALoss()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        foreach (int id in ShipId.All)
        {
            FreshScene();
            collisionDetection.lifeCounter = 0;
            var rig = Build(id, Vector3.zero, true, 5);
            Pose(rig, 20f, 0f, 1.3f, 1f, 0f, 2f);
            var hs = rig.hearts.Hearts;
            ShipLivesIndicator.Impact(rig.ship.transform.position + new Vector3(.3f, 1f, 0f));
            collisionDetection.lifeCounter = 2;
            rig.hearts.SendMessage("Update");
            float jump = 0f;
            var last = new Vector3[3];
            for (int i = 0; i < 3; i++) last[i] = hs[i].position;
            // (a loop-de-loop holds a heart back a moment: the best of the
            // last three seconds)
            float settled = float.MaxValue;
            for (int f = 0; f < 7 * 60; f++)
            {
                if (f >= 4 * 60) settled = Mathf.Min(settled, SpacingError(rig));
                rig.hearts.Place(Dt, Dt);
                rig.hearts.StepBreaks(Dt);
                for (int i = 0; i < 3; i++)
                {
                    jump = Mathf.Max(jump, ((Vector2)(hs[i].position - last[i])).magnitude);
                    last[i] = hs[i].position;
                }
            }
            string name = Name(rig);
            Check(name + ": two hits leave three hearts flying, two shielding",
                  rig.hearts.ShownCount == 3 && hs[3] != null && !hs[3].gameObject.activeSelf && !hs[4].gameObject.activeSelf);
            Check(name + ": the hearts left glide, never jump (" + jump.ToString("F3") + " a frame)", jump < .12f);
            var o = rig.hearts.OrbitParams;
            if (o.spread == 0f)
            {
                // one orbit: evenly round it, or at the train's spacing
                Check(name + ": the three left re-space round their orbit (off by " + settled.ToString("F2") + " rad)", settled < .3f);
            }
            Teardown(rig);
            collisionDetection.lifeCounter = 0;
        }
    }

    // One orbit: how far the three hearts are from even (a ring) or the
    // train's spacing (radians, worst gap).
    static float SpacingError(Rig rig)
    {
        var o = rig.hearts.OrbitParams;
        if (o.spread != 0f) return 0f;
        var th = new List<float>();
        for (int i = 0; i < 3; i++) th.Add(rig.hearts.Theta(i));
        th.Sort();
        var gaps = new List<float> { th[1] - th[0], th[2] - th[1], 2f * Mathf.PI - th[2] + th[0] };
        gaps.Sort();
        float want = o.gap > 0f ? o.gap : 2f * Mathf.PI / 3f;
        // a train: the two smallest gaps (the widest is ahead of its leader)
        return Mathf.Max(Mathf.Abs(gaps[0] - want), Mathf.Abs(gaps[1] - want));
    }

    // Each style's own life: crossing styles fly on different planes, gyro
    // rings go both ways, and over half a minute hearts throw loop-de-loops
    // (and the playful styles turn back).
    static void FlourishesHappen()
    {
        ShipUiSlots.ScreenOverride = () => Everywhere;
        var seen = new HashSet<HeartStyle>();
        foreach (int id in ShipId.All)
        {
            var style = ShipHeartStyles.For(id);
            if (!seen.Add(style)) continue;
            FreshScene();
            var rig = Build(id, Vector3.zero, true, 4);
            Pose(rig, 20f, 0f, 1.3f, 1f);
            bool looped = false, turned = false, bothWays = false;
            var startDir = new float[4];
            for (int i = 0; i < 4; i++) startDir[i] = Mathf.Sign(rig.hearts.Direction(i));
            for (int f = 0; f < 40 * 60; f++)
            {
                rig.hearts.Place(Dt, Dt);
                bool cw = false, ccw = false;
                for (int i = 0; i < 4; i++)
                {
                    if (rig.hearts.Looping(i)) looped = true;
                    float d = rig.hearts.Direction(i);
                    if (Mathf.Abs(d) > .9f && Mathf.Sign(d) != startDir[i]) turned = true;
                    if (d > .5f) ccw = true;
                    if (d < -.5f) cw = true;
                }
                if (cw && ccw) bothWays = true;
            }
            var o = rig.hearts.OrbitParams;
            string name = Name(rig);
            Check(name + ": throws loop-de-loops now and then (" + rig.hearts.Loops + " in 40s)", looped && rig.hearts.Loops >= 2);
            if (o.turnBackChance > 0f) Check(name + ": and turns back now and then (" + rig.hearts.TurnBacks + " in 40s)", turned && rig.hearts.TurnBacks >= 1);
            if (o.alternate) Check(name + ": neighbours go opposite ways round", bothWays);
            Teardown(rig);
        }
        Check("all six orbit styles are flown (" + seen.Count + ")", seen.Count == 6);
    }
}
