using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;


public class shopingShips : MonoBehaviour {

    static readonly Dictionary<string, Sprite> runtimeSprites = new Dictionary<string, Sprite>();

    // Ship 1 is the starter hull. It must never appear as a paid upgrade,
    // including for players carrying old PlayerPrefs from before the change.
    public const int StarterShip = 1;

    //if button is clicked move ship;
    public static bool buttonIsClicked;

    // Retro hulls retain their saved indices; original ships follow them.
    public static int shipTotal = 16;
    public static GameObject[] ships = new GameObject[shipTotal];
    public Button[] shipButtons = new Button[shipTotal];
    private static string[] shipNamesShared;
    private string[] shipNames = new string[shipTotal];
    private float[] shipCost = new float[shipTotal];
    
    public static int shipNumber;
    private static int spawnShipNumber;
    public static int LastShipSelected;

    // pop up canvis section
    public static GameObject popUpCanvis;
    public static GameObject buttonCanvis;
    public Image shipImg;
    public Button yesButton;
    public Button noButton;
    public Text question;
    // star dust controll
    //public Text notEnoughStarDust;
    private float notEnoughStarDustTimer;

    public Text starDust;

    // Use this for initialization
    void Start() {

        PlayerPrefs.SetString("boughtship" + StarterShip, "True");

        // `shipButtons` is serialized, so the scene may still hold an array
        // sized for the old roster; grow it before indexing.
        if (shipButtons == null || shipButtons.Length < shipTotal)
        {
            var grown = new Button[shipTotal];
            if (shipButtons != null) shipButtons.CopyTo(grown, 0);
            shipButtons = grown;
        }
        if (ships == null || ships.Length < shipTotal)
            ships = new GameObject[shipTotal];
        if (shipNames == null || shipNames.Length < shipTotal)
            shipNames = new string[shipTotal];
        if (shipCost == null || shipCost.Length < shipTotal)
            shipCost = new float[shipTotal];

        // initilizing the names of ships
        shipNamesShared = shipNames;
        for (int i = 0; i < shipNames.Length && i < Roster.Length; i++)
            shipNames[i] = Roster[i];

        updateStarDustLabel();

        var dustCanvas = SceneUtil.FindAny("StarDustCanvas");
        if (dustCanvas != null) dustCanvas.SetActive(true);

        buttonCanvis = SceneUtil.FindAny("Canvas");
        if (buttonCanvis != null) buttonCanvis.SetActive(true);
        popUpCanvis = SceneUtil.FindAny("PopUpCanvas");
        if (popUpCanvis != null) popUpCanvis.SetActive(false);
        notEnoughStarDustTimer = 0.0f;

        // initilizing the cost of ships. Each ship has a differnt cost
        // Star dust is much harder to earn now, so the ships have real prices.
        for (int i = 0; i < shipCost.Length && i < Prices.Length; i++)
            shipCost[i] = Prices[i];
  

        
  
        // setting index zeros to null for having an empty object
        ships[0] = null;
        shipButtons[0] = null;
        
        shipNumber = 0;
        LastShipSelected = 0;
        if (PlayerPrefs.GetInt("spawnShip") == 0)
        {
            spawnShipNumber = 0;
            PlayerPrefs.SetInt("spawnShip", spawnShipNumber);
        }
        // will find every button in this secene and give player option to buy a ship. if player has already bought it will not show
        // buy as an option
        // Buttons for the newer ships may not be authored in the scene yet.
        // Missing entries are skipped rather than throwing -- the old code
        // called GetComponent<Button>() straight off a possibly-null Find().
        for (int i = 1; i <= ships.Length - 1; i++)
        {
            ships[i] = SceneUtil.FindAny("ship" + i.ToString());

            GameObject buttonGo = SceneUtil.FindAny("Button" + i.ToString());
            shipButtons[i] = buttonGo != null ? buttonGo.GetComponent<Button>() : null;
        }
        //Debug.Log(PlayerPrefs.GetFloat("PlayerCurrecny").ToString("F2"));
	
	}
	
	// Update is called once per frame
	void Update () {
        notEnoughStarDustTimer -= Time.deltaTime;

        // setShipImage() only ever set shipImg.sprite once, when the
        // confirm/already-owned dialog opened, so it sat on a single static
        // frame -- unlike every other ship display in the shop (the dock
        // bay, the launch sequence), which all idle-cycle. Keep it animating
        // for as long as the dialog is actually up.
        if (shipImg != null && popUpCanvis != null && popUpCanvis.activeSelf && shipNumber > 0)
        {
            int idleFrame = Mathf.FloorToInt(Time.unscaledTime * 8f) % 3;
            Sprite animated = IdleSpriteFor(shipNumber, 0, idleFrame);
            if (animated != null) shipImg.sprite = animated;
        }
    }
    // redo this function later for efficincy
    // what this function does :
    //   seaches for the index in prefab by checking what button was pushed. if that button was pushed then
    //   keep track of the ship number selected. once player pushes start
    public void shipselected()
    {
        for (int i = 1; i < ships.Length; i++)
        {
            if(EventSystem.current.currentSelectedGameObject.name == "Button" + i.ToString()) {

                // if you have bought the current ship
                if (PlayerPrefs.GetString("boughtship"+i.ToString()) == "True")
                {
                    buttonCanvis.SetActive(false);
                    popUpCanvis.SetActive(true);
                    
                    setShipImage(i);
                    question.text = "You Have Already Bought " + shipNames[i] + " star ship.";
                    Text changeyestoOk = yesButton.gameObject.GetComponentInChildren<Text>();
                    Text changenotoCancel = noButton.gameObject.GetComponentInChildren<Text>();
                    changenotoCancel.text = "Cancel";
                    changeyestoOk.text = "Select";
                    //used for selecting the previously selected ships
                    LastShipSelected = shipNumber;
                    shipNumber = i;
                }
                // if ship was not bought yet
                else {
                    
                    buttonCanvis.SetActive(false);
                    popUpCanvis.SetActive(true);
                    setShipImage(i);
                    question.text = "Cost: "+shipCost[i]+ " StarDust. \n\n" + "Would you like to buy the " + shipNames[i] + " ship?";
                    Text changeyestoOk = yesButton.gameObject.GetComponentInChildren<Text>();
                    changeyestoOk.text = "Yes";
                    Text changenotoCancel = noButton.gameObject.GetComponentInChildren<Text>();
                    changenotoCancel.text = "No";
                    LastShipSelected = shipNumber;
                    shipNumber = i;

                    
                }
            }
        }
    }

    // will not need this function
   
    public void yes_no()
    {
        // if you push yes.
        if (EventSystem.current.currentSelectedGameObject.name == "Yes Button")
        {   // if the player has pushed yet and has bought the ship
            if (PlayerPrefs.GetString("boughtship" + shipNumber.ToString()) == "True")
            {
                
                spawnShipNumber = shipNumber;
                startMenu.spawnTracker = spawnShipNumber;               // used incase player goses back to main menu
                PlayerPrefs.SetInt("spawnShip", spawnShipNumber);
                rotateRight.shipSelected = shipNumber;
                buttonCanvis.SetActive(true);
                popUpCanvis.SetActive(false);
            }   //if the player has not bought the ship
            else if (PlayerPrefs.GetFloat("PlayerCurrecny") >= shipCost[shipNumber])
            {
                spawnShipNumber = shipNumber;
                startMenu.spawnTracker = spawnShipNumber;               // used incase player goes back to main menu
                PlayerPrefs.SetInt("spawnShip", spawnShipNumber);
                rotateRight.shipSelected = shipNumber;
                // reduce cost, switch canvases and show new cost
                PlayerPrefs.SetFloat("PlayerCurrecny", PlayerPrefs.GetFloat("PlayerCurrecny") - shipCost[shipNumber]);
                updateStarDustLabel();
                buttonCanvis.SetActive(true);
                popUpCanvis.SetActive(false);
                PlayerPrefs.SetString("boughtship" + shipNumber.ToString(), "True");
            } // else they dont have enough star dust
            else
            {
                if (notEnoughStarDustTimer <= 0)
                {
                    //notEnoughStarDust.text = "Not Enough StarDust to buy this....";
                    notEnoughStarDustTimer = 2.0f;
                }

            }// if you push no
        }
        else if (EventSystem.current.currentSelectedGameObject.name == "No Button")
        { 
            buttonCanvis.SetActive(true);
            popUpCanvis.SetActive(false);
        }
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
        if (runtimeSprites.TryGetValue(path, out cached)) return cached;
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

    void updateStarDustLabel()
    {
        if (starDust == null) return;
        starDust.text = "✦  STAR DUST   " + PlayerPrefs.GetFloat("PlayerCurrecny").ToString("F2");
    }

    // Fills the confirm panel's preview.
    //
    // Authored ships carry a child UI Image holding their shop art, but ships
    // built at runtime only have a SpriteRenderer -- so this used to come back
    // null for them and the panel kept showing whichever ship was opened last.
    // Falls through Image -> SpriteRenderer -> the ship's sheet in Resources,
    // so every ship resolves to something.
    void setShipImage(int i)
    {
        if (shipImg == null) return;

        Sprite found = null;

        // The scene's original ship children use large, unrelated menu art.
        // Always prefer the canonical roster sheet, which is also what the
        // dock displays. This keeps Darkwing and every later ship consistent.
        found = SpriteFor(i, 0);

        if (found == null && ships[i] != null)
        {
            SpriteRenderer sr = ships[i].GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null) found = sr.sprite;
        }

        if (found == null)
        {
            Image img = ships[i] != null ? ships[i].GetComponentInChildren<Image>(true) : null;
            if (img != null && img.sprite != null)
            {
                found = img.sprite;
            }
        }

        if (found != null)
        {
            shipImg.sprite = found;
            // The original panel was sized from a short ship. Wide sheets such
            // as Darkwing could cover the question/cost text. Every hull now
            // gets the same bounded preview card; preserveAspect handles the
            // individual silhouette without scaling the UI around its pixels.
            shipImg.preserveAspect = true;
            var rect = shipImg.rectTransform;
            rect.localScale = Vector3.one;
            // The preview had an old perpetual-rotation script attached. It
            // made a selected hull look like a random spinning icon.
            var oldSpinner = shipImg.GetComponent<roate>();
            if (oldSpinner != null) oldSpinner.enabled = false;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(240f, 180f);
        }
    }

    public static void turnOffCanves()
    {
        if (buttonCanvis != null) buttonCanvis.SetActive(false);
        if (popUpCanvis != null) popUpCanvis.SetActive(false);
    }
}
