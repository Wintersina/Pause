using UnityEngine;
using System.Collections;

public class moveStarsWithOutTouch : MonoBehaviour {


    private float itemSpeed;
    // Use this for initialization
    void Start()
    {
        itemSpeed = 30;
    }

    // Update is called once per frame
    void Update()
    {

            moveEnim();


    }
    void moveEnim()
    {
        transform.Translate(new Vector2(0, -1) * Time.deltaTime * .05f * itemSpeed);
    }
}

