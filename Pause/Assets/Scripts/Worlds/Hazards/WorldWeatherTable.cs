using UnityEngine;

// THE ONE DATA PLACE for world weather and hazards (docs/world-weather-hazards.md sections 3 - 8):
// every world's ambient particles, its hazards, and the rate at which they happen. Worlds are in
// WorldManager.Worlds order (Space, Frost, Verdant, Ember, Tide, Storm, Desert); colours come from WorldCue,
// so pink exists only in Space.
public enum AmbientShape { Dot, Streak, Flake, Leaf, Bubble, Ember, Grain, Edge }   // Edge: a soft gradient bar (glows, wind edges), never an ambient particle

public sealed class AmbientKind
{
    public AmbientShape shape;
    public int count;
    public float size;          // world units, the sprite's long side
    public float alpha;         // <= WorldWeatherTable.AmbientMaxAlpha
    public Color color;
    public Vector2 drift;       // u/s
    public float sway, swayHz;  // sideways wobble (u/s amplitude, Hz)
    public float windLean;      // u/s sideways per unit of live wind (hazard) -- 0: none beyond the field push
    public int artKind = -1;    // the approved ambient sheet's kind (0-3, wx_ambient.png), or -1: the procedural placeholder sprite
}

public sealed class WorldWeatherEntry
{
    public string world;
    public AmbientKind[] ambient;
    public HazardDef[] hazards;
    public float first;                       // earliest first event, seconds into the world
    public Vector2 gap;                       // seconds between events
    public Vector2 bossFirst;                 // seconds into a boss fight when the first event may come
    public WindProfile wind;                  // the always-on wind with its gusts (null: this world has only events, Space)
}

public static class WorldWeatherTable
{
    // ---- global tunables ----
    public const float MinWarning = 1.5f;     // the telegraph is never shorter than this
    public const float RampIn = .8f, RampOut = 1f;
    public const float PlayerGain = .6f, ReturnRate = 2.2f, PlayerCap = 1.0f;   // the ship's finger-offset
    public const float BodyMaxSpeed = 4.5f, ShotMaxSpeed = 7.5f, EliteAccel = 5f, EliteMaxSpeed = 7.5f, PilotShare = .6f;   // the old caps were 2.4 / 4 / 3 / 6: the weather could not push hard enough to read
    public const float MinGapAfterElite = 4f;
    public const float BossLeadSeconds = 14f;
    public const float LoopShorten = .08f, LoopShortenMax = 1.3f;
    public const int MaxFields = 2, AmbientMax = 72;
    public const float AmbientBoost = 1.6f;   // the table's counts x this: the old layer (40 specks) read as nothing
    public const float AmbientMaxAlpha = .6f;
    public const float AmbientWindGain = 1.3f;   // how much a live hazard's field drags the ambient particles

    static TargetMix M(float g, float pull = 1f, float swirl = 1f) { return new TargetMix(g, pull, swirl); }

    // Defaults: the player feels .7 of the field (it is also capped), bodies 1, elites .3, hostile shots 1, the player's shots .5.
    static HazardDef H(string id, int world, HazardShape shape, float strength, float size, float live)
    {
        return new HazardDef
        {
            id = id, world = world, shape = shape, strength = strength, size = size, live = live, warning = 2f,
            player = M(.7f), body = M(1f), elite = M(.3f), hostile = M(1f), mine = M(.5f),
        };
    }

    static AmbientKind A(AmbientShape sh, int n, float size, float alpha, Color c, float dx, float dy, float sway = 0f, float hz = 0f, float lean = 0f)
    {
        return new AmbientKind { shape = sh, count = n, size = size, alpha = alpha, color = c, drift = new Vector2(dx, dy), sway = sway, swayHz = hz, windLean = lean };
    }

    static AmbientKind Art(AmbientKind a, int kind) { a.artKind = kind; return a; }

    static Color Pale(int w, float k = .55f) { return Color.Lerp(WorldCue.Body(w), Color.white, k); }

    public static readonly WorldWeatherEntry[] Entries = Build();

    static WorldWeatherEntry[] Build()
    {
        var space = new WorldWeatherEntry
        {
            // SOLAR FLARES replace the old gravity well / ion wind: a flare is a hot plasma front that crosses the screen
            // sideways after a long, readable warning and SHOVES everything in it along the way it travels.
            world = "Space", first = 24f, gap = new Vector2(48f, 78f), bossFirst = new Vector2(16f, 28f),
            ambient = new[]
            {
                // drifting solar sparks and plasma motes (procedural until Codex's art is wired); pink is allowed here
                Art(A(AmbientShape.Ember, 12, .10f, .5f, new Color(1f, .78f, .42f), .05f, .22f, .06f, .3f, 1.0f), 0),
                Art(A(AmbientShape.Dot, 10, .08f, .45f, new Color(1f, .93f, .8f), .0f, .15f, .05f, .2f, 1.0f), 1),
                Art(A(AmbientShape.Dot, 6, .11f, .4f, WorldCue.Body(0), .0f, .1f, .04f, .15f, 1.0f), 3),   // magenta: Space only
            },
            hazards = new[]
            {
                // the big one: a single wide plasma front crossing in about two seconds; the leading edge is the hard shove
                With(H("solar_flare", 0, HazardShape.Sweep, 6.5f, 3.0f, 0f), h => { h.warning = 2.4f; h.speed = 5.2f; h.push = new Vector2(1f, -.12f); h.shock = .5f; h.capMul = 1.7f; h.stuns = true; h.player = M(.5f); h.body = M(.9f); h.elite = M(.4f); h.hostile = M(1.1f); h.mine = M(.7f); h.artRow = 0; h.artWarnCol = 0; }),
                // a flare train: two narrower fronts a beat apart, each a separate punch with a gap you can step through
                With(H("flare_storm", 0, HazardShape.Sweep, 5.4f, 2.1f, 0f), h => { h.warning = 2.6f; h.speed = 4.2f; h.bands = 2; h.bandGap = 5.4f; h.push = new Vector2(1f, -.1f); h.shock = .55f; h.capMul = 1.6f; h.stuns = true; h.player = M(.45f); h.body = M(.85f); h.elite = M(.4f); h.hostile = M(1.1f); h.mine = M(.7f); h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        var frost = new WorldWeatherEntry
        {
            world = "Frost", first = 30f, gap = new Vector2(60f, 95f), bossFirst = new Vector2(18f, 30f),
            ambient = new[]
            {
                Art(A(AmbientShape.Flake, 16, .16f, .5f, new Color(.94f, .98f, 1f), .06f, -.55f, .18f, .35f, .3f), 0),
                Art(A(AmbientShape.Flake, 14, .09f, .45f, new Color(.82f, .93f, 1f), .08f, -.8f, .12f, .5f, .3f), 1),
                Art(A(AmbientShape.Dot, 6, .06f, .4f, Pale(1, .8f), 0f, -.3f, .1f, .6f), 2),
            },
            hazards = new[]
            {
                // a white wall: sideways drift plus a headwind (up-screen) that stalls shots and bodies
                With(H("blizzard_front", 1, HazardShape.Front, 2.8f, 6f, 0f), h => { h.speed = 2.6f; h.push = new Vector2(1f, .9f); h.hostile = M(1.2f); h.player = M(.6f); h.artRow = 0; h.artWarnCol = 0; }),
                // a squall eddy: mostly spin
                With(H("whiteout_eddy", 1, HazardShape.Swirl, 3.0f, 2f, 10f), h => { h.pull = .15f; h.swirl = 1f; h.speed = .8f; h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        var verdant = new WorldWeatherEntry
        {
            world = "Verdant", first = 30f, gap = new Vector2(58f, 92f), bossFirst = new Vector2(18f, 30f),
            ambient = new[]
            {
                Art(A(AmbientShape.Dot, 16, .07f, .5f, new Color(.96f, .93f, .55f), .05f, .1f, .2f, .3f, .2f), 0),
                Art(A(AmbientShape.Leaf, 8, .15f, .5f, new Color(.55f, .85f, .5f), .1f, -.25f, .3f, .25f, .3f), 1),
                Art(A(AmbientShape.Dot, 8, .06f, .4f, Pale(2, .75f), 0f, .12f, .12f, .4f), 3),
            },
            hazards = new[]
            {
                // a pollen cloud rolls through and gently bends shots
                With(H("spore_gust", 2, HazardShape.Front, 2.2f, 4.5f, 0f), h => { h.speed = 2f; h.push = new Vector2(1f, 0f); h.player = M(.5f); h.artRow = 0; h.artWarnCol = 0; }),
                // warm air column: lifts up-screen
                With(H("vine_updraft", 2, HazardShape.Column, 3.4f, 1.8f, 9f), h => { h.lift = 1f; h.spread = .5f; h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        var ember = new WorldWeatherEntry
        {
            world = "Ember", first = 28f, gap = new Vector2(52f, 85f), bossFirst = new Vector2(16f, 28f),
            ambient = new[]
            {
                Art(A(AmbientShape.Ember, 14, .09f, .55f, new Color(1f, .7f, .28f), .04f, .55f, .1f, .6f, .2f), 0),
                Art(A(AmbientShape.Flake, 12, .1f, .4f, new Color(.5f, .48f, .52f), .05f, .3f, .12f, .3f, .25f), 1),
                Art(A(AmbientShape.Dot, 6, .06f, .5f, new Color(1f, .85f, .5f), 0f, .7f), 2),
            },
            hazards = new[]
            {
                // heat column, pulsing lift
                With(H("ash_updraft", 3, HazardShape.Column, 4.2f, 2.2f, 8f), h => { h.lift = 1f; h.spread = .3f; h.pulseHz = .5f; h.pulseDepth = .5f; h.artRow = 0; h.artWarnCol = 0; }),
                // lava geyser: three hard bursts, outward and up
                With(H("geyser_plume", 3, HazardShape.Column, 6.0f, 1.2f, 6f), h => { h.lift = 1f; h.spread = .9f; h.pulseHz = .5f; h.pulseDepth = 1f; h.player = M(.6f); h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        var tide = new WorldWeatherEntry
        {
            world = "Tide", first = 26f, gap = new Vector2(50f, 80f), bossFirst = new Vector2(16f, 28f),
            ambient = new[]
            {
                Art(A(AmbientShape.Bubble, 14, .12f, .5f, Pale(4, .6f), .02f, .5f, .12f, .5f, .2f), 0),
                Art(A(AmbientShape.Streak, 8, .22f, .35f, Pale(4, .7f), -.4f, -3f, 0f, 0f, .2f), 1),
                Art(A(AmbientShape.Dot, 6, .08f, .4f, Pale(4, .85f), 0f, .2f, .08f, .4f), 3),
            },
            hazards = new[]
            {
                // strong pull and spin
                With(H("whirlpool", 4, HazardShape.Swirl, 4.6f, 2.6f, 11f), h => { h.pull = .7f; h.swirl = .8f; h.speed = .5f; h.artRow = 0; h.artWarnCol = 0; }),
                // a surge sweeps sideways
                With(H("current_surge", 4, HazardShape.Front, 4.4f, 5.5f, 0f), h => { h.speed = 3.4f; h.push = new Vector2(1f, 0f); h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        var storm = new WorldWeatherEntry
        {
            world = "Storm", first = 24f, gap = new Vector2(46f, 75f), bossFirst = new Vector2(14f, 26f),
            ambient = new[]
            {
                Art(A(AmbientShape.Streak, 18, .3f, .35f, new Color(.86f, .85f, 1f), -.9f, -5.5f, 0f, 0f, .5f), 0),
                Art(A(AmbientShape.Streak, 8, .18f, .5f, new Color(.96f, .96f, 1f), -.7f, -6.5f, 0f, 0f, .5f), 1),
                Art(A(AmbientShape.Dot, 6, .12f, .35f, new Color(.7f, .68f, .95f), -.3f, -1.2f, .1f, .5f), 2),
            },
            hazards = new[]
            {
                // a funnel crosses the field: pulls ships, throws bullets outward and round
                With(H("tornado", 5, HazardShape.Swirl, 4.8f, 2f, 12f), h => { h.pull = .6f; h.swirl = 1f; h.speed = 1.1f; h.artRow = 0; h.artWarnCol = 0; h.spinDegrees = 150f; h.hostile = M(1.2f, -.9f, 1.3f); h.mine = M(.5f, -.5f, 1f); }),
                // a charged gust, fast
                With(H("gust_front", 5, HazardShape.Front, 4.4f, 3.8f, 0f), h => { h.speed = 4.2f; h.push = new Vector2(1f, 0f); h.hostile = M(1.2f); h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        var desert = new WorldWeatherEntry
        {
            world = "Desert", first = 22f, gap = new Vector2(44f, 72f), bossFirst = new Vector2(14f, 26f),
            ambient = new[]
            {
                Art(A(AmbientShape.Grain, 20, .06f, .5f, new Color(.98f, .92f, .68f), .9f, -.2f, .1f, .5f, .6f), 0),
                Art(A(AmbientShape.Streak, 8, .2f, .3f, new Color(.95f, .86f, .6f), 1.2f, -.15f, 0f, 0f, .6f), 1),
                Art(A(AmbientShape.Dot, 6, .09f, .4f, new Color(.8f, .66f, .42f), .5f, -.3f, .1f, .3f, .5f), 2),
            },
            hazards = new[]
            {
                // the big wall of sand: the strongest sideways shove, bends bullets
                With(H("sandstorm_front", 6, HazardShape.Front, 5.0f, 7f, 0f), h => { h.speed = 3f; h.push = new Vector2(1f, .2f); h.hostile = M(1.3f); h.player = M(.6f); h.artRow = 0; h.artWarnCol = 0; }),
                // a small fast devil
                With(H("dust_devil", 6, HazardShape.Swirl, 3.8f, 1.5f, 10f), h => { h.pull = .35f; h.swirl = 1f; h.speed = 1.6f; h.artRow = 1; h.artWarnCol = 2; }),
            },
        };
        // the always-on wind (WeatherWind): breeze, shear, turbulence and telegraphed gusts. Space has none: its weather is the flares.
        frost.wind = W("Frost gale", bx: .45f, by: 0f, wobble: .6f, gust: 3.4f, gx: 1f, gy: .5f, every: 7f, every2: 12f, shear: .7f, len: 6f, rate: .8f, turb: .15f);
        verdant.wind = W("Pollen drift", bx: .3f, by: .25f, wobble: .5f, gust: 2.6f, gx: 1f, gy: .9f, every: 8f, every2: 13f, shear: .9f, len: 5f, rate: .6f, turb: .1f);
        ember.wind = W("Thermals", bx: 0f, by: .75f, wobble: .8f, gust: 3.2f, gx: .35f, gy: 1.6f, every: 7f, every2: 11f, shear: 1.3f, len: 3.2f, rate: .5f, turb: .2f, byX: true);
        tide.wind = W("Currents", bx: .65f, by: 0f, wobble: .35f, gust: 3.8f, gx: 1f, gy: 0f, every: 8f, every2: 12f, shear: 1.4f, len: 7f, rate: .9f, turb: .1f);
        storm.wind = W("Charged wind", bx: -.5f, by: -.3f, wobble: .5f, gust: 4.4f, gx: 1f, gy: -.6f, every: 6f, every2: 10f, live: 3f, shear: 1.9f, len: 3.8f, rate: 1.8f, turb: .35f);
        desert.wind = W("Crosswind", bx: 1f, by: 0f, wobble: .3f, gust: 4.6f, gx: 1f, gy: .15f, every: 7f, every2: 11f, shear: .5f, len: 8f, rate: .5f, turb: .3f);
        return new[] { space, frost, verdant, ember, tide, storm, desert };
    }

    static WindProfile W(string name, float bx, float by, float wobble, float gust, float gx, float gy, float every, float every2, float shear, float len, float rate, float turb, float live = 3.4f, bool byX = false)
    {
        return new WindProfile
        {
            name = name, breeze = new Vector2(bx, by), wobble = wobble, gust = gust, gustDir = new Vector2(gx, gy), every = new Vector2(every, every2),
            live = live, shear = shear, shearLen = len, shearRate = rate, turb = turb, shearByX = byX,
        };
    }

    static HazardDef With(HazardDef d, System.Action<HazardDef> f) { f(d); return d; }

    public static WorldWeatherEntry ForWorld(int world)
    {
        return world >= 0 && world < Entries.Length ? Entries[world] : null;
    }

    // Every problem with the data, or an empty string (the test and a dev check read it).
    public static string Validate()
    {
        var sb = new System.Text.StringBuilder();
        if (Entries.Length != WorldManager.Worlds.Length) sb.Append("entry count ").Append(Entries.Length).Append(" != worlds ").Append(WorldManager.Worlds.Length).Append("; ");
        for (int i = 0; i < Entries.Length; i++)
        {
            var e = Entries[i];
            if (i < WorldManager.Worlds.Length && e.world != WorldManager.Worlds[i].displayName) sb.Append(e.world).Append(" is out of world order; ");
            if (e.ambient == null || e.ambient.Length == 0) sb.Append(e.world).Append(" has no ambient; ");
            if (e.hazards == null || e.hazards.Length == 0) sb.Append(e.world).Append(" has no hazard; ");
            if (e.first < 8f) sb.Append(e.world).Append(" first event inside the calm start; ");
            if (e.gap.x < 30f || e.gap.y < e.gap.x) sb.Append(e.world).Append(" gap not rare; ");
            if (e.bossFirst.x < 10f || e.bossFirst.y < e.bossFirst.x) sb.Append(e.world).Append(" boss-fight first bad; ");
            if (e.wind != null && (e.wind.warn < MinWarning - .2f || e.wind.gust > 6f || e.wind.every.x < 5f)) sb.Append(e.world).Append(" wind unfair; ");
            if (i > 0 && e.wind == null) sb.Append(e.world).Append(" has no wind; ");
            int total = 0;
            if (e.ambient != null)
                foreach (var a in e.ambient)
                {
                    total += a.count;
                    if (a.alpha > AmbientMaxAlpha) sb.Append(e.world).Append(" ambient too opaque; ");
                    if (i > 0 && WorldCue.IsPink(a.color)) sb.Append(e.world).Append(" ambient pink; ");
                }
            if (Mathf.CeilToInt(total * AmbientBoost) > AmbientMax) sb.Append(e.world).Append(" ambient over budget; ");
            if (e.hazards != null)
                foreach (var h in e.hazards)
                {
                    if (h.world != i) sb.Append(h.id).Append(" wrong world; ");
                    if (h.warning < MinWarning) sb.Append(h.id).Append(" warning too short; ");
                    if (h.strength <= 0f || h.size <= 0f) sb.Append(h.id).Append(" empty; ");
                    if (h.elite.gain > .5f) sb.Append(h.id).Append(" elites pushed too hard; ");
                    if ((h.shape == HazardShape.Swirl || h.shape == HazardShape.Column) && h.live < 4f) sb.Append(h.id).Append(" too short; ");
                    if ((h.shape == HazardShape.Front || h.shape == HazardShape.Sweep) && h.speed < 1f) sb.Append(h.id).Append(" front too slow; ");
                    if (h.shape == HazardShape.Sweep && (h.bands < 1 || (h.bands > 1 && h.bandGap < h.size * 1.4f))) sb.Append(h.id).Append(" bands overlap; ");
                }
        }
        return sb.ToString();
    }
}
