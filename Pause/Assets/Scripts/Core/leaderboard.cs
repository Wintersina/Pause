using UnityEngine;
using System.Collections;
public class leaderboard : MonoBehaviour {

	// Use this for 

	// The LeaderBoard button: the in-game rankings panel. Its "View all"
	// opens the store's own leaderboard screen.
	public void pull_up_leaderboard () {
        LeaderboardPanel.Open();
	}
    public void playTut()
    {
        // Replaying used to skip this, so the tutorial ran as a real run: it
        // paid real star dust and handed out 5 pauses instead of 50.
        startMenu.PrepareTutorialRun();
        UnityEngine.SceneManagement.SceneManager.LoadScene("tutorialS5");
    }
	
}
