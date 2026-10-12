using UnityEngine;
using UnityEngine.UI;

// The feel of a player hit that damages a boss (BossEncounter.OnShipAttackHit):
//   * a burst of flame / sparks / smoke at the hit point (boss_hit_burst for
//     weighty hits, boss_hit_burst_small for light ones), 12 cells over 0.5 s;
//   * camera shake: an offset that starts at its amplitude and decays to zero
//     over ShakeSeconds (0.5 s), quadratically;
//   * hit-stop (WorldTimeFx.HitStop, real time) -- 45 ms for a light hit up to
//     90 ms for a kill;
//   * screen cracks: a full-screen crack overlay (one of 4, random) on a
//     screen-space canvas above the HUD and below the pause menu, held a beat
//     then faded out over CrackSeconds (0.5 s); a weighty hit also grows the
//     8-cell crack strip at the impact point. At most MaxCracks overlays exist
//     (pooled, the oldest reused) and light hits crack at most once per
//     LightCrackGap, so a gatling never buries the screen;
//   * the boss's own white flash (BossActor.Flash(strength)).
//
// Weight is the hit's share of one full ultimate hit (BossTarget): a gatling
// shell 1/14, a beam tick .2, a rail slug / blast 1. Everything is pooled and
// built once; nothing is allocated per hit. All of it runs on unscaled time
// (the hit-stop and the ultimate's slow motion must not stretch it).
//
// Stepped by its own Update in the game; tests call Step(realDt) directly.
public class BossImpact : MonoBehaviour
{
    // ---- tuning (one place) ------------------------------------------------
    public const float ShakeSeconds = .5f;
    public const float ShakeLight = .09f, ShakeHeavy = .3f;   // world units, peak offset at weight ~0 / 1
    public const float ShakeHeartBonus = .08f, ShakeKillBonus = .2f, ShakeMax = .6f;
    public const float HitStopLight = .045f, HitStopMid = .06f, HitStopHeavy = .08f, HitStopKill = .09f;
    public const float LightWeight = .2f;         // below this: a light hit (no hit-stop, rare cracks)
    public const float BigWeight = .5f;           // from this: the big burst + the growth strip
    public const float LightStopGap = .6f;        // light hits hit-stop at most this often
    public const float WhiteSeconds = .12f;       // the boss's white flash
    public const float CrackSeconds = .5f;
    public const float CrackHold = .12f;          // full strength for this long, then fades
    public const float LightCrackGap = .45f;
    public const int MaxCracks = 3, MaxLightCracks = 2;
    public const int BurstCells = 12, BurstPool = 6;
    public const float BurstSeconds = .5f;
    public const float BurstBig = 2.4f, BurstSmall = 1.3f;   // world units across
    public const int CanvasOrder = 60;            // over the HUD (0), under PauseQuickActions (90) and the boss intro (590)
    public const int CrackStageCells = 8;

    // ---- pure maths (tested) -----------------------------------------------

    // 0..1 from a hit's weight: gatling shells ~.2, a beam tick ~.4, a full hit 1.
    public static float Intensity(float weight) => Mathf.Pow(Mathf.Clamp01(weight), .6f);

    public static float ShakeAmplitude(float weight, bool heartLost, bool kill)
    {
        float a = Mathf.Lerp(ShakeLight, ShakeHeavy, Intensity(weight));
        if (heartLost) a += ShakeHeartBonus;
        if (kill) a += ShakeKillBonus;
        return Mathf.Min(a, ShakeMax);
    }

    // The offset's magnitude `age` seconds after a shake of `amplitude`: quadratic decay to 0 at ShakeSeconds.
    public static float ShakeEnvelope(float amplitude, float age)
    {
        float k = 1f - Mathf.Clamp01(age / ShakeSeconds);
        return amplitude * k * k;
    }

    public static float HitStopSeconds(float weight, bool kill)
    {
        if (kill) return HitStopKill;
        if (weight < LightWeight) return HitStopLight;
        return Mathf.Lerp(HitStopMid, HitStopHeavy, Mathf.InverseLerp(LightWeight, 1f, Mathf.Clamp01(weight)));
    }

    // The crack overlay's alpha `age` seconds in: full for CrackHold, then linear to 0 at CrackSeconds.
    public static float CrackAlpha(float age)
    {
        if (age < 0f || age >= CrackSeconds) return 0f;
        return age <= CrackHold ? 1f : 1f - (age - CrackHold) / (CrackSeconds - CrackHold);
    }

    public static int GrowthCell(float age) =>
        Mathf.Clamp(Mathf.FloorToInt(age / (CrackHold * 1.5f) * CrackStageCells), 0, CrackStageCells - 1);

    public static int BurstCell(float age) => Mathf.Clamp(Mathf.FloorToInt(age / BurstSeconds * BurstCells), 0, BurstCells - 1);

    // ---- state ---------------------------------------------------------------

    sealed class Crack
    {
        public Image overlay, growth;
        public float age = 99f, strength;
        public bool Active => age < CrackSeconds;
    }
    sealed class Burst { public SpriteRenderer sr; public float age = 99f; public bool big; }

    static BossImpact instance;
    public static BossImpact Instance => instance;

    float shakeAmp, shakeAge = 99f, shakeClock;
    float lastCrackAt = -99f, lastStopAt = -99f, clock;
    Vector3 shakeApplied;
    Camera shakeCam;
    Crack[] cracks;
    Burst[] bursts;
    Canvas canvas;
    int nextBurst, crackPick;
    Vector2 canvasSize = new Vector2(1080f, 2400f);

    // Counters for tests / diagnostics.
    public int CracksShown { get { int n = 0; if (cracks != null) foreach (var c in cracks) if (c.Active) n++; return n; } }
    public int BurstsShown { get { int n = 0; if (bursts != null) foreach (var b in bursts) if (b.age < BurstSeconds) n++; return n; } }
    public int CracksSpawned { get; private set; }
    public float ShakeNow => ShakeEnvelope(shakeAmp, shakeAge);
    public Vector3 ShakeOffset => shakeApplied;
    public static int Spawned;   // hits that reached the effect (tests)
    public Canvas OverlayCanvas => canvas;
    public Image CrackOverlay(int i) => cracks != null && i >= 0 && i < cracks.Length ? cracks[i].overlay : null;
    public float CrackAge(int i) => cracks != null && i >= 0 && i < cracks.Length ? cracks[i].age : 99f;

    // Tests drive the clock through this; null means Time.unscaledTime.
    public static System.Func<float> ClockOverride;
    float Now => ClockOverride != null ? ClockOverride() : Time.unscaledTime;

    // The one effect in the scene, built on first use.
    public static BossImpact Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("~BossImpact");
        instance = go.AddComponent<BossImpact>();
        instance.Build();
        return instance;
    }

    public static void Dispose()
    {
        if (instance != null) BossUtil.Kill(instance.gameObject);
        instance = null;
    }

    void OnDestroy()
    {
        UndoShake();
        if (instance == this) instance = null;
    }

    // ---- the hit -------------------------------------------------------------

    // A player hit that damaged the boss, at world point `at`. `heartLost`: it cost a
    // heart (a phase change); `kill`: it took the last hit point.
    public static void Hit(Vector3 at, float weight, bool heartLost, bool kill)
    {
        if (weight <= 0f) return;
        Ensure().Trigger(at, weight, heartLost, kill);
    }

    public void Trigger(Vector3 at, float weight, bool heartLost, bool kill)
    {
        Spawned++;
        float now = Now;
        bool light = weight < LightWeight && !heartLost && !kill;

        // shake: restart only when stronger than what is left of the last one (a gatling pulses, never piles up)
        float amp = ShakeAmplitude(weight, heartLost, kill);
        if (amp > ShakeEnvelope(shakeAmp, shakeAge) * 1.05f) { shakeAmp = amp; shakeAge = 0f; }

        // hit-stop (real time)
        if (!light || now - lastStopAt >= LightStopGap)
        {
            WorldTimeFx.HitStop(light ? HitStopLight : HitStopSeconds(weight, kill));
            lastStopAt = now;
        }

        SpawnBurst(at, weight >= BigWeight || heartLost || kill);
        SpawnCrack(at, weight, light, now);
    }

    void SpawnBurst(Vector3 at, bool big)
    {
        var b = bursts[nextBurst];
        nextBurst = (nextBurst + 1) % bursts.Length;
        b.age = 0f;
        b.big = big;
        b.sr.transform.position = new Vector3(at.x, at.y, 0f);
        float size = big ? BurstBig : BurstSmall;
        b.sr.transform.localScale = new Vector3(size, size, 1f) * Random.Range(.9f, 1.1f);
        b.sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));
        b.sr.sprite = Cell(big, 0);
        b.sr.enabled = b.sr.sprite != null;
    }

    void SpawnCrack(Vector3 at, float weight, bool light, float now)
    {
        if (light && (now - lastCrackAt < LightCrackGap || CracksShown >= MaxLightCracks)) return;
        // a free slot, else the oldest
        Crack pick = null;
        foreach (var c in cracks) if (!c.Active) { pick = c; break; }
        if (pick == null) { pick = cracks[0]; foreach (var c in cracks) if (c.age > pick.age) pick = c; }
        lastCrackAt = now;
        CracksSpawned++;
        pick.age = 0f;
        pick.strength = light ? .6f : 1f;
        crackPick = (crackPick + 1 + Random.Range(0, 3)) % 4;
        pick.overlay.sprite = CrackSprite(crackPick);
        pick.overlay.enabled = pick.overlay.sprite != null;
        pick.overlay.transform.SetAsLastSibling();
        // the growth strip, scaled up at the impact point, on the weighty hits
        pick.growth.enabled = false;
        if (!light && Camera.main != null)
        {
            Vector2 view = Camera.main.WorldToViewportPoint(at);
            var rt = pick.growth.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Clamp01(view.x), Mathf.Clamp01(view.y));
            rt.anchoredPosition = Vector2.zero;
            float side = Mathf.Lerp(520f, 900f, Intensity(weight));
            rt.sizeDelta = new Vector2(side, side);
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0, 4) * 90f);
            pick.growth.sprite = GrowthSprite(0);
            pick.growth.enabled = pick.growth.sprite != null;
            pick.growth.transform.SetAsLastSibling();
        }
    }

    // ---- per frame -----------------------------------------------------------

    void Update() { Step(Mathf.Min(Time.unscaledDeltaTime, .1f)); }

    public void Step(float dt)
    {
        clock += dt;
        shakeAge += dt;
        shakeClock += dt;
        if (cracks == null) return;
        foreach (var c in cracks)
        {
            if (c.age >= CrackSeconds + 1f) continue;
            c.age += dt;
            bool on = c.Active;
            c.overlay.enabled = on && c.overlay.sprite != null;
            if (on)
            {
                float a = CrackAlpha(c.age) * c.strength;
                c.overlay.color = new Color(1f, 1f, 1f, a);
                if (c.growth.enabled)
                {
                    c.growth.sprite = GrowthSprite(GrowthCell(c.age));
                    c.growth.color = new Color(1f, 1f, 1f, a);
                }
            }
            else c.growth.enabled = false;
        }
        foreach (var b in bursts)
        {
            if (b.age >= BurstSeconds) { if (b.sr.enabled) b.sr.enabled = false; continue; }
            b.age += dt;
            if (b.age >= BurstSeconds) { b.sr.enabled = false; continue; }
            b.sr.sprite = Cell(b.big, BurstCell(b.age));
        }
    }

    void LateUpdate()
    {
        UndoShake();
        float m = ShakeNow;
        if (m < .002f) return;
        var cam = Camera.main;
        if (cam == null) return;
        // high-frequency, slightly flatter vertically: a kick, not a wobble
        shakeApplied = new Vector3(Mathf.Sin(shakeClock * 97f), Mathf.Cos(shakeClock * 71f) * .8f, 0f) * m;
        cam.transform.position += shakeApplied;
        shakeCam = cam;
    }

    void UndoShake()
    {
        if (shakeCam != null) shakeCam.transform.position -= shakeApplied;
        shakeCam = null;
        shakeApplied = Vector3.zero;
    }

    // ---- build ---------------------------------------------------------------

    void Build()
    {
        var cgo = new GameObject("Cracks", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasOrder;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = canvasSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        cracks = new Crack[MaxCracks];
        for (int i = 0; i < cracks.Length; i++)
        {
            var c = new Crack();
            c.overlay = NewImage(cgo.transform, "Crack" + i, true);
            c.growth = NewImage(cgo.transform, "Growth" + i, false);
            cracks[i] = c;
        }
        bursts = new Burst[BurstPool];
        for (int i = 0; i < bursts.Length; i++)
        {
            var go = new GameObject("Burst" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 40;   // over the boss and its shots
            sr.enabled = false;
            bursts[i] = new Burst { sr = sr };
        }
    }

    static Image NewImage(Transform parent, string name, bool full)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.enabled = false;
        var rt = img.rectTransform;
        if (full)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        return img;
    }

    // ---- art -------------------------------------------------------------------

    static Sprite[] crackSprites, growthCells, burstBig, burstSmall;

    static Texture2D Tex(string name) => Resources.Load<Texture2D>(BossArt.ImpactFolder + name);

    static Sprite CrackSprite(int i)
    {
        if (crackSprites == null) crackSprites = new Sprite[4];
        i = Mathf.Clamp(i, 0, 3);
        if (crackSprites[i] == null)
        {
            var t = Tex("crack_0" + (i + 1));
            if (t != null) crackSprites[i] = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        }
        return crackSprites[i];
    }

    public static Sprite GrowthSprite(int cell) => Strip(ref growthCells, "crack_growth", CrackStageCells, cell);
    public static Sprite Cell(bool big, int cell) =>
        big ? Strip(ref burstBig, "boss_hit_burst", BurstCells, cell) : Strip(ref burstSmall, "boss_hit_burst_small", BurstCells, cell);

    static Sprite Strip(ref Sprite[] cache, string name, int cells, int cell)
    {
        if (cache == null || cache.Length != cells || cache[0] == null)
        {
            var t = Tex(name);
            cache = new Sprite[cells];
            if (t != null)
            {
                int w = t.width / cells;
                for (int i = 0; i < cells; i++)
                    cache[i] = Sprite.Create(t, new Rect(i * w, 0, w, t.height), new Vector2(.5f, .5f), w, 0, SpriteMeshType.FullRect);
            }
        }
        return cache[Mathf.Clamp(cell, 0, cells - 1)];
    }

    // Every sprite this effect shows (the art-completeness test).
    public static System.Collections.Generic.IEnumerable<Sprite> AllSprites()
    {
        for (int i = 0; i < 4; i++) yield return CrackSprite(i);
        for (int i = 0; i < CrackStageCells; i++) yield return GrowthSprite(i);
        for (int i = 0; i < BurstCells; i++) { yield return Cell(true, i); yield return Cell(false, i); }
    }
}
