using UnityEngine;

// The feel of a player hit that damages a boss (BossEncounter.OnShipAttackHit):
//   * a small themed impact at the hit point: one 12-cell strip per boss
//     (boss_impact_<key>.png, 256 px cells: Frost ice shards, Ember rising
//     embers, Verdant leaves, Tide splash crown, Storm bolt cross, Desert grit
//     fan, Space ion forks), BurstSeconds long, BurstBig / BurstSmall world
//     units across (weighty vs light hits);
//   * camera shake: an offset that starts at its amplitude and decays to zero
//     over ShakeSeconds (0.5 s), quadratically;
//   * the boss's own white flash (BossActor.Flash(strength)).
// There is no screen overlay any more (the old full-screen cracks were too big).
//
// Weight is the hit's share of one full ultimate hit (BossTarget): a gatling
// shell 1/14, a beam tick .2, a rail slug / blast 1. Everything is pooled and
// built once; nothing is allocated per hit. All of it runs on unscaled time.
//
// Stepped by its own Update in the game; tests call Step(realDt) directly.
public class BossImpact : MonoBehaviour
{
    // ---- tuning (one place) ------------------------------------------------
    public const float ShakeSeconds = .5f;
    public const float ShakeLight = .09f, ShakeHeavy = .3f;   // world units, peak offset at weight ~0 / 1
    public const float ShakeHeartBonus = .08f, ShakeKillBonus = .2f, ShakeMax = .6f;
    public const float LightWeight = .2f;         // below this: a light hit (rate-limited burst)
    public const float BigWeight = .5f;           // from this: the big burst
    public const float WhiteSeconds = .12f;       // the boss's white flash
    // Light hits (weight < BigWeight, no heart, no kill) come in streams (sentries, gatling): their shake is
    // smaller and restarts only when clearly stronger (+25%) and at most this often.
    public const float LightShakeScale = .6f, LightShakeRestart = 1.25f, LightShakeGap = .3f;
    public const float LightBurstGap = .15f;      // light hits burst at most this often
    public const int BurstCells = 12, BurstPool = 6;
    public const float BurstSeconds = .4f;
    public const float BurstBig = 1.5f, BurstSmall = .85f;   // world units across (a cell is 256 px, content ~200)
    public const float BurstTilt = 10f;           // random roll, degrees (Desert stays flat)
    public const int BurstOrder = 40;             // over the boss and its shots

    // The boss art keys with a strip (BossCatalog artKey).
    public static readonly string[] Keys = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

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

    public static int BurstCell(float age) => Mathf.Clamp(Mathf.FloorToInt(age / BurstSeconds * BurstCells), 0, BurstCells - 1);

    // ---- state ---------------------------------------------------------------

    sealed class Burst { public SpriteRenderer sr; public float age = 99f; public string key; }

    static BossImpact instance;
    public static BossImpact Instance => instance;

    float shakeAmp, shakeAge = 99f, shakeClock;
    float lastBurstAt = -99f, lastLightShakeAt = -99f;
    Vector3 shakeApplied;
    Camera shakeCam;
    Burst[] bursts;
    int nextBurst;
    SpriteRenderer lastBurstSr;

    // Counters for tests / diagnostics.
    public int BurstsShown { get { int n = 0; if (bursts != null) foreach (var b in bursts) if (b.age < BurstSeconds) n++; return n; } }
    public int BurstsSpawned { get; private set; }
    public int PoolSize => bursts != null ? bursts.Length : 0;
    public float ShakeNow => ShakeEnvelope(shakeAmp, shakeAge);
    public Vector3 ShakeOffset => shakeApplied;
    public static int Spawned;   // hits that reached the effect (tests)
    // The last burst's boss key, size and current sprite (tests).
    public string LastBurstKey { get; private set; }
    public float LastBurstSize { get; private set; }
    public Sprite LastBurstSprite => lastBurstSr != null ? lastBurstSr.sprite : null;

    // Tests drive the clock through this; null means Time.unscaledTime.
    public static System.Func<float> ClockOverride;
    float Now => ClockOverride != null ? ClockOverride() : Time.unscaledTime;
    public static float ClockNow => ClockOverride != null ? ClockOverride() : Time.unscaledTime;

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

    // A player hit that damaged the boss (art key `key`, e.g. "Frost"), at world point `at`.
    // `heartLost`: it cost a heart (a phase change); `kill`: it took the last hit point.
    public static void Hit(string key, Vector3 at, float weight, bool heartLost, bool kill)
    {
        if (weight <= 0f) return;
        Ensure().Trigger(key, at, weight, heartLost, kill);
    }

    public void Trigger(string key, Vector3 at, float weight, bool heartLost, bool kill)
    {
        Spawned++;
        float now = Now;
        bool light = weight < LightWeight && !heartLost && !kill;

        // shake: restart only when stronger than what is left of the last one (a gatling pulses, never piles up)
        float amp = ShakeAmplitude(weight, heartLost, kill);
        bool streamHit = weight < BigWeight && !heartLost && !kill;
        if (streamHit)
        {
            amp *= LightShakeScale;
            if (amp > ShakeEnvelope(shakeAmp, shakeAge) * LightShakeRestart && now - lastLightShakeAt >= LightShakeGap)
            {
                shakeAmp = amp; shakeAge = 0f; lastLightShakeAt = now;
            }
        }
        else if (amp > ShakeEnvelope(shakeAmp, shakeAge) * 1.05f) { shakeAmp = amp; shakeAge = 0f; }

        if (light && now - lastBurstAt < LightBurstGap) return;
        lastBurstAt = now;
        SpawnBurst(key, at, weight >= BigWeight || heartLost || kill);
    }

    void SpawnBurst(string key, Vector3 at, bool big)
    {
        var b = bursts[nextBurst];
        nextBurst = (nextBurst + 1) % bursts.Length;
        BurstsSpawned++;
        b.age = 0f;
        b.key = key;
        float size = (big ? BurstBig : BurstSmall) * Random.Range(.92f, 1.08f);
        b.sr.transform.position = new Vector3(at.x, at.y, 0f);
        b.sr.transform.localScale = new Vector3(size, size, 1f);
        float tilt = key == "Desert" ? 0f : BurstTilt;
        b.sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-tilt, tilt));
        b.sr.sprite = Cell(key, 0);
        b.sr.enabled = b.sr.sprite != null;
        LastBurstKey = key; LastBurstSize = size; lastBurstSr = b.sr;
    }

    // ---- per frame -----------------------------------------------------------

    void Update() { Step(Mathf.Min(Time.unscaledDeltaTime, .1f)); }

    public void Step(float dt)
    {
        shakeAge += dt;
        shakeClock += dt;
        if (bursts == null) return;
        foreach (var b in bursts)
        {
            if (b.age >= BurstSeconds) { if (b.sr.enabled) b.sr.enabled = false; continue; }
            b.age += dt;
            if (b.age >= BurstSeconds) { b.sr.enabled = false; continue; }
            b.sr.sprite = Cell(b.key, BurstCell(b.age));
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
        bursts = new Burst[BurstPool];
        for (int i = 0; i < bursts.Length; i++)
        {
            var go = new GameObject("Burst" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = BurstOrder;
            sr.enabled = false;
            bursts[i] = new Burst { sr = sr };
        }
    }

    // ---- art -------------------------------------------------------------------

    static readonly Sprite[][] strips = new Sprite[Keys.Length][];

    static int KeyIndex(string key)
    {
        for (int i = 0; i < Keys.Length; i++) if (Keys[i] == key) return i;
        return 3;   // unknown boss: Ember's embers read as a neutral impact
    }

    public static string StripName(string key) => "boss_impact_" + Keys[KeyIndex(key)].ToLowerInvariant();

    // Cell `cell` (0..11) of the boss's strip; sliced once, cached.
    public static Sprite Cell(string key, int cell)
    {
        int k = KeyIndex(key);
        var cache = strips[k];
        if (cache == null || cache[0] == null)
        {
            var t = Resources.Load<Texture2D>(BossArt.ImpactFolder + StripName(key));
            cache = strips[k] = new Sprite[BurstCells];
            if (t != null)
            {
                int w = t.width / BurstCells;
                for (int i = 0; i < BurstCells; i++)
                    cache[i] = Sprite.Create(t, new Rect(i * w, 0, w, t.height), new Vector2(.5f, .5f), w, 0, SpriteMeshType.FullRect);
            }
        }
        return cache[Mathf.Clamp(cell, 0, BurstCells - 1)];
    }

    // Every sprite this effect shows (the art-completeness test).
    public static System.Collections.Generic.IEnumerable<Sprite> AllSprites()
    {
        foreach (var k in Keys)
            for (int i = 0; i < BurstCells; i++) yield return Cell(k, i);
    }
}
