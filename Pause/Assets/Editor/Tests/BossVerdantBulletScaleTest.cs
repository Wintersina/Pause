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

    // A sprite's opaque half-width: the farthest opaque texel from its rect's centre line, in its own texels.
    static float OpaqueHalfWidth(Sprite s)
    {
        int w, h;
        var px = ShieldContour.ReadPixels(s, out w, out h);   // the sprite's own rect, w x h
        if (px == null) return 0f;
        float cx = w * .5f, far = 0f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 4) far = Mathf.Max(far, Mathf.Abs(x + .5f - cx));
        return far;
    }

    // The rim's thickness: how far its outermost opaque texel reaches past the art's own silhouette, in cell
    // units. (The art sits inside its cell with margins, so "past the cell" would read 0 for every boss.) The
    // silhouette is read the way the rim builder reads it: from the art's whole cell (Sprite.rect; textureRect
    // is trimmed to the drawing), measured from the cell's centre, then put on the rim's texel grid.
    static float RimReach(Sprite rim, Sprite art)
    {
        var cell = art.rect;
        int w = Mathf.RoundToInt(cell.width), h = Mathf.RoundToInt(cell.height);
        var cellSprite = Sprite.Create(art.texture, new Rect(cell.x, cell.y, w, h), new Vector2(.5f, .5f), w, 0, SpriteMeshType.FullRect);
        float artHalf;
        try { artHalf = OpaqueHalfWidth(cellSprite); }
        finally { Object.DestroyImmediate(cellSprite); }
        float artHalfInRim = artHalf * BossArt.ShotRimTexels / w;
        return (OpaqueHalfWidth(rim) - artHalfInRim) / BossArt.ShotRimTexels;
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
            // every boss's rim thickness as drawn, per style: the Bloom Queen's must equal the others'
            var rimThick = new System.Collections.Generic.List<(string key, BossShotStyle style, float thick, float baseSize)>();
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
                        // the rim keeps its absolute thickness: its opaque band past the art's silhouette, in world units, measured
                        // as drawn for every boss and compared below (the builder's band is thinner than the nominal ShotRimReach:
                        // coverage threshold and alpha falloff, the same for every boss, so the comparison is between bosses)
                        int rimCell = style == BossShotStyle.Bolt ? BossArt.Bolt0 : BossArt.Shard0;
                        var rimSprite = BossArt.ShotRim(boss, rimCell, false);
                        var artSprite = BossArt.Shot(boss, rimCell);
                        if (rimSprite != null && artSprite != null)
                            rimThick.Add((boss.artKey, style, RimReach(rimSprite, artSprite) * s.transform.lossyScale.x, baseSize));
                        s.Recycle();
                    }
                foreach (var style in new[] { BossShotStyle.Bolt, BossShotStyle.Shard })
                {
                    var others = new System.Collections.Generic.List<float>();
                    float verdantThick = -1f, baseSize = 0f;
                    string list = "";
                    foreach (var r in rimThick)
                    {
                        if (r.style != style) continue;
                        baseSize = r.baseSize;
                        if (r.key == "Verdant") verdantThick = r.thick; else others.Add(r.thick);
                        list += r.key + " " + r.thick.ToString("F4") + " ";
                    }
                    others.Sort();
                    bool band = others.Count > 0 && verdantThick > 0f;
                    foreach (var t in others) band &= t > 0f;
                    float median = others.Count > 0 ? others[others.Count / 2] : 0f;
                    Check(style + ": every boss's rim is a band past its silhouette (" + list.Trim() + ")", band);
                    Check(style + ": the Bloom Queen's rim is as thick as the other bosses' in world units (" + verdantThick.ToString("F4") +
                          " vs median " + median.ToString("F4") + ", +- 1.5 rim texels)",
                          band && Mathf.Abs(verdantThick - median) <= baseSize * 1.5f / BossArt.ShotRimTexels);
                }
            }
            finally { pool.Dispose(); }
        }
        finally { BossEncounter.ResetRun(); }
        Debug.Log("[VBS] failures: " + fails);
        return fails;
    }
}
