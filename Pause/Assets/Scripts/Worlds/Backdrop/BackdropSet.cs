using System.Collections.Generic;
using UnityEngine;

// One sky and orientation per run. A half-turn keeps the portrait tile's
// dimensions unchanged while giving each selected sky a second composition.
public static class SpaceSkySelection
{
    public const int VariantCount = 4;
    public static int Variant { get; private set; }
    public static bool HalfTurn { get; private set; }

    public static void BeginRun()
    {
        Variant = Random.Range(1, VariantCount + 1);
        HalfTurn = Random.Range(0, 2) == 1;
    }

    public static string Texture
    {
        get
        {
            if (Variant == 0) BeginRun();
            return "sky_0" + Variant;
        }
    }
}

// One world's complete background: its tile layers, atlases and director,
// under a single root so it can be cross-faded and torn down as a unit.
public class BackdropSet
{
    public readonly BackdropCatalog.Spec Spec;
    public readonly Transform Root;
    public BackdropAtlas Fx { get; private set; }
    public BackdropAtlas Anim { get; private set; }
    public BackdropAtlas Extras { get; private set; }
    public BackdropAtlas NeonFrames { get; private set; }
    public BackdropAtlas AsteroidFx { get; private set; }
    public BackdropAtlas CometFrames { get; private set; }
    public BackdropDirector Director { get; private set; }
    // The variant set the tile layers came from (Spec.variantSets; 0 = none).
    public int Variant { get; private set; }
    readonly Dictionary<string, BackdropAtlas> atlases = new Dictionary<string, BackdropAtlas>();
    public readonly List<BackdropTile> Tiles = new List<BackdropTile>();
    public readonly List<Texture> Textures = new List<Texture>();

    public float Alpha = 1f;
    public float HalfWidth { get; private set; }
    public float HalfHeight { get; private set; }
    public float TileScale { get; private set; }   // world units per tile-art unit (tile art is 6 wide)

    // False when the world's art is missing; the caller then keeps the
    // scene's original background visible.
    public bool Complete { get; private set; }

    float laidOutW = -1f, laidOutH = -1f;

    public BackdropSet(string world, Transform parent, float halfWidth, float halfHeight)
    {
        Spec = BackdropCatalog.For(world);
        Root = new GameObject("Backdrop_" + Spec.world).transform;
        Root.SetParent(parent, false);
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;

        string folder = BackdropCatalog.Folder(Spec.world);
        Fx = LoadAtlas(folder, BackdropCatalog.AtlasFx);
        Anim = LoadAnimAtlas(folder);
        if (Anim.texture != null) Textures.Add(Anim.texture);
        if (Spec.world == "Space") Extras = LoadAtlas(folder, "extras");
        if (Spec.world == "Space") NeonFrames = LoadAtlas(folder, "neon_frames");
        if (Spec.world == "Space") AsteroidFx = LoadAtlas(folder, "asteroid_fx");
        if (Spec.world == "Space") CometFrames = LoadAtlas(folder, "comet_frames_v1");
        Complete = string.IsNullOrEmpty(Spec.keyAtlas) ? Fx.Count > 0 : Atlas(Spec.keyAtlas).Count > 0;

        // A world with variant sets: one per entry, among those installed.
        string tileFolder = folder;
        if (Spec.variantSets > 0)
        {
            Variant = BackdropVariants.For(Spec.world).Pick(Spec.variantSets);
            tileFolder = BackdropCatalog.TileFolder(Spec.world, Variant);
        }

        foreach (var layer in Spec.layers)
        {
            if (layer.kind == BackdropCatalog.Kind.Pieces) continue;
            bool spaceSky = Spec.world == "Space" && layer.name == "sky";
            string texture = spaceSky ? SpaceSkySelection.Texture : layer.texture;
            var sprite = Resources.Load<Sprite>(tileFolder + texture);
            if (sprite == null) { Complete = false; continue; }
            Textures.Add(sprite.texture);
            float lift = BackdropGrade.Lift(Spec, layer, Variant);
            Tiles.Add(new BackdropTile(Root, layer, sprite, Spec.Order(layer.name), DepthZ(layer.name),
                                       spaceSky && SpaceSkySelection.HalfTurn, lift, BackdropGrade.Saturation(Spec, lift)));
        }

        Layout(halfWidth, halfHeight);
        Director = CreateDirector(Spec.world);
        if (Director != null && Complete) Director.Init(this);
    }

    // A named atlas from the world's folder, loaded once and kept for the
    // set's life. A missing atlas is an empty one (Count 0, every Get null),
    // so art that has not landed yet just spawns nothing.
    public BackdropAtlas Atlas(string name)
    {
        BackdropAtlas a;
        if (atlases.TryGetValue(name, out a)) return a;
        a = LoadAtlas(BackdropCatalog.Folder(Spec.world), name);
        atlases[name] = a;
        return a;
    }

    BackdropAtlas LoadAtlas(string folder, string name)
    {
        var tex = Resources.Load<Texture2D>(folder + name);
        var json = Resources.Load<TextAsset>(folder + name);
        if (tex != null) Textures.Add(tex);
        return new BackdropAtlas(tex, json);
    }

    // The world's anim atlas: the high-resolution re-render (anim_hires) when
    // one is installed, else anim. Both carry the same cells under the same
    // names; a body's world size comes from its director (SetSprite), never
    // from the cell's pixel size, so the swap changes sharpness only.
    public static BackdropAtlas LoadAnimAtlas(string folder)
    {
        var tex = Resources.Load<Texture2D>(folder + BackdropCatalog.AtlasAnimHires);
        var json = tex != null ? Resources.Load<TextAsset>(folder + BackdropCatalog.AtlasAnimHires) : null;
        if (tex == null || json == null)
        {
            tex = Resources.Load<Texture2D>(folder + BackdropCatalog.AtlasAnim);
            json = Resources.Load<TextAsset>(folder + BackdropCatalog.AtlasAnim);
        }
        return new BackdropAtlas(tex, json);
    }

    static BackdropDirector CreateDirector(string world)
    {
        switch (world)
        {
            case "Frost": return new FrostDirector();
            case "Verdant": return new VerdantDirector();
            case "Ember": return new EmberDirector();
            default: return new SpaceDirector();
        }
    }

    // Far layers sit deeper; all stay behind the walls (z = 1) and gameplay.
    public float DepthZ(string layer)
    {
        int i = 0;
        for (; i < Spec.layers.Length; i++) if (Spec.layers[i].name == layer) break;
        return 1.9f - 0.05f * i;
    }

    public void Layout(float halfWidth, float halfHeight)
    {
        if (Mathf.Approximately(halfWidth, laidOutW) && Mathf.Approximately(halfHeight, laidOutH)) return;
        laidOutW = HalfWidth = halfWidth;
        laidOutH = HalfHeight = halfHeight;
        // Tiles span the full view width (a little over, so sway never shows an edge).
        float mainWidth = halfWidth * 2f * 1.02f;
        TileScale = mainWidth / 6f;
        foreach (var t in Tiles) t.Layout(mainWidth, halfHeight * 2f, mainWidth);
    }

    // dt is already scaled: 0 while paused.
    public void Tick(float dt, float velocity)
    {
        foreach (var t in Tiles) t.Tick(dt, velocity, HalfHeight * 2f, Alpha);
        if (Director != null && Complete) Director.Tick(dt, velocity);
    }

    public int PieceCount
    {
        get
        {
            int n = 0;
            if (Director != null) foreach (var p in Director.Pools) n += p.Capacity;
            return n;
        }
    }

    // The textures themselves are released by the Resources.UnloadUnusedAssets
    // call WorldBackdrop makes once a cross-fade has finished.
    public void Destroy()
    {
        if (Director != null) Director.Teardown();
        foreach (var t in Tiles) t.Destroy();
        Fx.Destroy();
        Anim.Destroy();
        if (Extras != null) Extras.Destroy();
        if (NeonFrames != null) NeonFrames.Destroy();
        if (AsteroidFx != null) AsteroidFx.Destroy();
        if (CometFrames != null) CometFrames.Destroy();
        foreach (var a in atlases.Values) a.Destroy();
        atlases.Clear();
        if (Root != null) BackdropAtlas.Kill(Root.gameObject);
        Textures.Clear();
    }
}
