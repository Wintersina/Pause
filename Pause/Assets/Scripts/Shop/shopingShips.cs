using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;


// Ship roster data (names, prices, art) and the shop's purchase rules.
//
// The dock itself -- berths, selection, the floating popup and the launch --
// is SpaceDock. The old full-screen yes/no dialog is gone; yes_no() and
// shipselected() stay as thin entry points because shopS6 once wired them to
// UnityEvents.
public class shopingShips : MonoBehaviour {

    // Ship 1 is the starter hull. It must never appear as a paid upgrade,
    // including for players carrying old PlayerPrefs from before the change.
    public const int StarterShip = 1;

    // Retro hulls retain their saved indices; original ships follow them.
    // Every id the code knows (the full roster, slot 0 included).
    public const int RosterTotal = 17;

    // The roster the player can see: slot 0 + the 16 ships in play. Paragon (the
    // last id) is always listed: a locked COMING SOON berth until it is bought.
    public static int shipTotal { get { return RosterTotal; } }
    public static GameObject[] ships = new GameObject[RosterTotal];

    public static int shipNumber;
    public static int LastShipSelected;

    // The HUD canvas (BACK / LIFT-OFF). Authored inactive in shopS6.
    public static GameObject buttonCanvis;

    public Text starDust;

    void Start() {

        PlayerPrefs.SetString(ShipId.OwnedKey(StarterShip), "True");

        if (ships == null || ships.Length < shipTotal)
            ships = new GameObject[shipTotal];
        ships[0] = null;

        var dustCanvas = SceneUtil.FindAny("StarDustCanvas");
        if (dustCanvas != null) dustCanvas.SetActive(true);
        buttonCanvis = SceneUtil.FindAny("Canvas");
        if (buttonCanvis != null) buttonCanvis.SetActive(true);
        RefreshStarDust();

        shipNumber = 0;
        LastShipSelected = 0;
        if (PlayerPrefs.GetInt("spawnShip") == 0)
            PlayerPrefs.SetInt("spawnShip", 0);

        // SpaceDock registers its ships as it builds; pick up any it missed.
        for (int i = 1; i < shipTotal; i++)
            if (ships[i] == null) ships[i] = SceneUtil.FindAny("ship" + i);
    }

    // Legacy UnityEvent entry: resolves the tapped button's "Button<N>" name.
    public void shipselected()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return;
        var button = selected.GetComponentInParent<Button>();
        if (button == null) return;
        string name = button.gameObject.name.Replace("Button", "");
        int index;
        if (int.TryParse(name, out index)) SelectShip(index);
    }

    // Selects a berth: powers the ship up and floats its popup above it.
    public void SelectShip(int index)
    {
        if (index < 1 || index >= shipTotal) return;
        if (SpaceDock.Instance != null) SpaceDock.Instance.Select(index);
    }

    // Legacy UnityEvent entry for the removed yes/no dialog's buttons: presses
    // the floating popup's action (BUY or LAUNCH) for the selected ship.
    public void yes_no()
    {
        var dock = SpaceDock.Instance;
        if (dock != null && dock.popup != null && dock.popup.Visible) dock.popup.Press();
    }

    // Spends the dust, marks the ship owned and selects it, then saves at
    // once: a purchase used to sit in memory until something else happened
    // to save, so killing the app from the shop could undo it.
    public static bool TryPurchase(int index, float cost)
    {
        // Premium ships (Paragon) are sold for real money (ParagonPurchase), never
        // star dust -- see PremiumShips. This is the dust-purchase gate.
        if (PremiumShips.IsPremium(index)) return false;
        // The last three berths open only once enough ships are owned (ShipUnlocks).
        if (ShipUnlocks.IsLocked(index)) return false;
        float dust = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        if (dust < cost) return false;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, dust - cost);
        PlayerPrefs.SetString(ShipId.OwnedKey(index), "True");
        ShipId.Equip(index);
        AchievementTracker.OnShipBought(index, cost);
        PrefsSaver.SaveNow();
        return true;
    }

    // Roster names, available before this component's Start() has run.
    // Scout, Interceptor and Xenon were removed: reskins/duplicates of the
    // roster's own Neon Comet, Volt Viper and Solar Fang (indices 1-3), so
    // the same ship was effectively listed twice under two names.
    public static readonly string[] Roster =
    {
        "non", "Neon Comet", "Volt Viper", "Solar Fang", "Crimson Halo",
        "Ion Lancer", "Jade Phantom", "Gold Warden",
        "Lightning", "Ligher", "Paranoid", "Ninja", "Saboteur", "UFO",
        "Dove", "Turtle", "Paragon",
    };

    // Prices, exposed so the shop buttons can show them before you tap in.
    public static readonly float[] Prices =
    {
        0f, 0f, 700f, 1700f, 2650f, 3850f, 5300f, 7000f,
        950f, 1200f, 1450f, 1950f, 2150f, 2400f, 2900f, 3600f,
        // 16 Paragon: no dust price (a real-money IAP ship, PremiumShips);
        // 0 here is never shown or charged: TryPurchase refuses it.
        0f,
    };

    public static float CostFor(int index)
    {
        if (index < 0 || index >= Prices.Length) return 0f;
        // a premium ship has no dust price: sort it after every dust ship
        if (PremiumShips.IsPremium(index)) return float.MaxValue;
        return Prices[index];
    }

    public static string NameFor(int index)
    {
        if (index < 0 || index >= Roster.Length) return null;
        return Roster[index];
    }

    // Reference world size every hull is normalised to on spawn, whatever its
    // source art's native resolution -- 0.58 world units along its longest
    // edge. Shared so any code that places a ship (the dynamic gameS1
    // spawner, or a ship authored directly into a scene like the tutorial's)
    // produces the same on-screen size instead of drifting apart.
    public const float ReferenceHullSize = 0.58f;

    public static float NormalizedHullScale(Sprite sprite)
    {
        if (sprite == null) return 1f;
        float extent = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        return extent > 0f ? ReferenceHullSize / extent : 1f;
    }

    // Every hull, Retro80s (1-7) and originals (8-15) alike, is one Akira
    // flipbook sheet now (ShipHullArt): the old 64x64 / 32x32 pixel art and
    // its hard-coded crop rects are gone. damageState 0/1/2 = intact /
    // damaged / critical; the sprite has the old hull's world size.
    public static Sprite SpriteFor(int index, int damageState = 0)
    {
        return ShipHullArt.Rest(index, damageState);
    }

    // One of the hull's idle drawings (0..ShipHullArt.IdleDrawings-1). Which
    // drawing shows when, and for how long, is ShipHullArt's tick table;
    // ShipHullAnimator plays it in flight, DockBay in the dock.
    public static Sprite IdleSpriteFor(int index, int damageState, int idleFrame)
    {
        Sprite sprite = ShipHullArt.Idle(index, damageState, idleFrame);
        return sprite != null ? sprite : SpriteFor(index, damageState);
    }

    public static Sprite[] DamageSpritesFor(int index)
    {
        return new[] { SpriteFor(index, 0), SpriteFor(index, 1), SpriteFor(index, 2) };
    }

    // Snaps the read-out to the true saved balance and ends any count-up.
    public void RefreshStarDust()
    {
        countingUp = false;
        if (starDust == null) return;
        SetShown(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey));
    }

    void SetShown(float value)
    {
        starDust.text = "\u2726  STAR DUST   " + Mathf.Max(0f, value).ToString("N0");
    }

    // ------------------------------------------------- purchase count-up

    const float CountUpSeconds = 1.3f, PopupSeconds = 1.5f, PopupRise = 70f;

    bool countingUp;
    float countFrom, countStart;
    readonly List<Text> popups = new List<Text>();
    readonly List<float> popupStarts = new List<float>();

    // Star dust was just bought and is already in the saved balance: float a
    // "+amount" up from the read-out and count the total up to it. A second
    // purchase mid-count restarts from the number on screen. Unscaled time, so
    // it runs while paused; the end always lands on the saved balance.
    public void ShowPurchase(int amount)
    {
        if (starDust == null || !isActiveAndEnabled) { RefreshStarDust(); return; }
        float saved = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        countFrom = countingUp ? CountValue(Time.unscaledTime, saved) : Mathf.Max(0f, saved - amount);
        countStart = Time.unscaledTime;
        countingUp = true;
        SetShown(countFrom);

        var go = new GameObject("+dust", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(starDust.transform.parent, false);
        var t = go.GetComponent<Text>();
        t.font = starDust.font;
        t.fontSize = starDust.fontSize + 12;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = DockArt.Gold;
        t.raycastTarget = false;
        t.text = "+" + amount.ToString("N0");
        var rt = t.rectTransform;
        var src = starDust.rectTransform;
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.sizeDelta = src.sizeDelta;
        rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, -40f);
        popups.Add(t);
        popupStarts.Add(Time.unscaledTime);
    }

    float CountValue(float now, float saved)
    {
        float k = Mathf.Clamp01((now - countStart) / CountUpSeconds);
        float eased = 1f - (1f - k) * (1f - k) * (1f - k);   // ease-out
        return Mathf.Lerp(countFrom, saved, eased);
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (countingUp && starDust != null)
        {
            if (now - countStart >= CountUpSeconds) RefreshStarDust();
            else SetShown(Mathf.Round(CountValue(now, PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey))));
        }
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            float k = (now - popupStarts[i]) / PopupSeconds;
            var t = popups[i];
            if (t == null) { popups.RemoveAt(i); popupStarts.RemoveAt(i); continue; }
            if (k >= 1f) { Destroy(t.gameObject); popups.RemoveAt(i); popupStarts.RemoveAt(i); continue; }
            // A quick pop (grow past 1, settle), then a steady rise and fade.
            float pop = k < .15f ? Mathf.Lerp(.6f, 1.2f, k / .15f) : Mathf.Lerp(1.2f, 1f, Mathf.Clamp01((k - .15f) / .15f));
            t.rectTransform.localScale = Vector3.one * pop;
            var pos = t.rectTransform.anchoredPosition;
            t.rectTransform.anchoredPosition = new Vector2(pos.x, popupBaseY(t) + PopupRise * k);
            t.color = AkiraPalette.WithAlpha(DockArt.Gold, k < .6f ? 1f : 1f - (k - .6f) / .4f);
        }
    }

    // The popup's start height is its rest position; the read-out's own y is
    // not moved, so derive it from the read-out each frame.
    float popupBaseY(Text t) { return starDust.rectTransform.anchoredPosition.y - 40f; }

    void OnDisable()
    {
        if (countingUp) RefreshStarDust();
        foreach (var t in popups) if (t != null) Destroy(t.gameObject);
        popups.Clear();
        popupStarts.Clear();
    }

    public static void turnOffCanves()
    {
        if (buttonCanvis != null) buttonCanvis.SetActive(false);
    }
}
