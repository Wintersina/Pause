using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class removeThingsFromScreen : MonoBehaviour {

    public Text fingerMover;
    private bool touched;

	// Use this for initialization
	void Start () {
        touched = true;
        fingerMover.gameObject.SetActive(true);
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
