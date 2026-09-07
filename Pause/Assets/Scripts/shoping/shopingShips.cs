using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;


public class shopingShips : MonoBehaviour {

    //if button is clicked move ship;
    public static bool buttonIsClicked;

    public static int shipTotal = 7;
    public static GameObject[] ships = new GameObject[shipTotal];
    public Button[] shipButtons = new Button[shipTotal];
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
        shipNames[0] = "non";
        shipNames[1] = "Proteus";
        shipNames[2] = "Amadeus";
        shipNames[3] = "Darkwing";
        shipNames[4] = "Cygnus";
        shipNames[5] = "Vesper";
        shipNames[6] = "XR7";

        updateStarDustLabel();

        buttonCanvis = GameObject.Find("Canvas");
        if (buttonCanvis != null) buttonCanvis.SetActive(true);
        popUpCanvis = GameObject.Find("PopUpCanvas");
        if (popUpCanvis != null) popUpCanvis.SetActive(false);
        notEnoughStarDustTimer = 0.0f;

        // initilizing the cost of ships. Each ship has a differnt cost
        // Star dust is much harder to earn now, so the ships have real prices.
        shipCost[0] = 0f;
        shipCost[1] = 150f;
        shipCost[2] = 400f;
        shipCost[3] = 900f;
        shipCost[4] = 1600f;
        shipCost[5] = 2600f;
        shipCost[6] = 4000f;
  

        
  
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
            ships[i] = GameObject.Find("ship" + i.ToString());

            GameObject buttonGo = GameObject.Find("Button" + i.ToString());
            shipButtons[i] = buttonGo != null ? buttonGo.GetComponent<Button>() : null;
        }
        //Debug.Log(PlayerPrefs.GetFloat("PlayerCurrecny").ToString("F2"));
	
	}
	
	// Update is called once per frame
	void Update () {
        notEnoughStarDustTimer -= Time.deltaTime;

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
       
    void updateStarDustLabel()
    {
        if (starDust == null) return;
        starDust.text = "Star Dust: " + PlayerPrefs.GetFloat("PlayerCurrecny").ToString("F2");
    }

    // Ship preview objects only exist for ships authored into the scene.
    void setShipImage(int i)
    {
        if (shipImg == null || ships[i] == null) return;
        Image img = ships[i].GetComponentInChildren<Image>();
        if (img != null) shipImg.sprite = img.sprite;
    }

    public static void turnOffCanves()
    {
        if (buttonCanvis != null) buttonCanvis.SetActive(false);
        if (popUpCanvis != null) popUpCanvis.SetActive(false);
    }
}
