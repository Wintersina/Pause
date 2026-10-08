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

            Object.DestroyImmediate(good);
            Object.DestroyImmediate(bad);
        }
        finally
        {
            EnemyDeathFlipbook.TextureLoader = saved;
            EnemyDeathFlipbook.ClearCache();
        }
        Debug.Log("[EDF] RESULT " + (fails == 0 ? "PASS" : "FAIL (" + fails + ")"));
        return fails;
    }
}
