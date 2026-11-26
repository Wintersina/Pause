using System.Reflection;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class DroneSquadTest
{
    static int fails;
    static void Check(string label, bool pass)
    {
        Debug.Log((pass ? "[SQUAD] PASS " : "[SQUAD] FAIL ") + label);
        if (!pass) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try { Opening(); PickupRoute(); Modes(); Bosses(); Armour(); Stress(); }
        finally { FloatieBullets.ReleaseAll(); EliteSystem.Clear(); ShotAtoms.BeginRun(3); }
        Debug.Log("[SQUAD] failures: " + fails); return fails;
    }
    static void Fresh()
    {
        ShotFeelTest.FreshScene(); ShotAtoms.BeginRun(3);
        WorldManager.ClearReplayWorld();
    }
    static void Opening()
    {
        Fresh();
        var rig = ShotFeelTest.MakeShip(1, 0, activate: false);
        Check("a fully upgraded ship begins with three visible dormant slots and no bullets", rig.battery.Count == 3 && DroneSquad.ActiveCount == 0 && !rig.battery.Fire(0) && !rig.battery.Fire(1) && !rig.battery.Fire(2));
        DroneSquad.OfferStarter(); DroneSquad.OfferStarter();
        var starters = Object.FindObjectsByType<DroneCorePickup>(FindObjectsSortMode.None);
        Check("the immediate starter is exactly one aimed directional coin", starters.Length == 1 && starters[0].mode == DroneMode.Aimed);
        var core = DroneSquad.CollectCore(DroneMode.Aimed); rig.battery.Refresh();
        Check("starter activates only drone 1", core.slot == 0 && core.activated && DroneSquad.ActiveCount == 1 && DroneSquad.Mode(0) == DroneMode.Aimed && !DroneSquad.IsActive(1));
        var twin1 = ShotAtoms.Collect(ShotAtom.Twin, .99f);
        var twin2 = ShotAtoms.Collect(ShotAtom.Twin, 0f);
        Check("first two TWIN coins activate dormant drones in order, independent of random rolls", twin1.drone == 1 && twin2.drone == 2 && twin1.activated && twin2.activated && DroneSquad.ActiveCount == 3 && DroneSquad.Mode(1) == DroneMode.Aimed && DroneSquad.Mode(2) == DroneMode.Aimed);
        var arc = ShotAtoms.Collect(ShotAtom.Arc);
        Check("ARC upgrades the predictable weakest eligible drone", arc.drone == 0 && !arc.activated && ShotAtoms.Level(0, ShotAtom.Arc) == 1);
        Check("the upgrade reports actual before/after behavior", ShotAtoms.BenefitChange(ShotAtom.Twin, 1, 2).Contains("2 SHOTS → 3 SHOTS"));
        Check("plateau upgrades describe the fan or damage that actually changes",
            ShotAtoms.BenefitChange(ShotAtom.Twin, 3, 4).Contains("FAN") &&
            ShotAtoms.BenefitChange(ShotAtom.Split, 1, 2).Contains("FAN") &&
            ShotAtoms.BenefitChange(ShotAtom.Arc, 1, 2).Contains("DAMAGE"));
        ShotFeelTest.Teardown(rig);
        ShotAtoms.BeginRun(3);
        Check("new run clears activation and restores three dormant slots", DroneSquad.Slots == 3 && DroneSquad.ActiveCount == 0 && !DroneSquad.StarterOffered);
    }
    static void PickupRoute()
    {
        Fresh();
        var catcher = ShotAtomsTest.SpawnShip();
        var battery = FloatieBattery.Attach(catcher.ship, 1, null);
        var core = DroneCorePickup.Build(DroneMode.Aimed, catcher.ship.transform.position);
        var sensor = core.GetComponent<Collider2D>();
        Physics2D.SyncTransforms();
        bool overlap = Physics2D.Distance(catcher.ship.GetComponent<Collider2D>(), sensor).isOverlapped;
        Check("starter core is a real trigger pickup with kinematic physics and hull overlap", sensor.isTrigger && core.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Kinematic && overlap);
        catcher.trigger(sensor);
        Check("the normal pickup collision activates a drone and discovers its core", core == null && DroneSquad.ActiveCount == 1 && Codex.IsDiscovered(CodexCatalogue.DroneCoreId(DroneMode.Aimed)));
        var twin = ShotAtomPickup.Build(ShotAtom.Twin, catcher.ship.transform.position);
        catcher.trigger(twin.GetComponent<Collider2D>());
        Check("the real TWIN pickup path activates the next drone and adds its two-shot upgrade", (twin == null || !twin.GetComponent<Collider2D>().enabled) && DroneSquad.ActiveCount == 2 && ShotAtoms.Level(1, ShotAtom.Twin) == 1);
        var recruit = DroneRewardPickup.Spawn(catcher.ship.transform.position);
        catcher.trigger(recruit.GetComponent<Collider2D>());
        Check("the real boss-reward pickup joins one active drone", recruit == null && DroneSquad.Slots == 4 && DroneSquad.IsActive(3) && battery.Count == 4);
        while (DroneSquad.Slots < DroneSquad.MaxSlots) DroneSquad.AddBossDrone();
        int awarded = score.dustPickups;
        var overflow = DroneRewardPickup.Spawn(catcher.ship.transform.position);
        catcher.trigger(overflow.GetComponent<Collider2D>());
        Check("a capped recruit pays dust once without replacing a drone", overflow == null && DroneSquad.Slots == 12 && score.dustPickups == awarded + 1);
        while (!DroneSquad.CollectCore(DroneMode.Aimed).maxed) { }
        Check("a fully upgraded matching core pays dust rather than changing the squad", DroneSquad.CoreRecipient(DroneMode.Aimed) == -1 && DroneSquad.CollectCore(DroneMode.Aimed).maxed);
        Object.DestroyImmediate(catcher.ship);
    }

    static void Modes()
    {
        Fresh();
        var rig = ShotFeelTest.MakeShip(1, 0, activate: false);
        DroneSquad.CollectCore(DroneMode.Straight); DroneSquad.CollectCore(DroneMode.Alternating); DroneSquad.CollectCore(DroneMode.Aimed);
        rig.battery.Refresh();
        rig.battery.Fire(0); float straight = rig.battery.LastAngle[0];
        rig.battery.Fire(1); float left = rig.battery.LastAngle[1];
        rig.battery.Fire(1); float right = rig.battery.LastAngle[1];
        Check("straight is forward and sweep alternates sides predictably", Mathf.Abs(straight) < .001f && left * right < 0f && Mathf.Abs(left) == 25f && Mathf.Abs(right) == 25f);
        var target = new GameObject("aimTarget"); target.tag = "Enimey"; target.transform.position = new Vector3(1f, -1f, 0f); ClearTarget.Ensure(target).SetRadius(.15f);
        rig.battery.Fire(2);
        FloatieBullet shot = null;
        foreach (var bullet in FloatieBullets.All) if (bullet.Active && bullet.Source == 2) shot = bullet;
        bool locked = rig.battery.LastSeekerLocked && shot != null;
        var heading = shot != null ? shot.Heading : Vector3.zero;
        target.transform.position += Vector3.left * 3f;
        if (shot != null) shot.Tick(.01f);
        Check("aimed chooses a direction once; moving targets never bend the bullet", locked && shot != null && (shot.Heading - heading).sqrMagnitude < 1e-8f);
        var match = DroneSquad.CollectCore(DroneMode.Aimed);
        Check("matching core upgrades the matching drone instead of randomly replacing another mode", match.slot == 2 && !match.activated && match.beforeMode == DroneMode.Aimed && match.level == 2);
        ShotFeelTest.Teardown(rig);
        Fresh(); DroneCorePickup.SpawnChoice(Vector3.up);
        var choices = Object.FindObjectsByType<DroneCorePickup>(FindObjectsSortMode.None);
        bool first = choices[0].TryClaim(), second = choices[1].TryClaim();
        Check("one mode choice can be claimed; sibling pickups cannot pay twice", choices.Length == 3 && first && !second);
        choices[0].Consume();
    }
    static void Bosses()
    {
        Fresh();
        DroneSquad.CollectCore(DroneMode.Aimed); DroneSquad.Activate(1); DroneSquad.Activate(2);
        ShotAtoms.SetLevel(ShotAtom.Twin, 4);
        int power = ShotAtoms.PowerLevel;
        ShotAtomDrops.OnBossDestroyed(Vector3.up);
        Check("boss reward is a new drone pickup, not modifier coins", Object.FindObjectsByType<DroneRewardPickup>(FindObjectsSortMode.None).Length == 1 && Object.FindObjectsByType<ShotAtomPickup>(FindObjectsSortMode.None).Length == 0);
        int first = DroneSquad.AddBossDrone();
        Check("new drone joins active with the preferred mode and useful inherited upgrades", first == 3 && DroneSquad.ActiveCount == 4 && DroneSquad.Mode(3) == DroneMode.Aimed && ShotAtoms.Level(3, ShotAtom.Twin) == 2 && ShotAtoms.PowerLevel >= power);
        while (DroneSquad.Slots < 12) DroneSquad.AddBossDrone();
        Check("boss recruitment caps at twelve without overwriting any drone", DroneSquad.Slots == 12 && DroneSquad.ActiveCount == 12 && DroneSquad.AddBossDrone() == -1 && ShotAtoms.Level(0, ShotAtom.Twin) == 4);
        ShotAtoms.BeginRun(3); Check("a new run clears boss recruits", DroneSquad.Slots == 3 && DroneSquad.ActiveCount == 0);
    }
    public sealed class Target : MonoBehaviour, IShipAttackTargetUncapped
    {
        public int hits;
        public void TakeShipAttack(int ship, float weight, Vector3 at) { hits++; }
    }
    static void Stress()
    {
        Fresh();
        var rig = ShotFeelTest.MakeShip(1, 4, activate: false);
        DroneSquad.CollectCore(DroneMode.Aimed); DroneSquad.Activate(1); DroneSquad.Activate(2);
        while (DroneSquad.Slots < 12) DroneSquad.AddBossDrone();
        for (int atom = 0; atom < ShotAtomTuning.Count; atom++) ShotAtoms.SetLevel((ShotAtom)atom, 10);
        rig.battery.Refresh(); GunBoltArt.Prewarm(); FloatieFx.Prebuild();
        var target = new GameObject("endurance target"); target.tag = "Enimey"; target.transform.position = Vector3.up * 2f;
        target.AddComponent<Target>(); ClearTarget.Ensure(target).SetRadius(.5f);
        for (int i = 0; i < 180; i++) rig.battery.Step(1f / 60f);
        int peak = FloatieBullets.Active;
        int pool = FloatieBullets.All.Count;
        bool active = true;
        for (int i = 0; i < 12; i++) active &= rig.battery.Shots[i] > 0;
        for (int i = 0; i < 600; i++) { rig.battery.Step(1f / 60f); peak = Mathf.Max(peak, FloatieBullets.Active); }
        Check("all twelve upgraded aimed drones fire while the bullet pool stays bounded", active && peak <= 128 && FloatieBullets.All.Count == pool && pool <= 128);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long allocated = meter ? TestHarness.AllocatedBytes(() =>
        {
            for (int frame = 0; frame < 600; frame++) rig.battery.Step(1f / 60f);
        }) : -1;
        Check("twelve max-upgrade drones sustain ten seconds of combat without managed allocations", meter && allocated == 0);
        Check("ultimate and drone projectile budgets have independent reserved room", AttackPool.MaxProjectiles == 40 && AttackPool.MaxCombinedProjectiles == 168);
        var boxes = new Bounds[11];
        Check("every additional drone reports a resting envelope for heart/hull avoidance", rig.battery.ExtraRestBounds(boxes) == 11);
        bool inView = true;
        var view = ShipTargets.View();
        foreach (float x in new[] { -2f, 0f, 2f })
        foreach (float y in new[] { view.yMin + .7f, -2f, view.yMax - .7f })
        {
            rig.ship.transform.position = new Vector3(x, y, 0f);
            for (int i = 0; i < 11; i++)
            {
                var extra = rig.battery.Extra(i); extra.Place();
                var bounds = extra.Renderer.bounds;
                inView &= bounds.min.x >= view.xMin - .01f && bounds.max.x <= view.xMax + .01f && bounds.min.y >= view.yMin - .01f && bounds.max.y <= view.yMax + .01f;
            }
        }
        Check("all eleven extra drones stay visible when the ship reaches screen corners", inView);
        var baseSprite = GunBoltArt.For(0);
        bool tiers = GunBoltArt.For(1) != baseSprite && GunBoltArt.For(2) != GunBoltArt.For(1) && GunBoltArt.For(3) != GunBoltArt.For(2);
        Check("gun milestones have distinct cached projectile silhouettes", tiers && GunBoltArt.For(3) == GunBoltArt.For(3));
        ShotFeelTest.Teardown(rig);
    }

    static void Armour()
    {
        Fresh();
        var manager = new GameObject("world").AddComponent<WorldManager>(); manager.SendMessage("Awake");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(WorldManager).GetField("levelBegun", flags).SetValue(manager, true);
        EnemyDef tier3 = null, tier4 = null;
        foreach (var def in EnemyRoster.All)
        { if (def.role == EnemyRole.Fighter && def.tier == 3 && tier3 == null) tier3 = def; if (def.role == EnemyRole.Fighter && def.tier == 4 && tier4 == null) tier4 = def; }
        WorldManager.CurrentIndex = 3;
        typeof(WorldManager).GetField("distanceLeft", flags).SetValue(manager, manager.WorldDistance * .9f);
        Check("early enemies keep two hearts even when the player is strong", Toughness.HeartsFor(tier3) == 2 && Toughness.HeartsFor(tier4) == 2);
        typeof(WorldManager).GetField("distanceLeft", flags).SetValue(manager, manager.WorldDistance * .4f);
        Check("later authored tiers introduce three-heart veterans and four-heart armour", Toughness.HeartsFor(tier3) == 3 && Toughness.HeartsFor(tier4) == 4);
        var enemy = EnemyFactory.Create(tier4, Vector3.zero, Quaternion.identity, 1f, true);
        Check("extra health has a visible armoured-hull marker", enemy.GetComponent<EnemyHearts>().Max == 4 && enemy.transform.Find("ArmouredPlates") != null);
    }
}
