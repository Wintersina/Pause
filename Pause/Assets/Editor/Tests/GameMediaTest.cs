using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The gameplay clips (docs/video-clips.md): the files are where the player
// loads them from and inside their size budgets, the player is optional and
// silent, and the title video draws as a dimmed, cropped quad between the
// backdrop and the title traffic -- never over the menu.
public static class GameMediaTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MEDIA] PASS  " : "[MEDIA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool disabledWas = LoopingClip.Disabled;
        try
        {
            Files();
            Fallbacks();
            TitleQuad();
            Options();
        }
        finally { LoopingClip.Disabled = disabledWas; HowToPlayClip.PreviewTexture = null; }
        Debug.Log("[MEDIA] failures: " + fails);
        return fails;
    }

    static void Files()
    {
        CheckClip(TitleVideo.File, 3_000_000, "title loop");
        CheckClip(HowToPlayClip.File, 1_500_000, "how-to-play clip");
        Check("clips live in StreamingAssets/Video, outside every Resources folder",
              Directory.Exists("Assets/StreamingAssets/Video") && !Directory.Exists("Assets/Resources/Video"));
    }

    static void CheckClip(string file, long maxBytes, string label)
    {
        string path = "Assets/StreamingAssets/Video/" + file;
        bool exists = File.Exists(path);
        long size = exists ? new FileInfo(path).Length : 0;
        bool mp4 = false;
        if (exists)
        {
            var head = new byte[12];
            using (var f = File.OpenRead(path)) f.Read(head, 0, 12);
            mp4 = head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p';
        }
        Check(label + " exists, is an mp4 and is " + (size / 1024) + " KB (budget " + (maxBytes / 1024) + " KB)", exists && mp4 && size <= maxBytes);
        // no audio track: an mp4 'soun' handler would mean the clip carries sound
        bool silent = exists && IndexOf(File.ReadAllBytes(path), "soun") < 0;
        Check(label + " has no audio track", silent);
        Check(label + " is only ever played with audio output off (LoopingClip: AudioOutputMode.None)",
              File.ReadAllText("Assets/Scripts/Video/LoopingClip.cs").Contains("VideoAudioOutputMode.None"));
    }

    static int IndexOf(byte[] data, string tag)
    {
        for (int i = 0; i + 4 <= data.Length; i++)
            if (data[i] == tag[0] && data[i + 1] == tag[1] && data[i + 2] == tag[2] && data[i + 3] == tag[3]) return i;
        return -1;
    }

    static void Fallbacks()
    {
        LoopingClip.Disabled = true;
        var go = new GameObject("clip test");
        Check("a disabled / unsupported player makes no clip (the old backdrop stays)", LoopingClip.Create(go, TitleVideo.File, 540, 960) == null);
        LoopingClip.Disabled = false;
        Check("headless batch mode never starts a VideoPlayer", !LoopingClip.Supported || !Application.isBatchMode);
        Check("a missing file makes no clip", LoopingClip.UrlFor("does_not_exist.mp4") == null);
        Check("the title clip resolves to a file URL", LoopingClip.UrlFor(TitleVideo.File) != null);
        Object.DestroyImmediate(go);
    }

    static void TitleQuad()
    {
        EditorSceneLoader.Open("startS4", OpenSceneMode.Single);
        var tex = new Texture2D(4, 4);
        var video = new GameObject("~TitleVideo").AddComponent<TitleVideo>();
        Check("title video draws nothing until a frame exists", video.Quad == null);
        video.ShowTexture(tex);
        video.SetFade(1f);
        Check("title video quad exists once a frame is shown", video.Quad != null);
        if (video.Quad == null) return;
        Check("it sorts above the backdrop tiles (" + BackdropCatalog.BaseOrder + ") and below the title traffic (-300)",
              video.Quad.sortingOrder > BackdropCatalog.BaseOrder + 50 && video.Quad.sortingOrder < -300);
        var color = video.Quad.sharedMaterial.color;
        Check("it is dimmed to " + TitleVideo.Dim + " so the menu stays readable", Mathf.Abs(color.r - TitleVideo.Dim) < 0.01f && color.a > 0.99f);
        var cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            var b = video.Quad.bounds;
            Check("the quad covers the whole camera view",
                  b.size.y >= 2f * cam.orthographicSize - 0.01f && b.size.x >= 2f * cam.orthographicSize * cam.aspect - 0.01f);
        }
        Object.DestroyImmediate(video.gameObject);
        Object.DestroyImmediate(tex);
    }

    static void Options()
    {
        EditorSceneLoader.Open("leaderboardS3", OpenSceneMode.Single);
        HowToPlayClip.PreviewTexture = Texture2D.grayTexture;
        var clip = new GameObject("~HowToPlayClip").AddComponent<HowToPlayClip>();
        clip.SendMessage("Start");
        clip.Relayout();
        Check("how-to-play frame is built under the Options rows", clip.Frame != null);
        if (clip.Frame != null)
        {
            Check("... inside the options canvas, not a button", clip.Frame.GetComponentInParent<Canvas>() != null
                  && clip.Frame.GetComponent<UnityEngine.UI.Selectable>() == null);
            if (clip.Frame.gameObject.activeInHierarchy && clip.Frame.rect.height > 1f)
                Check("... 9:16 and labelled HOW TO PLAY",
                      Mathf.Abs(clip.Frame.rect.width / clip.Frame.rect.height - 540f / 960f) < 0.05f
                      && clip.GetComponentInChildren<UnityEngine.UI.Text>(true) == null
                      && clip.Frame.parent.GetComponentInChildren<UnityEngine.UI.Text>(true).text == HowToPlayClip.LabelText);
        }
        Object.DestroyImmediate(clip.gameObject);
    }
}
