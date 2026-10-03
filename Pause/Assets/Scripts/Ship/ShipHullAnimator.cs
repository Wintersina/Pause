using UnityEngine;

// Plays a flying hull's flipbook (ShipHullArt): the idle loop, a bank pose
// while the ship is being steered left or right, and a one-shot hit flash
// when it takes damage. Plain C#, owned by lifeControler, which already sets
// the hull sprite every frame for the damage state.
//
//   idle   ShipHullArt's tick table on SCALED time. When the world is frozen
//          (timeScale 0, the game's pause) the hull freezes with it -- the
//          ship holds its pose like everything else on screen.
//   bank   from the hull's own movement: movePlayer moves the transform
//          directly, so a frame's x step is the steering. A step too big to
//          be a drag is a teleport and doesn't lean. The pose is held a few
//          ticks so a drag reads as one lean, not a flicker. Spinning craft
//          (Ninja, UFO) rotate, so they never bank.
//   hit    when the damage level goes up: a 3-tick flat BONE/RED impact
//          frame (docs/art-style.md "Impact frames"), on unscaled time so a
//          hit that also slows the world still flashes crisply.
public class ShipHullAnimator
{
    public const float FlashSeconds = 3f / ShipHullArt.TicksPerSecond;
    public const float BankHoldTicks = 4f;
    public const float BankSpeed = .5f;        // world units / s of steering that reads as a lean
    public const float TeleportStep = .75f;    // a single-frame jump at least this big is a teleport

    public readonly int id;
    readonly Transform ship;
    readonly bool canBank;

    float ticks;
    int lastLife;
    float flashLeft;
    int bank;
    float bankHold;
    float lastX;
    bool hasX;

    public ShipHullAnimator(int id, Transform ship, int life)
    {
        this.id = id;
        this.ship = ship;
        canBank = !ShipExhaust.UsesWind(id);
        lastLife = life;
    }

    public float Ticks { get { return ticks; } }
    public int Bank { get { return bank; } }
    public bool Flashing { get { return flashLeft > 0f; } }

    // One frame. deltaTime is scaled (Time.deltaTime), unscaledDeltaTime is
    // real time; life is collisionDetection.lifeCounter (0 when not live).
    public void Step(float deltaTime, float unscaledDeltaTime, int life)
    {
        if (life > lastLife) flashLeft = FlashSeconds;
        lastLife = life;
        if (flashLeft > 0f) flashLeft -= Mathf.Max(0f, unscaledDeltaTime);

        // Frozen world: no idle frames advance, the lean holds.
        if (deltaTime <= 0f) return;
        ticks += deltaTime * ShipHullArt.TicksPerSecond;

        if (!canBank || ship == null) return;
        float x = ship.position.x;
        if (hasX)
        {
            float dx = x - lastX;
            if (Mathf.Abs(dx) < TeleportStep && Mathf.Abs(dx) / deltaTime >= BankSpeed)
            {
                bank = dx < 0f ? -1 : 1;
                bankHold = BankHoldTicks;
            }
            else
            {
                bankHold -= deltaTime * ShipHullArt.TicksPerSecond;
                if (bankHold <= 0f) bank = 0;
            }
        }
        lastX = x;
        hasX = true;
    }

    // The sheet column to show now.
    public int Column
    {
        get
        {
            if (flashLeft > 0f) return ShipHullArt.Hit;
            if (bank < 0) return ShipHullArt.BankLeft;
            if (bank > 0) return ShipHullArt.BankRight;
            return ShipHullArt.IdleDrawingAt(ticks);
        }
    }

    public Sprite Current(int damageState)
    {
        return ShipHullArt.Get(id, damageState, Column);
    }
}
