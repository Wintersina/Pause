using System.Collections.Generic;
using UnityEngine;

// One world's complete background: its tile layers, atlases and director,
// under a single root so it can be cross-faded and torn down as a unit.
public class BackdropSet
{
    public readonly BackdropCatalog.Spec Spec;
    public readonly Transform Root;
    public BackdropAtlas Fx { get; private set; }
    public BackdropAtlas Anim { get; private set; }
    public BackdropDirector Director { get; private set; }
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
        Anim = LoadAtlas(folder, BackdropCatalog.AtlasAnim);
        Complete = Fx.Count > 0;

        foreach (var layer in Spec.layers)
        {
            if (layer.kind == BackdropCatalog.Kind.Pieces) continue;
            var sprite = Resources.Load<Sprite>(folder + layer.texture);
            if (sprite == null) { Complete = false; continue; }
            Textures.Add(sprite.texture);
            Tiles.Add(new BackdropTile(Root, layer, sprite, Spec.Order(layer.name), DepthZ(layer.name)));
        }

        Layout(halfWidth, halfHeight);
        Director = CreateDirector(Spec.world);
        if (Director != null && Complete) Director.Init(this);
    }

    BackdropAtlas LoadAtlas(string folder, string name)
    {
        var tex = Resources.Load<Texture2D>(folder + name);
        var json = Resources.Load<TextAsset>(folder + name);
        if (tex != null) Textures.Add(tex);
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
        Fx.Destroy();
        Anim.Destroy();
        if (Root != null) BackdropAtlas.Kill(Root.gameObject);
        Textures.Clear();
    }
}
