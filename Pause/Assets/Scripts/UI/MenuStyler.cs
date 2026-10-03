using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Shared runtime styling for the menu screens' text buttons (startS4, the
// Options screen leaderboardS3, and the space dock's BACK / LIFT-OFF in
// shopS6), so the scenes themselves are not edited.
//
// Akira look (docs/art-style.md): BONE type, a thick INK outline and a hard
// RED cel drop under it. Motion: every button gets the cartoon press
// (CelPress); the primary action (PLAY) flashes AMBER for two ticks on a beat
// so the eye finds it. Only Text labels on Buttons are touched -- never the
// PAUSE logo, never layout.
public class MenuStyler : MonoBehaviour
{
    public static readonly string[] Scenes = { "startS4", "leaderboardS3", "shopS6" };
    public const string PrimaryButton = "PlayButton";
    const float BeatSeconds = 2.6f;

    readonly List<Text> primary = new List<Text>();

    public static void StyleScene()
    {
        if (FindFirstObjectByType<MenuStyler>() != null) return;
        var styler = new GameObject("~MenuStyler").AddComponent<MenuStyler>();
        foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) continue;
            StyleLabel(label);
            CelPress.AddTo(button.gameObject);
            if (button.name == PrimaryButton) styler.primary.Add(label);
        }
    }

    public static void StyleLabel(Text label)
    {
        label.color = AkiraPalette.Bone;
        if (label.fontStyle == FontStyle.Normal) label.fontStyle = FontStyle.Bold;

        // Outline (ink) first, then the hard red cel drop: two Shadow effects.
        Outline outline = null;
        Shadow cel = null;
        foreach (var s in label.GetComponents<Shadow>())
        {
            if (s is Outline o) { if (outline == null) outline = o; }
            else if (cel == null) cel = s;
        }
        if (outline == null) outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = AkiraPalette.Ink;
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        outline.enabled = true;
        if (cel == null) cel = label.gameObject.AddComponent<Shadow>();
        cel.effectColor = AkiraPalette.Red;
        cel.effectDistance = new Vector2(4f, -5f);
        cel.enabled = true;
    }

    void Update()
    {
        if (primary.Count == 0) return;
        // Two ticks AMBER on each beat; BONE the rest of the time.
        float k = Mathf.Repeat(Time.unscaledTime, BeatSeconds) * 24f;
        Color c = k < 2f ? AkiraPalette.Amber : AkiraPalette.Bone;
        foreach (var t in primary)
            if (t != null && t.color != c) t.color = c;
    }
}

public static class MenuStylerBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(MenuStyler.Scenes, scene.name) < 0) return;
        MenuStyler.StyleScene();
    }
}
