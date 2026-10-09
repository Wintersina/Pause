using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The tutorial robot and its speech bubble.
//
// The robot used to be a still sprite with a plain Text box beside it that
// typed one character per tick. It now talks: lines come out a syllable at a
// time, and every syllable drives the robot -- a mouth frame picked from the
// syllable's vowel, a flash of the red antenna lamp, a jolt on stressed beats
// with a hard amber octagon pulse stepping off it, and a short pitch-varied chirp
// (generated here with AudioClip.Create, no audio assets). When it is quiet
// it hovers and blinks.
//
// Art direction: a painted rustic steampunk robot (brass, rust, amber nixie
// eyes and mouth, red antenna lamp) talking from a brass-framed navy panel
// (TutorialPalette holds the matching text/tint colours). Every moving part
// is a separate sprite frame in Art/Resources/Tutorial and the motion is limited
// animation: poses are held for whole steps of 1/12 s ("on 2s") or 1/8 s
// ("on 3s") and snap between them, cartoon style, rather than easing.
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

    public const float RobotArt = 128f;        // tut_robot.png's canvas, in its own units
    public const float RobotSize = 150f;       // what it occupies on screen
    const float ArtScale = RobotSize / RobotArt;
    public const float BubbleHeight = 116f;
    public const float MaxBubbleWidth = 480f;
    public const float MinBubbleWidth = 300f;
    public const float RobotBubbleGap = 14f;   // the tail spans it
    public const float TailReach = 19f;        // tail tip, left of the bubble body
    public const float Margin = 22f;           // from the safe area, > hover + pop travel
    public const float BubbleMargin = 4f;      // tut_bubble's transparent rim (8 px at 200 ppu) outside the brass frame
    public const float TopGap = 10f;           // below whatever is blocking the top
    public const float TextPadX = 22f, TextPadY = 12f;
    public const int FontMax = 28, FontMin = 18;
    // How far the hover, jolt and pop overshoot can push art past its
    // laid-out rect. Margin must exceed it so nothing leaves the safe area.
    public const float MaxAnimatedOverhang = 12f;

    // ---- Timing (seconds) ----

    public const float Step = 1f / 12f;        // "on 2s" at 24 fps
    public const float SlowStep = 1f / 8f;     // "on 3s"
    public const float SyllableSeconds = .105f;
    public const float StressExtra = .035f;
    public const float FirstSyllableDelay = .22f;

    const string SpriteRoot = "Tutorial/";
    const int RingCount = 3;
    const float RingAlpha = .55f;   // the painted ring is a thick cream octagon; keep the pulse a hint

    // Mouth frames, by index.
    const int MouthRest = 0, MouthE = 1, MouthA = 2, MouthO = 3, MouthBig = 4;
    static readonly string[] MouthNames = { "rest", "e", "a", "o", "big" };
    // Eye frames, by index.
    const int EyeOpen = 0, EyeHalf = 1, EyeShut = 2, EyeHappy = 3;
    static readonly string[] EyeNames = { "open", "half", "shut", "happy" };

    // ---- Built state ----

    Canvas canvas;
    RectTransform root, robotAnchor, robotBody, bubbleAnchor;
    CanvasGroup bubbleGroup;
    Image eyeL, eyeR, mouth, jet, lampFlash;
    readonly Sprite[] mouthFrames = new Sprite[5];
    readonly Sprite[] eyeFrames = new Sprite[4];
    readonly Sprite[] jetFrames = new Sprite[2];
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
    int mouthFrame = MouthRest;
    float mouthHeldUntil;
    float beatAt = -10f;          // last syllable
    bool beatStressed;
    float happyUntil = -10f;
    int nextRing;

    // ---- Show/hide state ----

    bool robotShown, bubbleShown;
    float robotShownAt = -10f, robotHiddenAt = -10f, bubbleShownAt = -10f, bubbleHiddenAt = -10f, lineSwapAt = -10f;
    float nextBlinkAt, blinkStartedAt = -10f;
    int lastScreenW, lastScreenH;
    float lastScaleFactor;
    Rect lastSafe;
    float topBlocked;
    float fitScale = 1f;

    public float voiceVolume = .45f;

    public RectTransform Root { get { return root; } }
    public RectTransform Bubble { get { return bubbleAnchor; } }
    public RectTransform Robot { get { return robotAnchor; } }
    public bool Talking { get { return line != null && spoken < line.SyllableCount; } }
    public bool LineFinished { get { return line != null && spoken >= line.SyllableCount; } }
    // Unscaled seconds since the current line finished (0 while talking).
    public float SinceLineFinished { get { return LineFinished ? Time.unscaledTime - lineDoneAt : 0f; } }
    public Text Text { get { return text; } }
    public int MouthFrame { get { return mouthFrame; } }

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
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = MatchWidthOrHeight;
        UiScaleFloor.Configure(scaler, ReferenceResolution, 0f, FontMin);   // its type never under UiScale.MinTextPt

        var speaker = go.AddComponent<RobotSpeaker>();
        speaker.canvas = c;
        speaker.Build(font);
        return speaker;
    }

    void Build(Font font)
    {
        root = (RectTransform)transform;
        for (int i = 0; i < mouthFrames.Length; i++) mouthFrames[i] = Load("tut_mouth_" + MouthNames[i]);
        for (int i = 0; i < eyeFrames.Length; i++) eyeFrames[i] = Load("tut_eye_" + EyeNames[i]);
        jetFrames[0] = Load("tut_jet_a");
        jetFrames[1] = Load("tut_jet_b");

        // Robot: pulses and the hover jet behind, then the body and its face.
        robotAnchor = NewRect("Robot", root);
        robotAnchor.sizeDelta = new Vector2(RobotSize, RobotSize);
        for (int i = 0; i < RingCount; i++)
        {
            rings[i] = NewImage("VoicePulse", robotAnchor, Load("tut_ring"), TutorialPalette.Orange);
            Place(rings[i].rectTransform, 0f, 0f, RobotSize, RobotSize);
            rings[i].enabled = false;
            ringBornAt[i] = -10f;
        }

        // The body is laid out in the art's own 128 units and scaled up.
        robotBody = NewRect("Body", robotAnchor);
        robotBody.sizeDelta = new Vector2(RobotArt, RobotArt);
        robotBody.pivot = new Vector2(.5f, .2f);              // squash from near the chin
        robotBody.anchoredPosition = new Vector2(0f, -RobotSize * .3f);
        robotBody.localScale = Vector3.one * ArtScale;

        jet = NewImage("HoverJet", robotBody, jetFrames[0], Color.white);
        PlaceSvg(jet.rectTransform, 64f, 108f + 13f - 2f, 30f, 26f);
        var shell = NewImage("Shell", robotBody, Load("tut_robot"), Color.white);
        shell.raycastTarget = true;                           // tap the robot to skip ahead
        Stretch(shell.rectTransform);

        // Face parts at tut_robot.png coordinates (128 art units) (y down from the top).
        lampFlash = NewImage("LampFlash", robotBody, Load("tut_glow"), TutorialPalette.Red);
        PlaceSvg(lampFlash.rectTransform, 77f, 9f, 26f, 26f);
        lampFlash.enabled = false;
        var lamp = NewImage("Lamp", robotBody, Load("tut_lamp"), Color.white);
        PlaceSvg(lamp.rectTransform, 77f, 9f, 12f, 12f);
        eyeL = NewImage("EyeL", robotBody, eyeFrames[EyeOpen], Color.white);
        eyeR = NewImage("EyeR", robotBody, eyeFrames[EyeOpen], Color.white);
        PlaceSvg(eyeL.rectTransform, 47f, 57f, 26f, 20f);
        PlaceSvg(eyeR.rectTransform, 81f, 57f, 26f, 20f);
        eyeR.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // same frames, mirrored
        mouth = NewImage("Mouth", robotBody, mouthFrames[MouthRest], Color.white);
        PlaceSvg(mouth.rectTransform, 64f, 79f, 36f, 20f);

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
        frame.rectTransform.offsetMin = new Vector2(-BubbleMargin, -BubbleMargin);
        frame.rectTransform.offsetMax = new Vector2(BubbleMargin, BubbleMargin);

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
        text.color = TutorialPalette.Paper;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(TextPadX, TextPadY);
        text.rectTransform.offsetMax = new Vector2(-TextPadX, -TextPadY);
        reveal = text.gameObject.AddComponent<SpeechRevealEffect>();
        var outline = text.gameObject.AddComponent<Outline>();   // after the reveal, so it copies its alpha
        outline.effectColor = TutorialPalette.Ink;
        outline.effectDistance = new Vector2(2f, -2f);

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
            nextSyllableAt = now + FirstSyllableDelay + Step;
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
        Beat(true, MouthBig, now);
        FinishLine(now);
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
    // Animation: limited, held poses that snap on whole steps
    // ---------------------------------------------------------------------

    void Update()
    {
        float now = Time.unscaledTime;
        Fit(false);

        // Speak.
        if (line != null && bubbleShown && spoken < line.SyllableCount && now >= nextSyllableAt)
        {
            bool stressed = line.stressed[spoken];
            char vowel = line.vowel[spoken];
            reveal.Reveal(line.glyphsVisible[spoken], now);
            Chirp(spoken, vowel, stressed);
            Beat(stressed, stressed ? MouthBig : VowelMouth(vowel), now);
            nextSyllableAt = now + SyllableSeconds + (stressed ? StressExtra : 0f) + line.pauseAfter[spoken];
            spoken++;
            if (spoken >= line.SyllableCount)
            {
                lineDoneAt = now;
                FinishLine(now);
            }
        }
        reveal.Tick(now);

        AnimateRobot(now);
        AnimateFace(now);
        AnimateRings(now);
        AnimateBubble(now);
    }

    void Beat(bool stressed, int mouthShape, float now)
    {
        beatAt = now;
        beatStressed = stressed;
        mouthFrame = mouthShape;
        mouthHeldUntil = now + Step * (stressed ? 3f : 2f);
        if (stressed)
        {
            ringBornAt[nextRing] = now;
            nextRing = (nextRing + 1) % RingCount;
        }
    }

    void FinishLine(float now)
    {
        if (line.plainText[line.plainText.Length - 1] == '!') happyUntil = now + .7f;
    }

    // Whole steps elapsed since `t` (0 on the step it happened).
    static int StepsSince(float now, float t, float step) { return Mathf.FloorToInt((now - t) / step); }

    void AnimateRobot(float now)
    {
        // Pop in/out: held poses that overshoot, not an eased tween.
        float scale;
        if (robotShown)
        {
            int k = StepsSince(now, robotShownAt, Step);
            scale = k <= 0 ? .5f : k == 1 ? 1.12f : k == 2 ? .96f : 1f;
        }
        else
        {
            int k = StepsSince(now, robotHiddenAt, Step);
            scale = k <= 0 ? 1.08f : k == 1 ? .6f : 0f;
        }
        SetScale(robotAnchor, scale * fitScale, scale * fitScale);

        // Idle hover on 3s, in whole units; a jolt on every beat, bigger and
        // with a tilt and squash on stressed ones.
        float slow = Mathf.Floor(now / SlowStep) * SlowStep;
        float hover = Mathf.Round(3f * Mathf.Sin(slow * 2.4f));
        float y = 0f, tilt = 0f, sx = 1f, sy = 1f;
        int b = StepsSince(now, beatAt, Step);
        if (beatStressed)
        {
            if (b == 0) { y = 6f; tilt = -5f; sx = .94f; sy = 1.08f; }
            else if (b == 1) { y = 3f; tilt = -2f; sx = 1.06f; sy = .95f; }
            else if (b == 2) { y = -1f; }
        }
        else if (b == 0) y = 2f;
        robotBody.anchoredPosition = new Vector2(0f, -RobotSize * .3f + hover + y);
        robotBody.localRotation = Quaternion.Euler(0f, 0f, tilt);
        SetScale(robotBody, sx * ArtScale, sy * ArtScale);

        // Two-frame jet flicker on 2s.
        jet.sprite = jetFrames[(Mathf.FloorToInt(now / Step) & 1)];
    }

    void AnimateFace(float now)
    {
        // Mouth: the beat's shape for two or three steps, then shut.
        int m = now < mouthHeldUntil ? mouthFrame : MouthRest;
        if (mouth.sprite != mouthFrames[m]) mouth.sprite = mouthFrames[m];

        // Eyes: frame-by-frame blink (half, shut, half), happy after an
        // exclamation.
        if (now >= nextBlinkAt)
        {
            blinkStartedAt = now;
            nextBlinkAt = now + Random.Range(2.2f, 4.4f);
        }
        int k = StepsSince(now, blinkStartedAt, Step);
        int eye = k == 0 || k == 2 ? EyeHalf : k == 1 ? EyeShut : EyeOpen;
        if (now < happyUntil && eye == EyeOpen) eye = EyeHappy;
        var eyeSprite = eyeFrames[eye];
        if (eyeL.sprite != eyeSprite) { eyeL.sprite = eyeSprite; eyeR.sprite = eyeSprite; }

        // Antenna lamp: hard flash on each beat, a slow indicator blink idle.
        bool flash = StepsSince(now, beatAt, Step) <= (beatStressed ? 1 : 0)
                     || (!Talking && Mathf.Repeat(now, 1.2f) < Step * 2f);
        if (lampFlash.enabled != flash) lampFlash.enabled = flash;
    }

    void AnimateRings(float now)
    {
        // A hard octagon pulse stepping out on 3s: three held sizes, then gone.
        for (int i = 0; i < RingCount; i++)
        {
            int k = StepsSince(now, ringBornAt[i], SlowStep);
            var ring = rings[i];
            bool on = k >= 0 && k < 3;
            if (ring.enabled != on) ring.enabled = on;
            if (!on) continue;
            float s = k == 0 ? 1f : k == 1 ? 1.2f : 1.4f;
            SetScale(ring.rectTransform, s, s);
            SetAlpha(ring, RingAlpha * (k == 0 ? 1f : k == 1 ? .65f : .3f));
        }
    }

    void AnimateBubble(float now)
    {
        float scale, alpha;
        if (bubbleShown)
        {
            int k = StepsSince(now, bubbleShownAt, Step);
            scale = k <= 0 ? .75f : k == 1 ? 1.08f : 1f;
            alpha = 1f;
            // New line on an open bubble: one held punch.
            if (StepsSince(now, lineSwapAt, Step) == 0) scale *= .95f;
        }
        else
        {
            int k = StepsSince(now, bubbleHiddenAt, Step);
            scale = k <= 0 ? .9f : .65f;
            alpha = k <= 0 ? 1f : k == 1 ? .5f : 0f;
        }
        SetScale(bubbleAnchor, scale * fitScale, scale * fitScale);
        if (!Mathf.Approximately(bubbleGroup.alpha, alpha)) bubbleGroup.alpha = alpha;
    }

    static int VowelMouth(char v)
    {
        switch (v)
        {
            case 'i': case 'y': case 'e': return MouthE;
            case 'o': case 'u': return MouthO;
            default: return MouthA;
        }
    }

    // ---------------------------------------------------------------------
    // Voice
    // ---------------------------------------------------------------------

    void Chirp(int index, char vowel, bool stressed)
    {
        if (voice == null || chirps == null) return;
        int clip = VowelClip(vowel);
        float pitch = VowelPitch(vowel) * (stressed ? 1.1f : 1f) * Random.Range(.96f, 1.04f);
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
        if (!force && ScreenInfo.Width == lastScreenW && ScreenInfo.Height == lastScreenH && ScreenInfo.SafeArea == lastSafe
            && Mathf.Approximately(canvas.scaleFactor, lastScaleFactor)) return;
        lastScreenW = ScreenInfo.Width;
        lastScreenH = ScreenInfo.Height;
        lastSafe = ScreenInfo.SafeArea;
        lastScaleFactor = canvas.scaleFactor;

        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        var rootRect = root.rect;
        if (rootRect.width <= 0f) rootRect = new Rect(0f, 0f, ScreenInfo.Width / sf, ScreenInfo.Height / sf);
        Rect safe = ScreenInfo.SafeArea;
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
    // bubble's shadow adds BubbleMargin * scale around it.
    public static void ComputeLayout(Rect safe, float topBlocked, out Rect robot, out Rect bubble, out float scale)
    {
        float inner = safe.width - 2f * (Margin + BubbleMargin);
        float bubbleWidth = Mathf.Min(MaxBubbleWidth, inner - RobotSize - RobotBubbleGap);
        scale = 1f;
        if (bubbleWidth < MinBubbleWidth)
        {
            scale = Mathf.Max(.2f, inner / (RobotSize + RobotBubbleGap + MinBubbleWidth));
            bubbleWidth = MinBubbleWidth;
        }
        float rowHeight = Mathf.Max(RobotSize, BubbleHeight + 2f * BubbleMargin) * scale;
        float top = safe.yMax - Mathf.Max(0f, topBlocked) - TopGap;
        // Never pushed below the safe area on a very short screen.
        top = Mathf.Max(top, safe.yMin + Margin + rowHeight);
        top = Mathf.Min(top, safe.yMax - Margin);
        float centreY = top - rowHeight * .5f;

        float left = safe.xMin + Margin + BubbleMargin * scale;
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

    // Places a child of the robot body by tut_robot.png coordinates (origin
    // top-left, y down, in the art's 128 units).
    static void PlaceSvg(RectTransform rt, float x, float y, float w, float h)
    {
        Place(rt, x - RobotArt * .5f, RobotArt * .5f - y, w, h);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void SetScale(Transform t, float x, float y)
    {
        var s = t.localScale;
        if (Mathf.Approximately(s.x, x) && Mathf.Approximately(s.y, y)) return;
        t.localScale = new Vector3(x, y, 1f);
    }

    static void SetAlpha(Graphic g, float a)
    {
        var c = g.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        g.color = c;
    }
}
