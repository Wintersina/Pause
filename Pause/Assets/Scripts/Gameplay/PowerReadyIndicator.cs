using UnityEngine;

// A compact charge lamp fixed above the active hull. It fills while the
// ultimate charges and switches to a bright, pulsing READY state just before
// the cinematic sweep starts.
public class PowerReadyIndicator : MonoBehaviour
{
    SpriteRenderer rendererRef;
    ShipPowerController controller;
    float baseWidth;

    void Start()
    {
        controller = GetComponent<ShipPowerController>();
        var hull = GetComponent<SpriteRenderer>();
        var go = new GameObject("~PowerReadyIndicator", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        rendererRef = go.GetComponent<SpriteRenderer>();
        rendererRef.sprite = SolidSprite();
        rendererRef.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 4;
        float halfHeight = hull != null && hull.sprite != null ? hull.sprite.bounds.extents.y : .4f;
        baseWidth = hull != null && hull.sprite != null ? hull.sprite.bounds.size.x * .85f : .6f;
        go.transform.localPosition = new Vector3(0f, halfHeight + .16f, -.05f);
        go.transform.localScale = new Vector3(.02f, .035f, 1f);
    }

    void Update()
    {
        if (rendererRef == null || controller == null) return;
        float progress = controller.Charge01;
        bool ready = progress >= .995f || ShipPowerController.CinematicClearActive;
        rendererRef.transform.localScale = new Vector3(Mathf.Max(.025f, baseWidth * progress), ready ? .075f : .035f, 1f);
        rendererRef.color = ready
            ? Color.Lerp(new Color(1f, .65f, .15f), Color.white, .5f + .5f * Mathf.Sin(Time.unscaledTime * 12f))
            : Color.Lerp(new Color(.12f, .25f, .38f, .8f), new Color(.15f, .95f, 1f, 1f), progress);
    }

    static Sprite cached;
    static Sprite SolidSprite()
    {
        if (cached != null) return cached;
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        cached = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2f);
        return cached;
    }
}
