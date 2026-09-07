using UnityEngine;
using System.Collections;

public class TitleScreenMoveDown : MonoBehaviour {

    private float itemSpeed;
    // Use this for initialization
    void Start()
    {
        itemSpeed = 10;
    }

    // Update is called once per frame
    void Update()
    {
      spawnEnimies();
        
    }
    void spawnEnimies()
    {
        transform.Translate(new Vector2(0, 1) * Time.deltaTime * .05f * itemSpeed);
    }
}

