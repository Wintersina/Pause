using UnityEngine;

// A premium hull's timed homing missiles: every PremiumShips tier's
// missileEvery seconds (10 s at tier 0, quicker with every skin) it fires
// that tier's missiles at the nearest hazards (ShipAttackRunner.FireMissiles),
// whatever the ultimate and the shield are doing. Attached by
// ShipPowerController for a premium ship only.
//
// Gameplay time: it holds while the world is frozen (a pause, a world
// transition, a boss's entrance) and when nothing is on screen to hunt (it
// fires on the first frame there is), and dies with the ship.
public class ParagonMissiles : MonoBehaviour
{
    int ship;
    float timer;
    ShipAttackRunner runner;

    public int Fired { get; private set; }
    public float TimeLeft { get { return timer; } }
    public int Ship { get { return ship; } }

    public static ParagonMissiles Attach(GameObject go, int shipId)
    {
        var c = go.GetComponent<ParagonMissiles>();
        if (c == null) c = go.AddComponent<ParagonMissiles>();
        c.Setup(shipId);
        return c;
    }

    public void Setup(int shipId)
    {
        ship = shipId;
        runner = ShipAttackRunner.Attach(gameObject, shipId);
        timer = PremiumShips.Current(shipId).missileEvery;
    }

    void Update() { Step(ShipAttackRunner.ScaledDelta()); }

    // One frame (public so edit-mode tests can step it).
    public void Step(float dt)
    {
        if (dt <= 0f || buttonClicks.playerDied) return;
        if (ShipPowerController.TransitionFrozen || BossEncounter.ScriptedEntrance) return;
        if (timer > 0f) timer -= dt;
        if (timer > 0f) return;
        if (runner == null) runner = ShipAttackRunner.Attach(gameObject, ship);
        var tier = PremiumShips.Current(ship);
        if (runner.FireMissiles(tier.missiles) <= 0) return;   // nothing to hunt: try again next frame
        Fired++;
        UltimateShotSound.Play(ship);
        timer = tier.missileEvery;
    }
}
