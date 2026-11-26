using UnityEngine;
using UnityEngine.UI;

public partial class ScoreHud
{
    public const float UpgradeCardWidth = 340f, UpgradeCardHeight = 72f;
    public const int UpgradeHeaderSize = 16, UpgradeBenefitSize = 22;
    sealed class UpgradeCard
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image drone, atom;
        public Text recipient, upgrade;
    }
    UpgradeCard[] upgradeCards;
    string upgradeBenefit, upgradeHeader;
    public string AtomUpgradeBenefit => AtomUpgradeShowing ? upgradeBenefit : null;
    public string AtomUpgradeHeader => AtomUpgradeShowing ? upgradeHeader : null;
    public RectTransform AtomUpgradeCard => AtomUpgradeShowing ? upgradeCards[atomSlot].root : null;
    public Image AtomUpgradeIcon => AtomUpgradeShowing ? upgradeCards[atomSlot].atom : null;

    void BuildUpgradeCards()
    {
        upgradeCards = new UpgradeCard[PopupPool];
        for (int i = 0; i < upgradeCards.Length; i++)
        {
            var go = new GameObject("UpgradeCard", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(canvasRect, false);
            var card = new UpgradeCard { root = go.GetComponent<RectTransform>(), group = go.GetComponent<CanvasGroup>() };
            card.root.anchorMin = card.root.anchorMax = card.root.pivot = Vector2.one * .5f;
            card.root.sizeDelta = new Vector2(UpgradeCardWidth, UpgradeCardHeight);
            var backing = go.GetComponent<Image>(); backing.color = new Color(.025f, .035f, .065f, .92f); backing.raycastTarget = false;
            card.group.blocksRaycasts = false; card.group.interactable = false;
            card.drone = CardIcon("Drone", card.root, new Vector2(-145f, 20f));
            card.drone.sprite = DockLoadoutArt.Drone;
            card.atom = CardIcon("Atom", card.root, new Vector2(-6f, 20f));
            card.recipient = CardLabel("Recipient", card.root, new Vector2(-128f, 20f), 113f);
            card.upgrade = CardLabel("Upgrade", card.root, new Vector2(12f, 20f), 143f);
            go.SetActive(false); upgradeCards[i] = card;
        }
    }
    Image CardIcon(string name, Transform parent, Vector2 at)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false); image.raycastTarget = false; image.preserveAspect = true;
        image.rectTransform.anchoredPosition = at; image.rectTransform.sizeDelta = Vector2.one * 20f;
        return image;
    }
    Text CardLabel(string name, Transform parent, Vector2 at, float width)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false); Style(text, font, UpgradeHeaderSize, AkiraPalette.Bone, TextAnchor.MiddleLeft);
        var rt = text.rectTransform; rt.pivot = new Vector2(0f, .5f); rt.anchoredPosition = at; rt.sizeDelta = new Vector2(width, 24f);
        text.resizeTextForBestFit = true; text.resizeTextMinSize = 11; text.resizeTextMaxSize = UpgradeHeaderSize;
        return text;
    }
    void ResetAtomCard(int index)
    {
        if (upgradeCards != null) upgradeCards[index].root.gameObject.SetActive(false);
        var text = popups[index].text;
        text.rectTransform.sizeDelta = new Vector2(360f, 60f);
        text.alignment = TextAnchor.MiddleCenter;
    }
    void ConfigureAtomCard(int index, string title, string level)
    {
        var card = upgradeCards[index];
        string recipient = "SQUAD", name = title;
        int arrow = title.IndexOf("->", System.StringComparison.Ordinal);
        if (arrow >= 0) recipient = title.Substring(arrow + 2).Trim();
        else if (title.Contains("ALL DRONES")) recipient = "ALL DRONES";
        else if (title.StartsWith("DRONE ", System.StringComparison.Ordinal))
        {
            int end = title.IndexOf(' ', 6);
            recipient = end >= 0 ? title.Substring(0, end) : title;
            name = end >= 0 ? title.Substring(end + 1) : "ONLINE";
        }
        card.atom.gameObject.SetActive(false);
        for (int atom = 0; atom < ShotAtomTuning.Count; atom++)
        {
            var kind = (ShotAtom)atom;
            if (!title.StartsWith(ShotAtoms.Name(kind), System.StringComparison.Ordinal)) continue;
            name = ShotAtoms.Name(kind) + (title.Contains("MAXED") ? " MAXED" : "");
            var frames = PickupArt.Frames(PickupArt.IdleName(ShotAtoms.KindOf(kind)), PickupArt.ShotIdleTicks.Length);
            card.atom.sprite = frames[0]; card.atom.gameObject.SetActive(true); break;
        }
        card.recipient.text = recipient;
        card.upgrade.text = name + (string.IsNullOrEmpty(level) ? "" : " · " + level);
        upgradeHeader = recipient + "  " + card.upgrade.text;
        card.root.gameObject.SetActive(true); card.root.SetAsLastSibling();
    }
    static string EmphasizeBenefit(string hint)
    {
        if (string.IsNullOrEmpty(hint)) return "";
        int arrow = hint.IndexOf('→');
        if (arrow < 0) return hint;
        return "<color=#B5BDCC>" + hint.Substring(0, arrow + 1) + "</color><color=#FFB43C>" + hint.Substring(arrow + 1) + "</color>";
    }
    void SyncAtomCards()
    {
        if (upgradeCards == null) return;
        for (int i = 0; i < upgradeCards.Length; i++)
        {
            var card = upgradeCards[i]; bool on = i == atomSlot && AtomUpgradeShowing;
            if (!on) { if (card.root.gameObject.activeSelf) card.root.gameObject.SetActive(false); continue; }
            var text = popups[i].text;
            card.root.anchoredPosition = text.rectTransform.anchoredPosition + Vector2.up * 10f;
            card.root.localScale = text.rectTransform.localScale;
            card.group.alpha = text.color.a;
        }
    }
}
