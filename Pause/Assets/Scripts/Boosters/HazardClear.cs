using System.Collections.Generic;
using UnityEngine;

// The HAZARD CLEAR booster's effect: AUTOMATIC. A held charge fires by itself when a hit would cost a heart
// (collisionDetection, the one place a heart is lost): the hit is absorbed, the heart is kept, the charge is spent, the hazards on
// screen are wiped for no credit, and the usual post-hit i-frames (PlayerInvuln) follow. The fatal hit is saved too. A hit that
// costs no heart (shield, Hard Shell, Cloak, i-frames) never reaches this. There is no button: the game is one-finger.
//
// WHAT IT CLEARS (everything the view holds that can hurt the ship, bar the bosses' and elites' own bodies):
//   * rocks and asteroids (tag Astr, Space's FieldAsteroid rocks without a split),
//   * rail mines, fighters, heavies, aliens, chasers (tag Enimey: the roster bodies),
//   * every hostile projectile in flight (HostileShots.ClearIn: boss and elite shots too, like a Shield Pulse; landed pools stay).
// WHAT IT LEAVES: bosses and their hitboxes, elites and their drones and shot hitboxes, backdrop rocks (scenery), the world's weather,
// pickups, and area attacks already drawn (jets, waves, blast rings, strikes, lashes: they are timed pulses, not bodies).
// Bosses and elites take NO damage from it.
//
// NO CREDIT. It never calls collisionDetection.AwardDestroyedTarget, so a cleared body pays no run score, no kill chain, no
// combo or DEATH COMBO roll, no kill achievement, no codex entry, no secret-meter charge, no adaptive-difficulty kill and no star
// dust. It is not a weapon kill: ClearTarget.Release + Destroy only.
//
// LOOK. The existing pixel-art flipbooks (WeaponFx / TargetExplosion): each body's own burst in its material (white, no ship tint) and
// one thin ring from the ship in the world's cue colour (WorldCue.Trace: pink only in Space). No outline, no new sprite.
public static class HazardClear
{
    public const int MaxAudio = 3;
    public const int MaxBursts = 14;

    // SwitchedOff: charges are held but the player switched Hazard Clear off in the Shop (Boosters.SetOn), so it never auto-fires.
    public enum Block { None, NoCharge, NotInRun, Dead, Scripted, SwitchedOff }

    // Per run (tests, the death panel could read them).
    public static int UsedThisRun { get; private set; }
    public static int LastBodies { get; private set; }
    public static int LastShots { get; private set; }

    static readonly List<ClearTarget> buffer = new List<ClearTarget>(48);

    public static void BeginRun() { UsedThisRun = 0; LastBodies = LastShots = 0; }

    public static Block BlockedBy()
    {
        if (!score.paysRealDust || startMenu.youAreInTutorial) return Block.NotInRun;   // the tutorial and practice runs
        if (buttonClicks.playerDied || DeathCrash.Running) return Block.Dead;
        if (WorldEntry.Active || Liftoff.HoldsShip || Planetfall.HoldsShip || PortalArrival.HoldsShip || WorldTransition.InProgress ||
            BossEncounter.ScriptedEntrance || ShipPowerController.CinematicClearActive) return Block.Scripted;
        if (Boosters.ClearCharges <= 0) return Block.NoCharge;
        if (!Boosters.IsOn(Boosters.Kind.HazardClear)) return Block.SwitchedOff;
        return Block.None;
    }

    // Is this registered target one the clear removes? (Pure on the object: tests build fixtures.)
    public static bool IsClearable(ClearTarget t)
    {
        if (!ShipTargets.IsThreat(t)) return false;
        if (t.Elite != null || t.Mother != null || t.IsShotHitbox) return false;
        var go = t.gameObject;
        if (go.name.StartsWith("Boss")) return false;
        if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out EliteShip _)) return false;
        // Anything else that answers to a ship attack (elite shots, backdrop rocks) is not an ordinary body; the field rocks are.
        if (go.TryGetComponent(out IShipAttackTarget _) && !go.TryGetComponent(out FieldAsteroid _)) return false;
        return true;
    }

    // The bodies a clear would take right now (in view).
    public static int Collect(List<ClearTarget> into)
    {
        var all = new List<ClearTarget>(48);
        ShipTargets.Collect(all, 0f);
        into.Clear();
        for (int i = 0; i < all.Count; i++) if (IsClearable(all[i])) into.Add(all[i]);
        return into.Count;
    }

    // A hit that would cost a heart calls this. True: the hit is absorbed (a charge was spent, the clear ran from `from`, the i-frames
    // began) and the caller must lose no heart. False: blocked or no charge, the hit lands normally.
    public static bool AutoSave(Vector3 from)
    {
        if (BlockedBy() != Block.None) return false;
        Rect view = ShipTargets.View();
        int bodies = Collect(buffer);
        if (!Boosters.ConsumeClear()) return false;
        PlayerInvuln.BeginPostHit();   // the same 2 s window a lost heart gives: the hazard that is still there cannot hit again
        UsedThisRun++;
        int world = Mathf.Max(0, WorldManager.CurrentIndex);
        int ship = ShipId.Equipped();
        int bursts = 0, heard = 0;
        for (int i = 0; i < buffer.Count; i++)
        {
            var t = buffer[i];
            if (t == null) continue;
            var go = t.gameObject;
            Vector3 at = go.transform.position;
            if (bursts < MaxBursts && !TargetExplosion.Crowded) { Burst(go, at, ship); bursts++; }
            if (heard < MaxAudio) { EnemyDeathAudio.Play(go); heard++; }
            Remove(go);
        }
        LastBodies = bodies;
        LastShots = HostileShots.ClearIn(view);
        Ring(from, view, world, ship);
        Word("CLEAR", from, WorldCue.Trace(world));
        return true;
    }

    static void Remove(GameObject go)
    {
        var rock = go.GetComponent<FieldAsteroid>();
        if (rock != null) { rock.Vanish(); return; }
        ClearTarget.Release(go);
        if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
    }

    // The body's own pixel-art burst: white (the flipbook's own colours), no ship-coloured flash on top.
    static void Burst(GameObject go, Vector3 at, int ship)
    {
        var kind = TargetExplosion.KindFor(go);
        var size = TargetExplosion.SizeFor(go);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Explosion, at, TargetExplosion.WorldSizeFor(size, kind), ship, kind,
            Color.white, TargetExplosion.HoldFor(size), 66);
    }

    // One thin pixel ring out of the ship in the world's cue colour, as wide as the view is tall.
    static void Ring(Vector3 from, Rect view, int world, int ship)
    {
        Color tint = WorldCue.Trace(world);
        tint.a = .7f;
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Ring, from, Mathf.Min(view.height * 1.1f, 30f), ship, TargetExplosion.KindForWorld(null), tint, 1.6f, 65);
    }

    static void Word(string text, Vector3 at, Color colour)
    {
        var hud = ScoreHud.Current;
        if (hud != null) hud.ShowWord(text, at + Vector3.up * 0.8f, colour, 28);
    }
}
