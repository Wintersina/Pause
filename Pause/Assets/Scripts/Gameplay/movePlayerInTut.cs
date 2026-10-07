using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class movePlayerInTut : MonoBehaviour
{

    Vector3 fingerPos;
    Vector3 textPos;

    private RectTransform boostText;
    private RectTransform hypeText;


    void Start()
    {
        //will move pos of texts to follow ship
        boostText = GameObject.Find("boostText").GetComponent<RectTransform>();
        hypeText = GameObject.Find("hypeText").GetComponent<RectTransform>();

    }

    // Update is called once per frame
    void Update()
    {
            if (TouchInput.IsPressed)
            {
                fingerPos = Camera.main.ScreenToWorldPoint(new Vector3(TouchInput.Position.x, TouchInput.Position.y, 0));
                textPos = Camera.main.WorldToScreenPoint(transform.position);
            }
            if (TouchInput.IsPressed)
                moveLeft_Right(fingerPos);
        }

    

    // will move the player left and right baised on touch positions.
    void moveLeft_Right(Vector3 fingerPos)
    {
        //gets a position of the finger on the screen
        //checks position of finger is in bound box
        // (vertically: the same reach as the run, ShipReach, in the view this
        // device shows; it was the constant -4.15 .. 4.5)
        // (sideways: ShipReach's reach too, which follows the rails)
        float reach = ShipReach.HalfWidth;
        if (fingerPos.x <= reach && fingerPos.x > -reach)
        {
            this.transform.position = new Vector3(fingerPos.x,
                ShipReach.ClampY(fingerPos.y + 1.5f));

            // Allow text to follow player----------------------------

            hypeText.gameObject.SetActive(true);
            boostText.gameObject.SetActive(true);
            hypeText.transform.position = textPos + new Vector3(0, 70, 0);
            boostText.transform.position = textPos + new Vector3(0, -40, 0);

            //--------------------------------------------------------
        }
        else
        {
            this.transform.position = new Vector3(ShipReach.ClampX(fingerPos.x),
                ShipReach.ClampY(fingerPos.y + 1.5f));
        }

    }

}
