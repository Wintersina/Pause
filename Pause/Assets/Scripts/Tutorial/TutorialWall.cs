using System.Collections.Generic;
using UnityEngine;

// The pause lesson's wall: one row of real aliens (EnemyFactory, like every
// roster enemy) across the whole lane with no gap the ship fits through. It
// rushes in from just above the top edge, fast, stops on a hold line above
// the ship and waits there; the only way over is the PAUSE: lift the finger
// (time freezes) and touch ABOVE the wall -- the pause-jump
// (movePlayerInTut.Arrive) lands the ship on the far side without ever
// crossing the aliens. Then the wall is released and drives on down and away.
//
// Like every tutorial mover it only moves while the world is moving
// (finger down, or no pauses left), on scaled time, so letting go freezes it.
//
// The row's layout is a pure function (PlanRow) and the no-gap rule a second
// one (HasDodgeGap) that TutorialWallTest runs on every device's lane.
public class TutorialWall : MonoBehaviour
{
    public enum Phase { Entering, Holding, Released, Gone }

    // ---- Tuning (world units, seconds) ----
    // Entry speed: fast, like the dust and atoms (TutorialStarDrift.Speed).
    public const float Speed = 7.5f;
    // Wall stops this share of the view's height above its bottom edge.
    public const float HoldFraction = .62f;
    // Spawn this far above the camera's top edge (just out of view).
    public const float SpawnAboveTop = 1.2f;
    // Gone once this far below the bottom edge.
    public const float LeaveBelow = 1.2f;
    // The widest gap between two aliens, as a share of the ship's width
    // (< 1: the ship never fits through).
    public const float GapShare = .45f;
    // The ship must be this far below the hold line before the wall comes in.
    public const float ShipClearance = 1.0f;
    // Aliens a row can have at most (pool of one row, never more).
    public const int MaxPieces = 14;
    const float FallbackPieceWidth = .8f, FallbackPieceHalfHeight = .4f, FallbackShipWidth = .6f;

    public static TutorialWall Live { get; private set; }

    readonly List<Transform> pieces = new List<Transform>(MaxPieces);
    int total;
    float pieceWidth, pieceHalfHeight, shipHalfHeight;
    float y;
    int seenJumps;
    Phase phase = Phase.Entering;
    bool crossed, broken;

    public Phase CurrentPhase { get { return phase; } }
    public bool Crossed { get { return crossed; } }
    public bool Broken { get { return broken; } }
    public float Y { get { return y; } }
    public int Total { get { return total; } }
    public float PieceWidth { get { return pieceWidth; } }
    public float PieceHalfHeight { get { return pieceHalfHeight; } }
    public IReadOnlyList<Transform> Pieces { get { return pieces; } }
    // Aliens of the row still standing.
    public int Alive
    {
        get { int n = 0; foreach (var t in pieces) if (t != null) n++; return n; }
    }
    // The wall is low enough that the pilot can still touch ABOVE it (a jump
    // that lands clear of it fits under the top of the ship's reach). The
    // freeze step only counts a lifted finger from here on, so the lesson
    // never asks for a jump there is no room for.
    public bool Jumpable
    {
        get
        {
            return phase != Phase.Gone && phase != Phase.Released
                   && y + CrossMargin(pieceHalfHeight, shipHalfHeight) + .3f <= ShipReach.ClampY(1000f);
        }
    }

    // Where the jump should land: just clear of the wall on its far side
    // (above it), within the ship's reach. Anchored on the wall, so it holds
    // on every aspect ratio.
    public float JumpTargetY
    {
        get { return Mathf.Min(y + CrossMargin(pieceHalfHeight, shipHalfHeight) + .15f, ShipReach.ClampY(1000f)); }
    }

    // The frame the wall is in view and holding / coming (for the freeze step).
    public bool OnScreen
    {
        get
        {
            float bottom, top;
            TutorialAtomDrift.View(out bottom, out top);
            return phase != Phase.Gone && y < top && y > bottom;
        }
    }

    // ---- Pure layout ----

    // The y the wall stops at.
    public static float HoldY(float bottom, float top) { return bottom + HoldFraction * (top - bottom); }

    // How far above the wall's middle a jump must land to count as over it:
    // clear of the aliens' tops, the blink's blast (TeleportFx.BlastRadius)
    // and the ship's own hull, so the landing never erases the wall.
    public static float CrossMargin(float pieceHalfHeight, float shipHalfHeight)
    {
        return Mathf.Max(pieceHalfHeight + TeleportFx.BlastRadius + .1f, pieceHalfHeight + 2f * shipHalfHeight + .2f);
    }

    // The row: how many aliens of width `pieceWidth` it takes to close a lane
    // whose ship centre ranges over [-reachHalf, reachHalf], with no gap at
    // GapShare of the ship's width `shipWidth` or wider. First alien's outer
    // edge sits on -reachHalf, the last on +reachHalf. Returns the count and
    // the distance between centres.
    public static int PlanRow(float reachHalf, float pieceWidth, float shipWidth, out float spacing, out float firstX)
    {
        float gap = Mathf.Max(.05f, shipWidth * GapShare);
        float span = Mathf.Max(0f, 2f * reachHalf - pieceWidth);   // first centre to last centre
        int n = Mathf.Clamp(Mathf.CeilToInt(span / (pieceWidth + gap)) + 1, 2, MaxPieces);
        spacing = span / (n - 1);
        firstX = -reachHalf + pieceWidth * .5f;
        return n;
    }

    // True if a ship `shipWidth` wide, its centre anywhere in
    // [-reachHalf, reachHalf], fits between the aliens (centres `xs`, each
    // `pieceWidth` wide) or between an end alien and the edge of its reach.
    public static bool HasDodgeGap(IList<float> xs, float pieceWidth, float reachHalf, float shipWidth)
    {
        if (xs == null || xs.Count == 0) return true;
        var sorted = new List<float>(xs);
        sorted.Sort();
        float half = pieceWidth * .5f, sh = shipWidth * .5f;
        // left of the first alien: the ship at its leftmost centre
        if (sorted[0] - half >= -reachHalf + sh) return true;
        if (sorted[sorted.Count - 1] + half <= reachHalf - sh) return true;
        for (int i = 0; i + 1 < sorted.Count; i++)
            if ((sorted[i + 1] - half) - (sorted[i] + half) >= shipWidth) return true;
        return false;
    }

    // ---- Spawn ----

    // Builds the row above the top edge. Null: no roster alien / no room.
    public static TutorialWall Spawn(Transform ship)
    {
        Clear();
        var def = EnemyRoster.Find(TutorialEnemy.DefKey) ?? EnemyRoster.One(0, EnemyRole.Alien);
        if (def == null) return null;

        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        float spawnY = top + SpawnAboveTop;

        var holder = new GameObject("~TutorialWall");
        var wall = holder.AddComponent<TutorialWall>();
        var first = EnemyFactory.Create(def, new Vector3(0f, spawnY, 0f), Quaternion.identity);
        if (first == null) { Destroy(holder); return null; }
        float w = FallbackPieceWidth, hh = FallbackPieceHalfHeight;
        var col = first.GetComponentInChildren<Collider2D>();
        if (col != null || ship != null) Physics2D.SyncTransforms();   // (auto sync is off: bounds follow the last sync)
        if (col != null) { w = Mathf.Max(.2f, col.bounds.size.x); hh = Mathf.Max(.1f, col.bounds.extents.y); }
        wall.pieceWidth = w;
        wall.pieceHalfHeight = hh;

        float shipW = FallbackShipWidth, shipHH = FallbackShipWidth * .5f;
        var shipCol = ship != null ? ship.GetComponent<Collider2D>() : null;
        if (shipCol != null) { shipW = Mathf.Max(.2f, shipCol.bounds.size.x); shipHH = shipCol.bounds.extents.y; }
        wall.shipHalfHeight = shipHH;

        float spacing, firstX;
        int n = PlanRow(ShipReach.HalfWidth, w, shipW, out spacing, out firstX);
        for (int i = 0; i < n; i++)
        {
            var go = i == 0 ? first : EnemyFactory.Create(def, new Vector3(0f, spawnY, 0f), Quaternion.identity);
            if (go == null) continue;
            TutorialEnemy.Prepare(go);
            go.transform.position = new Vector3(firstX + i * spacing, spawnY, 0f);
            go.transform.SetParent(null);
            wall.pieces.Add(go.transform);
        }
        wall.total = wall.pieces.Count;
        wall.y = spawnY;
        wall.seenJumps = movePlayerInTut.Jumps;
        Live = wall;
        return wall;
    }

    public static void Clear()
    {
        var w = Live;
        Live = null;
        if (w == null) return;
        w.Remove();
    }

    void Remove()
    {
        phase = Phase.Gone;
        foreach (var t in pieces)
        {
            if (t == null) continue;
            ClearTarget.Release(t.gameObject);
            if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
        }
        pieces.Clear();
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    // ---- Motion ----

    static bool WorldMoving()
    {
        return (TouchInput.IsPressed || score.pauseCounter <= 0) && !buttonClicks.playerDied;
    }

    void Update()
    {
        Step(Time.deltaTime, WorldMoving());
    }

    // One frame; dt is scaled time. Judges the jump and the damage even on a
    // frozen frame. Returns false once the wall is gone.
    public bool Step(float dt, bool worldMoving)
    {
        if (phase == Phase.Gone) return false;
        Judge();

        if (worldMoving && dt > 0f)
        {
            float bottom, top;
            TutorialAtomDrift.View(out bottom, out top);
            var ship = ShipTransform();
            float hold = HoldY(bottom, top);
            if (phase == Phase.Entering || phase == Phase.Holding)
            {
                // it waits above the screen until the ship is clear below the hold line
                bool shipClear = ship == null || ship.position.y <= hold - ShipClearance;
                if (phase == Phase.Entering && (y <= top + .2f || shipClear))
                {
                    y = Mathf.Max(hold, y - Speed * dt);
                    if (y <= hold + .0001f) phase = Phase.Holding;
                }
            }
            else if (phase == Phase.Released)
            {
                y -= Speed * dt;
                if (y < bottom - LeaveBelow) { Remove(); if (Live == this) Live = null; return false; }
            }
            Place();
        }
        return true;
    }

    void Place()
    {
        foreach (var t in pieces)
        {
            if (t == null) continue;
            var p = t.position;
            p.y = y;
            t.position = p;
        }
    }

    Transform shipCache;
    Transform ShipTransform()
    {
        if (shipCache == null)
        {
            var m = Object.FindFirstObjectByType<movePlayerInTut>();
            shipCache = m != null ? m.transform : null;
        }
        return shipCache;
    }

    // Did the ship just get over (a jump from below that lands clear above),
    // or break the wall up (ram / blink onto it / drag through it)?
    void Judge()
    {
        if (crossed || broken) return;
        if (Alive < total) { broken = true; return; }
        var ship = ShipTransform();
        if (movePlayerInTut.Jumps != seenJumps)
        {
            seenJumps = movePlayerInTut.Jumps;
            bool from = movePlayerInTut.LastJumpFrom.y < y - .2f;
            bool over = movePlayerInTut.LastJumpTo.y > y + CrossMargin(pieceHalfHeight, shipHalfHeight);
            if (from && over && ship != null && ship.position.y > y)
            {
                crossed = true;
                phase = Phase.Released;
                return;
            }
        }
        // on the far side without a jump (dragged through the row): not the lesson
        if (ship != null && y < 20f && ship.position.y > y + pieceHalfHeight + shipHalfHeight && phase != Phase.Entering)
            broken = true;
    }
}
