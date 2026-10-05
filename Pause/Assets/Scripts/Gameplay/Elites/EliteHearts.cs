using UnityEngine;

// An elite's hearts (EliteDef.hearts, 2): the player's own orbit, shield
// dart and crumble (HeartOrbit), on a smaller orbit, in the elite's own
// colour (EliteDef.heartColor: magenta / violet / cyan -- never the
// player's red), drawn from the white heart Vfx/eliteHeart tinted.
// Built when the elite joins the play; a heart lost darts to the hit and
// crumbles exactly like the player's.
[DefaultExecutionOrder(50)]
public class EliteHearts : HeartOrbit
{
    public const string SpritePath = "Vfx/eliteHeart";

    EliteShip ship;

    public void Bind(EliteShip s)
    {
        ship = s;
        heartSize = s.Def.heartSize;
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

    protected override Sprite HeartSprite() { return Resources.Load<Sprite>(SpritePath); }

    protected override Color HeartTint { get { return ship != null ? ship.Def.HeartColor : Color.magenta; } }

    protected override void ResolveStyle(out HeartStyle s, out OrbitStyle o)
    {
        s = HeartStyle.Halo;
        if (ship != null && !string.IsNullOrEmpty(ship.Def.heartStyle))
        {
            HeartStyle parsed;
            if (System.Enum.TryParse(ship.Def.heartStyle, out parsed)) s = parsed;
        }
        o = ShipHeartStyles.Orbit(s);
        o.radius = ship != null ? ship.Def.heartOrbit : .85f;
        o.flourishEvery = 0f;
        styleKey = ship != null ? ship.Def.key.Length * 7 : 0;
    }

    protected override Bounds HullBounds()
    {
        float r = ship != null ? ship.Def.hullRadius : .4f;
        return new Bounds(transform.position, new Vector3(r * 2f, r * 2f, .1f));
    }

    protected override SpriteRenderer HullRenderer() { return ship != null ? ship.Hull : null; }
}
