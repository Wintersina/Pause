using UnityEngine;

// The effects drawn over the gate film (GateFilm) for a GateSim: the player's
// taps (sparks, a burst of star dust with a few tiny player ships streaking out, a glow
// and steam off a leaf), the smash (a bigger dust and ship burst, sparks, steam,
// full-view white flash) and the steam leaking after tap 2.
// The film itself carries the gate, the smoke and steam of the opening and the
// wordmark, so a natural run draws nothing here.
//
// Everything lives in "gate space": root is a child of the film centred on the
// gate, scaled so the shut gate is 1 unit wide (each leaf 0.5 wide, 1 tall).
// All pools are built once; Sync allocates nothing.
public sealed class GateView
{
    // Live caps of the themed burst: a ring buffer, so rapid taps recycle the oldest and never grow.
    public const int MaxDust = 40, MaxShips = 6;
    const int MaxSparks = 96, MaxSteam = 32, MaxGlow = 6;

    // Gate space in film widths: the shut gate's width (two leaves).
    public const float GateSpace = 2f * GateFilm.LeafWidth;

    readonly GateArt art;
    readonly GateSim sim;
    public readonly Transform root;
    SpriteRenderer flash;
    float flashAlpha;
    public float FlashAlpha { get { return flashAlpha; } }

    // star dust (the dock's sparkle glyph)
    readonly Transform[] dT = new Transform[MaxDust];
    // per piece: its size relative to the dock glyph the burst was tuned for (small / medium / large mix)
    readonly float[] dSizeK = new float[MaxDust];
    readonly SpriteRenderer[] dR = new SpriteRenderer[MaxDust];
    readonly Vector2[] dPos = new Vector2[MaxDust], dVel = new Vector2[MaxDust];
    readonly float[] dRot = new float[MaxDust], dSpin = new float[MaxDust], dSize = new float[MaxDust],
                     dLife = new float[MaxDust], dMax = new float[MaxDust];
    int dNext;
    // tiny player ships with a short exhaust streak (slot i always wears hull i % hulls: same-texture slots draw together)
    readonly Transform[] pT = new Transform[MaxShips], pTrailT = new Transform[MaxShips];
    readonly SpriteRenderer[] pR = new SpriteRenderer[MaxShips], pTrailR = new SpriteRenderer[MaxShips];
    readonly Vector2[] pPos = new Vector2[MaxShips], pVel = new Vector2[MaxShips];
    readonly float[] pLife = new float[MaxShips], pMax = new float[MaxShips], pSize = new float[MaxShips];
    int pNext;
    int warmFrames = 2;       // the first frames draw one of every sprite invisibly so no tap pays a first-draw upload
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

    float leakTimer;

    // filmRoot: the film's transform (1 unit = 1 film width, origin at its centre).
    public GateView(Transform filmRoot, GateArt art, GateSim sim)
    {
        this.art = art; this.sim = sim;
        root = new GameObject("Gate effects").transform;
        root.SetParent(filmRoot, false);
        root.localPosition = new Vector3(0f, GateFilm.GateCentreY, -0.02f);
        root.localScale = Vector3.one * GateSpace;

        for (int i = 0; i < MaxDust; i++)
        {
            // every size of star dust in the burst (StarDustArt); the dock glyph backs up a missing sprite
            var piece = StarDustArt.Mixed(i);
            Pool("Dust " + i, piece != null ? piece : art.dust != null ? art.dust : art.white, 6, out dT[i], out dR[i]);
            dSizeK[i] = piece != null ? DustPieceScale(piece, i) : 1f;
        }
        int hulls = HullCount;
        for (int i = 0; i < MaxShips; i++)
        {
            Sprite hull = hulls > 0 ? art.ships[HullOf(i)] : null;
            Pool("Ship " + i, hull, 10 + (hulls > 0 ? HullOf(i) : 0), out pT[i], out pR[i]);
            Pool("Ship trail " + i, art.white, 7, out pTrailT[i], out pTrailR[i]);
        }
        for (int i = 0; i < MaxSparks; i++) Pool("Spark " + i, art.white, 7, out sT[i], out sR[i]);
        for (int i = 0; i < MaxSteam; i++) Pool("Steam " + i, art.steam[0], 5, out tT[i], out tR[i]);
        for (int i = 0; i < MaxGlow; i++) Pool("Glow " + i, art.glow, 8, out gT[i], out gR[i]);
        Transform ft;
        Pool("Flash", art.white, 9, out ft, out flash);
        flash.color = new Color(1, 1, 1, 0);

        WarmUp(true);
        sim.Cracked += OnCracked;
        sim.Smashed += OnSmashed;
    }

    // The scale that makes `piece` draw about as wide as the dock glyph the burst was tuned for, times its size in the mix.
    float DustPieceScale(Sprite piece, int index)
    {
        float glyph = art.dust != null ? art.dust.bounds.size.x : piece.bounds.size.x;
        return glyph / Mathf.Max(.0001f, piece.bounds.size.x) * StarDustArt.Relative(index);
    }

    // hulls that actually loaded (the leading ones: a missing sheet just means fewer kinds)
    int HullCount
    {
        get { int n = 0; if (art.ships != null) for (int i = 0; i < art.ships.Length; i++) if (art.ships[i] != null) n++; return n; }
    }
    int HullOf(int slot)
    {
        int n = art.ships.Length;
        for (int k = 0; k < n; k++) { int h = (slot + k) % n; if (art.ships[h] != null) return h; }
        return 0;
    }

    // Draws every pooled sprite once, invisibly (alpha ~0, tiny), then hides it. Done in the first frames of the card
    // so textures are uploaded and batches are built before the player's first tap.
    void WarmUp(bool on)
    {
        for (int i = 0; i < MaxDust; i++)
        {
            if (!on && dLife[i] > 0f) continue;      // never hide a live sparkle
            dR[i].enabled = on && i == 0; dR[i].color = new Color(1, 1, 1, 0.003f); dT[i].localScale = Vector3.one * 0.001f;
        }
        for (int i = 0; i < MaxShips; i++)
        {
            if (!on && pLife[i] > 0f) continue;
            bool e = on && pR[i].sprite != null;
            pR[i].enabled = e; pR[i].color = new Color(1, 1, 1, 0.003f); pT[i].localScale = Vector3.one * 0.001f;
            pTrailR[i].enabled = e && i == 0; pTrailR[i].color = new Color(1, 1, 1, 0.003f); pTrailT[i].localScale = Vector3.one * 0.001f;
        }
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

    // visible area in world units and the film's world width
    public void SetView(Vector2 viewWorld, float filmWorldWidth)
    {
        float k = 1f / Mathf.Max(filmWorldWidth * GateSpace, 0.0001f);
        Vector2 v = viewWorld * k;
        flash.transform.localPosition = new Vector3(0f, -GateFilm.GateCentreY / GateSpace, -0.04f);
        flash.transform.localScale = new Vector3(v.x * 1.5f, v.y * 1.5f, 1f);
    }

    // Everything off (previews / tests restarting the card).
    public void Clear()
    {
        for (int i = 0; i < MaxDust; i++) { dR[i].enabled = false; dLife[i] = 0f; }
        for (int i = 0; i < MaxShips; i++) { pR[i].enabled = false; pTrailR[i].enabled = false; pLife[i] = 0f; }
        warmFrames = 0;
        for (int i = 0; i < MaxSparks; i++) sR[i].enabled = false;
        for (int i = 0; i < MaxSteam; i++) tR[i].enabled = false;
        for (int i = 0; i < MaxGlow; i++) gR[i].enabled = false;
        flashAlpha = 0f; leakTimer = 0f;
        flash.enabled = false;
    }

    public int ActiveDust { get { int n = 0; for (int i = 0; i < MaxDust; i++) if (dLife[i] > 0f) n++; return n; } }
    public int ActiveShips { get { int n = 0; for (int i = 0; i < MaxShips; i++) if (pLife[i] > 0f) n++; return n; } }

    public int ActiveParticles
    {
        get
        {
            int n = 0;
            n += ActiveDust + ActiveShips;
            for (int i = 0; i < MaxSparks; i++) if (sR[i].enabled) n++;
            for (int i = 0; i < MaxSteam; i++) if (tR[i].enabled) n++;
            return n;
        }
    }

    // ---- events -----------------------------------------------------------

    void OnCracked(int stage)
    {
        if (stage >= 3) return;
        // sparks and chips from where the crack starts on one leaf
        float side = sim.Rand() < 0.5f ? -1f : 1f;
        var p = LeafPoint(side, 0.80f, 0.52f);
        int sp = stage == 1 ? 10 : 20;
        for (int i = 0; i < sp; i++) Spark(p, 0.5f + 0.8f * stage, side);
        ThemedBurst(p, side, stage == 1 ? 8 : 14, stage == 1 ? 2 : 3);
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
        for (int k = 0; k < 2; k++)
        {
            float side = k == 0 ? -1f : 1f;
            ThemedBurst(LeafPoint(side, 0.8f, 0.5f), side, 18, 3);
        }
        for (int i = 0; i < 64; i++) Spark(LeafPoint(i % 2 == 0 ? -1f : 1f, sim.Rand(), sim.Rand()), 2.2f, i % 2 == 0 ? -1 : 1);
        for (int i = 0; i < 8; i++)
            Steam(LeafPoint(i % 2 == 0 ? -1f : 1f, sim.Rand(), sim.Rand()),
                  new Vector2((sim.Rand() - 0.5f) * 1.4f, 0.2f + sim.Rand() * 0.9f), 0.28f + 0.2f * sim.Rand(), 0.8f);
        Glow(Vector2.zero, 3f);
        Glow(new Vector2(-0.2f, 0.05f), 2f);
        Glow(new Vector2(0.2f, -0.05f), 2f);
    }

    // Point on a leaf of the film's gate: u (0..1) across, v (0..1) up. side -1 = left leaf.
    // In gate space a leaf is 0.5 wide and GateHeight / GateSpace (~1) tall.
    Vector2 LeafPoint(float side, float u, float v)
    {
        float cx = GateFilm.LeafCentreX(side, sim.open) / GateSpace;
        // the inner edge (seam) is u = 1 for the left leaf, mirrored on the right
        float lx = (side < 0 ? u - 0.5f : 0.5f - u) * 0.5f;
        return new Vector2(cx + lx, (v - 0.5f) * GateFilm.GateHeight / GateSpace);
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

    // One tap's burst: `dust` star dust sparkles and `ships` tiny hulls streaking out from p, away from the gate's
    // centre (dirX is the side the point is on), so it never sweeps across the mark in the middle. Public so tests
    // can hammer it; the pools are ring buffers (MaxDust / MaxShips), nothing is allocated.
    public void ThemedBurst(Vector2 p, float dirX, int dust, int ships)
    {
        for (int i = 0; i < dust; i++) Dust(p, dirX);
        for (int i = 0; i < ships; i++) Ship(p, dirX, i);
    }

    void Dust(Vector2 p, float dirX)
    {
        int i = dNext; dNext = (dNext + 1) % MaxDust;
        dPos[i] = p;
        float a = (sim.Rand() - 0.5f) * 2.6f;
        float sp = 0.25f + sim.Rand() * 0.55f;
        dVel[i] = new Vector2(Mathf.Cos(a) * dirX * sp, Mathf.Sin(a) * sp + 0.12f);
        dRot[i] = sim.Rand() * 90f - 45f;
        dSpin[i] = (sim.Rand() - 0.5f) * 240f;
        dSize[i] = 0.03f + 0.035f * sim.Rand();
        dMax[i] = dLife[i] = 0.7f + sim.Rand() * 0.5f;
        dR[i].enabled = true;
    }

    void Ship(Vector2 p, float dirX, int n)
    {
        if (HullCount == 0) return;
        int i = pNext; pNext = (pNext + 1) % MaxShips;
        pPos[i] = p;
        // a fan between 15 and 70 degrees above the horizontal, away from the centre
        float a = (15f + 55f * sim.Rand()) * Mathf.Deg2Rad;
        float sp = 0.9f + sim.Rand() * 0.5f;
        pVel[i] = new Vector2(Mathf.Cos(a) * dirX * sp, Mathf.Sin(a) * sp);
        pMax[i] = pLife[i] = 0.55f + sim.Rand() * 0.2f;
        pSize[i] = 0.10f + 0.028f * sim.Rand();   // ~1.4x the old 0.07..0.09
        pR[i].enabled = true; pTrailR[i].enabled = true;
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
        // leaks from the cracks after tap 2
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

        if (warmFrames > 0 && --warmFrames == 0) WarmUp(false);
        UpdateParticles(dt);

        // full-view flash
        flashAlpha = Mathf.Max(0f, flashAlpha - dt * (sim.smashed ? 3.2f : 5f));
        flash.enabled = flashAlpha > 0.003f;
        flash.color = new Color(1f, 0.93f, 0.8f, flashAlpha);
    }

    void UpdateParticles(float dt)
    {
        for (int i = 0; i < MaxDust; i++)
        {
            if (dLife[i] <= 0f) continue;
            dLife[i] -= dt;
            if (dLife[i] <= 0f) { dLife[i] = 0f; dR[i].enabled = false; continue; }
            dVel[i] *= 1f - 1.6f * dt;               // drifts out and slows
            dPos[i] += dVel[i] * dt;
            dRot[i] += dSpin[i] * dt;
            float k = dLife[i] / dMax[i];
            dT[i].localPosition = new Vector3(dPos[i].x, dPos[i].y, -0.02f);
            dT[i].localRotation = Quaternion.Euler(0f, 0f, dRot[i]);
            // the glyph is 72 px at 300 px/unit (0.24 wide): twinkle in, shrink out
            dT[i].localScale = Vector3.one * (dSize[i] / 0.24f * dSizeK[i] * (0.55f + 0.45f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI * 0.5f)));
            dR[i].color = new Color(1f, 0.95f, 0.8f, Mathf.Clamp01(k * 2.5f) * 0.8f);
        }
        for (int i = 0; i < MaxShips; i++)
        {
            if (pLife[i] <= 0f) continue;
            pLife[i] -= dt;
            if (pLife[i] <= 0f) { pLife[i] = 0f; pR[i].enabled = false; pTrailR[i].enabled = false; continue; }
            pVel[i].y += 0.35f * dt;
            pPos[i] += pVel[i] * dt;
            float k = pLife[i] / pMax[i];
            float ang = Mathf.Atan2(pVel[i].y, pVel[i].x) * Mathf.Rad2Deg;
            float speed = pVel[i].magnitude;
            float al = Mathf.Clamp01(k * 3f) * 0.9f;
            pT[i].localPosition = new Vector3(pPos[i].x, pPos[i].y, -0.022f);
            pT[i].localRotation = Quaternion.Euler(0f, 0f, ang - 90f);     // the hull's nose is up
            pT[i].localScale = Vector3.one * pSize[i];
            pR[i].color = new Color(1f, 1f, 1f, al);
            // thin exhaust streak behind the tail, longer when quick; cyan fading to amber with age
            float len = 0.05f + 0.05f * speed;
            Vector2 dir = pVel[i] / Mathf.Max(speed, 0.0001f);
            Vector2 tail = pPos[i] - dir * (pSize[i] * 0.4f + len * 0.5f);
            pTrailT[i].localPosition = new Vector3(tail.x, tail.y, -0.021f);
            pTrailT[i].localRotation = Quaternion.Euler(0f, 0f, ang);
            pTrailT[i].localScale = new Vector3(len, 0.006f, 1f);
            pTrailR[i].color = new Color(0.45f + 0.55f * (1f - k), 0.95f - 0.15f * (1f - k), 1f - 0.65f * (1f - k), al * 0.6f);
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
