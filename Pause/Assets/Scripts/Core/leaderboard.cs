using UnityEngine;
using System.Collections;
public class leaderboard : MonoBehaviour {

	// Use this for 

	// The Options screen's CREDITS button (it took the old LeaderBoard
	// button's slot; the leaderboard moved to the home screen). The home
	// music plays on through (MenuMusic) and Back returns to Options.
	public const string CreditsScene = "creditsS7";
	public static System.Action<string> LoadScene = name => MenuNav.Load(name);
	public void credits () {
        LoadScene(CreditsScene);
	}
    public void playTut()
    {
        // Replaying used to skip this, so the tutorial ran as a real run: it
        // paid real star dust and handed out 5 pauses instead of 50.
        startMenu.PrepareTutorialRun();
        MenuNav.Load("tutorialS5");
    }
	
}
