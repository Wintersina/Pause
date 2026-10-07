using UnityEngine;
using System.Collections;

public class spawnCorrectly : MonoBehaviour
{

    private static float spawnedTimer;
    void Start()
    {

        spawnedTimer = .0001f;
    }
    void Update()
    {
        spawnedTimer -= Time.deltaTime;
    }
    // this script just adjusts spawn location of anything that is spawned on the board
    void OnTriggerEnter2D(Collider2D hit)
    {
       
        if (hit.gameObject.tag == "Enimey" || hit.gameObject.tag == "pickUp")
        {
            if (spawnedTimer >=0 && hit.gameObject.name != "atom3a(Clone)")
            {
                Destroy(hit.gameObject);
                //this.gameObject.transform.position = new Vector2(hit.gameObject.transform.position.x, hit.gameObject.transform.position.y + .5f);

            }
           
        }
    }

}
