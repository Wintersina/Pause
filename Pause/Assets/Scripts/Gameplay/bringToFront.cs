using UnityEngine;
using System.Collections;

public class bringToFront : MonoBehaviour {

    void OnEnable()
    {
        transform.SetAsLastSibling();
    }
}
