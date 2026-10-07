using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Renders stills of the runtime-generated splash gate for review.
public static class HapticGatePreview
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Render()
    {
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        var card = Object.FindFirstObjectByType<splashScene>();
        var cam = card.GetComponentInParent<Camera>();
        const int width = 1080, height = 1920;
        var layout = card.ApplyLayout(width, height, new Rect(0, 0, width, height));

        typeof(splashScene).GetMethod("EnsureGate", Private).Invoke(card, null);
        var root = (Transform)typeof(splashScene).GetField("gateRoot", Private).GetValue(card);
        root.position = card.logo.transform.position + new Vector3(0f, 0f, -0.15f);
        root.localScale = Vector3.one * layout.logoSize.x;

        var canvas = card.wordsScaler.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;
        canvas.scaleFactor = layout.wordsScaleFactor;

        string dir = Path.Combine(Application.dataPath, "../../HapticGateFramesV2");
        Directory.CreateDirectory(dir);
        const int frameCount = 61;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.aspect = (float)width / height;
        for (int i = 0; i < frameCount; i++)
        {
            float time = i / 30f;
            typeof(splashScene).GetField("elapsed", Private).SetValue(card, time);
            typeof(splashScene).GetMethod("AnimateGate", Private).Invoke(card, null);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var crop = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            crop.ReadPixels(new Rect(40, 460, 1000, 1000), 0, 0);
            crop.Apply();
            string file = Path.Combine(dir, "gate_" + i.ToString("00") + ".png");
            File.WriteAllBytes(file, crop.EncodeToPNG());
            Object.DestroyImmediate(crop);
            Debug.Log("[GATE PREVIEW] " + file + " at " + time.ToString("F2") + "s");
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorApplication.Exit(0);
    }
}
