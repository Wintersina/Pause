using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DiagShopShip
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);

        var shop = Object.FindFirstObjectByType<shopingShips>();
        if (shop != null) shop.SendMessage("Start", SendMessageOptions.DontRequireReceiver);

        ShopSceneExtender.Build();

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            var go = shopingShips.ships[i];
            if (go == null) { Debug.Log("[DSS] ship" + i + " MISSING"); continue; }
            var comps = go.GetComponents<Component>();
            var names = new System.Collections.Generic.List<string>();
            foreach (var c in comps) names.Add(c == null ? "NULL" : c.GetType().Name);
            Debug.Log("[DSS] ship" + i + " components: " + string.Join(", ", names));
        }
        EditorApplication.Exit(0);
    }
}
