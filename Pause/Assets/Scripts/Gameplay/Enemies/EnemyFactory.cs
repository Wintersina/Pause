using System;
using System.Collections.Generic;
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
// On top of its mover every enemy but the chaser carries an EnemyBrain
// running its EnemyBehaviours record (its own pattern and attack; with one,
// a rock or alien's moveEnimes only scrolls -- the brain moves it sideways);
// the chaser's ChaserEnemy takes its style and numbers from the same record.
//
// Every enemy carries a SpawnFootprint (its reserved space, see SpawnSpace)
// bound to its mover. Every enemy is a ClearTarget (the movers register themselves in Awake;
// Create also registers explicitly so edit-mode builds count too).
//
// POOLING. An ordinary enemy's body (the GameObject, its SpriteRenderer, trigger collider, kinematic body and EnemyIdentity) is
// built once per roster entry and reused (PrefabPool: its snapshot restores transform, tag, layer, colour, sprite, collider and body
// to the fresh state). Everything that carries a life of its own -- movers, spin, flipbook, ClearTarget, brain, footprint, hearts,
// armour plates, the obsidian glint, whatever a weapon or a flare added -- is NOT part of the pooled body: it is added again by
// Configure on every spawn (exactly the calls and the order Create always made) and removed again on release (PooledObject drops
// everything that is not in the snapshot). So a reused enemy cannot carry a timer, a registry entry or a status from its last life.
// Not pooled (built and destroyed as before): mines (rail mounts), chasers, and every caller that asks pooled: false (elites, the
// tutorial). Outside play mode the pool is off (PrefabPool), so edit-mode tests build plain objects as they always did.
public static class EnemyFactory
{
    // The roles whose bodies are pooled.
    public static bool Poolable(EnemyDef def)
    {
        return def != null && def.role != EnemyRole.Mine && def.role != EnemyRole.Chaser;
    }

    // Idle instances kept per roster entry at level start (more are built on demand, up to PrefabPool.MaxFreePerKey).
    public const int PrewarmPerType = 2;
    // A released body rests this long (real seconds, in play) before it can be another enemy: anything that remembers it for a
    // moment (a weapon's already-hit list, a lingering follower) never meets it again as a stranger. See also EnemyLife.
    public const float ReuseCooldownSeconds = 4f;

    sealed class Slot
    {
        public string key;
        public Func<GameObject> build;
    }
    static readonly Dictionary<EnemyDef, Slot> slots = new Dictionary<EnemyDef, Slot>();

    static Slot SlotFor(EnemyDef def)
    {
        Slot slot;
        if (slots.TryGetValue(def, out slot)) return slot;
        slot = new Slot { key = "enemy:" + def.key };
        slot.build = () => BuildBody(def);
        slots[def] = slot;
        PrefabPool.SetCooldown(slot.key, ReuseCooldownSeconds);
        return slot;
    }

    // `size`: a rock's size as a multiple of its roster size (HazardSize;
    // the spawner draws it). Ignored for everything but a rock.
    // `tough`: false builds a one-hit enemy (the tutorial); otherwise see Toughness.
    // `pooled`: false always builds a plain object that the caller destroys (elites, the tutorial).
    public static GameObject Create(EnemyDef def, Vector3 position, Quaternion rotation, float size = 1f, bool tough = true, bool pooled = true)
    {
        if (def == null) return null;
        GameObject go;
        if (pooled && PrefabPool.Enabled && Poolable(def))
        {
            var slot = SlotFor(def);
            go = PrefabPool.Get(slot.key, slot.build, position, rotation);
        }
        else
        {
            go = BuildBody(def);
            go.transform.SetPositionAndRotation(position, rotation);
        }
        Configure(go, def, position, size, tough);
        return go;
    }

    // The pooled part: a body with its art slot, trigger collider and (for the ones that have it) kinematic rigid body.
    static GameObject BuildBody(EnemyDef def)
    {
        var go = new GameObject(def.ObjectName);
        go.tag = def.Tag;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = def.role == EnemyRole.Mine ? 12 : def.role == EnemyRole.Rock ? 2 : 3;
        go.AddComponent<EnemyIdentity>();

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = def.ColliderSize;

        if (def.role != EnemyRole.Rock && def.role != EnemyRole.Alien && def.role != EnemyRole.Mine) Kinematic(go);
        return go;
    }

    // The per-life part, in the order Create has always made it.
    static void Configure(GameObject go, EnemyDef def, Vector3 position, float size, bool tough)
    {
        var identity = go.GetComponent<EnemyIdentity>();
        identity.Set(def);
        // the obsidian shard's glass colour, stable for this body (ObsidianShards.Pick)
        if (ObsidianShards.Applies(def))
        {
            uint shardSeed = ObsidianShards.SeedFor(position, size);
            identity.SetVariant(ObsidianShards.Pick(shardSeed));
            go.AddComponent<ObsidianGlint>().Init(shardSeed, identity.Variant);   // its blinking glint (a child sprite)
        }

        // What it does (EnemyBehaviours): the mover below scrolls it, the
        // brain adds its pattern and its attack on top.
        var behaviour = EnemyBehaviours.For(def);
        moveEnimes weaver = null;
        moveItemEnmInStrightLine scroller = null;

        switch (def.role)
        {
            case EnemyRole.Rock:
                weaver = go.AddComponent<moveEnimes>();
                var spin = go.AddComponent<AsteroidSpin>();
                spin.speedRange = behaviour != null ? behaviour.spin : new Vector2(15f, 60f);   // the aestroid_* prefabs' tuning
                if (def.floating)
                {
                    // a chunk of the world's ground: it stays upright (cap on
                    // top) and rocks gently; its frames draw the bob
                    spin.swayDegrees = behaviour != null && behaviour.tilt > 0f ? behaviour.tilt : EnemyRoster.FloatSwayDegrees;
                    spin.swayPeriod = behaviour != null && behaviour.tilt > 0f ? behaviour.tiltPeriod : EnemyRoster.FloatSwayPeriod;
                }
                break;
            case EnemyRole.Alien:
                weaver = go.AddComponent<moveEnimes>();
                break;
            case EnemyRole.Mine:
                scroller = go.AddComponent<moveItemEnmInStrightLine>();
                break;
            case EnemyRole.Chaser:
                go.AddComponent<ChaserEnemy>().Configure(behaviour);
                break;
            default:
                scroller = go.AddComponent<moveItemEnmInStrightLine>();
                break;
        }
        // the brain owns the sideways movement: no ping-pong weave
        if (weaver != null && behaviour != null) weaver.Straight();

        var flipbook = def.role == EnemyRole.Mine ? go.AddComponent<RailBombAnimator>() : go.AddComponent<EnemyFlipbook>();
        flipbook.Init(def);
        if (def.key == "space_alien") go.AddComponent<AlienArrivalSmoke>();
        ClearTarget.Ensure(go);

        if (behaviour != null && def.role != EnemyRole.Chaser)
        {
            var brain = go.AddComponent<EnemyBrain>();
            brain.Init(def, behaviour);
            if (weaver != null) weaver.brain = brain;
            if (scroller != null) scroller.brain = brain;
        }

        // Its size (rocks: scale, collider, motion, hit radius; HazardSize),
        // then its reserved space on the board (SpawnSpace): the body at that
        // size, and the mover as its movement pattern (a mine rebinds to its
        // rail mount).
        size = HazardSize.Apply(go, def, size);
        SpawnFootprint.Attach(go, SpawnSpace.BodyHalf(def, size));
        SpawnFootprint.Bind(go, go.GetComponent<IMovementFootprint>());
        // 2 hearts (a rock or mine stays one-hit); the tutorial's enemies stay one-hit
        if (tough) Toughness.Attach(go, def);
    }

    static void Kinematic(GameObject go)
    {
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
    }

    // ---- prewarm (level start) ----

    // Builds ONE missing idle body of the world's poolable roster per call (spread the work over frames); true while more remain.
    public static bool PrewarmStep(int world)
    {
        if (!PrefabPool.Enabled) return false;
        var all = EnemyRoster.All;
        for (int i = 0; i < all.Length; i++)
        {
            var def = all[i];
            if (def.world != world || !Poolable(def)) continue;
            var slot = SlotFor(def);
            int have = PrefabPool.FreeCount(slot.key);
            if (have >= PrewarmPerType) continue;
            PrefabPool.Prewarm(slot.key, slot.build, have + 1);
            return true;
        }
        return false;
    }
}
