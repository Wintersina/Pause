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
    public static int shipTotal = 16;
    public static GameObject[] ships = new GameObject[shipTotal];

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
        "Dove", "Turtle",
    };

    // Prices, exposed so the shop buttons can show them before you tap in.
    public static readonly float[] Prices =
    {
        0f, 0f, 600f, 1400f, 2200f, 3200f, 4400f, 5800f,
        800f, 1000f, 1200f, 1600f, 1800f, 2000f, 2400f, 3000f,
    };

    public static float CostFor(int index)
    {
        if (index < 0 || index >= Prices.Length) return 0f;
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

    public void RefreshStarDust()
    {
        if (starDust == null) return;
        starDust.text = "\u2726  STAR DUST   " + PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey).ToString("N0");
    }

    public static void turnOffCanves()
    {
        if (buttonCanvis != null) buttonCanvis.SetActive(false);
    }
}
