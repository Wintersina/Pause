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

    // True from a press until the finger lifts: the first frame of a press is
    // an arrival (a pause-jump), every later one is steering.
    bool held;

    // Update is called once per frame
    void Update()
    {
        if (TouchInput.IsPressed)
        {
            fingerPos = Camera.main.ScreenToWorldPoint(new Vector3(TouchInput.Position.x, TouchInput.Position.y, 0));
            textPos = Camera.main.WorldToScreenPoint(transform.position);

            Vector3 before = transform.position;
            moveLeft_Right(fingerPos);
            if (!held) Arrive(before, transform.position);
            held = true;
        }
        else held = false;
    }

    // The pause-jump, as in a run (movePlayer): the portal opens where the
    // ship left and where it lands, and the landing erases what it lands on
    // (the tutorial alien too). TeleportFx ignores nudges shorter than
    // TeleportFx.MinimumJump. The tutorial has no teleport cooldown.
    public static bool Arrive(Vector3 from, Vector3 to)
    {
        if (Vector3.Distance(from, to) < TeleportFx.MinimumJump) return false;
        TeleportFx.Play(from, to);
        return true;
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
