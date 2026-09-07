using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class splashScene : MonoBehaviour {

    private float sceneTimer;
	// Use this for initialization
	void Start () {
        sceneTimer = 3f;

    }
	
	// Update is called once per frame
	void Update () {
        sceneTimer -= Time.deltaTime;
        if (sceneTimer <= 0)
        {
            SceneManager.LoadScene("startS4");
        }

    }
}
