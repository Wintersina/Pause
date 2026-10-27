using UnityEngine;

// EnemyDeathFlipbook.Frames: any world with a valid 3 x height strip gets
// its death drawings; a missing or wrong-width strip falls back to the
// generic debris (null). Synthetic textures are injected, no repo files.
public static class EnemyDeathFlipbookTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EDF] PASS  " : "[EDF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        var saved = EnemyDeathFlipbook.TextureLoader;
        EnemyDeathFlipbook.ClearCache();
        try
        {
            var frost = EnemyRoster.One(1, EnemyRole.Fighter);
            var space = EnemyRoster.One(0, EnemyRole.Fighter);

            var good = new Texture2D(192 * 3, 192);
            var bad = new Texture2D(500, 192);
            EnemyDeathFlipbook.TextureLoader = key => key == frost.key ? good : null;
            var frames = EnemyDeathFlipbook.Frames(frost);
            Check("Frost key with a 3x strip resolves 3 sprites", frames != null && frames.Length == 3);
            if (frames != null && frames.Length == 3)
            {
                bool ok = true;
                for (int i = 0; i < 3; i++)
                    ok &= frames[i] != null && Mathf.Approximately(frames[i].rect.width, 192f) &&
                          Mathf.Approximately(frames[i].rect.height, 192f) && Mathf.Approximately(frames[i].rect.x, i * 192f) &&
                          Mathf.Approximately(frames[i].pixelsPerUnit, 192f / frost.FrameWorldSize);
                Check("Frost death cells are square, in order, sized to the role", ok);
            }
            Check("Space key with no strip stays null (generic debris)", EnemyDeathFlipbook.Frames(space) == null);

            EnemyDeathFlipbook.ClearCache();
            EnemyDeathFlipbook.TextureLoader = key => null;
            Check("Frost key with no strip returns null", EnemyDeathFlipbook.Frames(frost) == null);
            EnemyDeathFlipbook.TextureLoader = key => bad;
            Check("Bad-width strip returns null", EnemyDeathFlipbook.Frames(frost) == null);
            Check("Null def returns null", EnemyDeathFlipbook.Frames(null) == null);

            EnemyDeathFlipbook.TextureLoader = key => key == space.key ? good : null;
            var s = EnemyDeathFlipbook.Frames(space);
            Check("Space strip still resolves 3 sprites", s != null && s.Length == 3);

            Pool(frost, good);

            Object.DestroyImmediate(good);
            Object.DestroyImmediate(bad);
        }
        finally
        {
            EnemyDeathFlipbook.AllowInEditMode = false;
            EnemyDeathFlipbook.DestroyPool();
            EnemyDeathFlipbook.TextureLoader = saved;
            EnemyDeathFlipbook.ClearCache();
        }
        Debug.Log("[EDF] RESULT " + (fails == 0 ? "PASS" : "FAIL (" + fails + ")"));
        return fails;
    }

    // A dead enemy of `def` at `at`, drawn at sorting order `order`: just what Spawn reads.
    static GameObject Victim(EnemyDef def, Vector3 at, int order, float scale)
    {
        var go = new GameObject("~victim " + def.key, typeof(SpriteRenderer));
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        go.GetComponent<SpriteRenderer>().sortingOrder = order;
        go.AddComponent<EnemyIdentity>().Set(def);
        return go;
    }

    // The pool: a death takes a parked pose, every bit of it reset; the pose
    // is parked again when its drawings are over, not destroyed; nothing leaks.
    static void Pool(EnemyDef def, Texture2D strip)
    {
        EnemyDeathFlipbook.ClearCache();
        EnemyDeathFlipbook.TextureLoader = key => key == def.key ? strip : null;
        EnemyDeathFlipbook.DestroyPool();
        EnemyDeathFlipbook.AllowInEditMode = true;
        var frames = EnemyDeathFlipbook.Frames(def);
        int created0 = EnemyDeathFlipbook.Created;
        var victims = new System.Collections.Generic.List<GameObject>();
        try
        {
            var a = Victim(def, new Vector3(1f, 2f, 0f), 5, 1.5f); victims.Add(a);
            EnemyDeathFlipbook.Spawn(a);
            var pose = EnemyDeathFlipbook.LastSpawned;
            Check("pool: a death shows a pose", pose != null && pose.gameObject.activeSelf);
            if (pose == null) return;
            Check("pool: the pose is where the enemy died, turned and sized like it",
                  pose.transform.position == a.transform.position && pose.transform.rotation == a.transform.rotation && pose.transform.localScale == a.transform.lossyScale);
            Check("pool: the pose draws the first death drawing over the enemy's order (>= 67)",
                  pose.Renderer.sprite == frames[0] && pose.Renderer.sortingOrder == 67 && pose.Age == 0f && pose.Drawings == frames);
            Check("pool: a pose is one GameObject with a SpriteRenderer and the flipbook, nothing else",
                  pose.GetComponents<Component>().Length == 3 && pose.transform.childCount == 0);
            Check("pool: one pose built, none parked while it plays", EnemyDeathFlipbook.Created == created0 + 1 && EnemyDeathFlipbook.FreeCount == 0);

            pose.Step(.1f);
            Check("pool: the second drawing after the flash", pose.Renderer.sprite == frames[1]);
            pose.Step(.1f);
            Check("pool: the third drawing after the rupture", pose.Renderer.sprite == frames[2]);
            pose.Step(.3f);
            Check("pool: over, the pose is parked (inactive, in the pool, not destroyed)",
                  pose != null && !pose.gameObject.activeSelf && EnemyDeathFlipbook.FreeCount == 1 && pose.Drawings == null);
            // a parked pose steps no further
            pose.Step(1f);
            Check("pool: a parked pose stays parked", EnemyDeathFlipbook.FreeCount == 1 && !pose.gameObject.activeSelf);

            var b = Victim(def, new Vector3(-3f, 4f, 0f), 80, .7f); victims.Add(b);
            EnemyDeathFlipbook.Spawn(b);
            var again = EnemyDeathFlipbook.LastSpawned;
            Check("pool: the next death reuses the parked pose (nothing new built)", again == pose && EnemyDeathFlipbook.Created == created0 + 1 && EnemyDeathFlipbook.FreeCount == 0);
            Check("pool: ... reset to the new death: place, size, order, age, first drawing",
                  again.gameObject.activeSelf && again.transform.position == b.transform.position && again.transform.localScale == b.transform.lossyScale &&
                  again.Renderer.sortingOrder == 80 && again.Age == 0f && again.Renderer.sprite == frames[0] && again.Drawings == frames);
            Check("pool: no component leaked across the reuse", again.GetComponents<Component>().Length == 3 && again.transform.childCount == 0);

            // three deaths at once: three poses, all parked afterwards, reused in turn
            var c = Victim(def, Vector3.zero, 1, 1f); victims.Add(c);
            var d = Victim(def, Vector3.one, 1, 1f); victims.Add(d);
            EnemyDeathFlipbook.Spawn(c);
            var pc = EnemyDeathFlipbook.LastSpawned;
            EnemyDeathFlipbook.Spawn(d);
            var pd = EnemyDeathFlipbook.LastSpawned;
            Check("pool: concurrent deaths each get their own pose", pc != again && pd != again && pc != pd && EnemyDeathFlipbook.Created == created0 + 3);
            again.Step(1f); pc.Step(1f); pd.Step(1f);
            Check("pool: all three parked when over", EnemyDeathFlipbook.FreeCount == 3 && !again.gameObject.activeSelf && !pc.gameObject.activeSelf && !pd.gameObject.activeSelf);
            EnemyDeathFlipbook.Spawn(c);
            Check("pool: a parked pose serves the next death", EnemyDeathFlipbook.FreeCount == 2 && EnemyDeathFlipbook.Created == created0 + 3);
            EnemyDeathFlipbook.LastSpawned.Step(1f);

            // a pose whose object went with its scene is pruned, not handed out
            Object.DestroyImmediate(pd.gameObject);
            EnemyDeathFlipbook.Prune();
            Check("pool: a pose destroyed with its scene is pruned", EnemyDeathFlipbook.FreeCount == 2);
            EnemyDeathFlipbook.Spawn(c);
            Check("pool: ... and never handed out", EnemyDeathFlipbook.LastSpawned != null && EnemyDeathFlipbook.LastSpawned.gameObject.activeSelf);
            EnemyDeathFlipbook.LastSpawned.Step(1f);

            // an enemy with no strip shows nothing and takes nothing from the pool
            var space = EnemyRoster.One(0, EnemyRole.Fighter);
            var e = Victim(space, Vector3.zero, 1, 1f); victims.Add(e);
            int freeBefore = EnemyDeathFlipbook.FreeCount;
            EnemyDeathFlipbook.Spawn(e);
            Check("pool: a death without a strip takes no pose", EnemyDeathFlipbook.FreeCount == freeBefore);

            Check("pool: the cap keeps a burst of kills from growing without bound", EnemyDeathFlipbook.PoolCap >= 8 && EnemyDeathFlipbook.PoolCap <= 64);
        }
        finally
        {
            foreach (var v in victims) if (v != null) Object.DestroyImmediate(v);
            EnemyDeathFlipbook.AllowInEditMode = false;
            EnemyDeathFlipbook.DestroyPool();
        }
    }
}
