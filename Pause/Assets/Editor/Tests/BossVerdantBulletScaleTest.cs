using UnityEngine;

// THE BLOOM QUEEN'S BULLETS ARE 40 % BIGGER (Verdant, the third world): thorns (Bolt) and spores / seeds (Shard), every painted
// variant, are drawn at BossConfig.VerdantBulletScale x the shared size, while the hit radius is unchanged and no other boss changes.
public static class BossVerdantBulletScaleTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[VBS] PASS  " : "[VBS] FAIL  ") + what);
        if (!ok) fails++;
    }

    // the rim's farthest opaque texel past the centre cell, in cell units (the sprite spans the cell plus the pad)
    static float RimReach(Sprite rim)
    {
        var t = rim.texture; var r = rim.textureRect;
        var px = t.GetPixels32();
        int cx0 = (int)r.x + BossArt.ShotRimPad, cx1 = cx0 + BossArt.ShotRimTexels - 1, far = 0;
        for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
            for (int x = (int)r.x; x < (int)(r.x + r.width); x++)
                if (px[y * t.width + x].a > 4) far = Mathf.Max(far, Mathf.Max(cx0 - x, x - cx1));
        return far / (float)BossArt.ShotRimTexels;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Check("the scale constant is 1.4", Mathf.Abs(BossConfig.VerdantBulletScale - 1.4f) < 1e-6f);
            var verdant = BossCatalog.ForWorld(2);
            Check("world 2 (the third) is the Bloom Queen", verdant.artKey == "Verdant");
            Check("the base sizes and hit radii are untouched (.46 / .42, .13 / .12)",
                  BossConfig.BoltWorldSize == .46f && BossConfig.ShardWorldSize == .42f && BossConfig.BoltHitRadius == .13f && BossConfig.ShardHitRadius == .12f);

            var pool = new BossProjectilePool(8, 2);
            try
            {
                foreach (var boss in BossCatalog.All)
                    foreach (var style in new[] { BossShotStyle.Bolt, BossShotStyle.Shard })
                    {
                        float baseSize = style == BossShotStyle.Bolt ? BossConfig.BoltWorldSize : BossConfig.ShardWorldSize;
                        float hit = style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
                        float want = baseSize * (boss.artKey == "Verdant" ? 1.4f : 1f);
                        var s = pool.Fire(boss, style, new Vector3(0f, 1f, 0f), Vector2.down);
                        var col = s.Hitbox.GetComponent<CircleCollider2D>();
                        float worldHit = col.radius * s.transform.lossyScale.x;
                        Check(boss.artKey + " " + style + ": drawn " + s.transform.lossyScale.x.ToString("F3") + " (want " + want.ToString("F3") + "), hit radius " +
                              worldHit.ToString("F3") + " (want " + hit + ")",
                              Mathf.Abs(s.transform.lossyScale.x - want) < 1e-4f && Mathf.Abs(worldHit - hit) < 1e-4f && Mathf.Abs(s.Radius - hit) < 1e-6f);
                        // the rim keeps its absolute thickness: its opaque reach past the art, in world units, equals the unscaled one (+- one rim texel)
                        var rimSprite = BossArt.ShotRim(boss, style == BossShotStyle.Bolt ? BossArt.Bolt0 : BossArt.Shard0, false);
                        if (rimSprite != null)
                        {
                            float thick = RimReach(rimSprite) * s.transform.lossyScale.x;
                            float was = BossArt.ShotRimReach * baseSize;
                            Check(boss.artKey + " " + style + ": rim reaches " + thick.ToString("F4") + " wu (unscaled " + was.ToString("F4") + ")",
                                  Mathf.Abs(thick - was) <= baseSize * 1.5f / BossArt.ShotRimTexels);
                        }
                        s.Recycle();
                    }
            }
            finally { pool.Dispose(); }
        }
        finally { BossEncounter.ResetRun(); }
        Debug.Log("[VBS] failures: " + fails);
        return fails;
    }
}
