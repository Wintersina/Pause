using UnityEngine;

// The primed instant atoms (AtomFusionState's slots holding a heal, pause,
// capacitor or Mending): each is a small dot in its atom's colour circling
// the ship, like a heart of HeartOrbit but much plainer -- thin, low glow,
// restrained -- until it fuses or its FusionTuning.MoteSeconds run out (it
// fades over its last second). A timed atom (shield, magnet, Hard Shell)
// needs no mote: its own effect on screen is the window.
//
// Built once with the ship (two renderers, one per slot), on the world's
// clock (AtomFusions.Clock: a pause holds them still), nothing allocated per frame.
public sealed class FusionMotes : MonoBehaviour
{
    SpriteRenderer[] dots;
    SpriteRenderer hull;
    static Sprite dotSprite;

    public static FusionMotes Attach(GameObject ship)
    {
        if (ship == null) return null;
        var m = ship.GetComponent<FusionMotes>();
        if (m == null) m = ship.AddComponent<FusionMotes>();
        return m;
    }

    // Visible motes (tests).
    public int Shown
    {
        get
        {
            int n = 0;
            if (dots != null) for (int i = 0; i < dots.Length; i++) if (dots[i] != null && dots[i].enabled) n++;
            return n;
        }
    }

    void Awake()
    {
        hull = GetComponent<SpriteRenderer>();
        dots = new SpriteRenderer[FusionTuning.MaxSlots];
        for (int i = 0; i < dots.Length; i++)
        {
            var go = new GameObject("~FusionMote" + i);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = DotSprite;
            r.enabled = false;
            if (hull != null) { r.sortingLayerID = hull.sortingLayerID; r.sortingOrder = hull.sortingOrder + 2; }
            dots[i] = r;
        }
    }

    // A small pixel disc (built once): crisp, no halo ring, no outline.
    static Sprite DotSprite
    {
        get
        {
            if (dotSprite != null) return dotSprite;
            const int n = 6;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "FusionMote" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - (n - 1) * .5f, dy = y - (n - 1) * .5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = d <= 1.2f ? (byte)255 : d <= 2.6f ? (byte)140 : (byte)0;   // a bright core, a soft rim
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            dotSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
            dotSprite.name = "FusionMote";
            return dotSprite;
        }
    }

    void LateUpdate()
    {
        var state = AtomFusions.State;
        float now = AtomFusions.Clock;
        bool on = AtomFusions.Enabled;
        // the hull's scale is not the mote's: keep a fixed world size
        Vector3 s = transform.lossyScale;
        float sx = Mathf.Abs(s.x) > 1e-4f ? 1f / Mathf.Abs(s.x) : 1f, sy = Mathf.Abs(s.y) > 1e-4f ? 1f / Mathf.Abs(s.y) : 1f;
        for (int i = 0; i < dots.Length; i++)
        {
            var r = dots[i];
            var slot = i < state.SlotCount ? state.SlotAt(i) : default;
            bool show = on && slot.used && now < slot.expires && !AtomFusionTable.IsTimed(slot.kind);
            if (r.enabled != show) r.enabled = show;
            if (!show) continue;
            float angle = (now - slot.primedAt) * FusionTuning.MoteSpeed + i * Mathf.PI;
            Vector3 local = new Vector3(Mathf.Cos(angle) * FusionTuning.MoteOrbit * sx, Mathf.Sin(angle) * FusionTuning.MoteOrbit * .55f * sy, 0f);
            r.transform.localPosition = local;
            r.transform.localScale = new Vector3(FusionTuning.MoteSize * sx, FusionTuning.MoteSize * sy, 1f);
            Color c = AtomFusionTable.ColourOf(slot.kind);
            float left = slot.expires - now;
            c.a = FusionTuning.MoteAlpha * Mathf.Clamp01(left);   // fades over its last second
            if (r.color != c) r.color = c;
        }
    }
}
