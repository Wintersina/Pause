using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class scrollingText : MonoBehaviour {
    public GameObject textControl;
    public GameObject cloneControl;


    void Start()
    {
        textControl = GameObject.Find("scrollingInfoText");
        cloneControl = GameObject.Find("scrollingInfoText2");
        Destroy(textControl, 30);
        Destroy(cloneControl, 60);

    }
    // Update is called once per frame
    void Update()
    {
        transform.Translate(new Vector2(1, 0) * Time.deltaTime * -5f*4.5f );

    }
}
