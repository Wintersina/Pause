using UnityEngine;
using UnityEngine.UI;

// The SHOP row of the home menu: a copy of the Space dock row (same art, label style and press), placed right
// under it, that opens the Shop panel over the home screen. Built at runtime like the Privacy link, so the scene
// itself is not edited. Shown on every platform (ShopCatalog.EntryVisible).
public static class ShopHomeEntry
{
    public const string ButtonName = "ShopEntryButton";
    public const string Template = "shopButton";
    public const string Caption = "Shop";

    // Creates the row once (or shows / hides it as the platform allows). Null when there is nothing to copy.
    public static GameObject Ensure()
    {
        bool want = ShopCatalog.EntryVisible();
        var existing = SceneUtil.FindAny(ButtonName);
        if (existing != null)
        {
            if (existing.activeSelf != want) existing.SetActive(want);
            return existing;
        }
        if (!want) return null;
        var template = SceneUtil.FindAny(Template);
        if (template == null) return null;
        var go = Object.Instantiate(template, template.transform.parent, false);
        go.name = ButtonName;
        go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        go.SetActive(true);
        var button = go.GetComponent<Button>();
        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OpenShop);
        }
        foreach (var label in go.GetComponentsInChildren<Text>(true)) label.text = Caption;
        return go;
    }

    public static void OpenShop() { ShopPanel.Open(); }
}
