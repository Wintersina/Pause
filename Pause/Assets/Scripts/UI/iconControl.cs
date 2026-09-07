using UnityEngine;
using System.Collections;

public class iconControl : MonoBehaviour {

    public GameObject[] icons = new GameObject[3];
    // 0 is play
    // 1 is fastforward
    // 2 is paused

	// Use this for initialization
	void Start () {
        icons[0] = GameObject.Find("playIcon");
        icons[1] = GameObject.Find("fastIcon");
        icons[2] = GameObject.Find("pauseIcon");
        for (int i = 0; i < icons.Length; i++)
        {
            icons[i].SetActive(false);
        }
       
	}

    // Update is called once per frame
    void Update() {

        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            if (collisionDetection.atomCheck)
            {
                icons[0].SetActive(false);
                icons[1].SetActive(true);
                icons[2].SetActive(false);
            }
            else {
                icons[0].SetActive(true);
                icons[1].SetActive(false);
                icons[2].SetActive(false);
            }
        }
       
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            icons[0].SetActive(true);
            icons[1].SetActive(false);
            icons[2].SetActive(false);
        }
        else
        {
            icons[0].SetActive(false);
            icons[1].SetActive(false);
            icons[2].SetActive(true);

        }

    }
}
