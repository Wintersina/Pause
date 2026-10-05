using UnityEngine;

// The gateway between planets -- and, after the final world, back round to
// the world the run began in.
//
// Built from primitives at runtime. It comes down from above the view to a
// station in the middle of the screen and STAYS there, drifting slowly from
// side to side, until the ship flies into it: there is no lifetime and it
// can not be missed (WorldManager; PortalPressure is what waiting costs).
//
// It moves only while the world is running, on its own clock, so a paused
// game holds it still.
//
// Its approach stays clear: it is parked off-centre, so one side of the
// lane is always open to pilots, and the column it drifts in is closed to
// them (PilotAirspace.TryAdmit asks Reserves). Pilots hold stations above
// it, chasers orbit and leave, and board-riding hazards scroll through in
// about a second, so nothing can sit on it.
public class Portal : MonoBehaviour
{
    // ---- tunables ----
    // World units a second it comes down at.
    public static float FallSpeed = 1.6f;
    // Where it holds: this share of the view's height, from the bottom.
    public static float StationHeight = .5f;
    // Its home x is this far off the centre line, on either side ...
    public static float HomeMinX = .7f, HomeMaxX = 1.2f;
    // ... and it drifts this far either side of home, once per DriftSeconds.
    public static float DriftHalf = .4f, DriftSeconds = 9f;
    public const float Radius = .55f;
    // The column closed to pilots: its drift, its body and this margin.
    public static float ColumnMargin = .1f;

    // The open portal, if any (one at a time).
    public static Portal Live { get; private set; }

    SpriteRenderer ring, core, sparks;
    float spin, clock, held;
    float homeX;

    public float HomeX { get { return homeX; } }
    // It has reached its station (it is holding, not arriving).
    public bool OnStation { get; private set; }
    // Flight seconds since it appeared.
    public float SecondsOpen { get { return clock; } }

    public static float SpawnY { get { return Mathf.Max(7f, CameraFit.ViewTop + 1.2f); } }
    public static float StationY { get { return Mathf.Lerp(CameraFit.ViewBottom, CameraFit.ViewTop, StationHeight); } }
    public static float ColumnHalf { get { return DriftHalf + Radius + ColumnMargin; } }

    // Does [xMin, xMax] cross the column the open portal drifts in?
    public static bool Reserves(float xMin, float xMax)
    {
        var p = Live;
        if (p == null) return false;
        float h = ColumnHalf;
        return xMin < p.homeX + h && xMax > p.homeX - h;
    }

    public static Portal Spawn(Color color)
    {
        var go = new GameObject("~Portal");
        // Just above the visible top (7 on the authored view, higher on a
        // tall screen), off-centre on a random side.
        float side = Random.value < .5f ? -1f : 1f;
        float x = side * Random.Range(HomeMinX, HomeMaxX);
        go.transform.position = new Vector3(x, SpawnY, 0f);

        var p = go.AddComponent<Portal>();
        p.homeX = x;
        p.Build(color);
        Live = p;
        return p;
    }

    void Build(Color color)
    {
        ring = MakePart("PortalVortex", TeleportPortalSprites.FrameAt(0), color, 0);
        core = MakePart("PortalCore", TeleportPortalSprites.FrameAt(4),
            new Color(0.62f, 0.9f, 1f, 0.72f), 1);
        sparks = MakePart("PortalSparks", TeleportPortalSprites.FrameAt(8),
            new Color(0.9f, 0.48f, 1f, 0.36f), 2);
        core.transform.localScale = Vector3.one * 0.72f;
        sparks.transform.localScale = Vector3.one * 1.14f;

        var col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = Radius;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        WorldBanner.Show(PortalPressure.OpenBanner);
    }

    SpriteRenderer MakePart(string name, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = 50 + order;
        return sr;
    }

    void OnDestroy()
    {
        if (Live == this) Live = null;
    }

    void Update()
    {
        // Only while the world is actually moving: a paused game holds it.
        if (WorldManager.Flying) Step(Time.deltaTime);
    }

    // One running frame. Public so edit-mode tests can step it
    // (Time.deltaTime is 0 there).
    public void Step(float dt)
    {
        if (dt <= 0f) return;
        clock += dt;

        Vector3 at = transform.position;
        float station = StationY;
        if (!OnStation)
        {
            at.y = Mathf.Max(station, at.y - FallSpeed * dt);
            if (at.y <= station) OnStation = true;
        }
        else
        {
            // holds its height (the view may change shape) and drifts
            held += dt;
            at.y = station;
            at.x = homeX + DriftHalf * Mathf.Sin(held * 2f * Mathf.PI / Mathf.Max(.1f, DriftSeconds));
        }
        transform.position = at;

        spin += dt * 90f;
        int frame = Mathf.FloorToInt(clock * 15f);
        if (ring != null)
        {
            ring.sprite = TeleportPortalSprites.FrameAt(frame);
            ring.transform.localRotation = Quaternion.Euler(0, 0, spin);
        }
        if (core != null)
        {
            core.sprite = TeleportPortalSprites.FrameAt(frame + 5);
            // the core beats faster as the pressure climbs
            float rate = .6f * Mathf.Min(4f, 1f + .25f * PortalPressure.Level);
            float pulse = 0.5f + Mathf.PingPong(clock * rate, 0.25f);
            core.transform.localScale = Vector3.one * pulse;
            core.transform.localRotation = Quaternion.Euler(0, 0, -spin * 0.6f);
        }
        if (sparks != null)
        {
            sparks.sprite = TeleportPortalSprites.FrameAt(frame + 10);
            sparks.transform.localRotation = Quaternion.Euler(0, 0, spin * 1.45f);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<movePlayer>() == null &&
            other.GetComponentInParent<movePlayer>() == null) return;
        Enter();
    }

    // The ship flew in: on to the next world (or round again).
    public void Enter()
    {
        if (Live == this) Live = null;
        if (WorldManager.Instance != null) WorldManager.Instance.Advance();
        BossUtil.Kill(gameObject);
    }
}


// Portal sprites, drawn once into textures so no art assets are required.
public static class PortalArt
{
    static Sprite ring, core;

    public static Sprite Ring()
    {
        if (ring == null) ring = Build(128, 0.62f, 0.94f);
        return ring;
    }

    public static Sprite Core()
    {
        if (core == null) core = Build(128, 0f, 0.72f);
        return core;
    }

    static Sprite Build(int size, float innerR, float outerR)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a;
            if (d > outerR) a = 0f;
            else if (d < innerR) a = 0f;
            else
            {
                // soft falloff at both edges so the ring glows rather than cuts
                float outerFade = Mathf.InverseLerp(outerR, outerR - 0.14f, d);
                float innerFade = innerR <= 0f ? 1f : Mathf.InverseLerp(innerR, innerR + 0.14f, d);
                a = Mathf.Clamp01(Mathf.Min(outerFade, innerFade));
            }
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
