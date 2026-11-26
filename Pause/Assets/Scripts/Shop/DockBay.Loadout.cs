using UnityEngine;

public partial class DockBay
{
    Transform loadoutRoot;
    SpriteRenderer healthMark, droneMark, loadoutPlate;
    TextMesh healthCount, droneCount;
    public Vector3 LoadoutPosition => loadoutRoot != null ? loadoutRoot.localPosition : Vector3.zero;
    public bool ShowsLoadout => loadoutRoot != null && loadoutRoot.gameObject.activeSelf;
    public string HealthCountText => healthCount != null ? healthCount.text : "";
    public string DroneCountText => droneCount != null ? droneCount.text : "";
    void BuildLoadout(Font font)
    {
        loadoutRoot = new GameObject("Loadout").transform; loadoutRoot.SetParent(transform, false);
        loadoutPlate = MakeSprite("LoadoutPlate", DockArt.Get("chip", 6f), Vector3.zero, OrderLabel - 1, loadoutRoot);
        loadoutPlate.drawMode = SpriteDrawMode.Sliced; loadoutPlate.size = new Vector2(.34f, .43f);
        loadoutPlate.color = new Color(.04f, .07f, .12f, .92f);
        healthMark = MakeSprite("Hearts", DockLoadoutArt.Heart, Vector3.zero, OrderLabel, loadoutRoot);
        droneMark = MakeSprite("Drones", DockLoadoutArt.Drone, Vector3.zero, OrderLabel, loadoutRoot);
        healthCount = MakeText("HeartCount", font, Vector3.zero, .16f, TextAnchor.MiddleCenter);
        droneCount = MakeText("DroneCount", font, Vector3.zero, .16f, TextAnchor.MiddleCenter);
        healthCount.transform.SetParent(loadoutRoot, false); droneCount.transform.SetParent(loadoutRoot, false);
        healthCount.color = AkiraPalette.Bone; droneCount.color = AkiraPalette.Cyan;
        loadoutRoot.gameObject.SetActive(false); PlaceLoadout();
    }
    void RefreshLoadout()
    {
        if (loadoutRoot == null) return;
        loadoutRoot.gameObject.SetActive(Owned);
        if (!Owned) return;
        healthCount.text = ShipLives.Max(index).ToString();
        droneCount.text = DroneSquad.StartingFor(index).ToString();
        PlaceLoadout();
    }
    void PlaceLoadout()
    {
        if (loadoutRoot == null) return;
        // The wall readout belongs to the berth number, behind the parked hull.
        Vector3 number = (digits[0].transform.localPosition + digits[1].transform.localPosition) * .5f;
        float scale = Mathf.Max(1f, E);
        loadoutRoot.localPosition = number + Vector3.down * (.34f * scale);
        loadoutRoot.localScale = Vector3.one * scale;
        healthMark.transform.localPosition = new Vector3(-.075f, .10f, 0f);
        healthCount.transform.localPosition = new Vector3(.075f, .10f, 0f);
        droneMark.transform.localPosition = new Vector3(-.075f, -.10f, 0f);
        droneCount.transform.localPosition = new Vector3(.075f, -.10f, 0f);
        FitMark(healthMark, .13f); FitMark(droneMark, .15f);
    }
    static void FitMark(SpriteRenderer mark, float size)
    {
        if (mark.sprite != null) mark.transform.localScale = Vector3.one * size / Mathf.Max(mark.sprite.bounds.size.x, mark.sprite.bounds.size.y);
    }
}
