using UnityEditor.SceneManagement;
using UnityEngine;

// "A very small, quiet, short pew whenever an enemy or a boss fires a projectile."
//
//   ONCE      one hostile shot (roster / elite pool, boss pool) plays exactly one laser; a shot that only splits or
//             bursts into shards (Fire(..., pew: false)) plays none
//   BURST     one source firing rapidly (3+ shots, each < BurstGap after the last) plays the gun, chained at GunInterval
//   RATE      a same-frame barrage collapses to one sound; at most MaxPerWindow per window
//   EFFECTS   silent (nothing played, nothing counted) while the Effects toggle or volume is off
//   CLIPS     the two recordings load from Resources/Audio/Attack/EnemyShot, short, imported mono + decompress-on-load
//   SCOPE     beams are not projectiles: BossProjectilePool.Beam makes no sound
public static class PewSoundTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PEW] PASS  " : "[PEW] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool sim = PewSound.Simulate;
        var oldClock = PewSound.Clock;
        double t = 100.0;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PewSound.Simulate = true;
            PewSound.Clock = () => t;
            SoundSettings.SetSfxOn(true);
            SoundSettings.SetSfxVolume(1f);
            PewSound.Reset();

            var def = EliteCatalog.All[0];
            var pool = EliteSystem.Shots;

            Vector3 up = new Vector3(0, 3, 0);
            var bp = new BossProjectilePool(64, 2);
            var boss = BossCatalog.ForWorld(0);

            // ONCE
            t += 1; PewSound.Reset();
            var s = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(0, 3), Vector2.down);
            Check("a roster/elite shot plays exactly one laser (" + PewSound.Played + ", " + PewSound.LastKind + ")", s != null && PewSound.Played == 1 && PewSound.LastKind == PewSound.Kind.Laser);
            t += 1; PewSound.Reset();
            pool.Fire(null, def, EliteShots.Kind.Shard, new Vector2(0, 3), Vector2.down, false);
            Check("a split/burst chip plays none (" + PewSound.Played + ")", PewSound.Played == 0);
            t += 1; PewSound.Reset();
            var shot = bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down);
            Check("a single boss shot plays exactly one laser (" + PewSound.Played + ", " + PewSound.LastKind + ")",
                  shot != null && PewSound.Played == 1 && PewSound.LastKind == PewSound.Kind.Laser);
            t += 1; PewSound.Reset();
            bp.Beam(boss, null, 0, up, -90f, 0f, .5f, 1f, .3f);
            Check("a boss beam is not a projectile: no pew (" + PewSound.Played + ")", PewSound.Played == 0);
            Check("the gains are quiet (" + PewSound.LaserVolume + ", " + PewSound.GunVolume + ")", PewSound.LaserVolume <= .35f && PewSound.GunVolume <= .35f);

            // BURST: one source firing rapidly switches to the chained gun; slow fire / other sources stay laser
            t += 1; PewSound.Reset();
            int guns = 0, lasers = 0;
            for (int i = 0; i < 20; i++)
            {
                t += .06;
                if (PewSound.Play(77)) { if (PewSound.LastKind == PewSound.Kind.Gun) guns++; else lasers++; }
            }
            Check("rapid fire from one source: laser, laser, then the gun chain (" + lasers + " laser, " + guns + " gun)", lasers == 2 && guns >= 4);
            Check("the gun chain retriggers at its steady rate, not per bullet (" + guns + " plays for 20 shots over " + (20 * .06) + " s)", guns <= 20 * .06 / PewSound.GunInterval + 1);
            t += 5; PewSound.Reset();
            int slowGuns = 0;
            for (int i = 0; i < 6; i++) { t += .6; if (PewSound.Play(78) && PewSound.LastKind == PewSound.Kind.Gun) slowGuns++; }
            Check("slow fire (0.6 s apart) is always the laser (" + slowGuns + " gun)", slowGuns == 0 && PewSound.Played == 6);
            t += 5; PewSound.Reset();
            int mixedGuns = 0;
            for (int i = 0; i < 6; i++) { t += .4; if (PewSound.Play(100 + i % 3) && PewSound.LastKind == PewSound.Kind.Gun) mixedGuns++; }
            Check("three shooters each firing slowly are not a burst (" + mixedGuns + " gun)", mixedGuns == 0);
            t += 5; PewSound.Reset();
            for (int i = 0; i < 6; i++) { t += .1; pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(0, 3), Vector2.down, true, 555); }
            Check("a roster shooter's rapid volleys reach the gun chain through the real pool (" + PewSound.LastKind + ")", PewSound.LastKind == PewSound.Kind.Gun);

            // RATE
            bp.RecycleAll();
            t += 5; PewSound.Reset();
            for (int i = 0; i < 40; i++) bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down);
            Check("40 boss shots in one frame make one sound (" + PewSound.Played + ", dropped " + PewSound.Dropped + ")", PewSound.Played == 1 && PewSound.Dropped == 39);
            t += 5; PewSound.Reset();
            bp.RecycleAll();   // (the pool is spent otherwise: a full pool skips shots, and their sound)
            double t0 = t;
            while (t < t0 + PewSound.WindowSeconds * .9) { t += .02; bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down); }
            Check("a sustained bullet hell stays within the per-second cap (" + PewSound.Played + " <= " + PewSound.MaxPerWindow + ")", PewSound.Played <= PewSound.MaxPerWindow && PewSound.Played >= 8);
            t += 5; PewSound.Reset();
            bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down);
            Check("after the window passes sounds play again (" + PewSound.Played + ")", PewSound.Played == 1);

            // EFFECTS
            SoundSettings.SetSfxOn(false);
            t += 5; PewSound.Reset();
            pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(0, 3), Vector2.down);
            bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down);
            Check("Effects toggle off: silent (" + PewSound.Played + ", dropped " + PewSound.Dropped + ")", PewSound.Played == 0 && PewSound.Dropped == 0);
            SoundSettings.SetSfxOn(true); SoundSettings.SetSfxVolume(0f);
            t += 5; PewSound.Reset();
            bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down);
            Check("Effects volume 0: silent (" + PewSound.Played + ")", PewSound.Played == 0);
            SoundSettings.SetSfxVolume(1f);
            t += 5; PewSound.Reset();
            bp.Fire(boss, BossShotStyle.Bolt, up, Vector2.down);
            Check("Effects back on: plays (" + PewSound.Played + ")", PewSound.Played == 1);

            // CLIPS: the user's recordings ship in Resources, short, mono-importable
            var laserClip = Resources.Load<AudioClip>(PewSound.LaserPath);
            var gunClip = Resources.Load<AudioClip>(PewSound.GunPath);
            Check("the laser and gunshot clips load from Resources and are short (laser " + (laserClip != null ? laserClip.length.ToString("F2") : "missing") +
                  " s, gun " + (gunClip != null ? gunClip.length.ToString("F2") : "missing") + " s)",
                  laserClip != null && gunClip != null && laserClip.length > .05f && laserClip.length <= .5f && gunClip.length > .05f && gunClip.length <= .5f);
            if (laserClip != null)
            {
                var imp = UnityEditor.AssetImporter.GetAtPath("Assets/Resources/" + PewSound.LaserPath + ".wav") as UnityEditor.AudioImporter;
                Check("the clips import mono, decompress-on-load", imp != null && imp.forceToMono && imp.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad);
            }
        }
        finally
        {
            PewSound.Simulate = sim; PewSound.Clock = oldClock; PewSound.Reset();
            EliteSystem.Clear();
        }
        Debug.Log("[PEW] failures: " + fails);
        return fails;
    }
}
