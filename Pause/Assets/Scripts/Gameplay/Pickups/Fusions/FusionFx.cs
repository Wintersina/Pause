using UnityEngine;

// Each fusion's own visual (Art/Resources/Fusion/<FusionDef.key>.png: a
// 9-frame flipbook strip drawn by Codex, 209 px cells, transparent). Played
// when the fusion fires (AtomFusions.Fire), centred on the ship, thin and
// low-alpha with an EMPTY middle, drawn just behind the hull (the shield's
// own order), so it never covers the ship, the bullets or the HUD.
//
// One renderer per fusion, built with the run's ship (Build, from
// AtomFusions.BeginRun) and re-used for good: a trigger only rewinds that
// fusion's clock and enables its renderer, so nothing is created or allocated
// per trigger (a chain simply restarts the flipbook). Frames are sliced once
// and shared by every run.
//
// It runs on the world's clock (AtomFusions.Tick: a pause holds it mid-frame)
// and goes dark at once when fusions are off (the kill cam's replay, a dead
// pilot, the tutorial).
public sealed class FusionFx : MonoBehaviour
{
    public const int FrameCount = 9;
    public const float Fps = 14f;                  // ~0.64 s a play
    public const float Alpha = .8f;                // restrained: thin and low glow
    public const string Folder = "Fusion/";

    // The drawn cell's width in world units, by fusion (the lane is ~4.7 u wide).
    static readonly float[] sizes =
    {
        4.2f,  // Vacuum
        5.2f,  // Lifeline (the cord rises out of the cell's top)
        3.8f,  // Bloom
        4.6f,  // Nova
        3.6f,  // Overclock
        3.8f,  // Stasis
        4.0f,  // Dust Storm
        3.4f,  // Singularity
        4.0f,  // Feather Storm
        4.2f,  // Shell Burst
        3.8f,  // Overheal
        4.6f,  // Aftershock
    };

    static FusionFx instance;
    static Sprite[][] frames;              // [fusion][frame], shared across runs

    SpriteRenderer[] layers;
    float[] age;
    bool[] playing;
    Transform follow;
    public static int Builds { get; private set; }
    public static int Plays { get; private set; }

    public static FusionFx Instance => instance;
    public static float SizeOf(FusionId id) { return sizes[(int)id]; }
    public bool IsPlaying(FusionId id) { return playing != null && playing[(int)id]; }
    public int FrameOf(FusionId id) { return playing != null && playing[(int)id] ? Mathf.Min(FrameCount - 1, (int)(age[(int)id] * Fps)) : -1; }
    public SpriteRenderer LayerOf(FusionId id) { return layers != null ? layers[(int)id] : null; }
    public static bool HasArt(FusionId id)
    {
        var f = Frames();
        return f != null && f[(int)id] != null && f[(int)id].Length == FrameCount;
    }

    // With the run's ship (AtomFusions.BeginRun): the renderers, built once.
    public static FusionFx Build(GameObject ship)
    {
        if (ship == null) return null;
        if (instance == null)
        {
            var go = new GameObject("~FusionFx");
            instance = go.AddComponent<FusionFx>();
            instance.Make(ship);
            Builds++;
        }
        instance.follow = ship.transform;
        instance.StopAll();
        return instance;
    }

    void Make(GameObject ship)
    {
        var all = AtomFusionTable.All;
        var f = Frames();
        var hull = ship.GetComponent<SpriteRenderer>();
        layers = new SpriteRenderer[all.Length];
        age = new float[all.Length];
        playing = new bool[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            var go = new GameObject("~Fx_" + all[i].key);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.enabled = false;
            r.color = new Color(1f, 1f, 1f, Alpha);
            if (f != null && f[i] != null && f[i].Length > 0) r.sprite = f[i][0];
            // just behind the hull, like the shield: never over the ship
            if (hull != null) { r.sortingLayerID = hull.sortingLayerID; r.sortingOrder = hull.sortingOrder - 1; }
            go.transform.localScale = Vector3.one * sizes[i] / (f != null && f[i] != null && f[i].Length > 0 ? f[i][0].bounds.size.x : 1f);
            layers[i] = r;
        }
    }

    // Slice every strip once (frames are 1:1 pixel cells, point filtered).
    static Sprite[][] Frames()
    {
        if (frames != null && frames[0] != null && frames[0].Length > 0 && frames[0][0] != null) return frames;
        var all = AtomFusionTable.All;
        var set = new Sprite[all.Length][];
        for (int i = 0; i < all.Length; i++)
        {
            var tex = Resources.Load<Texture2D>(Folder + all[i].key);
            if (tex == null) continue;
            tex.filterMode = FilterMode.Point;
            int cell = tex.height;
            int n = Mathf.Min(FrameCount, tex.width / cell);
            var s = new Sprite[n];
            for (int k = 0; k < n; k++)
            {
                s[k] = Sprite.Create(tex, new Rect(k * cell, 0f, cell, cell), new Vector2(.5f, .5f), cell, 0, SpriteMeshType.FullRect);
                s[k].name = "Fx_" + all[i].key + "_" + k;
            }
            set[i] = s;
        }
        frames = set;
        return frames;
    }

    // A fusion fired: rewind its flipbook (a chain restarts it). Nothing is created.
    public static void Play(FusionId id)
    {
        var fx = instance;
        if (fx == null || fx.layers == null || fx.layers[(int)id] == null || fx.layers[(int)id].sprite == null) return;
        int i = (int)id;
        fx.age[i] = 0f;
        fx.playing[i] = true;
        fx.Place(i);
        fx.layers[i].enabled = true;
        Plays++;
    }

    void Place(int i)
    {
        if (follow != null) layers[i].transform.position = new Vector3(follow.position.x, follow.position.y, follow.position.z);
    }

    // The world's clock (AtomFusions.Tick).
    public static void Advance(float dt)
    {
        var fx = instance;
        if (fx == null || fx.layers == null || dt <= 0f) return;
        var f = Frames();
        for (int i = 0; i < fx.layers.Length; i++)
        {
            if (!fx.playing[i]) continue;
            fx.age[i] += dt;
            int k = (int)(fx.age[i] * Fps);
            if (k >= FrameCount || f == null || f[i] == null || k >= f[i].Length) { fx.Stop(i); continue; }
            fx.layers[i].sprite = f[i][k];
            fx.Place(i);
        }
    }

    // Off when fusions are (kill cam replay, dead pilot, tutorial): hide without waiting for the clock.
    void LateUpdate()
    {
        if (layers == null) return;
        if (!AtomFusions.Enabled) { StopAll(); return; }
        // a paused / frozen world still draws the held frame, glued to the ship
        for (int i = 0; i < layers.Length; i++) if (playing[i]) Place(i);
    }

    void Stop(int i)
    {
        playing[i] = false;
        if (layers[i] != null && layers[i].enabled) layers[i].enabled = false;
    }

    public void StopAll()
    {
        if (layers == null) return;
        for (int i = 0; i < layers.Length; i++) Stop(i);
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
