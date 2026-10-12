using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

// A muted, looping H.264 clip from StreamingAssets/Video, decoded by a Unity
// VideoPlayer into a RenderTexture. Used by the title screen's background
// (TitleVideo) and the Options screen's how-to-play loop (HowToPlayClip).
//
// Rules that keep it safe to add to any screen:
//   * Async: Prepare() runs in the background; nothing is drawn (and the caller
//     keeps its normal art) until the first frame is decoded, so the screen
//     loads at the same speed as without it.
//   * Silent: no audio track in the files and AudioOutputMode.None, so the
//     menu's own music is never touched.
//   * Optional: any failure (file missing, codec or device unsupported, a
//     decoder error, low-memory device, batch mode) leaves Ready false /
//     Failed true and the caller shows what it showed before.
//
// Why StreamingAssets and not VideoClip assets: see docs/video-clips.md.
public sealed class LoopingClip : MonoBehaviour
{
    public const string Folder = "Video";
    // Phones below this much RAM keep the still backdrop (the decoder and
    // render texture are the only extra memory; 2.5 GB leaves every 3 GB phone in).
    public const int MinMemoryMB = 2500;

    // Tests and previews switch it off / on; batch mode (headless tests) is off unless forced.
    public static bool Disabled;
    public static bool AllowInBatchMode;

    public static bool Supported
    {
        get
        {
            if (Disabled) return false;
            if (Application.isBatchMode && !AllowInBatchMode) return false;
            if (Application.platform == RuntimePlatform.WebGLPlayer) return false;
            if (!SystemInfo.supportsRenderTextures) return false;
            return SystemInfo.systemMemorySize >= MinMemoryMB || SystemInfo.systemMemorySize <= 0;
        }
    }

    public RenderTexture Texture { get; private set; }
    public bool Ready { get; private set; }     // first frame decoded and playing
    public bool Failed { get; private set; }
    public string Error { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public event Action BecameReady;

    VideoPlayer player;

    // Builds the player on `host` and starts preparing. Null when the clip
    // cannot be used (unsupported, missing); the caller then does nothing.
    public static LoopingClip Create(GameObject host, string fileName, int width, int height)
    {
        if (!Supported) return null;
        string url = UrlFor(fileName);
        if (url == null) return null;
        var clip = host.AddComponent<LoopingClip>();
        clip.Begin(url, width, height);
        return clip;
    }

    // jar:file:// (Android APK/AAB assets) is used as is; anywhere else the
    // file must exist on disk.
    public static string UrlFor(string fileName)
    {
        string path = Application.streamingAssetsPath + "/" + Folder + "/" + fileName;
        if (path.Contains("://")) return path;
        if (!File.Exists(path)) return null;
        return "file://" + path;
    }

    void Begin(string url, int width, int height)
    {
        Width = width; Height = height;
        Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "LoopingClip", useMipMap = false };
        Texture.Create();
        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.source = VideoSource.Url;
        player.url = url;
        player.isLooping = true;
        player.skipOnDrop = true;
        player.waitForFirstFrame = true;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = Texture;
        player.aspectRatio = VideoAspectRatio.Stretch;
        player.sendFrameReadyEvents = true;
        player.errorReceived += OnError;
        player.prepareCompleted += OnPrepared;
        player.frameReady += OnFrame;
        player.Prepare();
    }

    void OnPrepared(VideoPlayer p) { if (!Failed) p.Play(); }

    void OnFrame(VideoPlayer p, long frame)
    {
        if (Ready || Failed) return;
        Ready = true;
        p.sendFrameReadyEvents = false;
        if (BecameReady != null) BecameReady();
    }

    void OnError(VideoPlayer p, string message)
    {
        Failed = true; Ready = false; Error = message;
        Debug.LogWarning("[LoopingClip] " + message + " -> keeping the still art");
    }

    // The player stops itself on disable; a screen that hides the clip calls this.
    public void SetPlaying(bool on)
    {
        if (player == null || Failed) return;
        if (on && Ready && !player.isPlaying) player.Play();
        else if (!on && player.isPlaying) player.Pause();
    }

    void OnDestroy()
    {
        if (player != null)
        {
            player.errorReceived -= OnError;
            player.prepareCompleted -= OnPrepared;
            player.frameReady -= OnFrame;
            player.Stop();
        }
        if (Texture != null) { Texture.Release(); Destroy(Texture); }
        Texture = null;
    }
}
