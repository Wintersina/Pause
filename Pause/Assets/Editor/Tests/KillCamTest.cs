using System.IO;
using UnityEngine;

// The kill cam (KillCam, KillCamRecorder, KillCamStage, KillCamClip) and its
// codex slot (KillCamArchive): the slow-motion clock, the tape (frames, the
// ring dropping the oldest, a GC-free capture), drawing a clip between two
// frames, the live replay hiding the board and handing it back untouched
// (no zoom: the camera's size never changes), the crash held meanwhile, and
// the saved high-score death (policy, one slot that the latest replaces,
// the file round trip, versions and corrupt files, a sprite whose shader
// works in its own texture space kept as it looked), the death on the tape
// (the collision frame, the crash after it, the replay slowing into the hit
// and playing on through it), and the saved clip's fidelity (sprites copied at
// the size they are seen, box-filtered, their edges extruded; every dot of a
// full-height telegraph recorded). Edit mode, no scene.
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
            Death(made);
            Contact(made);
            Busy(made);
            Replay(made);
            Blend(made);
            Archive(made);
            Fidelity(made);
            RecordOrder(made);
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
        Check("the live replay is a few seconds of real time (" + real.ToString("F2") + " s)", real > 2.2f && real < 3.8f && Mathf.Approximately(KillCamClip.LiveWindow, 1.3f));
        Check("a tap can skip it early", KillCam.SkipGuard < .5f);
        Check("after the hit the clock eases back up through the death",
              Mathf.Approximately(KillCamClip.RateAt(-.0001f), KillCamClip.SlowRate) &&
              Mathf.Approximately(KillCamClip.RateAt(-KillCamClip.TailRampSeconds), KillCamClip.TailRate) &&
              KillCamClip.RateAt(-.2f) > KillCamClip.SlowRate && KillCamClip.RateAt(-.2f) < KillCamClip.TailRate && KillCamClip.TailRate < 1f);
        float whole = KillCamClip.RealSeconds(KillCamClip.LiveWindow, KillCamClip.DeathTail);
        Check("the replay with the death is still short (" + whole.ToString("F2") + " s) and inside the safety net",
              whole > real + .8f && whole + KillCam.IntroSeconds < KillCam.MaxReal - 1f && whole < 6.5f);
        float cm3 = KillCam.LabelDropPixels(320f, 1920f, 100f);
        Check("the REPLAY label drops ~3 cm by the screen dpi (" + cm3.ToString("F0") + " px at 320 dpi)", Mathf.Abs(cm3 - 3f / 2.54f * 320f) < .5f);
        Check("a dpi of 0 falls back to a tenth of the screen height", Mathf.Abs(KillCam.LabelDropPixels(0f, 1920f, 100f) - 192f) < .5f);
        Check("the label never ends up lower than 40% of the screen",
              KillCam.LabelDropPixels(700f, 1000f, 100f) <= 300.01f && KillCam.LabelDropPixels(320f, 1920f, 100f) + 100f <= 1920f * .4f);
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
        KillCamAudio.ClearCache();   // count from the very first request (Begin fires the KILLED cue)
        kc.Begin(clip);
        Check("the replay plays and the crash is held", KillCam.Playing && DeathCrash.Held);
        Check("the board is hidden under the replay", boardSr.forceRenderingOff && kc.HiddenCount >= 1);
        Check("the camera takes the tape's position, never its size (no zoom)",
              main == null || (Mathf.Abs(main.transform.position.x - .3f) < 1e-3f && Mathf.Approximately(main.orthographicSize, mainSize)));
        Check("it begins on the frozen fatal frame under the KILLED banner",
              kc.CurrentPhase == KillCam.Phase.Killed && kc.BannerShown && Mathf.Approximately(kc.ClipTime, clip.EndTime) && !kc.LabelShown &&
              kc.Banner.Text.text == "KILLED" && KillCamAudio.LastRequested == KillCamAudio.Cue.Killed);
        float real = 0f;
        float lastClock = -1f;
        bool forward = true, backward = true, sawRewind = false, sawLabelInRewind = false, killedHeld = true, fxSeen = false;
        float rewindPrev = clip.EndTime + 1f, phaseStart = 0f, killedEnd = -1f, rewindEnd = -1f;
        while (KillCam.Playing && real < 20f)
        {
            kc.Step(1f / 60f);
            real += 1f / 60f;
            switch (kc.CurrentPhase)
            {
                case KillCam.Phase.Killed:
                    killedHeld &= Mathf.Approximately(kc.ClipTime, clip.EndTime);
                    break;
                case KillCam.Phase.Rewind:
                    sawRewind = true;
                    backward &= kc.ClipTime <= rewindPrev + 1e-4f;
                    rewindPrev = kc.ClipTime;
                    sawLabelInRewind |= kc.LabelShown;
                    fxSeen |= kc.FxAlpha > 0f;
                    if (killedEnd < 0f) killedEnd = real;
                    break;
                case KillCam.Phase.Play:
                    if (rewindEnd < 0f) { rewindEnd = real; phaseStart = kc.ClipTime; }
                    forward &= kc.ClipTime >= lastClock;
                    lastClock = kc.ClipTime;
                    break;
            }
        }
        Check("KILLED holds the fatal frame, then the tape rewinds backwards under the label and the rewind look",
              killedHeld && sawRewind && backward && sawLabelInRewind && fxSeen);
        Check("the intro is modest (" + (rewindEnd).ToString("F2") + " s) and the replay then starts from the beginning",
              Mathf.Abs(rewindEnd - KillCam.IntroSeconds) < .1f && KillCam.IntroSeconds <= 1.5f && phaseStart < .2f);
        Check("the sound hooks fired in order and a missing clip is silent (no placeholder)",
              KillCamAudio.Requested == 4 && KillCamAudio.Played == 0 && KillCamAudio.LastRequested == KillCamAudio.Cue.ReplayEnd);
        Check("the song's reverse is undone on finish", !KillCamMusic.Reversing && !KillCamMusic.MusicPaused);
        float want = KillCamClip.RealSeconds(clip.Duration) + KillCam.IntroSeconds;
        Check("the replay runs forward to the hit and ends by itself (" + real.ToString("F2") + " s, about " + want.ToString("F2") + ")",
              !KillCam.Playing && forward && Mathf.Abs(real - want) < .2f);
        Check("the board, the camera and the crash are given back", !boardSr.forceRenderingOff && !DeathCrash.Held &&
              (main == null || (main.transform.position == mainAt && Mathf.Approximately(main.orthographicSize, mainSize))) &&
              kc.Stage.Shown == 0);

        kc.Begin(clip);
        KillCam.Stop();
        Check("a stop (a revive) ends it at once and restores", !KillCam.Playing && !boardSr.forceRenderingOff);
        KillCam.Intro = false;
        kc.Begin(clip);
        Check("without the intro the replay starts at once from the beginning, REPLAY label up",
              kc.CurrentPhase == KillCam.Phase.Play && kc.ClipTime == 0f && !kc.BannerShown);
        KillCam.Stop();
        KillCam.Intro = true;
        kc.Begin(clip);
        kc.Step(.1f);
        KillCam.Stop();
        Check("a stop during KILLED restores everything and hides the banner",
              !KillCam.Playing && !kc.BannerShown && !boardSr.forceRenderingOff && !KillCamMusic.Reversing);
        MusicTap();
        KillCam.Enabled = false;
        Check("switched off, nothing begins", !KillCam.TryBegin());
        KillCam.Enabled = true;
        Check("edit mode never begins one from the tape", !KillCam.TryBegin());
    }

    // ---- the song's reverse: what the tap heard, newest first ----
    static void MusicTap()
    {
        var go = new GameObject("~KillCamTapTest");
        try
        {
            var tap = go.AddComponent<KillCamMusicTap>();
            tap.Prepare(8000);
            var data = new float[8];   // 4 stereo frames: L = n, R = -n
            for (int f = 0; f < 4; f++) { data[f * 2] = f + 1; data[f * 2 + 1] = -(f + 1); }
            tap.Feed(data, 2);
            tap.Feed(data, 2);   // frames 1,2,3,4,1,2,3,4
            float[] dest = null;
            int ch, rate;
            int frames = tap.Snapshot(.0005f, ref dest, out ch, out rate);   // 4 frames at 8 kHz
            Check("the tap keeps what was heard and hands it back reversed (newest first)",
                  frames == 4 && ch == 2 && rate == 8000 && dest[0] == 4f && dest[1] == -4f && dest[6] == 1f && dest[7] == -1f);
            frames = tap.Snapshot(5f, ref dest, out ch, out rate);
            Check("it never hands back more than it heard", frames == 8);
            var big = new float[8000 * 2 * 3];
            tap.Feed(big, 2);
            frames = tap.Snapshot(5f, ref dest, out ch, out rate);
            Check("the ring holds only its last couple of seconds", frames == 16000);
        }
        finally { Object.DestroyImmediate(go); }
        Check("a missing sound clip plays nothing and the music reverse needs play mode",
              !KillCamAudio.Play(KillCamAudio.Cue.Rewind) && !KillCamMusic.Reversing);
        KillCamMusic.BeginReverse();
        KillCamMusic.Restore();
        KillCamMusic.Restore();
        Check("restoring twice is harmless", !KillCamMusic.Reversing && !KillCamMusic.MusicPaused);
        Check("the slow-mo muffle sweeps from open to a clear low-pass and back, and is off outside play",
              Mathf.Approximately(KillCamMusic.CutoffFor(0f), KillCamMusic.OpenCutoff) &&
              Mathf.Abs(KillCamMusic.CutoffFor(1f) - KillCamMusic.MuffleCutoff) < 1f &&
              KillCamMusic.CutoffFor(.5f) < KillCamMusic.OpenCutoff && KillCamMusic.CutoffFor(.5f) > KillCamMusic.MuffleCutoff &&
              KillCamMusic.MuffleCutoff >= 1000f && KillCamMusic.MuffleCutoff <= 2500f && !KillCamMusic.Muffling);
        KillCamMusic.EndMuffle();
        Check("ending the muffle with none up is harmless", !KillCamMusic.Muffling && KillCamMusic.MuffleAmount == 0f);
    }

    // ---- the death on the tape: the collision frame, the crash after it ----
    static void Death(System.Collections.Generic.List<Object> made)
    {
        KillCamRecorder.RequireVisible = false;
        KillCamRecorder.BeginRun();
        var rec = KillCamRecorder.Instance;
        var sprite = MakeSprite(made, Color.white);
        SpriteRenderer Make(string name)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            made.Add(go);
            var r = go.GetComponent<SpriteRenderer>();
            r.sprite = sprite;
            return r;
        }
        var ship = Make("~KillCamTestShip");
        var killer = Make("~KillCamTestKiller");
        var blast = Make("~KillCamTestBlast");
        blast.enabled = false;
        KillCamRecorder.TagBlast(blast, true);   // what TargetExplosion / LegacyExplosionFx do for their renderers
        var piece = Make("~KillCamTestPiece");    // a hull piece: not a blast
        piece.enabled = false;

        // 2 s of flight, the killer closing in
        const float dt = 1f / 60f;
        for (int f = 0; f < 120; f++)
        {
            ship.transform.position = new Vector3(0f, -3f, 0f);
            killer.transform.position = new Vector3(0f, -3f + (120 - f) * .02f, 0f);
            rec.Flight(dt);
        }
        // the hit: the killer on the hull
        killer.transform.position = new Vector3(0f, -3f, 0f);
        rec.Impact(dt);
        Check("the fatal hit is taped at once and the tape rolls on through the death",
              rec.Tailing && rec.ImpactAt > 0f && Mathf.Approximately(rec.ImpactAt, rec.Clock));
        // the death: the hull gone, a blast in its place, on the crash's clock
        ship.enabled = false;
        blast.enabled = true;
        piece.enabled = true;
        int steps = 0;
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long used = TestHarness.AllocatedBytes(() => { while (rec.Tailing && steps < 600) { rec.Tail(dt); steps++; } });
        Check("the death tail ends by itself after DeathTail (" + steps + " crash frames)", !rec.Tailing && steps >= 55 && steps <= 62);
        Check("taping the death allocates nothing: " + used + " bytes (meter control " + control + ")", meter && used == 0);
        rec.Tail(dt);
        Check("once the tail is long enough nothing more is taped", Mathf.Abs(rec.Clock - rec.ImpactAt - steps * dt) < 1e-3f);

        var clip = rec.Clip(KillCamClip.LiveWindow);
        Check("the clip holds LiveWindow before the hit and the death after it (" +
              (clip != null ? clip.ImpactTime.ToString("F2") + " + " + (clip.EndTime - clip.ImpactTime).ToString("F2") : "none") + " s)",
              clip != null && clip.HasTail && Mathf.Abs(clip.ImpactTime - KillCamClip.LiveWindow) <= 2f * KillCamRecorder.MinGap &&
              clip.EndTime - clip.ImpactTime >= KillCamClip.DeathTail - 1e-3f && clip.EndTime - clip.ImpactTime <= KillCamClip.DeathTail + KillCamRecorder.MinGap);
        if (clip == null) return;
        float a;
        int hitFrame = clip.FrameAt(clip.ImpactTime, out a);
        int shipTrack = ship.GetInstanceID(), killerTrack = killer.GetInstanceID(), blastTrack = blast.GetInstanceID();
        bool Has(int frame, int track, out KillCamEntry found)
        {
            found = default;
            for (int i = 0; i < clip.count[frame]; i++)
                if (clip.Entry(frame, i).track == track) { found = clip.Entry(frame, i); return true; }
            return false;
        }
        KillCamEntry hs, hk, tmp;
        Check("the collision frame is on the tape: the ship whole, the killer on its hull",
              Mathf.Abs(clip.time[hitFrame] - clip.ImpactTime) < 1e-4f && Has(hitFrame, shipTrack, out hs) && Has(hitFrame, killerTrack, out hk) &&
              Mathf.Abs(hs.pos.y - hk.pos.y) < 1e-3f && !Has(hitFrame, blastTrack, out tmp));
        int last = clip.frameCount - 1;
        Check("the clip's last frames are the death: the blast, no hull",
              Has(last, blastTrack, out tmp) && !Has(last, shipTrack, out tmp) && Has(last - 1, blastTrack, out tmp));
        // the death's explosion is on the tape, tagged, and only after the collision frame
        int pieceTrack = piece.GetInstanceID(), firstBlast = -1;
        for (int f = 0; f < clip.frameCount && firstBlast < 0; f++) if (Has2(clip, f, blastTrack)) firstBlast = f;
        KillCamEntry be, pe;
        Check("the blast is tagged on the tape (KillCamEntry.Blast), the hull piece beside it is not",
              Has(last, blastTrack, out be) && be.IsBlast && Has(last, pieceTrack, out pe) && !pe.IsBlast);
        Check("the blast's first frame comes after the collision frame (" + firstBlast + " > " + hitFrame + ")", firstBlast > hitFrame);
        Check("the replay runs slowest at the collision, livelier before and after it",
              clip.Rate(clip.ImpactTime) < clip.Rate(0f) && clip.Rate(clip.ImpactTime) < clip.Rate(clip.EndTime) &&
              Mathf.Approximately(clip.Rate(clip.ImpactTime), KillCamClip.SlowRate));

        // the replay plays the approach, slows into the collision and plays on through the death
        var kc = KillCam.Ensure();
        made.Add(kc.gameObject);
        KillCam.Intro = false;
        kc.Begin(clip);
        float real = 0f;
        bool through = false, slowAtHit = false;
        while (KillCam.Playing && real < 15f)
        {
            float before = kc.ClipTime;
            kc.Step(1f / 60f);
            real += 1f / 60f;
            through |= kc.ClipTime >= clip.EndTime - 1e-4f;
            if (before < clip.ImpactTime && kc.ClipTime >= clip.ImpactTime) slowAtHit = (kc.ClipTime - before) * 60f < KillCamClip.SlowRate + .02f;
        }
        KillCam.Intro = true;
        float want = KillCamClip.RealSeconds(clip.ImpactTime, clip.EndTime - clip.ImpactTime);
        Check("the replay plays on through the death to the tape's end, very slow at the collision (" + real.ToString("F2") + " s, about " + want.ToString("F2") + ")",
              through && slowAtHit && !KillCam.Playing && Mathf.Abs(real - want) < .25f);

        // a revive takes the death back off the tape
        rec.Uncrash();
        var after = rec.Clip(KillCamClip.LiveWindow);
        Check("a revive takes the death off the tape: flight only, no impact",
              rec.ImpactAt < 0f && !rec.Tailing && after != null && !after.HasTail && after.impact < 0f &&
              !Has2(after, after.frameCount - 1, blastTrack));
        rec.StopRecording();
        rec.Clear();
        KillCamRecorder.TagBlast(blast, false);
    }

    // The contact beat: the replay keeps the collision clear of the death's
    // explosion. A blast entry (tagged on the tape) fades in beneath the hull
    // pieces and the killer over the crash's hit-stop after the impact, then
    // plays as recorded. The live look is untouched (a replay-only rule).
    static void Contact(System.Collections.Generic.List<Object> made)
    {
        Check("the clear window is the crash's hit-stop, the blast goes beneath the pieces and the killer",
              Mathf.Approximately(KillCamStage.ContactClearSeconds, DeathCrash.HitStop) && KillCamStage.BelowContactOrder < DeathCrash.PieceOrder);
        var c = new Color(1f, 1f, 1f, .8f); int order = 70;
        KillCamStage.ContactRule(-.01f, ref c, ref order);
        Check("before the impact a blast draws as recorded", Mathf.Approximately(c.a, .8f) && order == 70);
        c = new Color(1f, 1f, 1f, .8f); order = 70;
        KillCamStage.ContactRule(0f, ref c, ref order);
        Check("at the impact it is invisible and beneath", c.a == 0f && order == KillCamStage.BelowContactOrder);
        c = new Color(1f, 1f, 1f, .8f); order = 70;
        KillCamStage.ContactRule(KillCamStage.ContactClearSeconds * .5f, ref c, ref order);
        Check("half way through the hit-stop it is fading in, still beneath", c.a > 0f && c.a < .8f && order == KillCamStage.BelowContactOrder);
        c = new Color(1f, 1f, 1f, .8f); order = 70;
        KillCamStage.ContactRule(KillCamStage.ContactClearSeconds, ref c, ref order);
        Check("from the end of the hit-stop it draws as recorded", Mathf.Approximately(c.a, .8f) && order == 70);
        c = new Color(1f, 1f, 1f, .8f); order = 50;
        KillCamStage.ContactRule(0f, ref c, ref order);
        Check("something already beneath is not lifted", order == 50);

        // the stage applies it to tagged entries only, around the clip's impact
        var sprite = MakeSprite(made, Color.white);
        var clip = MakeClip(sprite, 4, .1f, 1f);   // frames at 0, .1, .2, .3 s
        clip.impact = .1f;
        for (int f = 1; f < 4; f++)
        {
            var e = clip.entries[f * 2];   // track 7 is the blast from the impact frame on
            e.flags |= KillCamEntry.Blast;
            e.order = 70;
            clip.entries[f * 2] = e;
        }
        var holder = new GameObject("~KillCamTestContactStage");
        made.Add(holder);
        var stage = new KillCamStage(holder.transform, "Stage");
        stage.Draw(clip, .1f);
        var blast = stage.SpriteAt(0);
        var other = stage.SpriteAt(1);
        Check("at the impact frame the blast is drawn invisible and beneath; the untagged sprite as recorded",
              blast.color.a < 1e-3f && blast.sortingOrder == KillCamStage.BelowContactOrder && other.sortingOrder == 3 && Mathf.Abs(other.color.a - 128f / 255f) < .01f);
        stage.Draw(clip, .1f + KillCamStage.ContactClearSeconds * .5f);
        Check("half way through the hit-stop it is fading in", blast.color.a > .1f && blast.color.a < .9f && blast.sortingOrder == KillCamStage.BelowContactOrder);
        stage.Draw(clip, .3f);
        Check("after the hit-stop it draws as recorded (order 70, full alpha)", blast.sortingOrder == 70 && blast.color.a > .99f);
        stage.Draw(clip, 0f);
        Check("before the impact the same track, untagged, is untouched", stage.SpriteAt(0).sortingOrder == 5 && stage.SpriteAt(0).color.a > .99f);
        stage.Hide();
    }

    static bool Has2(KillCamClip clip, int frame, int track)
    {
        for (int i = 0; i < clip.count[frame]; i++) if (clip.Entry(frame, i).track == track) return true;
        return false;
    }

    // ---- a busy board: every dot of the elites' full-height telegraphs ----
    static void Busy(System.Collections.Generic.List<Object> made)
    {
        KillCamRecorder.RequireVisible = false;
        var rec = KillCamRecorder.Ensure();
        rec.Clear();
        var sprite = MakeSprite(made, Color.white, 4);
        // a Static Bastion hail wall: 3 waves x 2 lanes x 2 full-height sides of ~95 dots each, and the rest of the board
        const int dots = 1300;
        var holder = new GameObject("~KillCamTestDots");
        made.Add(holder);
        for (int i = 0; i < dots; i++)
        {
            var go = new GameObject("Dot", typeof(SpriteRenderer));
            go.transform.SetParent(holder.transform, false);
            go.transform.position = new Vector3((i / 95) * .3f, 6f - (i % 95) * .13f, 0f);
            go.GetComponent<SpriteRenderer>().sprite = sprite;
        }
        rec.Capture(0f);
        int taped = rec.LastFrameEntries;
        Check("a frame with " + dots + " dots tapes every one of them (" + taped + ")", taped >= dots && KillCamRecorder.MaxPerFrame >= dots);
        int needed = Mathf.CeilToInt((KillCamClip.LiveWindow + KillCamClip.DeathTail) / KillCamRecorder.MinGap) + 1;
        Check("the ring holds the whole replay (" + needed + " frames) of a board that busy",
              KillCamRecorder.FrameCap >= needed && KillCamRecorder.EntryCap / (dots - 250) >= needed - 2);
        Object.DestroyImmediate(holder);
        rec.Clear();
    }

    // ---- blending: a burst of new sprites, a pooled renderer handed back elsewhere ----
    static void Blend(System.Collections.Generic.List<Object> made)
    {
        var sprite = MakeSprite(made, Color.white);
        // frame 0: tracks 1..3; frame 1: 60 new sprites first, then 1..3 moved on; track 3 jumped across the board (a respawn)
        var clip = new KillCamClip(2, 80, 2);
        clip.time[0] = 0f; clip.time[1] = .1f;
        clip.first[0] = 0; clip.count[0] = 3;
        for (int i = 0; i < 3; i++)
            clip.entries[i] = new KillCamEntry { track = i + 1, sprite = sprite, pos = new Vector3(i, 0f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255) };
        clip.first[1] = 3; clip.count[1] = 63;
        for (int i = 0; i < 60; i++)
            clip.entries[3 + i] = new KillCamEntry { track = 100 + i, sprite = sprite, pos = new Vector3(0f, 3f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255) };
        for (int i = 0; i < 3; i++)
            clip.entries[63 + i] = new KillCamEntry { track = i + 1, sprite = sprite, pos = new Vector3(i == 2 ? -4f : i, 1f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255) };
        clip.frameCount = 2;
        int cursor = 0;
        Check("a track is still found behind a burst of 60 new sprites", clip.Match(1, 1, ref cursor) == 60);
        var holder = new GameObject("~KillCamTestBlend");
        made.Add(holder);
        var stage = new KillCamStage(holder.transform, "Stage");
        stage.Draw(clip, .05f);
        Check("behind the burst a mover still blends", Mathf.Abs(stage.SpriteAt(0).transform.position.y - .5f) < 1e-3f);
        Check("a pooled renderer handed back elsewhere does not sweep across the screen",
              Mathf.Abs(stage.SpriteAt(2).transform.position.x - 2f) < 1e-3f && Mathf.Abs(stage.SpriteAt(2).transform.position.y) < 1e-3f);
        stage.Destroy();
    }

    // ---- the saved clip's fidelity ----
    static void Fidelity(System.Collections.Generic.List<Object> made)
    {
        // sized as it is seen: a 1-unit boss cell drawn 3.3 u wide
        var cell = new KillCamArchive.Slot { worldSize = Vector2.one, srcW = 512f, srcH = 512f, srcPpu = 512f };
        KillCamArchive.Grow(cell, new KillCamEntry { m00 = 3.3f, m11 = 3.3f });
        KillCamArchive.SizeSlot(cell, 1f);
        Check("a sprite is copied at the size it is drawn on screen, not its own (" + cell.w + " px for a 1 u cell drawn 3.3 u wide)",
              cell.w == Mathf.CeilToInt(3.3f * KillCamArchive.Density) && cell.h == cell.w);
        var plain = new KillCamArchive.Slot { worldSize = Vector2.one, srcW = 512f, srcH = 512f, srcPpu = 512f };
        KillCamArchive.Grow(plain, new KillCamEntry { m00 = 0f, m01 = -1f, m10 = 1f, m11 = 0f });
        KillCamArchive.SizeSlot(plain, 1f);
        var small = new KillCamArchive.Slot { worldSize = Vector2.one, srcW = 64f, srcH = 64f, srcPpu = 64f };
        KillCamArchive.Grow(small, new KillCamEntry { m00 = 3f, m11 = 3f });
        KillCamArchive.SizeSlot(small, 1f);
        Check("rotated, the same scale; never more texels than the source has",
              plain.w == Mathf.CeilToInt(KillCamArchive.Density) && small.w == 64 && KillCamArchive.Density >= 140f);

        // edges extruded into the gutter
        var px = new byte[8 * 8 * 4];
        for (int y = 3; y < 5; y++) for (int x = 3; x < 5; x++) { int at = (y * 8 + x) * 4; px[at] = 200; px[at + 3] = 255; }
        KillCamArchive.Extrude(px, 8, 8, new RectInt(3, 3, 2, 2), 2);
        Check("a slot's edge texels are copied out into its gutter (corners too)",
              px[(3 * 8 + 1) * 4 + 3] == 255 && px[(6 * 8 + 4) * 4 + 3] == 255 && px[(1 * 8 + 1) * 4] == 200 && px[(7 * 8 + 7) * 4 + 3] == 0);

        // through the GPU: an opaque plate keeps an opaque edge, and a 1 px line survives a 4:1 shrink
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var cols = new Color[S * S];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color(.1f, .2f, .6f, 1f);
        for (int x = 0; x < S; x++) cols[29 * S + x] = Color.white;   // row 29: a point copy at 4:1 reads rows 2, 6, 10, ... and drops it
        tex.SetPixels(cols);
        tex.Apply();
        made.Add(tex);
        var plate = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S);
        made.Add(plate);
        float shrink = 15.9f / KillCamArchive.Density;   // drawn so it gets 16 texels
        var clip = new KillCamClip(3, 3, 1);
        for (int f = 0; f < 3; f++)
        {
            clip.time[f] = f * .05f;
            clip.first[f] = f; clip.count[f] = 1;
            clip.entries[f] = new KillCamEntry { track = 1, sprite = plate, pos = new Vector3(f * .1f, 0f, 0f), m00 = shrink, m11 = shrink, color = new Color32(255, 255, 255, 255) };
        }
        clip.frameCount = 3;
        clip.impact = .05f;
        var record = new KillCamArchive.Record { score = 50000, utcTicks = 638000000000009999L, ship = 1, world = "Storm", killer = "x" };
        bool saved = KillCamArchive.Save(clip, record, false);
        var loaded = KillCamArchive.Load();
        Check("a clip with a death after the hit is kept and loads with its impact (" + KillCamArchive.LastPackMs + " ms to pack, " +
              (File.Exists(KillCamArchive.FilePath) ? new FileInfo(KillCamArchive.FilePath).Length : 0) + " bytes, " + KillCamArchive.LastPages + " page(s))",
              saved && loaded != null && loaded.clip.HasTail && Mathf.Abs(loaded.clip.ImpactTime - .05f) < 1e-4f && loaded.clip.frameCount == 3);
        if (loaded == null) return;
        try
        {
            var s = loaded.clip.Entry(0, 0).sprite;
            var r = s.rect;
            var t = s.texture;
            Check("the plate is copied 16 texels across", Mathf.RoundToInt(r.width) == 16 && Mathf.RoundToInt(r.height) == 16);
            int cx = (int)r.center.x;
            Color outside = t.GetPixel((int)r.xMin - 1, (int)r.center.y), below = t.GetPixel(cx, (int)r.yMin - 1), above = t.GetPixel(cx, (int)r.yMax);
            Check("the plate's edge does not fade into its gutter (no seam where plates meet): " + outside.a.ToString("F2") + " " + below.a.ToString("F2") + " " + above.a.ToString("F2"),
                  outside.a > .95f && below.a > .95f && above.a > .95f);
            float brightest = 0f;
            for (int y = (int)r.yMin; y < (int)r.yMax; y++) brightest = Mathf.Max(brightest, t.GetPixel(cx, y).r);
            Check("a 1 px line of the art survives the shrink, box-filtered (brightest red " + brightest.ToString("F2") + " over the plate's .10)", brightest > .25f);
        }
        finally { loaded.Dispose(); }

        // the matrix as stored: a beam scaled 20 x keeps its length
        var p = new KillCamArchive.Packed { meta = KillCamArchive.Record.Parse(record.Format()), camHalfHeight = 6.6f, camAspect = .5f, camZ = -10f };
        p.atlas.Add(new KillCamArchive.Page { width = 4, height = 4, rgba = new byte[64] });
        p.pages.Add(0); p.rects.Add(new RectInt(0, 0, 4, 4)); p.pivots.Add(new Vector2(.5f, .5f)); p.ppu.Add(4f); p.borders.Add(Vector4.zero);
        p.frames = 1; p.time = new[] { 0f }; p.cam = new[] { Vector2.zero }; p.counts = new[] { 1 }; p.meshCounts = new[] { 0 };
        p.entries.Add(new KillCamArchive.PEntry { sprite = 0, material = -1, m00 = .02f, m11 = 21.5f, m01 = -3.25f, color = new Color32(255, 255, 255, 255) });
        var back = KillCamArchive.Deserialize(KillCamArchive.Serialize(p));
        Check("a sprite stretched 21.5 x (a lane the height of the view) keeps its length and shear through the file",
              back != null && Mathf.Approximately(back.entries[0].m11, 21.5f) && Mathf.Approximately(back.entries[0].m01, -3.25f) && Mathf.Approximately(back.entries[0].m00, .02f));
        Check("the exhaust skins' palette shift is baked (it lives in the renderer, not the material)",
              System.Array.IndexOf(KillCamArchive.BakedShaders, ExhaustRemap.ShaderName) >= 0 &&
              (ExhaustRemap.Material == null || KillCamArchive.Bakes(ExhaustRemap.Material)));
        KillCamArchive.Clear();
    }

    // ---- the record is written only once the file is ----
    static void RecordOrder(System.Collections.Generic.List<Object> made)
    {
        KillCamArchive.Clear();
        var sprite = MakeSprite(made, Color.green, 16);
        var clip = MakeClip(sprite, 12, 1f / 60f, 1f);
        var r = new KillCamArchive.Record { score = 777, utcTicks = 638000000000012345L, ship = 2, world = "Tide", killer = "" };
        bool saved = KillCamArchive.Save(clip, r, true);
        var pending = KillCamArchive.Pending;
        Check("a background save is under way", saved && pending != null);
        if (pending == null) return;
        // (the record is never ahead of the file: before the write is done there is none)
        string before = PlayerPrefs.GetString(KillCamArchive.PrefsKey, "");
        bool notAhead = string.IsNullOrEmpty(before) || pending.IsCompleted;
        pending.Wait();
        Check("the record is not written before the file", notAhead);
        var latest = KillCamArchive.Latest;
        Check("once the file is written the record follows it (settled on the main thread)",
              latest != null && latest.SameAs(r) && File.Exists(KillCamArchive.FilePath) && KillCamArchive.Pending == null);
        var loaded = KillCamArchive.Load();
        Check("and it loads", loaded != null && loaded.meta.SameAs(r));
        if (loaded != null) loaded.Dispose();
        KillCamArchive.Clear();
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
        var rgba = new byte[4 * 4 * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 200; rgba[i + 3] = 255; }
        p.atlas.Add(new KillCamArchive.Page { width = 4, height = 4, rgba = rgba });
        p.pages.Add(0);
        p.rects.Add(new RectInt(0, 0, 4, 4)); p.pivots.Add(new Vector2(.5f, .5f)); p.ppu.Add(4f); p.borders.Add(Vector4.zero);
        p.frames = 2;
        p.time = new[] { 1f, 1.05f };
        p.cam = new[] { new Vector2(0f, 0f), new Vector2(0f, .5f) };
        p.counts = new[] { 1, 1 };
        p.meshCounts = new[] { 0, 0 };
        p.entries.Add(new KillCamArchive.PEntry { track = 0, sprite = 0, material = -1, order = 4, pos = new Vector3(1.5f, 2f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255) });
        p.entries.Add(new KillCamArchive.PEntry { track = 0, sprite = 0, material = -1, order = 4, pos = new Vector3(1.75f, 2f, 0f), m00 = 1f, m11 = 1f, color = new Color32(255, 255, 255, 255), flags = KillCamEntry.Blast });
        var bytes = KillCamArchive.Serialize(p);
        var back = KillCamArchive.Deserialize(bytes);
        Check("the file round-trips", back != null && back.frames == 2 && back.entries.Count == 2 && back.meta.SameAs(p.meta) &&
              Mathf.Abs(back.entries[1].pos.x - 1.75f) < .01f && back.background.b == 3);
        var loaded = KillCamArchive.Build(back);
        Check("a loaded clip starts at 0, its entries back in world space, its sprite from the atlas",
              loaded != null && loaded.clip.frameCount == 2 && loaded.clip.time[0] == 0f && Mathf.Abs(loaded.clip.time[1] - .05f) < 1e-4f &&
              Mathf.Abs(loaded.clip.Entry(1, 0).pos.y - 2.5f) < .01f && loaded.clip.Entry(0, 0).sprite != null);
        Check("the blast tag survives the file (the codex's replay clears the contact beat too)",
              loaded != null && loaded.clip.Entry(1, 0).IsBlast && !loaded.clip.Entry(0, 0).IsBlast);
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
        Check("it loads: frames kept at the store rate (" + (first != null ? first.clip.frameCount : -1) + "), the sprites' own colours",
              first != null && first.clip.frameCount >= 20 && first.clip.frameCount <= 30 && colours);
        if (first != null) first.Dispose();
        long size = File.Exists(KillCamArchive.FilePath) ? new FileInfo(KillCamArchive.FilePath).Length : 0;
        Check("the file is small (" + size + " bytes)", size > 0 && size < 64 * 1024);

        Check("the version 1 single-atlas format is dropped, not misread",
              KillCamArchive.Version == 2 && KillCamArchive.Record.Parse("1|5|6|1|Space|x") == null);

        var r2 = new KillCamArchive.Record { score = 20000, utcTicks = r.utcTicks + 1, ship = 1, world = "Space", killer = "" };
        KillCamArchive.Save(clip, r2, false);
        var second = KillCamArchive.Load();
        Check("the next new best replaces it: one slot, the latest", KillCamArchive.Latest.score == 20000 && second != null && second.meta.score == 20000);
        if (second != null) second.Dispose();
        Check("the codex describes the latest", KillCamArchive.Describe().Contains("20,000") || KillCamArchive.Describe().Contains("20000") || KillCamArchive.Describe().Contains("20"));

        Baked(made, r);

        var newer = new KillCamArchive.Record { score = 1, utcTicks = r.utcTicks + 99, ship = r.ship, world = r.world, killer = r.killer };
        PlayerPrefs.SetString(KillCamArchive.PrefsKey, newer.Format());
        Check("a file older than its record is not trusted", KillCamArchive.Load() == null);
        PlayerPrefs.DeleteKey(KillCamArchive.PrefsKey);
        var adopted = KillCamArchive.Load();
        Check("a whole file without its record (the app closed first) is adopted, record and all",
              adopted != null && KillCamArchive.Latest != null && KillCamArchive.Latest.utcTicks == r.utcTicks + 2);
        if (adopted != null) adopted.Dispose();
        KillCamArchive.Clear();
        Check("cleared, there is no record", !KillCamArchive.HasRecord && KillCamArchive.Load() == null);
    }

    // A sprite drawn with a shader that works in its own texture space (the
    // Space sky's wrap: v' = v * (1 - blend), cross-faded from v' + 1 - blend)
    // is kept as it looked, not re-made on the atlas, where the same maths
    // would read other sprites. The tile here is red below, green above;
    // through the wrap at blend .5 its copy shows green at the bottom and red
    // at the top. The same sprite drawn plain keeps its own slot.
    static void Baked(System.Collections.Generic.List<Object> made, KillCamArchive.Record r)
    {
        var shader = Shader.Find("Pause/BackdropSkyWrap");
        Check("the sky wrap shader is found", shader != null);
        if (shader == null) return;
        var wrap = new Material(shader);
        made.Add(wrap);
        wrap.SetFloat("_Blend", .5f);
        var plainMat = new Material(Shader.Find("Sprites/Default"));
        made.Add(plainMat);
        Check("the sky wrap is baked, the default sprite material is not",
              KillCamArchive.Bakes(wrap) && !KillCamArchive.Bakes(null) && !KillCamArchive.Bakes(plainMat));

        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) px[y * size + x] = y < size / 2 ? Color.red : Color.green;
        tex.SetPixels(px);
        tex.Apply();
        var sky = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        made.Add(tex);
        made.Add(sky);

        var clip = MakeClip(sky, 20, 1f / 60f, 1f);
        for (int f = 0; f < clip.frameCount; f++) { var e = clip.entries[f * 2]; e.material = wrap; clip.entries[f * 2] = e; }
        var record = new KillCamArchive.Record { score = 30000, utcTicks = r.utcTicks + 2, ship = r.ship, world = "Space", killer = r.killer };
        bool saved = KillCamArchive.Save(clip, record, false);
        var loaded = KillCamArchive.Load();
        Check("a clip with a wrapped sky is kept", saved && loaded != null);
        if (loaded == null) return;
        try
        {
            KillCamEntry baked = default, plain = default;
            for (int i = 0; i < loaded.clip.count[0]; i++)
            {
                var e = loaded.clip.Entry(0, i);
                if (e.order == 5) baked = e; else plain = e;
            }
            Check("the wrapped sprite replays with the plain sprite material (its look is in the atlas)",
                  baked.sprite != null && baked.material == null && plain.sprite != null && baked.sprite != plain.sprite);
            if (baked.sprite == null || plain.sprite == null) return;
            Color Px(Sprite s, bool top)
            {
                var rect = s.rect;
                return s.texture.GetPixel((int)rect.center.x, top ? (int)rect.yMax - 1 : (int)rect.yMin);
            }
            Color bb = Px(baked.sprite, false), bt = Px(baked.sprite, true);
            Check("the baked slot holds the wrap's output: green at the bottom, red at the top (" + bb + " / " + bt + ")",
                  bb.g > .8f && bb.r < .2f && bt.r > .8f && bt.g < .2f && bb.a > .95f && bt.a > .95f);
            Color pb = Px(plain.sprite, false), pt = Px(plain.sprite, true);
            Check("the same sprite drawn plain keeps its own art: red at the bottom, green at the top",
                  pb.r > .8f && pb.g < .2f && pt.g > .8f && pt.r < .2f);
        }
        finally { loaded.Dispose(); }
    }
}
