using UnityEngine;

// Ninja and UFO turn continuously while advancing. Their movement is read as
// spinning through air/space, so they get trailing wind ribbons rather than a
// rocket plume that would rotate around the hull.
public class ShipSpinWind : MonoBehaviour
{
    public float degreesPerSecond = 300f;
    Transform wind;
    SpriteRenderer[] ribbons;
    float seed;

    void Start()
    {
        seed = Random.value * 6.28f;
        var hull = GetComponent<SpriteRenderer>();
        var texture = Resources.Load<Texture2D>("Prefabs/Vfx/vfx_trace_01");
        if (texture == null) return;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                                   new Vector2(.5f, .5f), 100f);
        wind = new GameObject("~SpinWind").transform;
        wind.SetParent(transform, false);
        ribbons = new SpriteRenderer[2];
        float size = hull != null && hull.sprite != null
            ? Mathf.Max(hull.sprite.bounds.size.x, hull.sprite.bounds.size.y) * 1.55f : 1f;
        for (int i = 0; i < ribbons.Length; i++)
        {
            var go = new GameObject("Ribbon" + i, typeof(SpriteRenderer));
            go.transform.SetParent(wind, false);
            go.transform.localScale = new Vector3(size * .5f, size, 1f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            ribbons[i] = go.GetComponent<SpriteRenderer>();
            ribbons[i].sprite = sprite;
            ribbons[i].color = i == 0
                ? new Color(.5f, .9f, 1f, .36f)
                : new Color(.75f, .45f, 1f, .26f);
            ribbons[i].sortingOrder = (hull != null ? hull.sortingOrder : 0) - 1;
        }
    }

    void Update()
    {
        bool docked = GetComponent<DockShipIdleAnimator>() != null;
        bool flying = !docked && !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (flying) transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
        if (wind == null || ribbons == null) return;

        // Counter-rotate the ribbons so they remain a flowing wake around the
        // spinning hull instead of looking bolted onto it.
        wind.localRotation = Quaternion.Euler(0f, 0f, -transform.localEulerAngles.z +
            Time.unscaledTime * 45f + seed);
        foreach (var ribbon in ribbons)
            if (ribbon != null) ribbon.enabled = flying;
    }
}
