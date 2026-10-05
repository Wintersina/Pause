using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Home-screen traffic, part 4: the player's fingers push ships around.
//
// While a finger is down on an empty part of the home screen, ships near it
// are shoved away by a soft field around the touch: strongest at the
// finger, fading to nothing at PushRadius, and harder the faster the finger
// moves (a swipe also drags them along a little). The shove is a velocity
// that decays (PushDecay), so a ship drifts off its line and its own
// steering brings it back onto its path. A shoved ship can still bump into
// another per the usual crash rules.
//
// A touch that starts on a button or any other interactive UI (an
// EventSystem raycast that lands on something with a pointer handler, or
// on another overlay such as the codex panel) pushes nothing for its whole
// life, wherever it is dragged. Every finger is its own pointer
// (multitouch); without a touchscreen the left mouse button is one.
//
// Read-only on input: nothing here consumes, uses or blocks an event, so the
// menu's taps and the back button work exactly as before (the home screen
// has no other gestures). Step itself never reads input -- Update feeds the
// pointers in first -- so the tests drive pointers through FeedPointer.
public partial class TitleScreenTraffic
{
    public const int MaxPointers = 10;
    public const float PushRadius = 1.6f;        // world units, at the front layer
    public const float PushBase = 2.4f;          // shove (u/s per s) at the finger, still
    public const float PushPerSpeed = .9f;       // ... plus this per u/s of finger speed
    public const float PushDrag = .35f;          // a swipe drags ships along this much
    public const float PushDecay = 2.6f;         // per second
    public const float PushMax = 6f;             // u/s

    // Depth: the far layers feel the finger less.
    static readonly float[] PushLayer = { .55f, .8f, 1f };

    struct Pointer
    {
        public bool down, ui;
        public int id;
        public Vector2 world, vel;
    }

    readonly Pointer[] pointers = new Pointer[MaxPointers];
    PointerEventData uiEvent;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
    Selectable[] selectables = new Selectable[0];
    Canvas homeCanvas;
    readonly bool[] seenThisFrame = new bool[MaxPointers];

    public int PointersDown
    {
        get { int n = 0; for (int i = 0; i < MaxPointers; i++) if (pointers[i].down) n++; return n; }
    }

    public bool PointerOnUi(int slot) => pointers[slot].down && pointers[slot].ui;
    public int Shoves { get; private set; }

    void BuildTouch()
    {
        for (int i = 0; i < MaxPointers; i++) pointers[i].id = int.MinValue;
    }

    void FindTouchUi()
    {
        selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        homeCanvas = menuPanel != null ? menuPanel.GetComponentInParent<Canvas>() : null;
        if (homeCanvas != null) homeCanvas = homeCanvas.rootCanvas;
    }

    // Update: the real fingers (or the mouse) into the pointer slots.
    void ReadInput(float dt)
    {
        for (int i = 0; i < MaxPointers; i++) seenThisFrame[i] = false;
        if (Input.touchSupported && Input.touchCount > 0)
        {
            int n = Mathf.Min(Input.touchCount, MaxPointers);
            for (int i = 0; i < n; i++)
            {
                var t = Input.GetTouch(i);
                bool down = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
                int slot = SlotFor(t.fingerId);
                if (slot < 0) continue;
                seenThisFrame[slot] = true;
                FeedPointer(slot, t.fingerId, down, t.position, dt);
            }
        }
        else if (!Input.touchSupported || Input.touchCount == 0)
        {
            bool mouse = !Input.touchSupported && Input.GetMouseButton(0);
            if (mouse || pointers[0].id == PointerInputModule.kMouseLeftId)
            {
                seenThisFrame[0] = true;
                FeedPointer(0, PointerInputModule.kMouseLeftId, mouse, Input.mousePosition, dt);
            }
        }
        // a finger that vanished without an Ended phase is up
        for (int i = 0; i < MaxPointers; i++)
            if (!seenThisFrame[i] && pointers[i].down) { pointers[i].down = false; pointers[i].id = int.MinValue; }
    }

    int SlotFor(int fingerId)
    {
        for (int i = 0; i < MaxPointers; i++) if (pointers[i].id == fingerId) return i;
        for (int i = 0; i < MaxPointers; i++) if (!pointers[i].down) return i;
        return -1;
    }

    // One pointer's state this frame, in screen pixels. Tests call this.
    public void FeedPointer(int slot, int pointerId, bool down, Vector2 screen, float dt)
    {
        if (slot < 0 || slot >= MaxPointers) return;
        ref var p = ref pointers[slot];
        if (!down)
        {
            p.down = false;
            p.id = int.MinValue;
            p.vel = Vector2.zero;
            return;
        }
        Vector2 world = ScreenPointToWorld(screen);
        if (!p.down)
        {
            p.down = true;
            p.id = pointerId;
            p.world = world;
            p.vel = Vector2.zero;
            // decided once, when the touch lands
            p.ui = StartsOnUi(pointerId, screen);
            return;
        }
        if (dt > 0f)
        {
            Vector2 v = (world - p.world) / dt;
            p.vel = Vector2.Lerp(p.vel, v, 1f - Mathf.Exp(-18f * dt));
        }
        p.world = world;
    }

    Vector2 ScreenPointToWorld(Vector2 screen)
    {
        int w = screenW > 0 ? screenW : Screen.width, h = screenH > 0 ? screenH : Screen.height;
        if (w <= 0 || h <= 0) return view.center;
        return ScreenToWorld(screen, w, h);
    }

    // True when a touch at `screen` lands on interactive UI: a control, or
    // any overlay other than the home menu's own canvas (the codex panel).
    public bool StartsOnUi(int pointerId, Vector2 screen)
    {
        var es = EventSystem.current;
        if (es == null) es = Object.FindFirstObjectByType<EventSystem>();
        if (es != null)
        {
            if (uiEvent == null) uiEvent = new PointerEventData(es);
            uiEvent.Reset();
            uiEvent.pointerId = pointerId;
            uiEvent.position = screen;
            uiHits.Clear();
            es.RaycastAll(uiEvent, uiHits);
            for (int i = 0; i < uiHits.Count; i++)
            {
                var go = uiHits[i].gameObject;
                if (go == null) continue;
                if (go.layer == 2 && go.GetComponentInParent<TitleScreenTraffic>() != null) continue;
                if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) != null ||
                    ExecuteEvents.GetEventHandler<IPointerDownHandler>(go) != null ||
                    ExecuteEvents.GetEventHandler<IDragHandler>(go) != null ||
                    ExecuteEvents.GetEventHandler<IScrollHandler>(go) != null)
                    return true;
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && homeCanvas != null && canvas.rootCanvas != homeCanvas) return true;
            }
        }
        // no raycaster running (edit mode, or before the EventSystem wakes):
        // the controls' own rects
        if (selectables.Length == 0) FindTouchUi();
        for (int i = 0; i < selectables.Length; i++)
        {
            var sel = selectables[i];
            if (sel == null || !sel.isActiveAndEnabled) continue;
            var rt = sel.transform as RectTransform;
            var canvas = sel.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam)) return true;
        }
        return false;
    }

    void ApplyPush(float dt)
    {
        for (int k = 0; k < MaxPointers; k++)
        {
            ref var p = ref pointers[k];
            if (!p.down || p.ui) continue;
            float speed = Mathf.Min(p.vel.magnitude, 14f);
            for (int i = 0; i < pool.Length; i++)
            {
                var f = pool[i];
                if (!f.active || !Pushable(f)) continue;
                float layerK = PushLayer[(int)f.layer];
                float reach = PushRadius * (.75f + .25f * layerK);
                Vector2 d = f.pos - p.world;
                float dist = d.magnitude;
                if (dist >= reach) continue;
                float fall = 1f - dist / reach;
                fall *= fall;
                Vector2 away = dist > 1e-3f ? d / dist : (p.vel.sqrMagnitude > 1e-4f ? new Vector2(-p.vel.y, p.vel.x).normalized : Vector2.up);
                float strength = (PushBase + PushPerSpeed * speed) * fall * layerK;
                f.push += (away * strength + p.vel * (PushDrag * fall * layerK)) * (dt * 6f);
                float m = f.push.magnitude;
                if (m > PushMax) f.push *= PushMax / m;
                Shoves++;
            }
        }
        float decay = Mathf.Exp(-PushDecay * dt);
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (!f.active) continue;
            if (f.push.sqrMagnitude < 1e-6f) { f.push = Vector2.zero; continue; }
            if (!Pushable(f)) { f.push = Vector2.zero; continue; }
            f.pos += f.push * dt;
            if (f.state == State.Dizzy) f.tumble += f.push * dt;
            f.push *= decay;
        }
    }

    // Zips run on fixed launch curves and a diving ship is committed.
    static bool Pushable(Flyer f)
    {
        return f.state != State.ZipIn && f.state != State.ZipOut && f.state != State.Plunge;
    }
}
