using System.IO;
using UnityEngine;

// The kill cam (KillCam, KillCamRecorder, KillCamStage, KillCamClip) and its
// codex slot (KillCamArchive): the slow-motion clock, the tape (frames, the
// ring dropping the oldest, a GC-free capture), drawing a clip between two
// frames, the live replay hiding the board and handing it back untouched
// (no zoom: the camera's size never changes), the crash held meanwhile, and
// the saved high-score death (policy, one slot that the latest replaces,
// the file round trip, versions and corrupt files). Edit mode, no scene.
public static class KillCamTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[KCM] PASS  " : "[KCM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var dir = Path.Combine(Path.GetTempPath(), "PauseKillCamTest");
        KillCamArchive.DirectoryOverride = dir;
        var made = new System.Collections.Generic.List<Object>();
        try
        {
            KillCamArchive.Clear();
            Clock();
            Clips();
            Tape(made);
            Replay(made);
            Archive(made);
        }
        finally
        {
            KillCamArchive.Clear();
            KillCamArchive.DirectoryOverride = null;
            KillCamRecorder.RequireVisible = true;
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            if (KillCam.Current != null) Object.DestroyImmediate(KillCam.Current.gameObject);
            if (KillCamRecorder.Instance != null) Object.DestroyImmediate(KillCamRecorder.Instance.gameObject);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (System.Exception) { }
        }
        return fails;
    }

    // ---- the slow-motion clock ----
    static void Clock()
    {
        Check("very slow at the hit, livelier before it",
              Mathf.Approximately(KillCamClip.RateAt(0f), KillCamClip.SlowRate) &&
              Mathf.Approximately(KillCamClip.RateAt(KillCamClip.RampSeconds), KillCamClip.LeadRate) &&
              Mathf.Approximately(KillCamClip.RateAt(5f), KillCamClip.LeadRate));
        bool slowing = true;
        for (float t = 0f; t < 1f; t += .05f) slowing &= KillCamClip.RateAt(t) <= KillCamClip.RateAt(t + .05f) + 1e-5f;
        Check("the clock only slows toward the hit", slowing);
        Check("every rate is slow motion", KillCamClip.LeadRate < 1f && KillCamClip.SlowRate <= .25f);
        float real = KillCamClip.RealSeconds(KillCamClip.LiveWindow);
        Check("the live replay is a few seconds of real time (" + real.ToString("F2") + " s)", real > 2.5f && real < 4.5f);
        Check("a tap can skip it early", KillCam.SkipGuard < .5f);
    }

    // ---- the clip: frames by time, tracks between frames ----
    static KillCamClip MakeClip(Sprite sprite, int frames, float dt, float speed)
    {
        var clip = new KillCamClip(frames, frames * 2, frames);
        for (int f = 0; f < frames; f++)
        {
            clip.time[f] = f * dt;
            clip.cam[f] = new Vector2(0f, 0f);
            clip.first[f] = f * 2;
            clip.count[f] = 2;
            clip.meshFirst[f] = f;
            clip.meshCount[f] = 0;
            // track 7 moves right; track 9 (flipped, rotated 90) sits still
            clip.entries[f * 2] = new KillCamEntry { track = 7, sprite = sprite, pos = new Vector3(f * dt * speed, 1f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255), order = 5 };
            clip.entries[f * 2 + 1] = new KillCamEntry { track = 9, sprite = sprite, pos = new Vector3(-1f, -1f, 0f), m00 = 0f, m01 = -2f, m10 = 2f, m11 = 0f, color = new Color32(255, 0, 0, 128), order = 3, flags = KillCamEntry.FlipX };
        }
        clip.frameCount = frames;
        return clip;
    }

    static void Clips()
    {
        var clip = MakeClip(null, 5, .1f, 1f);
        float a;
        Check("a time between frames finds the earlier one and the blend", clip.FrameAt(.25f, out a) == 2 && Mathf.Abs(a - .5f) < 1e-4f);
        Check("before the start / past the end clamp", clip.FrameAt(-1f, out a) == 0 && clip.FrameAt(9f, out a) == 4 && a == 0f);
        int cursor = 0;
        Check("a track is found in the next frame", clip.Match(3, 9, ref cursor) == 1 && clip.Match(3, 42, ref cursor) == -1);
        Check("a playable clip has a duration", clip.Playable && Mathf.Abs(clip.Duration - .4f) < 1e-4f);
        Check("a rail offset blends the short way round its wrap",
              Mathf.Abs(KillCamStage.LerpWrapped(new Vector2(0f, .9f), new Vector2(0f, .1f), .5f).y - 1f) < 1e-4f);
    }

    static Sprite MakeSprite(System.Collections.Generic.List<Object> made, Color colour, int size = 8)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = colour;
        tex.SetPixels(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        made.Add(tex);
        made.Add(sprite);
        return sprite;
    }

    // ---- the tape ----
    static void Tape(System.Collections.Generic.List<Object> made)
    {
        KillCamRecorder.RequireVisible = false;
        var rec = KillCamRecorder.Ensure();
        made.Add(rec.gameObject);
        rec.Clear();
        var sprite = MakeSprite(made, Color.green);
        var go = new GameObject("~KillCamTestMover", typeof(SpriteRenderer));
        made.Add(go);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 12;
        sr.flipY = true;
        var hiddenGo = new GameObject("~KillCamTestOff", typeof(SpriteRenderer));
        made.Add(hiddenGo);
        hiddenGo.GetComponent<SpriteRenderer>().sprite = sprite;
        hiddenGo.GetComponent<SpriteRenderer>().enabled = false;

        for (int f = 0; f < 30; f++)
        {
            go.transform.position = new Vector3(f * .1f, 2f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, f * 3f);
            rec.Capture(f / 60f);
        }
        var clip = rec.Clip(10f);
        Check("the tape keeps every frame and its clip starts at 0", rec.Frames == 30 && clip != null && clip.frameCount == 30 && clip.time[0] == 0f);
        int track = sr.GetInstanceID();
        bool found = false, off = false;
        for (int i = 0; i < clip.count[29]; i++)
        {
            var e = clip.Entry(29, i);
            if (e.track == track)
                found = e.sprite == sprite && Mathf.Abs(e.pos.x - 2.9f) < 1e-4f && e.order == 12 && (e.flags & KillCamEntry.FlipY) != 0;
            if (e.track == hiddenGo.GetComponent<SpriteRenderer>().GetInstanceID()) off = true;
        }
        Check("a sprite is recorded with its sprite, position, order and flips", found);
        Check("a switched-off sprite is not recorded", !off);
        var window = rec.Clip(.1f);
        Check("a window keeps only the last seconds", window != null && window.frameCount <= 8 && window.frameCount >= 6);

        for (int f = 30; f < KillCamRecorder.FrameCap + 40; f++) rec.Capture(f / 60f);
        Check("a full tape drops its oldest frames", rec.Frames == KillCamRecorder.FrameCap);

        // the capture allocates nothing once warm (60 fps all run long)
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        float at = 100f;
        long used = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 120; i++) rec.Capture(at += 1f / 60f); });
        Check("recording allocates nothing per frame: " + used + " bytes (meter control " + control + ")", meter && used == 0);

        rec.Clear();
        Check("a new run starts an empty tape", rec.Frames == 0 && rec.Clip(1f) == null);
    }

    // ---- drawing and the live replay ----
    static void Replay(System.Collections.Generic.List<Object> made)
    {
        var sprite = MakeSprite(made, Color.white);
        var clip = MakeClip(sprite, 20, .1f, 1f);
        var holder = new GameObject("~KillCamTestStage");
        made.Add(holder);
        var stage = new KillCamStage(holder.transform, "Stage");
        stage.Draw(clip, .25f);
        var a = stage.SpriteAt(0);
        var b = stage.SpriteAt(1);
        Check("a drawn frame shows each entry", stage.Shown == 2 && a.enabled && b.enabled && a.sprite == sprite);
        Check("between frames a mover is blended", Mathf.Abs(a.transform.position.x - .25f) < 1e-3f && a.sortingOrder == 5);
        Check("the 2x2 matrix becomes rotation and scale",
              Mathf.Abs(Mathf.DeltaAngle(b.transform.eulerAngles.z, 90f)) < .1f && Mathf.Abs(b.transform.localScale.x - 2f) < 1e-3f &&
              Mathf.Abs(b.transform.localScale.y - 2f) < 1e-3f && b.flipX && b.color.a < .51f);
        stage.Hide();
        Check("hiding switches the pool off", stage.Shown == 0 && !a.enabled);

        // the live kill cam over a 'board': its renderer hidden, then given back
        var board = new GameObject("~KillCamTestBoard", typeof(SpriteRenderer));
        made.Add(board);
        var boardSr = board.GetComponent<SpriteRenderer>();
        boardSr.sprite = sprite;
        var camGo = new GameObject("~KillCamTestCamera", typeof(Camera));
        made.Add(camGo);
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6.6f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        for (int f = 0; f < clip.frameCount; f++) clip.cam[f] = new Vector2(.3f, 0f);   // a shake on the tape

        var main = Camera.main;
        Vector3 mainAt = main != null ? main.transform.position : Vector3.zero;
        float mainSize = main != null ? main.orthographicSize : 0f;
        var kc = KillCam.Ensure();
        made.Add(kc.gameObject);
        kc.Begin(clip);
        Check("the replay plays and the crash is held", KillCam.Playing && DeathCrash.Held);
        Check("the board is hidden under the replay", boardSr.forceRenderingOff && kc.HiddenCount >= 1);
        Check("the camera takes the tape's position, never its size (no zoom)",
              main == null || (Mathf.Abs(main.transform.position.x - .3f) < 1e-3f && Mathf.Approximately(main.orthographicSize, mainSize)));
        float real = 0f;
        float lastClock = -1f;
        bool forward = true;
        while (KillCam.Playing && real < 20f)
        {
            kc.Step(1f / 60f);
            real += 1f / 60f;
            forward &= kc.ClipTime >= lastClock;
            lastClock = kc.ClipTime;
        }
        float want = KillCamClip.RealSeconds(clip.Duration);
        Check("the replay runs forward to the hit and ends by itself (" + real.ToString("F2") + " s, about " + want.ToString("F2") + ")",
              !KillCam.Playing && forward && Mathf.Abs(real - want) < .2f);
        Check("the board, the camera and the crash are given back", !boardSr.forceRenderingOff && !DeathCrash.Held &&
              (main == null || (main.transform.position == mainAt && Mathf.Approximately(main.orthographicSize, mainSize))) &&
              kc.Stage.Shown == 0);

        kc.Begin(clip);
        KillCam.Stop();
        Check("a stop (a revive) ends it at once and restores", !KillCam.Playing && !boardSr.forceRenderingOff);
        KillCam.Enabled = false;
        Check("switched off, nothing begins", !KillCam.TryBegin());
        KillCam.Enabled = true;
        Check("edit mode never begins one from the tape", !KillCam.TryBegin());
    }

    // ---- the codex slot ----
    static void Archive(System.Collections.Generic.List<Object> made)
    {
        Check("only a new best on a ranked run is kept",
              KillCamArchive.ShouldSave(true, true) && !KillCamArchive.ShouldSave(true, false) && !KillCamArchive.ShouldSave(false, true));

        var r = new KillCamArchive.Record { score = 12345, utcTicks = 638000000000000000L, ship = 3, world = "Fr|ost", killer = "enemy_frost_alien" };
        var parsed = KillCamArchive.Record.Parse(r.Format());
        Check("a record round-trips (a stray separator cleaned)", parsed != null && parsed.score == 12345 && parsed.ship == 3 && parsed.world == "Fr/ost" && parsed.killer == r.killer);
        Check("an old save has no record, and another version reads as none",
              KillCamArchive.Latest == null && !KillCamArchive.HasRecord && KillCamArchive.Record.Parse("9|1|2|3|a|b") == null &&
              KillCamArchive.Record.Parse("garbage") == null);
        Check("the codex says how to earn one", KillCamArchive.Describe().StartsWith("No last stand"));

        // the file round trip, by hand (no GPU): a 4x4 atlas, two frames
        var p = new KillCamArchive.Packed { meta = r, camHalfHeight = 6.6f, camAspect = .5f, camZ = -10f, background = new Color32(1, 2, 3, 255) };
        p.meta = KillCamArchive.Record.Parse(r.Format());
        p.atlasW = p.atlasH = 4;
        p.atlasRgba = new byte[4 * 4 * 4];
        for (int i = 0; i < p.atlasRgba.Length; i += 4) { p.atlasRgba[i] = 200; p.atlasRgba[i + 3] = 255; }
        p.rects.Add(new RectInt(0, 0, 4, 4)); p.pivots.Add(new Vector2(.5f, .5f)); p.ppu.Add(4f); p.borders.Add(Vector4.zero);
        p.frames = 2;
        p.time = new[] { 1f, 1.05f };
        p.cam = new[] { new Vector2(0f, 0f), new Vector2(0f, .5f) };
        p.counts = new[] { 1, 1 };
        p.meshCounts = new[] { 0, 0 };
        p.entries.Add(new KillCamArchive.PEntry { track = 0, sprite = 0, material = -1, order = 4, pos = new Vector3(1.5f, 2f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255) });
        p.entries.Add(new KillCamArchive.PEntry { track = 0, sprite = 0, material = -1, order = 4, pos = new Vector3(1.75f, 2f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255) });
        var bytes = KillCamArchive.Serialize(p);
        var back = KillCamArchive.Deserialize(bytes);
        Check("the file round-trips", back != null && back.frames == 2 && back.entries.Count == 2 && back.meta.SameAs(p.meta) &&
              Mathf.Abs(back.entries[1].pos.x - 1.75f) < .01f && back.background.b == 3);
        var loaded = KillCamArchive.Build(back);
        Check("a loaded clip starts at 0, its entries back in world space, its sprite from the atlas",
              loaded != null && loaded.clip.frameCount == 2 && loaded.clip.time[0] == 0f && Mathf.Abs(loaded.clip.time[1] - .05f) < 1e-4f &&
              Mathf.Abs(loaded.clip.Entry(1, 0).pos.y - 2.5f) < .01f && loaded.clip.Entry(0, 0).sprite != null);
        if (loaded != null) loaded.Dispose();
        var wrong = (byte[])bytes.Clone();
        wrong[4] = 99;
        Check("another version or a corrupt file reads as nothing, never throws",
              KillCamArchive.Deserialize(wrong) == null && KillCamArchive.Deserialize(new byte[] { 1, 2, 3 }) == null &&
              KillCamArchive.Deserialize(null) == null);

        // the real save: a clip of two sprites through the GPU copy, one slot the latest replaces
        var red = MakeSprite(made, Color.red, 16);
        var blue = MakeSprite(made, Color.blue, 16);
        var clip = MakeClip(red, 40, 1f / 60f, 2f);
        for (int f = 0; f < clip.frameCount; f++) { var e = clip.entries[f * 2 + 1]; e.sprite = blue; clip.entries[f * 2 + 1] = e; }
        bool saved = KillCamArchive.Save(clip, r, false);
        Check("a high-score death is kept (record + file)", saved && KillCamArchive.HasRecord && KillCamArchive.Latest.score == 12345 && File.Exists(KillCamArchive.FilePath));
        var first = KillCamArchive.Load();
        bool colours = false;
        if (first != null)
        {
            var s = first.clip.Entry(0, 0).sprite;
            if (s != null)
            {
                var c = s.texture.GetPixel((int)s.rect.center.x, (int)s.rect.center.y);
                colours = c.r > .8f && c.b < .2f;
            }
        }
        Check("it loads: frames thinned to the store rate, the sprites' own colours",
              first != null && first.clip.frameCount >= 15 && first.clip.frameCount <= 25 && colours);
        if (first != null) first.Dispose();
        long size = File.Exists(KillCamArchive.FilePath) ? new FileInfo(KillCamArchive.FilePath).Length : 0;
        Check("the file is small (" + size + " bytes)", size > 0 && size < 64 * 1024);

        var r2 = new KillCamArchive.Record { score = 20000, utcTicks = r.utcTicks + 1, ship = 1, world = "Space", killer = "" };
        KillCamArchive.Save(clip, r2, false);
        var second = KillCamArchive.Load();
        Check("the next new best replaces it: one slot, the latest", KillCamArchive.Latest.score == 20000 && second != null && second.meta.score == 20000);
        if (second != null) second.Dispose();
        Check("the codex describes the latest", KillCamArchive.Describe().Contains("20,000") || KillCamArchive.Describe().Contains("20000") || KillCamArchive.Describe().Contains("20"));

        PlayerPrefs.SetString(KillCamArchive.PrefsKey, r.Format());
        Check("a file that does not match its record is not trusted", KillCamArchive.Load() == null);
        KillCamArchive.Clear();
        Check("cleared, there is no record", !KillCamArchive.HasRecord && KillCamArchive.Load() == null);
    }
}
