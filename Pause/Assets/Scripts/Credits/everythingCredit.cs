using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.SceneManagement;


public class everythingCredit : MonoBehaviour {

    //public GameObject creditText;
    public Vector3 checkYPos;

	// Use this for initialization
	void Start () {


	}
	
	// Update is called once per frame
	void Update () {

//      
	
	}
    // BACK (UnityEvent-wired): credits -> home, via BackNavigator.
    public void back()
    {
        BackNavigator.Back();
    }
}
