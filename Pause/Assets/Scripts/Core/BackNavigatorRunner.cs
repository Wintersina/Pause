using UnityEngine;

// The only reader of KeyCode.Escape in the game.
public class BackNavigatorRunner : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) BackNavigator.HandleEscape(Time.frameCount);
    }
}
