using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Headless checks of the animated world backgrounds (WorldBackdrop).
public static class WorldBackdropTest
{
    static int failures;

    // Budgets / guards.
    public const long TextureBudgetBytes = 9L * 1024 * 1024;   // per world, GPU size of the imported format
    // Frost carries eight shared 1024 atlases (landmarks, sites, weather and
    // five ambient-loop sheets) besides one 4-tile variant set.
    public const long FrostTextureBudgetBytes = 12L * 1024 * 1024;
    // Space carries the 2048 anim_hires planet sheet (~5.3 MB with mips; the 1x
    // sheet was ~1.3 MB) beside the asteroid-drift sheets: 9573 KB, over the
    // generic 9 MB. The user asked for much sharper planets (2026-10-08), so
    // Space gets a deliberate 10 MB.
    public const long SpaceTextureBudgetBytes = 10L * 1024 * 1024;
    // Verdant carries ten shared 1024 atlases (landmarks, pipes, fires,
    // sites, weather and five ambient-loop sheets: the user asked for "more
    // pipes and smokes and wild fires", 2026-10-08) besides one 4-tile
    // variant set: two atlases more than Frost. Measured 5488 KB (ASTC 6x6
    // sizes); 7 MB leaves room for one more sheet.
    public const long VerdantTextureBudgetBytes = 7L * 1024 * 1024;
    // Ember carries the same ten shared 1024 atlases (landmarks, pipes, fires,
    // sites, weather and five ambient-loop sheets) as Verdant besides one
    // 4-tile variant set (~5.5 MB measured, ASTC 6x6 sizes).
    public const long EmberTextureBudgetBytes = 7L * 1024 * 1024;
    // Tide carries the same shared 1024 atlases as Ember (landmarks, pipes, fires, sites,
    // weather) plus run C's five loop sheets (smoke, flames, surf, leaks, lights) besides
    // one 4-tile variant set: ~6 MB measured now (ASTC 6x6 sizes), ~8.5 MB with run C.
    public const long TideTextureBudgetBytes = 10L * 1024 * 1024;
    const float SeamTolerance = 0.02f;          // mean |top row - bottom row|, premultiplied RGBA
    // The guide's sky ramps (docs/art-style.md 1.3) peak at ~#123248 / #143430,
    // so the opaque sky averages up to ~0.12 relative luminance.
    const float SkyMaxLuminance = 0.13f;        // opaque far layer
    const float TileMaxLuminance = 0.17f;       // alpha-weighted, screen-filling tile layers
    const float TileMaxChroma = 0.20f;          // alpha-weighted max(rgb) - min(rgb)
    // docs/art-style.md 4.1: backdrop forms at HSV value <= 35%; point lights
    // (windows, dashes, sparks) are tiny and excepted, so the 90th percentile
    // of value is checked. Saturation is gated as chroma (above): the guide's
    // own night ramps are >60% HSV saturation at <15% value, where HSV S is
    // not a meaningful "colourfulness".
    const float TileMaxValueP90 = 0.35f;
    const float AtlasMaxLuminance = 0.66f;      // set-piece art (lights included) before its dimming runtime tint

    // FROST IS BRIGHTER, DELIBERATELY (user, 2026-10-08: "the backgrounds
    // are too dark, can you make it be more bright?"). Its art is still
    // painted to the rules above (value <= .33); FrostTuning.Brightness
    // lifts it at draw time (BackdropGrade), and Frost is held to these
    // limits AS DRAWN (the checks grade the pixels the way the shader does).
    // The real gameplay guard stays the same for every world: enemy bodies
    // >= 2.5:1 and the brightest tone >= 7:1 against the lane, and the lane
    // darker than the enemy hull (CheckReadability). With Brightness 1.7 the
    // drawn tiles reach value p90 ~.49, luminance ~.20, chroma ~.23.
    const float FrostTileMaxValueP90 = 0.55f;
    const float FrostSkyMaxLuminance = 0.18f;
    const float FrostTileMaxLuminance = 0.24f;
    const float FrostTileMaxChroma = 0.26f;
    // The weather atlas is the cloud ceiling: drawn thick and bright on
    // purpose, to continue the planetfall's cloud deck (mean value ~.86).
    const float FrostCloudMaxLuminance = 0.80f;

    // VERDANT IS BRIGHTER TOO, AS PAINTED: its v3 jungle art (approved by
    // the user 2026-10-08: "new forest is great") is painted lit and lush,
    // value p90 ~.47 on every ground tile, and drawn as painted on the day
    // side (the night side v4 is drawn darker, VerdantTuning). It is held
    // to these limits AS DRAWN; the gameplay guard (enemy bodies 2.5:1,
    // brightest tone 7:1 against the rendered lane: CheckReadability and
    // VerdantBackdropTest) is the same as every world's.
    // Measured as drawn (2026-10-08): value p90 .40-.47, luminance .23-.32
    // (the "sky" tile is the deepest ground plane -- canopy far below, not
    // a night sky -- so it is as lit as the rest), chroma .13-.24.
    const float VerdantTileMaxValueP90 = 0.52f;
    const float VerdantSkyMaxLuminance = 0.34f;
    const float VerdantTileMaxLuminance = 0.34f;
    const float VerdantTileMaxChroma = 0.27f;
    const float VerdantCloudMaxLuminance = 0.80f;

    // EMBER IS DARK AND LAVA-LIT, AS PAINTED: its v3 forge world is painted
    // at value p90 ~.40-.47 (v4 ~.32-.40) with lava as the bright accent,
    // and drawn as painted (brightness 1: below Spec.BrightLift, so shots
    // keep the thin outline). Held to these limits AS DRAWN; the gameplay
    // guard (enemy bodies 2.5:1, brightest tone 7:1 against the rendered
    // lane: CheckReadability and EmberBackdropTest) is every world's.
    const float EmberTileMaxValueP90 = 0.45f;
    const float EmberSkyMaxLuminance = 0.16f;
    const float EmberTileMaxLuminance = 0.18f;
    const float EmberTileMaxChroma = 0.25f;
    const float EmberCloudMaxLuminance = 0.80f;

    // TIDE IS DARK, MINT-FOAMED AND LAMP-LIT, AS PAINTED: its v3 ocean world is painted at value
    // p90 ~.38-.47 (v4 ~.30-.40) with white-mint foam and lamps as the bright accent, and
    // drawn as painted (brightness 1: below Spec.BrightLift, so shots keep the thin outline;
    // a set that fails the lane guard is drawn darker by TideTuning.VariantBrightness).
    // Held to these limits AS DRAWN; the gameplay guard (enemy bodies 2.5:1, brightest tone
    // 7:1 against the rendered lane: CheckReadability and TideBackdropTest) is every world's.
    const float TideTileMaxValueP90 = 0.48f;
    const float TideSkyMaxLuminance = 0.17f;
    const float TideTileMaxLuminance = 0.20f;
    const float TideTileMaxChroma = 0.30f;
    const float TideCloudMaxLuminance = 0.80f;

    static bool Frost(BackdropCatalog.Spec spec) { return spec.world == "Frost"; }
    static bool Ember(BackdropCatalog.Spec spec) { return spec.world == "Ember"; }
    static bool Verdant(BackdropCatalog.Spec spec) { return spec.world == "Verdant"; }
    // the worlds measured as drawn, with their own v3 atlases
    static bool Tide(BackdropCatalog.Spec spec) { return spec.world == "Tide"; }
    static bool V3(BackdropCatalog.Spec spec) { return Frost(spec) || Verdant(spec) || Ember(spec) || Tide(spec); }
    static float ValueCap(BackdropCatalog.Spec spec) { return Frost(spec) ? FrostTileMaxValueP90 : Verdant(spec) ? VerdantTileMaxValueP90 : Ember(spec) ? EmberTileMaxValueP90 : Tide(spec) ? TideTileMaxValueP90 : TileMaxValueP90; }
    static float ChromaCap(BackdropCatalog.Spec spec) { return Frost(spec) ? FrostTileMaxChroma : Verdant(spec) ? VerdantTileMaxChroma : Ember(spec) ? EmberTileMaxChroma : Tide(spec) ? TideTileMaxChroma : TileMaxChroma; }
    static float SkyLumCap(BackdropCatalog.Spec spec) { return Frost(spec) ? FrostSkyMaxLuminance : Verdant(spec) ? VerdantSkyMaxLuminance : Ember(spec) ? EmberSkyMaxLuminance : Tide(spec) ? TideSkyMaxLuminance : SkyMaxLuminance; }
    static float TileLumCap(BackdropCatalog.Spec spec) { return Frost(spec) ? FrostTileMaxLuminance : Verdant(spec) ? VerdantTileMaxLuminance : Ember(spec) ? EmberTileMaxLuminance : Tide(spec) ? TideTileMaxLuminance : TileMaxLuminance; }

    // A tile layer's pixels as drawn (BackdropGrade; the art itself when the
    // layer is not lifted).
    static Color[] AsDrawn(BackdropCatalog.Spec spec, BackdropCatalog.Layer layer, Color[] px, int variant = 0)
    {
        float lift = BackdropGrade.Lift(spec, layer, variant);
        return BackdropGrade.Apply(px, lift, BackdropGrade.Saturation(spec, lift));
    }

    // A Frost atlas's pixels as drawn: the strongest lift any of its users
    // draws it with (the weather at the cloud ceiling's thickening).
    static Color[] FrostAtlasAsDrawn(BackdropCatalog.Spec spec, string atlas, Color[] px)
    {
        float lift = 1f, alphaLift = 1f, satScale = 1f;
        if (atlas == "landmarks" || atlas == "sites") lift = BackdropGrade.Lift(spec, spec.Find("landmarks"));
        else if (atlas == "aurora") lift = BackdropGrade.Lift(spec, spec.Find("aurora"));
        else if (FrostAmbientCatalog.Lifted(atlas)) lift = BackdropGrade.Lift(spec, FrostTuning.PlumeShare);
        else if (atlas == "weather")
        {
            lift = BackdropGrade.Lift(spec, spec.Find("ceiling"));
            alphaLift = FrostTuning.CeilingThicken;
            satScale = FrostTuning.CeilingSaturation;
        }
        return BackdropGrade.Apply(px, lift, BackdropGrade.Saturation(spec, lift) * satScale, alphaLift);
    }

    // A Verdant atlas as drawn: the ground pieces at the ground's lift, the
    // weather at the cloud ceiling's thickening.
    static Color[] VerdantAtlasAsDrawn(BackdropCatalog.Spec spec, string atlas, Color[] px)
    {
        float lift = 1f, alphaLift = 1f, sat = 1f;
        if (atlas == "landmarks" || atlas == "sites" || atlas == "pipes" || atlas == "fires") lift = BackdropGrade.Lift(spec, spec.Find("ground"), 1);
        else if (atlas == "smoke" || atlas == "firesmoke") lift = VerdantTuning.PlumeLift;
        else if (atlas == "weather") { alphaLift = VerdantTuning.CeilingThicken; lift = VerdantTuning.CeilingLift; sat = VerdantTuning.CeilingSaturation; }
        return BackdropGrade.Apply(px, lift, sat, alphaLift);
    }

    // An Ember atlas as drawn: pieces and loops as painted (brightness 1, no
    // lifted smoke), the weather at the cloud ceiling's thickening and warming.
    static Color[] EmberAtlasAsDrawn(BackdropCatalog.Spec spec, string atlas, Color[] px)
    {
        float lift = 1f, alphaLift = 1f, sat = 1f;
        if (atlas == "landmarks" || atlas == "sites" || atlas == "pipes" || atlas == "fires") lift = BackdropGrade.Lift(spec, spec.Find("ground"), 1);
        else if (atlas == "weather") { alphaLift = EmberTuning.CeilingThicken; lift = EmberTuning.CeilingLift; sat = EmberTuning.CeilingSaturation; }
        return BackdropGrade.Apply(px, lift, sat, alphaLift);
    }

    // A Tide atlas as drawn: pieces and loops as painted, the weather at the cloud
    // ceiling's thickening, lift and tint toward the planetfall's teal deck.
    static Color[] TideAtlasAsDrawn(BackdropCatalog.Spec spec, string atlas, Color[] px)
    {
        float lift = 1f, alphaLift = 1f, sat = 1f;
        if (atlas == "landmarks" || atlas == "sites" || atlas == "pipes" || atlas == "fires") lift = BackdropGrade.Lift(spec, spec.Find("ground"), 1);
        else if (atlas == "weather") { alphaLift = TideTuning.CeilingThicken; lift = TideTuning.CeilingLift; sat = TideTuning.CeilingSaturation; }
        return BackdropGrade.Apply(px, lift, sat, alphaLift);
    }

    static float TideDrawAlpha(string atlas)
    {
        float a = 0f;
        foreach (var l in TideAmbientCatalog.Table.loops) if (l.atlas == atlas) a = Mathf.Max(a, l.alpha * TideTuning.NightLightBoost);
        return a > 0f ? a : 1f;
    }

    static float EmberDrawAlpha(string atlas)
    {
        float a = 0f;
        foreach (var l in EmberAmbientCatalog.Table.loops) if (l.atlas == atlas) a = Mathf.Max(a, l.alpha * EmberTuning.NightLightBoost);
        return a > 0f ? a : 1f;
    }

    static float VerdantDrawAlpha(string atlas)
    {
        float a = 0f;
        foreach (var l in VerdantAmbientCatalog.Table.loops) if (l.atlas == atlas) a = Mathf.Max(a, l.alpha * VerdantTuning.NightLightBoost);
        return a > 0f ? a : 1f;
    }

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WB] PASS  " : "[WB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static readonly Dictionary<string, string[]> RequiredSprites = new Dictionary<string, string[]>
    {
        { "Space", new[] { "giant_00", "giant_11", "rocky_00", "rocky_03", "station_00", "station_03",
                           "ringstation_00", "ringstation_03", "mini_station_00", "mini_ringstation_00",
                           "mini_rocky_00", "comet_00", "comet_01", "galaxy0", "galaxy1", "wisp0", "wisp1",
                           "moon", "star", "dot", "streak" } },
        // Frost's, Verdant's and Ember's v3 backdrops have no fx / anim atlases: their own atlases.
    };

    // Frost's shared atlases and the drawings the director relies on.
    static readonly Dictionary<string, string[]> FrostAtlases = new Dictionary<string, string[]>
    {
        { "landmarks", new[] { "rig_00", "rig_01", "platform_00", "platform_01", "platform_02", "refinery_00", "refinery_01",
                               "relay_00", "relay_01", "causeway_00", "icebreaker_00", "icebreaker_01", "cliff_00",
                               "cliff_01", "derrick_00", "convoy_00" } },
        { "weather", new[] { "cloud_bank_00", "cloud_bank_03", "cloud_wisp_00", "mist_00", "blizzard_00", "blizzard_02",
                             "snow_00", "snow_01" } },
        { "sites", new[] { "hangar_closed", "hangar_open", "rigbay_closed", "rigbay_open", "padring_idle", "padring_active",
                           "crawlerbay_closed", "crawlerbay_open", "hatch_closed", "hatch_open", "lights_off", "lights_on" } },
        { "smoke", new[] { "smoke_a_00", "smoke_a_07", "smoke_b_00" } },
        { "steam", new[] { "steam_vent_00", "geyser_07" } },
        { "fire_lights", new[] { "flare_00", "searchlight_07" } },
        { "beacons", new[] { "beacon_magenta_03", "beacon_cyan_00", "strobe_white_00", "window_lights_03" } },
        { "aurora", new[] { "aurora_00", "aurora_15" } },
    };

    // Verdant's shared atlases and the drawings the director relies on.
    static readonly Dictionary<string, string[]> VerdantAtlases = new Dictionary<string, string[]>
    {
        { "landmarks", VerdantPieces("landmarks") },
        { "pipes", VerdantPieces("pipes") },
        { "fires", VerdantPieces("fires") },
        { "sites", new[] { "roothangar_closed", "roothangar_open", "riverbay_closed", "riverbay_open", "podpad_idle", "podpad_active",
                           "towerbay_closed", "towerbay_open", "hatch_closed", "hatch_open", "lights_off", "lights_on" } },
        { "weather", new[] { "cloud_bank_00", "cloud_bank_03", "cloud_wisp_00", "mist_00", "pollen_00", "smokepall_00", "spore_00" } },
        { "smoke", new[] { "smoke_a_00", "smoke_a_07", "smoke_b_07" } },
        { "wildfire", new[] { "flame_front_00", "flame_patch_07" } },
        { "firesmoke", new[] { "wildsmoke_a_00", "wildsmoke_b_07" } },
        { "leaks", new[] { "steam_vent_00", "leak_sap_03", "ember_rain_00", "spore_burst_03" } },
        { "lights", new[] { "beacon_lime_00", "beacon_magenta_03", "window_lights_00", "strobe_white_00", "fireflies_03" } },
    };

    // Ember's shared atlases and the drawings the director relies on.
    static readonly Dictionary<string, string[]> EmberAtlases = new Dictionary<string, string[]>
    {
        { "landmarks", EmberPieces("landmarks") },
        { "pipes", EmberPieces("pipes") },
        { "fires", EmberPieces("fires") },
        { "sites", new[] { "foundryhangar_closed", "foundryhangar_open", "magmabay_closed", "magmabay_open", "slagpad_idle", "slagpad_active",
                           "furnacebay_closed", "furnacebay_open", "hatch_closed", "hatch_open", "lights_off", "lights_on" } },
        { "weather", new[] { "cloud_bank_00", "cloud_bank_03", "cloud_wisp_00", "mist_00", "ashgust_00", "ashgust_02", "smokepall_00", "smokepall_01", "embers_00" } },
        { "smoke", new[] { "smoke_a_00", "smoke_a_07", "smoke_b_07" } },
        { "lavafire", new[] { "flare_00", "flare_07", "fountain_00", "fountain_07" } },
        { "eruption", new[] { "eruptsmoke_a_00", "eruptsmoke_a_07", "eruptsmoke_b_00", "eruptsmoke_b_07" } },
        { "leaks", new[] { "steam_vent_00", "pipe_drip_03", "ember_rain_00", "lava_bubble_03" } },
        { "lights", new[] { "beacon_amber_00", "beacon_amber_03", "beacon_magenta_03", "strobe_white_00" } },
    };

    // Tide's shared atlases and the drawings the director relies on (run C's loop
    // sheets are optional until they land: TideAmbientCatalog.Missing lists them).
    static readonly Dictionary<string, string[]> TideAtlases = new Dictionary<string, string[]>
    {
        { "landmarks", TidePieces("landmarks") },
        { "pipes", TidePieces("pipes") },
        { "fires", TidePieces("fires") },
        { "sites", new[] { "trench_hatch_closed", "trench_hatch_open", "rig_bay_closed", "rig_bay_open", "reef_dock_closed", "reef_dock_open",
                           "vent_stack_closed", "vent_stack_open", "wreck_bay_closed", "wreck_bay_open", "lights_off", "lights_on" } },
        { "weather", new[] { "cloud_bank_00", "cloud_bank_03", "cloud_wisp_00", "cloud_wisp_03", "mist_00", "mist_02", "gust_00", "gust_02", "pall_00", "rain_00" } },
    };

    // Every drawing of a Tide piece atlas the director names.
    static string[] TidePieces(string atlas)
    {
        var names = new List<string>();
        foreach (var group in new[] { TideTuning.Rigs, TideTuning.Neighbours, TideTuning.Fronts, TideTuning.Satellites,
                                      TideTuning.Vessels, TideTuning.Banks, TideTuning.Lone, TideTuning.HorizontalPipes })
            foreach (string n in group)
            {
                bool pipe = n.StartsWith("pipe") || n.StartsWith("manifold") || n == "pumphouse_00";
                bool fire = n.StartsWith("whirlpool") || n.StartsWith("oil_slick") || n.StartsWith("reef_head") || n.StartsWith("wreck_hull") ||
                            n.StartsWith("bubbling_vent") || n.StartsWith("flare_stack") || n.StartsWith("plankton") || n.StartsWith("salvage_crawler");
                string a = pipe ? "pipes" : fire ? "fires" : "landmarks";
                if (a == atlas && !names.Contains(n)) names.Add(n);
            }
        return names.ToArray();
    }

    // Every drawing of an Ember piece atlas the director names.
    static string[] EmberPieces(string atlas)
    {
        var names = new List<string>();
        foreach (var group in new[] { EmberTuning.Forges, EmberTuning.Neighbours, EmberTuning.Fronts, EmberTuning.Satellites,
                                      EmberTuning.Barges, EmberTuning.Banks, EmberTuning.Pools, EmberTuning.Lone, EmberTuning.HorizontalPipes })
            foreach (string n in group)
            {
                bool pipe = n.StartsWith("pipe") || n.StartsWith("manifold") || n == "pumphouse_00";
                bool fire = n.StartsWith("lava") && !n.StartsWith("lavafall") || n.StartsWith("eruption") || n.StartsWith("coal") ||
                            n.StartsWith("scorched") || n.StartsWith("firebreak") || n.StartsWith("flarestack");
                string a = pipe ? "pipes" : fire ? "fires" : "landmarks";
                if (a == atlas && !names.Contains(n)) names.Add(n);
            }
        return names.ToArray();
    }

    // Every drawing of a Verdant piece atlas the director names.
    static string[] VerdantPieces(string atlas)
    {
        var names = new List<string>();
        foreach (var group in new[] { VerdantTuning.Refineries, VerdantTuning.Neighbours, VerdantTuning.Fronts, VerdantTuning.Satellites,
                                      VerdantTuning.Barges, VerdantTuning.Banks, VerdantTuning.Lone, VerdantTuning.HorizontalPipes })
            foreach (string n in group)
            {
                bool pipe = n.StartsWith("pipe") || n.StartsWith("manifold") || n == "pumphouse_00";
                bool fire = n.StartsWith("burn") || n.StartsWith("coal") || n.StartsWith("scorched") || n.StartsWith("firebreak");
                string a = pipe ? "pipes" : fire ? "fires" : "landmarks";
                if (a == atlas && !names.Contains(n)) names.Add(n);
            }
        return names.ToArray();
    }

    static string ArtDir(string world) { return "Assets/Art/Backgrounds/Resources/" + BackdropCatalog.Folder(world); }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            // A tile can gain its importer rule after the image was first
            // dropped in; force one normal import so that rule takes effect.
            // A tile can gain its importer rule after the image was first
            // dropped in (Frost's / Verdant's / Ember's v3 sheets): reimport those.
            foreach (string world in new[] { "Frost", "Verdant", "Ember", "Tide" })
            foreach (string png in Directory.GetFiles(ArtDir(world), "*.png", SearchOption.AllDirectories))
            {
                var imp = AssetImporter.GetAtPath(png.Replace('\\', '/')) as TextureImporter;
                if (imp != null && imp.mipmapEnabled) AssetDatabase.ImportAsset(png.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            }
            CheckCatalog();
            CheckArt();
            CheckSpaceAtlas();
            CheckSpaceTiers();
            CheckVerdantPalette();
            CheckEmberPalette();
            CheckTidePalette();
            CheckReadability();
            CheckWalls();
            CheckSpaceRailMaterials();
            CheckRailBrightness();
            CheckRuntime();
            CheckSpaceDiscs();
            CheckSpaceMotion();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
        }
        Debug.Log("[WB] failures: " + failures);
        return failures;
    }

    // Every world resolves to its own complete set; rates rise far -> near.
    static void CheckCatalog()
    {
        for (int w = 0; w < WorldManager.Worlds.Length; w++)   // Tide has its own backdrop now, whatever its release switch says
        {
            var theme = WorldManager.Worlds[w];
            var spec = BackdropCatalog.For(theme.displayName);
            Check(theme.displayName + " has its own backdrop spec", spec.world == theme.displayName);
            // Space carries two runs of body tiers (planets, structures).
            // Space carries two runs of body tiers (planets, structures);
            // Frost its weather at several depths.
            int maxLayers = spec.world == "Space" || V3(spec) ? 14 : 10;
            Check(theme.displayName + " has 4-" + maxLayers + " depth layers (" + spec.layers.Length + ")",
                  spec.layers.Length >= 4 && spec.layers.Length <= maxLayers);

            // A layer PINNED to the ground tile before it (Layer.pinTo:
            // Verdant's ground pieces on the mid tile) shares its rate.
            bool increasing = true;
            for (int i = 1; i < spec.layers.Length; i++)
                if (!(spec.layers[i].rate > spec.layers[i - 1].rate) &&
                    !(spec.layers[i].pinTo == spec.layers[i - 1].name && spec.layers[i].rate == spec.layers[i - 1].rate))
                    increasing = false;
            Check(theme.displayName + " parallax rates strictly increase far -> near (pinned layers share their host's)", increasing);
            Check(theme.displayName + " far layer is an opaque sky tile",
                  spec.layers[0].kind == BackdropCatalog.Kind.Tile && spec.layers[0].name == "sky");
            if (spec.world != "Space") CheckDepthModel(spec);

            string folder = BackdropCatalog.Folder(spec.world);
            int sets = Mathf.Max(1, spec.variantSets);
            for (int v = 1; v <= sets; v++)
            {
                string tiles = spec.variantSets > 0 ? BackdropCatalog.TileFolder(spec.world, v) : folder;
                foreach (var l in spec.layers)
                {
                    if (l.kind == BackdropCatalog.Kind.Pieces) continue;
                    string texture = spec.world == "Space" && l.name == "sky"
                        ? SpaceSkySelection.Texture : l.texture;
                    Check(spec.world + "/" + (spec.variantSets > 0 ? "v" + v + "/" : "") + texture + " tile sprite resolves",
                          Resources.Load<Sprite>(tiles + texture) != null);
                }
            }
            if (V3(spec))
            {
                foreach (var kv in Frost(spec) ? FrostAtlases : Ember(spec) ? EmberAtlases : Tide(spec) ? TideAtlases : VerdantAtlases)
                {
                    var atlas = new BackdropAtlas(Resources.Load<Texture2D>(folder + kv.Key), Resources.Load<TextAsset>(folder + kv.Key));
                    Check(spec.world + " atlas " + kv.Key + " resolves (" + atlas.Count + " sprites)", atlas.Count >= kv.Value.Length);
                    foreach (string n in kv.Value) Check(spec.world + " " + kv.Key + " sprite " + n + " present", atlas.Has(n));
                    atlas.Destroy();
                }
                continue;
            }
            if (spec.world == "Space")
            {
                for (int i = 1; i <= SpaceSkySelection.VariantCount; i++)
                {
                    string name = "sky_0" + i;
                    var sky = Resources.Load<Sprite>(folder + name);
                    Check("Space " + name + " is a crisp high-resolution sky",
                          sky != null && sky.texture.width >= 887 && sky.texture.height >= 1774 &&
                          sky.texture.filterMode == FilterMode.Point &&
                          sky.texture.wrapModeV == TextureWrapMode.Repeat);
                }
            }
            foreach (string atlas in new[] { BackdropCatalog.AtlasFx, BackdropCatalog.AtlasAnim })
            {
                Check(spec.world + "/" + atlas + " atlas texture resolves", Resources.Load<Texture2D>(folder + atlas) != null);
                Check(spec.world + "/" + atlas + " manifest resolves", Resources.Load<TextAsset>(folder + atlas) != null);
            }

            var fx = new BackdropAtlas(Resources.Load<Texture2D>(folder + "fx"), Resources.Load<TextAsset>(folder + "fx"));
            var anim = new BackdropAtlas(Resources.Load<Texture2D>(folder + "anim"), Resources.Load<TextAsset>(folder + "anim"));
            foreach (string s in RequiredSprites[spec.world])
                Check(spec.world + " sprite " + s + " present", fx.Has(s) || anim.Has(s));
            fx.Destroy();
            anim.Destroy();
        }
    }

    // The ship flies at atmosphere level: ground and landmarks are far below
    // on slow parallax, only atmospheric layers (air, clouds, particles) may
    // move fast, and at least one cloud/haze layer sits between ground and ship.
    static void CheckDepthModel(BackdropCatalog.Spec spec)
    {
        float maxGround = 0f;
        bool groundFar = true, nearAtmospheric = true;
        foreach (var l in spec.layers)
        {
            bool ground = l.role == BackdropCatalog.Role.Ground || l.role == BackdropCatalog.Role.Landmark;
            if (ground)
            {
                maxGround = Mathf.Max(maxGround, l.rate);
                if (l.rate > BackdropCatalog.MaxGroundRate) groundFar = false;
            }
            else if (l.rate > BackdropCatalog.MaxGroundRate &&
                     l.role != BackdropCatalog.Role.Atmosphere && l.role != BackdropCatalog.Role.Cloud)
                nearAtmospheric = false;
        }
        Check(spec.world + " ground and landmark layers use far parallax (<= " + BackdropCatalog.MaxGroundRate + ")",
              groundFar);
        Check(spec.world + " near layers are atmospheric only", nearAtmospheric);
        bool cloudBetween = false;
        foreach (var l in spec.layers)
            if (l.role == BackdropCatalog.Role.Cloud && l.rate > maxGround && l.rate < 1f) cloudBetween = true;
        Check(spec.world + " has a cloud/haze layer between the ground and the ship", cloudBetween);
        bool hasLandmark = false;
        foreach (var l in spec.layers) if (l.role == BackdropCatalog.Role.Landmark) hasLandmark = true;
        Check(spec.world + " has landmark set pieces", hasLandmark);
    }

    static float largestLandmark;

    static bool LandmarksSmall(WorldBackdrop wb)
    {
        var pd = wb.Current.Director as PlanetDirector;
        if (pd == null) return true;
        bool ok = true;
        foreach (var pool in pd.Landmarks)
            foreach (var p in pool.items)
            {
                if (!p.active) continue;
                Vector3 size = p.sr.bounds.size;
                float m = Mathf.Max(size.x, size.y);
                largestLandmark = Mathf.Max(largestLandmark, m);
                if (m > BackdropCatalog.MaxLandmarkSize * 1.15f) ok = false;   // height may exceed width a little
                if (p.rate > BackdropCatalog.MaxGroundRate) ok = false;
            }
        return ok;
    }

    // Pixels: seamless tiles, contrast guard, texture budget.
    static void CheckArt()
    {
        foreach (var spec in BackdropCatalog.All)
        {
            string dir = ArtDir(spec.world);
            long bytes = 0, astc = 0;
            long skyBytes = 0, skyAstc = 0;
            // Frost: one variant tile set per landing, so only one set counts.
            var variantBytes = new long[FrostBackdropSelection.MaxVariants + 1];
            var variantAstc = new long[FrostBackdropSelection.MaxVariants + 1];
            foreach (string path in Directory.GetFiles(dir, "*.png",
                         spec.variantSets > 0 ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                string asset = path.Replace('\\', '/');
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                if (tex == null) { Check(asset + " imports", false); continue; }
                // GPU size of the imported format (Profiler's editor number also
                // counts transient CPU copies, so it varies between sessions).
                long assetBytes = (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(
                    tex.width, tex.height, tex.graphicsFormat);
                long assetAstc = ((tex.width + 5) / 6) * ((tex.height + 5) / 6) * 16L;

                var imp = (TextureImporter)AssetImporter.GetAtPath(asset);
                // Space's planet sheet is drawn from ~0.4x to ~2.8x and keeps
                // a trilinear mip chain (WorldBackdropImport.IsPlanetSheet;
                // SpacePlanetSheetTest); a run loads anim_hires instead of
                // anim when it is installed, so only one counts.
                bool planetSheet = WorldBackdropImport.IsPlanetSheet(asset);
                if (planetSheet && Path.GetFileNameWithoutExtension(path) == BackdropCatalog.AtlasAnim &&
                    File.Exists(dir + BackdropCatalog.AtlasAnimHires + ".png"))
                    assetBytes = assetAstc = 0;
                else if (planetSheet)
                {
                    // Mip chain (+1/3); ASTC 4x4 on phones, not 6x6.
                    assetBytes = assetBytes * 4 / 3;
                    assetAstc = ((tex.width + 3) / 4) * ((tex.height + 3) / 4) * 16L * 4 / 3;
                }
                if (planetSheet)
                    Check(asset + " is mipmapped and trilinear (planet sheet)",
                          imp != null && imp.mipmapEnabled && imp.filterMode == FilterMode.Trilinear);
                else
                    Check(asset + " has no mipmaps", imp != null && !imp.mipmapEnabled);
                Check(asset + " is compressed", imp != null && imp.textureCompression != TextureImporterCompression.Uncompressed);

                var px = ReadPixels(path);
                string file = Path.GetFileNameWithoutExtension(path);
                bool spaceSky = spec.world == "Space" &&
                                (file == "sky" || file.StartsWith("sky_", System.StringComparison.Ordinal));
                // A run loads one of the stored Space skies, never all of them.
                if (spaceSky)
                {
                    skyBytes = System.Math.Max(skyBytes, assetBytes);
                    skyAstc = System.Math.Max(skyAstc, assetAstc);
                }
                else if (spec.variantSets > 0 && VariantOf(path) > 0)
                {
                    variantBytes[VariantOf(path)] += assetBytes;
                    variantAstc[VariantOf(path)] += assetAstc;
                }
                else { bytes += assetBytes; astc += assetAstc; }
                bool tile = WorldBackdropImport.IsTile(asset);
                // The layer this file is the art of: a tile layer may name its
                // own texture (Layer.WithTexture).
                // A tile-named file no layer uses any more (the old mid.png)
                // is not drawn and not held to the drawn layers' rules.
                string name = null;
                foreach (var l in spec.layers)
                    if (l.kind != BackdropCatalog.Kind.Pieces && l.texture == file) name = l.name;
                if (spaceSky) name = "sky";     // sky_01..04 are all the sky layer's art
                if (tile && name == null)
                {
                    Debug.Log("[WB] NOTE  " + asset + " is a tile no " + spec.world + " layer draws any more");
                    continue;
                }
                if (name == null) name = file;
                if (tile)
                {
                    Check(asset + " wraps vertically (Repeat)", tex.wrapModeV == TextureWrapMode.Repeat);
                    BackdropCatalog.Layer layer = default(BackdropCatalog.Layer);
                    bool foundLayer = false;
                    foreach (var candidate in spec.layers)
                        if (candidate.texture == name || candidate.name == name)
                        {
                            layer = candidate;
                            foundLayer = true;
                            break;
                        }
                    if (!foundLayer && spaceSky) { layer = spec.Find("sky"); foundLayer = true; }
                    if (!foundLayer) { Check(spec.world + "/" + name + " maps to a catalog layer", false); continue; }
                    float seam = layer.wrapBlend > 0f ? WrapBlendSeam(px, layer.wrapBlend) : SeamDifference(px);
                    Check(spec.world + "/" + VariantTag(path) + name + " is vertically seamless (top vs bottom row " +
                          seam.ToString("F4") + " <= " + SeamTolerance +
                          (layer.wrapBlend > 0f ? ", as rendered with a " + layer.wrapBlend + " wrap cross-fade; raw art " +
                                                  SeamDifference(px).ToString("F4") : "") + ")", seam <= SeamTolerance);
                }

                // Frost and Verdant are measured as drawn (BackdropGrade).
                if (V3(spec))
                {
                    if (tile) px = AsDrawn(spec, spec.Find(name), px, VariantOf(path));
                    else if (Frost(spec)) px = FrostAtlasAsDrawn(spec, file, px);
                    else if (Ember(spec)) px = EmberAtlasAsDrawn(spec, file, px);
                    else if (Tide(spec)) px = TideAtlasAsDrawn(spec, file, px);
                    else px = VerdantAtlasAsDrawn(spec, file, px);
                }
                string drawnTag = V3(spec) ? " as drawn" : "";
                float lum, chroma;
                Measure(px, out lum, out chroma);
                if (spaceSky || name == "sky" || name == "far" || name == "mid")
                {
                    float v90 = ValuePercentile(px, 0.9f);
                    Check(spec.world + "/" + VariantTag(path) + name + " forms at HSV value <= " + (ValueCap(spec) * 100f).ToString("F0") +
                          "%" + drawnTag + " (p90 " + v90.ToString("F3") + ")", v90 <= ValueCap(spec));
                }
                if (spaceSky || name == "sky")
                    Check(spec.world + " " + VariantTag(path) + "sky stays dark" + drawnTag + " (lum " + lum.ToString("F3") + ")",
                          lum <= SkyLumCap(spec));
                if (spaceSky || name == "sky" || name == "far" || name == "mid")
                    Check(spec.world + "/" + VariantTag(path) + name + " under gameplay contrast guard" + drawnTag + " (lum " + lum.ToString("F3") +
                          ", chroma " + chroma.ToString("F3") + ")",
                          lum <= TileLumCap(spec) && chroma <= ChromaCap(spec));
                if (!tile && Ember(spec))
                {
                    // drawn at their loops' alpha (x the night boost) / the
                    // weather at the ceiling's thickening
                    float maxA = 0f;
                    foreach (var c in px) maxA = Mathf.Max(maxA, c.a);
                    float drawn = lum * Mathf.Min(1f, maxA * EmberDrawAlpha(file));
                    float cap = file == "weather" ? EmberCloudMaxLuminance : AtlasMaxLuminance;
                    Check(spec.world + "/" + name + " atlas art under brightness ceiling as drawn (" + drawn.ToString("F3") + " <= " + cap + ")",
                          drawn <= cap);
                }
                else if (!tile && Tide(spec))
                {
                    float maxA = 0f;
                    foreach (var c in px) maxA = Mathf.Max(maxA, c.a);
                    float drawn = lum * Mathf.Min(1f, maxA * TideDrawAlpha(file));
                    float cap = file == "weather" ? TideCloudMaxLuminance : AtlasMaxLuminance;
                    Check(spec.world + "/" + name + " atlas art under brightness ceiling as drawn (" + drawn.ToString("F3") + " <= " + cap + ")",
                          drawn <= cap);
                }
                else if (!tile && Verdant(spec))
                {
                    // drawn at their loops' alpha (x the night boost) / the
                    // weather at the ceiling's thickening
                    float maxA = 0f;
                    foreach (var c in px) maxA = Mathf.Max(maxA, c.a);
                    float drawn = lum * Mathf.Min(1f, maxA * VerdantDrawAlpha(file));
                    float cap = file == "weather" ? VerdantCloudMaxLuminance : AtlasMaxLuminance;
                    Check(spec.world + "/" + name + " atlas art under brightness ceiling as drawn (" + drawn.ToString("F3") + " <= " + cap + ")",
                          drawn <= cap);
                }
                else if (!tile && spec.world == "Frost")
                {
                    // Frost's v3 sheets are drawn at their loops' draw alpha
                    // (FrostAmbientCatalog) and the weather carries its
                    // translucency in the art: held to the ceiling as drawn.
                    float maxA = 0f;
                    foreach (var c in px) maxA = Mathf.Max(maxA, c.a);
                    float drawn = lum * Mathf.Min(1f, maxA * FrostDrawAlpha(file));
                    float cap = file == "weather" ? FrostCloudMaxLuminance : AtlasMaxLuminance;
                    Check(spec.world + "/" + name + " atlas art under brightness ceiling as drawn (lum " + lum.ToString("F3") +
                          " x opacity " + Mathf.Min(1f, maxA * FrostDrawAlpha(file)).ToString("F2") + " = " + drawn.ToString("F3") +
                          " <= " + cap + ")", drawn <= cap);
                }
                else if (!tile)
                    Check(spec.world + "/" + name + " atlas art under brightness ceiling (lum " + lum.ToString("F3") + ")",
                          lum <= AtlasMaxLuminance);
            }
            bytes += skyBytes;
            astc += skyAstc;
            long maxSet = 0, maxSetAstc = 0;
            for (int v = 1; v < variantBytes.Length; v++) { maxSet = System.Math.Max(maxSet, variantBytes[v]); maxSetAstc = System.Math.Max(maxSetAstc, variantAstc[v]); }
            bytes += maxSet;
            astc += maxSetAstc;
            long budget = spec.world == "Frost" ? FrostTextureBudgetBytes : spec.world == "Space" ? SpaceTextureBudgetBytes :
                          spec.world == "Verdant" ? VerdantTextureBudgetBytes : spec.world == "Ember" ? EmberTextureBudgetBytes : spec.world == "Tide" ? TideTextureBudgetBytes : TextureBudgetBytes;
            Debug.Log("[WB] " + spec.world + " texture memory: " + (bytes / 1024) + " KB desktop, ~" +
                      (astc / 1024) + " KB ASTC 6x6");
            // The budget is the phone's (ASTC) size. `bytes` is only that while the
            // Library's active target is Android; a Library last used as Standalone
            // reports the desktop format (~2x), so measure the ASTC figure then.
            long measured = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? bytes : astc;
            Check(spec.world + " texture memory " + (measured / 1024) + " KB <= " + (budget / 1024) + " KB",
                  measured <= budget);
        }
    }

    // ---------------------------------------------------- composited look --

    // The ground layers stacked the way the game draws them at rest: the
    // opaque sky, then far and mid (alpha over), then the river strip centred.
    static Color[] Composite(string world, out int w, out int h, int variant = 1)
    {
        var spec = BackdropCatalog.For(world);
        string dir = "Assets/Art/Backgrounds/Resources/" + (spec.variantSets > 0
            ? BackdropCatalog.TileFolder(world, variant) : BackdropCatalog.Folder(world));
        string skyName = world == "Space" ? SpaceSkySelection.Texture : "sky";
        var outPx = (Color[])AsDrawn(spec, spec.Find("sky"), ReadPixels(dir + skyName + ".png"), variant).Clone();
        w = ReadW; h = ReadH;
        foreach (string layerName in new[] { "far", "mid", "flow" })
        {
            bool hasLayer = false;
            foreach (var candidate in spec.layers)
                if (candidate.name == layerName) { hasLayer = true; break; }
            if (!hasLayer) continue;
            var layer = spec.Find(layerName);
            string path = dir + layer.texture + ".png";
            if (!File.Exists(path)) continue;
            var px = AsDrawn(spec, layer, ReadPixels(path), variant);
            int lw = ReadW, lh = ReadH;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // Runtime scales each tile to the camera width, regardless
                    // of its source dimensions. Match that here so generated
                    // portrait art participates in the composite review.
                    int sx = Mathf.Clamp(x * lw / w, 0, lw - 1);
                    int sy = Mathf.Clamp(y * lh / h, 0, lh - 1);
                    Color s = px[sy * lw + sx];
                    float alpha = s.a * layer.tint.a;
                    if (alpha <= 0f) continue;
                    int i = y * w + x;
                    outPx[i] = Color.Lerp(outPx[i], new Color(s.r * layer.tint.r, s.g * layer.tint.g,
                                                                 s.b * layer.tint.b, 1f), alpha);
                }
        }
        return outPx;
    }

    // 1..4 for a file in a Frost variant folder (".../v3/mid.png"), else 0.
    static int VariantOf(string path)
    {
        string d = Path.GetFileName(Path.GetDirectoryName(path.Replace('\\', '/')));
        if (d != null && d.Length == 2 && d[0] == 'v' && char.IsDigit(d[1])) return d[1] - '0';
        return 0;
    }

    // The highest draw alpha a Frost atlas is drawn at: its loops' alpha
    // (the world aurora's per-variant alpha for aurora), 1 for the rest
    // (landmarks, sites, weather).
    static float FrostDrawAlpha(string atlas)
    {
        float a = 0f;
        if (atlas == "aurora") { foreach (float v in FrostAmbientCatalog.AuroraAlpha) a = Mathf.Max(a, v); return a; }
        foreach (var l in FrostAmbientCatalog.Loops) if (l.atlas == atlas) a = Mathf.Max(a, l.alpha);
        return a > 0f ? a : 1f;
    }

    static string VariantTag(string path) { int v = VariantOf(path); return v > 0 ? "v" + v + "/" : ""; }

    static float Chroma(Color c) { return Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)); }
    static float Value(Color c) { return Mathf.Max(c.r, Mathf.Max(c.g, c.b)); }

    // docs/art-style.md 4.2 (WCAG relative luminance of sRGB colours).
    static float Linear(float v) { return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
    static float RelLum(Color c) { return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b); }
    static float Contrast(float a, float b) { return (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f); }

    // Hue families used by the diversity check.
    const int FamGreen = 0, FamTeal = 1, FamIndigo = 2, FamWarm = 3, FamOther = 4, FamGrey = 5;

    static int Family(float hue, Color c)
    {
        if (Chroma(c) <= 0.03f) return FamGrey;
        if (hue >= 90f && hue < 175f) return FamGreen;
        if (hue >= 175f && hue < 200f) return FamTeal;
        if (hue >= 200f && hue < 265f) return FamIndigo;
        if (hue < 60f || hue >= 330f) return FamWarm;
        return FamOther;
    }

    static bool IsTeal(float hue, float s, float v) { return hue >= 165f && hue < 200f && s >= 0.5f && v >= 0.55f; }
    // Verdant's amber is distant, fog-muted refinery light rather than a
    // foreground rail lamp, so its valid brightness is intentionally lower.
    static bool IsSodium(float hue, float s, float v) { return hue >= 15f && hue < 45f && s >= 0.6f && v >= 0.55f; }

    // Verdant must not read as monochrome mud. The v3 jungle (user,
    // 2026-10-08: "new forest is great") is a lit green world -- the old
    // "80s anime night forest" rule (an indigo night sky over 20% of the
    // screen) went with the old art: the planet's night side is now its own
    // variant (v4). Every variant still needs several distinct hue/value
    // clusters and a real value range, and its accents -- the wildfires'
    // orange, the lime / magenta beacons -- come from the piece and loop
    // atlases drawn over it.
    public const int VerdantMinClusters = 6;            // 30-degree hue x 0.1 value bins with >= 0.5% coverage
    public const float VerdantMinValueRange = 0.15f;    // p95 - p5 of HSV value
    public const float VerdantMaxFamilyShare = 0.97f;   // the jungle is green, but never a single flat family
    public const int VerdantMinFirePixels = 2000;       // hot orange pixels in the wildfire atlases
    public const int VerdantMinBeaconPixels = 200;      // lime and magenta lamp pixels in the lights atlas

    static void CheckVerdantPalette()
    {
        var spec = BackdropCatalog.For("Verdant");
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            int w, h;
            var px = Composite("Verdant", out w, out h, v);
            var bins = new Dictionary<int, int>();
            var fam = new int[6];
            var values = new List<float>();
            int n = 0;
            for (int i = 0; i < px.Length; i += 2)
            {
                Color c = px[i];
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                float hue = hh * 360f;
                int band = Mathf.Min((int)(vv / 0.1f), 5);
                int key = Chroma(c) > 0.03f ? ((int)(hue / 30f) % 12) * 6 + band : 100 + band;
                int k;
                bins.TryGetValue(key, out k);
                bins[key] = k + 1;
                fam[Family(hue, c)]++;
                values.Add(vv);
                n++;
            }
            int clusters = 0;
            foreach (var kv in bins) if (kv.Value >= 0.005f * n) clusters++;
            values.Sort();
            float range = values[(int)(n * 0.95f)] - values[(int)(n * 0.05f)];
            float maxShare = 0f;
            for (int f = 0; f < FamGrey; f++) maxShare = Mathf.Max(maxShare, fam[f] / (float)n);
            Check("Verdant v" + v + " has >= " + VerdantMinClusters + " distinct hue/value clusters (" + clusters + ")", clusters >= VerdantMinClusters);
            Check("Verdant v" + v + " value range p5..p95 >= " + VerdantMinValueRange + " (" + range.ToString("F3") + ")", range >= VerdantMinValueRange);
            Check("Verdant v" + v + " is not one flat hue family (largest " + maxShare.ToString("F2") + " <= " + VerdantMaxFamilyShare + ")",
                  maxShare <= VerdantMaxFamilyShare);
        }
        int fire = 0, lime = 0, magenta = 0;
        foreach (string atlas in new[] { "fires", "wildfire" })
            foreach (var c in ReadPixels(ArtDir("Verdant") + atlas + ".png"))
            {
                if (c.a < 0.9f) continue;
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                if (IsSodium(hh * 360f, ss, vv)) fire++;
            }
        foreach (var c in ReadPixels(ArtDir("Verdant") + "lights.png"))
        {
            if (c.a < 0.5f) continue;
            float hh, ss, vv;
            Color.RGBToHSV(c, out hh, out ss, out vv);
            float hue = hh * 360f;
            if (ss > .5f && vv > .6f && hue >= 70f && hue < 110f) lime++;
            if (ss > .4f && vv > .6f && hue >= 290f && hue < 335f) magenta++;
        }
        Check("Verdant's wildfires carry hot orange (" + fire + " px >= " + VerdantMinFirePixels + ")", fire >= VerdantMinFirePixels);
        Check("Verdant's lights carry lime (" + lime + ") and magenta (" + magenta + ") beacons (>= " + VerdantMinBeaconPixels + " each)",
              lime >= VerdantMinBeaconPixels && magenta >= VerdantMinBeaconPixels);
    }

    // Ember must not read as one flat orange-brown mud. Every variant needs
    // several distinct hue/value clusters and a real value range (lava
    // against black basalt), and its accents -- molten lava and fire in the
    // fires / lavafire sheets, amber and magenta lanterns in the lights
    // sheet -- come from the piece and loop atlases drawn over it.
    public const int EmberMinClusters = 5;
    public const float EmberMinValueRange = 0.15f;
    // (no hue-family share rule: the forge world is amber and orange throughout by design; its variety is value and saturation)
    public const int EmberMinLavaPixels = 3000;        // hot orange pixels in the fires / lavafire atlases
    public const int EmberMinBeaconPixels = 200;       // amber and magenta lantern pixels in the lights atlas

    static void CheckEmberPalette()
    {
        var spec = BackdropCatalog.For("Ember");
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            int w, h;
            var px = Composite("Ember", out w, out h, v);
            var bins = new Dictionary<int, int>();
            var fam = new int[6];
            var values = new List<float>();
            int n = 0;
            for (int i = 0; i < px.Length; i += 2)
            {
                Color c = px[i];
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                float hue = hh * 360f;
                int band = Mathf.Min((int)(vv / 0.1f), 5);
                int key = Chroma(c) > 0.03f ? ((int)(hue / 30f) % 12) * 6 + band : 100 + band;
                int k;
                bins.TryGetValue(key, out k);
                bins[key] = k + 1;
                fam[Family(hue, c)]++;
                values.Add(vv);
                n++;
            }
            int clusters = 0;
            foreach (var kv in bins) if (kv.Value >= 0.005f * n) clusters++;
            values.Sort();
            float range = values[(int)(n * 0.95f)] - values[(int)(n * 0.05f)];
            Check("Ember v" + v + " has >= " + EmberMinClusters + " distinct hue/value clusters (" + clusters + ")", clusters >= EmberMinClusters);
            Check("Ember v" + v + " value range p5..p95 >= " + EmberMinValueRange + " (" + range.ToString("F3") + ")", range >= EmberMinValueRange);
        }
        int lava = 0, amber = 0, magenta = 0;
        foreach (string atlas in new[] { "fires", "lavafire" })
            foreach (var c in ReadPixels(ArtDir("Ember") + atlas + ".png"))
            {
                if (c.a < 0.9f) continue;
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                if (IsSodium(hh * 360f, ss, vv)) lava++;
            }
        foreach (var c in ReadPixels(ArtDir("Ember") + "lights.png"))
        {
            if (c.a < 0.5f) continue;
            float hh, ss, vv;
            Color.RGBToHSV(c, out hh, out ss, out vv);
            float hue = hh * 360f;
            if (ss > .5f && vv > .6f && hue >= 25f && hue < 50f) amber++;
            if (ss > .3f && vv > .6f && hue >= 290f && hue < 335f) magenta++;
        }
        Check("Ember's lava and fires carry hot orange (" + lava + " px >= " + EmberMinLavaPixels + ")", lava >= EmberMinLavaPixels);
        Check("Ember's lights carry amber (" + amber + ") and magenta (" + magenta + ") lanterns (>= " + EmberMinBeaconPixels + " each)",
              amber >= EmberMinBeaconPixels && magenta >= EmberMinBeaconPixels);
    }

    // Tide must not read as one flat teal-green mud, and must keep clear of the
    // player's red and of orange fire (the brief: "no red, lime, orange"). Every
    // variant needs several distinct hue/value clusters and a real value range;
    // its accents -- mint foam, green lamps and blue-violet lamps -- come from the
    // piece atlases drawn over it.
    public const int TideMinClusters = 5;
    public const float TideMinValueRange = 0.15f;
    public const int TideMinMintPixels = 3000;          // bright mint foam / glow pixels in the fires atlas
    public const int TideMinLampPixels = 150;           // green and blue-violet lamp pixels in the landmarks atlas
    public const float TideMaxForbiddenShare = 0.003f;  // tile pixels in the forbidden hue bands (player red 345..15, fire orange 15..45 bright)
    public const int TideMaxForbiddenAtlasPixels = 150; // ... in the piece atlases (a rusted rivet may stray)

    // the forbidden bands: the player's red (345..15 deg, saturated and lit) and bright fire orange
    static bool IsForbiddenTide(Color c)
    {
        float hh, ss, vv;
        Color.RGBToHSV(c, out hh, out ss, out vv);
        float hue = hh * 360f;
        bool red = (hue >= 345f || hue < 15f) && ss > .5f && vv > .45f;
        bool fire = hue >= 15f && hue < 45f && ss > .6f && vv > .6f;
        return red || fire;
    }

    static void CheckTidePalette()
    {
        var spec = BackdropCatalog.For("Tide");
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            int w, h;
            var px = Composite("Tide", out w, out h, v);
            var bins = new Dictionary<int, int>();
            var values = new List<float>();
            int n = 0, forbidden = 0;
            for (int i = 0; i < px.Length; i += 2)
            {
                Color c = px[i];
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                float hue = hh * 360f;
                int band = Mathf.Min((int)(vv / 0.1f), 5);
                int key = Chroma(c) > 0.03f ? ((int)(hue / 30f) % 12) * 6 + band : 100 + band;
                int k;
                bins.TryGetValue(key, out k);
                bins[key] = k + 1;
                values.Add(vv);
                if (IsForbiddenTide(c)) forbidden++;
                n++;
            }
            int clusters = 0;
            foreach (var kv in bins) if (kv.Value >= 0.005f * n) clusters++;
            values.Sort();
            float range = values[(int)(n * 0.95f)] - values[(int)(n * 0.05f)];
            Check("Tide v" + v + " has >= " + TideMinClusters + " distinct hue/value clusters (" + clusters + ")", clusters >= TideMinClusters);
            Check("Tide v" + v + " value range p5..p95 >= " + TideMinValueRange + " (" + range.ToString("F3") + ")", range >= TideMinValueRange);
            Check("Tide v" + v + " keeps out of the player's red and fire orange (" + (forbidden / (float)n).ToString("F4") + " <= " + TideMaxForbiddenShare + ")",
                  forbidden <= TideMaxForbiddenShare * n);
        }
        int mint = 0, green = 0, blue = 0;
        foreach (string atlas in new[] { "landmarks", "pipes", "fires", "sites", "weather" })
        {
            int bad = 0;
            foreach (var c in ReadPixels(ArtDir("Tide") + atlas + ".png"))
            {
                if (c.a < 0.9f) continue;
                if (IsForbiddenTide(c)) bad++;
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                float hue = hh * 360f;
                if (atlas == "fires" && hue >= 135f && hue < 175f && ss > .3f && vv > .6f) mint++;
                if (atlas == "landmarks" && ss > .45f && vv > .45f && hue >= 120f && hue < 190f) green++;
                if (atlas == "landmarks" && ss > .4f && vv > .4f && hue >= 200f && hue < 265f) blue++;
            }
            Check("Tide " + atlas + " keeps out of the player's red and fire orange (" + bad + " px <= " + TideMaxForbiddenAtlasPixels + ")", bad <= TideMaxForbiddenAtlasPixels);
        }
        Check("Tide's foam and vents carry bright mint (" + mint + " px >= " + TideMinMintPixels + ")", mint >= TideMinMintPixels);
        Check("Tide's landmarks carry green (" + green + ") and blue-violet (" + blue + ") lamps (>= " + TideMinLampPixels + " each)",
              green >= TideMinLampPixels && blue >= TideMinLampPixels);
    }

    // docs/art-style.md 4, with each world's enemies on top: the composited
    // lane stays darker and greyer than the enemy bodies, bodies reach 2.5:1
    // and the brightest tone 7:1 against the lane (its median luminance).
    static void CheckReadability()
    {
        for (int wi = 0; wi < WorldManager.Worlds.Length; wi++)
        for (int variant = 1; variant <= Mathf.Max(1, BackdropCatalog.For(WorldManager.Worlds[wi].displayName).variantSets); variant++)
        {
            var wspec = BackdropCatalog.For(WorldManager.Worlds[wi].displayName);
            string world = wspec.world + (wspec.variantSets > 0 ? " v" + variant : "");
            var theme = EnemyPalette.ThemeFor(wi);
            int w, h;
            var px = Composite(wspec.world, out w, out h, variant);
            var lum = new List<float>();
            var val = new List<float>();
            double chroma = 0;
            int x0 = (int)(w * 0.2f), x1 = (int)(w * 0.8f);
            for (int y = 0; y < h; y += 2)
                for (int x = x0; x < x1; x += 2)
                {
                    Color c = px[y * w + x];
                    lum.Add(RelLum(c));
                    val.Add(Value(c));
                    chroma += Chroma(c);
                }
            lum.Sort();
            val.Sort();
            float laneLum = lum[lum.Count / 2];
            float laneV90 = val[(int)(val.Count * 0.9f)];
            float laneChroma = (float)(chroma / lum.Count);
            float body = Contrast(RelLum(theme.hull), laneLum);
            float bright = Mathf.Max(RelLum(theme.light), Mathf.Max(RelLum(theme.hullHighlight), RelLum(theme.bone)));
            float kick = Contrast(bright, laneLum);
            Check(world + " lane with enemies on top: body " + body.ToString("F2") + ":1 >= 2.5, brightest " +
                  kick.ToString("F2") + ":1 >= 7", body >= 2.5f && kick >= 7f);
            Check(world + " lane stays darker than enemy bodies (lane value p90 " + laneV90.ToString("F2") +
                  " <= " + ValueCap(wspec) + " and < hull " + Value(theme.hull).ToString("F2") + ")",
                  laneV90 <= ValueCap(wspec) && laneV90 < Value(theme.hull));
            // Ember's enemies are deliberately grey char on a warm ground, so
            // there only the world's chroma ceiling applies; the green world
            // must also stay greyer than its (green) enemies.
            bool greyer = laneChroma <= ChromaCap(wspec) && (wspec.world != "Verdant" || laneChroma < Chroma(theme.hull));
            Check(world + " lane chroma " + laneChroma.ToString("F2") + " <= " + ChromaCap(wspec) +
                  (wspec.world == "Verdant" ? " and < enemy hull " + Chroma(theme.hull).ToString("F2") : ""), greyer);
        }
    }

    // ------------------------------------------------------------- walls --

    // Every world flies between its reinforced rail (the old flat
    // wallLeft / wallRight planet walls are deleted). Validate the art that
    // WorldPainter actually binds at runtime.
    static void CheckWalls()
    {
        for (int wi = 0; wi < WorldManager.LiveWorldCount; wi++)
        {
            string world = WorldManager.Worlds[wi].displayName;
            bool rail = WorldPainter.RailTextureName(world) != null;
            Check(world + " has a reinforced rail", rail);
            if (rail) failures += WorldRailTest.CheckArt(WorldManager.Worlds[wi]);
        }
        foreach (string world in new[] { "Frost", "Verdant", "Ember", "Tide" })
            foreach (string name in new[] { "wallLeft", "wallRight" })
                Check("the legacy " + world + " " + name + " wall stays deleted",
                      !File.Exists("Assets/Art/Resources/Worlds/" + world + "/" + name + ".png") &&
                      Resources.Load<Texture2D>("Worlds/" + world + "/" + name) == null);
    }

    // The rails are scene quads (leftPipe / rightPipe) that WorldPainter
    // dresses per world with a reinforced rail texture: a band of art inside
    // a wider canvas with transparent margins, mirrored for the right wall.
    // For the rail to show, in colour, three things have to hold:
    //   - the wall shader is alpha-blended and unlit (Pause/WorldRailRepeat).
    //     The built-in opaque Mobile/(Bumped) Diffuse the scene materials
    //     once used draws the transparent margins as the black they are
    //     stored as; the material assets use the rail shader too, so a wall
    //     nothing has painted yet is never opaque;
    //   - the art is inside the camera's view (CameraFit.GameplayHalfWidth)
    //     and outside the ship's reach;
    //   - its texels are square (RailFit.RefreshTextureTiling), whatever the
    //     screen's height.
    static void CheckSpaceRailMaterials()
    {
        bool shaders = true;
        foreach (string name in new[] { "left_1", "right_6", "right_7" })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Walls/" + name + ".mat");
            if (m == null || m.shader == null || m.shader.name != "Pause/WorldRailRepeat" || m.renderQueue < 3000 ||
                !m.HasProperty("_Color") || m.color != Color.white)
                shaders = false;
        }
        Check("Wall material assets use the unlit alpha-blended Pause/WorldRailRepeat shader, untinted", shaders);

        const float ShipReach = 2.4f;
        EditorSceneLoader.Open("gameS1", UnityEditor.SceneManagement.OpenSceneMode.Single);
        var cam = Camera.main;
        var walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
        Check("gameS1 has both rail quads and a camera", cam != null && walls[0] != null && walls[1] != null);
        if (cam == null || walls[0] == null || walls[1] == null) return;

        float refInner = -1f, refOuter = -1f;
        foreach (float aspect in new[] { 9f / 21f, 3f / 4f })
        {
            float ortho = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Mathf.RoundToInt(1000 * aspect), 1000);
            float halfW = ortho * aspect;
            foreach (var theme in WorldManager.Worlds)
            {
                string railName = WorldPainter.RailTextureName(theme.displayName);
                if (railName == null) continue;
                string folder = string.IsNullOrEmpty(theme.resourceFolder) ? theme.displayName : theme.resourceFolder;

                // Columns of the PNG that hold visible art (not transparent,
                // not the black matte the shader cuts).
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                src.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Worlds/" + folder + "/" + railName + ".png"));
                var px = src.GetPixels32();
                int tw = src.width, th = src.height, first = -1, last = -1, lastAny = -1;
                for (int x = 0; x < tw; x++)
                {
                    int solid = 0;
                    for (int y = 0; y < th; y += 3)
                    {
                        Color32 c = px[y * tw + x];
                        if (c.a > 127 && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 8) solid++;
                    }
                    if (solid * 3 >= th / 50 && x > lastAny) lastAny = x;   // any art at all (2% of the column)
                    if (solid * 3 < th / 2) continue;       // at least half the column is rail
                    if (first < 0) first = x;
                    last = x;
                }
                Object.DestroyImmediate(src);

                WorldPainter.Apply(theme);
                bool ok = true, squareOk = true;
                float inner = 0f, outer = 0f, tip = 0f;
                for (int side = 0; side < 2; side++)
                {
                    var wall = walls[side];
                    var sc = wall.transform.localScale;
                    sc.y = ortho * 2f * 1.085f;             // what RailFit sets for this screen
                    wall.transform.localScale = sc;
                    RailFit.RefreshTextureTiling(wall);
                    var mat = wall.GetComponent<Renderer>().sharedMaterial;
                    bool mirrored = mat.mainTextureScale.x < 0f;
                    if (mat.shader.name != "Pause/WorldRailRepeat" || mat.renderQueue < 3000 || mat.mainTexture == null ||
                        mat.mainTexture.name != railName || mat.color != WorldPainter.RailTint(theme) || mirrored != (side == 1))
                        ok = false;
                    // World x of the art's two edges on this wall. The left
                    // wall shows the texture as drawn (gameplay edge = the
                    // art's right side), the right wall its mirror image.
                    float w = Mathf.Abs(wall.transform.lossyScale.x), cx = Mathf.Abs(wall.transform.position.x);
                    float quadOuter = cx + w * 0.5f;
                    float artInner = quadOuter - (last + 1) / (float)tw * w;
                    if (side == 0) tip = quadOuter - (lastAny + 1) / (float)tw * w;
                    float artOuter = quadOuter - first / (float)tw * w;
                    if (side == 0) { inner = artInner; outer = artOuter; }
                    else if (Mathf.Abs(artInner - inner) > 0.01f || Mathf.Abs(artOuter - outer) > 0.01f) ok = false;
                    // Square texels: one texture repeat is as tall as its shape says.
                    float overlap = mat.HasProperty("_Overlap") ? mat.GetFloat("_Overlap") : 0f;
                    float tile = Mathf.Abs(wall.transform.lossyScale.y) / mat.mainTextureScale.y / (1f - overlap);
                    if (Mathf.Abs(tile / (w * th / tw) - 1f) > 0.01f) squareOk = false;
                }
                string tag = theme.displayName + " rail (aspect " + aspect.ToString("F2") + ")";
                Check(tag + " is drawn by the rail shader with its own texture, mirrored on the right wall", ok);
                Check(tag + " art spans |x| " + inner.ToString("F3") + " .. " + outer.ToString("F3") + ": outside the ship's reach (" +
                      ShipReach + "), inside the view (" + halfW.ToString("F2") + ")",
                      inner > ShipReach && outer <= halfW + 0.02f && outer > inner + 0.5f);
                Check(tag + " texels are square", squareOk);
                // The rail edge gameplay measures (boss shots, and whatever
                // else reads BossRails) is the drawn rail's innermost reach:
                // not the padded quad's face, which is well inside the lane,
                // and not the authored fallback that face used to trigger.
                BossRails.Measure();
                float quadFace = Mathf.Abs(walls[0].transform.position.x) - Mathf.Abs(walls[0].transform.lossyScale.x) * 0.5f;
                Check(tag + " BossRails.InnerEdge " + BossRails.InnerEdge.ToString("F3") + " is the drawn rail's innermost art (" +
                      tip.ToString("F3") + " measured from the PNG; solid body from " + inner.ToString("F3") +
                      "), not the padded quad's face (" + quadFace.ToString("F3") + ")",
                      Mathf.Abs(BossRails.InnerEdge - tip) < 0.03f && BossRails.InnerEdge > ShipReach &&
                      BossRails.InnerEdge <= inner + 0.005f);
                BossRails.Reset();
                if (refInner < 0f) { refInner = inner; refOuter = outer; }
                Check(tag + " frames the same lane as the other worlds (inner " + inner.ToString("F3") + " vs " +
                      refInner.ToString("F3") + ", outer " + outer.ToString("F3") + " vs " + refOuter.ToString("F3") + ")",
                      Mathf.Abs(inner - refInner) < 0.05f && Mathf.Abs(outer - refOuter) < 0.05f);
            }
        }
    }

    // Frost's rails are dimmed (WorldPainter.RailBrightness) so they recede
    // behind the lane: rendered alone through the game camera, their light
    // is 50..70% of the undimmed rail's, and every other world draws its
    // rail exactly as painted. Run after CheckSpaceRailMaterials (gameS1 open).
    static void CheckRailBrightness()
    {
        var cam = Camera.main;
        var walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
        if (cam == null || walls[0] == null || walls[1] == null) { Check("rail brightness: gameS1 has its rails and camera", false); return; }
        var hidden = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && r.gameObject != walls[0] && r.gameObject != walls[1]) { r.enabled = false; hidden.Add(r); }
        float saved = WorldPainter.FrostRailBrightness;
        var flags = cam.clearFlags; var bg = cam.backgroundColor;
        float size = cam.orthographicSize, aspect = cam.aspect;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.aspect = 9f / 21f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 900, 2100);
        try
        {
            foreach (var theme in WorldManager.Worlds)
            {
                if (WorldPainter.RailTextureName(theme.displayName) == null) continue;
                WorldPainter.FrostRailBrightness = 1f;
                WorldPainter.Apply(theme);
                float before = RenderedValue(cam);
                WorldPainter.FrostRailBrightness = saved;
                WorldPainter.Apply(theme);
                float after = RenderedValue(cam);
                float ratio = after / Mathf.Max(1e-4f, before);
                Debug.Log("[WB] rail light " + theme.displayName + ": painted " + before.ToString("F0") + ", drawn " + after.ToString("F0"));
                var mat = walls[0].GetComponent<Renderer>().sharedMaterial;
                if (theme.displayName == "Frost")
                    Check("Frost rails render at its rail dial (" + (ratio * 100f).ToString("F0") + "% of the painted rail, dial " +
                          (saved * 100f).ToString("F0") + "%)", before > 1f && Mathf.Abs(ratio - saved) < .02f);
                else
                    Check(theme.displayName + " rails render as painted (" + (ratio * 100f).ToString("F1") + "%, tint untouched)",
                          before > 1f && Mathf.Abs(ratio - 1f) < .01f && mat.color == theme.tint);
            }
        }
        finally
        {
            WorldPainter.FrostRailBrightness = saved;
            cam.clearFlags = flags; cam.backgroundColor = bg;
            cam.orthographicSize = size; cam.aspect = aspect;
            foreach (var r in hidden) if (r != null) r.enabled = true;
        }
    }

    // The summed HSV value of everything the camera draws (a 9:21 frame).
    static float RenderedValue(Camera cam)
    {
        const int w = 270, h = 630;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        Object.DestroyImmediate(rt);
        float sum = 0f;
        foreach (var c in tex.GetPixels32()) sum += Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
        Object.DestroyImmediate(tex);
        return sum;
    }

    // -------------------------------------------------------------- space --

    [System.Serializable] class AtlasRect { public string n; public int x, y, w, h; }
    [System.Serializable] class AtlasManifest { public AtlasRect[] sprites; }

    // Space's atlas cells are variants, cut so each sprite pivots on its art:
    // a variant never hops and a rotation turns in place. Planets, stations
    // and the rest are centred on their bounding box, galaxies on their
    // bright core and wisps on their mass (those two spin).
    static void CheckSpaceAtlas()
    {
        string dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
        var rects = new Dictionary<string, string>();
        foreach (string atlas in new[] { "anim", "fx" })
        {
            var px = ReadPixels(dir + atlas + ".png");
            int w = ReadW, h = ReadH;
            var m = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText(dir + atlas + ".json"));
            float worst = 0f;
            string worstName = "";
            bool inside = true, unique = true;
            foreach (var r in m.sprites)
            {
                if (r.x < 0 || r.y < 0 || r.x + r.w > w || r.y + r.h > h) { inside = false; continue; }
                string key = atlas + ":" + r.x + "," + r.y + "," + r.w + "," + r.h;
                if (rects.ContainsKey(key)) unique = false;
                rects[key] = r.n;

                int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                double mass = 0, mx = 0, my = 0;
                var lum = new List<float>();
                for (int y = 0; y < r.h; y++)
                    for (int x = 0; x < r.w; x++)
                    {
                        Color c = px[(r.y + y) * w + r.x + x];
                        if (c.a <= 0f) continue;
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                        y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                        mass += c.a; mx += c.a * (x + 0.5); my += c.a * (y + 0.5);
                        lum.Add((c.r + c.g + c.b) * c.a);
                    }
                if (x1 < 0) { Check("Space " + r.n + " has art in its rect", false); continue; }
                Vector2 centre = new Vector2((x0 + x1 + 1) * 0.5f, (y0 + y1 + 1) * 0.5f);
                if (r.n.StartsWith("wisp")) centre = new Vector2((float)(mx / mass), (float)(my / mass));
                else if (r.n.StartsWith("galaxy"))
                {
                    lum.Sort();
                    float cut = lum[Mathf.Max(0, lum.Count - 400)];
                    double n = 0, cx = 0, cy = 0;
                    for (int y = 0; y < r.h; y++)
                        for (int x = 0; x < r.w; x++)
                        {
                            Color c = px[(r.y + y) * w + r.x + x];
                            if (c.a <= 0f || (c.r + c.g + c.b) * c.a < cut) continue;
                            n++; cx += x + 0.5; cy += y + 0.5;
                        }
                    centre = new Vector2((float)(cx / n), (float)(cy / n));
                }
                float off = Mathf.Max(Mathf.Abs(centre.x - r.w * 0.5f), Mathf.Abs(centre.y - r.h * 0.5f));
                if (off > worst) { worst = off; worstName = r.n; }

                // Stars are pinpoints and streaks thin lines, not 256 px cells.
                if (r.n == "star" || r.n == "dot")
                    Check("Space " + r.n + " is a pinpoint sprite (" + r.w + "x" + r.h + " px)", r.w <= 32 && r.h <= 32);
                if (r.n == "streak")
                    Check("Space streak is a thin line (" + r.w + "x" + r.h + " px)", r.h <= 12 && r.w >= 4 * r.h);
            }
            Check("Space/" + atlas + " sprite rects lie inside the texture", inside);
            Check("Space/" + atlas + " has no two names on one rect (cells are variants, not padded flipbooks)", unique);
            Check("Space/" + atlas + " art is centred in every sprite rect (worst " + worst.ToString("F1") + " px, " +
                  worstName + ")", worst <= 2f);
        }
    }

    // The depth tiers themselves: farther = smaller, slower, dimmer, hazier
    // and sorted behind; most planets far away, near ones rare.
    static void CheckSpaceTiers()
    {
        var spec = BackdropCatalog.For("Space");
        var tiers = SpaceDirector.Tiers;
        bool mono = true;
        int total = 0;
        for (int i = 0; i < tiers.Length; i++)
        {
            total += tiers[i].weight;
            if (i == 0) continue;
            var a = tiers[i - 1];
            var b = tiers[i];
            if (!(a.scale < b.scale && a.light < b.light && a.clarity <= b.clarity &&
                  spec.Rate(a.layer) < spec.Rate(b.layer) && spec.Order(a.layer) < spec.Order(b.layer) &&
                  spec.Rate(a.planetLayer) < spec.Rate(b.planetLayer) &&
                  spec.Order(a.planetLayer) < spec.Order(b.planetLayer))) mono = false;
        }
        Check("Space depth tiers grow, speed up and brighten strictly far -> near", mono);

        // Parallax follows distance, not drawn size: the biggest planet is
        // still far slower than (and sorted behind) the farthest station or
        // rock, and no faster than a crawl next to the gameplay scroll.
        var nearest = tiers[tiers.Length - 1];
        float planetMax = spec.Rate(nearest.planetLayer), structureMin = spec.Rate(tiers[0].layer);
        Check("Space planets parallax slower than every station / rock (largest planet " + planetMax +
              " <= " + BackdropCatalog.MaxPlanetRate + ", < a third of the farthest structure's " + structureMin + ")",
              planetMax <= BackdropCatalog.MaxPlanetRate && planetMax * 3f < structureMin &&
              spec.Order(nearest.planetLayer) < spec.Order(tiers[0].layer));
        Check("Space planets sit in front of the sky, stars and comets",
              spec.Rate(tiers[0].planetLayer) > spec.Rate("comets") && spec.Rate("comets") > spec.Rate("stars") &&
              spec.Rate("stars") > spec.Rate("sky"));
        // The biggest planet tier, on a 12.4 u tall view, at a mid-run HUD
        // speed of 30: how long from its top edge entering to it being gone.
        float bigPlanet = nearest.scale * 1.25f;
        float seconds = (12.4f + bigPlanet) / (planetMax * WorldBackdrop.ScrollVelocity(0.30f));
        Check("Space's largest planet stays in view a long time (" + seconds.ToString("F0") + " s at speed 30 >= 45 s)",
              seconds >= 45f);
        float farShare = (tiers[0].weight + tiers[1].weight) / (float)total;
        float nearShare = tiers[tiers.Length - 1].weight / (float)total;
        Check("Space random planets remain mostly far away (two farthest tiers " + farShare.ToString("F2") +
              " >= 0.7, nearest " + nearShare.ToString("F2") + " <= 0.10)", farShare >= 0.7f && nearShare <= 0.10f);
        Check("Space comets pass behind every body", spec.Order("comets") < spec.Order(tiers[0].planetLayer) &&
              spec.Order("comets") < spec.Order(tiers[0].layer));
        float nearRate = spec.Rate(tiers[tiers.Length - 1].layer);
        Check("Space bodies stay far behind the ship's own depth (nearest tier rate " + nearRate + " <= 0.15)",
              nearRate <= 0.15f);
    }

    // Watched over the long run: what each set piece looked like at spawn.
    class Seen { public Sprite sprite; public float age; }
    static readonly Dictionary<BackdropPiece, Seen> spaceSeen = new Dictionary<BackdropPiece, Seen>();
    static readonly List<BackdropPiece> spaceActive = new List<BackdropPiece>();
    const int SpaceKinds = 4;
    static float[,] sizeMin, sizeMax, valueMin, valueMax;
    static int[] planetsPerTier;
    static int spriteSwaps, overlaps, crossings, depthErrors, tierMismatches, bodiesSeen, maxGroupsInView;
    static float planetRateMax, structureRateMin;

    static void SpaceWatchReset()
    {
        int t = SpaceDirector.Tiers.Length;
        spaceSeen.Clear();
        sizeMin = new float[SpaceKinds, t]; sizeMax = new float[SpaceKinds, t];
        valueMin = new float[SpaceKinds, t]; valueMax = new float[SpaceKinds, t];
        for (int k = 0; k < SpaceKinds; k++)
            for (int i = 0; i < t; i++)
            {
                sizeMin[k, i] = valueMin[k, i] = float.MaxValue;
                sizeMax[k, i] = valueMax[k, i] = -1f;
            }
        planetsPerTier = new int[t];
        spriteSwaps = overlaps = crossings = depthErrors = tierMismatches = bodiesSeen = maxGroupsInView = 0;
        planetRateMax = 0f;
        structureRateMin = float.MaxValue;
    }

    static void SpaceWatch(SpaceDirector d, BackdropSet set, bool checkOverlap)
    {
        // A set piece keeps the variant it spawned with for its whole life
        // (its age restarting marks a new life in a recycled pool slot).
        foreach (var pool in d.SetPieces)
            foreach (var p in pool.items)
            {
                if (!p.active) { spaceSeen.Remove(p); continue; }
                Seen s;
                if (!spaceSeen.TryGetValue(p, out s) || p.age < s.age)
                {
                    spaceSeen[p] = new Seen { sprite = p.sr.sprite, age = p.age };
                    if (d.Bodies.Contains(pool)) NoteBody(p, set.Spec);
                }
                else
                {
                    if (p.frames == null && p.sr.sprite != s.sprite) spriteSwaps++;
                    s.age = p.age;
                }
            }
        if (!checkOverlap) return;

        // No two bodies of one depth class overlap, unless one is the other's
        // own moon / station. A structure crossing a planet is fine -- it is
        // far nearer -- provided it draws in front and moves faster.
        spaceActive.Clear();
        foreach (var pool in d.Bodies)
            foreach (var p in pool.items) if (p.active) spaceActive.Add(p);
        int groups = 0;
        for (int i = 0; i < spaceActive.Count; i++)
        {
            var a = spaceActive[i];
            if (a.parent == null && Mathf.Abs(a.y) < set.HalfHeight) groups++;
            Bounds ba = a.sr.bounds;
            for (int j = i + 1; j < spaceActive.Count; j++)
            {
                var b = spaceActive[j];
                if (SpaceDirector.Group(a) == SpaceDirector.Group(b)) continue;
                Bounds bb = b.sr.bounds;
                if (!(ba.min.x < bb.max.x && bb.min.x < ba.max.x && ba.min.y < bb.max.y && bb.min.y < ba.max.y)) continue;
                bool pa = SpaceDirector.InPlanetClass(a), pb = SpaceDirector.InPlanetClass(b);
                if (pa == pb) { overlaps++; continue; }
                var planet = pa ? a : b;
                var structure = pa ? b : a;
                crossings++;
                if (!(structure.sr.sortingOrder > planet.sr.sortingOrder && structure.rate > planet.rate)) depthErrors++;
            }
        }
        maxGroupsInView = Mathf.Max(maxGroupsInView, groups);
    }

    static void NoteBody(BackdropPiece p, BackdropCatalog.Spec spec)
    {
        bodiesSeen++;
        var tier = SpaceDirector.Tiers[p.tier];
        if (p.kind == SpaceDirector.Planet) planetsPerTier[p.tier]++;
        sizeMin[p.kind, p.tier] = Mathf.Min(sizeMin[p.kind, p.tier], p.size);
        sizeMax[p.kind, p.tier] = Mathf.Max(sizeMax[p.kind, p.tier], p.size);
        float v = Value(p.color);
        valueMin[p.kind, p.tier] = Mathf.Min(valueMin[p.kind, p.tier], v);
        valueMax[p.kind, p.tier] = Mathf.Max(valueMax[p.kind, p.tier], v);
        // Rate and sorting come from the tier's layer for the body's depth
        // class; a companion shares its planet's.
        bool planetClass = SpaceDirector.InPlanetClass(p);
        string layer = SpaceDirector.LayerOf(p.tier, planetClass);
        if (planetClass) planetRateMax = Mathf.Max(planetRateMax, p.rate);
        else structureRateMin = Mathf.Min(structureRateMin, p.rate);
        if (!Mathf.Approximately(p.rate, spec.Rate(layer))) tierMismatches++;
        if (Mathf.Abs(p.sr.sortingOrder - spec.Order(layer)) > 4) tierMismatches++;
        if (p.parent != null && (p.parent.tier != p.tier || p.parent.rate != p.rate || p.size >= p.parent.size))
            tierMismatches++;
    }

    static void SpaceWatchReport()
    {
        int tiers = SpaceDirector.Tiers.Length;
        Check("Space set pieces keep the sprite they spawned with (" + spriteSwaps + " swaps)", spriteSwaps == 0);
        Check("Space bodies of one depth class never overlap over a 20-minute run (" + overlaps +
              " overlapping samples, at most " + maxGroupsInView + " in view at once)",
              overlaps == 0 && maxGroupsInView <= 5);
        Check("Space structures crossing a planet draw in front of it and move faster (" + crossings +
              " crossing samples, " + depthErrors + " wrong)", depthErrors == 0);
        Check("Space planets in the run all parallax slower than every station / rock (fastest planet " +
              planetRateMax + " < slowest structure " + structureRateMin + ")", planetRateMax < structureRateMin);
        Check("Space bodies take rate and sorting from their depth tier (" + tierMismatches + " mismatches in " +
              bodiesSeen + " bodies)", tierMismatches == 0 && bodiesSeen >= 40);

        // Within a kind, every body of a farther tier is smaller and dimmer
        // than every body of a nearer one.
        bool mono = true;
        string seen = "";
        for (int k = 0; k < SpaceKinds; k++)
        {
            int last = -1, count = 0;
            for (int t = 0; t < tiers; t++)
            {
                if (sizeMax[k, t] < 0f) continue;
                count++;
                if (last >= 0 && !(sizeMax[k, last] < sizeMin[k, t] && valueMax[k, last] < valueMin[k, t])) mono = false;
                last = t;
            }
            seen += (k > 0 ? "/" : "") + count;
        }
        Check("Space farther tier => smaller and dimmer, per kind (tiers seen: planet/station/planetoid/moon " +
              seen + ")", mono);

        int planets = 0;
        foreach (int n in planetsPerTier) planets += n;
        int far = planetsPerTier[0] + planetsPerTier[1], near = planetsPerTier[tiers - 1];
        // Authored hero beats add guaranteed near planets on top of the
        // random tier distribution. Far planets must still be the plurality,
        // while enough near planets appear to define the world's scale.
        Check("Space keeps a far-field majority plus recurring hero planets (" + far + " far, " + near +
              " near of " + planets + ")", planets >= 20 && far >= 0.40f * planets &&
              near >= 0.15f * planets && near <= 0.40f * planets);
    }


    static Color[] ReadPixelsCache;
    static int ReadW, ReadH;

    static Color[] ReadPixels(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        ReadW = t.width;
        ReadH = t.height;
        var px = t.GetPixels();
        Object.DestroyImmediate(t);
        ReadPixelsCache = px;
        return px;
    }

    static float SeamDifference(Color[] px)
    {
        int w = ReadW, h = ReadH;
        double sum = 0;
        for (int x = 0; x < w; x++)
        {
            Color a = px[x];                    // bottom row
            Color b = px[(h - 1) * w + x];      // top row
            sum += Mathf.Abs(a.r * a.a - b.r * b.a) + Mathf.Abs(a.g * a.a - b.g * b.a) +
                   Mathf.Abs(a.b * a.a - b.b * b.a) + Mathf.Abs(a.a - b.a);
        }
        return (float)(sum / (w * 4));
    }

    // The join between two copies of a wrap-blended tile as BackdropSkyWrap
    // draws them: a copy's top row is display v' just under P = 1 - blend
    // (pure art), the next copy's bottom row is v' = 0, which the shader
    // fills with the art at P. Rows are evaluated with the shader's formula.
    static float WrapBlendSeam(Color[] px, float blend)
    {
        int w = ReadW, h = ReadH;
        float p = 1f - blend;
        int shown = Mathf.RoundToInt(h * p);
        Color[] top = WrapRow(px, w, h, (shown - 0.5f) / h, p, blend);
        Color[] bottom = WrapRow(px, w, h, 0.5f / h * p, p, blend);
        double sum = 0;
        for (int x = 0; x < w; x++)
        {
            Color a = bottom[x], b = top[x];
            sum += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
        }
        return (float)(sum / (w * 4));
    }

    static Color[] WrapRow(Color[] px, int w, int h, float v, float p, float blend)
    {
        var row = new Color[w];
        int ra = Mathf.Clamp((int)(v * h), 0, h - 1);
        int rb = Mathf.Clamp((int)((v + p) * h), 0, h - 1);
        float k = Mathf.Clamp01(v / blend);
        for (int x = 0; x < w; x++) row[x] = Color.Lerp(px[rb * w + x], px[ra * w + x], k);
        return row;
    }

    static float ValuePercentile(Color[] px, float q)
    {
        var hist = new int[256];
        int n = 0;
        for (int i = 0; i < px.Length; i += 3)
        {
            Color p = px[i];
            if (p.a <= 0.5f) continue;
            hist[Mathf.Clamp((int)(Mathf.Max(p.r, Mathf.Max(p.g, p.b)) * 255f + 0.5f), 0, 255)]++;
            n++;
        }
        int target = (int)(n * q), acc = 0;
        for (int v = 0; v < 256; v++)
        {
            acc += hist[v];
            if (acc >= target) return v / 255f;
        }
        return 1f;
    }

    static void Measure(Color[] px, out float lum, out float chroma)
    {
        double wsum = 0, l = 0, c = 0;
        for (int i = 0; i < px.Length; i += 3)
        {
            Color p = px[i];
            if (p.a <= 0.01f) continue;
            float mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
            float mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
            wsum += p.a;
            l += p.a * (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b);
            c += p.a * (mx - mn);
        }
        lum = wsum > 0 ? (float)(l / wsum) : 0f;
        chroma = wsum > 0 ? (float)(c / wsum) : 0f;
    }

    // Runtime: freeze, speed integration, bounded pools, portal swap.
    static void CheckRuntime()
    {
        var go = new GameObject("~WorldBackdropTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            foreach (var spec in BackdropCatalog.All)
            {
                Time.timeScale = 1f;
                moveBackGround.speed = 0.2f;
                wb.Show(spec.world, false);
                Check(spec.world + " set builds complete", wb.Current != null && wb.Current.Complete &&
                                                          wb.Current.Spec.world == spec.world);
                if (wb.Current == null) continue;
                for (int i = 0; i < 300; i++) wb.Step(1f / 60f);      // let set pieces spawn

                // Freeze: timeScale 0 must leave every renderer exactly where it was.
                var before = Snapshot(go);
                Time.timeScale = 0f;
                for (int i = 0; i < 120; i++) wb.Step(1f / 60f);
                var after = Snapshot(go);
                Check(spec.world + " background is a still frame while timeScale = 0 (" + before.Count + " renderers)",
                      Same(before, after));
                Time.timeScale = 1f;
                for (int i = 0; i < 2; i++) wb.Step(1f / 60f);
                Check(spec.world + " background moves again when time resumes", !Same(after, Snapshot(go)));

                // Speed coupling: the offset integrates speed, so a speed change alters
                // the rate, never the position.
                var mid = wb.Current.Tiles[wb.Current.Tiles.Count > 2 ? 2 : 0];
                const float dt = 1f / 60f;
                moveBackGround.speed = 0.1f;
                wb.Step(dt);
                float o0 = mid.offset;
                moveBackGround.speed = 0.55f;                         // big jump in speed
                wb.Step(dt);
                float step = Mathf.Repeat(mid.offset - o0, mid.TileHeight);
                float expected = (WorldBackdrop.ScrollVelocity(0.55f) * mid.layer.rate + mid.layer.flow) * dt;
                Check(spec.world + " speed change moves the " + mid.layer.name + " layer by one frame of the new speed (" +
                      step.ToString("F5") + " vs " + expected.ToString("F5") + ")", Mathf.Abs(step - expected) < 1e-4f);

                // Long run: pools never grow.
                int transforms = go.GetComponentsInChildren<Transform>(true).Length;
                int capacity = wb.Current.PieceCount;
                bool withinCapacity = true;
                bool landmarksSmall = true;
                largestLandmark = 0f;
                var space = wb.Current.Director as SpaceDirector;
                if (space != null) SpaceWatchReset();
                for (int i = 0; i < 20 * 60 * 30; i++)           // 20 minutes at 30 fps
                {
                    moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
                    wb.Step(1f / 30f);
                    if (i % 600 == 0)
                        foreach (var p in wb.Current.Director.Pools)
                            if (p.ActiveCount > p.Capacity) withinCapacity = false;
                    if (i % 30 == 0 && !LandmarksSmall(wb)) landmarksSmall = false;
                    if (space != null) SpaceWatch(space, wb.Current, i % 3 == 0);
                }
                if (space != null) SpaceWatchReport();
                Check(spec.world + " pooled set pieces stay bounded over a 20-minute run (" + transforms + " transforms, " +
                      capacity + " pooled)", withinCapacity && wb.Current.PieceCount == capacity &&
                      go.GetComponentsInChildren<Transform>(true).Length == transforms);
                if (spec.world != "Space")
                    Check(spec.world + " landmarks stay small and far over a long run (largest " +
                          largestLandmark.ToString("F2") + " u, limit " + BackdropCatalog.MaxLandmarkSize + ")",
                          landmarksSmall && largestLandmark > 0f);
                int activeNow = 0;
                foreach (var p in wb.Current.Director.Pools) activeNow += p.ActiveCount;
                Check(spec.world + " still spawning set pieces late in a run (" + activeNow + " active)", activeNow > 0);
            }

            // Portal swap cross-fades, then the old set is torn down.
            Time.timeScale = 1f;
            wb.Show("Space", false);
            wb.Show("Frost", true);
            Check("portal swap starts a cross-fade", wb.Current.Spec.world == "Frost" && wb.Current.Alpha < 0.01f);
            int roots = go.transform.childCount;
            Check("both sets exist mid-fade", roots == 2);
            for (int i = 0; i < 120; i++) wb.Step(1f / 60f);
            Check("cross-fade completes and the old set is removed",
                  Mathf.Approximately(wb.Current.Alpha, 1f) && go.transform.childCount == 1);
            Check("unknown world falls back to Space art", BackdropCatalog.For("Nowhere").world == "Space");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ------------------------------------------------- Space: turning bodies --

    // SpaceDirector.DiscOf assumes each sphere's disc fills its centred cut
    // less a small border; a re-cut that broke that would turn the surface
    // about the wrong centre, or slide it over the rim halo.
    static void CheckSpaceDiscs()
    {
        string dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
        float worst = 0f;
        string worstName = "-";
        int spheres = 0;
        foreach (string atlas in new[] { "anim", "fx" })
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(dir + atlas + ".png"));
            var px = tex.GetPixels();
            int w = tex.width;
            var m = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText(dir + atlas + ".json"));
            foreach (var r in m.sprites)
            {
                var s = Sprite.Create(tex, new Rect(r.x, r.y, r.w, r.h), new Vector2(0.5f, 0.5f), 100f);
                s.name = r.n;
                if (SpaceDirector.IsSphere(s))
                {
                    spheres++;
                    Vector4 d = SpaceDirector.DiscOf(s);
                    int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                    for (int y = 0; y < r.h; y++)
                        for (int x = 0; x < r.w; x++)
                            if (px[(r.y + y) * w + r.x + x].a >= 0.5f)
                            {
                                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                                y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                            }
                    // measured disc, atlas pixels
                    var got = new Vector4(r.x + (x0 + x1 + 1) / 2f, r.y + (y0 + y1 + 1) / 2f,
                                          (x1 - x0 + 1) / 2f, (y1 - y0 + 1) / 2f);
                    var want = new Vector4(d.x * w, d.y * tex.height, d.z * w, d.w * tex.height);
                    float err = Mathf.Max(Mathf.Max(Mathf.Abs(got.x - want.x), Mathf.Abs(got.y - want.y)),
                                          Mathf.Max(Mathf.Abs(got.z - want.z), Mathf.Abs(got.w - want.w)));
                    if (x1 < 0) err = 999f;
                    if (err > worst) { worst = err; worstName = r.n; }
                }
                Object.DestroyImmediate(s);
            }
            Object.DestroyImmediate(tex);
        }
        Check("Space spheres' discs fill their centred cuts (" + spheres + " spheres, worst " + worstName + " off by " +
              worst.ToString("F1") + " px <= 2)", spheres >= 12 && worst <= 2f);
    }

    static BackdropPiece FirstSphere(SpaceDirector d, int kind)
    {
        foreach (var pool in d.Bodies)
            foreach (var p in pool.items)
                if (p.active && p.planet && p.kind == kind && p.parent == null) return p;
        return null;
    }

    // Space bodies play their atlas sequences on scaled time; comets stay
    // small and dim, the sky draws seamlessly, and none of it allocates.
    static void CheckSpaceMotion()
    {
        var go = new GameObject("~SpaceMotionTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            Time.timeScale = 1f;
            moveBackGround.speed = 0.2f;
            wb.Show("Space", false);
            var sd = wb.Current != null ? wb.Current.Director as SpaceDirector : null;
            Check("Space backdrop runs the SpaceDirector", sd != null);
            if (sd == null) return;
            string spaceArt = BackdropCatalog.Folder("Space");
            bool asteroidSprites = true;
            for (int i = 0; i < 3; i++)
                asteroidSprites &= Resources.Load<Sprite>(spaceArt + "asteroid_" + i.ToString("00")) != null;
            Check("Space loads three separate asteroid sprites", asteroidSprites);
            bool effectFrames = wb.Current.AsteroidFx != null;
            for (int i = 0; i < 3 && effectFrames; i++)
                effectFrames &= wb.Current.AsteroidFx.Frames("asteroidfx" + i).Length >= 4;
            Check("Space gives every still asteroid at least four smoke/light frames", effectFrames);
            Check("Space loads eight comet animation frames",
                  wb.Current.CometFrames != null && wb.Current.CometFrames.Frames("comet_frame").Length == 8);

            var sky = wb.Current.Tiles[0];
            Check("Space sky draws through the wrap cross-fade shader",
                  sky.WrapMaterial != null && sky.WrapMaterial.shader.name == "Pause/BackdropSkyWrap");
            Check("Space sky uses the run's selected variant and half-turn",
                  sky.SpriteName == SpaceSkySelection.Texture && sky.HalfTurn == SpaceSkySelection.HalfTurn);

            BackdropPiece planet = null;
            foreach (var pool in sd.Bodies)
                foreach (var p in pool.items)
                    if (p.active && p.kind == SpaceDirector.Planet && p.parent == null) planet = p;
            Check("Space opens on a continuously turning giant", planet != null && planet.planet &&
                  planet.turnRate != 0f && planet.frames == null);
            if (planet == null) return;

            const float dt = 1f / 60f;
            bool smooth = true;
            Sprite s0 = planet.sr.sprite;
            float turn0 = planet.turn;
            for (int i = 0; i < 180 && planet.active; i++)
            {
                float before = planet.turn;
                wb.Step(dt);
                if (!planet.active) break;
                if (planet.sr.sprite != s0 ||
                    Mathf.Abs(planet.turn - before - planet.turnRate * dt) > 1e-5f) smooth = false;
            }
            Check("Space giant turns smoothly without sprite jumps", smooth &&
                  Mathf.Abs(planet.turn - turn0) > 0.1f);

            Time.timeScale = 0f;
            float frozen = planet.turn;
            for (int i = 0; i < 60; i++) wb.Step(dt);
            Check("Space giant holds its rotation while timeScale = 0", planet.turn == frozen);
            Time.timeScale = 1f;

            // A long run: every sphere turns, comets stay small and dim.
            const float step = 1f / 30f;
            int spheres = 0, still = 0, comets = 0;
            bool cometsDim = true, animatedCometSeen = false;
            BackdropPiece watchedRock = null;
            Sprite rockImage = null, effectImage = null;
            bool rockStill = true, effectsAdvance = false;
            float rockAge = 0f;
            float brightest = 0f, biggest = 0f;
            for (int i = 0; i < 300 * 30; i++)
            {
                moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
                wb.Step(step);
                if (!effectsAdvance && sd.AsteroidDrift != null)
                {
                    // Codex's asteroids drift on their own (SpaceAsteroidDrift): a
                    // rock keeps its sprite for life while its smoke puffs walk
                    // their stages (AsteroidDriftTest covers the rest).
                    var drift = sd.AsteroidDrift;
                    var rock = drift.Rocks[0].piece;
                    if (watchedRock == null || !rock.active || rock.age < rockAge) { watchedRock = rock; rockImage = rock.sr.sprite; }
                    else rockStill &= rock.sr.sprite == rockImage;
                    rockAge = rock.age;
                    if (drift.PuffOwner[0] < 0) effectImage = null;
                    else if (effectImage == null) effectImage = drift.Puffs[0].sprite;
                    else effectsAdvance |= drift.Puffs[0].sprite != effectImage;
                }
                if (i % 30 != 0) continue;
                foreach (var pool in sd.Bodies)
                    foreach (var p in pool.items)
                    {
                        if (!p.active || !SpaceDirector.IsSphere(p.sr.sprite)) continue;
                        spheres++;
                        if (!p.planet || p.turnRate == 0f || p.frames != null) still++;
                    }
                foreach (var pool in sd.Pools)
                    foreach (var p in pool.items)
                    {
                        if (!p.active || p.sr.sprite == null || !p.sr.sprite.name.StartsWith("comet")) continue;
                        comets++;
                        animatedCometSeen |= p.frames != null && p.frames.Length == 8 &&
                            p.sr.sprite != p.frames[0];
                        brightest = Mathf.Max(brightest, p.sr.color.a);
                        biggest = Mathf.Max(biggest, p.size);
                        Color c = p.sr.color;
                        if (c.a > SpaceDirector.CometMaxAlpha + 1e-4f || p.size > SpaceDirector.CometMaxWidth + 1e-4f ||
                            Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.96f)
                            cometsDim = false;
                    }
            }
            Check("Space spheres all animate over a 5-minute run (" + spheres + " samples, " + still + " still)",
                  spheres > 50 && still == 0);
            Check("Space drifting asteroid keeps its sprite while its smoke puffs advance through their stages",
                  rockStill && effectsAdvance);
            Check("Space comets stay small and dim (" + comets + " samples, alpha <= " + brightest.ToString("F2") + " <= " +
                  SpaceDirector.CometMaxAlpha + ", width <= " + biggest.ToString("F2") + " <= " + SpaceDirector.CometMaxWidth + " u)",
                  cometsDim && comets > 0);
            Check("Space comet animation advances during flight", animatedCometSeen);

            for (int i = 0; i < 60; i++) wb.Step(step);
            long before0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120 * 30; i++) wb.Step(step);
            long used = System.GC.GetAllocatedBytesForCurrentThread() - before0;
            Check("Space backdrop allocates nothing over 2 minutes of frames and spawns (" + used + " bytes)", used == 0);
        }
        finally
        {
            Time.timeScale = 1f;
            Object.DestroyImmediate(go);
        }
    }

    struct State { public Vector3 pos; public Sprite sprite; public Color color; public bool visible; public Vector3 scale; }

    static List<State> Snapshot(GameObject root)
    {
        var list = new List<State>();
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            list.Add(new State { pos = sr.transform.position, sprite = sr.sprite, color = sr.color,
                                 visible = sr.enabled && sr.gameObject.activeInHierarchy,
                                 scale = sr.transform.lossyScale });
        return list;
    }

    static bool Same(List<State> a, List<State> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].visible != b[i].visible || a[i].sprite != b[i].sprite) return false;
            if (!a[i].visible) continue;
            if ((a[i].pos - b[i].pos).sqrMagnitude > 1e-12f) return false;
            if ((a[i].scale - b[i].scale).sqrMagnitude > 1e-12f) return false;
            if (a[i].color != b[i].color) return false;
        }
        return true;
    }
}
