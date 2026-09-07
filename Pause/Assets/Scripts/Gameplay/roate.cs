using UnityEngine;
using System.Collections;

public class roate : MonoBehaviour {

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
        transform.Rotate(0,0, 30 *Time.deltaTime);
    } 
}
    