using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class backButton : MonoBehaviour {

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
        // Back/Escape. Was Android-gated, and the touchCount == 0 guard
        // swallowed the key on every other platform.
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // if you push the back key, turn off ads
                if (AdMob.isAdsShowwing)
                {
                    AdMob.hide();
                }
                SceneManager.LoadScene("startS4");
                return;
            }
        }
    }
}
