using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class lifeControler : MonoBehaviour {


    private SpriteRenderer spriteControl;
    public Sprite []img = new Sprite[3];
    private int currentShipIndex;
    private string extention;
    private string []shipNames= new string[shopingShips.shipTotal];

    // The shop's own ship1-ship3 are authored with the same collisionDetection
    // component the real player ship carries (so this script's sprite-picking
    // can be reused there too), so GetComponent<collisionDetection>() can't
    // tell a dock ship apart from a live one. Scene name can.
    private bool isLiveGameplay;

    // Use this for initialization
    void Start() {

        // Single source of truth for the roster lives in shopingShips.
        for (int i = 0; i < shipNames.Length && i < shopingShips.Roster.Length; i++)
            shipNames[i] = shopingShips.Roster[i];

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

        currentShipIndex = shipIndex;
        img = shopingShips.DamageSpritesFor(shipIndex);

        // Damage art and fx only mean something for the ship actually being
        // flown in a live run. GameStateReset.Clear() already zeroes
        // collisionDetection.lifeCounter (a static, so it otherwise survives
        // scene changes untouched) on the way out of gameplay; this is the
        // second, scene-scoped guard so a dock ship reads as fully healed
        // even if that ordering were ever missed, and so it never gets the
        // fire/spark ShipDamageFx in the first place.
        string sceneName = SceneManager.GetActiveScene().name;
        isLiveGameplay = sceneName == "gameS1" || sceneName == "tutorialS5";

        if (isLiveGameplay && GetComponent<ShipDamageFx>() == null)
            gameObject.AddComponent<ShipDamageFx>();
        applyDamageSprite();

        // Ships placed by spawnShips.cs (gameS1) already get normalised to a
        // consistent on-screen size from their sprite's own bounds. A ship
        // authored directly into a scene instead -- the tutorial's ship1 is
        // the one case of this -- never went through that and just kept
        // whatever scale the 2016 prefab happened to have baked in, which is
        // why it rendered far smaller than the same ship looks in game.
        // Applying the identical formula here closes that gap for any ship
        // placed either way, and is harmless where spawnShips.cs already set
        // it, since both compute the same value from the same sprite.
        if (img != null && img.Length > 0 && img[0] != null)
        {
            float scale = shopingShips.NormalizedHullScale(img[0]);
            transform.localScale = new Vector3(scale, scale, transform.localScale.z);
        }
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
        int life = isLiveGameplay ? collisionDetection.lifeCounter : 0;
        int frame = Mathf.Clamp(life, 0, img.Length - 1);
        int idleFrame = Mathf.FloorToInt(Time.unscaledTime * 8f) % 3;
        Sprite animated = shopingShips.IdleSpriteFor(currentShipIndex, frame, idleFrame);
        spriteControl.sprite = animated != null ? animated : img[frame];

        // The newer hulls swap authored damaged frames. Legacy sheets have a
        // single intact frame, so add a warm scorch tint at the same health
        // thresholds; ShipDamageFx supplies the visible fire and sparks.
        if (currentShipIndex >= ShipLivesIndicator.FirstShipWithoutDamageArt)
        {
            float damage = Mathf.Clamp01(life / 2f);
            spriteControl.color = Color.Lerp(Color.white, new Color(1f, .48f, .34f), damage);
        }
    }
}
