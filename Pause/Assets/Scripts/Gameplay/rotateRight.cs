using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class rotateRight : MonoBehaviour {

    [Header("Hangar departure")]
    [Tooltip("World units per second while a selected ship slides into its launch lane.")]
    public float checkoutSpeed = 4.5f;
    [Tooltip("World units per second while the ship burns out of the dock.")]
    public float launchSpeed = 10f;
    [Tooltip("Degrees per second while the selected hull swings from its berth to launch heading.")]
    public float turnSpeed = 540f;

    private GameObject[] Targets = new GameObject[shopingShips.shipTotal];
    private GameObject[] startingPoss = new GameObject[shopingShips.shipTotal];
    private static bool[] checkedOut = new bool[shopingShips.shipTotal];
    private GameObject liftOffLeft;
    private GameObject liftOffRight;
    public static bool flyOffChecker;
    public static float flyOffTimer;
    public GameObject[] boost = new GameObject[shopingShips.shipTotal];

    public static int shipSelected;

   void Start()
    {
        //boost[0] = null;
        flyOffChecker = false;
        flyOffTimer = -1f;   // disarmed; only counts down once lift-off is armed
        liftOffLeft = SceneUtil.FindAny("LiftOffLeft");
        liftOffRight = SceneUtil.FindAny("LiftOffRight");
        shipSelected = 0;
        // `boost` is serialized, so the scene may still hold an array sized for
        // the old roster. Resize before indexing.
        if (boost == null || boost.Length < shopingShips.shipTotal)
        {
            var grown = new GameObject[shopingShips.shipTotal];
            if (boost != null) boost.CopyTo(grown, 0);
            boost = grown;
        }

        // find all game objects within the current scene and assign them apporpriatly.
        // Newer ships may not have Boost/return/face objects authored yet, so a
        // missing object is skipped instead of throwing.
        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            boost[i] = SceneUtil.FindAny("Boost" + i.ToString());
            if (boost[i] != null) boost[i].SetActive(false);
            startingPoss[i] = SceneUtil.FindAny("return" + i.ToString());
            Targets[i] = SceneUtil.FindAny("face" + i.ToString());
            checkedOut[i] = false;
        }
        

        // find faces;
    }

    //every frame of the game, the game checks if a ship has been checked out.
    // it will then find all other ships and place them back to their original spot.
    // it will also boost the ships out into the nexus if a player selects lift off.
    void Update()
    {  
        if (shipSelected != 0)
        {
            if (shopingShips.ships[shipSelected] != null && Targets[shipSelected] != null)
            {
                shopingShips.ships[shipSelected].transform.position = Vector3.MoveTowards(
                    shopingShips.ships[shipSelected].transform.position,
                    Targets[shipSelected].transform.position,
                    checkoutSpeed * Time.unscaledDeltaTime);
                if (!flyOffChecker)
                    TurnToDockHeading(shopingShips.ships[shipSelected], shipSelected);
            }

            // Find whatever ship was checked out and return it to its position
            for (int k = 1; k < shopingShips.shipTotal; k++)
            {
                if (k == shipSelected) continue;
                if (shopingShips.ships[k] == null || startingPoss[k] == null) continue;

                shopingShips.ships[k].transform.position = Vector3.MoveTowards(
                    shopingShips.ships[k].transform.position,
                    startingPoss[k].transform.position,
                    checkoutSpeed * Time.unscaledDeltaTime);
                TurnToDockHeading(shopingShips.ships[k], k);
            }
        }
        // Unscaled: this is a transition timer, and the game scene leaves
        // Time.timeScale at 0 whenever the player's finger is up. On scaled
        // time the countdown can freeze and lift-off never fires.
        if (flyOffChecker) flyOffTimer -= Time.unscaledDeltaTime;
        if (flyOffChecker && shipSelected != 0)
        {
            // each ship takes off to its own unique location
            if (boost[shipSelected] != null && !ShipExhaust.UsesWind(shipSelected)) boost[shipSelected].SetActive(true);

            if (shopingShips.ships[shipSelected] != null)
            {
                GameObject pad = (shipSelected % 2 == 0) ? liftOffRight : liftOffLeft;
                var ship = shopingShips.ships[shipSelected];
                ship.transform.rotation = Quaternion.RotateTowards(
                    ship.transform.rotation, Quaternion.identity,
                    turnSpeed * Time.unscaledDeltaTime);

                // The launch begins only after the hull has visibly made its
                // quarter-turn toward the top of the screen.
                if (pad != null && Quaternion.Angle(ship.transform.rotation, Quaternion.identity) < 1f)
                {
                    ship.transform.position = Vector3.MoveTowards(
                        ship.transform.position,
                        pad.transform.position,
                        launchSpeed * Time.unscaledDeltaTime);
                }
            }
        }
        // Only fires once armed, so simply idling in the shop no longer
        // eventually launches the player by itself.
        if (flyOffChecker && flyOffTimer <= 0)
        {
            flyOffChecker = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene("gameS1");
        }
    }

    void TurnToDockHeading(GameObject ship, int index)
    {
        ship.transform.rotation = Quaternion.RotateTowards(
            ship.transform.rotation,
            Quaternion.Euler(0f, 0f, ShopSceneExtender.DockAngle(index)),
            turnSpeed * Time.unscaledDeltaTime);
    }
}
