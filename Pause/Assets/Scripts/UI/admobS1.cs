using UnityEngine;
using System.Collections;

public class admobS1 : MonoBehaviour {

	// Use this for initialization

	// Update is called once per frame
	void Update () {
        if (!buttonClicks.playerDied)
        {
            AdMob.hide();
        }
	}
}
