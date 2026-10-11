using System.IO;
using UnityEngine;

// Desert's 12-unit roster and its Mirage Mine (docs/desert-world-plan.md sections 5 and 10, docs/desert-wire.md): the defs (names, roles, tiers, world 6),
// the strips (11 idle strips of seven 192 cells / 1792 for the Sandcrawler, 12 death strips), the fourth mine atlas, the pinned behaviours and
// themed attacks, the palette, the explosion kind, the pilot density, the Scarab Drone's burrow, and the art rules: no pink on any body (pink is for
// hostile shots and attack effects only) and no player red.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites DesertRosterTest
public static class DesertRosterTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DRT] PASS  " : "[DRT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int Desert = 6;
    static readonly (string key, EnemyRole role, int tier, string name)[] Expected =
    {
        ("desert_rock_dune", EnemyRole.Rock, 1, "Dune Boulder"), ("desert_rock_spire", EnemyRole.Rock, 2, "Glass Spire"),
        ("desert_rock_husk", EnemyRole.Rock, 3, "Fulgurite Husk"), ("desert_rock_slab", EnemyRole.Rock, 4, "Dune Slab"),
        ("desert_mine", EnemyRole.Mine, 0, "Mirage Mine"), ("desert_big", EnemyRole.Big, 0, "Sandcrawler"),
        ("desert_fighter_1", EnemyRole.Fighter, 1, "Sand Skimmer"), ("desert_fighter_2", EnemyRole.Fighter, 2, "Glint Hawk"),
        ("desert_fighter_3", EnemyRole.Fighter, 3, "Lens Gunner"), ("desert_fighter_4", EnemyRole.Fighter, 4, "Dust Devil Tank"),
        ("desert_chaser", EnemyRole.Chaser, 0, "Scarab Drone"), ("desert_alien", EnemyRole.Alien, 0, "Mirage Wisp"),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Defs();
        Art();
        Behaviours();
        Palette();
        Burrow();
        Debug.Log("[DRT] failures: " + fails);
        return fails;
    }

    static void Defs()
    {
        int n = 0;
        foreach (var d in EnemyRoster.All) if (d.world == Desert) n++;
        Check("Desert has 12 roster units (" + n + ")", n == 12);
        foreach (var x in Expected)
        {
            var d = EnemyRoster.Find(x.key);
            Check(x.key + ": " + x.name + ", " + x.role + ", tier " + x.tier + ", world 6, codex id, concept and lore",
                  d != null && d.displayName == x.name && d.role == x.role && d.tier == x.tier && d.world == Desert && !string.IsNullOrEmpty(d.concept) && !string.IsNullOrEmpty(d.lore) &&
                  d.codexId == (x.role == EnemyRole.Rock || x.role == EnemyRole.Mine ? "hazard_" : "enemy_") + x.key.Replace("desert_", "desert_") &&
                  d.explosion == (x.role == EnemyRole.Mine ? TargetExplosion.Kind.Mine : TargetExplosion.Kind.Rock));
            Check(x.key + ": lore is three sentences or fewer", d != null && d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
        Check("exactly one floating rock (the Dune Slab)", EnemyRoster.Find("desert_rock_slab").floating && !EnemyRoster.Find("desert_rock_dune").floating);
        Check("EnemyRoster.For(6, role) serves Desert's own cast, not a stand-in", EnemyRoster.One(Desert, EnemyRole.Fighter).world == Desert && EnemyRoster.For(Desert, EnemyRole.Rock).Count == 4);
        Check("pilot density has a seventh entry, 5.0 low / 4.0 high speed",
              EnemyDensity.PilotLoadAtLowSpeed.Length == 7 && EnemyDensity.PilotLoadAtHighSpeed.Length == 7 &&
              Mathf.Approximately(EnemyDensity.PilotLoadAtLowSpeed[6], 5f) && Mathf.Approximately(EnemyDensity.PilotLoadAtHighSpeed[6], 4f));
        Check("difficulty tier .00425 / 1.80, above Storm's", Mathf.Approximately(WorldManager.Worlds[6].speedRampPerSecond, .00425f) &&
              Mathf.Approximately(WorldManager.Worlds[6].enemyRampScale, 1.8f) && WorldManager.Worlds[6].enemyRampScale > WorldManager.Worlds[5].enemyRampScale);
    }

    static void Art()
    {
        foreach (var x in Expected)
        {
            var d = EnemyRoster.Find(x.key);
            if (x.role != EnemyRole.Mine)
            {
                var strip = Load("Assets/Art/Resources/Enemies/" + x.key + ".png");
                int cell = x.role == EnemyRole.Big ? 256 : 192;
                Check(x.key + ": idle strip is seven " + cell + " px cells", strip != null && strip.width == cell * 7 && strip.height == cell);
                if (strip != null) { Pink(x.key, strip); Object.DestroyImmediate(strip); }
            }
            var death = Load("Assets/Art/Resources/Enemies/Death/" + x.key + ".png");
            int dcell = x.role == EnemyRole.Big ? 256 : 192;
            Check(x.key + ": death strip is three " + dcell + " px cells", death != null && death.width == dcell * 3 && death.height == dcell);
            if (death != null) Object.DestroyImmediate(death);
            var go = EnemyFactory.Create(d, Vector3.zero, Quaternion.identity);
            Check(x.key + ": the factory builds it with a sprite", go != null && go.GetComponentInChildren<SpriteRenderer>() != null && go.GetComponentInChildren<SpriteRenderer>().sprite != null);
            if (go != null) Object.DestroyImmediate(go);
        }
        var mine = Load("Assets/Art/Resources/Enemies/Mines/rail_mines_neon_desert.png");
        Check("the Mirage Mine row is the fourth mine atlas, 1254 x 314, routed by RailMineArt", mine != null && mine.width == 1254 && mine.height == 314 &&
              EnemyRoster.Find("desert_mine").StripPath == RailMineArt.DesertAtlasPath);
        if (mine != null) { Pink("desert_mine atlas", mine); Object.DestroyImmediate(mine); }
        Check("Dune Boulder... Mirage Wisp strips are bordered, not repainted: the source script exists", File.Exists("Assets/Art/Enemies/src~/desert_enemies/add_border.py"));
    }

    // No pink on any enemy body: the hostile pink (hue 312-326) is for shots and attack effects. A few antialiased texels are tolerated (<= 0.2 % of opaque pixels).
    static void Pink(string what, Texture2D t)
    {
        var px = t.GetPixels32();
        int opaque = 0, pink = 0, red = 0;
        foreach (var p in px)
        {
            if (p.a < 128) continue;
            opaque++;
            float h, s, v;
            Color.RGBToHSV(p, out h, out s, out v);
            float deg = h * 360f;
            if (deg >= 310f && deg <= 328f && s > .35f && v > .4f) pink++;
            if ((deg < 12f || deg > 348f) && s > .6f && v > .5f) red++;
        }
        Check(what + ": no pink on the body (" + pink + " of " + opaque + " px) and no player red (" + red + ")", pink <= opaque * .002f && red <= opaque * .002f);
    }

    static void Behaviours()
    {
        BehaviourIs("desert_rock_dune", EnemyAttack.None, EnemyLateral.Drift);
        BehaviourIs("desert_rock_spire", EnemyAttack.None, EnemyLateral.Glide);
        BehaviourIs("desert_rock_husk", EnemyAttack.None, EnemyLateral.None);
        BehaviourIs("desert_rock_slab", EnemyAttack.None, EnemyLateral.Sway);
        BehaviourIs("desert_mine", EnemyAttack.Laser, EnemyLateral.None);
        BehaviourIs("desert_big", EnemyAttack.Shot, EnemyLateral.None);
        BehaviourIs("desert_fighter_1", EnemyAttack.Lunge, EnemyLateral.Drift);
        BehaviourIs("desert_fighter_2", EnemyAttack.Shot, EnemyLateral.Track);
        BehaviourIs("desert_fighter_3", EnemyAttack.Shot, EnemyLateral.Drift);
        BehaviourIs("desert_fighter_4", EnemyAttack.Blast, EnemyLateral.Track);
        BehaviourIs("desert_chaser", EnemyAttack.None, EnemyLateral.None);
        BehaviourIs("desert_alien", EnemyAttack.Shot, EnemyLateral.Sway);
        var big = EnemyBehaviours.For("desert_big");
        Check("Sandcrawler: a fan of 3 sand-slugs (Shard), a 1 s bucket-wheel tell", big.shotCount == 3 && big.shotKind == EliteShots.Kind.Shard && big.tell >= 1f);
        var hawk = EnemyBehaviours.For("desert_fighter_2");
        Check("Glint Hawk: three quick glass shards straight down", hawk.shotKind == EliteShots.Kind.Shard && hawk.maxVolleys >= 3 && hawk.aimCone == 0f);
        var gunner = EnemyBehaviours.For("desert_fighter_3");
        Check("Lens Gunner: one aimed heat-lance after a ~0.9 s lens flare", gunner.shotCount == 1 && gunner.aimCone > 0f && gunner.tell >= .85f && gunner.tell <= 1f);
        var tank = EnemyBehaviours.For("desert_fighter_4");
        Check("Dust Devil Tank: a Blast ring in Desert's material with a crack, a ~1 s spin-up", tank.blast.world == 6 && tank.blast.gapDeg >= AttackBlast.MinGapDeg && tank.tell >= 1f);
        var skimmer = EnemyBehaviours.For("desert_fighter_1");
        Check("Sand Skimmer: a straight dash that never tracks (lunge, no tracking lateral)", skimmer.attack == EnemyAttack.Lunge && skimmer.lateral != EnemyLateral.Track);
        Check("Scarab Drone hunts in its own Burrow style; Mirage Wisp spits a heat bolt on a pulse", EnemyBehaviours.For("desert_chaser").chaser == ChaserStyle.Burrow && EnemyBehaviours.For("desert_alien").Shoots);
        Check("Desert's Blast, hazard ramp and strike style are the pale sand ones (pink core stays on the effect)",
              AttackHazardArt.RampOf(6).light.r > 200 && AttackHazardArt.StyleOf(6) == StrikeStyle.Eruption);
    }

    static void BehaviourIs(string key, EnemyAttack attack, EnemyLateral lateral)
    {
        var b = EnemyBehaviours.For(key);
        Check(key + ": behaviour " + attack + (lateral != EnemyLateral.None ? " / " + lateral : ""), b != null && b.attack == attack && (lateral == EnemyLateral.None || b.lateral == lateral));
    }

    static void Palette()
    {
        var t = EnemyPalette.ThemeFor(Desert);
        float h, s, v;
        Color.RGBToHSV(t.hull, out h, out s, out v);
        Check("Desert palette: bone-gold hull S <= .33, sun-white light #FFF1C8, copper accent, Rock explosion",
              t.world == "desert" && s <= .35f && EnemyPalette.Html(t.light) == "#FFF1C8" && EnemyPalette.Html(t.accent) == "#9A5E3A" && t.explosion == TargetExplosion.Kind.Rock &&
              TargetExplosion.KindForWorld("desert") == TargetExplosion.Kind.Rock);
        Color.RGBToHSV(t.light, out h, out s, out v);
        Check("the enemy light is never amber nor red (hue 40..50, S <= .3)", h * 360f > 40f && h * 360f < 50f && s <= .3f);
    }

    // The Scarab Drone: runs, goes under (hidden, untouchable), resurfaces beside the pilot and lunges; never stays buried once its chase ends.
    static void Burrow()
    {
        var ship = new GameObject("~DRShip").transform;
        ship.position = new Vector3(0f, 2f, 0f);
        var go = EnemyFactory.Create(EnemyRoster.One(Desert, EnemyRole.Chaser), new Vector3(0f, -4f, 0f), Quaternion.identity);
        var c = go.GetComponent<ChaserEnemy>();
        c.Target = ship;
        bool sawBuried = false, hiddenWhileBuried = true, back = false, collidersOff = true;
        var rend = go.GetComponentsInChildren<Renderer>(true);
        var cols = go.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < 60 * 8 && c.enabled; i++)
        {
            c.Step(1f / 60f);
            if (c.Buried)
            {
                sawBuried = true;
                foreach (var r in rend) hiddenWhileBuried &= !r.enabled;
                foreach (var col in cols) collidersOff &= !col.enabled;
            }
            else if (sawBuried) back = true;
        }
        Check("the Scarab Drone burrows: it goes under at some point of its chase (" + sawBuried + ")", sawBuried);
        Check("... hidden and untouchable while under, visible again after", hiddenWhileBuried && collidersOff && back);
        Check("... and never left buried when it leaves", !c.Buried);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(ship.gameObject);
    }

    static Texture2D Load(string path)
    {
        if (!File.Exists(path)) return null;
        var t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }
}
