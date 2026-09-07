using UnityEngine;

// Animates each hull's own exhaust without changing its engine tint or mount.
public class DockLaunchFlame : MonoBehaviour
{
    Vector3 baseScale;
    SpriteRenderer rendererRef;
    float seed;
    Color baseColor;

    void Awake() { Refresh(); }

    public void Refresh()
    {
        baseScale = transform.localScale;
        rendererRef = GetComponent<SpriteRenderer>();
        baseColor = rendererRef != null ? rendererRef.color : Color.white;
        seed = Random.value * 6.283185f;
    }

    void LateUpdate()
    {
        float wave = 1f + Mathf.Sin(Time.unscaledTime * 20f + seed) * 0.17f;
        transform.localScale = new Vector3(baseScale.x * (0.90f + wave * 0.10f),
                                           baseScale.y * wave,
                                           baseScale.z);
        if (rendererRef != null)
        {
            float heat = 0.83f + Mathf.Sin(Time.unscaledTime * 15f + seed) * 0.12f;
            rendererRef.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * heat);
        }
    }
}
