using UnityEngine;
using System.Collections;

public class destoryAnything : MonoBehaviour
{

    void OnTriggerEnter2D(Collider2D hit)
    {
        
        Destroy(hit.gameObject);
    }
}
