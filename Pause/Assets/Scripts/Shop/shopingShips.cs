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

    static readonly Dictionary<string, Sprite> runtimeSprites = new Dictionary<string, Sprite>();

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

        PlayerPrefs.SetString("boughtship" + StarterShip, "True");

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
        PlayerPrefs.SetString("boughtship" + index.ToString(), "True");
        PlayerPrefs.SetInt("spawnShip", index);
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

    // These deliberate paths keep each hull's three health states in the
    // correct intact -> damaged -> critical order.
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

    public static Sprite SpriteFor(int index, int damageState = 0)
    {
        if (index >= 8) return OriginalShipArt.SpriteFor(index);
        string[] keys = { "", "NeonComet", "VoltViper", "SolarFang", "CrimsonHalo",
                          "IonLancer", "JadePhantom", "GoldWarden" };
        string[] states = { "intact", "damaged", "critical" };
        if (index <= 0 || index >= keys.Length) return null;
        string state = states[Mathf.Clamp(damageState, 0, states.Length - 1)];
        return LoadRuntimeSprite("Prefabs/Ships/Retro80s/" + keys[index] + "_" + state);
    }

    public static Sprite IdleSpriteFor(int index, int damageState, int idleFrame)
    {
        // The eight single-image legacy ships (Lightning onward) had no idle
        // frames at all -- this always returned the same static sprite, so
        // they never bobbed like the Retro80s ships do. OriginalIdleSpriteFor
        // now supplies genuine frames for them; falls back to the static
        // sprite only if a frame is actually missing.
        if (index >= 8) return OriginalShipArt.OriginalIdleSpriteFor(index, idleFrame);
        string[] keys = { "", "NeonComet", "VoltViper", "SolarFang", "CrimsonHalo",
                          "IonLancer", "JadePhantom", "GoldWarden" };
        string[] states = { "intact", "damaged", "critical" };
        if (index <= 0 || index >= keys.Length) return null;
        string path = "Prefabs/Ships/Retro80s/" + keys[index] + "_" +
                      states[Mathf.Clamp(damageState, 0, states.Length - 1)] +
                      "_idle" + Mathf.Clamp(idleFrame, 0, 2);
        Sprite sprite = LoadRuntimeSprite(path);
        return sprite != null ? sprite : SpriteFor(index, damageState);
    }

    static Sprite LoadRuntimeSprite(string path)
    {
        Sprite cached;
        if (runtimeSprites.TryGetValue(path, out cached) && cached != null) return cached;
        Texture2D texture = Resources.Load<Texture2D>(path);
        if (texture == null) return null;
        Rect rect = new Rect(0, 0, texture.width, texture.height);
        if (path.Contains("/NeonComet_")) rect = new Rect(16, 18, 32, 29);
        if (path.Contains("/VoltViper_")) rect = new Rect(18, 21, 28, 23);
        if (path.Contains("/SolarFang_")) rect = new Rect(17, 18, 29, 28);
        if (path.Contains("/CrimsonHalo_")) rect = new Rect(8, 4, 46, 57);
        if (path.Contains("/IonLancer_")) rect = new Rect(9, 6, 47, 55);
        if (path.Contains("/JadePhantom_")) rect = new Rect(4, 6, 56, 55);
        if (path.Contains("/GoldWarden_")) rect = new Rect(7, 6, 51, 55);
        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
        runtimeSprites[path] = sprite;
        return sprite;
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
