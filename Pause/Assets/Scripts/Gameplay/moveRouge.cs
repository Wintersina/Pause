using UnityEngine;
using System.Collections;

public class moveRouge : MonoBehaviour {





    // Update is called once per frame
    void Update () {
        transform.Translate(new Vector2(1, 0) * Time.deltaTime * .05f * 5);
        
        if (this.gameObject.transform.position.x >= 4.7f)
        {
            this.gameObject.transform.position = new Vector2(-4.7f, -2.5f);
        }
    }
}
