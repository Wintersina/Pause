using UnityEngine;
using UnityEngine.UI;

// The boss intro: a one-tick flat flash and the WARNING slab that snaps in
// and blinks (a screen overlay), then the boss's NAME -- nothing else -- on
// its plate, slamming in from the right. It holds, readable, then cracks
// and crumbles: the plate splits, and letter by letter the name drops away
// with a kick, a spin and a puff of sparks in the boss's colour, tumbling
// down past the ship and off the bottom of the screen.
//
// The name is runtime text, one Text per letter, on a world-space canvas
// (so it really falls past the ship and is measured against the camera's
// edges); the plate, crack and sparks are code-drawn cel polygons
// (BossNameShapes), so no baked card art -- and no spec line -- is involved.
// Nothing has a collider, a raycaster or a raycast target: it may still be
// falling as the fight starts and is purely visual. Everything is built once
// in Play (a fixed pool, no per-frame allocation; pieces are shown and hidden
// by culling, never rebuilt), stepped on unscaled time (the world is frozen
// through the intro) and moves on held ticks or plain ballistics, never
// eased fades. The overlay stays up through the fight for the ultimate's hit
// flashes and is closed by the encounter.
public class BossIntroUI : MonoBehaviour
{
    static readonly Color Bone = new Color(.957f, .918f, .831f);

    const int NameCanvasOrder = 150;
    const int SparksPerLetter = 3;
    const int SparkTicks = 6;
    const float LetterKickUp = 1.4f;     // the largest upward kick a letter gets
    const float HalfKickUp = .5f;
    const float MaxFontSize = 104f;

    sealed class Piece
    {
        public RectTransform t;
        public Graphic g;
        public Vector2 home;     // canvas-local, on the plate
        public float radius;     // world, bounding the piece at any spin
        public float releaseAt;  // intro clock
        public bool letter;
        public bool falling, gone;
        public Vector2 pos, vel;
        public float rot, spin;
    }

    sealed class Spark
    {
        public RectTransform t;
        public Graphic g;
        public Vector2 pos, vel;
        public float age = -1f;
    }

    BossDef boss;
    Image flash, warning;
    float clock;
    float flashLeft;
    Color flashColor;
    bool landed, cracked;

    Camera cam;
    GameObject world;
    RectTransform plateRoot;
    Graphic plate, crack;
    Piece[] pieces = new Piece[0];
    Spark[] sparks = new Spark[0];
    int letterCount;
    string letters = "";
    float scale = 1f;           // canvas units (plate px) -> world
    float slideFrom;            // world x offset the plate slides in from
    float gravity;
    Color tint;

    // ---- read-outs for tests -------------------------------------------
    public float Clock => clock;
    public string ShownName => letters;
    public int LetterCount => letterCount;
    public bool Cracked => cracked;
    public GameObject WorldRoot => world;
    public int PieceCount => pieces.Length;
    public RectTransform PieceAt(int i) => pieces[i].t;
    public float PieceRadius(int i) => pieces[i].radius;
    public bool PieceVisible(int i) => !pieces[i].g.canvasRenderer.cull && pieces[i].g.enabled;
    public bool AllGone
    {
        get
        {
            foreach (var p in pieces) if (!p.gone) return false;
            return true;
        }
    }
    // When the last piece is due off screen.
    public static float FinishedAt => BossConfig.NameGoneAt;

    public static BossIntroUI Play(BossDef boss)
    {
        var root = new GameObject("~BossIntro", typeof(Canvas), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 590; // under WorldBanner (600)
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var ui = root.AddComponent<BossIntroUI>();
        ui.boss = boss;
        ui.flash = NewImage(root.transform, "Flash", null, Vector2.zero, Vector2.zero, true);
        ui.flash.color = new Color(1f, 1f, 1f, 0f);
        ui.warning = NewImage(root.transform, "Warning", BossArt.Warning(), new Vector2(-1000f, 330f), new Vector2(760f, 143f), false);
        ui.BuildName();
        ui.Flash(Bone, 2, .9f);
        ui.Step(0f);
        return ui;
    }

    static Image NewImage(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, bool stretch)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = sprite != null;
        img.raycastTarget = false;
        img.enabled = sprite != null || stretch;
        return img;
    }

    T NewGraphic<T>(string name, Vector2 pos, Vector2 size) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(plateRoot, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var g = go.GetComponent<T>();
        g.raycastTarget = false;
        Show(g, false);
        return g;
    }

    // Shown / hidden by culling (only on a change, never per frame). A culled
    // graphic skips its rebuilds, so it is re-dirtied the moment it shows.
    static void Show(Graphic g, bool on)
    {
        if (g == null || g.canvasRenderer.cull != on) return;
        g.canvasRenderer.cull = !on;
        if (on) g.SetAllDirty();
    }

    // ---- the name plate ------------------------------------------------

    // Sizes the plate to the screen the way the old overlay card was (95% of
    // the visible width, a little above the middle), sets the name on it as
    // text, and builds every piece the crumble will ever need.
    void BuildName()
    {
        tint = boss != null ? boss.flash : Bone;
        tint.a = 1f;
        cam = Camera.main;
        float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float halfW = cam != null ? halfH * cam.aspect : CameraFit.GameplayHalfWidth;
        Vector2 centre = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
        float bottom = centre.y - halfH;

        world = new GameObject("~BossName", typeof(RectTransform), typeof(Canvas));
        var canvas = world.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.sortingOrder = NameCanvasOrder; // over the boss, the ship and its shots
        plateRoot = (RectTransform)world.transform;
        plateRoot.sizeDelta = new Vector2(BossNameShapes.PlateW, BossNameShapes.PlateH);
        scale = halfW * 2f * .95f / BossNameShapes.PlateW;
        plateRoot.localScale = Vector3.one * scale;
        plateRoot.position = new Vector3(centre.x, centre.y + .3f * halfW, 0f);
        slideFrom = halfW + BossNameShapes.PlateW * scale * .5f + .3f;

        var whole = NewGraphic<BossPlateGraphic>("Plate", Vector2.zero, plateRoot.sizeDelta);
        whole.light = tint;
        plate = whole;

        // The two halves the plate splits into, each centred on itself.
        var halves = new Piece[2];
        for (int h = 0; h < 2; h++)
        {
            float from = h == 0 ? 0f : BossNameShapes.CrackX;
            float to = h == 0 ? BossNameShapes.CrackX : BossNameShapes.PlateW;
            var mid = new Vector2((from + to) * .5f, BossNameShapes.PlateH * .5f);
            var size = new Vector2(to - from + 40f, BossNameShapes.PlateH + 20f);
            var g = NewGraphic<BossPlateGraphic>("PlateHalf" + h, BossNameShapes.Local(mid.x, mid.y), size);
            g.light = tint;
            g.side = h == 0 ? -1f : 1f;
            g.origin = mid;
            halves[h] = NewPiece(g, size.magnitude * .5f * scale, false);
        }

        SetName(halves);

        var ck = NewGraphic<BossCrackGraphic>("Crack", Vector2.zero, plateRoot.sizeDelta);
        ck.color = tint;
        crack = ck;

        sparks = new Spark[letterCount * SparksPerLetter];
        for (int i = 0; i < sparks.Length; i++)
        {
            var g = NewGraphic<BossSparkGraphic>("Spark" + i, Vector2.zero, new Vector2(64f, 64f));
            g.color = tint;
            sparks[i] = new Spark { t = g.rectTransform, g = g };
        }

        // Crumble order: outward from the crack, a little shuffled.
        float crackX = BossNameShapes.Local(BossNameShapes.CrackX, 0f).x;
        var order = new float[letterCount];
        for (int i = 0; i < letterCount; i++)
            order[i] = Mathf.Abs(pieces[i].home.x - crackX) + Random.Range(-60f, 60f);
        for (int i = 0; i < letterCount; i++)
        {
            int rank = 0;
            for (int j = 0; j < letterCount; j++)
                if (order[j] < order[i] || (order[j] == order[i] && j < i)) rank++;
            float u = letterCount > 1 ? rank / (float)(letterCount - 1) : 0f;
            pieces[i].releaseAt = BossConfig.NameFallAt + BossConfig.NameStaggerSeconds * u;
        }
        for (int i = letterCount; i < pieces.Length; i++) pieces[i].releaseAt = BossConfig.NameFallAt;

        // One gravity for the whole crumble, strong enough that every piece
        // -- even the highest, kicked hardest -- clears the bottom edge within
        // NameFallSeconds of its release, on any screen height.
        float fall = Mathf.Max(.1f, BossConfig.NameFallSeconds);
        float need = 0f;
        foreach (var p in pieces)
        {
            float t = p.letter ? fall : fall + BossConfig.NameStaggerSeconds;
            float kick = p.letter ? LetterKickUp : HalfKickUp;
            float y0 = plateRoot.position.y + p.home.y * scale;
            float drop = y0 + kick * t - (bottom - p.radius - .1f);
            need = Mathf.Max(need, 2f * drop / (t * t));
        }
        gravity = need;
    }

    // The name as one Text per letter (spaces dropped), laid out with the
    // font's own advances so it reads exactly like the set line, centred on
    // the slab and sized to fit it.
    void SetName(Piece[] halves)
    {
        string name = boss != null ? boss.name : "";
        var font = DockArt.FindSceneFont();
        float avail = BossNameShapes.NameRight - BossNameShapes.NameLeft;
        float size = Mathf.Floor(Mathf.Min(MaxFontSize, avail / Mathf.Max(1f, Advance(font, name, 100) / 100f)));
        int fs = Mathf.Max(8, (int)size);
        float total = Advance(font, name, fs);
        float left = (BossNameShapes.NameLeft + BossNameShapes.NameRight) * .5f - total * .5f;

        int n = 0;
        foreach (char c in name) if (c != ' ') n++;
        letterCount = n;
        letters = name.Replace(" ", "");
        pieces = new Piece[n + halves.Length];
        float pen = left;
        int k = 0;
        float outline = Mathf.Max(2f, fs * .055f);
        foreach (char c in name)
        {
            float adv = Advance(font, c, fs);
            if (c != ' ')
            {
                var home = BossNameShapes.Local(pen + adv * .5f, BossNameShapes.NameMidY);
                var box = new Vector2(adv + fs * .5f, fs * 1.5f);
                var text = NewGraphic<Text>("Letter" + k + "_" + c, home, box);
                text.font = font;
                text.fontSize = fs;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = Bone;
                text.text = c.ToString();
                // A thick ink contour (diagonal + straight), then a drop
                // shadow, then the forward lean.
                var go = text.gameObject;
                AddOutline(go, new Vector2(outline, outline) * .72f);
                AddOutline(go, new Vector2(outline, .01f));
                AddOutline(go, new Vector2(.01f, outline));
                var drop = go.AddComponent<Shadow>();
                drop.effectColor = AkiraPalette.Ink;
                drop.effectDistance = new Vector2(outline * .9f, -outline * 1.2f);
                drop.useGraphicAlpha = false;
                go.AddComponent<BossSkewEffect>();
                // Letters sit over the plate halves in draw order.
                pieces[k] = NewPiece(text, box.magnitude * .5f * scale, true);
                k++;
            }
            pen += adv;
        }
        for (int h = 0; h < halves.Length; h++)
        {
            pieces[n + h] = halves[h];
            halves[h].t.SetSiblingIndex(1 + h); // right after the whole plate
        }
    }

    static void AddOutline(GameObject go, Vector2 d)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = AkiraPalette.Ink;
        o.effectDistance = d;
        o.useGraphicAlpha = false;
    }

    static float Advance(Font font, string s, int size)
    {
        float w = 0f;
        foreach (char c in s) w += Advance(font, c, size);
        return w;
    }

    static float Advance(Font font, char c, int size)
    {
        if (font != null)
        {
            font.RequestCharactersInTexture(c.ToString(), size);
            CharacterInfo info;
            if (font.GetCharacterInfo(c, out info, size) && info.advance > 0) return info.advance;
        }
        return size * (c == ' ' ? .35f : .8f);
    }

    Piece NewPiece(Graphic g, float radius, bool letter)
    {
        return new Piece { t = g.rectTransform, g = g, home = g.rectTransform.anchoredPosition, radius = radius, letter = letter };
    }

    // ---- overlay flashes -----------------------------------------------

    void Flash(Color c, int ticks, float alpha)
    {
        flashColor = new Color(c.r, c.g, c.b, alpha);
        flashLeft = ticks * BossArt.Tick;
    }

    public void HitFlash(Color tint)
    {
        Flash(tint, 2, .35f);
    }

    void Update()
    {
        Step(Mathf.Min(Time.unscaledDeltaTime, .1f));
    }

    // One frame of the intro, dt in real (unscaled) seconds.
    public void Step(float dt)
    {
        clock += dt;

        flashLeft -= dt;
        flash.color = flashLeft > 0f ? flashColor : new Color(1f, 1f, 1f, 0f);

        // A second, smaller flash the moment the boss lands.
        float land = BossConfig.BossArriveAt + BossConfig.BossArriveSeconds * .55f;
        if (!landed && clock >= land) { landed = true; Flash(Bone, 1, .45f); }

        Slide(warning.rectTransform, clock - BossConfig.WarningAt, BossConfig.NameCardAt - BossConfig.WarningAt,
              -1000f, 1000f, 330f);
        // WARNING blinks on threes: 6 ticks on, 2 off.
        int tick = Mathf.FloorToInt((clock - BossConfig.WarningAt) / BossArt.Tick);
        if (warning.sprite != null) warning.enabled = tick < 0 || tick % 8 < 6;

        StepName(dt);
    }

    void StepName(float dt)
    {
        if (plateRoot == null) return;
        float t = clock - BossConfig.NameCardAt;
        bool shown = t >= 0f;

        if (clock < BossConfig.NameBreakAt)
        {
            // Slam in from the right on ticks; the whole name rides the plate.
            Vector3 p = plateRoot.position;
            float baseX = cam != null ? cam.transform.position.x : 0f;
            plateRoot.position = new Vector3(baseX + SlideIn(t, slideFrom), p.y, p.z);
            Show(plate, shown);
            for (int i = 0; i < letterCount; i++) Show(pieces[i].g, shown);
            return;
        }

        if (!cracked)
        {
            cracked = true;
            Vector3 p = plateRoot.position;
            plateRoot.position = new Vector3(cam != null ? cam.transform.position.x : 0f, p.y, p.z);
            Flash(tint, 1, .4f);
            Show(plate, false);
            Show(crack, true);
            for (int i = 0; i < pieces.Length; i++)
            {
                var pc = pieces[i];
                Show(pc.g, true);
                pc.t.anchoredPosition = pc.home;
                pc.t.localRotation = Quaternion.identity;
            }
        }

        // The crack frame: the split shows and the letters shudder on ticks.
        if (clock < BossConfig.NameFallAt)
        {
            int k = Mathf.FloorToInt((clock - BossConfig.NameBreakAt) / BossArt.Tick);
            for (int i = 0; i < letterCount; i++)
            {
                float jx = ((i * 7 + k * 3) % 5 - 2) * .012f;
                float jy = ((i * 5 + k * 11) % 5 - 2) * .012f;
                pieces[i].t.anchoredPosition = pieces[i].home + new Vector2(jx, jy) / scale;
            }
            return;
        }
        Show(crack, false);

        float bottom = CameraFit.ViewBottom;
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p.gone) continue;
            if (!p.falling)
            {
                if (clock < p.releaseAt) { p.t.anchoredPosition = p.home; continue; }
                Release(p);
                if (p.letter) Puff(i, p.pos);
                // Integrate whatever part of this frame came after release.
                Fall(p, clock - p.releaseAt, bottom);
                continue;
            }
            Fall(p, dt, bottom);
        }
        StepSparks(dt);
    }

    void Release(Piece p)
    {
        p.falling = true;
        p.pos = plateRoot.TransformPoint(p.home);
        if (p.letter)
        {
            float out01 = p.home.x * scale / Mathf.Max(.1f, slideFrom);
            p.vel = new Vector2(Random.Range(-.9f, .9f) + out01 * 2.2f, Random.Range(.4f, LetterKickUp));
            p.spin = (Random.value < .5f ? -1f : 1f) * Random.Range(160f, 420f);
        }
        else
        {
            float side = p.home.x < 0f ? -1f : 1f;
            p.vel = new Vector2(side * Random.Range(.4f, .9f), Random.Range(0f, HalfKickUp));
            p.spin = side * Random.Range(30f, 70f);
        }
        p.rot = 0f;
    }

    void Fall(Piece p, float dt, float bottom)
    {
        p.vel.y -= gravity * dt;
        p.pos += p.vel * dt;
        p.rot += p.spin * dt;
        p.t.position = new Vector3(p.pos.x, p.pos.y, plateRoot.position.z);
        p.t.localRotation = Quaternion.Euler(0f, 0f, p.rot);
        if (p.pos.y + p.radius < bottom)
        {
            p.gone = true;
            Show(p.g, false);
        }
    }

    // A small burst of sparks where a letter broke off.
    void Puff(int letter, Vector2 at)
    {
        for (int k = 0; k < SparksPerLetter; k++)
        {
            var s = sparks[letter * SparksPerLetter + k];
            float a = (k * 120f + Random.Range(-35f, 35f)) * Mathf.Deg2Rad;
            float v = Random.Range(1.2f, 2.2f);
            s.pos = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .12f;
            s.vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v;
            s.age = 0f;
            Show(s.g, true);
            s.t.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
        }
    }

    void StepSparks(float dt)
    {
        float life = SparkTicks * BossArt.Tick;
        foreach (var s in sparks)
        {
            if (s.age < 0f) continue;
            s.age += dt;
            if (s.age >= life) { s.age = -1f; Show(s.g, false); continue; }
            s.vel *= Mathf.Max(0f, 1f - 6f * dt);
            s.pos += s.vel * dt;
            // Shrinks on held ticks: 2 at full size, 2 at 70%, 2 at 40%.
            int k = Mathf.FloorToInt(s.age / BossArt.Tick);
            float size = k < 2 ? 1f : k < 4 ? .7f : .4f;
            s.t.position = new Vector3(s.pos.x, s.pos.y, plateRoot.position.z);
            s.t.localScale = Vector3.one * (.6f * size);
        }
    }

    // The plate's x offset slamming in: 4 ticks with an overshoot on the
    // third, then held.
    static float SlideIn(float t, float fromX)
    {
        float tick = BossArt.Tick;
        if (t < 0f) return fromX;
        if (t < tick) return fromX * .55f;
        if (t < 2f * tick) return fromX * .18f;
        if (t < 3f * tick) return -fromX * .04f;   // overshoot
        return 0f;
    }

    // In over 4 ticks (overshoot on the third), held, out over 3 ticks.
    static void Slide(RectTransform rt, float t, float hold, float fromX, float toX, float y)
    {
        float tick = BossArt.Tick;
        float x;
        if (t < 0f) x = fromX;
        else if (t < tick) x = fromX * .55f;
        else if (t < 2f * tick) x = fromX * .18f;
        else if (t < 3f * tick) x = -fromX * .04f;   // overshoot
        else if (t < hold) x = 0f;
        else if (t < hold + tick) x = toX * .12f;
        else if (t < hold + 2f * tick) x = toX * .45f;
        else x = toX;
        rt.anchoredPosition = new Vector2(x, y);
    }

    void OnDestroy()
    {
        if (world != null) BossUtil.Kill(world);
    }

    public void Close()
    {
        BossUtil.Kill(world);
        world = null;
        BossUtil.Kill(gameObject);
    }
}
