using UnityEngine;

// Steps an engine plume through ShipExhaust's tail-light flipbook (24 fps
// ticks, on 2s with a 1-tick smear). Drawn frames replace the old single
// painted flame; ShipThruster / DockLaunchFlame still set the plume's size.
//
// Unscaled time, like the thruster's flicker: when the world freezes the
// engine gutters on as a pilot light instead of turning into a still image.
public class ShipFlameFlipbook : MonoBehaviour
{
    SpriteRenderer target;
    float offset;

    public static ShipFlameFlipbook Attach(SpriteRenderer renderer, int nozzle)
    {
        if (renderer == null) return null;
        var book = renderer.GetComponent<ShipFlameFlipbook>();
        if (book == null) book = renderer.gameObject.AddComponent<ShipFlameFlipbook>();
        book.target = renderer;
        // twin plumes run out of step, so the pair never pulses as one
        book.offset = nozzle * 5f + Random.value * 3f;
        book.Apply();
        return book;
    }

    void LateUpdate() { Apply(); }

    void Apply()
    {
        if (target == null) target = GetComponent<SpriteRenderer>();
        if (target == null) return;
        var frame = ShipExhaust.Frame(ShipExhaust.FrameAt(Time.unscaledTime * ShipHullArt.TicksPerSecond + offset));
        if (frame != null) target.sprite = frame;
    }
}
