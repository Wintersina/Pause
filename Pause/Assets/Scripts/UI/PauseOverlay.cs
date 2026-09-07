using UnityEngine;
using UnityEngine.UI;

// The scene reference keeps the original app icon available in player builds.
public class PauseOverlay : MonoBehaviour
{
    public Sprite icon;
    GameObject indicator;

    void Start()
    {
        var holder = new GameObject("PauseOverlayCanvas", typeof(Canvas), typeof(CanvasScaler));
        holder.transform.SetParent(transform, false);
        var canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 1000f);
        scaler.matchWidthOrHeight = 0.5f;

        indicator = new GameObject("PausedIcon", typeof(Image));
        indicator.transform.SetParent(holder.transform, false);
        var image = indicator.GetComponent<Image>();
        image.sprite = icon;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.55f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(144f, 144f);
        indicator.SetActive(false);
    }

    void LateUpdate()
    {
        if (indicator != null)
            indicator.SetActive(ShouldShow(Time.timeScale, buttonClicks.playerDied));
    }

    public static bool ShouldShow(float timeScale, bool playerDied)
    {
        return timeScale == 0f && !playerDied;
    }
}
