using UnityEngine;

// The blue-atom shield, wrapped round the hull like a glove.
//
// Replaces the old round bubble (ShipShieldBubble). The shape comes from
// ShieldContour -- the hull's own silhouette grown by a few pixels and
// faceted -- cached per ship type, so every roster ship and any future one
// gets a fitted shield with nothing authored per hull. Drawn as one mesh
// (one draw call) in flat Akira-palette cel style: a translucent flat fill,
// a ring of hex energy plates, a crisp teal energy line and an ink stroke
// outside it, all rendered BEHIND the hull so the ship's own art and
// silhouette are never covered.
//
// Animation is contour-driven and stepped like a flipbook (held key poses):
//   Show()    anticipation flash at the nose (squash -> stretch -> glint),
//             then the shield zips round both sides of the hull in ~0.25 s
//             with a white-hot leading edge, and lands with a stretch/squash.
//   idle      highlight dashes march round the outline; a brief flicker.
//   Absorb()  impact frame (whole line white for a beat), an impact flipbook
//             at the hit point, a hard ripple running both ways round the
//             outline, and the plates there crack.
//   expiring  the last ExpireWindow seconds blink with an accelerating beat.
//   Hide()    the plates shatter into pooled flat shards (ShieldShards).
//
// Gameplay is unchanged: collisionDetection still owns the 5.8 s timer and
// the "destroy whatever touches you while atomCheck" rule. While the shield
// is up the ship's hitbox becomes the shield outline (unioned with its normal
// box) so hazards are absorbed where the shield visibly is; see ColliderPath.
//
// Everything advances on scaled time, so it all freezes with the game.
public class ShipShield : MonoBehaviour
{
    public enum Phase { Off, Anticipation, Zip, Idle }

    public const float AnticipationTime = .08f;
    public const float ZipTime = .18f;
    public const float LandTime = .1f;
    public const float FlipbookFps = 20f;     // flash / impact frame rate
    public const float StepFps = 15f;         // marching dashes step rate
    public const float ImpactFrameTime = .05f;
    public const float RippleTime = .3f;
    public const float ExpireWindow = 1.5f;
    public const int DashCount = 3;
    public const float DashLength = .055f;
    public const float DashSpeed = .42f;      // laps per second

    // Remaining shield time; < 0 reads collisionDetection's own timer.
    public float remainingOverride = -1f;

    GameObject root;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Mesh mesh;
    Color32[] colors;
    Vector2[] uvs;
    bool[] cracked;
    SpriteRenderer flash;      // impact flipbook
    SpriteRenderer spark;      // activation anticipation flipbook
    PolygonCollider2D shieldCollider;
    BoxCollider2D hullCollider;
    ShieldContour contour;

    Phase phase = Phase.Off;
    float clock;               // time in the current phase
    float life;                // time since Show (idle effects)
    float hitAge = 99f, hitArc;
    Vector2 hitLocal, hitNormal;
    float blinkPhase;
    float flickerTimer = .7f, flickerLeft;
    int flickerSeed;
    float landAge = 99f;

    public static ShipShield For(GameObject ship)
    {
        var shield = ship.GetComponent<ShipShield>();
        if (shield == null) shield = ship.AddComponent<ShipShield>();
        shield.Ensure();
        return shield;
    }

    public GameObject Visual { get { Ensure(); return root; } }
    public ShieldContour Contour { get { return contour; } }
    public Phase CurrentPhase { get { return phase; } }
    public bool IsUp { get { return phase != Phase.Off; } }
    public bool Blinking { get; private set; }
    public bool MeshVisible { get { return meshRenderer != null && meshRenderer.enabled; } }
    public float ZipFront { get; private set; }
    public int CrackedCount
    {
        get { int n = 0; if (cracked != null) foreach (var c in cracked) if (c) n++; return n; }
    }
    public bool FlashShowing { get { return flash != null && flash.enabled; } }
    public bool SparkShowing { get { return spark != null && spark.enabled; } }
    public float Clock { get { return clock; } }
    public Color32[] Colors { get { return colors; } }
    public PolygonCollider2D ShieldCollider { get { return shieldCollider; } }

    // Scaled time, like gameplay; 0 while the game is frozen (timeScale 0).
    public static float ScaledDelta()
    {
        return Time.timeScale <= 0f ? 0f : Time.deltaTime;
    }

    public float Remaining
    {
        get
        {
            if (remainingOverride >= 0f) return remainingOverride;
            return collisionDetection.atomCheck ? collisionDetection.invTimer : float.MaxValue;
        }
    }

    void Ensure()
    {
        if (root != null) return;

        // The legacy round bubble authored into the old ship prefabs.
        var legacy = transform.Find("Shield") ?? transform.Find("shield");
        if (legacy != null) legacy.gameObject.SetActive(false);

        root = new GameObject("~ContourShield", typeof(MeshFilter), typeof(MeshRenderer));
        root.transform.SetParent(transform, false);
        // Nudged toward the camera so it sorts over the exhaust it shares an
        // order with, while still drawing behind the hull.
        root.transform.localPosition = new Vector3(0f, 0f, -.01f);
        meshFilter = root.GetComponent<MeshFilter>();
        meshRenderer = root.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = ShieldArt.Material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        var hull = GetComponent<SpriteRenderer>();
        int layer = hull != null ? hull.sortingLayerID : 0;
        int order = hull != null ? hull.sortingOrder : 0;
        meshRenderer.sortingLayerID = layer;
        meshRenderer.sortingOrder = order - 1;

        flash = NewFlipbook("~ShieldImpact", layer, order + 4);
        spark = NewFlipbook("~ShieldSpark", layer, order + 4);
        root.SetActive(false);
    }

    SpriteRenderer NewFlipbook(string name, int layer, int order)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(root.transform, false);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sortingLayerID = layer;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // Ship index from "ship<N>" / "ship<N>(Clone)", without allocating.
    public static int ShipIndex(string name)
    {
        if (name == null || !name.StartsWith("ship", System.StringComparison.Ordinal)) return 0;
        int n = 0, i = 4;
        for (; i < name.Length && name[i] >= '0' && name[i] <= '9'; i++) n = n * 10 + (name[i] - '0');
        return i > 4 ? n : 0;
    }

    // The hull the contour is cut from: the ship type's intact art (stable
    // across idle bob frames and damage states), else whatever is showing.
    Sprite ContourSprite()
    {
        int index = ShipIndex(gameObject.name);
        if (index > 0)
        {
            var s = shopingShips.SpriteFor(index, 0);
            if (s != null) return s;
        }
        var hull = GetComponent<SpriteRenderer>();
        return hull != null ? hull.sprite : null;
    }

    bool EnsureContour()
    {
        if (contour != null && mesh != null) return true;
        contour = ShieldContour.For(ContourSprite());
        if (contour == null) return false;
        if (mesh == null)
        {
            mesh = new Mesh { name = "~ContourShield" };
            mesh.MarkDynamic();
        }
        mesh.Clear();
        mesh.vertices = contour.Vertices;
        uvs = (Vector2[])contour.Uvs.Clone();
        mesh.uv = uvs;
        colors = new Color32[contour.Vertices.Length];
        mesh.colors32 = colors;
        mesh.triangles = contour.Triangles;
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;
        cracked = new bool[contour.PlateCount];
        return true;
    }

    // ------------------------------------------------------------------
    // Gameplay hooks (collisionDetection)
    // ------------------------------------------------------------------

    public void Show()
    {
        Ensure();
        root.SetActive(true);
        bool hasShape = EnsureContour();
        if (phase == Phase.Off)
        {
            phase = Phase.Anticipation;
            clock = 0f;
            life = 0f;
            hitAge = 99f;
            landAge = 99f;
            blinkPhase = 0f;
            if (cracked != null) System.Array.Clear(cracked, 0, cracked.Length);
            if (uvs != null) System.Array.Copy(contour.Uvs, uvs, uvs.Length);
        }
        else
        {
            // Another blue atom while already shielded: a fresh charge reads
            // as a land beat and the cracks are mended.
            landAge = 0f;
            blinkPhase = 0f;
            if (cracked != null) System.Array.Clear(cracked, 0, cracked.Length);
        }
        if (hasShape) SwapCollider(true);
        Paint();
    }

    public void Absorb(Vector3 worldPoint)
    {
        if (phase == Phase.Off || contour == null) return;
        Vector2 local = transform.InverseTransformPoint(worldPoint);
        int s = contour.NearestSample(local);
        hitArc = contour.SampleArc[s];
        hitLocal = contour.SamplePoint[s];
        hitNormal = contour.SampleNormal[s].normalized;
        hitAge = 0f;
        // Crack the plates around the hit (at least the nearest one).
        int nearest = -1; float best = float.MaxValue;
        for (int p = 0; p < contour.PlateCount; p++)
        {
            float d = ShieldContour.ArcDistance(contour.QuadArc[contour.PlateQuad[p]], hitArc);
            if (d < .045f) cracked[p] = true;
            if (d < best) { best = d; nearest = p; }
        }
        if (nearest >= 0) cracked[nearest] = true;
        Paint();
    }

    public void Hide()
    {
        if (root == null) return;
        if (phase != Phase.Off && contour != null && root.activeInHierarchy) Shatter();
        phase = Phase.Off;
        Blinking = false;
        SwapCollider(false);
        root.SetActive(false);
    }

    // While shielded the ship's hit zone is the shield outline (unioned with
    // its usual box) instead of the box, so a hazard is absorbed when it
    // touches the shield you can see. Only one of the two is ever enabled,
    // so a single contact never reports twice.
    void SwapCollider(bool shielded)
    {
        if (hullCollider == null) hullCollider = GetComponent<BoxCollider2D>();
        if (hullCollider == null) return;
        if (shielded)
        {
            if (shieldCollider == null)
            {
                shieldCollider = gameObject.AddComponent<PolygonCollider2D>();
                shieldCollider.isTrigger = hullCollider.isTrigger;
            }
            var box = new Rect(hullCollider.offset - hullCollider.size * .5f, hullCollider.size);
            shieldCollider.pathCount = 1;
            shieldCollider.SetPath(0, contour.ColliderPath(box));
            shieldCollider.enabled = true;
            hullCollider.enabled = false;
        }
        else
        {
            hullCollider.enabled = true;
            if (shieldCollider != null) shieldCollider.enabled = false;
        }
    }

    // ------------------------------------------------------------------
    // Animation
    // ------------------------------------------------------------------

    void Update() { Tick(ScaledDelta()); }

    public void Tick(float dt)
    {
        if (phase == Phase.Off || dt <= 0f) return;
        clock += dt;
        life += dt;
        hitAge += dt;
        landAge += dt;

        if (phase == Phase.Anticipation && clock >= AnticipationTime)
        {
            phase = Phase.Zip; clock -= AnticipationTime;
        }
        if (phase == Phase.Zip && clock >= ZipTime)
        {
            phase = Phase.Idle; clock -= ZipTime; landAge = 0f;
        }

        // Flicker: a two-frame dip every ~0.5-1.4 s.
        if (flickerLeft > 0f) flickerLeft -= dt;
        else if ((flickerTimer -= dt) <= 0f)
        {
            flickerSeed = (flickerSeed * 1103515245 + 12345) & 0x7fffffff;
            flickerTimer = .5f + (flickerSeed % 1000) / 1000f * .9f;
            flickerLeft = 2f / StepFps;
        }

        // Expiry: blink with a beat that tightens from 0.3 s to 0.06 s.
        float remaining = Remaining;
        Blinking = phase == Phase.Idle && remaining <= ExpireWindow;
        if (Blinking)
        {
            float period = Mathf.Lerp(.06f, .3f, Mathf.Clamp01(remaining / ExpireWindow));
            blinkPhase += dt / period;
        }
        Paint();
    }

    static float Step(float t, float fps) { return Mathf.Floor(t * fps) / fps; }

    void Paint()
    {
        if (contour == null || mesh == null) { UpdateFlipbooks(); return; }

        bool blinkOff = Blinking && (blinkPhase - Mathf.Floor(blinkPhase)) > .55f;
        meshRenderer.enabled = !blinkOff;

        float front;
        if (phase == Phase.Anticipation) front = 0f;
        else if (phase == Phase.Zip) front = Mathf.Clamp01(Step(clock, 60f) / ZipTime);
        else front = 1f;
        ZipFront = front;
        bool zipping = phase == Phase.Zip || phase == Phase.Anticipation;

        bool impactFrame = hitAge < ImpactFrameTime;
        float ripple = hitAge < RippleTime ? Step(hitAge, 30f) / RippleTime * .5f : -1f;
        bool flicker = flickerLeft > 0f && !zipping;
        float dashSpeed = Blinking ? DashSpeed * 2f : DashSpeed;
        float dashPhase = Step(life, StepFps) * dashSpeed;

        // Fill: comes in once the zip has closed.
        Color32 fill = front >= 1f ? ShieldArt.Fill : ShieldArt.Clear;
        if (flicker) fill.a = (byte)(fill.a / 2);
        for (int i = 0; i < contour.FillVertexCount; i++) colors[i] = fill;

        int plate = 0;
        for (int q = 0; q < contour.QuadCount; q++)
        {
            float arc = contour.QuadArc[q];
            // Distance from the nose, 0 at the nose .. 1 at the tail, so the
            // zip runs down both flanks at once.
            float fromNose = ShieldContour.ArcDistance(arc, 0f) * 2f;
            bool shown = fromNose <= front;
            bool edge = zipping && shown && fromNose > front - .14f;
            bool rippleHot = ripple >= 0f &&
                             Mathf.Abs(ShieldContour.ArcDistance(arc, hitArc) - ripple) < .035f;
            Color32 c;
            byte kind = contour.QuadKind[q];
            if (!shown) c = ShieldArt.Clear;
            else if (kind == ShieldContour.KindInk) c = ShieldArt.Ink;
            else if (kind == ShieldContour.KindLine)
            {
                if (edge || impactFrame || rippleHot) c = ShieldArt.Hot;
                else if (IsDash(arc, dashPhase)) c = ShieldArt.Dash;
                else c = flicker ? ShieldArt.LineDim : ShieldArt.Line;
            }
            else
            {
                c = contour.QuadShadow[q] ? ShieldArt.PlateShadow : ShieldArt.PlateLit;
                if (flicker) c = ShieldArt.PlateShadow;
                int cell = (edge || impactFrame || rippleHot) ? ShieldContour.CellHot
                         : cracked[plate] ? ShieldContour.CellCracked : ShieldContour.CellPlate;
                SetPlateCell(q, cell);
                plate++;
            }
            int v = contour.FillVertexCount + q * 4;
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;
        }
        mesh.colors32 = colors;
        mesh.uv = uvs;

        // Stretch on landing, then squash, then rest: held poses.
        Vector3 pose = Vector3.one;
        if (landAge < LandTime)
            pose = landAge < LandTime * .5f ? new Vector3(.95f, 1.08f, 1f) : new Vector3(1.04f, .97f, 1f);
        else if (hitAge < ImpactFrameTime * 2f)
            pose = new Vector3(1.04f, 1.04f, 1f);
        root.transform.localScale = pose;

        UpdateFlipbooks();
    }

    static bool IsDash(float arc, float phase)
    {
        for (int k = 0; k < DashCount; k++)
        {
            float d = arc - (phase + k / (float)DashCount);
            d -= Mathf.Floor(d);
            if (d < DashLength) return true;
        }
        return false;
    }

    void SetPlateCell(int q, int cell)
    {
        int v = contour.FillVertexCount + q * 4;
        uvs[v] = ShieldContour.CellUv(cell, 0f, 0f);
        uvs[v + 1] = ShieldContour.CellUv(cell, 0f, 1f);
        uvs[v + 2] = ShieldContour.CellUv(cell, 1f, 1f);
        uvs[v + 3] = ShieldContour.CellUv(cell, 1f, 0f);
    }

    void UpdateFlipbooks()
    {
        float U = contour != null ? contour.Unit : .01f;

        // Anticipation at the nose: gather, squash, stretch, then the glint
        // carries a frame into the zip.
        float sparkT = phase == Phase.Anticipation ? clock
                     : phase == Phase.Zip ? AnticipationTime + clock : 99f;
        int sparkFrame = Mathf.FloorToInt(sparkT / (AnticipationTime + ZipTime * .35f) * 4f);
        if (sparkFrame >= 0 && sparkFrame < 4 && contour != null)
        {
            var frames = ShieldArt.Spark;
            spark.sprite = frames[sparkFrame];
            spark.enabled = true;
            spark.transform.localPosition = contour.Polygon[0];
            float size = 16f * U / 1.28f;
            spark.transform.localScale = new Vector3(size, size, 1f);
        }
        else spark.enabled = false;

        int flashFrame = Mathf.FloorToInt(hitAge * FlipbookFps);
        if (flashFrame >= 0 && flashFrame < 4 && contour != null)
        {
            var frames = ShieldArt.Impact;
            flash.sprite = frames[flashFrame];
            flash.enabled = true;
            flash.transform.localPosition = hitLocal + hitNormal * (contour.Unit * .5f);
            float angle = Mathf.Atan2(hitNormal.y, hitNormal.x) * Mathf.Rad2Deg - 90f;
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            float size = 18f * U / 1.28f;
            flash.transform.localScale = new Vector3(size, size, 1f);
        }
        else flash.enabled = false;
    }

    void Shatter()
    {
        var pool = ShieldShards.Instance;
        var hull = GetComponent<SpriteRenderer>();
        int layer = hull != null ? hull.sortingLayerID : 0;
        int order = (hull != null ? hull.sortingOrder : 0) + 4;
        float world = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
        float size = 6f * contour.Unit * world / .64f;
        // At most 20 shards per break; spread evenly round the outline.
        int count = Mathf.Min(contour.PlateCount, 20);
        for (int i = 0; i < count; i++)
        {
            int p = count == contour.PlateCount ? i : i * contour.PlateCount / count;
            Vector3 pos = transform.TransformPoint(contour.PlateCenter[p]);
            Vector3 dir = transform.TransformDirection(contour.PlateNormal[p]).normalized;
            float speed = (1.1f + .45f * (i % 3)) * world * contour.Unit * 30f;
            float rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 37f * i;
            pool.Spawn(pos, dir * speed, size * (i % 4 == 3 ? .8f : 1f), rot, i, layer, order);
        }
    }

    void OnDestroy()
    {
        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
