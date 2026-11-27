using System.Collections.Generic;
using UnityEngine;

// Stable, visible recipient before collection. Only the nearest nearby pickup draws a receiver line.
public sealed class DronePickupPreview : MonoBehaviour
{
    static readonly List<DronePickupPreview> live = new List<DronePickupPreview>(DroneSquad.MaxSlots);
    TextMesh label;
    LineRenderer line;
    ShotAtomPickup modifier;
    DroneCorePickup core;
    DroneRewardPickup reward;
    int version = -1, squadVersion = -1, recipient = -2;
    public int Recipient => recipient;
    void OnEnable() { if (!live.Contains(this)) live.Add(this); }
    void OnDisable() { live.Remove(this); }
    void Start() { Build(); }
    public void Build()
    {
        if (label != null) return;
        TryGetComponent(out modifier); TryGetComponent(out core); TryGetComponent(out reward);
        var text = new GameObject("Recipient", typeof(TextMesh)); text.transform.SetParent(transform, false);
        text.transform.localPosition = Vector3.up * .4f;
        float scale = Mathf.Max(.01f, transform.lossyScale.x);
        text.transform.localScale = Vector3.one / scale;
        label = text.GetComponent<TextMesh>(); label.font = CodexUi.FindFont(); label.fontSize = 64;
        label.characterSize = .12f * 10f / 64f; label.anchor = TextAnchor.LowerCenter; label.alignment = TextAlignment.Center;
        var renderer = label.GetComponent<MeshRenderer>(); renderer.sharedMaterial = label.font.material; renderer.sortingOrder = 76;
        var ray = new GameObject("Receiver", typeof(LineRenderer)); ray.transform.SetParent(transform, false);
        line = ray.GetComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2;
        line.startWidth = .012f; line.endWidth = .006f; line.sortingOrder = 7;
        line.sharedMaterial = FxLinks.Material; line.enabled = false;
        Refresh();
    }
    public void Refresh()
    {
        if (label == null) return;
        version = ShotAtoms.Version; squadVersion = DroneSquad.Version;
        Color colour = Color.white;
        if (modifier != null)
        {
            recipient = ShotAtoms.IsShared(modifier.atom) ? -1 : DroneSquad.ModifierRecipient(modifier.atom);
            label.text = ShotAtoms.Name(modifier.atom) + "\n" + (recipient < 0 ? "ALL DRONES" : "DRONE " + (recipient + 1));
            colour = ShotAtoms.Colour(modifier.atom);
        }
        else if (core != null)
        {
            recipient = DroneSquad.CoreRecipient(core.mode);
            label.text = DroneSquad.Name(core.mode) + "\n" + (recipient < 0 ? "+DUST" : "DRONE " + (recipient + 1) + "\n" +
                (DroneSquad.Mode(recipient) == core.mode ? "CORE " + DroneSquad.CoreLevel(recipient) + ">" + (DroneSquad.CoreLevel(recipient) + 1) :
                 Short(DroneSquad.Mode(recipient)) + ">" + Short(core.mode)));
            colour = DroneSquad.Colour(core.mode);
        }
        else if (reward != null)
        {
            recipient = DroneSquad.Slots < DroneSquad.MaxSlots ? DroneSquad.Slots : -1;
            label.text = recipient < 0 ? "SQUAD FULL\n+5 DUST" : "NEW DRONE " + (recipient + 1);
            colour = new Color(1f, .85f, .3f);
        }
        label.color = Color.Lerp(colour, Color.white, .3f);
        line.startColor = new Color(colour.r, colour.g, colour.b, .55f);
        line.endColor = new Color(colour.r, colour.g, colour.b, .1f);
    }
    static string Short(DroneMode mode) => mode == DroneMode.Straight ? "FWD" : mode == DroneMode.Alternating ? "LR" : mode == DroneMode.Aimed ? "AIM" : "OFF";
    void LateUpdate()
    {
        Build();
        if (version != ShotAtoms.Version || squadVersion != DroneSquad.Version) Refresh();
        var battery = FloatieBattery.Current;
        bool nearest = battery != null && recipient >= 0 && recipient < DroneSquad.Slots;
        float distance = nearest ? (transform.position - battery.transform.position).sqrMagnitude : float.MaxValue;
        nearest &= distance < 2.5f * 2.5f;
        if (nearest)
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i] != this && (live[i].transform.position - battery.transform.position).sqrMagnitude < distance - .001f)
                { nearest = false; break; }
        line.enabled = nearest;
        if (nearest)
        {
            line.SetPosition(0, transform.position); line.SetPosition(1, battery.MuzzleFor(recipient));
            battery.PreviewDrone(recipient); DroneSquad.Select(recipient);
        }
    }
}
