using UnityEngine;

// The blast a hazard makes when a player weapon destroys it -- the
// ultimate's homing shots and ramming it under the boost shield.
//
// Explosions v2 (Art/Weapons/ExplosionsV2~/src~, Explosions.png): soft,
// compact neon-pixel bursts with no outlines and no red -- a hot core
// that swells, breaks into glowing debris and fades. Hostile craft burst
// magenta-violet with metal plates, asteroids in rock browns with sodium
// fire, mines bigger and hotter, and the per-world casts (EnemyRoster,
// elites and bosses by world) their own: ice shatters cold, Verdant's
// organics burst in spore green, Ember in magma. Under it, a flash and a
// shockwave ring in the firing weapon's own energy colour (faint) tie the
// hit to the ship that made it. Three sizes from the target's bounds, and a
// small camera kick on the bigger two. The v1 comic atlas is kept for
// rollback in Art/Weapons/ExplosionsV1~.
//
// LOUDNESS: one master knob, Intensity (below), scales the world size,
// hold, camera kick and overlay alpha of every target explosion, the death
// crash's blasts and the death combo's rings. And when many bursts overlap
// (death combo / death crash chains) everything past MaxConcurrentBursts
// plays fainter and without its flash so the screen never walls off.
//
// Runs on gameplay time (Delta): frozen while the world is paused, but kept
// moving at a readable rate through the ultimate's deep slow motion.
public static class TargetExplosion
{
    // Row order in Weapons/Explosions.png (Art/Weapons/src~ EXPLOSION_ROWS).
    public enum Kind { Metal = 0, Rock = 1, Mine = 2, Ice = 3, Spore = 4, Magma = 5 }
    public enum Size { Small, Medium, Large }

    // ---- loudness -------------------------------------------------------
    // Master explosion loudness. 1 = the tuned default (compact: ~0.72x the
    // v1 sizes, ~0.65-0.8 s, half the old kick, overlays at 0.7 alpha).
    // < 1 shrinks everything proportionally, > 1 grows it: world size,
    // camera kick and flash/ring alpha scale linearly, the hold by
    // sqrt(Intensity) (so a quiet blast still reads, a loud one doesn't hang).
    // Clamped to [0.25, 3].
    public static float Intensity = 1f;

    public const float SizeK = .72f;          // world size vs the v1 tuning (.85/1.25/1.8)
    public const float KickK = .5f;           // camera kick vs the v1 tuning (.028/.055)
    public const float MaxKick = .03f;        // kick ceiling at Intensity 1
    public const float OverlayAlpha = .7f;    // flash star + shockwave ring alpha at Intensity 1
    public const int MaxConcurrentBursts = 8; // explosion bodies on screen before crowding kicks in
    public const float CrowdAlpha = .55f;     // body alpha of a crowded burst (it also skips its flash)

    public static float I => Mathf.Clamp(Intensity, .25f, 3f);
    public static float SizeScale => SizeK * I;
    public static float HoldScale => Mathf.Sqrt(I);
    public static float OverlayAlphaNow => Mathf.Min(1f, OverlayAlpha * I);

    // The per-world material: Space ships burst as metal, Frost as ice,
    // Verdant as spore, Ember as magma (EnemyPalette's theme table).
    public static Kind KindForWorld(string world)
    {
        if (string.IsNullOrEmpty(world)) return Kind.Metal;
        switch (world.ToLowerInvariant())
        {
            case "frost": return Kind.Ice;
            case "verdant": return Kind.Spore;
            case "ember": return Kind.Magma;
            default: return Kind.Metal;
        }
    }

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
        var elite = target.GetComponent<EliteShip>();
        if (elite != null && elite.Def != null) return KindForWorld(elite.Def.world);
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
        // (a rock drawn small or large blasts at its size: HazardSize.Blast)
        if (def != null) return HazardSize.Blast(def, EnemyIdentity.ScaleOf(target));
        var r = target.GetComponentInChildren<Renderer>();
        if (r == null) return Size.Medium;
        Vector3 s = r.bounds.size;
        return SizeFor(Mathf.Max(s.x, s.y));
    }

    // Small / Medium / Large = .61 / .90 / 1.30 world units at Intensity 1
    // (mines 15% bigger); proportional to the target, so big ones read bigger.
    public static float WorldSizeFor(Size size, Kind kind)
    {
        float s = size == Size.Small ? .85f : size == Size.Medium ? 1.25f : 1.8f;
        s *= SizeScale;
        return kind == Kind.Mine ? s * 1.15f : s;
    }

    // Flipbook hold multiplier: the burst's 20 ticks at 24 fps (.83 s) times
    // this = .65 / .72 / .80 s at Intensity 1.
    public static float HoldFor(Size size) =>
        (size == Size.Large ? .96f : size == Size.Medium ? .865f : .78f) * HoldScale;

    public static float SecondsFor(Size size) => WeaponArt.Seconds(WeaponArt.ExplosionTicks) * HoldFor(size);

    public static float KickFor(Size size)
    {
        float k = (size == Size.Large ? .055f : size == Size.Medium ? .028f : 0f) * KickK * I;
        return Mathf.Min(k, MaxKick * I);
    }

    // Explosion bodies playing right now (cheap: walks the flipbook pool).
    public static int ActiveBursts => WeaponFx.ActiveOf(FlipbookFx.Mode.Explosion);
    public static bool Crowded => ActiveBursts >= MaxConcurrentBursts;

    public static void Spawn(GameObject target, int ship)
    {
        if (target == null) return;
        RailBombAnimator.Burst(target);   // a rail mine flashes its burst frame first (no-op otherwise)
        var size = SizeFor(target);
        // Sometimes it breaks into spinning pieces of itself instead, over a
        // blast a size smaller (EnemySplit).
        if (EnemySplit.TrySplit(target, size)) size = EnemySplit.Smaller(size);
        EnemyDeathFlipbook.Spawn(target);
        Spawn(target.transform.position, KindFor(target), size, ship);
    }

    public static void Spawn(Vector3 at, Kind kind, Size size, int ship)
    {
        Burst(at, kind, WorldSizeFor(size, kind), ship, HoldFor(size), true, 1.15f, 1.35f);
        CameraKick.Kick(KickFor(size));
    }

    // The shared blast: tinted flash + ring + material body, `world` units
    // across. Past MaxConcurrentBursts it plays fainter and skips its flash.
    public static void Burst(Vector3 at, Kind kind, float world, int ship, float hold, bool flash,
                             float flashK, float ringK)
    {
        bool crowded = Crowded;
        float overlay = OverlayAlphaNow * (crowded ? CrowdAlpha : 1f);
        Color energy = WeaponStyleTable.For(ship).energy;
        energy.a *= overlay;
        if (flash && !crowded) WeaponFx.Flipbook().Play(FlipbookFx.Mode.Flash, at, world * flashK, ship, kind, energy, hold, 64);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Ring, at, world * ringK, ship, kind, energy, hold, 65);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Explosion, at, world, ship, kind,
                                 new Color(1f, 1f, 1f, crowded ? CrowdAlpha : 1f), hold, 66);
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
