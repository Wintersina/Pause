using UnityEngine;

// The BASE elite brains and attacks (interceptor / lance_dash, gunship / broadside, striker / claw_dive, hauler / slag_drop, skirmisher /
// blink_shards, siege / siege_cannon) stay in the game for Storm and Desert (and are the building blocks of the newer ones), but Ember's six
// have their own now (EmberElites.cs) -- and the Coalrunner was the last elite to fly the gunship. The older tests that exercise a base
// brain ask for it here: the first catalogue def that flies it, else a stand-in cloned from Ember's Coalrunner with the numbers the
// gunship had there (a test fixture only: it is not in the catalogue and never spawns in a run).
public static class BaseEliteDefs
{
    static EliteDef gunship;

    public static EliteDef For(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        if (brain != "gunship") return null;
        if (gunship != null) return gunship;
        var src = EliteCatalog.Find("ember_elite_coalrunner");
        if (src == null) return null;
        var d2 = JsonUtility.FromJson<EliteDef>(JsonUtility.ToJson(src));
        d2.brain = "gunship";
        d2.attack = "broadside";
        d2.hearts = 2;
        d2.hurtHide = false;
        d2.speed = 2.6f; d2.accel = 6f; d2.turnRate = 200f; d2.aggression = .5f;
        d2.tellSeconds = .5f; d2.attackGap = 3.2f; d2.actionSeconds = .6f;
        d2.shotCount = 4; d2.shotSpeed = 5.5f; d2.shotSpread = 0f; d2.shotSize = .2f; d2.shotInterval = .14f; d2.shotKind = "bolt";
        d2.laneOffset = 1.7f;
        d2.Resolve();
        gunship = d2;
        return d2;
    }

    static EliteDef sunstoke;

    // The Ember Sunstoke as it was before the mini-boss rework (a plain interceptor: 2 hearts, hull .36, avoidance .8): the shove / rail crash
    // tests need an elite that sits still enough for a shoved rock to reach it (a fixture only).
    public static EliteDef LegacyInterceptor()
    {
        if (sunstoke != null) return sunstoke;
        var src = For("interceptor");
        if (src == null) return null;
        var d2 = JsonUtility.FromJson<EliteDef>(JsonUtility.ToJson(src));
        d2.hearts = 2; d2.hurtHide = false; d2.armored = false;
        d2.speed = 3f; d2.accel = 7f; d2.turnRate = 260f; d2.followDistance = 2f; d2.aggression = .6f;
        d2.tellSeconds = .6f; d2.attackGap = 3f; d2.avoidance = .8f; d2.lookAhead = .5f; d2.hullRadius = .36f;
        d2.Resolve();
        sunstoke = d2;
        return d2;
    }

    public static void Reset() { gunship = null; sunstoke = null; }
}
