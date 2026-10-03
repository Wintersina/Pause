using UnityEngine;
using UnityEngine.UI;

// Carries the roster index with the button itself. It avoids using the global
// EventSystem selection, which changes while the dock is being scrolled.
[RequireComponent(typeof(Button))]
public class ShopShipSelectButton : MonoBehaviour
{
    public int shipIndex;

    public void Configure(int index)
    {
        shipIndex = index;
        var button = GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Select);
    }

    void Select()
    {
        var shop = Object.FindFirstObjectByType<shopingShips>();
        if (shop != null) shop.SelectShip(shipIndex);
    }
}
