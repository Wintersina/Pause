using UnityEngine;

// The rail mine's flipbook: the current world's row of the neon rail-mine
// atlas (RailMineArt, through EnemyRoster / EnemyArt). It idles dormant with a
// blink of the waking core, loops waking -> charging (the arming tell) while
// the ship is close, and shows its burst as the hit flash and, through
// Burst(), for the first beat of its detonation. Mounting and movement remain
// the responsibility of RailMineMount and the straight-line scroller.
public class RailBombAnimator : EnemyFlipbook
{
    // How long the burst frame holds over the explosion: one beat of the old
    // RailBombAnimator's 6 fps.
    public const int BurstTicks = 4;

    protected override void Awake()
    {
        // An old mine Animator would overwrite the flipbook's sprite.
        var legacy = GetComponent<Animator>();
        if (legacy != null) legacy.enabled = false;

        base.Awake();
        if (!HasFrames)
        {
            var def = EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Mine);
            if (def != null) Init(def);
        }
        if (sr != null) sr.sortingOrder = 12;
    }

    // Leaves the mine's burst frame where the mine was, facing the same wall,
    // for BurstTicks: the first frame of its explosion (the blast itself
    // draws over it). Call it just before the mine is destroyed. Returns the
    // burst object, or null if the target isn't a rail mine.
    public static GameObject Burst(GameObject mine)
    {
        if (mine == null) return null;
        var def = EnemyIdentity.Of(mine);
        if (def == null || def.role != EnemyRole.Mine)
        {
            if (mine.GetComponent<RailBombAnimator>() == null) return null;
            def = EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Mine);
            if (def == null) return null;
        }
        // Its blast catches whatever is near a beat later (friendly fire).
        FriendlyFire.MineBlast(mine);
        var sprite = RailMineArt.Frame(def.world, RailMineArt.Burst);
        if (sprite == null) return null;

        var go = new GameObject("RailMineBurst");
        go.transform.SetPositionAndRotation(mine.transform.position, mine.transform.rotation);
        go.transform.localScale = mine.transform.lossyScale;
        var burstSr = go.AddComponent<SpriteRenderer>();
        burstSr.sprite = sprite;
        var mineSr = mine.GetComponent<SpriteRenderer>();
        burstSr.flipX = mineSr != null && mineSr.flipX;
        burstSr.sortingOrder = 12;
        go.AddComponent<RailMineBurst>().seconds = BurstTicks * TickSeconds;
        return go;
    }
}

// The detonating mine's burst frame: holds on gameplay time (frozen while
// the world is paused, like the explosion over it), scrolling with the board
// the way the mine did (moveItemEnmInStrightLine's rule, without becoming a
// ClearTarget), then removes itself.
public class RailMineBurst : MonoBehaviour
{
    public float seconds = RailBombAnimator.BurstTicks * EnemyFlipbook.TickSeconds;

    void Update()
    {
        if (TargetExplosion.WorldScrolling)
            transform.Translate(Vector2.down * moveBackGround.speed * Time.deltaTime * 30f, Space.World);
        seconds -= TargetExplosion.Delta();
        if (seconds <= 0f) Destroy(gameObject);
    }
}
