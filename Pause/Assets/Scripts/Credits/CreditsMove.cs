using UnityEngine;
using System.Collections;

public class CreditsMove : MonoBehaviour {


	
	// Update is called once per frame
	void Update () {
        transform.Translate(new Vector2(0, 1) * Time.deltaTime * 40);
    }
}
