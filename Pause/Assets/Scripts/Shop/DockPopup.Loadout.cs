using UnityEngine;
using UnityEngine.UI;

public partial class DockPopup
{
    RectTransform droneBadge;
    Text droneBadgeCount;
    public int StartingDronesShown { get; private set; }
    public bool DroneBadgeVisible => droneBadge != null && droneBadge.gameObject.activeSelf;
    void BuildDroneBadge()
    {
        droneBadge = Rect("StartingDrones", body);
        var icon = Rect("Drone", droneBadge);
        Place(icon, new Vector2(0f, .5f), Vector2.zero, Vector2.one * LivesIconSize, new Vector2(0f, .5f));
        var image = icon.gameObject.AddComponent<Image>(); image.sprite = DockLoadoutArt.Drone; image.preserveAspect = true; image.raycastTarget = false;
        droneBadgeCount = Label("Count", droneBadge, font, StatusSize, TextAnchor.MiddleLeft, AkiraPalette.Cyan);
        Place(droneBadgeCount.rectTransform, new Vector2(0f, .5f), new Vector2(LivesIconSize + 3f, 0f), new Vector2(14f, 18f), new Vector2(0f, .5f));
    }
    void ShowDrones(int ship)
    {
        StartingDronesShown = DroneSquad.StartingFor(ship);
        int level = ShipWeaponUpgrades.Level(ship);
        bool preview = SkinRowVisible && !ShipSkins.IsOwned(ship, SkinShown);
        int next = DroneSquad.UnlockedAtLevel(Mathf.Min(ShipWeaponUpgrades.MaxLevel, level + 1));
        droneBadgeCount.text = StartingDronesShown + (preview && next > StartingDronesShown ? "+1" : "");
    }
    void FitDroneBadge(ref float x)
    {
        float width = LivesIconSize + 3f + droneBadgeCount.preferredWidth;
        Place(droneBadge, new Vector2(1f, 1f), new Vector2(-x, -Pad - 34f), new Vector2(width, 18f), new Vector2(1f, .5f));
        x += width + 8f;
    }
}
