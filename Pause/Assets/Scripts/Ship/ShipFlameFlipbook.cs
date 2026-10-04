using UnityEngine;

// Steps an engine plume through its ship's exhaust flipbook
// (ShipExhaustStyle: 24 fps ticks, on 2s with a 1-tick smear). Drawn frames
// replace the old single painted flame; ShipThruster / DockLaunchFlame still
// set the plume's size.
//
// Scaled time, like the hull's own idle flipbook (ShipHullAnimator): when
// the world freezes (timeScale 0, the game's pause) the plume holds its
// drawing. The thruster still shrinks it to a pilot light.
//
// The plume wears the ship's skin (ExhaustRemap): re-applied whenever a
// skin changes (ExhaustRemap.Version), paused or not.
public class ShipFlameFlipbook : MonoBehaviour
{
    SpriteRenderer target;
    int shipId = ShipId.Starter;
    bool boost;
    float ticks;
    int shown = -1;
    int skinVersion = -1;

    public int ShipIdShown { get { return shipId; } }
    public int FrameShown { get { return shown; } }

    // Re-reads the skin on the next step (a caller swapped the material).
    public void RefreshSkin() { skinVersion = -1; Apply(); }

    // The boost drawings (longer, hotter) instead of the cruise ones.
    public bool Boost
    {
        get { return boost; }
        set { if (boost != value) { boost = value; shown = -1; Apply(); } }
    }

    public static ShipFlameFlipbook Attach(SpriteRenderer renderer, int nozzle, int shipId, bool boost = false)
    {
        if (renderer == null) return null;
        var book = renderer.GetComponent<ShipFlameFlipbook>();
        if (book == null) book = renderer.gameObject.AddComponent<ShipFlameFlipbook>();
        book.target = renderer;
        book.shipId = shipId;
        book.boost = boost;
        // twin plumes run out of step, so the pair never pulses as one
        book.ticks = nozzle * 5f + Random.value * 3f;
        book.shown = -1;
        book.skinVersion = -1;
        book.Apply();
        return book;
    }

    void LateUpdate() { Step(Time.deltaTime); }

    // Advances by `deltaTime` seconds of (scaled) game time; zero holds.
    public void Step(float deltaTime)
    {
        if (deltaTime > 0f) ticks += deltaTime * ShipHullArt.TicksPerSecond;
        Apply();
    }

    void Apply()
    {
        if (target == null) target = GetComponent<SpriteRenderer>();
        if (target == null) return;
        if (skinVersion != ExhaustRemap.Version)
        {
            skinVersion = ExhaustRemap.Version;
            ExhaustRemap.Apply(target, shipId);
        }
        int frame = ShipExhaust.FrameAt(shipId, ticks);
        if (frame == shown) return;
        var sprite = ShipExhaust.Frame(shipId, boost, frame);
        if (sprite == null) return;
        target.sprite = sprite;
        shown = frame;
    }
}
