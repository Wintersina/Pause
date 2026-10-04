using System;
using System.Collections.Generic;
using UnityEngine;

// One elite ship, as data: Resources/Elites/Defs/<key>.json (JsonUtility).
//
// Adding an elite is adding one of these files plus its art strip -- see the
// add-elite-ship skill (.claude/skills/add-elite-ship/SKILL.md). Nothing in
// code names an elite: the director spawns whatever the current world's defs
// are, the codex lists them, the tests walk them all.
//
// Art: Resources/Elites/<World>/<key>.png, a strip of seven square cells
// (idle 0..3, tell, action, hit), copied from Codex's
// Art/Enemies/Elite/<World>/ by EliteArtSync. Optional strips that are
// used automatically when they exist (any number of square cells):
//   <key>_parked.png   sitting on the landing site, engines off
//   <key>_liftoff.png  rising off the site (played once over the lift-off)
//   <key>_death.png    blowing apart (played once, then the debris)
// Without them: parked = idle 0 hazed and dimmed, lift-off = the idle loop
// with procedural dust / heat shimmer / engine glow, death = debris.
//
// Points (muzzles, nozzles) are cell pixels, x right, y down, measured off
// the strip by .claude/skills/add-elite-ship/scripts/measure_elite_points.py.
[Serializable]
public class ElitePoint
{
    public string name;
    public float x, y;          // cell px (x right, y down)
    public float dir = -1f;     // art-space degrees the shot / plume leaves at (0 right, 90 up); < 0: away from the nose (nozzles) / along the nose (muzzles)
}

[Serializable]
public class EliteDef
{
    // ---- identity ----
    public string key;                  // "ember_elite_sunstoke": art key, object name, codex match
    public string displayName;          // "Sunstoke"
    public string world;                // EnemyRoster.WorldKeys entry: "ember"
    public string role;                 // one line for the codex: "spear interceptor"
    public string codexId;              // "elite_ember_sunstoke"
    public string lore;

    // ---- personality ----
    public string brain;                // EliteBrains: interceptor | gunship | striker | hauler | skirmisher | siege
    public string attack;               // EliteAttacks: lance_dash | broadside | claw_dive | slag_drop | blink_shards | siege_cannon

    // ---- body ----
    public float cellWorldSize = 1.4f;  // one strip cell, world units
    public float hullRadius = .42f;     // collider + crash radius (world)
    public float noseDeg = 90f;         // where the nose points in the art (0 right, 90 up)
    public bool turnsToFace = true;     // rotate the drawing to face its heading; false: stays upright and banks
    public float maxBank = 10f;         // upright ships: degrees of bank into a sideways move
    public bool armored;                // smashes rocks without losing a heart (mines, enemies, elites, rails still hurt)

    // ---- brain tuning (shared base) ----
    public float speed = 2.6f;          // cruise, world u/s
    public float accel = 6f;            // u/s^2
    public float turnRate = 240f;       // degrees/s the heading may swing
    public float followDistance = 1.8f; // how far from the pilot it keeps while following
    public float aggression = .5f;      // 0..1: shorter gaps between attacks, closer stalking
    public float tellSeconds = .55f;    // the wind-up before an action
    public float attackGap = 3.2f;      // base seconds between attacks (scaled down by aggression)
    public float avoidance = .8f;       // 0..1 obstacle-dodging skill
    public float lookAhead = .5f;       // seconds of closing it watches for obstacles
    public float perception = 2.4f;     // u/s its idea of where the pilot is catches up after a teleport
    public float laneOffset = 1.5f;     // gunship / hauler: lane distance from the pilot
    public float circleRadius = 1.9f;   // striker: circling radius
    public float keepDistance = 2.6f;   // skirmisher: preferred distance
    public float topMargin = 1.5f;      // siege: distance below the top of the view

    // ---- attack tuning ----
    public float actionSeconds = .5f;   // how long the action frame / move lasts
    public float dashSpeed = 7.5f;      // lance_dash / claw_dive: ram speed
    public int shotCount = 3;           // shots per action
    public float shotSpeed = 6f;        // u/s
    public float shotSpread = 14f;      // degrees between shots of a fan
    public float shotSize = .22f;       // world diameter of the drawn shot
    public float shotInterval = .1f;    // seconds between shots of a burst
    public float blinkDistance = 1.4f;  // skirmisher blink
    public string shotKind = "bolt";    // EliteShots.Kind: bolt | slag | shell | shard

    // ---- colours (hex) ----
    public string heartColor = "#C85AFF";   // hearts: magenta / violet / cyan, never the player's red
    public string shotColor = "#FF4FD8";
    public string shotCore = "#9FF6FF";
    public string engineColor = "#FF9A3C";

    // ---- hearts ----
    public int hearts = 2;
    public float heartSize = .15f;
    public float heartOrbit = .72f;     // orbit radius, x the hull's half-size
    public string heartStyle = "Halo";  // HeartStyle name

    // ---- exhaust ----
    public int exhaustShip = 3;         // ShipExhaustStyle row whose plume drawings it borrows (3: Solar Fang tongues)
    public float exhaustScale = .55f;   // plume length vs the cell, 0 = none

    // ---- rewards (0: ScoreRules defaults) ----
    public int score;
    public float dust;

    // ---- measured points ----
    public ElitePoint[] muzzles = new ElitePoint[0];   // action frame
    public ElitePoint[] nozzles = new ElitePoint[0];   // idle 0
    public int cellPixels = 192;

    // ---- derived ----
    [NonSerialized] public Color HeartColor, ShotColor, ShotCore, EngineColor;
    [NonSerialized] public int WorldIndex = -1;

    public int Score => score > 0 ? score : ScoreRules.EliteDown;
    public float Dust => dust > 0f ? dust : ScoreRules.EliteDownDust;
    public float PixelWorld => cellWorldSize / Mathf.Max(1, cellPixels);

    // A cell pixel -> offset from the ship's centre in the art's own frame
    // (before the drawing's rotation), world units.
    public Vector2 PixelToLocal(float px, float py)
    {
        float half = cellPixels * .5f;
        return new Vector2((px - half) * PixelWorld, (half - py) * PixelWorld);
    }

    public void Resolve()
    {
        HeartColor = Hex(heartColor, new Color(.78f, .35f, 1f));
        ShotColor = Hex(shotColor, new Color(1f, .31f, .85f));
        ShotCore = Hex(shotCore, new Color(.62f, .96f, 1f));
        EngineColor = Hex(engineColor, new Color(1f, .6f, .24f));
        WorldIndex = Array.IndexOf(EnemyRoster.WorldKeys, world);
        if (muzzles == null) muzzles = new ElitePoint[0];
        if (nozzles == null) nozzles = new ElitePoint[0];
        hearts = Mathf.Max(1, hearts);
    }

    static Color Hex(string s, Color fallback)
    {
        Color c;
        return !string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString(s, out c) ? c : fallback;
    }
}

// Every elite def in Resources/Elites/Defs, loaded once, sorted by key.
public static class EliteCatalog
{
    public const string DefsFolder = "Elites/Defs";
    public const string ArtFolder = "Elites";

    static EliteDef[] all;
    static Dictionary<string, EliteDef> byKey;

    public static EliteDef[] All
    {
        get
        {
            if (all == null) Load();
            return all;
        }
    }

    public static void Reload() { all = null; byKey = null; }

    static void Load()
    {
        var list = new List<EliteDef>();
        byKey = new Dictionary<string, EliteDef>();
        foreach (var text in Resources.LoadAll<TextAsset>(DefsFolder))
        {
            EliteDef def = null;
            try { def = JsonUtility.FromJson<EliteDef>(text.text); }
            catch (Exception e) { Debug.LogError("[Elite] bad def " + text.name + ": " + e.Message); }
            if (def == null || string.IsNullOrEmpty(def.key)) continue;
            def.Resolve();
            list.Add(def);
            byKey[def.key] = def;
        }
        list.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        all = list.ToArray();
    }

    public static EliteDef Find(string key)
    {
        if (all == null) Load();
        EliteDef d;
        return key != null && byKey.TryGetValue(key, out d) ? d : null;
    }

    public static EliteDef FindByCodexId(string id)
    {
        foreach (var d in All) if (d.codexId == id) return d;
        return null;
    }

    // The defs of one world (EnemyRoster.WorldKeys index), into `into`.
    public static int ForWorld(int world, List<EliteDef> into)
    {
        into.Clear();
        foreach (var d in All) if (d.WorldIndex == world) into.Add(d);
        return into.Count;
    }

    public static bool WorldHasElites(int world)
    {
        foreach (var d in All) if (d.WorldIndex == world) return true;
        return false;
    }
}
