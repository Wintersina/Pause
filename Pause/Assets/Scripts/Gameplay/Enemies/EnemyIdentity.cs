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

    public void Set(EnemyDef d)
    {
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
