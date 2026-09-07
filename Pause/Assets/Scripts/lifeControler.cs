using UnityEngine;
using System.Collections;

public class lifeControler : MonoBehaviour {


    private SpriteRenderer spriteControl;
    public Sprite []img = new Sprite[3];
    private string extention;
    private string []shipNames= new string[shopingShips.shipTotal];

    // Use this for initialization
    void Start() {

        // Kept in sync with shopingShips. The array is sized from
        // shopingShips.shipTotal, so it grows with the roster.
        shipNames[0] = "non";
        shipNames[1] = "Proteus";
        shipNames[2] = "Amadeus";
        shipNames[3] = "Darkwing";
        shipNames[4] = "Cygnus";
        shipNames[5] = "Vesper";
        shipNames[6] = "XR7";

        spriteControl = this.gameObject.GetComponent<SpriteRenderer>();
        if (this.gameObject.name.Contains("(Clone)"))
        {
            extention = this.gameObject.name;
            extention = extention.Replace("(Clone)", "");
        }
        else
            extention = this.gameObject.name;

        extention = extention.Replace("ship", "");
   
        int shipIndex;
        if (!int.TryParse(extention, out shipIndex) ||
            shipIndex < 0 || shipIndex >= shipNames.Length)
            shipIndex = 0;

        img = Resources.LoadAll<Sprite>("prefabs/Ships/Sprites/" + shipNames[shipIndex]);
        applyDamageSprite();
    }
	
	// Update is called once per frame
	void Update () {

        applyDamageSprite();

	}

    // lifeCounter can exceed the number of damage frames a ship has; clamp
    // instead of indexing past the end.
    void applyDamageSprite()
    {
        if (img == null || img.Length == 0) return;
        int frame = Mathf.Clamp(collisionDetection.lifeCounter, 0, img.Length - 1);
        spriteControl.sprite = img[frame];
    }
}
