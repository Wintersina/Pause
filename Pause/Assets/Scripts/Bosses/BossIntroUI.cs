using UnityEngine;
using UnityEngine.UI;

// The boss intro's screen layer: a one-tick flat flash, the WARNING slab
// that snaps in and blinks, then the boss's name card that slams in from
// the right, holds and snaps out. Everything on unscaled time (the world is
// frozen through the intro) and on held ticks, never eased fades. It stays
// up through the fight for the ultimate's hit flashes and is closed by the
// encounter.
public class BossIntroUI : MonoBehaviour
{
    static readonly Color Bone = new Color(.957f, .918f, .831f);

    Image flash, warning, card;
    float clock;
    float flashLeft;
    Color flashColor;
    bool landed;

    public static BossIntroUI Play(BossDef boss)
    {
        var root = new GameObject("~BossIntro", typeof(Canvas), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 590; // under WorldBanner (600)
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var ui = root.AddComponent<BossIntroUI>();
        ui.flash = NewImage(root.transform, "Flash", null, Vector2.zero, Vector2.zero, true);
        ui.flash.color = new Color(1f, 1f, 1f, 0f);
        ui.warning = NewImage(root.transform, "Warning", BossArt.Warning(), new Vector2(-1000f, 330f), new Vector2(760f, 143f), false);
        ui.card = NewImage(root.transform, "NameCard", BossArt.Card(boss), new Vector2(1000f, 120f), new Vector2(760f, 214f), false);
        ui.Flash(Bone, 2, .9f);
        return ui;
    }

    static Image NewImage(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, bool stretch)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = sprite != null;
        img.raycastTarget = false;
        img.enabled = sprite != null || stretch;
        return img;
    }

    void Flash(Color c, int ticks, float alpha)
    {
        flashColor = new Color(c.r, c.g, c.b, alpha);
        flashLeft = ticks * BossArt.Tick;
    }

    public void HitFlash(Color tint)
    {
        Flash(tint, 2, .35f);
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        clock += dt;

        flashLeft -= dt;
        flash.color = flashLeft > 0f ? flashColor : new Color(1f, 1f, 1f, 0f);

        // A second, smaller flash the moment the boss lands.
        float land = BossConfig.BossArriveAt + BossConfig.BossArriveSeconds * .55f;
        if (!landed && clock >= land) { landed = true; Flash(Bone, 1, .45f); }

        Slide(warning.rectTransform, clock - BossConfig.WarningAt, BossConfig.NameCardAt - BossConfig.WarningAt,
              -1000f, 1000f, 330f);
        // WARNING blinks on threes: 6 ticks on, 2 off.
        int tick = Mathf.FloorToInt((clock - BossConfig.WarningAt) / BossArt.Tick);
        if (warning.sprite != null) warning.enabled = tick < 0 || tick % 8 < 6;

        Slide(card.rectTransform, clock - BossConfig.NameCardAt, BossConfig.IntroSeconds - BossConfig.NameCardAt,
              1000f, -1000f, 120f);
    }

    // In over 4 ticks (overshoot on the third), held, out over 3 ticks.
    static void Slide(RectTransform rt, float t, float hold, float fromX, float toX, float y)
    {
        float tick = BossArt.Tick;
        float x;
        if (t < 0f) x = fromX;
        else if (t < tick) x = fromX * .55f;
        else if (t < 2f * tick) x = fromX * .18f;
        else if (t < 3f * tick) x = -fromX * .04f;   // overshoot
        else if (t < hold) x = 0f;
        else if (t < hold + tick) x = toX * .12f;
        else if (t < hold + 2f * tick) x = toX * .45f;
        else x = toX;
        rt.anchoredPosition = new Vector2(x, y);
    }

    public void Close()
    {
        BossUtil.Kill(gameObject);
    }
}
