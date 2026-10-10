using System;
using UnityEngine;
using UnityEngine.UI;

// Regression: a ship open in the codex detail view turned into a plain white
// rectangle every so often and then back to the ship art. The colour "tell"
// holds sprites cut from the skin sheets, and ShipHullArt frees those sheets
// (and destroys the sprites) whenever nothing wears the skin -- so the tell
// handed the Image a destroyed sprite, which an Image draws as a white box.
// Plays every codex entry's detail animation for minutes of unscaled time,
// with skin sheets being released underneath it, and samples the Image every
// frame: it must always hold a live sprite on a live texture, enabled, and
// never a fully white-tinted frame.
public static class CodexWhiteSpriteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CWS] PASS  " : "[CWS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Codex.Reload();

        var root = new GameObject("CwsRoot", typeof(RectTransform));
        try
        {
            foreach (int ship in ShipId.All) CheckShip(root.transform, ship);
            CheckEveryEntry(root.transform);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        Debug.Log("[CWS] failures: " + fails);
        return fails;
    }

    // The sprite on screen is alive: not destroyed, with a live texture.
    static bool Alive(Image img)
    {
        return img != null && img.enabled && img.sprite != null && img.sprite.texture != null &&
               img.color.a > 0f;
    }

    static CodexAnimator Make(Transform parent, CodexAnimation anim, bool detail)
    {
        var box = new GameObject("Box", typeof(RectTransform));
        box.transform.SetParent(parent, false);
        ((RectTransform)box.transform).sizeDelta = new Vector2(400, 400);
        var art = CodexUi.NewImage("Art", (RectTransform)box.transform, null, Color.white);
        var a = CodexAnimator.On(art);
        a.Bind(anim, false, detail);
        return a;
    }

    // Plays `seconds` of 60 fps, calling `perFrame(t)` first; returns the
    // frame time of the first bad sample, or -1.
    static float Play(CodexAnimator a, float seconds, Action<float> perFrame)
    {
        const float dt = 1f / 60f;
        for (float t = 0f; t < seconds; t += dt)
        {
            if (perFrame != null) perFrame(t);
            a.Ticking = true;
            a.Advance(dt);
            if (!Alive(a.Image)) return t;
            if (a.Image.color.r > .999f && a.Image.color.g > .999f && a.Image.color.b > .999f &&
                a.Image.sprite.texture.width <= 2) return t;   // the 1x1 white fallback
        }
        return -1f;
    }

    static void CheckShip(Transform parent, int ship)
    {
        string key = ShipId.KeyOf(ship);
        var entry = Codex.Find(CodexCatalogue.ShipPrefix + key);
        if (entry == null) { Check("ship " + key + " has a codex entry", false); return; }

        // Plain run, a minute of detail view (several full colour rounds).
        var anim = CodexAnimations.For(entry);
        var a = Make(parent, anim, true);
        float bad = Play(a, 60f, null);
        Check("ship " + key + ": a minute of detail view never shows a blank/white sprite (bad at " + bad + ")", bad < 0f);
        UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject);

        // The reported bug: the skin sheets are freed while the codex is
        // open (dock / title screen / scene load), at every moment of the tell.
        for (int k = 0; k < 4; k++)
        {
            anim = CodexAnimations.For(entry);
            a = Make(parent, anim, true);
            int n = 0;
            bad = Play(a, 40f, t =>
            {
                if (++n % (97 + k * 53) == 0) ShipHullArt.ReleaseUnusedSkins();
            });
            Check("ship " + key + " (release pattern " + k + "): freeing skin sheets under the open detail never blanks the art (bad at " + bad + ")", bad < 0f);
            UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject);
        }

        // Re-opening after a release gets an animation that can show its colours.
        ShipHullArt.ReleaseUnusedSkins();
        anim = CodexAnimations.For(entry);
        bool live = anim != null && anim.HasArt && anim.HasTell && anim.EnsureTell(0);
        if (live) foreach (var s in anim.tells[0]) live &= s != null && s.texture != null;
        Check("ship " + key + ": the animation fetched after a release has live colour frames", live);
        ShipHullArt.ReleaseUnusedSkins();
    }

    static void CheckEveryEntry(Transform parent)
    {
        // Ships are covered above; this is elites, bosses, enemies, mines,
        // atoms, worlds and the portal, with and without a death in between.
        foreach (var e in Codex.Entries)
        {
            if (e.category == CodexCategory.Ships) continue;
            var anim = CodexAnimations.For(e);
            if (anim == null || !anim.HasArt) continue;
            var a = Make(parent, anim, true);
            float bad = Play(a, 45f, null);
            Check(e.id + ": detail view stays drawn for 45 s (bad at " + bad + ")", bad < 0f);
            UnityEngine.Object.DestroyImmediate(a.transform.parent.gameObject);
        }
    }
}
