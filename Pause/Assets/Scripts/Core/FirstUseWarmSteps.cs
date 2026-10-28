using UnityEngine;

// The lists FirstUseWarm works through (see there for the budget): what the first pause, the first death panel,
// the fusion strips, the first liftoff and the first achievement toast read, slice or build, in the order they are
// likely wanted. Each call only queues (keyed, so a repeat is a no-op); nothing here loads anything itself.
public static class FirstUseWarmSteps
{
    // First pause / resume: the resume vignette and streak, the speed-meter strip the x2 cue and the resume row share,
    // and the paused label's twelve glow frames (variants a and b, six drawings each; ~3 KB apiece).
    public static void Pause()
    {
        Art(ResumeFx.VignetteSprite);
        Art(ResumeFx.StreakSprite);
        Art("Hud/hud_meter");
        foreach (var v in new[] { "a", "b" })
            for (int i = 0; i < 6; i++) Art(PausedOverlayAnim.FxRoot + "pausedGlow_" + v + "_" + i);
    }

    // First death panel: the panel's sprites, then the translucent copy of the button frame (a GPU readback).
    public static void DeathPanel()
    {
        foreach (var n in DeathPanelView.WarmSprites) Art(DeathPanelView.SpriteRoot + n);
        FirstUseWarm.Add("death:button", DeathPanelView.PrewarmButton);
    }

    // The death frame of `world`: its sprite, then the border measurements (top, bottom), each its own frame.
    public static void DeathFrame(int world)
    {
        world = Mathf.Max(0, world);
        FirstUseWarm.Add("death:frame:" + world, HudSkin.DeathPanelPath(world), typeof(Sprite), null);
        FirstUseWarm.Add("death:top:" + world, () => DeathPanelView.FrameTopInset(world));
        FirstUseWarm.Add("death:bottom:" + world, () => DeathPanelView.FrameBottomInset(world));
    }

    // The twelve fusion flipbooks, one strip a frame, then the three generated overlay sprites (the ring in chunks).
    public static void Fusion()
    {
        var all = AtomFusionTable.All;
        for (int i = 0; i < all.Length; i++)
        {
            int k = i;
            FirstUseWarm.Add("fusion:strip:" + k, FusionFx.Folder + all[i].key, typeof(Texture2D), () => FusionFx.PrewarmStrip(k));
        }
        for (int s = 0; s < FusionFx.OverlayWarmSteps; s++)
        {
            int k = s;
            FirstUseWarm.Add("fusion:overlay:" + k, () => FusionFx.PrewarmOverlay(k));
        }
    }

    // First liftoff and planetfall: the layer shader's variant, compiled off to the side.
    public const string LiftoffShaderKey = "shader:planetfall";
    static ShaderVariantCollection variants;
    public static void Liftoff()
    {
        FirstUseWarm.Add(LiftoffShaderKey, Planetfall.ShaderPath, typeof(Shader), () => WarmShader(Planetfall.ShaderPath));
    }

    public static bool ShaderWarmed { get; private set; }

    static void WarmShader(string path)
    {
        var shader = Resources.Load<Shader>(path);
        if (shader == null || !shader.isSupported) return;
        try
        {
            if (variants == null) variants = new ShaderVariantCollection { hideFlags = HideFlags.HideAndDontSave };
            if (variants.Add(new ShaderVariantCollection.ShaderVariant(shader, UnityEngine.Rendering.PassType.Normal, new string[0])))
            {
                variants.WarmUp();
                ShaderWarmed = true;
                return;
            }
        }
        catch (System.ArgumentException) { }
        // not a variant the collection takes: one off-screen-free pass setup with a throwaway material
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        m.SetPass(0);
        Object.Destroy(m);
        ShaderWarmed = true;
    }

    // The enemy kill's ship overlay, the hit-smoke atlas.
    public static void Cues()
    {
        FirstUseWarm.Add("cue:legacyexplosion", "Weapons/LegacyExplosion", typeof(Texture2D), () => LegacyExplosionFx.Frames());
        FirstUseWarm.Add("cue:damagefx", "ShipArt/DamageFx", typeof(Texture2D), () => ShipDamageFx.Frame(0, 0));
    }

    public static int AchievementArtWarmed = 6;

    // The first achievement toast: the art of the first few still-locked bronze achievements (the likeliest next unlocks).
    public static void Achievements()
    {
        int n = 0;
        foreach (var def in AchievementCatalog.All)
        {
            if (n >= AchievementArtWarmed) break;
            if (def == null || def.tier != AchievementTier.Bronze || AchievementStore.IsUnlocked(def)) continue;
            var d = def;
            FirstUseWarm.Add("ach:" + d.id, AchievementArt.Folder + d.id, typeof(Sprite), () => AchievementArt.For(d));
            n++;
        }
    }

    static void Art(string path)
    {
        FirstUseWarm.Add("sprite:" + path, path, typeof(UnityEngine.Sprite), null);
    }
}
