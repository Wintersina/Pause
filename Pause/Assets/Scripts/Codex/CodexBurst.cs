using UnityEngine;
using UnityEngine.UI;

// The composed elite death for the codex (an elite with no death strip of its
// own): the same pieces EliteDeath uses in the game -- the ship's drawing cut
// into shards that tumble out under gravity, a ring, a glow and magenta / cyan
// sparks (EliteFx.Debris, EliteFxArt) -- drawn as UI Images over the art box.
// One fixed pool of Images built on first use; stepping is arithmetic only.
// Driven by CodexAnimator's death clock (unscaled time).
public sealed class CodexBurst
{
    public const float FlashSeconds = .09f;     // the hit drawing, then the break-up
    public const float Duration = 1.15f;        // flash + flight, to the last shard
    public const int Shards = EliteArt.ShardCols * EliteArt.ShardRows;
    public const int Sparks = 16;
    const float ShardLifeMin = .85f, ShardLifeMax = 1.05f;
    // Speeds are in cells per second so they read the same at any art size.
    const float Speed = .3f;

    readonly RectTransform root;
    readonly Image glow, ring;
    readonly Image[] shards = new Image[Shards];
    readonly Image[] sparks = new Image[Sparks];
    readonly Vector2[] pos = new Vector2[Shards + Sparks], vel = new Vector2[Shards + Sparks];
    readonly float[] life = new float[Shards + Sparks], spin = new float[Shards + Sparks], size0 = new float[Shards + Sparks];

    Vector2 origin;
    float cell, shardPx;
    Color shot = Color.white, core = Color.white;
    bool live;

    // The effect layer is a sibling of the art box (so the box's circular mask
    // does not clip the flying pieces), drawn after it, in its own canvas.
    public CodexBurst(RectTransform box)
    {
        var go = new GameObject("DeathFx", typeof(RectTransform));
        root = (RectTransform)go.transform;
        root.SetParent(box.parent, false);
        root.SetSiblingIndex(box.GetSiblingIndex() + 1);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = box.anchoredPosition;
        root.sizeDelta = box.rect.size;
        CodexUi.Isolate(go);
        glow = CodexUi.NewImage("Glow", root, EliteFxArt.Glow, Color.white);
        ring = CodexUi.NewImage("Ring", root, EliteFxArt.Ring, Color.white);
        for (int i = 0; i < Shards; i++) shards[i] = CodexUi.NewImage("Shard", root, null, Color.white);
        for (int i = 0; i < Sparks; i++) sparks[i] = CodexUi.NewImage("Spark", root, EliteFxArt.Spark, Color.white);
        Hide();
    }

    public bool Alive { get { return root != null; } }
    public bool Live { get { return live; } }
    public RectTransform Root { get { return root; } }

    // Re-fits the layer to the box (the panel re-lays out between uses).
    public void Fit(RectTransform box)
    {
        root.anchoredPosition = box.anchoredPosition;
        root.sizeDelta = box.rect.size;
    }

    // Sets the pieces for one elite: its shards, colours and size on screen.
    // `unit`: pixels per world unit of the idle art.
    public bool Prepare(EliteDef def, float unit)
    {
        var cut = EliteArt.Shards(def);
        if (cut == null || unit <= 0f) return false;
        cell = def.cellWorldSize * unit;
        shardPx = def.cellWorldSize * .7f / EliteArt.ShardCols * 1.05f * unit;
        shot = def.ShotColor;
        core = def.ShotCore;
        for (int i = 0; i < Shards; i++)
        {
            shards[i].sprite = cut[i];
            shards[i].rectTransform.sizeDelta = new Vector2(shardPx, shardPx);
            var home = EliteArt.ShardHome(def, i);
            size0[i] = home.magnitude;
        }
        this.def = def;
        return true;
    }

    EliteDef def;

    // Starts the flight at `centre` (the art's anchored position in the box).
    public void Begin(Vector2 centre)
    {
        origin = centre;
        live = true;
        float u = cell / Mathf.Max(.0001f, def.cellWorldSize);   // px per world unit
        for (int i = 0; i < Shards; i++)
        {
            Vector2 home = EliteArt.ShardHome(def, i);
            // the wreck breaks up and out to the nearer side, as in the game
            float side = Mathf.Sign(home.x + .001f);
            Vector2 v = home.normalized * Random.Range(.8f, 1.8f) +
                        new Vector2(side * Random.Range(2.5f, 4.5f), Random.Range(.5f, 2f));
            pos[i] = home * u;
            vel[i] = v * u * Speed;
            spin[i] = Random.Range(-360f, 360f);
            life[i] = Random.Range(ShardLifeMin, ShardLifeMax);
            shards[i].enabled = false;
        }
        for (int k = 0; k < Sparks; k++)
        {
            int i = Shards + k;
            float a = Random.value * Mathf.PI * 2f;
            float s = Random.Range(1.2f, 3f);
            pos[i] = Vector2.zero;
            vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * u * Speed * 1.6f;
            life[i] = Random.Range(.2f, .4f);
            sparks[k].color = k % 2 == 0 ? shot : core;
            sparks[k].enabled = false;
        }
        glow.enabled = ring.enabled = false;
        root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        live = false;
        if (root != null) root.gameObject.SetActive(false);
    }

    // `t`: seconds since the death began (flash first, then the flight).
    public void Step(float t)
    {
        if (!live) return;
        float f = t - FlashSeconds;
        if (f < 0f) return;
        float u = cell / Mathf.Max(.0001f, def.cellWorldSize);

        float rk = Mathf.Clamp01(f / .35f);
        ring.enabled = rk < 1f;
        if (ring.enabled)
        {
            float s = Mathf.Lerp(.35f, 1.15f, rk) * cell;
            Put(ring, origin, s, 0f, new Color(shot.r, shot.g, shot.b, (1f - rk) * .7f));
        }
        float gk = Mathf.Clamp01(f / .25f);
        glow.enabled = gk < 1f;
        if (glow.enabled)
        {
            float s = Mathf.Lerp(.6f, .15f, gk) * cell;
            Put(glow, origin, s, 0f, new Color(shot.r, shot.g, shot.b, 1f - gk));
        }

        for (int i = 0; i < Shards; i++)
        {
            float k = f / life[i];
            if (k >= 1f) { shards[i].enabled = false; continue; }
            // gravity and drag, closed form (no per-frame state)
            Vector2 p = pos[i] + vel[i] * f - new Vector2(0f, 2.2f * u * Speed * .5f * f * f);
            // fades in hard steps, never a smooth ramp
            float a = k < .55f ? 1f : k < .8f ? .6f : .3f;
            shards[i].enabled = true;
            Put(shards[i], origin + p, shardPx, spin[i] * f, new Color(1f, 1f, 1f, a));
        }
        for (int j = 0; j < Sparks; j++)
        {
            int i = Shards + j;
            float k = f / life[i];
            if (k >= 1f) { sparks[j].enabled = false; continue; }
            float size = Mathf.Lerp(.1f, .04f, k) * u;
            Color c = sparks[j].color;
            c.a = k < .55f ? 1f : k < .8f ? .6f : .3f;
            sparks[j].enabled = true;
            Put(sparks[j], origin + pos[i] + vel[i] * f, size, 45f, c);
        }
    }

    static void Put(Image img, Vector2 at, float size, float angle, Color c)
    {
        var rt = img.rectTransform;
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(size, size);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        img.color = c;
    }
}
