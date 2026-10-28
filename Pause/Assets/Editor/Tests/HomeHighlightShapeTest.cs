using UnityEngine;
using UnityEngine.UI;

// The home menu's highlight (the translucent red plate a pressed button shows)
// is the game's cut-corner rectangle: chamfer at the TOP-LEFT and BOTTOM-RIGHT
// corners only, like the death panel's buttons. Same colour as before (the
// scene's pressed colour), one cached sprite shared by every home item.
public static class HomeHighlightShapeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HOMEHLSHAPE] PASS  " : "[HOMEHLSHAPE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var s = CutCornerShape.Sprite;
        var t = s.texture;
        int n = t.width - 1;
        Check("sprite is cached (same instance on every call: nothing allocated per frame)", ReferenceEquals(s, CutCornerShape.Sprite));
        Check("point filtered", t.filterMode == FilterMode.Point);
        Check("top-left corner is cut", t.GetPixel(0, n).a < .01f);
        Check("bottom-right corner is cut", t.GetPixel(n, 0).a < .01f);
        Check("top-right corner is filled", t.GetPixel(n, n).a > .99f);
        Check("bottom-left corner is filled", t.GetPixel(0, 0).a > .99f);
        Check("centre filled", t.GetPixel(n / 2, n / 2).a > .99f);
        Check("the cut is a 45 degree chamfer of " + CutCornerShape.Cut + " px",
              t.GetPixel(CutCornerShape.Cut - 1, n).a < .01f && t.GetPixel(CutCornerShape.Cut, n).a > .99f &&
              t.GetPixel(0, n - CutCornerShape.Cut + 1).a < .01f && t.GetPixel(0, n - CutCornerShape.Cut).a > .99f);
        var b = s.border;
        Check("9-slice borders valid (" + b + ")", b.x > 0 && b.x == b.y && b.y == b.z && b.z == b.w &&
              b.x * 2 < t.width && CutCornerShape.Cut <= b.x);

        var screen = ScreenFitScreens.Find("home");
        EditorSceneLoader.Open(screen.scene);
        Time.timeScale = 1f;
        using (var rig = new ScreenFitRig(FitDevice.Find("and-1080x2340-notch"), screen.MinHalfWidth))
        {
            UiScaleFloor.AttachScene();
            rig.Sync();
            screen.stage(rig);
            rig.Sync();
            foreach (var name in new[] { "PlayButton", "shopButton", "achivButton", "LeaderboardButton", "CodexButton", "PrivacyButton", "QuitButton" })
            {
                var go = GameObject.Find(name);
                var button = go != null ? go.GetComponent<Button>() : null;
                var image = button != null ? button.targetGraphic as Image : null;
                Check(name + " plate uses the cut-corner sprite, sliced", image != null && image.sprite == s && image.type == Image.Type.Sliced);
                Check(name + " highlight colour is the scene's pressed red (alpha .5)", button != null &&
                      Mathf.Abs(button.colors.pressedColor.a - .503f) < .01f && button.colors.pressedColor.r > .8f && button.colors.pressedColor.g < .3f);
            }
        }
        Debug.Log("[HOMEHLSHAPE] failures: " + fails);
        return fails;
    }
}
