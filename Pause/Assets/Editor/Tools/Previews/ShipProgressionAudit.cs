using System;
using System.IO;
using System.Text;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Controlled one-cast benchmark: stationary boss, no drones/secret powers/coins.
public static class ShipProgressionAudit
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public sealed class BossProbe : MonoBehaviour, IBossAttackTarget
    {
        public float damage;
        public void TakeShipAttack(int ship, float weight, Vector3 at) { damage += weight; }
        public bool TryStrike(int ship, float weight, Vector3 at) { damage += weight; return true; }
    }
    static void OwnLevel(int id, int level)
    {
        DeveloperUnlocks.SetEnabled(false);
        for (int skin = 1; skin < ShipSkins.CountFor(id); skin++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, skin), skin <= level ? 1 : 0);
    }
    static float BossDamage(int id, int level, float targetY = 1.4f)
    {
        ShotFeelTest.FreshScene(); OwnLevel(id, level);
        var rig = ShotFeelTest.MakeShip(id, level, slots: 1, activate: false);
        var target = new GameObject("BossAudit", typeof(BossProbe)); target.tag = "Enimey";
        target.transform.position = new Vector3(0f, targetY, 0f);
        ClearTarget.Ensure(target).SetRadius(.7f);
        var probe = target.GetComponent<BossProbe>();
        typeof(ShipPowerController).GetMethod("Fire", Hidden).Invoke(rig.power, null);
        for (int frame = 0; frame < 20 * 60; frame++) rig.power.Runner.Step(1f / 60f);
        float damage = probe != null ? probe.damage : 0f;
        typeof(ShipPowerController).GetMethod("FinishCinematic", Hidden).Invoke(rig.power, null);
        ShotFeelTest.Teardown(rig); UnityEngine.Object.DestroyImmediate(target);
        return damage;
    }
    public static void Export()
    {
        using var sandbox = new TestHarness.Sandbox();
        var markdown = new StringBuilder("# Ultimate progression audit\n\nOne ultimate cast against a stationary, unlimited-health boss at near (0, -1) and far (0, 1.4), radius 0.7, with the ship at (0, -3), stepped for 20 seconds at 60 Hz. No drone fire, secret powers, coins or incoming projectiles. Damage is in boss hearts (one weight = one heart); the per-minute column uses the better of the two positions. This is a controlled benchmark, not guaranteed live DPS. Ordinary enemies touched by an ultimate die outright. Dove needs enemy projectiles/collision targets to express its control/reflect value.\n\n| ID | Dock position | Ship | Price | Ultimate | Boss damage near/far, base → full | Mean cooldown base → full | Boss damage / min base → full |\n|---|---|---|---:|---|---:|---:|---:|\n");
        var csv = new StringBuilder("id,dock_position,ship,price,ultimate,level,boss_hearts,boss_hearts_near,boss_hearts_far,shots,max_hits,duration,range,width,speed,mean_cooldown,boss_hearts_per_minute\n");
        foreach (int id in ShipId.All)
        {
            float[] damage = new float[5], close = new float[5], far = new float[5]; float[] cooldown = new float[5];
            for (int sample = 0; sample <= ShipWeaponUpgrades.MaxLevel; sample++)
            {
                int level = sample;
                far[sample] = BossDamage(id, level);
                close[sample] = BossDamage(id, level, -1f);
                damage[sample] = Mathf.Max(close[sample], far[sample]);
                cooldown[sample] = 45f * ShipWeaponUpgrades.CooldownScaleAt(id, level);
                var row = ShipWeaponUpgrades.LoadoutAt(id, level);
                csv.AppendLine(string.Join(",", id, SpaceDock.SlotOf(id) + 1, ShipId.NameOf(id), shopingShips.CostFor(id), row.attackName, level,
                    damage[sample].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), close[sample], far[sample], row.shots, row.maxHits, row.duration, row.range, row.width, row.speed,
                    cooldown[sample], (damage[sample] * 60f / cooldown[sample]).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            }
            var baseRow = ShipLoadoutTable.For(id);
            markdown.AppendLine($"| {id:00} | {SpaceDock.SlotOf(id) + 1} | {ShipId.NameOf(id)} | {(PremiumShips.IsPremium(id) ? "IAP" : shopingShips.CostFor(id).ToString("N0"))} | {baseRow.attackName} | {close[0]:0.##}/{far[0]:0.##} → {close[4]:0.##}/{far[4]:0.##} | {cooldown[0]:0.##} → {cooldown[4]:0.##} s | {damage[0] * 60f / cooldown[0]:0.##} → {damage[4] * 60f / cooldown[4]:0.##} |");
        }
        const string directory = "/private/tmp/pause-ship-progression-review";
        Directory.CreateDirectory(directory);
        File.WriteAllText(directory + "/ultimate-audit.md", markdown.ToString());
        File.WriteAllText(directory + "/ultimate-audit.csv", csv.ToString());
        Debug.Log("[ULT-AUDIT] exported " + directory);
        EditorApplication.Exit(0);
    }
}
