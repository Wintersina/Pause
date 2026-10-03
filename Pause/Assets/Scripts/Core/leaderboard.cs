using UnityEngine;
using System.Collections;
public class leaderboard : MonoBehaviour {

	// Use this for 

    void Start()
    {
        AdMob.show();
    }
	public void pull_up_leaderboard () {
      
        achievementAPICalls.showLeaderboard();
	}
    public void playTut()
    {
        if (AdMob.isAdsShowwing)
            AdMob.hide();
        // Replaying used to skip this, so the tutorial ran as a real run: it
        // paid real star dust and handed out 5 pauses instead of 50.
        startMenu.PrepareTutorialRun();
        UnityEngine.SceneManagement.SceneManager.LoadScene("tutorialS5");
    }
	
}
