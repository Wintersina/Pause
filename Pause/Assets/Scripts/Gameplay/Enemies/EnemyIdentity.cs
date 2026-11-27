using UnityEngine;

// Which roster entry a spawned enemy is. Lets other systems ask "what is
// this?" by role and stable id rather than by GameObject name:
//
//   EnemyIdentity.Of(go)          -> EnemyDef (null for non-roster objects)
//   EnemyIdentity.IsRole(go, r)   -> role check (Alien, Mine, ...)
//   def.key / def.codexId         -> stable ids (art key / codex entry)
//   def.explosion / explosionSize -> TargetExplosion variant and size
[DisallowMultipleComponent]
public class EnemyIdentity : MonoBehaviour
{
    [SerializeField] string key;

    EnemyDef def;

    public EnemyDef Def
    {
        get
        {
            if (def == null && !string.IsNullOrEmpty(key)) def = EnemyRoster.Find(key);
            return def;
        }
    }

    public string Key => key;

    // This body's size as a multiple of its roster size (HazardSize; 1 for
    // everything but a rock drawn small or large). Set at spawn.
    public float Scale { get; private set; } = 1f;

    // Which life of its (possibly pooled) body this is: bumped when a body is set up for a new enemy (Set) and when it is switched
    // off (a release), so anything that remembered a stamp (EnemyLife.Stamp) can tell "the same enemy" from "the body's next one".
    public int Life { get; private set; }
    void OnDisable() { Life++; }

    public void Set(EnemyDef d)
    {
        Life++;
        def = d;
        key = d != null ? d.key : null;
        Scale = 1f;
        Variant = 0;
    }

    public void SetScale(float scale) { Scale = scale > 0f ? scale : 1f; }

    // Which colour of its art this body wears (0 = the roster strip; only the obsidian shard has more, ObsidianShards).
    public int Variant { get; private set; }
    public void SetVariant(int variant) { Variant = variant > 0 ? variant : 0; }

    // A roster body's size multiple (1 for anything without an identity).
    public static float ScaleOf(GameObject go)
    {
        EnemyIdentity id;
        return go != null && go.TryGetComponent(out id) ? id.Scale : 1f;
    }

    public static int VariantOf(GameObject go)
    {
        EnemyIdentity id;
        return go != null && go.TryGetComponent(out id) ? id.Variant : 0;
    }

    public static EnemyDef Of(GameObject go)
    {
        if (go == null) return null;
        EnemyIdentity id;   // TryGetComponent: no editor allocation on a miss
        return go.TryGetComponent(out id) ? id.Def : null;
    }

    public static bool IsRole(GameObject go, EnemyRole role)
    {
        var d = Of(go);
        return d != null && d.role == role;
    }
}

// Is this enemy still the one a holder remembered? A pooled body is not destroyed when its enemy dies: it is released (inactive,
// then reused), so a holder that only tests `== null` would keep following / re-hitting / counting it. These helpers are the
// "gone" test for anything that keeps an enemy's GameObject or Transform across frames.
//   Gone(go)        null, or a pooled body that was released (pending or free). A plain object is never "gone" while it exists.
//   Stamp / Same    a per-life id: Same(go, stamp) is false once the body was released or reused as another enemy.
// (Components added to an enemy -- ClearTarget, EnemyBrain, hearts, movers -- are destroyed on release, so holders of those already see null.)
public static class EnemyLife
{
    public static bool Gone(GameObject go)
    {
        if (go == null) return true;
        PooledObject po;
        return go.TryGetComponent(out po) && (po.IsFree || po.IsPending);
    }

    public static bool Gone(Component c) { return c == null || Gone(c.gameObject); }

    public static int Stamp(GameObject go)
    {
        EnemyIdentity id;
        return go != null && go.TryGetComponent(out id) ? id.Life : 0;
    }

    public static bool Same(GameObject go, int stamp)
    {
        return !Gone(go) && Stamp(go) == stamp;
    }
}
