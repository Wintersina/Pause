using System.Collections.Generic;
using UnityEngine;

// One resolver per kind of codex entry, each reading the very loader and
// timing table the game itself animates that thing with -- so art dropped in
// through those loaders later (new enemy strips, boss sheets, atom frames,
// hull sheets) animates here with no codex work:
//
//   Enemy   EnemyArt.Frames + EnemyRoster idle/tell ticks (EnemyFlipbook's
//           loop); rocks turn or sway like AsteroidSpin
//   Mine    the same, through EnemyArt -> RailMineArt (dormant + waking
//           blink; the arming tell waking -> charging)
//   Boss    BossArt.Body idle 0..3 on BossArt.IdleTicks; tells: each drawn
//           pose's two frames, then the fire frame
//   Atom    PickupArt.Frames on PickupArt.IdleTicks; the green atom keeps its
//           original drawing and plays its light overlays on top
//   Ship    ShipHullArt stock sheet, idle drawings on ShipHullArt's tick
//           table; the spinners (Ninja, UFO) turn. The detail view cycles
//           through the hull's other colours (ShipSkins) as its "tell"
//   World   the world's sky tile, slowly panned inside the round mask
//   Portal  TeleportPortalSprites' 16 frames at Portal's 15 fps, turning
//   Log     static story art
//
// Built once per entry and cached (Sprite arrays included), so binding a
// card is a dictionary lookup and ticking it allocates nothing.
public static class CodexAnimations
{
    public const float PortalFps = 15f;
    public const float PortalSpin = 40f;          // degrees / s (the in-game ring turns 90)
    public const float ShipSpin = 90f;            // degrees / s, a calm version of the 300 in flight
    public const float RockSpin = 24f;            // degrees / s (AsteroidSpin rolls 15-60)
    public const float WorldDriftPeriod = 28f;    // seconds per slow pan
    public const float WorldOverscan = 1.15f;
    public const float BossTellHoldTicks = 14f;   // the wound-up pose, before the fire frame

    static readonly Dictionary<CodexEntry, CodexAnimation> cache = new Dictionary<CodexEntry, CodexAnimation>();

    // The entry's animation (a static one if its loader has nothing to play).
    public static CodexAnimation For(CodexEntry e)
    {
        if (e == null) return null;
        CodexAnimation a;
        if (cache.TryGetValue(e, out a) && a != null && !a.Stale && (a.HasArt || e.Sprite == null)) return a;
        a = Resolve(e);
        if (a == null || !a.HasArt) a = CodexAnimation.Static(KindOf(e), e.Sprite);
        cache[e] = a;
        return a;
    }

    public static CodexAnimKind KindOf(CodexEntry e)
    {
        if (e == null) return CodexAnimKind.Static;
        switch (e.category)
        {
            case CodexCategory.Log: return CodexAnimKind.Log;
            case CodexCategory.Atoms: return CodexAnimKind.Atom;
            case CodexCategory.Ships: return CodexAnimKind.Ship;
            case CodexCategory.Worlds: return e.id == CodexCatalogue.PortalId ? CodexAnimKind.Portal : CodexAnimKind.World;
        }
        if (BossCatalog.Find(e.id) != null) return CodexAnimKind.Boss;
        if (EliteCatalog.FindByCodexId(e.id) != null) return CodexAnimKind.Enemy;
        var def = EnemyRoster.FindByCodexId(e.id);
        if (def != null) return def.role == EnemyRole.Mine ? CodexAnimKind.Mine : CodexAnimKind.Enemy;
        return CodexAnimKind.Static;
    }

    // Fresh from the loaders (no cache). Null when there is nothing to play.
    public static CodexAnimation Resolve(CodexEntry e)
    {
        switch (KindOf(e))
        {
            case CodexAnimKind.Enemy:
            case CodexAnimKind.Mine:
                var elite = EliteCatalog.FindByCodexId(e.id);
                if (elite != null) return Elite(elite);
                return Enemy(EnemyRoster.FindByCodexId(e.id));
            case CodexAnimKind.Boss: return Boss(BossCatalog.Find(e.id));
            case CodexAnimKind.Atom: return Atom(e);
            case CodexAnimKind.Ship: return Ship(CodexCatalogue.ShipIndex(e.id));
            case CodexAnimKind.World: return World(CodexCatalogue.WorldIndex(e.id));
            case CodexAnimKind.Portal: return Portal();
            default: return Log(e);
        }
    }

    // ---- Enemies and rail mines: EnemyArt + EnemyFlipbook's timing ----

    public static CodexAnimation Enemy(EnemyDef def)
    {
        if (def == null) return null;
        var frames = EnemyArt.Frames(def);
        if (frames == null || frames.Length == 0) return null;

        var ticks = EnemyRoster.IdleTicks(def.role);
        int n = Mathf.Min(EnemyRoster.TellFrame, frames.Length);
        var steps = new Sprite[n];
        var holds = new float[n];
        for (int i = 0; i < n; i++)
        {
            steps[i] = frames[i];
            holds[i] = ticks[Mathf.Min(i, ticks.Length - 1)] * EnemyFlipbook.TickSeconds;
        }
        var a = CodexAnimation.Loop(def.role == EnemyRole.Mine ? CodexAnimKind.Mine : CodexAnimKind.Enemy, steps, holds);
        if (a == null) return null;

        if (frames.Length >= EnemyRoster.TellFrame + 2)
        {
            var tt = EnemyRoster.TellTicks(def.role);
            a.AddTell(new[] { frames[EnemyRoster.TellFrame], frames[EnemyRoster.TellFrame + 1] },
                      new[] { tt[0] * EnemyFlipbook.TickSeconds, tt[Mathf.Min(1, tt.Length - 1)] * EnemyFlipbook.TickSeconds });
        }
        var mode = EnemyFlipbook.ModeFor(def.role);
        // Mines arm, aliens chomp and the chaser lunges in a loop; the others wind up once.
        a.tellRepeats = mode == EnemyFlipbook.TellMode.NearPlayer || mode == EnemyFlipbook.TellMode.Chasing ? 3 : 1;
        switch (def.role)
        {
            case EnemyRole.Rock: a.tellGap = new Vector2(3f, 7f); break;
            case EnemyRole.Big: a.tellGap = new Vector2(3f, 5.5f); break;
            default: a.tellGap = new Vector2(2.5f, 5f); break;
        }
        if (def.role == EnemyRole.Rock)
        {
            // AsteroidSpin: a floating chunk of ground rocks upright, the rest roll.
            if (def.floating)
            {
                a.swayDegrees = EnemyRoster.FloatSwayDegrees;
                a.swayPeriod = EnemyRoster.FloatSwayPeriod;
            }
            else a.spinDegreesPerSecond = RockSpin;
        }
        return a.Finish();
    }

    // ---- Elite ships: EliteArt's strip, EliteShip's idle timing ----

    public static CodexAnimation Elite(EliteDef def)
    {
        if (def == null) return null;
        var frames = EliteArt.Frames(def);
        var cells = def.cells;
        if (frames == null || frames.Length < cells.Count) return null;
        // the def's cell map: the flight loop (or a weave through its banks)
        var loop = cells.CodexLoop;
        var steps = new Sprite[loop.Length];
        var holds = new float[loop.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            steps[i] = frames[loop[i]];
            holds[i] = cells.CodexHold(i) * EliteArt.Tick;
        }
        var a = CodexAnimation.Loop(CodexAnimKind.Enemy, steps, holds);
        if (a == null) return null;
        // the tell then the action, as it attacks (no such cells: its launch)
        var beat = cells.CodexTell;
        var tellSteps = new Sprite[beat.Length];
        var tellHolds = new float[beat.Length];
        for (int i = 0; i < beat.Length; i++)
        {
            tellSteps[i] = frames[beat[i]];
            tellHolds[i] = cells.tell >= 0 ? (i == 0 ? def.tellSeconds : Mathf.Max(.2f, def.actionSeconds)) : (i == beat.Length - 1 ? .5f : .25f);
        }
        a.AddTell(tellSteps, tellHolds);
        a.tellGap = new Vector2(2.5f, 4.5f);
        return a.Finish();
    }

    // ---- Bosses: BossArt's sheet and tick tables ----

    public static CodexAnimation Boss(BossDef boss)
    {
        if (boss == null) return null;
        int n = BossArt.IdleFrames;
        var steps = new Sprite[n];
        var holds = new float[n];
        for (int i = 0; i < n; i++)
        {
            steps[i] = BossArt.Body(boss, BossArt.Idle0 + i);
            holds[i] = BossArt.IdleTicks[Mathf.Min(i, BossArt.IdleTicks.Length - 1)] * BossArt.Tick;
        }
        var a = CodexAnimation.Loop(CodexAnimKind.Boss, steps, holds);
        if (a == null) return null;
        // Each drawn tell pose in turn: anticipation, the held wind-up, fire.
        for (int pose = 0; pose < 3; pose++)
            a.AddTell(new[] { BossArt.Body(boss, BossArt.Tell(pose, 0)), BossArt.Body(boss, BossArt.Tell(pose, 1)),
                              BossArt.Body(boss, BossArt.Fire) },
                      new[] { BossArt.TellInTicks * BossArt.Tick, BossTellHoldTicks * BossArt.Tick, BossArt.FireTicks * BossArt.Tick });
        a.tellGap = new Vector2(3f, 5f);
        return a.Finish();
    }

    // ---- Atoms: PickupArt's idle flipbooks ----

    public static bool TryPickupKind(string id, out PickupKind kind)
    {
        switch (id)
        {
            case "atom_stardust": kind = PickupKind.DustSmall; return true;
            case "atom_bigstar": kind = PickupKind.Dust; return true;
            case "atom_blue": kind = PickupKind.Shield; return true;
            case "atom_red": kind = PickupKind.Pause; return true;
            case "atom_green": kind = PickupKind.Heal; return true;
            case CodexCatalogue.VioletAtomId: kind = PickupKind.Cooldown; return true;
        }
        kind = PickupKind.Shield;
        return false;
    }

    static Sprite healBase;

    public static CodexAnimation Atom(CodexEntry e)
    {
        PickupKind kind;
        if (e == null || !TryPickupKind(e.id, out kind)) return null;
        var ticks = PickupArt.IdleTicks(kind);
        var frames = PickupArt.Frames(PickupArt.IdleName(kind), ticks.Length);
        var holds = new float[ticks.Length];
        for (int i = 0; i < ticks.Length; i++) holds[i] = ticks[i] * PickupArt.Tick;
        var a = CodexAnimation.Loop(CodexAnimKind.Atom, frames, holds);
        if (a == null) return null;
        if (kind == PickupKind.Heal)
        {
            // PickupFlipbook draws the green atom's loop as overlays on its
            // untouched original, cropped and sized as HealAtom spawns it.
            a.SetUnder(HealBase() ?? e.Sprite);
        }
        return a.Finish();
    }

    static Sprite HealBase()
    {
        if (healBase != null) return healBase;
        var tex = Resources.Load<Texture2D>("Pickups/heal_atom_green");
        if (tex == null) return null;
        healBase = Sprite.Create(tex, HealAtom.ArtRect(tex.width, tex.height), new Vector2(.5f, .5f), 180f);
        healBase.name = "codex_heal_base";
        return healBase;
    }

    // ---- Ships: the stock hull sheet's idle loop ----

    public static CodexAnimation Ship(int id)
    {
        if (id < 0 || !ShipHullArt.Has(id)) return null;
        // ShipHullArt's table, one step per run of ticks on the same drawing.
        var steps = new List<Sprite>();
        var holds = new List<float>();
        IdleLoop(id, ShipSkins.Stock, steps, holds);
        var a = CodexAnimation.Loop(CodexAnimKind.Ship, steps.ToArray(), holds.ToArray());
        if (a == null) return null;
        if (ShipExhaust.UsesWind(id)) a.spinDegreesPerSecond = ShipSpin;

        // Detail view: one "tell" that shows off every other colour in turn
        // (ShipSkins), each for ShipColourLoops idle loops, then back to stock.
        var colourSteps = new List<Sprite>();
        var colourHolds = new List<float>();
        for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
            for (int k = 0; k < ShipColourLoops; k++) IdleLoop(id, skin, colourSteps, colourHolds);
        if (colourSteps.Count > 0)
        {
            a.AddTell(colourSteps.ToArray(), colourHolds.ToArray());
            a.tellGap = ShipColourGap;
        }
        return a.Finish();
    }

    // How many idle loops each extra colour shows for in the detail view,
    // and the stock colour's stay between rounds.
    public const int ShipColourLoops = 2;
    public static readonly Vector2 ShipColourGap = new Vector2(1.5f, 2.5f);

    // One idle loop of ship `id` in colour `skin`, as held steps.
    static void IdleLoop(int id, int skin, List<Sprite> steps, List<float> holds)
    {
        int loop = ShipHullArt.IdleLoopTicks;
        int last = -1;
        for (int t = 0; t < loop; t++)
        {
            int drawing = ShipHullArt.IdleDrawingAt(t + .5f);
            if (drawing == last) { holds[holds.Count - 1] += 1f / ShipHullArt.TicksPerSecond; continue; }
            last = drawing;
            steps.Add(ShipHullArt.Get(id, skin, 0, drawing));
            holds.Add(1f / ShipHullArt.TicksPerSecond);
        }
    }

    // ---- Worlds: the sky tile, panned slowly in the round window ----

    static readonly Dictionary<int, Sprite> skies = new Dictionary<int, Sprite>();

    public static CodexAnimation World(int index)
    {
        if (index < 0) return null;
        Sprite sky;
        if (!skies.TryGetValue(index, out sky) || sky == null)
        {
            var tex = SkyTexture(index);
            if (tex == null) return null;
            sky = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f);
            sky.name = "codex_sky_" + index;
            skies[index] = sky;
        }
        var a = CodexAnimation.Loop(CodexAnimKind.World, new[] { sky }, new[] { 1f });
        a.fit = CodexAnimation.FitMode.Cover;
        a.driftPeriod = WorldDriftPeriod;
        a.overscan = WorldOverscan;
        return a.Finish();
    }

    // The same textures CodexCatalogue crops for the static square.
    static Texture2D SkyTexture(int index)
    {
        if (index == 0)
        {
            var refs = Resources.Load<CodexArtRefs>("Codex/CodexArtRefs");
            return refs != null ? refs.spaceBackdrop : null;
        }
        var specs = BackdropCatalog.All;
        if (index >= specs.Length) return null;
        return Resources.Load<Texture2D>(BackdropCatalog.Folder(specs[index].world) + "sky");
    }

    // ---- The portal: its authored frames, as Portal plays them ----

    public static CodexAnimation Portal()
    {
        int n = TeleportPortalSprites.FrameCount;
        var steps = new Sprite[n];
        var holds = new float[n];
        for (int i = 0; i < n; i++)
        {
            steps[i] = TeleportPortalSprites.FrameAt(i);
            holds[i] = 1f / PortalFps;
        }
        var a = CodexAnimation.Loop(CodexAnimKind.Portal, steps, holds);
        if (a == null) return null;
        a.spinDegreesPerSecond = PortalSpin;
        return a.Finish();
    }

    // ---- The Pilot's Log: one painted image each ----

    public static CodexAnimation Log(CodexEntry e)
    {
        return CodexAnimation.Static(KindOf(e), e != null ? e.Sprite : null);
    }
}
