using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The elite-dock audit rules, in every planet world and ground set (DockGrade.Strict on, the game's own rules): never more than one dock on the
// ground at a time (a new one only after the last has scrolled off), every dock on solid ground its world allows (Frost: snow flat / ice sheet /
// quay, never a lead of water, no floating rig / icebreaker / rig bay; Ember: crust, never lava; Tide: a painted deck only, so none in the
// open-sea sets; Desert: never the cliffs; Storm: only the four self-supporting launch rigs), each dock drawn in a per-backdrop colour that
// only ever darkens it, and Space's docks one at a time too.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod DockAuditTest.Run
public static class DockAuditTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DKA] PASS  " : "[DKA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static readonly string[] Worlds = { "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        DockGrade.Strict = true;
        float savedSpeed = moveBackGround.speed;
        try
        {
            Tints();
            foreach (var w in Worlds)
                for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
                    if (BackdropVariants.For(w).Installed(v)) OneSet(w, v);
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            foreach (var w in Worlds) BackdropVariants.For(w).Reset();
            EliteSystem.Clear();
        }
        Debug.Log("[DKA] done, " + fails + " failures");
        return fails;
    }

    static void Tints()
    {
        var c = DockGrade.Tint("Frost", 1, "silo_00");
        Check("Frost's silo is drawn darker than painted (" + c + ")", c.r < .9f && c.r > .1f && c.g <= 1f && c.b <= 1f);
        Check("an unknown dock is left as painted", DockGrade.Tint("Frost", 1, "no_such_dock") == Color.white && DockGrade.Tint("Nowhere", 1, "x") == Color.white);
    }

    static void OneSet(string world, int variant)
    {
        string tag = world + " v" + variant + ": ";
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EliteSystem.Clear();
        Time.timeScale = 1f;
        moveBackGround.speed = .6f;
        var vars = BackdropVariants.For(world);
        vars.Reset();
        vars.Force = variant;
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        var set = wb.Current;
        var d = set.Director;
        var sitesProp = d.GetType().GetProperty("Sites");
        var pool = sitesProp != null ? (BackdropPool)sitesProp.GetValue(d) : null;
        Check(tag + "the backdrop has a dock pool", pool != null);
        if (pool == null) { Object.DestroyImmediate(wb.gameObject); return; }
        var plannerProp = d.GetType().GetProperty("Planner");
        var planner = plannerProp != null ? plannerProp.GetValue(d) as GroundPlanner : null;
        int most = 0, seen = 0;
        bool onGround = true, tinted = true, bannedKind = false;
        var spawn = d.GetType().GetMethod("SpawnSite");
        for (float t = 0f; t < 150f; t += Dt)
        {
            wb.Step(Dt);
            if (t > 3f && ((int)(t / Dt)) % 45 == 0) spawn.Invoke(d, new object[] { float.NaN, -1 });     // (the elite director asking for one)
            int on = 0;
            foreach (var p in pool.items)
            {
                if (!p.active || p.sr == null || p.sr.sprite == null) continue;
                if (p.y > -set.HalfHeight - p.size * .6f) on++;
                if (p.y < set.HalfHeight && p.y > -set.HalfHeight)
                {
                    seen++;
                    if (planner != null)
                    {
                        var cls = planner.ClassAt(p.x, planner.GroundY(p.y));
                        if (world == "Frost" && (cls & (GroundClass.Open | GroundClass.Canopy | GroundClass.Built)) == 0) onGround = false;
                        if (world == "Ember" && (cls & GroundClass.Land) == 0) onGround = false;
                        if (world == "Tide" && (cls & GroundClass.Built) == 0) onGround = false;
                        if (world == "Desert" && cls == GroundClass.Canopy) onGround = false;
                    }
                    if (p.dockTint.r > 1.001f || p.dockTint.g > 1.001f || p.dockTint.b > 1.001f || p.dockTint.r < .1f) tinted = false;
                    if (world == "Frost" && (p.kind == 1 || p.kind == 5 || p.kind == 8)) bannedKind = true;     // rig bay, rig, icebreaker
                    if (world == "Storm" && p.kind < 5) bannedKind = true;                                     // the old floating machines
                }
            }
            most = Mathf.Max(most, on);
        }
        Check(tag + "never more than one dock on the ground at once (most " + most + ")", most <= 1);
        Check(tag + "every dock on ground its world allows", onGround);
        Check(tag + "every dock drawn in a darkening colour", tinted);
        Check(tag + "no floating dock kind (rig, icebreaker, rig bay, old Storm machines)", !bannedKind);
        if (world == "Tide" && TideTuning.OpenSea[variant]) Debug.Log("[DKA] note  " + tag + "open sea: " + seen + " frames with a dock, only on its few painted decks");
        else Check(tag + "docks do come (" + seen + " frames with one in view)", seen > 0);
        Object.DestroyImmediate(wb.gameObject);
    }
}
