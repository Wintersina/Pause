using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The tutorial robot and its speech bubble.
//
// The robot used to be a still sprite with a plain Text box beside it that
// typed one character per tick. It now talks: lines come out a syllable at a
// time, and every syllable drives the robot -- an equalizer mouth on its face
// screen, a pulsing antenna light, a squash-and-bob on stressed beats, glow
// rings radiating off the loud ones and a short pitch-varied chirp (generated
// here with AudioClip.Create, no audio assets). When it is quiet it hovers and
// blinks.
//
// Styled with the Flight Complete family (DeathPanelView): dark space glass,
// cyan neon edges, gold highlights, the scene's Orbitron font. Art is in
// Art/Resources/Tutorial, rasterized from Art/UI/Tutorial/src~.
//
// Everything runs on unscaled time -- the tutorial world sits at timeScale 0
// whenever the player lifts their finger, and the robot must keep talking --
// and nothing allocates per frame: all objects, clips and per-line syllable
// data are built up front.
public class RobotSpeaker : MonoBehaviour, IPointerDownHandler
{
    // ---- Layout (canvas units on an 800x1000, match 0.5 canvas, the same
    //      scaler as PauseQuickActions so the two share units) ----

    public static readonly Vector2 ReferenceResolution = new Vector2(800f, 1000f);
    public const float MatchWidthOrHeight = .5f;

    public const float RobotSize = 128f;
    public const float BubbleHeight = 116f;
    public const float MaxBubbleWidth = 480f;
    public const float MinBubbleWidth = 300f;
    public const float RobotBubbleGap = 14f;   // the tail spans it
    public const float Margin = 22f;           // from the safe area, > hover + pop travel
    public const float BubbleGlow = 12f;       // tut_bubble's bloom outside the body
    public const float TopGap = 10f;           // below whatever is blocking the top
    public const float TextPadX = 22f, TextPadY = 12f;
    public const int FontMax = 28, FontMin = 18;
    // How far the idle hover, squash and pop overshoot can push art past its
    // laid-out rect. Margin must exceed it so nothing leaves the safe area.
    public const float MaxAnimatedOverhang = 8f;

    // ---- Timing (seconds) ----

    public const float SyllableSeconds = .105f;
    public const float StressExtra = .035f;
    public const float FirstSyllableDelay = .22f;
    const float BubbleIn = .32f, BubbleOut = .2f, RobotIn = .45f, LineSwap = .2f;

    static readonly Color Cyan = new Color(.32f, .9f, 1f);
    static readonly Color Gold = new Color(1f, .79f, .26f);
    static readonly Color Ink = new Color(0f, .03f, .12f, .9f);
    static readonly Color TextColor = new Color(.93f, .97f, 1f);

    const string SpriteRoot = "Tutorial/";
    const int Bars = 5, RingCount = 3;

    // ---- Built state ----

    Canvas canvas;
    RectTransform root, robotAnchor, robotBody, bubbleAnchor;
    CanvasGroup bubbleGroup;
    Image antenna, jet;
    RectTransform eyeL, eyeR, antennaRt;
    readonly RectTransform[] bars = new RectTransform[Bars];
    readonly Image[] rings = new Image[RingCount];
    readonly float[] ringBornAt = new float[RingCount];
    Text text;
    SpeechRevealEffect reveal;
    AudioSource voice;
    static AudioClip[] chirps;

    // ---- Speech state ----

    SpokenLine line;
    int spoken;
    float nextSyllableAt;
    float lineDoneAt = -1f;
    float envelope;          // 0..~1.3, kicked by each syllable, decays fast
    char currentVowel = 'a';
    float squash, squashVel;
    int nextRing;

    // ---- Show/hide state ----

    bool robotShown, bubbleShown;
    float robotShownAt = -10f, robotHiddenAt = -10f, bubbleShownAt = -10f, bubbleHiddenAt = -10f, lineSwapAt = -10f;
    float nextBlinkAt, blinkStartedAt = -10f;
    int lastScreenW, lastScreenH;
    float lastScaleFactor;
    Rect lastSafe;
    float topBlocked;

    public float voiceVolume = .45f;

    public RectTransform Root { get { return root; } }
    public RectTransform Bubble { get { return bubbleAnchor; } }
    public RectTransform Robot { get { return robotAnchor; } }
    public bool Talking { get { return line != null && spoken < line.SyllableCount; } }
    public bool LineFinished { get { return line != null && spoken >= line.SyllableCount; } }
    // Unscaled seconds since the current line finished (0 while talking).
    public float SinceLineFinished { get { return LineFinished ? Time.unscaledTime - lineDoneAt : 0f; } }
    public Text Text { get { return text; } }

    // ---------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------

    public static RobotSpeaker Create(Font font)
    {
        var go = new GameObject("RobotComms", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 400;   // over gameplay HUD, under the skip button (500)
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = MatchWidthOrHeight;

        var speaker = go.AddComponent<RobotSpeaker>();
        speaker.canvas = c;
        speaker.Build(font);
        return speaker;
    }

    void Build(Font font)
    {
        root = (RectTransform)transform;

        // Robot: rings and hover jet behind, then the body with its moving face.
        robotAnchor = NewRect("Robot", root);
        robotAnchor.sizeDelta = new Vector2(RobotSize, RobotSize);
        for (int i = 0; i < RingCount; i++)
        {
            rings[i] = NewImage("VoiceRing", robotAnchor, Load("tut_ring"), new Color(Cyan.r, Cyan.g, Cyan.b, 0f));
            Place(rings[i].rectTransform, 0f, 0f, RobotSize, RobotSize);
            ringBornAt[i] = -10f;
        }
        jet = NewImage("HoverJet", robotAnchor, Load("tut_glow"), new Color(Cyan.r, Cyan.g, Cyan.b, .3f));
        Place(jet.rectTransform, 0f, -RobotSize * .5f + 4f, 64f, 26f);

        robotBody = NewRect("Body", robotAnchor);
        robotBody.sizeDelta = new Vector2(RobotSize, RobotSize);
        robotBody.pivot = new Vector2(.5f, .2f);              // squash from near the chin
        robotBody.anchoredPosition = new Vector2(0f, -RobotSize * .3f);
        var shell = NewImage("Shell", robotBody, Load("tut_robot"), Color.white);
        shell.raycastTarget = true;                           // tap the robot to skip ahead
        Stretch(shell.rectTransform);

        // Face-screen coordinates from tut_robot.svg (y down from the top).
        antenna = NewImage("Antenna", robotBody, Load("tut_glow"), Gold);
        antennaRt = antenna.rectTransform;
        PlaceSvg(antennaRt, 64f, 9f, 22f, 22f);
        eyeL = NewImage("EyeL", robotBody, Load("tut_eye"), Cyan).rectTransform;
        eyeR = NewImage("EyeR", robotBody, Load("tut_eye"), Cyan).rectTransform;
        PlaceSvg(eyeL, 50f, 58f, 20f, 24f);
        PlaceSvg(eyeR, 78f, 58f, 20f, 24f);
        var barSprite = Load("tut_bar");
        for (int i = 0; i < Bars; i++)
        {
            var bar = NewImage("MouthBar", robotBody, barSprite, Cyan);
            bar.type = Image.Type.Sliced;
            PlaceSvg(bar.rectTransform, 64f + (i - 2) * 9f, 80f, 6f, 4f);
            bars[i] = bar.rectTransform;
        }

        // Bubble: pivot on its left edge so it grows out of the tail.
        bubbleAnchor = NewRect("Bubble", root);
        bubbleAnchor.pivot = new Vector2(0f, .5f);
        bubbleGroup = bubbleAnchor.gameObject.AddComponent<CanvasGroup>();
        bubbleGroup.alpha = 0f;
        bubbleGroup.blocksRaycasts = false;

        var frame = NewImage("Frame", bubbleAnchor, Load("tut_bubble"), Color.white);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;                           // tap the bubble to finish the line
        frame.rectTransform.anchorMin = Vector2.zero;
        frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.offsetMin = new Vector2(-BubbleGlow, -BubbleGlow);
        frame.rectTransform.offsetMax = new Vector2(BubbleGlow, BubbleGlow);

        var tail = NewImage("Tail", bubbleAnchor, Load("tut_tail"), Color.white);
        var tailRt = tail.rectTransform;
        tailRt.anchorMin = tailRt.anchorMax = new Vector2(0f, .5f);
        tailRt.pivot = new Vector2(21f / 26f, .5f);           // x=21 of the art sits on the body edge
        tailRt.sizeDelta = new Vector2(26f, 20f);
        tailRt.anchoredPosition = Vector2.zero;

        text = new GameObject("Line", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(bubbleAnchor, false);
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontStyle = FontStyle.Bold;
        text.fontSize = FontMax;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = FontMin;
        text.resizeTextMaxSize = FontMax;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        // Best fit only shrinks text that would otherwise be cut off, so the
        // box has to truncate; every line fits well above FontMin (tested),
        // so nothing is ever actually dropped.
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.lineSpacing = 1.05f;
        text.supportRichText = true;
        text.color = TextColor;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(TextPadX, TextPadY);
        text.rectTransform.offsetMax = new Vector2(-TextPadX, -TextPadY);
        reveal = text.gameObject.AddComponent<SpeechRevealEffect>();
        var outline = text.gameObject.AddComponent<Outline>();   // after the reveal, so it copies its alpha
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        voice.loop = false;
        if (chirps == null) chirps = BuildChirps();

        robotAnchor.localScale = Vector3.zero;
        nextBlinkAt = Time.unscaledTime + 1.5f;
        Fit(true);
    }

    // ---------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------

    public void ShowRobot()
    {
        if (robotShown) return;
        robotShown = true;
        robotShownAt = Time.unscaledTime;
    }

    // Starts speaking a line. Pops the bubble in if it is hidden, otherwise
    // gives it a small punch as the new line replaces the old.
    public void Say(SpokenLine next)
    {
        ShowRobot();
        float now = Time.unscaledTime;
        line = next;
        spoken = 0;
        lineDoneAt = -1f;
        text.text = next.richText;
        reveal.HideAll();
        if (!bubbleShown)
        {
            bubbleShown = true;
            bubbleShownAt = now;
            bubbleGroup.blocksRaycasts = true;
            nextSyllableAt = now + FirstSyllableDelay + .08f;
        }
        else
        {
            lineSwapAt = now;
            nextSyllableAt = now + FirstSyllableDelay;
        }
    }

    // Reveals the rest of the line at once (a tap while it is being spoken).
    public void CompleteLine()
    {
        if (!Talking) return;
        float now = Time.unscaledTime;
        spoken = line.SyllableCount;
        reveal.Reveal(line.totalGlyphs, now);
        lineDoneAt = now;
        Kick(true, now);
    }

    public void HideBubble()
    {
        if (!bubbleShown) return;
        bubbleShown = false;
        bubbleHiddenAt = Time.unscaledTime;
        bubbleGroup.blocksRaycasts = false;
        if (Talking) CompleteLine();
    }

    public void HideAll()
    {
        HideBubble();
        if (!robotShown) return;
        robotShown = false;
        robotHiddenAt = Time.unscaledTime;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (Talking) CompleteLine();
    }

    // ---------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------

    void Update()
    {
        float now = Time.unscaledTime;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        Fit(false);

        // Speak.
        if (line != null && bubbleShown && spoken < line.SyllableCount && now >= nextSyllableAt)
        {
            bool stressed = line.stressed[spoken];
            currentVowel = line.vowel[spoken];
            reveal.Reveal(line.glyphsVisible[spoken], now);
            Chirp(spoken, stressed);
            Kick(stressed, now);
            nextSyllableAt = now + SyllableSeconds + (stressed ? StressExtra : 0f) + line.pauseAfter[spoken];
            spoken++;
            if (spoken >= line.SyllableCount) lineDoneAt = now;
        }
        reveal.Tick(now);

        envelope *= Mathf.Exp(-dt * 13f);
        // Squash spring.
        float acc = -320f * squash - 16f * squashVel;
        squashVel += acc * dt;
        squash += squashVel * dt;

        AnimateRobot(now);
        AnimateFace(now);
        AnimateRings(now);
        AnimateBubble(now);
    }

    void Kick(bool stressed, float now)
    {
        envelope = stressed ? 1.3f : 1f;
        squashVel += stressed ? 7f : 2.6f;
        if (stressed)
        {
            ringBornAt[nextRing] = now;
            nextRing = (nextRing + 1) % RingCount;
        }
    }

    void AnimateRobot(float now)
    {
        float scale;
        if (robotShown) scale = Mathf.LerpUnclamped(0f, 1f, EaseOutBack(Mathf.Clamp01((now - robotShownAt) / RobotIn)));
        else scale = 1f - EaseInCubic(Mathf.Clamp01((now - robotHiddenAt) / BubbleOut));
        robotAnchor.localScale = Vector3.one * (Mathf.Max(0f, scale) * fitScale);

        // Idle hover and sway; talking adds a lift on every beat and a little
        // nodding tilt.
        float hover = 4f * Mathf.Sin(now * 2.2f) + 3f * Mathf.Min(envelope, 1f);
        float tilt = 2.5f * Mathf.Sin(now * 1.4f) + 3.5f * Mathf.Sin(now * 9f) * Mathf.Min(envelope, 1f);
        robotBody.anchoredPosition = new Vector2(0f, -RobotSize * .3f + hover);
        robotBody.localRotation = Quaternion.Euler(0f, 0f, tilt);
        float s = Mathf.Clamp(squash, -.6f, .6f) * .09f;
        robotBody.localScale = new Vector3(1f + s, 1f - s, 1f);

        // Hover jet flickers, flaring as the robot bobs down.
        SetAlpha(jet, .22f + .08f * Mathf.Sin(now * 23f) + .06f * Mathf.Sin(now * 2.2f + Mathf.PI));
    }

    void AnimateFace(float now)
    {
        float e = Mathf.Min(envelope, 1.3f);
        bool silent = !Talking && e < .05f;

        // Equalizer mouth: bar shape by vowel, height by the envelope, a fast
        // wobble on top so held beats shimmer. Flat dashes when silent.
        for (int i = 0; i < Bars; i++)
        {
            float shape = VowelShape(currentVowel, i);
            float wobble = .14f * Mathf.Sin(now * 31f + i * 1.7f);
            float h = 4f + 20f * e * Mathf.Clamp01(shape + wobble);
            if (silent) h = 4f + .8f * Mathf.Sin(now * 2.4f + i * .9f);
            var size = bars[i].sizeDelta;
            if (!Mathf.Approximately(size.y, h)) bars[i].sizeDelta = new Vector2(size.x, h);
        }

        // Eyes: blink every few seconds, squint a touch on loud beats.
        if (now >= nextBlinkAt)
        {
            blinkStartedAt = now;
            nextBlinkAt = now + Random.Range(2.2f, 4.4f);
        }
        float blink = Mathf.Clamp01((now - blinkStartedAt) / .16f);
        float lid = blink < 1f ? 1f - .9f * Mathf.Sin(blink * Mathf.PI) : 1f;
        float eyeY = lid * (1f - .14f * Mathf.Clamp01(e - .9f) * 2f);
        var eyeScale = new Vector3(1f, Mathf.Max(.1f, eyeY), 1f);
        eyeL.localScale = eyeScale;
        eyeR.localScale = eyeScale;

        // Antenna light: slow idle throb, flashes with the voice.
        float idle = .35f + .15f * Mathf.Sin(now * 3f);
        SetAlpha(antenna, Mathf.Clamp01(idle + .65f * e));
        antennaRt.localScale = Vector3.one * (1f + .5f * Mathf.Min(e, 1f));
    }

    void AnimateRings(float now)
    {
        const float life = .6f;
        for (int i = 0; i < RingCount; i++)
        {
            float p = (now - ringBornAt[i]) / life;
            var ring = rings[i];
            if (p < 0f || p >= 1f)
            {
                SetAlpha(ring, 0f);
                continue;
            }
            float e = EaseOutCubic(p);
            ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(.85f, 1.9f, e);
            SetAlpha(ring, .55f * (1f - p) * (1f - p));
        }
    }

    void AnimateBubble(float now)
    {
        float scale, alpha;
        if (bubbleShown)
        {
            float p = Mathf.Clamp01((now - bubbleShownAt) / BubbleIn);
            scale = Mathf.LerpUnclamped(.55f, 1f, EaseOutBack(p));
            alpha = EaseOutCubic(p / .6f);
            // New line on an open bubble: a quick punch.
            float q = Mathf.Clamp01((now - lineSwapAt) / LineSwap);
            if (q < 1f) scale *= Mathf.LerpUnclamped(.94f, 1f, EaseOutBack(q));
        }
        else
        {
            float p = Mathf.Clamp01((now - bubbleHiddenAt) / BubbleOut);
            scale = Mathf.Lerp(1f, .7f, EaseInCubic(p));
            alpha = 1f - EaseOutCubic(p);
        }
        bubbleAnchor.localScale = Vector3.one * (scale * fitScale);
        if (!Mathf.Approximately(bubbleGroup.alpha, alpha)) bubbleGroup.alpha = alpha;
    }

    // ---------------------------------------------------------------------
    // Voice
    // ---------------------------------------------------------------------

    void Chirp(int index, bool stressed)
    {
        if (voice == null || chirps == null) return;
        int clip = VowelClip(currentVowel);
        float pitch = VowelPitch(currentVowel) * (stressed ? 1.1f : 1f) * Random.Range(.96f, 1.04f);
        // Statements settle on the last beat, exclamations jump up.
        if (index == line.SyllableCount - 1)
            pitch *= line.plainText[line.plainText.Length - 1] == '!' ? 1.12f : .9f;
        voice.pitch = pitch;
        voice.PlayOneShot(chirps[clip], voiceVolume * (stressed ? 1f : .8f));
    }

    static int VowelClip(char v)
    {
        switch (v)
        {
            case 'i': case 'y': case 'e': return 0;   // bright
            case 'a': return 1;                       // open
            default: return 2;                        // round (o, u)
        }
    }

    static float VowelPitch(char v)
    {
        switch (v)
        {
            case 'i': return 1.26f;
            case 'y': return 1.18f;
            case 'e': return 1.12f;
            case 'a': return 1f;
            case 'o': return .9f;
            default: return .84f;
        }
    }

    static float VowelShape(char v, int bar)
    {
        // Five bars, centre-heavy for open vowels, flat and wide for e/i,
        // narrow for o/u.
        int d = Mathf.Abs(bar - 2);
        switch (v)
        {
            case 'i': case 'y': case 'e': return d == 0 ? .75f : d == 1 ? .85f : .6f;
            case 'o': case 'u': return d == 0 ? 1f : d == 1 ? .6f : .2f;
            default: return d == 0 ? 1f : d == 1 ? .8f : .45f;
        }
    }

    // Three short blips (~80 ms), one per vowel colour: a sine/square blend
    // with a quick downward glide and a soft attack so it reads as a voice,
    // not a beep. Built once per session.
    public static AudioClip[] BuildChirps()
    {
        const int rate = 22050;
        const float seconds = .08f;
        int count = Mathf.RoundToInt(rate * seconds);
        var data = new float[count];
        var clips = new AudioClip[3];
        float[] baseHz = { 760f, 560f, 430f };
        float[] squareMix = { .22f, .35f, .18f };
        for (int c = 0; c < clips.Length; c++)
        {
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float k = t / seconds;
                float hz = baseHz[c] * (1.08f - .2f * k);
                phase += hz / rate;
                float p = (float)(phase - System.Math.Floor(phase));
                float sine = Mathf.Sin(p * Mathf.PI * 2f);
                float square = p < .5f ? 1f : -1f;
                // A second, quieter partial a fifth up gives it a vowel-ish body.
                float fifth = Mathf.Sin(p * Mathf.PI * 3f) * .25f;
                float wave = sine * (1f - squareMix[c]) + square * squareMix[c] + fifth;
                float attack = Mathf.Clamp01(t / .006f);
                float release = Mathf.Clamp01((seconds - t) / .02f);
                data[i] = wave * attack * release * Mathf.Exp(-k * 1.6f) * .38f;
            }
            clips[c] = AudioClip.Create("RobotChirp" + c, count, 1, rate, false);
            clips[c].SetData(data, 0);
        }
        return clips;
    }

    // ---------------------------------------------------------------------
    // Layout and the safe area
    // ---------------------------------------------------------------------

    float fitScale = 1f;

    // Anything above this many canvas units from the top of the safe area is
    // taken (the HUD panel, the skip button). Set by the director.
    public void SetTopBlocked(float units)
    {
        if (Mathf.Approximately(units, topBlocked)) return;
        topBlocked = units;
        Fit(true);
    }

    void Fit(bool force)
    {
        if (canvas == null) return;
        if (!force && Screen.width == lastScreenW && Screen.height == lastScreenH && Screen.safeArea == lastSafe
            && Mathf.Approximately(canvas.scaleFactor, lastScaleFactor)) return;
        lastScreenW = Screen.width;
        lastScreenH = Screen.height;
        lastSafe = Screen.safeArea;
        lastScaleFactor = canvas.scaleFactor;

        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        var rootRect = root.rect;
        if (rootRect.width <= 0f) rootRect = new Rect(0f, 0f, Screen.width / sf, Screen.height / sf);
        Rect safe = Screen.safeArea;
        var safeUnits = new Rect(safe.x / sf - rootRect.width * .5f, safe.y / sf - rootRect.height * .5f,
                                 safe.width / sf, safe.height / sf);

        Rect robot, bubble;
        ComputeLayout(safeUnits, topBlocked, out robot, out bubble, out fitScale);
        robotAnchor.anchoredPosition = robot.center;
        bubbleAnchor.anchoredPosition = new Vector2(bubble.xMin, bubble.center.y);
        bubbleAnchor.sizeDelta = bubble.size / fitScale;
    }

    // Pure, so it can be tested at any aspect ratio. `safe` is the safe area
    // in canvas units (centre origin); `topBlocked` the band under its top
    // edge that is already in use. The robot sits at the left, the bubble to
    // its right, the pair as wide as the screen allows (up to
    // MaxBubbleWidth), scaled down as a unit only when even MinBubbleWidth
    // will not fit. Returned rects are the bodies, already scaled; the
    // bubble's bloom adds BubbleGlow * scale around it.
    public static void ComputeLayout(Rect safe, float topBlocked, out Rect robot, out Rect bubble, out float scale)
    {
        float inner = safe.width - 2f * (Margin + BubbleGlow);
        float bubbleWidth = Mathf.Min(MaxBubbleWidth, inner - RobotSize - RobotBubbleGap);
        scale = 1f;
        if (bubbleWidth < MinBubbleWidth)
        {
            scale = Mathf.Max(.2f, inner / (RobotSize + RobotBubbleGap + MinBubbleWidth));
            bubbleWidth = MinBubbleWidth;
        }
        float rowHeight = Mathf.Max(RobotSize, BubbleHeight + 2f * BubbleGlow) * scale;
        float top = safe.yMax - Mathf.Max(0f, topBlocked) - TopGap;
        // Never pushed below the safe area on a very short screen.
        top = Mathf.Max(top, safe.yMin + Margin + rowHeight);
        top = Mathf.Min(top, safe.yMax - Margin);
        float centreY = top - rowHeight * .5f;

        float left = safe.xMin + Margin + BubbleGlow * scale;
        robot = new Rect(left, centreY - RobotSize * scale * .5f, RobotSize * scale, RobotSize * scale);
        float bx = robot.xMax + RobotBubbleGap * scale;
        bubble = new Rect(bx, centreY - BubbleHeight * scale * .5f, bubbleWidth * scale, BubbleHeight * scale);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static Sprite Load(string name) { return Resources.Load<Sprite>(SpriteRoot + name); }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // Places a child of the 128-unit robot body by tut_robot.svg coordinates
    // (origin top-left, y down).
    static void PlaceSvg(RectTransform rt, float x, float y, float w, float h)
    {
        Place(rt, x - RobotSize * .5f, RobotSize * .5f - y, w, h);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void SetAlpha(Graphic g, float a)
    {
        var c = g.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        g.color = c;
    }

    static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); float i = 1f - x; return 1f - i * i * i; }
    static float EaseInCubic(float x) { x = Mathf.Clamp01(x); return x * x * x; }
    static float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
}
