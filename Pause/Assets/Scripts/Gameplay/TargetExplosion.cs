using UnityEngine;

// The blast a hazard makes when a player weapon destroys it -- the
// ultimate's homing shots and ramming it under the boost shield.
//
// Flat cartoon, 80s-anime timing: a one-frame pinch, a white flash, a red
// flash, a held hard-edged star burst, then ink-outlined smoke puffs that
// drift out and break up while debris tumbles clear (Art/Weapons/src~,
// Explosions.png). Hostile craft burst magenta-violet with metal plates,
// asteroids in rock browns with sodium fire, mines bigger and hotter, and the
// per-world casts (EnemyRoster) their own: ice shatters cold, Verdant's
// organics burst in spore green, Ember's magma in hot sodium. Under
// it, a flash star and a shockwave ring in the firing weapon's own energy
// colour tie the hit to the ship that made it. Three sizes from the target's
// bounds, and a small camera kick on the bigger two.
//
// Runs on gameplay time (Delta): frozen while the world is paused, but kept
// moving at a readable rate through the ultimate's deep slow motion.
public static class TargetExplosion
{
    // Row order in Weapons/Explosions.png (Art/Weapons/src~ EXPLOSION_ROWS).
    public enum Kind { Metal = 0, Rock = 1, Mine = 2, Ice = 3, Spore = 4, Magma = 5 }
    public enum Size { Small, Medium, Large }

    public static Kind KindFor(string tag, string name)
    {
        if (tag == "Astr") return Kind.Rock;
        if (!string.IsNullOrEmpty(name) && name.ToLowerInvariant().Contains("mine")) return Kind.Mine;
        return Kind.Metal; // enemy craft, aliens, chasers
    }

    public static Kind KindFor(GameObject target)
    {
        if (target == null) return Kind.Metal;
        // Roster enemies name their own variant (EnemyRoster: def.explosion).
        var def = EnemyIdentity.Of(target);
        if (def != null) return def.explosion;
        return KindFor(target.CompareTag("Astr") ? "Astr" : target.tag, target.name);
    }

    public static Size SizeFor(float worldExtent)
    {
        if (worldExtent < .5f) return Size.Small;
        if (worldExtent < 1f) return Size.Medium;
        return Size.Large;
    }

    public static Size SizeFor(GameObject target)
    {
        if (target == null) return Size.Medium;
        var def = EnemyIdentity.Of(target);
        if (def != null) return def.explosionSize;
        var r = target.GetComponentInChildren<Renderer>();
        if (r == null) return Size.Medium;
        Vector3 s = r.bounds.size;
        return SizeFor(Mathf.Max(s.x, s.y));
    }

    public static float WorldSizeFor(Size size, Kind kind)
    {
        float s = size == Size.Small ? .85f : size == Size.Medium ? 1.25f : 1.8f;
        return kind == Kind.Mine ? s * 1.15f : s;
    }

    static float HoldFor(Size size) => size == Size.Large ? 1.25f : size == Size.Medium ? 1.1f : 1f;
    static float KickFor(Size size) => size == Size.Large ? .055f : size == Size.Medium ? .028f : 0f;

    public static void Spawn(GameObject target, int ship)
    {
        if (target == null) return;
        RailBombAnimator.Burst(target);   // a rail mine flashes its burst frame first (no-op otherwise)
        Spawn(target.transform.position, KindFor(target), SizeFor(target), ship);
    }

    public static void Spawn(Vector3 at, Kind kind, Size size, int ship)
    {
        float world = WorldSizeFor(size, kind);
        float hold = HoldFor(size);
        Color energy = WeaponStyleTable.For(ship).energy;
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Flash, at, world * 1.15f, ship, kind, energy, hold, 64);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Ring, at, world * 1.35f, ship, kind, energy, hold, 65);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Explosion, at, world, ship, kind, Color.white, hold, 66);
        CameraKick.Kick(KickFor(size));
    }

    // Gameplay clock for explosions: scaled time, so a paused world (time
    // scale 0) freezes them mid-blast, but never slower than half speed
    // through the ultimate's cinematic slow motion, where a raw 0.06x would
    // leave blasts hanging for seconds.
    public static float Delta()
    {
        // The death crash plays its blasts over the frozen world.
        if (DeathCrash.Animating) return DeathCrash.FrameDt;
        if (Time.timeScale <= 0f) return 0f;
        float dt = Time.deltaTime;
        if (ShipPowerController.CinematicClearActive) dt = Mathf.Max(dt, Time.unscaledDeltaTime * .5f);
        return dt;
    }

    // The same rule the hazard movers use for whether the world is moving.
    public static bool WorldScrolling =>
        !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0) && Time.timeScale > 0f;
}

// A subtle camera kick for big blasts. Offsets the camera from wherever it
// is (it undoes its own previous offset first), decays on unscaled time so
// slow motion can't stretch it out, and holds still while the world is
// paused -- no shake over a frozen screen.
public class CameraKick : MonoBehaviour
{
    public const float MaxAmplitude = .07f;
    float amplitude;
    float phase;
    Vector3 applied;

    public float Amplitude => amplitude;

    public static void Kick(float amount)
    {
        if (amount <= 0f || Time.timeScale <= 0f || !Application.isPlaying) return;
        var cam = Camera.main;
        if (cam == null) return;
        var kick = cam.GetComponent<CameraKick>();
        if (kick == null) kick = cam.gameObject.AddComponent<CameraKick>();
        kick.amplitude = Mathf.Min(MaxAmplitude, Mathf.Max(kick.amplitude, amount));
    }

    void LateUpdate()
    {
        transform.position -= applied;
        applied = Vector3.zero;
        if (amplitude <= 0f || Time.timeScale <= 0f) return;
        float dt = Time.unscaledDeltaTime;
        amplitude *= Mathf.Exp(-16f * dt);
        if (amplitude < .002f) { amplitude = 0f; return; }
        phase += dt;
        applied = new Vector3(Mathf.Sin(phase * 71f), Mathf.Cos(phase * 53f) * .6f, 0f) * amplitude;
        transform.position += applied;
    }

    void OnDisable()
    {
        transform.position -= applied;
        applied = Vector3.zero;
    }
}
