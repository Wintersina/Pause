using UnityEngine;

// An enemy's own death drawings (EnemyRoster.DeathFrames): the Space alien
// plays the last two cells of its strip -- the eye-beam charge, then the
// burst -- as it dies, over the standard blast.
//
// The hook is TargetExplosion.Spawn(GameObject), the one call every kill that
// blows an enemy up makes (weapons and the ultimate via ShipAttackTarget.Kill,
// the shielded and unshielded rams via collisionDetection / RamKill, the boss
// fight's sweep) -- right beside RailBombAnimator.Burst. The dying body is
// destroyed by its caller on the same frame, so scoring, atoms and sound are
// untouched; this leaves a collider-less, brain-less ghost behind that holds
// each drawing a few ticks, drifts down with the board, fades on the last,
// and destroys itself. Gameplay time, like the explosion over it.
public class EnemyDeathFlipbook : MonoBehaviour
{
    public static int Spawned;   // tests

    SpriteRenderer sr;
    Sprite[] sprites;
    int[] cells;
    float[] holds;
    int step;
    float left;

    public bool Finished { get; private set; }
    // The strip cell on show (5 then 6 for the Space alien).
    public int CurrentCell => cells != null ? cells[step] : -1;
    public int Steps => cells != null ? cells.Length : 0;

    // Play mode only (an edit-mode test kill leaves nothing behind); null
    // when the target has no death drawings.
    public static EnemyDeathFlipbook Spawn(GameObject target)
    {
        if (!Application.isPlaying || target == null) return null;
        var def = EnemyIdentity.Of(target);
        if (EnemyRoster.DeathFrames(def) == null) return null;
        var src = target.GetComponent<SpriteRenderer>();
        return Create(def, target.transform.position, target.transform.rotation, target.transform.lossyScale, src);
    }

    // `like` (optional) lends its flip, material and sorting.
    public static EnemyDeathFlipbook Create(EnemyDef def, Vector3 at, Quaternion rot, Vector3 scale, SpriteRenderer like)
    {
        var cells = EnemyRoster.DeathFrames(def);
        var ticks = EnemyRoster.DeathTicks(def);
        var all = EnemyArt.Frames(def);
        if (cells == null || all == null) return null;

        var go = new GameObject("~EnemyDeath_" + def.key);
        go.transform.SetPositionAndRotation(at, rot);
        go.transform.localScale = scale;
        var d = go.AddComponent<EnemyDeathFlipbook>();
        d.sr = go.AddComponent<SpriteRenderer>();
        if (like != null)
        {
            d.sr.flipX = like.flipX;
            d.sr.flipY = like.flipY;
            d.sr.sharedMaterial = like.sharedMaterial;
            d.sr.sortingLayerID = like.sortingLayerID;
            d.sr.sortingOrder = like.sortingOrder;
        }
        d.cells = cells;
        d.sprites = new Sprite[cells.Length];
        d.holds = new float[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            d.sprites[i] = all[cells[i]];
            d.holds[i] = ticks[i] * EnemyFlipbook.TickSeconds;
        }
        d.Show(0);
        Spawned++;
        return d;
    }

    public float TotalSeconds { get { float t = 0f; foreach (var h in holds) t += h; return t; } }

    void Show(int i)
    {
        step = i;
        left = holds[i];
        sr.sprite = sprites[i];
    }

    void Update()
    {
        if (TargetExplosion.WorldScrolling)
            transform.Translate(Vector2.down * moveBackGround.speed * Time.deltaTime * 30f, Space.World);
        Advance(TargetExplosion.Delta());
    }

    // Public so edit-mode tests can step it deterministically.
    public void Advance(float dt)
    {
        if (Finished || dt <= 0f) return;
        left -= dt;
        while (left <= 0f)
        {
            if (step + 1 >= sprites.Length) { Finish(); return; }
            float over = -left;
            Show(step + 1);
            left -= over;
        }
        // the last drawing fades out over its second half
        if (step == sprites.Length - 1)
        {
            var c = sr.color;
            c.a = Mathf.Clamp01(left / (holds[step] * .5f));
            sr.color = c;
        }
    }

    void Finish()
    {
        Finished = true;
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
}
