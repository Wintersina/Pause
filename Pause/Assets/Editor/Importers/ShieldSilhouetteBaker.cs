using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Bakes every roster hull's silhouette (stock rest drawing, alpha >=
// ShieldContour.AlphaCutoff) into that ship's own folder,
// ShipArt/<Key>/<Key>_shield.bytes (ShieldSilhouettes' file format with one ship),
// read straight from the source PNGs. The game cuts the blue-atom shield's contour
// from these instead of reading non-readable hull textures back from the GPU.
//
// Alongside it, ShipHitboxBaker writes each ship's tight hull hitbox and
// shield zone (ShipArt/<Key>/<Key>_hitbox.bytes, see ShipHitbox).
//
// Each ship is baked on its own: a ship's PR rewrites only its folder. Re-baked
// automatically when a hull sheet is (re)imported and before every player build;
// ShieldFitTest / ShipHitboxTest fail if a file ever drifts from the art (or from
// ShipHitbox.ShieldMargin).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShieldSilhouetteBaker.Bake
public static class ShieldSilhouetteBaker
{
    // The baked file of ship `id` (null for an id with no art key).
    public static string AssetPath(int id)
    {
        string key = ShipId.ArtKeyOf(id);
        return key == null ? null : "Assets/Art/Resources/" + ShipData.ShieldPath(key) + ".bytes";
    }

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

    // The bytes of ship `id`'s file, or null if it has no art.
    public static byte[] Compute(int id)
    {
        if (!ShipHullArt.Has(id)) return null;
        int w, h;
        var mask = Silhouette(id, out w, out h);
        if (mask == null) { Debug.LogWarning("ShieldSilhouetteBaker: no art for ship " + id); return null; }
        return ShieldSilhouettes.Encode(new List<KeyValuePair<int, bool[]>> { new KeyValuePair<int, bool[]>(id, mask) },
                                        new List<Vector2Int> { new Vector2Int(w, h) });
    }

    // Every roster ship has an up-to-date silhouette file.
    public static bool IsCurrent()
    {
        foreach (int id in ShipId.All)
        {
            var want = Compute(id);
            if (want == null) continue;
            string path = AssetPath(id);
            if (!File.Exists(path)) return false;
            if (!System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), want)) return false;
        }
        return true;
    }

    [MenuItem("Pause/Bake Shield Silhouettes + Hitboxes")]
    public static void Bake()
    {
        BakeIfStale();
    }

    // Writes each ship's silhouette and ShipHitbox's hit zones (ShipHitboxBaker,
    // a sibling file) where they differ. Returns true if it wrote any.
    public static bool BakeIfStale()
    {
        bool wrote = BakeSilhouettesIfStale();
        wrote |= ShipHitboxBaker.BakeIfStale();
        return wrote;
    }

    static bool BakeSilhouettesIfStale()
    {
        bool wrote = false;
        foreach (int id in ShipId.All)
        {
            var bytes = Compute(id);
            if (bytes == null) continue;
            string path = AssetPath(id);
            if (File.Exists(path) && System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), bytes)) continue;
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("ShieldSilhouetteBaker: baked " + path + " (" + bytes.Length + " bytes)");
            wrote = true;
        }
        if (wrote) ShieldSilhouettes.Invalidate();
        return wrote;
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
                if (!ShipArtImporter.IsShipSheet(path) || path.EndsWith("_flame.png")) continue;
                EditorApplication.delayCall -= Rebake;
                EditorApplication.delayCall += Rebake;
                return;
            }
        }

        static void Rebake() { BakeIfStale(); }
    }
}
