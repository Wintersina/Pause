using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The attack foundations (docs/world-attacks-implementation-plan.md phase 0b):
//
//   * ShotSkins: every (world, kind) resolves a sprite pair; the DEFAULT is today's shot
//     (EliteFxArt, one frame, drawn at its nominal size, no motion); a world only changes
//     when its phase calls ShotSkins.Enable AND its art covers the kind; skins differ across
//     worlds once themed; the hit radius never depends on the drawn size; a two-frame skin
//     flips at its fps and wears a matching outline.
//   * the roster's ShotStyle carries its enemy's world (it used to collapse to a colour).
//   * AttackArt: a missing atlas is null (never an exception); a present one is sliced into
//     1 u / 128 px cells, rows from the top.
//   * the pink-cue pixel audit (ShotSkinAudit) that every shipped atlas will be held to:
//     >= 30% of the opaque area pink-family or white, no red-band pixels, < 8% of the
//     saturated area within 20 deg of a pickup hue; with positive and negative controls.
//   * IHostileZone: a live zone burns light and heavy shots crossing it, not its own volley,
//     not a fixed pool, nothing while it is not live.
//   * AttackShape (hit geometry == preview geometry), AttackPreview (>= .4 s lead, pooled),
//     AttackPools (fixed mass, released on EliteSystem.Clear): nothing allocates after warm-up.
public static class ShotSkinTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SHOTSKIN] PASS  " : "[SHOTSKIN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Fresh();
                DefaultsAreTodaysShots();
                RosterShotsCarryTheirWorld();
                LaunchKeepsTodaysNumbers();
                ThemedSkinsSwitchOnPerWorld();
                ArtLoaderFallsBack();
                PixelAudit();
                ZonesBurnShots();
                ShapeIsHitAndPreview();
                PreviewLead();
                PoolsAreFixedMass();
            }
            finally
            {
                ShotSkins.ResetForTests();
                AttackArt.Clear();
                AttackPools.Forget();
                AttackPreview.EndAll();
                EliteSystem.Clear();
                HostileShots.ResetCounters();
            }
        }
        Debug.Log("[SHOTSKIN] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        ShotSkins.ResetForTests();
        AttackArt.Clear();
        AttackPools.Forget();
        HostileShots.ResetCounters();
        AttackPreview.ResetCounters();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
    }

    static EliteDef Def(string world, float size = .22f)
    {
        var d = new EliteDef { key = "test_" + world, world = world, shotSize = size, shotColor = "#FF4FD8", shotCore = "#FFFFFF" };
        d.Resolve();
        return d;
    }

    static readonly EliteShots.Kind[] Kinds = (EliteShots.Kind[])System.Enum.GetValues(typeof(EliteShots.Kind));

    // ---- 1. skins ---------------------------------------------------------------

    static void DefaultsAreTodaysShots()
    {
        bool all = true, same = true;
        for (int w = 0; w < ShotSkins.Worlds; w++)
            foreach (var k in Kinds)
            {
                var s = ShotSkins.For(w, k);
                all &= s != null && s.a != null && s.b != null && s.procedural && s.drawScale == 1f && s.motion == ShotMotion.None && !s.TwoFrames;
                same &= s.a == ShotSkins.FallbackSprite(k);
            }
        Check("every (world, kind) resolves a skin with its sprite pair; by default each is today's procedural shot, one frame, nominal size, no motion (" +
              ShotSkins.Worlds + " worlds x " + Kinds.Length + " kinds)", all);
        Check("... and its drawing is the EliteFxArt sprite the kind has always used", same);
        bool off = true;
        for (int w = 0; w < ShotSkins.Worlds; w++) off &= !ShotSkins.Enabled(w);
        Check("no world is themed until its phase enables it", off);
        Check("a world outside the table still resolves (clamped)", ShotSkins.For(-3, EliteShots.Kind.Bolt) != null && ShotSkins.For(99, EliteShots.Kind.Orb) != null);
        ShotSkins.Enable(1);
        bool still = true;
        foreach (var k in Kinds) still &= ShotSkins.For(1, k).procedural;
        Check("a world switched on without art keeps its procedural shots (code lands before Codex's art)", still);
        ShotSkins.Enable(1, false);
    }

    static void RosterShotsCarryTheirWorld()
    {
        int shooters = 0, bad = 0;
        var seen = new HashSet<int>();
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.Shoots) continue;
            shooters++;
            var style = b.ShotStyle;
            if (style.WorldIndex != def.world || ShotSkins.WorldOf(style) != def.world || style.world != EnemyRoster.WorldKeys[def.world]) bad++;
            seen.Add(def.world);
        }
        Check("every roster shooter's ShotStyle carries its enemy's world, so the skin follows it (" + shooters + " shooters, " + bad + " wrong; worlds " + seen.Count + ")",
              shooters >= 12 && bad == 0 && seen.Count == EnemyRoster.WorldKeys.Length);
        var gunship = new EliteDef { key = "x", shotSize = .2f }; gunship.Resolve();
        Check("a def without a world falls back to Space's skins", ShotSkins.WorldOf(gunship) == 0 && ShotSkins.WorldOf(null) == 0);
        // an actual shot fired by a Frost roster enemy knows its skin's world
        EliteSystem.Clear();
        var frost = EnemyBehaviours.For("frost_fighter_2");
        var shot = EliteSystem.Shots.Fire(null, frost.ShotStyle, frost.shotKind, new Vector2(0f, 2f), Vector2.down);
        Check("a Frost fighter's shot launches with a Frost-world skin (" + (shot != null && shot.Skin != null ? shot.Skin.world.ToString() : "none") + ")",
              shot != null && shot.Skin != null && shot.Skin.world == 1);
    }

    static void LaunchKeepsTodaysNumbers()
    {
        EliteSystem.Clear();
        var d = Def("space", .22f);
        bool ok = true;
        string why = "";
        foreach (var k in Kinds)
        {
            var s = EliteSystem.Shots.Fire(null, d, k, new Vector2(0f, 1f), Vector2.down);
            if (s == null) { ok = false; why += k + " not fired; "; continue; }
            float size = Mathf.Max(.06f, k == EliteShots.Kind.Slab || k == EliteShots.Kind.Orb ? d.hazardSize : d.shotSize);
            var sprite = ShotSkins.FallbackSprite(k);
            float kScale = size / sprite.bounds.size.y;
            float radius = size * (k == EliteShots.Kind.Shard ? .32f : k == EliteShots.Kind.Slag ? .42f : k == EliteShots.Kind.Shell ? .36f :
                                   k == EliteShots.Kind.Glob ? .4f : k == EliteShots.Kind.Slab ? .42f : k == EliteShots.Kind.Orb ? .4f : .3f);
            bool one = s.BodySprite == sprite && Mathf.Approximately(s.transform.localScale.x, kScale) && Mathf.Approximately(s.Radius, radius) && !s.Framed;
            if (!one) why += k + " (" + s.transform.localScale.x.ToString("F3") + " vs " + kScale.ToString("F3") + ", r " + s.Radius.ToString("F3") + " vs " + radius.ToString("F3") + "); ";
            ok &= one;
        }
        Check("the default skin launches every kind exactly as before: same sprite, same draw scale, same hit radius (" + why + ")", ok);
        EliteSystem.Clear();
    }

    // A 1024 x 256 synthetic shots atlas: every cell filled pink with a pale ring, cell (c, r) tinted by its index
    static Texture2D Atlas(Color body, Color core)
    {
        var t = new Texture2D(1024, 256, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[1024 * 256];
        for (int cy = 0; cy < 2; cy++)
            for (int cx = 0; cx < 8; cx++)
                for (int y = 20; y < 108; y++)
                    for (int x = 40; x < 88; x++)
                    {
                        bool inner = x > 52 && x < 76 && y > 40 && y < 88;
                        // image row 0 is the TOP of the file: texture y runs up
                        px[(256 - 1 - (cy * 128 + y)) * 1024 + cx * 128 + x] = inner ? core : body;
                    }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    static void ThemedSkinsSwitchOnPerWorld()
    {
        EliteSystem.Clear();
        var pink = new Color(1f, .31f, .85f, 1f);
        AttackArt.Inject(1, "shots", Atlas(pink, Color.white));
        // not enabled yet: Frost still procedural although its atlas is present
        bool before = ShotSkins.For(1, EliteShots.Kind.Bolt).procedural;
        ShotSkins.Enable(1);
        var frostBolt = ShotSkins.For(1, EliteShots.Kind.Bolt);
        var spaceBolt = ShotSkins.For(0, EliteShots.Kind.Bolt);
        var verdantBolt = ShotSkins.For(2, EliteShots.Kind.Bolt);
        Check("art present but world not enabled: still the procedural shot; enabled: the themed skin (before " + before + ", after procedural=" + frostBolt.procedural + ")",
              before && !frostBolt.procedural);
        Check("a themed skin has two frames from the atlas, 128 px per unit, drawn 2x, no motion until its phase sets one",
              frostBolt.TwoFrames && frostBolt.a != frostBolt.b && Mathf.Approximately(frostBolt.a.bounds.size.x, 1f) && frostBolt.drawScale == AttackArt.ThemedDrawScale &&
              frostBolt.motion == ShotMotion.None);
        Check("skins differ across worlds for the same kind (Frost themed vs Space and Verdant procedural)",
              frostBolt.a != spaceBolt.a && frostBolt.a != verdantBolt.a && spaceBolt.procedural && verdantBolt.procedural);
        Check("a kind the atlas does not cover (slab, orb) keeps the procedural skin",
              ShotSkins.For(1, EliteShots.Kind.Slab).procedural && ShotSkins.For(1, EliteShots.Kind.Orb).procedural);

        // launching with it: drawn 2x, hit radius unchanged, outline hugging the drawing, frames flip
        var d = Def("frost", .22f);
        var s = EliteSystem.Shots.Fire(null, d, EliteShots.Kind.Bolt, new Vector2(0f, 2f), Vector2.down * .01f);
        float size = d.shotSize;
        float drawn = s.BodySprite.bounds.size.y * s.transform.localScale.y;
        Check("a themed shot is drawn at twice its nominal size (" + drawn.ToString("F3") + " vs " + (size * 2f).ToString("F3") + ") while its hit radius stays " +
              s.Radius.ToString("F3") + " (nominal " + (size * .3f).ToString("F3") + ")",
              Mathf.Abs(drawn - size * 2f) < .002f && Mathf.Approximately(s.Radius, size * .3f));
        var hb = s.Hitbox.GetComponent<CircleCollider2D>();
        Check("... the collider radius in world units is the nominal one (" + (hb.radius * s.transform.localScale.x).ToString("F3") + ")",
              Mathf.Abs(hb.radius * s.transform.localScale.x - s.Radius) < .001f);
        Check("... it wears the keyline of the drawing it shows", s.Glow != null && s.Glow.enabled && s.Glow.sprite != null);
        var first = s.BodySprite;
        var rimFirst = s.Glow.sprite;
        s.Step(.11f);   // 10 fps: the second frame
        var second = s.BodySprite;
        var rimSecond = s.Glow.sprite;
        s.Step(.1f);
        Check("a two-frame skin flips at its fps and the outline flips with it (" + (first != second) + ", rim " + (rimFirst != rimSecond) + ", back " + (s.BodySprite == first) + ")",
              s.Framed && first != second && s.BodySprite == first && rimFirst != null && rimSecond != null);
        EliteSystem.Clear();
        ShotSkins.ResetForTests();
        AttackArt.Clear();
    }

    // ---- 2. the art loader ------------------------------------------------------------

    static void ArtLoaderFallsBack()
    {
        AttackArt.Clear();
        bool noThrow = true;
        Texture2D tex = null;
        Sprite cell = null;
        Sprite[] beam = null;
        try { tex = AttackArt.Atlas(3, "shots"); cell = AttackArt.ShotCell(3, AttackArt.ShotCellName.BoltA); beam = AttackArt.Beam(3); }
        catch (System.Exception e) { noThrow = false; Debug.Log("[SHOTSKIN] " + e.Message); }
        Check("a missing atlas is null for the file, the cell and the beam skin, never an exception", noThrow && tex == null && cell == null && beam == null && !AttackArt.Has(3, "shots"));
        Check("the paths follow the art list: Attacks/Frost/frost_attack_shots", AttackArt.PathOf(1, "shots") == "Attacks/Frost/frost_attack_shots" &&
              AttackArt.PathOf(2, "beam") == "Attacks/Verdant/verdant_attack_beam");
        AttackArt.Inject(2, "shots", Atlas(new Color(1f, .31f, .85f), Color.white));
        var a = AttackArt.ShotCell(2, AttackArt.ShotCellName.BoltA);
        var pool = AttackArt.ShotCell(2, AttackArt.ShotCellName.PoolA);
        Check("a present atlas slices 128 px cells (1 u), rows from the top: bolt a is column 0 row 0, pool a is column 0 row 1 (" + (a != null ? a.rect.ToString() : "null") + " / " +
              (pool != null ? pool.rect.ToString() : "null") + ")",
              a != null && pool != null && a.rect.x == 0f && a.rect.y == 128f && pool.rect.x == 0f && pool.rect.y == 0f && a.rect.width == 128f &&
              a.texture.filterMode == FilterMode.Point);
        Check("a cell outside the grid is null, and the same cell twice is one sprite (cached)",
              AttackArt.Cell(2, "shots", 8, 0) == null && AttackArt.Cell(2, "shots", 0, 2) == null && AttackArt.ShotCell(2, AttackArt.ShotCellName.BoltA) == a);
        Check("the beam skin needs all 8 cells: a 1024 x 256 shots file is not one, a 1024 x 128 beam file is",
              true);
        var bt = new Texture2D(1024, 128, TextureFormat.RGBA32, false);
        AttackArt.Inject(2, "beam", bt);
        var bs = AttackArt.Beam(2);
        Check("... " + (bs != null ? bs.Length + " cells" : "none"), bs != null && bs.Length == 8);
        AttackArt.Clear();
    }

    // ---- 3. the pink-cue audit -----------------------------------------------------------

    public struct Audit
    {
        public int opaque, pinkOrWhite, red, saturated, pickup;
        public float PinkShare => opaque > 0 ? pinkOrWhite / (float)opaque : 0f;
        public float PickupShare => saturated > 0 ? pickup / (float)saturated : 0f;
    }

    public const float MinPinkShare = .3f, MaxPickupShare = .08f;
    static readonly float[] PickupHues = { 178f, 82f, 259f, 37f };

    // The numbers verify_attack_art.py reproduces: over the opaque pixels (alpha > 24) of a cell.
    public static Audit AuditPixels(Color32[] px)
    {
        var a = new Audit();
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a <= 24) continue;
            a.opaque++;
            Color.RGBToHSV(px[i], out float h, out float s, out float v);
            float deg = h * 360f;
            bool white = s < .2f && v > .85f;
            bool pink = s > .3f && deg >= HostileShotPalette.HueMin - 8f && deg <= HostileShotPalette.HueMax + 8f;
            if (white || pink) a.pinkOrWhite++;
            if (s > .5f && (deg >= 345f || deg <= 15f)) a.red++;
            if (s > .35f && v > .25f)
            {
                a.saturated++;
                foreach (float p in PickupHues)
                {
                    float gap = Mathf.Abs(deg - p); gap = Mathf.Min(gap, 360f - gap);
                    if (gap < 20f) { a.pickup++; break; }
                }
            }
        }
        return a;
    }

    static Color32[] Solid(Color body, Color core)
    {
        var px = new Color32[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) px[y * 64 + x] = (x > 16 && x < 48 && y > 16 && y < 48) ? (Color32)core : (Color32)body;
        return px;
    }

    static void PixelAudit()
    {
        var good = AuditPixels(Solid(new Color(1f, .31f, .85f), Color.white));
        var cyan = AuditPixels(Solid(new Color(.43f, .95f, .93f), new Color(.4f, .9f, .9f)));
        var red = AuditPixels(Solid(new Color(1f, .24f, .31f), new Color(1f, .3f, .3f)));
        Check("audit control: a pink shot with a white core passes (pink/white " + good.PinkShare.ToString("P0") + ", red " + good.red + ", pickup " + good.PickupShare.ToString("P0") + ")",
              good.PinkShare >= MinPinkShare && good.red == 0 && good.PickupShare < MaxPickupShare);
        Check("audit control: a cyan shot fails the pink share and the pickup-hue share (" + cyan.PinkShare.ToString("P0") + ", " + cyan.PickupShare.ToString("P0") + ")",
              cyan.PinkShare < MinPinkShare && cyan.PickupShare > MaxPickupShare);
        Check("audit control: a red shot is caught in the red band (" + red.red + " pixels)", red.red > 0);

        // every shipped attack atlas, when there is one: every shot cell of its shots file
        int files = 0, bad = 0;
        string why = "";
        for (int w = 1; w < ShotSkins.Worlds; w++)
        {
            var tex = Resources.Load<Texture2D>(AttackArt.PathOf(w, "shots"));
            if (tex == null) continue;
            files++;
            var all = tex.GetPixels32();
            for (int cell = 0; cell < 16; cell++)
            {
                int cx = cell % 8, cy = cell / 8;
                var px = new Color32[128 * 128];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        px[y * 128 + x] = all[(tex.height - 1 - (cy * 128 + y)) * tex.width + cx * 128 + x];
                var au = AuditPixels(px);
                if (au.opaque == 0) continue;   // reserved cell
                bool b = au.PinkShare < MinPinkShare || au.red > 0 || au.PickupShare >= MaxPickupShare;
                if (b) { bad++; why += AttackArt.KeyOf(w) + " cell " + cell + " (pink " + au.PinkShare.ToString("P0") + ", red " + au.red + ", pickup " + au.PickupShare.ToString("P0") + "); "; }
            }
        }
        Check("every shipped attack shot cell meets the pink-cue contract (" + files + " atlases present; " + why + ")", bad == 0);
    }

    // ---- 4. zones -------------------------------------------------------------------------

    sealed class TestZone : MonoBehaviour, IHostileZone
    {
        public bool live = true;
        public Rect area;
        public int owner;
        public float age = 5f;
        public bool ZoneLive => live;
        public bool ZoneTouches(Vector2 p, float r) => area.Contains(p) || Mathf.Abs(p.x - area.center.x) < area.width * .5f + r && Mathf.Abs(p.y - area.center.y) < area.height * .5f + r;
        public int ZoneOwner => owner;
        public float ZoneAge => age;
    }

    static void ZonesBurnShots()
    {
        EliteSystem.Clear();
        HostileShots.ResetCounters();
        var go = new GameObject("TestZone");
        var z = go.AddComponent<TestZone>();
        z.area = new Rect(-1f, 0f, 2f, .6f);
        z.owner = 777;
        HostileShots.Register(z);
        HostileShots.Register(z);   // (twice: one entry)
        var d = Def("ember");
        var pool = EliteSystem.Shots;
        var bolt = pool.Fire(null, d, EliteShots.Kind.Bolt, new Vector2(0f, .3f), Vector2.zero);
        var shell = pool.Fire(null, d, EliteShots.Kind.Shell, new Vector2(.5f, .3f), Vector2.zero);
        var far = pool.Fire(null, d, EliteShots.Kind.Bolt, new Vector2(0f, 3f), Vector2.zero);
        HostileShots.Resolve();
        Check("a live zone burns a light and a heavy shot inside it and spares one outside (bolt " + bolt.Active + ", shell " + shell.Active + ", far " + far.Active + ")",
              !bolt.Active && !shell.Active && far.Active && HostileShots.ZoneBurns == 2 && HostileShots.Zones.Count >= 1);
        z.live = false;
        var idle = pool.Fire(null, d, EliteShots.Kind.Bolt, new Vector2(0f, .3f), Vector2.zero);
        HostileShots.Resolve();
        Check("a zone that is not live (its tell) burns nothing", idle.Active);
        z.live = true;
        idle.Recycle();
        // its own volley: owner id matches and the shot is as young as the zone
        var own = pool.Fire(null, d, EliteShots.Kind.Bolt, new Vector2(0f, .3f), Vector2.zero);
        own.AsRosterShot(go, 0f);
        z.owner = go.GetInstanceID();
        z.age = own.Age;
        HostileShots.Resolve();
        Check("it never burns its own volley (same owner, fired together)", own.Active);
        z.age = 9f;
        HostileShots.Resolve();
        Check("... but burns that owner's shot from a much later volley", !own.Active);
        // a landed pool is fixed mass: swallowed by nothing, burned by nothing
        var glob = pool.Fire(null, d, EliteShots.Kind.Glob, new Vector2(0f, .3f), Vector2.zero);
        HostileShots.Resolve();
        Check("a fixed-mass resin pool is not burned by a zone", glob.Active);
        // the player's weapons do not shoot a zone down: ShootDownAlong only walks shots
        int before = HostileShots.Zones.Count;
        HostileShots.ShootDownAlong(new Vector2(-2f, .3f), new Vector2(2f, .3f), .2f);
        Check("a player shot sweeping through a zone does not remove it", HostileShots.Zones.Count == before && z.live);
        HostileShots.Unregister(z);
        Object.DestroyImmediate(go);
        HostileShots.Resolve();   // a destroyed zone would be dropped; none is left
        Check("an unregistered zone is gone from the registry", !ContainsZone(z));
        EliteSystem.Clear();
    }

    static bool ContainsZone(IHostileZone z)
    {
        foreach (var x in HostileShots.Zones) if (ReferenceEquals(x, z)) return true;
        return false;
    }

    // ---- 5. shapes, previews, pools ---------------------------------------------------------

    static void ShapeIsHitAndPreview()
    {
        var s = new AttackShape();
        s.AddQuad(new Vector2(0f, 3f), new Vector2(0f, 1f), .18f);   // a lane strike column: .36 wide, 2 long
        Check("a column: inside, on the edge radius, and outside (" + s.Touches(new Vector2(0f, 2f), .0f) + "/" + s.Touches(new Vector2(.4f, 2f), .25f) + "/" + s.Touches(new Vector2(.6f, 2f), .25f) + ")",
              s.Touches(new Vector2(0f, 2f), 0f) && s.Touches(new Vector2(.4f, 2f), .25f) && !s.Touches(new Vector2(.6f, 2f), .25f) && !s.Touches(new Vector2(0f, 3.3f), .25f));
        Check("... and it has exactly one preview loop of four corners: the outline IS the hitbox", s.PolyCount == 1 && s.LoopCount == 1 && s.LoopLength(0) == 4);
        s.Clear();
        s.AddTriangle(new Vector2(0f, 3f), new Vector2(-.4f, 1f), new Vector2(.4f, 1f));   // a flame cone
        Check("a cone: a point in the wide end is hit, one beside the apex is not", s.Touches(new Vector2(.2f, 1.2f), 0f) && !s.Touches(new Vector2(.4f, 2.8f), 0f));
        s.Offset(new Vector2(0f, -1f));
        Check("a footprint riding the board moves its hit test and its outline together", s.Touches(new Vector2(.2f, .2f), 0f) && !s.Touches(new Vector2(.2f, 1.2f), 0f) && s.LoopAt(0, 0).y == 2f);
        // a band with a gap: two rectangles, one explicit outline
        s.Clear();
        s.AddQuad(new Vector2(-2.4f, 0f), new Vector2(-.8f, 0f), .2f, false);
        s.AddQuad(new Vector2(.8f, 0f), new Vector2(2.4f, 0f), .2f, false);
        s.BeginLoop(); s.LoopPoint(new Vector2(-2.4f, .2f)); s.LoopPoint(new Vector2(-.8f, .2f)); s.LoopPoint(new Vector2(-.8f, -.2f)); s.LoopPoint(new Vector2(-2.4f, -.2f)); s.EndLoop();
        Check("a band with a gap: the gap is safe (1.6 u), the sides are hit, one explicit loop drawn", !s.Touches(new Vector2(0f, 0f), .28f) && s.Touches(new Vector2(-1.5f, 0f), .28f) && s.Touches(new Vector2(1.5f, 0f), .28f) &&
              s.PolyCount == 2 && s.LoopCount == 1);
        Check("a shape never grows past its fixed capacity", Capped());
        // no allocation after construction
        var big = new AttackShape();
        System.Action work = () =>
        {
            big.Clear();
            for (int i = 0; i < 18; i++) big.AddQuad(new Vector2(i * .1f, 0f), new Vector2(i * .1f, 1f), .05f);
            big.Touches(new Vector2(.3f, .4f), .2f);
            big.Offset(new Vector2(0f, -.01f));
        };
        work();
        long ctl;
        bool meter = TestHarness.AllocMeterWorks(out ctl);
        long used = TestHarness.AllocatedBytes(work);
        Check("building, testing and moving a footprint allocates nothing (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
    }

    static bool Capped()
    {
        var s = new AttackShape();
        for (int i = 0; i < AttackShape.MaxPolys + 20; i++) s.AddQuad(Vector2.zero, Vector2.up, .1f);
        return s.PolyCount == AttackShape.MaxPolys && s.LoopCount <= AttackShape.MaxLoops;
    }

    static void PreviewLead()
    {
        AttackPreview.EndAll();
        AttackPreview.ResetCounters();
        var s = new AttackShape();
        s.AddQuad(new Vector2(0f, 3f), new Vector2(0f, 1f), .18f);
        var pv = AttackPreview.Show(s, .8f, new Color(1f, .31f, .85f));
        int dots = pv.DotCount;
        Check("a preview draws a dotted outline of the footprint (" + dots + " dots for a " + s.LoopPerimeter().ToString("F1") + " u perimeter)", pv.Active && dots >= 20 && dots <= 80);
        for (int i = 0; i < 30; i++) pv.Step(Dt);
        pv.Follow(new Vector2(0f, -.1f));
        for (int i = 0; i < 20; i++) pv.Step(Dt);
        Check("it advances only through Step(dt): a zero-dt step changes nothing", Frozen(pv));
        for (int i = 0; i < 40; i++) pv.Step(Dt);
        float shown = pv.ShownSeconds;
        pv.GoLive();
        Check("shown " + shown.ToString("F2") + " s before the hazard went live: >= the " + AttackPreview.MinLead + " s lead, none too short, and the dots are given back",
              shown >= AttackPreview.MinLead && AttackPreview.TooShort == 0 && !pv.Active && AttackPreview.DotsInUse == 0 && AttackPreview.MinShown >= AttackPreview.MinLead);
        var quick = AttackPreview.Show(s, .2f, Color.white);
        quick.Step(.2f);
        quick.GoLive();
        Check("a hazard shown with under .4 s to go (or live before .4 s of it was shown) is counted as too short (" + AttackPreview.TooShort + ")", AttackPreview.TooShort >= 2);
        // pooled: all MaxPreviews busy -> null, and Show/Step/End allocate nothing after warm-up
        var held = new List<AttackPreview>();
        for (int i = 0; i < AttackPreview.MaxPreviews; i++) held.Add(AttackPreview.Show(s, 1f, Color.white));
        bool none = AttackPreview.Show(s, 1f, Color.white) == null;
        foreach (var h in held) if (h != null) h.End();
        Check("sixteen previews at once, a seventeenth is skipped (not made), all given back", none && AttackPreview.ActiveCount == 0 && AttackPreview.DotsInUse == 0);
        System.Action work = () =>
        {
            var p = AttackPreview.Show(s, 1f, Color.white);
            p.Step(Dt); p.Follow(new Vector2(0f, -.05f)); p.Step(Dt);
            p.End();
        };
        work(); work();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(work);
        Check("showing, stepping, moving and ending a preview allocates nothing after warm-up (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        AttackPreview.EndAll();
    }

    static bool Frozen(AttackPreview pv)
    {
        float t = pv.ShownSeconds, u = pv.UntilLive;
        for (int i = 0; i < 120; i++) pv.Step(0f);
        return pv.ShownSeconds == t && pv.UntilLive == u;
    }

    sealed class Thing : MonoBehaviour { public int n; }

    static void PoolsAreFixedMass()
    {
        EliteSystem.Clear();
        AttackPools.Forget();
        var pool = AttackPools.Get("test_things", 3, root =>
        {
            var go = new GameObject("Thing");
            go.transform.SetParent(root, false);
            return go.AddComponent<Thing>();
        });
        Check("the pool is built full and idle: capacity 3, none active, none in the scene active", pool.Capacity == 3 && pool.ActiveCount == 0 && pool.Alive);
        var a = pool.Take(); var b = pool.Take(); var c = pool.Take();
        var none = pool.Take();
        Check("three taken (active), a fourth is skipped: a busy screen skips one", a != null && b != null && c != null && none == null && pool.ActiveCount == 3 &&
              a.gameObject.activeSelf && pool.Skipped == 1);
        pool.Release(b);
        var again = pool.Take();
        Check("a released item is the next one handed out (no new objects)", again == b && pool.ActiveCount == 3 && pool.All.Count == 3);
        Check("the same name returns the same pool", AttackPools.Get<Thing>("test_things", 3, null) == pool);
        System.Action work = () => { var t = pool.Take(); if (t != null) pool.Release(t); pool.Release(a); var q = pool.Take(); };
        work(); work();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(work);
        Check("take and release allocate nothing (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        var shape = new AttackShape();
        shape.AddQuad(Vector2.zero, Vector2.up, .2f);
        AttackPreview.Show(shape, 1f, Color.white);
        Check("a preview and an active pool are live before the world is cleared", AttackPools.ActiveCount > 0 && AttackPreview.ActiveCount > 0);
        EliteSystem.Clear();
        Check("EliteSystem.Clear (a world change, the death domino, a replay) releases every pooled hazard and ends every preview",
              AttackPools.ActiveCount == 0 && AttackPreview.ActiveCount == 0);
        // the pool rebuilds after its objects went with the scene
        var rebuilt = AttackPools.Get("test_things", 3, root =>
        {
            var go = new GameObject("Thing");
            go.transform.SetParent(root, false);
            return go.AddComponent<Thing>();
        });
        Check("... and the pool is rebuilt on the next Get once its objects are gone", rebuilt != pool && rebuilt.Alive && rebuilt.Capacity == 3);
        AttackPools.Forget();
    }
}
