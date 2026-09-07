using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class removeThingsFromScreen : MonoBehaviour {

    public Text fingerMover;
    private bool touched;

    //private GameObject[] backgrounds = new GameObject[4];

	// Use this for initialization
	void Start () {
        touched = true;
        fingerMover.gameObject.SetActive(true);
        /*
        int selector = Random.Range(0, 4);
        for (int i = 0; i < 4; i++)
        {
            backgrounds[i] = GameObject.Find("starsBackground" + i.ToString());
            if ((i != selector))
            {
                backgrounds[i].gameObject.SetActive(false);
            }
        }
        */
        
        
	}
	
	// Update is called once per frame
	void Update () {
	    if(touched && TouchInput.IsPressed)
        {
            
            fingerMover.gameObject.SetActive(false);
            touched = false;
        }
	}
}
