using UnityEngine;
using UnityEngine.UI;

public class CreditsMove : MonoBehaviour {

    // The HapticGate lockup (open gate, red seam, wordmark) heads the roll: a
    // child of the scrolling text that sits just above its top edge, so it
    // rises and leaves with the words.
    public const string LockupName = "HapticGate lockup";
    public const float LockupWidth = 300f;   // canvas units

    void Awake() { EnsureLockup(); }

    public void EnsureLockup()
    {
        if (transform.Find(LockupName) != null) return;
        var sprite = HapticGateMark.Lockup();
        if (sprite == null) return;
        var go = new GameObject(LockupName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        float aspect = sprite.rect.height / sprite.rect.width;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(LockupWidth, LockupWidth * aspect);
        rt.anchoredPosition = new Vector2(0f, 10f);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    // Update is called once per frame
    void Update () {
        transform.Translate(new Vector2(0, 1) * Time.deltaTime * 40);
    }
}
