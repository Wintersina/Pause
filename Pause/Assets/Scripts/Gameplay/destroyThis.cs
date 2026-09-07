using UnityEngine;
using System.Collections;

public class destroyThis : MonoBehaviour {

	// Use this for initialization
	void Start () {
       
        Destroy(this.gameObject, 2);
	}
 

}
