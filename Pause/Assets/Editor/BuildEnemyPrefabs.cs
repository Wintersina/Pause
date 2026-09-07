using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Turns the CC0 Kenney sprites into playable enemy prefabs.
//
// Deliberately reuses the game's existing pieces rather than inventing new
// ones: the player's collisionDetection already reacts to anything tagged
// "Enimey" or "Astr", and moveEnimes / moveItemEnmInStrightLine already do the
// scrolling. So a prefab only needs the right art, collider, tag and mover.
public static class BuildEnemyPrefabs
{
    const string SrcDir = "Assets/Resources/Prefabs/Enemies/Kenney";
    const string OutDir = "Assets/Resources/Prefabs/Enemies";

    // Enemy ships escalate by colour as the run gets harder.
    static readonly string[] ShipOrder =
        { "enemyBlack", "enemyBlue", "enemyGreen", "enemyRed" };

    public static void Run()
    {
        if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);

        var made = new List<string>();

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SrcDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);

            // make sure it imports as a sprite before we reference it
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = 100;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) continue;

            bool isMeteor = name.StartsWith("meteor");
            string outPath = OutDir + "/kn_" + name + ".prefab";
            if (File.Exists(outPath)) { made.Add(name + " (existing)"); continue; }

            var go = new GameObject("kn_" + name);
            go.tag = isMeteor ? "Astr" : "Enimey";

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 3;

            // Kenney art is ~100px tall; scale so hulls read like the game's own
            float h = sprite.bounds.size.y;
            float target = isMeteor ? 0.55f : 0.75f;
            if (h > 0.001f)
            {
                float k = target / h;
                go.transform.localScale = new Vector3(k, k, 1f);
            }

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = sprite.bounds.size * 0.82f;   // slightly forgiving

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            // meteors tumble, ships fly straight -- both are existing scripts
            if (isMeteor) go.AddComponent<moveEnimes>();
            else go.AddComponent<moveItemEnmInStrightLine>();

            PrefabUtility.SaveAsPrefabAsset(go, outPath);
            Object.DestroyImmediate(go);
            made.Add(name);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EP] built " + made.Count + " enemy prefabs");
        EditorApplication.Exit(0);
    }
}
