using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Fully populated flight-complete panels, one per world (sample data, real code path).
public static class FlightSampleCapture
{
    static readonly string[] Names = { "space", "frost", "verdant", "ember", "tide", "storm", "desert" };

    static List<string> Roster(int w)
    {
        return EnemyRoster.All.Where(d => d.world == w).Select(d => d.displayName).ToList();
    }

    public static DeathPanelView.Results Sample(int w)
    {
        // score, best, newBest, then breakdown counts/points
        var p = new RunScore.Breakdown();
        long best; bool nb; float start, bonus; int killI, hurtA, hurtB, hurtMore, hurtCountA, hurtCountB;
        switch (w)
        {
            case 0:
                p.killCount = 64; p.kills = 6400; p.dustCount = 128; p.dust = 1280; p.atomCount = 6; p.atoms = 900; p.teleportCount = 2; p.teleports = 600;
                p.bossCount = 0; p.bosses = 0; p.worldCount = 0; p.worlds = 0; p.deathComboKills = 3; p.deathCombo = 450; p.loops = 0; p.bestMultiplier = 1.5f;
                p.distance = 18430 - (p.kills + p.dust + p.atoms + p.teleports + p.deathCombo); best = 18430; nb = true; start = 1240.5f; bonus = 36f;
                killI = 2; hurtA = 0; hurtB = 1; hurtMore = 0; hurtCountA = 2; hurtCountB = 1; break;
            case 1:
                p.killCount = 112; p.kills = 14200; p.dustCount = 241; p.dust = 2410; p.atomCount = 11; p.atoms = 2750; p.teleportCount = 4; p.teleports = 1800;
                p.bossCount = 1; p.bosses = 5000; p.worldCount = 1; p.worlds = 2500; p.deathComboKills = 5; p.deathCombo = 1200; p.loops = 0; p.bestMultiplier = 2f;
                p.distance = 48620 - (p.kills + p.dust + p.atoms + p.teleports + p.bosses + p.worlds + p.deathCombo); best = 61200; nb = false; start = 3870.25f; bonus = 97f;
                killI = 3; hurtA = 1; hurtB = 4; hurtMore = 2; hurtCountA = 3; hurtCountB = 1; break;
            case 2:
                p.killCount = 158; p.kills = 26800; p.dustCount = 330; p.dust = 3300; p.atomCount = 17; p.atoms = 5100; p.teleportCount = 6; p.teleports = 3600;
                p.bossCount = 2; p.bosses = 12000; p.worldCount = 2; p.worlds = 7000; p.deathComboKills = 7; p.deathCombo = 2400; p.loops = 0; p.bestMultiplier = 2.5f;
                p.distance = 96310 - (p.kills + p.dust + p.atoms + p.teleports + p.bosses + p.worlds + p.deathCombo); best = 96310; nb = true; start = 8120.75f; bonus = 192f;
                killI = 5; hurtA = 2; hurtB = 6; hurtMore = 1; hurtCountA = 2; hurtCountB = 2; break;
            case 3:
                p.killCount = 214; p.kills = 52400; p.dustCount = 421; p.dust = 4210; p.atomCount = 23; p.atoms = 9200; p.teleportCount = 8; p.teleports = 6400;
                p.bossCount = 3; p.bosses = 30000; p.worldCount = 3; p.worlds = 16000; p.deathComboKills = 9; p.deathCombo = 5800; p.loops = 0; p.bestMultiplier = 3f;
                p.distance = 212750 - (p.kills + p.dust + p.atoms + p.teleports + p.bosses + p.worlds + p.deathCombo); best = 305880; nb = false; start = 15420.5f; bonus = 425f;
                killI = 4; hurtA = 0; hurtB = 3; hurtMore = 3; hurtCountA = 4; hurtCountB = 2; break;
            case 4:
                p.killCount = 296; p.kills = 130500; p.dustCount = 612; p.dust = 6120; p.atomCount = 31; p.atoms = 18600; p.teleportCount = 11; p.teleports = 13200;
                p.bossCount = 4; p.bosses = 80000; p.worldCount = 4; p.worlds = 44000; p.deathComboKills = 12; p.deathCombo = 14500; p.loops = 0; p.bestMultiplier = 3.5f;
                p.distance = 523940 - (p.kills + p.dust + p.atoms + p.teleports + p.bosses + p.worlds + p.deathCombo); best = 523940; nb = true; start = 28760.0f; bonus = 1048f;
                killI = 6; hurtA = 1; hurtB = 5; hurtMore = 2; hurtCountA = 3; hurtCountB = 2; break;
            case 5:
                p.killCount = 402; p.kills = 410000; p.dustCount = 840; p.dust = 8400; p.atomCount = 44; p.atoms = 52800; p.teleportCount = 15; p.teleports = 39000;
                p.bossCount = 5; p.bosses = 240000; p.worldCount = 5; p.worlds = 120000; p.deathComboKills = 15; p.deathCombo = 41000; p.loops = 0; p.bestMultiplier = 4f;
                p.distance = 1284600 - (p.kills + p.dust + p.atoms + p.teleports + p.bosses + p.worlds + p.deathCombo); best = 1950200; nb = false; start = 52310.75f; bonus = 2569f;
                killI = 7; hurtA = 2; hurtB = 8; hurtMore = 4; hurtCountA = 5; hurtCountB = 3; break;
            default:
                p.killCount = 999; p.kills = 3400000; p.dustCount = 1840; p.dust = 18400; p.atomCount = 96; p.atoms = 420000; p.teleportCount = 38; p.teleports = 260000;
                p.bossCount = 12; p.bosses = 1900000; p.worldCount = 14; p.worlds = 1200000; p.deathComboKills = 21; p.deathCombo = 310000; p.loops = 2; p.bestMultiplier = 5f;
                p.distance = 9999999 - (p.kills + p.dust + p.atoms + p.teleports + p.bosses + p.worlds + p.deathCombo); best = 9999999; nb = true; start = 99987.65f; bonus = 19999f;
                killI = 1; hurtA = 3; hurtB = 6; hurtMore = 5; hurtCountA = 6; hurtCountB = 4; break;
        }
        var roster = Roster(w);
        string N(int i) { return roster[i % roster.Count]; }
        string hurt = N(hurtA) + (hurtCountA > 1 ? " x" + hurtCountA : "") + ", " + N(hurtB) + (hurtCountB > 1 ? " x" + hurtCountB : "") + (hurtMore > 0 ? " +" + hurtMore + " more" : "");
        long total = p.Total;
        float won = Mathf.Round(total / 25f) / 10f + bonus;
        Debug.Log("[FLIGHTDATA] " + Names[w] + " score=" + total + " best=" + best + " newBest=" + nb + " killedBy=" + N(killI) + " hurt=" + hurt + " dustWon=" + won + " dustStart=" + start + " roster=" + string.Join("|", roster));
        return new DeathPanelView.Results
        {
            score = total, bestScore = best, newBest = nb, ranked = true, parts = p,
            dustAtStart = start, dustWon = won, dustBonus = bonus, killedBy = N(killI), hurtBy = hurt,
        };
    }

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/FlightSample";
        var devices = new[] { FitDevice.Find("and-480x854"), FitDevice.Find("and-1080x2340-notch") };
        Directory.CreateDirectory(dir);
        ScreenFitScreens.DeathSample = Sample;
        using (new TestHarness.Sandbox())
        {
            for (int w = 0; w < Names.Length; w++)
                foreach (var d in devices)
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.DeathOn(w), d, Path.Combine(dir, Names[w] + "_" + d.w + "x" + d.h + ".png"), 0);
                    Debug.Log("[FLIGHTSHOT] " + Names[w] + " " + d.id + " err=" + shot.error + " failures=" + shot.failures + " minTextPt=" + shot.minTextPt.ToString("F1") + " (" + shot.minTextName + ")");
                    foreach (var f in shot.findings) if (!f.waived) Debug.Log("[FLIGHTFIND] " + Names[w] + " " + d.id + " " + f.kind + " " + f.element + ": " + f.detail);
                }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
