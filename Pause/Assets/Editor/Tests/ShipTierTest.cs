using System.IO;
using UnityEngine;

// The size tier system (docs/ship-scale-and-lockon-analysis.md, 2.1): fodder < fighter < heavy < elite < boss, every
// elite's DRAWN silhouette at least FloorHulls x the flown hull (HeavyFloorHulls for the armoured classes), its hit radius a
// sane share of what is drawn, the enemy roster's tier scale applied to frames, colliders and targets (mines and bosses
// untouched), and the lanes still wide enough for a ship beside them.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod ShipTierTest.Run
public static class ShipTierTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TIER] PASS  " : "[TIER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EliteCatalog.Reload();
        Elites();
        Enemies();
        Lanes();
        Debug.Log("[TIER] failures: " + fails);
        return fails;
    }

    static float Hull => shopingShips.ReferenceHullSize * ShipScale.Main;

    // The drawn silhouette's longest side of the flight cell(s), world units.
    public static float DrawnLongSide(EliteDef d)
    {
        string path = "Assets/Art/Resources/Elites/" + char.ToUpper(d.world[0]) + d.world.Substring(1) + "/" + d.key + ".png";
        if (!File.Exists(path)) return -1f;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return -1f;
            int h = tex.height;
            var px = tex.GetPixels32();
            var flights = d.cells.flight != null && d.cells.flight.Length > 0 ? d.cells.flight : new[] { 0, 1, 2, 3 };
            float best = 0f;
            foreach (int c in flights)
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = c * h; x < (c + 1) * h; x++)
                        if (px[y * tex.width + x].a > 40)
                        {
                            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                        }
                if (x1 < 0) continue;
                best = Mathf.Max(best, Mathf.Max(x1 - x0 + 1, y1 - y0 + 1) / (float)h * d.cellWorldSize);
            }
            return best;
        }
        finally { Object.DestroyImmediate(tex); }
    }

    static void Elites()
    {
        Check("the elite floor is 1.5 x the flown hull, the armoured floor 1.7 x (" + (EliteDef.FloorHulls * Hull).ToString("0.00") + " / " + (EliteDef.HeavyFloorHulls * Hull).ToString("0.00") + " u)",
              Mathf.Approximately(EliteDef.FloorHulls, 1.5f) && Mathf.Approximately(EliteDef.HeavyFloorHulls, 1.7f) && Hull > .7f);
        int n = 0;
        foreach (var d in EliteCatalog.All)
        {
            n++;
            float drawn = DrawnLongSide(d);
            float floor = d.TierFloorUnits;
            Check(d.key + ": drawn " + drawn.ToString("0.00") + " u >= the " + (d.Heavy ? "armoured" : "elite") + " floor " + floor.ToString("0.00") + " u (" + (drawn / Hull).ToString("0.0") + " x the flown hull)",
                  drawn >= floor * .97f);
            Check(d.key + ": below the boss tier (" + drawn.ToString("0.00") + " < 2.4 u)", drawn < 2.4f);
            // Space / Frost elites were shrunk 2026-10-18 (too big): clearly bigger than the player's ship, never huge
            if (d.world == "space" || d.world == "frost")
                Check(d.key + ": drawn " + drawn.ToString("0.00") + " u is within the shrunk band (1.5 x the flown hull .. 1.7 u): bigger than the player, not huge", drawn >= Hull * 1.5f * .985f && drawn <= 1.7f);
            // the hit radius is about half of what is drawn: not a sprite-only scale, not a giant hitbox
            float share = d.hullRadius / (drawn * .5f);
            Check(d.key + ": hit radius " + d.hullRadius.ToString("0.00") + " u is " + share.ToString("0.00") + " of the drawn half-size (0.35-0.80: the armoured elites were drawn with a big hit share from the start)", share >= .35f && share <= .8f);
        }
        Check("every elite is measured (" + n + ")", n >= 36);
        // tiers above: an elite is bigger than the world heavies at their tier
        float heavy = EnemyRoster.BigWidth;
        float lowest = 9f;
        foreach (var d in EliteCatalog.All) lowest = Mathf.Min(lowest, DrawnLongSide(d));
        Check("the smallest elite (" + lowest.ToString("0.00") + " u) out-sizes the heavies (" + heavy.ToString("0.00") + " u) and the player's ship (" + Hull.ToString("0.00") + " u) by 1.5x",
              lowest > heavy && lowest >= Hull * 1.5f * .985f);
    }

    static void Enemies()
    {
        Check("the size tier scale is 1.2", Mathf.Approximately(EnemyRoster.TierScale, 1.2f));
        Check("mines are not part of the tier (scale 1, 0.621 u collider, 0.65 u drawn)",
              Mathf.Approximately(EnemyRoster.TierScaleFor(EnemyRole.Mine), 1f) && EnemyRoster.ColliderSize(EnemyRole.Mine) == new Vector2(.621f, .621f) && Mathf.Approximately(EnemyRoster.MineWidth, .65f));
        float rock = EnemyRoster.TargetWidth(EnemyRole.Rock), alien = EnemyRoster.TargetWidth(EnemyRole.Alien), fighter = EnemyRoster.TargetWidth(EnemyRole.Fighter),
              chaser = EnemyRoster.TargetWidth(EnemyRole.Chaser), big = EnemyRoster.TargetWidth(EnemyRole.Big);
        Check("tiers rise: rock " + rock.ToString("0.00") + " < fighter " + fighter.ToString("0.00") + " < chaser " + chaser.ToString("0.00") + " < heavy " + big.ToString("0.00") + " u",
              rock < fighter && fighter <= chaser && chaser < big && alien < fighter);
        Check("the heavies are at least 1.3 x the fighters (" + (big / fighter).ToString("0.00") + ")", big >= fighter * 1.3f);
        foreach (EnemyRole r in new[] { EnemyRole.Rock, EnemyRole.Alien, EnemyRole.Fighter, EnemyRole.Chaser, EnemyRole.Big })
        {
            var col = EnemyRoster.ColliderSize(r);
            var tgt = EnemyRoster.TargetCollider(r);
            Check(r + ": frame, collider and target all carry the tier scale (frame " + EnemyRoster.FrameWorldSize(r).ToString("0.00") + ", collider " + col + ", target " + EnemyRoster.TargetWidth(r).ToString("0.00") + ")",
                  Mathf.Abs(col.x - tgt.x) <= tgt.x * .15f && Mathf.Abs(col.y - tgt.y) <= tgt.y * .15f &&
                  EnemyRoster.FrameWorldSize(r) >= EnemyRoster.TargetWidth(r) * .85f);
        }
        Check("a collider stays inside what is drawn (every role: collider <= 0.95 x target width)",
              EnemyRoster.ColliderSize(EnemyRole.Fighter).x <= EnemyRoster.TargetWidth(EnemyRole.Fighter) * .95f &&
              EnemyRoster.ColliderSize(EnemyRole.Big).x <= EnemyRoster.TargetWidth(EnemyRole.Big) * .95f &&
              EnemyRoster.ColliderSize(EnemyRole.Rock).x <= EnemyRoster.TargetWidth(EnemyRole.Rock) * .95f);
    }

    static void Lanes()
    {
        float lane = BossRails.InnerEdge * 2f;
        float gap = SpawnLane.ShipGap;
        float widest = 0f;
        foreach (EnemyRole r in new[] { EnemyRole.Rock, EnemyRole.Alien, EnemyRole.Fighter, EnemyRole.Chaser, EnemyRole.Big })
            widest = Mathf.Max(widest, EnemyRoster.ColliderSize(r).x);
        Check("a row of three of the widest enemies (" + widest.ToString("0.00") + " u collider) still leaves the player a gap in the lane (" + lane.ToString("0.0") + " u)",
              3f * widest + gap < lane);
        float widestElite = 0f;
        foreach (var d in EliteCatalog.All) widestElite = Mathf.Max(widestElite, d.hullRadius * 2f);
        Check("three elites side by side (" + (3f * widestElite).ToString("0.0") + " u of hit width) leave the lane's gap",
              3f * widestElite + gap < lane);
    }
}
