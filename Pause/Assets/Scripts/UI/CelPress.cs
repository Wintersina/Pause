using UnityEngine;
using UnityEngine.EventSystems;

// Cartoon press feedback for any UI button, timed like a flipbook rather
// than tweened: on press it snaps to a squashed key pose; on release it pops
// past full size for one tick, dips, and settles (24 fps ticks, held poses).
// Unscaled time, so it works on the frozen pause and death screens.
public class CelPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public RectTransform target;

    // (scale x, scale y, hold ticks): squash wide-and-short, then hold.
    static readonly Vector3[] PressPoses = { new Vector3(1.06f, .86f, 1), new Vector3(.95f, .92f, 99) };
    // Release: stretch tall past full size, small dip, settle.
    static readonly Vector3[] ReleasePoses = { new Vector3(.94f, 1.1f, 1), new Vector3(1.04f, .98f, 2), new Vector3(1f, 1f, 0) };

    Vector3[] poses;
    int pose;
    float clock;
    bool held;

    public bool Held { get { return held; } }

    public static CelPress AddTo(GameObject go, RectTransform target = null)
    {
        var press = go.GetComponent<CelPress>();
        if (press == null) press = go.AddComponent<CelPress>();
        press.target = target != null ? target : go.transform as RectTransform;
        return press;
    }

    public void OnPointerDown(PointerEventData e) { held = true; Play(PressPoses); }
    public void OnPointerUp(PointerEventData e) { if (held) { held = false; Play(ReleasePoses); } }
    public void OnPointerExit(PointerEventData e) { if (held) { held = false; Play(ReleasePoses); } }

    void Play(Vector3[] sequence)
    {
        poses = sequence;
        pose = 0;
        clock = 0f;
        Apply();
    }

    void OnDisable()
    {
        held = false;
        poses = null;
        if (target != null) target.localScale = Vector3.one;
    }

    void Update()
    {
        if (poses == null) return;
        clock += Time.unscaledDeltaTime;
        while (poses != null && poses[pose].z > 0 && clock >= poses[pose].z / 24f)
        {
            clock -= poses[pose].z / 24f;
            if (pose + 1 >= poses.Length) { poses = null; return; }
            pose++;
            Apply();
        }
    }

    void Apply()
    {
        if (target == null || poses == null) return;
        var p = poses[pose];
        target.localScale = new Vector3(p.x, p.y, 1f);
        if (p.z <= 0f) poses = null;   // a settle pose is the end
    }
}
