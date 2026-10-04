using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Bakes every roster hull's silhouette (stock rest drawing, alpha >=
// ShieldContour.AlphaCutoff) into ShieldSilhouettes' Resources file, read
// straight from the source PNGs. The game cuts the blue-atom shield's contour
// from these instead of reading non-readable hull textures back from the GPU.
//
// Alongside it, ShipHitboxBaker writes each ship's tight hull hitbox and
// shield zone (Resources/Shield/hull_hitboxes.bytes, see ShipHitbox).
//
// Re-baked automatically when a hull sheet is (re)imported and before every
// player build; ShieldFitTest / ShipHitboxTest fail if either file ever
// drifts from the art (or from ShipHitbox.ShieldMargin).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShieldSilhouetteBaker.Bake
public static class ShieldSilhouetteBaker
{
    public const string AssetPath = "Assets/Art/Resources/Shield/hull_silhouettes.bytes";
    const string Hulls = "Assets/Art/Resources/ShipArt/Hulls/";

    // The stock rest silhouette of ship `id`, from its source PNG.
    public static bool[] Silhouette(int id, out int w, out int h)
    {
        w = h = 0;
        var sprite = ShipHullArt.StockRest(id);
        if (sprite == null) return null;
        Rect r = sprite.textureRect;
        int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y);
        w = Mathf.RoundToInt(r.width);
        h = Mathf.RoundToInt(r.height);
        var px = ShieldContour.ReadFromSourceFile(sprite.texture, x, y, w, h);
        return ShieldContour.MaskOf(px, w, h);
    }

    public static byte[] Compute()
    {
        var masks = new List<KeyValuePair<int, bool[]>>();
        var sizes = new List<Vector2Int>();
        foreach (int id in ShipId.All)
        {
            if (!ShipHullArt.Has(id)) continue;
            int w, h;
            var mask = Silhouette(id, out w, out h);
            if (mask == null) { Debug.LogWarning("ShieldSilhouetteBaker: no art for ship " + id); continue; }
            masks.Add(new KeyValuePair<int, bool[]>(id, mask));
            sizes.Add(new Vector2Int(w, h));
        }
        return ShieldSilhouettes.Encode(masks, sizes);
    }

    public static bool IsCurrent()
    {
        if (!File.Exists(AssetPath)) return false;
        var have = File.ReadAllBytes(AssetPath);
        var want = Compute();
        if (have.Length != want.Length) return false;
        for (int i = 0; i < have.Length; i++) if (have[i] != want[i]) return false;
        return true;
    }

    [MenuItem("Pause/Bake Shield Silhouettes + Hitboxes")]
    public static void Bake()
    {
        BakeIfStale();
    }

    // Writes the silhouettes and ShipHitbox's hit zones (ShipHitboxBaker,
    // a sibling file) where they differ. Returns true if it wrote either.
    public static bool BakeIfStale()
    {
        bool wrote = BakeSilhouettesIfStale();
        wrote |= ShipHitboxBaker.BakeIfStale();
        return wrote;
    }

    static bool BakeSilhouettesIfStale()
    {
        var bytes = Compute();
        if (File.Exists(AssetPath))
        {
            var have = File.ReadAllBytes(AssetPath);
            if (have.Length == bytes.Length && System.Linq.Enumerable.SequenceEqual(have, bytes)) return false;
        }
        File.WriteAllBytes(AssetPath, bytes);
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        ShieldSilhouettes.Invalidate();
        Debug.Log("ShieldSilhouetteBaker: baked " + AssetPath + " (" + bytes.Length + " bytes)");
        return true;
    }

    sealed class BeforeBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnPreprocessBuild(BuildReport report) { BakeIfStale(); }
    }

    sealed class OnHullImport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!path.StartsWith(Hulls) || !path.EndsWith(".png")) continue;
                EditorApplication.delayCall -= Rebake;
                EditorApplication.delayCall += Rebake;
                return;
            }
        }

        static void Rebake() { BakeIfStale(); }
    }
}
