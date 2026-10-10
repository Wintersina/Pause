using UnityEngine;

// Draws a GateSim: the two leaves (with crack overlays and bulge), steam jets,
// debris chunks, sparks and flashes. Everything lives in the gate's local space
// (the gate is 1 unit wide; root.localScale maps that to the logo's width).
//
// All pools are built once; Sync allocates nothing.
public sealed class GateView
{
    const int MaxDebris = 56, MaxSparks = 96, MaxSteam = 32, MaxGlow = 6;

    public Transform leftLeaf, rightLeaf;
    public bool leavesVisible = true;

    readonly GateArt art;
    readonly GateSim sim;
    readonly Transform root;
    SpriteRenderer leftRenderer, rightRenderer;
    readonly SpriteRenderer[] leftCrack = new SpriteRenderer[3], rightCrack = new SpriteRenderer[3];
    SpriteRenderer flash;
    float flashAlpha;

    // debris
    readonly Transform[] dT = new Transform[MaxDebris];
    readonly SpriteRenderer[] dR = new SpriteRenderer[MaxDebris];
    readonly Vector2[] dPos = new Vector2[MaxDebris], dVel = new Vector2[MaxDebris];
    readonly float[] dRot = new float[MaxDebris], dSpin = new float[MaxDebris], dSize = new float[MaxDebris], dLife = new float[MaxDebris];
    int dNext;
    // sparks
    readonly Transform[] sT = new Transform[MaxSparks];
    readonly SpriteRenderer[] sR = new SpriteRenderer[MaxSparks];
    readonly Vector2[] sPos = new Vector2[MaxSparks], sVel = new Vector2[MaxSparks];
    readonly float[] sLife = new float[MaxSparks], sMax = new float[MaxSparks];
    int sNext;
    // steam
    readonly Transform[] tT = new Transform[MaxSteam];
    readonly SpriteRenderer[] tR = new SpriteRenderer[MaxSteam];
    readonly Vector2[] tPos = new Vector2[MaxSteam], tVel = new Vector2[MaxSteam];
    readonly float[] tLife = new float[MaxSteam], tMax = new float[MaxSteam], tSize = new float[MaxSteam], tRot = new float[MaxSteam],
                     tAlpha = new float[MaxSteam];
    readonly int[] tRow = new int[MaxSteam];
    int tNext;
    // glow flashes
    readonly Transform[] gT = new Transform[MaxGlow];
    readonly SpriteRenderer[] gR = new SpriteRenderer[MaxGlow];
    readonly float[] gLife = new float[MaxGlow], gSize = new float[MaxGlow];
    int gNext;

    float emitVent;
    float leakTimer;
    Vector2 viewLocal;   // visible area in gate-local units


    public GateView(Transform root, GateArt art, GateSim sim)
    {
        this.root = root; this.art = art; this.sim = sim;
        const float leafW = 0.5f;

        leftLeaf = NewLeaf("Left steel door", art.leftLeaf, leafW, out leftRenderer);
        rightLeaf = NewLeaf("Right steel door", art.rightLeaf, leafW, out rightRenderer);
        for (int i = 0; i < 3; i++)
        {
            leftCrack[i] = NewOverlay("Left cracks " + (i + 1), leftLeaf, art.leftCracks[i], art.leftLeaf);
            rightCrack[i] = NewOverlay("Right cracks " + (i + 1), rightLeaf, art.rightCracks[i], art.rightLeaf);
        }

        for (int i = 0; i < MaxDebris; i++) Pool("Debris " + i, art.debris[0], 6, out dT[i], out dR[i]);
        for (int i = 0; i < MaxSparks; i++) Pool("Spark " + i, art.white, 7, out sT[i], out sR[i]);
        for (int i = 0; i < MaxSteam; i++) Pool("Steam " + i, art.steam[0], 5, out tT[i], out tR[i]);
        for (int i = 0; i < MaxGlow; i++) Pool("Glow " + i, art.glow, 8, out gT[i], out gR[i]);
        Transform ft;
        Pool("Flash", art.white, 9, out ft, out flash);
        flash.color = new Color(1, 1, 1, 0);

        sim.Impact += OnImpact;
        sim.Cracked += OnCracked;
        sim.Smashed += OnSmashed;
    }

    Transform NewLeaf(string name, Sprite sprite, float width, out SpriteRenderer sr)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * (width / sprite.bounds.size.x);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 2;
        return go.transform;
    }

    SpriteRenderer NewOverlay(string name, Transform leaf, Sprite sprite, Sprite leafSprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(leaf, false);
        // The overlay is a slice of a sheet laid out like the door, so it covers the leaf exactly.
        go.transform.localScale = Vector3.one * (leafSprite.bounds.size.x / sprite.bounds.size.x);
        go.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 3;
        sr.enabled = false;
        return sr;
    }

    void Pool(string name, Sprite sprite, int order, out Transform t, out SpriteRenderer sr)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0, 0, -0.02f);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        t = go.transform;
    }

    // visible area in world units and the gate's world width
    public void SetView(Vector2 viewWorld, float gateWorldWidth)
    {
        float k = 1f / Mathf.Max(gateWorldWidth, 0.0001f);
        viewLocal = viewWorld * k;
        flash.transform.localScale = new Vector3(viewLocal.x * 1.5f, viewLocal.y * 1.5f, 1f);
    }

    // ---- events -----------------------------------------------------------

    void OnImpact(float s, int step)
    {
        if (step >= 0)
        {
            // a step: the leaves slam apart at the seam
            int sp = 8 + (int)(26f * s), db = 2 + (int)(5f * s);
            for (int i = 0; i < sp; i++) Spark(Seam(i % 2), 0.4f + 1.4f * s, i % 2 == 0 ? -1 : 1);
            for (int i = 0; i < db; i++) Chunk(Seam(i % 2), 0.06f + 0.05f * sim.Rand(), 0.5f + s, i % 2 == 0 ? -1 : 1);
            for (int i = 0; i < 3; i++) Steam(Seam(i % 2) + new Vector2(0, -0.1f), new Vector2((i % 2 == 0 ? -1 : 1) * (0.2f + sim.Rand() * 0.4f), 0.2f + sim.Rand() * 0.5f), 0.14f + 0.08f * sim.Rand(), 0.55f);
            Glow(Seam(0) * 0.5f, 0.5f + 0.5f * s);
            flashAlpha = Mathf.Max(flashAlpha, 0.12f + 0.2f * s);
        }
    }

    void OnCracked(int stage)
    {
        if (stage >= 3) return;
        // sparks and chips from where the crack starts on one leaf
        float side = sim.Rand() < 0.5f ? -1f : 1f;
        var p = LeafPoint(side, 0.80f, 0.52f);
        int sp = stage == 1 ? 8 : 18;
        for (int i = 0; i < sp; i++) Spark(p, 0.5f + 0.8f * stage, side);
        for (int i = 0; i < stage * 2; i++) Chunk(p, 0.04f + 0.04f * sim.Rand(), 0.6f, side);
        Glow(p, 0.6f + 0.3f * stage);
        flashAlpha = Mathf.Max(flashAlpha, 0.18f * stage);
        if (stage >= 2)
            for (int i = 0; i < 5; i++)
                Steam(LeafPoint(i % 2 == 0 ? -1f : 1f, 0.5f + 0.2f * sim.Rand(), 0.3f + 0.4f * sim.Rand()),
                      new Vector2((sim.Rand() - 0.5f) * 0.5f, 0.4f + sim.Rand() * 0.4f), 0.12f, 0.6f);
    }

    void OnSmashed()
    {
        flashAlpha = 1f;
        for (int i = 0; i < 40; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            Chunk(LeafPoint(side, sim.Rand(), sim.Rand()), 0.07f + 0.13f * sim.Rand(), 1.6f, side);
        }
        for (int i = 0; i < 64; i++) Spark(LeafPoint(i % 2 == 0 ? -1f : 1f, sim.Rand(), sim.Rand()), 2.2f, i % 2 == 0 ? -1 : 1);
        for (int i = 0; i < 8; i++)
            Steam(LeafPoint(i % 2 == 0 ? -1f : 1f, sim.Rand(), sim.Rand()),
                  new Vector2((sim.Rand() - 0.5f) * 1.4f, 0.2f + sim.Rand() * 0.9f), 0.28f + 0.2f * sim.Rand(), 0.8f);
        Glow(Vector2.zero, 3f);
        Glow(new Vector2(-0.2f, 0.05f), 2f);
        Glow(new Vector2(0.2f, -0.05f), 2f);
    }

    float LeafCenterX(float side) { return side * (0.25f + 0.58f * Mathf.Clamp01(sim.open)); }

    // Point on a leaf: u (0..1) across, v (0..1) up, measured on the leaf as drawn.
    // side -1 = left leaf. The leaf is 0.5 wide and ~0.68 tall.
    Vector2 LeafPoint(float side, float u, float v)
    {
        float cx = LeafCenterX(side);
        // the inner edge (seam) is u = 1 for the left leaf, mirrored on the right
        float lx = (side < 0 ? u - 0.5f : 0.5f - u) * 0.5f;
        return new Vector2(cx + lx, (v - 0.5f) * 0.68f);
    }

    // A point on a leaf's inner edge (where the leaves meet), at a random height.
    Vector2 Seam(int side)
    {
        float x = side == 0 ? LeafCenterX(-1f) + 0.25f : LeafCenterX(1f) - 0.25f;
        return new Vector2(x, (sim.Rand() - 0.5f) * 0.6f);
    }

    // ---- spawners ---------------------------------------------------------

    void Spark(Vector2 p, float power, float dirX)
    {
        int i = sNext; sNext = (sNext + 1) % MaxSparks;
        float a = (sim.Rand() - 0.5f) * 2.4f;
        float sp = (0.5f + sim.Rand() * 1.6f) * power;
        sPos[i] = p;
        sVel[i] = new Vector2(Mathf.Cos(a) * dirX * sp, Mathf.Sin(a) * sp + 0.2f * power);
        sMax[i] = sLife[i] = 0.25f + sim.Rand() * 0.45f;
        sR[i].enabled = true;
    }

    void Chunk(Vector2 p, float size, float power, float dirX)
    {
        int i = dNext; dNext = (dNext + 1) % MaxDebris;
        dPos[i] = p;
        float a = (sim.Rand() - 0.35f) * 2.2f;
        float sp = (0.4f + sim.Rand() * 0.9f) * power;
        dVel[i] = new Vector2(Mathf.Cos(a) * dirX * sp, Mathf.Sin(a) * sp + 0.25f);
        dRot[i] = sim.Rand() * 360f;
        dSpin[i] = (sim.Rand() - 0.5f) * 900f;
        size *= 1.4f * GateArt.DebrisDrawScale;
        dSize[i] = size;
        dLife[i] = 1.4f + sim.Rand() * 0.6f;
        dR[i].sprite = art.debris[(int)(sim.Rand() * art.debris.Length) % art.debris.Length];
        dT[i].localScale = Vector3.one * (size / 1.28f);
        dR[i].color = Color.white;
        dR[i].enabled = true;
    }

    void Steam(Vector2 p, Vector2 v, float size, float alpha)
    {
        int i = tNext; tNext = (tNext + 1) % MaxSteam;
        tPos[i] = p; tVel[i] = v;
        tMax[i] = tLife[i] = 0.5f + sim.Rand() * 0.45f;
        tSize[i] = size; tAlpha[i] = alpha; tRot[i] = (sim.Rand() - 0.5f) * 60f;
        tRow[i] = (int)(sim.Rand() * GateArt.SteamRows) % GateArt.SteamRows;
        tR[i].sprite = art.steam[tRow[i] * GateArt.SteamCols];
        tR[i].enabled = true;
    }

    void Glow(Vector2 p, float size)
    {
        int i = gNext; gNext = (gNext + 1) % MaxGlow;
        gT[i].localPosition = new Vector3(p.x, p.y, -0.03f);
        gSize[i] = 0.35f * size;
        gLife[i] = 0.22f;
        gR[i].enabled = true;
    }

    // ---- per frame --------------------------------------------------------

    public void Sync(float dt)
    {
        // leaves
        bool show = leavesVisible && !(sim.smashed && sim.smashTime > 0.03f);
        leftRenderer.enabled = rightRenderer.enabled = show;
        float travel = 0.58f * sim.open;
        float bulgeX = 0.012f * sim.bulge;
        float scl = 1f + 0.03f * sim.bulge;
        SetLeaf(leftLeaf, leftRenderer, art.leftLeaf, -(0.25f + travel) - bulgeX + sim.leftJitter.x, sim.leftJitter.y, sim.leftRot, scl);
        SetLeaf(rightLeaf, rightRenderer, art.rightLeaf, (0.25f + travel) + bulgeX - sim.rightJitter.x, sim.rightJitter.y, -sim.rightRot, scl);
        int stage = Mathf.Clamp(sim.crackStage, 0, 3);
        for (int i = 0; i < 3; i++)
        {
            bool on = show && stage == i + 1;
            leftCrack[i].enabled = on; rightCrack[i].enabled = on;
        }

        // continuous steam: jets from the leaf grilles, pressure building
        float p = sim.pressure;
        float rate = sim.smashed ? 0f : 3f + p * p * 26f + (sim.crackStage >= 2 ? 14f : 0f);
        emitVent += rate * dt;
        while (emitVent >= 1f)
        {
            emitVent -= 1f;
            float side = sim.Rand() < 0.5f ? -1f : 1f;
            Vector2 v = LeafPoint(side, 0.6f, 0.64f);
            // jets angle up and away from the seam, harder as pressure builds
            float j = 0.25f + p * 0.9f;
            Steam(v,
                  new Vector2(side * (0.1f + sim.Rand() * 0.35f) * j, (0.35f + sim.Rand() * 0.6f) * j),
                  0.07f + 0.05f * sim.Rand() + 0.05f * p, 0.35f + 0.3f * p);
        }
        // leaks from the cracks once the door is bulging
        if (sim.crackStage >= 2 && !sim.smashed)
        {
            leakTimer += dt;
            while (leakTimer > 0.045f)
            {
                leakTimer -= 0.045f;
                float side = sim.Rand() < 0.5f ? -1f : 1f;
                Steam(LeafPoint(side, 0.35f + 0.5f * sim.Rand(), 0.25f + 0.55f * sim.Rand()),
                      new Vector2(side * 0.15f, 0.5f + sim.Rand() * 0.5f), 0.07f, 0.5f);
            }
        }

        UpdateParticles(dt);

        // full-view flash
        flashAlpha = Mathf.Max(0f, flashAlpha - dt * (sim.smashed ? 3.2f : 5f));
        flash.enabled = flashAlpha > 0.003f;
        flash.color = new Color(1f, 0.93f, 0.8f, flashAlpha);
    }

    void SetLeaf(Transform t, SpriteRenderer sr, Sprite sprite, float x, float y, float rot, float scale)
    {
        t.localPosition = new Vector3(x, y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, rot);
        t.localScale = Vector3.one * (0.5f / sprite.bounds.size.x * scale);
    }

    void UpdateParticles(float dt)
    {
        for (int i = 0; i < MaxDebris; i++)
        {
            if (!dR[i].enabled) continue;
            dLife[i] -= dt;
            if (dLife[i] <= 0f || dPos[i].y < -2.5f) { dR[i].enabled = false; continue; }
            dVel[i].y -= 3.2f * dt;
            dPos[i] += dVel[i] * dt;
            dRot[i] += dSpin[i] * dt;
            dT[i].localPosition = new Vector3(dPos[i].x, dPos[i].y, -0.02f);
            dT[i].localRotation = Quaternion.Euler(0f, 0f, dRot[i]);
            float a = Mathf.Clamp01(dLife[i] / 0.35f);
            dR[i].color = new Color(1f, 1f, 1f, a);
        }
        for (int i = 0; i < MaxSparks; i++)
        {
            if (!sR[i].enabled) continue;
            sLife[i] -= dt;
            if (sLife[i] <= 0f) { sR[i].enabled = false; continue; }
            sVel[i].y -= 2.4f * dt;
            sPos[i] += sVel[i] * dt;
            float k = sLife[i] / sMax[i];
            float speed = sVel[i].magnitude;
            sT[i].localPosition = new Vector3(sPos[i].x, sPos[i].y, -0.025f);
            sT[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(sVel[i].y, sVel[i].x) * Mathf.Rad2Deg);
            sT[i].localScale = new Vector3(0.006f + speed * 0.022f, 0.007f, 1f);
            sR[i].color = new Color(1f, 0.55f + 0.45f * k, 0.15f + 0.6f * k * k, Mathf.Clamp01(k * 2f));
        }
        for (int i = 0; i < MaxSteam; i++)
        {
            if (!tR[i].enabled) continue;
            tLife[i] -= dt;
            if (tLife[i] <= 0f) { tR[i].enabled = false; continue; }
            float age = 1f - tLife[i] / tMax[i];
            tVel[i] *= 1f - 0.9f * dt;
            tPos[i] += tVel[i] * dt;
            // the atlas columns are growth stages: step through them with age
            tR[i].sprite = art.steam[tRow[i] * GateArt.SteamCols + Mathf.Min(GateArt.SteamCols - 1, (int)(age * GateArt.SteamCols))];
            float size = tSize[i] * (0.6f + age * 1.4f) * GateArt.SteamDrawScale;
            tT[i].localPosition = new Vector3(tPos[i].x, tPos[i].y, -0.015f);
            tT[i].localRotation = Quaternion.Euler(0f, 0f, tRot[i] * age);
            tT[i].localScale = Vector3.one * (size / tR[i].sprite.bounds.size.x);
            tR[i].color = new Color(0.78f, 0.88f, 0.95f, Mathf.Sin(age * Mathf.PI) * tAlpha[i]);
        }
        for (int i = 0; i < MaxGlow; i++)
        {
            if (!gR[i].enabled) continue;
            gLife[i] -= dt;
            if (gLife[i] <= 0f) { gR[i].enabled = false; continue; }
            float k = gLife[i] / 0.22f;
            gT[i].localScale = Vector3.one * (gSize[i] * (1.4f - 0.4f * k));
            gR[i].color = new Color(1f, 0.75f, 0.35f, k);
        }
    }
}
