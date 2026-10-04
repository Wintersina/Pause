using UnityEngine;

// Builds a playable enemy from its roster entry. Same pieces the old prefabs
// carried -- art, trigger collider, tag, mover -- so collisionDetection, the
// ultimate's targeting and the cinematic clear all treat it like any hazard:
//
//   Rock     tag Astr, moveEnimes (zig-zag) + AsteroidSpin (floating rocks
//            sway upright instead of tumbling)
//   Alien    tag Enimey, moveEnimes, named "alien1"
//   Mine     tag Enimey, moveItemEnmInStrightLine + RailBombAnimator, named
//            "mine" (the spawner adds the RailMineMount)
//   Chaser   tag Enimey, ChaserEnemy (no scroller -- it steers itself)
//   others   tag Enimey, moveItemEnmInStrightLine, kinematic body
//
// Every enemy carries a SpawnFootprint (its reserved space, see SpawnSpace)
// bound to its mover. Every enemy is a ClearTarget (the movers register themselves in Awake;
// Create also registers explicitly so edit-mode builds count too).
public static class EnemyFactory
{
    public static GameObject Create(EnemyDef def, Vector3 position, Quaternion rotation)
    {
        if (def == null) return null;
        var go = new GameObject(def.ObjectName);
        go.tag = def.Tag;
        go.transform.SetPositionAndRotation(position, rotation);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = def.role == EnemyRole.Mine ? 12 : def.role == EnemyRole.Rock ? 2 : 3;
        go.AddComponent<EnemyIdentity>().Set(def);

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = def.ColliderSize;

        switch (def.role)
        {
            case EnemyRole.Rock:
                go.AddComponent<moveEnimes>();
                var spin = go.AddComponent<AsteroidSpin>();
                spin.speedRange = new Vector2(15f, 60f);   // the aestroid_* prefabs' tuning
                if (def.floating)
                {
                    // a chunk of the world's ground: it stays upright (cap on
                    // top) and rocks gently; its frames draw the bob
                    spin.swayDegrees = EnemyRoster.FloatSwayDegrees;
                    spin.swayPeriod = EnemyRoster.FloatSwayPeriod;
                }
                break;
            case EnemyRole.Alien:
                go.AddComponent<moveEnimes>();
                break;
            case EnemyRole.Mine:
                go.AddComponent<moveItemEnmInStrightLine>();
                break;
            case EnemyRole.Chaser:
                Kinematic(go);
                go.AddComponent<ChaserEnemy>();
                break;
            default:
                Kinematic(go);
                go.AddComponent<moveItemEnmInStrightLine>();
                break;
        }

        var flipbook = def.role == EnemyRole.Mine ? go.AddComponent<RailBombAnimator>() : go.AddComponent<EnemyFlipbook>();
        flipbook.Init(def);
        ClearTarget.Ensure(go);

        // Its reserved space on the board (SpawnSpace): the body, and the
        // mover as its movement pattern (a mine rebinds to its rail mount).
        SpawnFootprint.Attach(go, SpawnSpace.BodyHalf(def));
        SpawnFootprint.Bind(go, go.GetComponent<IMovementFootprint>());
        return go;
    }

    static void Kinematic(GameObject go)
    {
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
    }
}
