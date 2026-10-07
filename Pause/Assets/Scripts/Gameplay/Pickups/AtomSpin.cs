using UnityEngine;

// Pickup atoms rotate slowly while crossing the field. It applies to the
// authored red/blue prefabs and to the generated green healing atom.
public class AtomSpin : MonoBehaviour
{
    [Range(-180f, 180f)] public float degreesPerSecond = 32f;

    void Update()
    {
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }

    void LateUpdate()
    {
        // Atoms are spawned in the play lane, but their visible lobes used to
        // extend through the side rails on narrow layouts. Clamp their full
        // rendered width inside the safe lane after any movement scripts run.
        var sprite = GetComponent<SpriteRenderer>();
        float halfWidth = sprite != null ? sprite.bounds.extents.x : 0.18f;
        float limit = Mathf.Max(0f, 2.35f - halfWidth);
        var p = transform.position;
        p.x = Mathf.Clamp(p.x, -limit, limit);
        transform.position = p;
    }

    public static float ClampAtomX(float x, float renderedHalfWidth)
    {
        return Mathf.Clamp(x, -(2.35f - renderedHalfWidth), 2.35f - renderedHalfWidth);
    }

    public static GameObject AddTo(GameObject atom)
    {
        if (atom != null && atom.GetComponent<AtomSpin>() == null) atom.AddComponent<AtomSpin>();
        return atom;
    }
}
