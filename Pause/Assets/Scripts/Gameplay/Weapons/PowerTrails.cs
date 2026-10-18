using UnityEngine;

// Two small pooled visuals for secret powers:
//   Phase Cloak  translucent copies of the hull left behind a moving ship
//   Ion Battery  a battery pack (the Ion Lancer power art) flying from the
//                ship up to the pause counter
// Both run on gameplay time (like AttackSprite), reuse a fixed set of
// SpriteRenderers and never allocate while playing.
public class PowerTrailSprite : MonoBehaviour
{
    public const int Pool = 8;
    public const float GhostLife = .32f;
    public const float PackLife = .5f;

    static PowerTrailSprite[] ghosts;
    static PowerTrailSprite pack;
    static int IonId => ShipId.FromKey("IonLancer");

    SpriteRenderer sr;
    float age, life;
    Color tint;
    bool flying;
    Vector3 from, to;

    public bool Active { get; private set; }
    public static int GhostsActive { get { int n = 0; if (ghosts != null) for (int i = 0; i < ghosts.Length; i++) if (ghosts[i] != null && ghosts[i].Active) n++; return n; } }
    public static bool PackActive => pack != null && pack.Active;
    public static Vector3 LastPackTarget { get; private set; }

    static PowerTrailSprite Make(string name)
    {
        var go = new GameObject(name);
        var p = go.AddComponent<PowerTrailSprite>();
        p.sr = go.AddComponent<SpriteRenderer>();
        go.SetActive(false);
        return p;
    }

    // One translucent copy of the hull where it is right now.
    public static PowerTrailSprite Ghost(SpriteRenderer hull, float alpha)
    {
        if (hull == null || hull.sprite == null) return null;
        if (ghosts == null || ghosts[0] == null)
        {
            ghosts = new PowerTrailSprite[Pool];
            for (int i = 0; i < Pool; i++) ghosts[i] = Make("~phaseGhost");
        }
        PowerTrailSprite g = null;
        float oldest = -1f;
        for (int i = 0; i < ghosts.Length; i++)
        {
            if (ghosts[i] == null) ghosts[i] = Make("~phaseGhost");
            if (!ghosts[i].Active) { g = ghosts[i]; break; }
            if (ghosts[i].age > oldest) { oldest = ghosts[i].age; g = ghosts[i]; }
        }
        var t = hull.transform;
        g.transform.SetPositionAndRotation(t.position, t.rotation);
        g.transform.localScale = t.lossyScale;
        g.sr.sprite = hull.sprite;
        g.sr.flipX = hull.flipX;
        g.sr.flipY = hull.flipY;
        g.sr.sortingLayerID = hull.sortingLayerID;
        g.sr.sortingOrder = hull.sortingOrder - 1;
        g.tint = hull.color;
        g.tint.a = alpha;
        g.sr.color = g.tint;
        g.age = 0f; g.life = GhostLife; g.flying = false;
        g.Active = true;
        g.gameObject.SetActive(true);
        return g;
    }

    // The battery pack from `from` to the pause counter.
    public static PowerTrailSprite Pack(int ship, Vector3 from)
    {
        if (pack == null) pack = Make("~ionPack");
        pack.sr.sprite = ShipFxArt.PowerLoop(IonId, 0);
        pack.sr.sortingOrder = 90;
        pack.from = from;
        pack.to = CounterWorldPosition(from);
        LastPackTarget = pack.to;
        pack.transform.position = from;
        pack.transform.localScale = Vector3.one * .35f;
        pack.tint = Color.white;
        pack.sr.color = Color.white;
        pack.age = 0f; pack.life = PackLife; pack.flying = true;
        pack.Active = true;
        pack.gameObject.SetActive(true);
        return pack;
    }

    // Where the pause counter text is, in world space (top of the screen if
    // the HUD cannot be found).
    public static Vector3 CounterWorldPosition(Vector3 fallbackFrom)
    {
        var cam = Camera.main;
        Vector3 w = fallbackFrom;
        var go = GameObject.Find("PauseCounter") ?? GameObject.Find("PausesRemainingText");
        if (cam != null)
        {
            Vector3 screen = new Vector3(Screen.width * .5f, Screen.height * .94f, 0f);
            if (go != null)
            {
                var rt = go.transform as RectTransform;
                var canvas = go.GetComponentInParent<Canvas>();
                if (rt != null && canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) screen = rt.position;
                else if (rt != null) screen = cam.WorldToScreenPoint(rt.position);
            }
            screen.z = Mathf.Abs(cam.transform.position.z);
            w = cam.ScreenToWorldPoint(screen);
        }
        w.z = fallbackFrom.z;
        return w;
    }

    // Steps everything by hand (tests, previews).
    public static void StepAll(float dt)
    {
        if (ghosts != null) for (int i = 0; i < ghosts.Length; i++) if (ghosts[i] != null) ghosts[i].Step(dt);
        if (pack != null) pack.Step(dt);
    }

    void Update()
    {
        if (!Active) return;
        Step(ShipAttackRunner.ScaledDelta());
    }

    public void Step(float dt)
    {
        if (!Active) return;
        age += dt;
        float k = Mathf.Clamp01(age / life);
        if (flying)
        {
            // ease-in arc: leaves slowly, ends fast, with a small sideways bow
            float e = k * k;
            Vector3 p = Vector3.Lerp(from, to, e);
            p.x += Mathf.Sin(k * Mathf.PI) * .6f;
            transform.position = p;
            transform.localScale = Vector3.one * Mathf.Lerp(.38f, .22f, k);
            sr.sprite = ShipFxArt.PowerLoop(IonId, Mathf.FloorToInt(age * 24f));
        }
        else
        {
            var c = tint;
            c.a = tint.a * (1f - k);
            sr.color = c;
        }
        if (k >= 1f) Stop();
    }

    public void Stop()
    {
        Active = false;
        gameObject.SetActive(false);
    }
}
