using UnityEngine;

// An elite's hearts (EliteDef.hearts, 2 and up): the player's own orbit,
// shield dart and crumble (HeartOrbit), in neon orange (Orange, on every elite
// in every world -- never the player's red), drawn from the white heart
// Vfx/eliteHeart tinted.
//
// They spin round the elite in 3D exactly like the player's hearts: the
// player's Atom style (ShipHeartStyles -- crossing tilted planes that
// precess, hearts passing behind the hull and in front of it). The orbit is
// sized from the hull's real silhouette (EliteSilhouette: the circle through
// its furthest opaque pixel) plus the heart and a margin, so it passes outside
// the art on every hull, however large. Where the tilt still swings a heart
// round the back over the art, it is drawn above the hull, dimmed, instead of
// vanishing under it (HidesBehindHull): the count always reads.
//
// Built when the elite joins the play; a heart lost darts to the hit and
// crumbles exactly like the player's. Each heart (and its dart and crumble)
// wears HeartOutline's thin light-and-dark trace so it reads over every
// world and over the elite's own art, drawn OutlineGrow larger than
// EliteDef.heartSize for the same reason (still under the player's 0.22).
[DefaultExecutionOrder(50)]
public class EliteHearts : HeartOrbit
{
    public const string SpritePath = "Vfx/eliteHeart";
    public const float OutlineGrow = 1.1f;
    // The elites' heart colour, all worlds: a warm, saturated neon orange
    // (#FF8C1A, hue 30 deg: 36 deg from the player's red, so never mistaken for
    // a life) -- EliteDef.Resolve forces it, BossCatalog gives it to the bosses.
    public static readonly Color Orange = new Color(1f, .549f, .102f);
    // The orbit passes this far (world) outside the hull's drawn silhouette,
    // past the heart's own half-size.
    public const float Clearance = .05f;

    EliteShip ship;

    public void Bind(EliteShip s)
    {
        ship = s;
        heartSize = s.Def.heartSize * OutlineGrow;
        personalSpace = 1.3f;
        BuildHearts();
        Update();   // settles the shown count, so the first heart lost shields
    }

    protected override void Start() { }   // built by Bind

    public override void BuildHearts()
    {
        if (ship != null) BuildHearts(ship.Def.hearts);
    }

    // Applies a lost heart now (EliteShip.TakeHit) instead of next frame.
    public void Refresh() { Update(); }

    protected override int RemainingHearts() { return ship != null ? ship.Hearts : 0; }

    protected override bool TakeImpact(out Vector3 at)
    {
        if (ship != null) return ship.TakeImpact(out at);
        at = transform.position;
        return false;
    }

    protected override bool Outlined { get { return true; } }

    // Pale-hulled elites (EliteDef.boldHearts) wear the bold outline in every world.
    protected override bool? BoldOutline { get { return ship != null && ship.Def.boldHearts ? true : (bool?)null; } }

    protected override Sprite HeartSprite() { return Resources.Load<Sprite>(SpritePath); }

    protected override Color HeartTint { get { return ship != null ? ship.Def.HeartColor : Orange; } }

    // The player's Atom style for every elite (the def's heartStyle is
    // ignored): crossing tilted planes, all one way round, evenly fanned, the
    // 3D the player's ships show. The radius comes from OrbitRadiiFor.
    protected override void ResolveStyle(out HeartStyle s, out OrbitStyle o)
    {
        s = HeartStyle.Atom;
        o = ShipHeartStyles.Orbit(s);
        o.radius = 1f;
        o.flourishEvery = 0f;
        styleKey = ship != null ? ship.Def.key.Length * 7 : 0;
    }

    // The silhouette (world, at the hull's present scale), centred on the hull.
    float SilhouetteRadius()
    {
        if (ship == null) return .4f;
        float scale = ship.Hull != null ? Mathf.Max(Mathf.Abs(ship.Hull.transform.lossyScale.x), .0001f) : 1f;
        return EliteSilhouette.Radius(ship.Def) * scale;
    }

    protected override Bounds HullBounds()
    {
        float r = SilhouetteRadius();
        return new Bounds(transform.position, new Vector3(r * 2f, r * 2f, .1f));
    }

    // Outside the art: the silhouette, the heart's visible half and a margin.
    protected override Vector2 OrbitRadiiFor(Bounds hull)
    {
        float r = hull.extents.x + BossHearts.VisibleHalf(heartSize) + Clearance;
        return new Vector2(r, r);
    }

    // A heart round the back whose centre is over the drawn hull would be
    // covered by it: it is drawn above instead, dimmed.
    protected override bool HidesBehindHull(Vector2 world)
    {
        Vector2 c = ship != null && ship.Hull != null ? (Vector2)ship.Hull.transform.position : (Vector2)transform.position;
        return (world - c).sqrMagnitude < SilhouetteRadius() * SilhouetteRadius();
    }

    protected override SpriteRenderer HullRenderer() { return ship != null ? ship.Hull : null; }
}
