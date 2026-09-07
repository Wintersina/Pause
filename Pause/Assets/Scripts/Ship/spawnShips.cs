using UnityEngine;
using System.Collections;

public class spawnShips : MonoBehaviour {

    //public GameObject[] ships = new GameObject[shopingShips.shipTotal];
    public GameObject ship;
    private Vector3 shipPos;
    private Quaternion shipRot;
	// Use this for initialization
	void Start () {
        shipPos = new Vector3(0, -2, 1);
        shipRot = new Quaternion(0, 0, 0,0);
        //will spawn ship corrosponding to what user selected
        if (PlayerPrefs.GetInt("spawnShip") == 0)
        {
            ship = (GameObject)Resources.Load("Prefabs/Ships/inGameShips/ship1", typeof(GameObject));
        }
        else {
            if (PlayerPrefs.GetString("boughtship"+ PlayerPrefs.GetInt("spawnShip").ToString()) == "True")
            {
                ship = (GameObject)Resources.Load("Prefabs/Ships/inGameShips/ship" + PlayerPrefs.GetInt("spawnShip").ToString(), typeof(GameObject));
            }
            else
            {
                ship = (GameObject)Resources.Load("Prefabs/Ships/inGameShips/ship1", typeof(GameObject));
            }
        }
           

        // spawn ship
        Instantiate(ship, shipPos, shipRot);
        
	}
	

}
