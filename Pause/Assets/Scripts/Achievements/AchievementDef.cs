using System;

public enum AchievementGroup { Meta, World, Boss, Elite, Enemy, Codex, Collection, Dust, Skill, Pause }

// Badge frame metal (display only): easy to hardest.
public enum AchievementTier { Bronze, Silver, Gold, Platinum }

// The seven blocks of the Codex ACHIEVEMENTS tab (a section groups several AchievementGroups).
public enum AchievementSection { Journey, Bosses, Elites, Combat, Pause, Codex, Collection }

// One achievement: static facts only (the player's state lives in AchievementStore).
//
// Progress comes from a lifetime counter (`counter`, see AchievementStore) that
// several achievements may share (kills_100/1000/5000 all read "kills"); a
// counter achievement unlocks when the counter reaches its target. A one-shot
// has no counter and is unlocked directly (Achievements.Report).
public sealed class AchievementDef
{
    public readonly string id, title, description;
    public readonly AchievementGroup group;
    public readonly AchievementTier tier;
    public readonly int points;
    public readonly bool hidden;          // shown as ??? in the Codex (and Hidden in the stores) until earned
    public readonly bool requiresTide;    // dormant until WorldManager.TideEnabled
    public readonly string counter;       // lifetime counter name, or null for a one-shot
    readonly int fixedTarget;
    readonly Func<int> dynamicTarget;     // "all" achievements whose target follows the game's content
    // Steps of the achievement in the store (0 = a standard, non-incremental one).
    public readonly int storeSteps;
    // Where it is tracked (documentation + a test that every live achievement has one).
    public readonly string hook;

    // Cached store keys (no string building while playing).
    public readonly string unlockedKey, claimedKey, syncedKey;

    public AchievementDef(string id, string title, string description, int points, AchievementTier tier,
                          AchievementGroup group, string hook, string counter = null, int target = 0,
                          Func<int> dynamicTarget = null, int storeSteps = 0, bool hidden = false, bool requiresTide = false)
    {
        this.id = id;
        this.title = title;
        this.description = description;
        this.points = points;
        this.tier = tier;
        this.group = group;
        this.hook = hook;
        this.counter = counter;
        fixedTarget = target;
        this.dynamicTarget = dynamicTarget;
        this.storeSteps = storeSteps;
        this.hidden = hidden;
        this.requiresTide = requiresTide;
        unlockedKey = "ach_u_" + id;
        claimedKey = "ach_c_" + id;
        syncedKey = "ach_s_" + id;
    }

    public bool IsCounter { get { return counter != null; } }

    // The counter value that unlocks it (0 for a one-shot).
    public int Target
    {
        get
        {
            if (counter == null) return 0;
            return dynamicTarget != null ? Math.Max(1, dynamicTarget()) : fixedTarget;
        }
    }

    // A progress bar is only worth drawing for a real count.
    public bool ShowsProgress { get { return counter != null && Target > 1; } }

    public AchievementSection Section
    {
        get
        {
            switch (group)
            {
                case AchievementGroup.Meta: case AchievementGroup.World: return AchievementSection.Journey;
                case AchievementGroup.Boss: return AchievementSection.Bosses;
                case AchievementGroup.Elite: return AchievementSection.Elites;
                case AchievementGroup.Enemy: case AchievementGroup.Skill: return AchievementSection.Combat;
                case AchievementGroup.Pause: return AchievementSection.Pause;
                case AchievementGroup.Codex: return AchievementSection.Codex;
                default: return AchievementSection.Collection;
            }
        }
    }
}
