#if UNITY_EDITOR
using UnityEngine;

// Editor-only bot for the marketing capture: holds a virtual finger down
// (TouchInput's editor override) and dodges what is falling, drifting toward
// pickups when the lane is clear. The ship's hearts are topped up when it is
// one hit from dying, so a stray hit never ends a take. Lives on the capture
// runner only; nothing here exists outside the editor.
public class MarketingAutopilot : MonoBehaviour
{
    public bool Enabled
    {
        get { return enabled_; }
        set
        {
            enabled_ = value;
            if (value)
            {
                TouchInput.PressedOverride = () => !frozen;
                TouchInput.PositionOverride = FingerScreenPos;
            }
            else
            {
                TouchInput.PressedOverride = null;
                TouchInput.PositionOverride = null;
                shipFound = false;
            }
        }
    }
    bool enabled_;

    const float MaxSpeed = 4.6f;         // world units per second, sideways
    const float VerticalSpeed = 2.2f;
    const float Margin = .62f;           // clearance around a hazard (ship half width + slack)

    // ---- pause moves: release (time freezes), hold, touch somewhere else (a teleport, costs a pause) ----
    public enum Move { Escape, OntoEnemy, OntoRed }
    // (what, move): "freeze" at the release, "hold" ~0.7 s into the frozen frame, "teleport" at the touch, "after" 0.35 s later
    public System.Action<string, Move> OnMoment;
    public float HoldSeconds = 1.3f;
    bool frozen;
    readonly System.Collections.Generic.Queue<Move> queue = new System.Collections.Generic.Queue<Move>();
    bool hasPending { get { return queue.Count > 0; } }
    Move pending { get { return queue.Peek(); } }
    float pendingWait;
    float frozenFor;
    bool heldFired, afterPending;
    float afterAt;
    Move active;
    Vector2 teleportTarget;
    public bool PauseMovePending { get { return hasPending || frozen; } }

    public bool NextMoveIsRed { get { return queue.Count > 0 && queue.Peek() == Move.OntoRed; } }

    public void CancelPending() { queue.Clear(); }

    public void QueueMove(Move m)
    {
        if (queue.Count == 0) pendingWait = 0f;
        queue.Enqueue(m);
    }

    // a red (pause) atom on screen, or null
    Collider2D FindRedAtom()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || !c.CompareTag("pickUp")) continue;
            if (!PrefabName.Is(c.gameObject, "pauseAtom")) continue;
            var vp = cam.WorldToViewportPoint(c.transform.position);
            if (vp.x > .05f && vp.x < .95f && vp.y > .12f && vp.y < .80f) return c;
        }
        return null;
    }

    // A freshly spawned red atom enters at the top and drifts down slowly: bring it into view.
    public void PlaceRedAtomInView()
    {
        var cam = Camera.main;
        if (cam == null) return;
        foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (c == null || !c.CompareTag("pickUp") || !PrefabName.Is(c.gameObject, "pauseAtom")) continue;
            var vp = cam.WorldToViewportPoint(c.transform.position);
            if (vp.y <= .8f) continue;
            var w = cam.ViewportToWorldPoint(new Vector3(.5f, .72f, Mathf.Abs(cam.transform.position.z)));
            c.transform.position = new Vector3(c.transform.position.x, w.y, c.transform.position.z);
        }
    }

    Collider2D NearestEnemy(float shipY)
    {
        Collider2D best = null; float bestD = float.MaxValue;
        Physics2D.SyncTransforms();   // (auto sync is off)
        int n = Physics2D.OverlapBoxNonAlloc(new Vector2(0f, shipY + 3f), new Vector2(7.5f, 8f), 0f, buffer);
        for (int i = 0; i < n; i++)
        {
            var c = buffer[i];
            if (c == null || !c.enabled || !c.CompareTag("Enimey")) continue;
            var b = c.bounds;
            if (b.center.y < shipY + 1f || b.center.y > shipY + 5f) continue;
            float d = Mathf.Abs(b.center.x - curX) + (b.center.y - shipY) * .5f;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    bool HostileAheadInLane(float shipY)
    {
        Physics2D.SyncTransforms();   // (auto sync is off)
        int n = Physics2D.OverlapBoxNonAlloc(new Vector2(curX, shipY + 1.9f), new Vector2(1.5f, 2.2f), 0f, buffer);
        for (int i = 0; i < n; i++)
        {
            var c = buffer[i];
            if (c != null && c.enabled && (c.CompareTag("Enimey") || c.CompareTag("Astr")) && c.bounds.min.y > shipY + .5f) return true;
        }
        return false;
    }

    void PauseMoveStep(float dt)
    {
        if (afterPending && Time.unscaledTime >= afterAt) { afterPending = false; if (OnMoment != null) OnMoment("after", active); }
        float shipY = ship != null ? ship.position.y : curY;
        if (frozen)
        {
            frozenFor += dt;
            if (!heldFired && frozenFor >= .7f) { heldFired = true; if (OnMoment != null) OnMoment("hold", active); }
            if (frozenFor >= HoldSeconds)
            {
                frozen = false;   // touch: the ship teleports to the finger
                curX = teleportTarget.x; curY = teleportTarget.y;
                cachedFrame = -1;
                afterPending = true; afterAt = Time.unscaledTime + .35f;
                if (OnMoment != null) OnMoment("teleport", active);
            }
            return;
        }
        if (!hasPending || BossEncounter.Running || WorldEntry.Active || !shipFound) return;
        pendingWait += dt;
        Move m = pending;
        // approach: red atom or an enemy must be on screen; an Escape waits for something dangerous in the lane
        if (m == Move.OntoRed && FindRedAtom() == null) { if (pendingWait < 6f) return; m = Move.Escape; }
        if (m == Move.OntoEnemy && NearestEnemy(shipY) == null) { if (pendingWait < 6f) return; m = Move.Escape; }
        if (m == Move.Escape && !HostileAheadInLane(shipY) && pendingWait < 5f) return;
        if (m == Move.OntoEnemy && pendingWait < 1f) return;
        if (m == Move.OntoRed && pendingWait < .3f) return;

        // freeze
        queue.Dequeue(); pendingWait = 0f; active = m;
        if (score.pauseCounter < 1) score.pauseCounter = 3;
        frozen = true; frozenFor = 0f; heldFired = false;
        teleportTarget = PickTeleportTarget(m, shipY);
        if (OnMoment != null) OnMoment("freeze", active);
    }

    Vector2 PickTeleportTarget(Move m, float shipY)
    {
        if (m == Move.OntoRed)
        {
            var red = FindRedAtom();
            if (red != null) return new Vector2(ShipReach.ClampX(red.bounds.center.x), ShipReach.ClampY(red.bounds.center.y));
        }
        if (m == Move.OntoEnemy)
        {
            var e = NearestEnemy(shipY);
            if (e != null) return new Vector2(ShipReach.ClampX(e.bounds.center.x), ShipReach.ClampY(e.bounds.center.y));
        }
        // Escape: the calmest lane, at least a lane away
        float bestX = curX, best = float.MaxValue;
        for (float x = -3f; x <= 3.001f; x += .2f)
        {
            float cx = ShipReach.ClampX(x);
            float cost = ThreatCost(cx, shipY) - Mathf.Min(2f, Mathf.Abs(cx - curX)) * 3f;
            if (cost < best) { best = cost; bestX = cx; }
        }
        return new Vector2(bestX, ShipReach.ClampY(-2.5f));
    }

    Transform ship;
    bool shipFound;
    float curX, curY, wanderT;
    int cachedFrame = -1;
    Vector2 cachedScreen;
    readonly Collider2D[] buffer = new Collider2D[160];

    void OnDestroy() { Enabled = false; }

    void Update()
    {
        if (!enabled_) return;
        PauseMoveStep(Mathf.Min(Time.unscaledDeltaTime, .1f));
        // never lose a take to a death: refill hearts when one hit from the end
        if (collisionDetection.MAXLIFE > 1 && collisionDetection.lifeCounter >= collisionDetection.MAXLIFE - 1)
            collisionDetection.lifeCounter = 0;
    }

    Vector2 FingerScreenPos()
    {
        if (cachedFrame == Time.frameCount) return cachedScreen;
        cachedFrame = Time.frameCount;
        var cam = Camera.main;
        if (cam == null) { cachedScreen = new Vector2(Screen.width * .5f, Screen.height * .3f); return cachedScreen; }

        if (!shipFound || ship == null)
        {
            var mp = Object.FindFirstObjectByType<movePlayer>();
            if (mp != null)
            {
                ship = mp.transform;
                curX = ship.position.x;
                curY = ship.position.y;
                shipFound = true;
            }
        }
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        float shipY = shipFound && ship != null ? ship.position.y : curY;
        wanderT += dt;

        float bestX = curX;
        float best = float.MaxValue;
        for (float x = -3f; x <= 3.001f; x += .2f)
        {
            float cx = ShipReach.ClampX(x);
            float cost = Mathf.Abs(cx - curX) * .35f + Mathf.Abs(cx - Mathf.Sin(wanderT * .5f) * 1.6f) * .05f;
            if (!(hasPending && pending != Move.OntoRed)) cost += ThreatCost(cx, shipY);   // approaching danger on purpose
            if (cost < best) { best = cost; bestX = cx; }
        }
        curX = Mathf.MoveTowards(curX, bestX, MaxSpeed * dt);
        float wantY = ShipReach.ClampY(-2.5f + Mathf.Sin(wanderT * .8f) * .5f);
        curY = Mathf.MoveTowards(curY, wantY, VerticalSpeed * dt);

        // the hull sits FingerOffset above the finger
        var world = new Vector3(curX, curY - ShipReach.FingerOffset, 0f);
        var sp = cam.WorldToScreenPoint(world);
        cachedScreen = new Vector2(sp.x, sp.y);
        return cachedScreen;
    }

    float ThreatCost(float x, float shipY)
    {
        float cost = 0f;
        var centre = new Vector2(x, shipY + 2f);
        Physics2D.SyncTransforms();   // (auto sync is off)
        int n = Physics2D.OverlapBoxNonAlloc(new Vector2(0f, shipY + 2.2f), new Vector2(7.5f, 6.4f), 0f, buffer);
        for (int i = 0; i < n; i++)
        {
            var c = buffer[i];
            if (c == null || !c.enabled) continue;
            if (ship != null && c.transform.IsChildOf(ship)) continue;
            bool hostile = c.CompareTag("Enimey") || c.CompareTag("Astr");
            bool pickup = !hostile && c.CompareTag("pickUp");
            if (!hostile && !pickup) continue;
            var b = c.bounds;
            if (b.max.y < shipY - .9f) continue;
            float gap = Mathf.Abs(x - b.center.x) - (b.extents.x + Margin);
            float ahead = Mathf.Max(0f, b.min.y - shipY);
            if (hostile)
            {
                if (gap < 0f) cost += 40f / (1f + ahead * .8f);
                else if (gap < .35f) cost += 3f / (1f + ahead);
            }
            else if (!NextMoveIsRed && gap < -Margin * .5f && ahead < 3f && ahead > .2f)
            {
                cost -= 1.2f;   // a pickup in this lane
            }
        }
        return cost;
    }
}
#endif
