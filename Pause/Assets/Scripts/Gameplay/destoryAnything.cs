using UnityEngine;
using System.Collections;

public class destoryAnything : MonoBehaviour
{

    void OnTriggerEnter2D(Collider2D hit)
    {
        
        Pooled.Destroy(hit.gameObject);   // (a pooled pickup goes back to its pool)
    }
}
