// The AttackBudgetTest, sharded: one registered suite per row group so a shard balancer can spread them
// (AttackBudgetTest.ExecuteGroups; lanes: PAUSE_TEST_FULL, see AttackBudgetTest.Full).
//   AttackBudgetCoreTest     harness sanity, catalogue fully pinned, spot checks of pinned attacks
//   AttackBudget<World>Test  the themed rows of one world
//   AttackBudgetBoss<World>Test  the boss rows of one world (the slow ones)
public static class AttackBudgetCoreTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("Core"); } }
public static class AttackBudgetFrostTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("Frost"); } }
public static class AttackBudgetEmberTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("Ember"); } }
public static class AttackBudgetVerdantTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("Verdant"); } }
public static class AttackBudgetSpaceTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("Space"); } }
public static class AttackBudgetTideTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("Tide"); } }
public static class AttackBudgetBossFrostTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("BossFrost"); } }
public static class AttackBudgetBossEmberTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("BossEmber"); } }
public static class AttackBudgetBossSpaceTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("BossSpace"); } }
public static class AttackBudgetBossTideTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("BossTide"); } }
public static class AttackBudgetBossStormTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("BossStorm"); } }
public static class AttackBudgetBossVerdantTest { public static void Run() { TestHarness.Exit(Execute()); } public static int Execute() { return AttackBudgetTest.ExecuteGroups("BossVerdant"); } }
