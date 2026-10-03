using UnityEngine;
using UnityEngine.EventSystems;

// Press feedback for the Flight Complete buttons: a quick squash while held
// and a springy release. Unscaled time, since the world is frozen at
// timeScale 0 behind the death panel. Does nothing once it has settled.
public class DeathPanelPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public RectTransform target;
    const float PressedScale = .93f;

    bool held;
    float scale = 1f, velocity;

    // 0 at rest, 1 fully pressed -- DeathPanelView brightens the glow with it.
    public float Pressed01 { get { return Mathf.Clamp01((1f - scale) / (1f - PressedScale)); } }

    public void OnPointerDown(PointerEventData e) { held = true; }
    public void OnPointerUp(PointerEventData e) { held = false; }
    public void OnPointerExit(PointerEventData e) { held = false; }

    void OnDisable()
    {
        held = false; scale = 1f; velocity = 0f;
        if (target != null) target.localScale = Vector3.one;
    }

    void Update()
    {
        float goal = held ? PressedScale : 1f;
        if (Mathf.Abs(scale - goal) < .0005f && Mathf.Abs(velocity) < .0005f)
        {
            if (scale != goal) { scale = goal; Apply(); }
            return;
        }
        // Critically-ish damped spring: snappy in, a touch of bounce out.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        float stiffness = held ? 900f : 520f, damping = held ? 60f : 26f;
        velocity += ((goal - scale) * stiffness - velocity * damping) * dt;
        scale += velocity * dt;
        Apply();
    }

    void Apply()
    {
        if (target != null) target.localScale = new Vector3(scale, scale, 1f);
    }
}
