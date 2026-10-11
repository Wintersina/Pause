using UnityEngine;
using UnityEngine.SceneManagement;

// The ship's lives as small hearts orbiting it, on every hull: one heart per
// life the ship flies with (ShipLives -- the hull's 2 on the starter, 3 on
// the cheap ships, 4 on the dear ones, 5 on Gold Warden, plus up to +2 from
// its colours and +2 for owning every skin: SkinHearts; up to ShipLives.Most,
// 9). A crowd flies a wider orbit (HeartOrbit.CrowdGrow).
//
// The orbit, the shield-dart and the crumble are HeartOrbit's (shared with
// the elite ships' hearts, EliteHearts). This is the player's side of it:
// each ship has its own flavour (ShipHeartStyles); the orbit is measured off
// the hull's sprite; it speeds through the bottom (where the player's thumb
// is -- a hull-length under the ship) and past the companion gun's resting
// spot (UltimateGun.LocalRestEnvelope), so a heart crosses those but never
// lingers there; at a screen edge the orbit flattens against it rather than
// leave the screen.
//
// When the ship is hit (collisionDetection calls Impact with where the hit
// came from), the best-placed heart darts to the impact point between the
// hull and the hit, flashes as a shield burst and crumbles into pixel
// shards. A heal pops a heart back into orbit. All of it runs on scaled
// time, so it holds still while the world is frozen -- except through the
// death crash, whose clock (DeathCrash.FrameDt) plays the last heart's
// shield over the frozen world.
//
// For effects round the hull that keep clear of the hearts wherever they
// are on their orbits (ShipDamageFx's spray hides droplets over them):
// HeartBounds, HeartPositions / HeartPosition / ShownCount.
//
// Placed in world space each frame, after the ship elements have moved.
[DefaultExecutionOrder(50)]
public class ShipLivesIndicator : HeartOrbit
{
    // The hit the next lost heart shields against (collisionDetection).
    static bool impactPending;
    static Vector3 impactAt;

    int shipId;

    // collisionDetection: the ship was hit by something at `worldPoint`; the
    // heart it costs shields against it.
    public static void Impact(Vector3 worldPoint)
    {
        impactPending = true;
        impactAt = worldPoint;
    }

    // One heart per life of this run (ShipLives.RunMax).
    public override void BuildHearts()
    {
        BuildHearts(ShipLives.RunMax);
    }

    protected override int MaxHearts { get { return ShipLives.Most; } }

    // Hearts left: the lives built, less the hits taken (ShipLives).
    protected override int RemainingHearts()
    {
        return (hearts != null ? hearts.Length : 0) - collisionDetection.lifeCounter;
    }

    protected override bool TakeImpact(out Vector3 at)
    {
        at = impactAt;
        bool had = impactPending;
        impactPending = false;
        return had;
    }

    protected override Sprite HeartSprite() { return Resources.Load<Sprite>("Vfx/lifeHeart"); }

    protected override void ResolveStyle(out HeartStyle s, out OrbitStyle o)
    {
        // The flown ship's own id (its name), not the raw saved selection:
        // an unowned selection flies the starter.
        shipId = ShipId.Of(gameObject, ShipId.Equipped());
        styleKey = shipId;
        s = ShipHeartStyles.For(shipId);
        o = ShipHeartStyles.Orbit(s);
    }

    // The hearts are sized against the ship's full size, never the moment
    // they are built: every run begins with the portal arrival, which flies
    // the ship out of the vortex from 6% of its size, and the hearts are
    // built in that first frame. Sized against the tiny ship they came out
    // ~16x too big once it grew. They follow the ship's scale as its
    // children, so they grow out of the portal with it.
    protected override float HullScale()
    {
        float now = base.HullScale();
        var arrival = PortalArrival.Live;
        if (arrival != null && PortalArrival.Active && arrival.Ship == transform)
        {
            Vector3 rest = arrival.RestScale, local = transform.localScale;
            float cur = Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.y));
            float full = Mathf.Max(Mathf.Abs(rest.x), Mathf.Abs(rest.y));
            if (cur > 1e-5f) now *= full / cur;
        }
        return now;
    }

    protected override Bounds HullBounds() { return ShipUiSlots.HullBounds(transform, shipId); }
    protected override Rect ClampRect() { return ShipUiSlots.ScreenRect(null); }
    protected override bool AvoidThumb { get { return true; } }
    protected override UltimateGun FindGun() { return GetComponentInChildren<UltimateGun>(true); }
    protected override Bounds GunRestBounds(UltimateGun g) { return ShipUiSlots.GunToWorld(transform, shipId, g.LocalRestEnvelope()); }

    protected override void LateUpdate()
    {
        // The death crash: the last heart's shield and crumble play over
        // the frozen world on the crash's clock.
        float dt = DeathCrash.Animating ? DeathCrash.FrameDt : Time.deltaTime;
        Place(Time.unscaledDeltaTime, dt);
        StepBreaks(dt);
    }
}

// Attaches the indicator to the player ship once it exists, matching the
// same poll-until-found pattern used by ShipThruster/ShipPowerController.
public class ShipLivesIndicatorAttach : MonoBehaviour
{
    float giveUp = 6f;

    void Update()
    {
        // gameS1's ship (movePlayer) or the tutorial's (movePlayerInTut): the
        // tutorial flies with orbiting hearts too.
        GameObject player = null;
        var main = Object.FindFirstObjectByType<movePlayer>();
        if (main != null) player = main.gameObject;
        else
        {
            var tut = Object.FindFirstObjectByType<movePlayerInTut>();
            if (tut != null) player = tut.gameObject;
        }
        if (player != null)
        {
            if (player.GetComponent<ShipLivesIndicator>() == null)
                player.AddComponent<ShipLivesIndicator>();
            Destroy(gameObject);
            return;
        }

        giveUp -= Time.unscaledDeltaTime;
        if (giveUp <= 0f) Destroy(gameObject);
    }
}

public static class ShipLivesIndicatorBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        new GameObject("~ShipLivesIndicatorAttach").AddComponent<ShipLivesIndicatorAttach>();
    }
}
