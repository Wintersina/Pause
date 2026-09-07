using UnityEngine;
using System.Collections;

public class stopAnimationOfBoost : MonoBehaviour {

    public Animator player;


    // Use this for initialization
    void Start()
    {
        player = GameObject.FindGameObjectWithTag("boost").GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        if (TouchInput.IsPressed)

        {
            showAnimation(false);
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            showAnimation(false);
        }
        else {
            showAnimation(true);
        }
    }
    void showAnimation(bool show)
    {
        if (show)
        {
            if (!(player == null))
            {
                player.StartPlayback();
            }
        }
        else {
            if (!(player == null))
            {
                player.StopPlayback();
            }
        }
    }
}
