using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The HapticGate splash (spashS7) must fill the whole screen on any phone:
// the black backdrop covers the camera's view edge to edge (the app renders
// into the cutout, so the view is the whole panel), and the mark sits centred
// in the safe area, uncropped, at its own proportions and a fixed share of the
// shorter screen side. The mark's art itself is protected, so its file must be
// byte-identical to the one this was written against.
//
// Regression: CameraFit grows the orthographic size on tall screens, and the
// backdrop was a fixed 7.02 x 11.05 quad, so on a 1080x2520 Z Flip the view
// (13.3 units tall) showed the camera's blue clear colour above and below it.
public static class SplashLayoutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPLASH] PASS  " : "[SPLASH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // SHA-256 of Assets/Art/UI/Splash/HapticGate.png on master (protected art).
    const string LogoSha256 = "73531a210c4b0c8fbedc6effae9391e3f88ff1346aac642b190d5f5545225c69";

    struct Case
    {
        public string name; public int w, h; public Rect safe;
        public Case(string n, int w, int h, Rect safe) { name = n; this.w = w; this.h = h; this.safe = safe; }
        public Case(string n, int w, int h) : this(n, w, h, new Rect(0, 0, w, h)) { }
    }

    static readonly Case[] Cases =
    {
        new Case("9:16 1080x1920", 1080, 1920),
        new Case("9:19.5 1080x2340", 1080, 2340),
        new Case("9:19.5 1080x2340 punch-hole (top 110px unsafe)", 1080, 2340, new Rect(0, 0, 1080, 2230)),
        new Case("9:20 1080x2400", 1080, 2400),
        new Case("9:20 1080x2400 notch + gesture bar", 1080, 2400, new Rect(0, 63, 1080, 2400 - 63 - 136)),
        new Case("9:21 1080x2520 Z Flip", 1080, 2520),
        new Case("9:21 1080x2520 Z Flip cutout (top 118px)", 1080, 2520, new Rect(0, 0, 1080, 2402)),
        new Case("~9:22 1440x3088", 1440, 3088),
        new Case("Z Fold cover 968x2376", 968, 2376),
        new Case("Z Fold cover 968x2376 cutout (top 90px)", 968, 2376, new Rect(0, 0, 968, 2286)),
        new Case("9:22 1080x2640", 1080, 2640),
        new Case("9:24 1080x2880", 1080, 2880),
        new Case("9:24 1080x2880 notch + gesture bar", 1080, 2880, new Rect(0, 63, 1080, 2880 - 63 - 136)),
        new Case("3:4 tablet 1536x2048", 1536, 2048),
        new Case("square-ish flip cover 948x1048", 948, 1048),
        new Case("16:9 landscape 1920x1080", 1920, 1080),
        new Case("16:9 landscape side cutout", 1920, 1080, new Rect(110, 0, 1920 - 220, 1080)),
        new Case("Mac window 1100x800", 1100, 800),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        LogoUntouched();
        PureLayout();
        SceneLayout();
        PlayerSettingsGuard();

        Debug.Log("[SPLASH] failures: " + fails);
        return fails;
    }

    static void LogoUntouched()
    {
        string path = Path.Combine(Application.dataPath, "Art/UI/Splash/HapticGate.png");
        string sha;
        using (var h = SHA256.Create())
            sha = BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        Check("HapticGate.png is byte-identical to master (" + sha.Substring(0, 12) + ")", sha == LogoSha256);
    }

    static void PureLayout()
    {
        var native = new Vector2(5.18f, 0.87f);
        // The authored look survives on the authored phone: 9:16 at size 5
        // keeps scale 0.7 and an unscaled, unshifted word canvas.
        var l = splashScene.Compute(5f, 1080, 1920, new Rect(0, 0, 1080, 1920), native, 0.7f * 5.18f / 5.625f);
        Check("authored 9:16: word canvas scale 1 (" + l.wordsScaleFactor.ToString("F3") + ")",
              Mathf.Abs(l.wordsScaleFactor - 1f) < 0.01f);
        Check("authored 9:16: logo scale 0.7", Mathf.Abs(l.logoScale - 0.7f) < 0.01f);

        // Degenerate screen falls back to something drawable.
        var d = splashScene.Compute(5f, 0, 0, Rect.zero, native);
        Check("zero screen size still lays out", d.logoScale > 0f && d.backdropScale.x > 0f);
        // Bogus (empty) safe area means the whole screen.
        var e = splashScene.Compute(6f, 1080, 2340, Rect.zero, native);
        Check("empty safe area treated as whole screen", Mathf.Abs(e.safeWorld.height - 12f) < 0.01f);
    }

    static void SceneLayout()
    {
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        var card = UnityEngine.Object.FindFirstObjectByType<splashScene>();
        Check("splash card component found", card != null);
        if (card == null) return;
        var cam = card.GetComponentInParent<Camera>();
        Check("splash camera found", cam != null && cam.orthographic);
        Check("logo wired", card.logo != null && card.logo.sprite != null);
        Check("word canvas wired", card.wordsScaler != null && card.words != null && card.words.Length == 2);
        Check("camera clears to black, so even a gap would not read as a band",
              cam != null && cam.clearFlags == CameraClearFlags.SolidColor
              && cam.backgroundColor.r < 0.01f && cam.backgroundColor.g < 0.01f && cam.backgroundColor.b < 0.01f);
        if (cam == null || card.logo == null) return;

        float baseSize = cam.orthographicSize;
        var backdrop = card.GetComponent<Renderer>();
        Vector2 native = splashScene.NativeSize(card.logo);
        float nativeAspect = native.x / native.y;
        Vector2[] authoredWords = new Vector2[card.words.Length];
        for (int i = 0; i < card.words.Length; i++) authoredWords[i] = card.words[i].anchoredPosition;

        foreach (var c in Cases)
        {
            cam.orthographicSize = CameraFit.ComputeSize(baseSize, 2.85f, c.w, c.h);
            var l = card.ApplyLayout(c.w, c.h, c.safe);

            float aspect = (float)c.w / c.h;
            float viewH = 2f * cam.orthographicSize, viewW = viewH * aspect;
            Vector2 camXY = cam.transform.position;
            var view = new Rect(camXY.x - viewW / 2f, camXY.y - viewH / 2f, viewW, viewH);

            Bounds b = backdrop.bounds;
            bool covers = b.min.x <= view.xMin && b.max.x >= view.xMax && b.min.y <= view.yMin && b.max.y >= view.yMax;
            Check(c.name + ": backdrop covers the whole screen (view " + viewW.ToString("F2") + "x" + viewH.ToString("F2")
                  + ", backdrop " + b.size.x.ToString("F2") + "x" + b.size.y.ToString("F2") + ")", covers);
            bool inFront = b.center.z > cam.transform.position.z + cam.nearClipPlane
                           && b.center.z < cam.transform.position.z + cam.farClipPlane;
            Check(c.name + ": backdrop inside the clip range", inFront);

            Bounds lb = card.logo.bounds;
            Rect safe = new Rect(view.xMin + c.safe.x / c.w * viewW, view.yMin + c.safe.y / c.h * viewH,
                                 c.safe.width / c.w * viewW, c.safe.height / c.h * viewH);
            const float eps = 0.002f;
            bool inside = lb.min.x >= safe.xMin - eps && lb.max.x <= safe.xMax + eps
                       && lb.min.y >= safe.yMin - eps && lb.max.y <= safe.yMax + eps;
            Check(c.name + ": logo fully inside the safe area", inside);
            Vector2 off = (Vector2)lb.center - safe.center;
            Check(c.name + ": logo centred in the safe area (off " + off.ToString("F3") + ")", off.magnitude < 0.01f);
            float aspectNow = lb.size.x / lb.size.y;
            Check(c.name + ": logo keeps its proportions", Mathf.Abs(aspectNow / nativeAspect - 1f) < 0.001f);
            var ls = card.logo.transform.lossyScale;
            Check(c.name + ": logo scaled uniformly", Mathf.Abs(ls.x - ls.y) < 0.0001f);
            Check(c.name + ": logo sits in front of the backdrop", lb.center.z < b.center.z);

            // Size: a consistent share of the shorter side unless the safe
            // area forces it smaller; never tiny.
            float shortSide = Mathf.Min(viewW, viewH);
            float share = lb.size.x / shortSide;
            bool safeBound = lb.size.x >= splashScene.SafeFill * safe.width - 0.01f
                          || lb.size.y >= splashScene.SafeFill * safe.height - 0.01f;
            Check(c.name + ": logo is " + (share * 100f).ToString("F1") + "% of the shorter side",
                  Mathf.Abs(share - splashScene.LogoFraction) < 0.005f || (safeBound && share > 0.3f));

            // Words ride with the mark: their offset from the logo, measured in
            // logo widths, is the same on every screen.
            for (int i = 0; i < card.words.Length; i++)
            {
                Vector2 rel = (card.words[i].anchoredPosition - l.wordsShift) * l.wordsScaleFactor / l.pixelsPerUnit / lb.size.x;
                Vector2 relAuthored = authoredWords[i] / splashScene.AuthoredPixelsPerUnit / (native.x * splashScene.AuthoredLogoScale);
                Check(c.name + ": word '" + card.words[i].name + "' keeps its place against the mark",
                      (rel - relAuthored).magnitude < 0.002f
                      && (card.words[i].anchoredPosition - authoredWords[i] - l.wordsShift).magnitude < 0.01f);
            }
            Check(c.name + ": word canvas scale follows the mark",
                  Mathf.Abs(card.wordsScaler.scaleFactor - l.wordsScaleFactor) < 0.0001f && l.wordsScaleFactor > 0.2f);
        }
        cam.orthographicSize = baseSize;
    }

    // Global settings the full-bleed card depends on. Changing them would
    // bring the bars back on cutout or tall phones in every scene.
    static void PlayerSettingsGuard()
    {
        Check("Android renders outside the safe area (into the cutout)",
              PlayerSettings.Android.renderOutsideSafeArea);
        string asset = File.ReadAllText(Path.Combine(Application.dataPath, "../ProjectSettings/ProjectSettings.asset"));
        // Aspect Ratio Mode: 0 Legacy Wide Screen (1.86 cap), 1 Native Aspect
        // Ratio (no cap: no android:maxAspectRatio in the manifest, any screen
        // shape full screen -- 9:24 phones, the Z Fold cover at ~2.45-2.56),
        // 2 Custom (capped at androidMaxAspectRatio, the old 2.4 letterboxed
        // the Fold cover).
        Check("Android aspect ratio is uncapped (Native Aspect Ratio, not Legacy or a Custom cap)",
              asset.Contains("androidSupportedAspectRatio: 1"));
        Check("Android activity is resizeable (the system sizes it to any screen, no compat letterbox)",
              PlayerSettings.Android.resizeableActivity);
        Check("Android starts fullscreen", asset.Contains("androidStartInFullscreen: 1"));
        Check("Android fullscreen mode is FullScreenWindow",
              PlayerSettings.Android.fullscreenMode == FullScreenMode.FullScreenWindow);
        // iOS: a launch screen (Unity's default storyboard) is what makes iOS
        // run at the device's native size instead of a letterboxed legacy one.
        Check("iOS requires full screen", PlayerSettings.iOS.requiresFullScreen);
        Check("iOS has a launch screen (type not None)", !asset.Contains("iOSLaunchScreenType: 3"));
        Check("spashS7 is the first scene in the build",
              EditorBuildSettings.scenes.Length > 0 && EditorBuildSettings.scenes[0].path.EndsWith("spashS7.unity"));
    }

    // Offscreen renders of the card for eyeballing:
    //   Unity -batchmode -projectPath <abs> -executeMethod SplashLayoutTest.Render -splashOut <dir>
    // Renders each case through the scene camera into a texture (no player).
    // Only the world-space card (backdrop + mark) is captured: batch mode never
    // builds the uGUI batches for the "A" / "Game" words, which the layout
    // checks above cover numerically instead.
    // "-splashRaw" renders the authored transforms without the runtime layout.
    public static void Render()
    {
        string outDir = Arg("-splashOut") ?? Path.Combine(Application.dataPath, "../Temp/splash");
        bool raw = Environment.GetCommandLineArgs().Length > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-splashRaw") >= 0;
        Directory.CreateDirectory(outDir);
        int[,] sizes = { { 1080, 1920 }, { 1080, 2340 }, { 1080, 2520 }, { 1536, 2048 }, { 1920, 1080 } };
        for (int i = 0; i < sizes.GetLength(0); i++)
        {
            int w = sizes[i, 0], h = sizes[i, 1];
            EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
            var card = UnityEngine.Object.FindFirstObjectByType<splashScene>();
            var cam = card.GetComponentInParent<Camera>();
            cam.orthographicSize = CameraFit.ComputeSize(cam.orthographicSize, 2.85f, w, h);
            if (!raw) card.ApplyLayout(w, h, new Rect(0, 0, w, h));
            // master's clear colour, to show what the device showed
            if (raw) cam.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 1f);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            var canvas = card.wordsScaler.GetComponent<Canvas>();
            float sf = raw ? 1f : card.wordsScaler.scaleFactor;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            canvas.scaleFactor = sf;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string file = Path.Combine(outDir, (raw ? "before_" : "after_") + w + "x" + h + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Debug.Log("[SPLASH] render " + file);
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        EditorApplication.Exit(0);
    }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
