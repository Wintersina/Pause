using UnityEngine;
using UnityEngine.SceneManagement;

// A small exhaust flame that burns the whole time a ship is flying.
//
// The ships already carry a big animated boost flame, but it only appears while
// a blue atom is active -- the rest of the time they hang in space looking
// inert. This adds a permanent, much smaller version of that same flame so a
// ship reads as under power.
//
// It is also a second channel for the game's core idea: lift your finger and
// the flame dies back to a pilot light, put it down and it flares again.
public class ShipThruster : MonoBehaviour
{
    [Tooltip("Idle flame size as a fraction of the ship's own boost flame.")]
    public float idleScale = 0.38f;

    [Tooltip("Extra size at full speed, on top of the idle size.")]
    public float speedBoost = 0.30f;

    [Tooltip("Size while paused -- a pilot light rather than a thrust plume.")]
    public float pausedScale = 0.12f;

    [Tooltip("Flicker depth. Zero is a perfectly steady flame.")]
    public float flicker = 0.10f;

    [Tooltip("Set false on ships that are not the player, e.g. menu traffic, " +
             "so the flame ignores pause state and just burns.")]
    public bool respondToPause = true;

    Transform flame;
    SpriteRenderer flameRenderer;
    GameObject boostObject;      // the big flame, so we never draw both
    Vector3 baseScale = Vector3.one;
    float seed;

    void Start()
    {
        seed = Random.value * 10f;
        Build();
    }

    void Build()
    {
        var hull = GetComponent<SpriteRenderer>();

        int index = ShipExhaust.IndexFor(gameObject);
        boostObject = ShipExhaust.ConfigureBoost(gameObject, index);
        Sprite sprite = ShipExhaust.SpriteFor(index);
        if (sprite == null) return;
        var go = new GameObject("~Thruster");
        go.transform.SetParent(transform, false);
        flameRenderer = go.AddComponent<SpriteRenderer>();
        flameRenderer.sprite = sprite;
        flameRenderer.color = ShipExhaust.TintFor(index);
        flameRenderer.sortingOrder = (hull != null ? hull.sortingOrder : 0) - 1;
        go.transform.localPosition = ShipExhaust.MountFor(hull != null ? hull.sprite : null, index);
        go.transform.localRotation = Quaternion.identity;
        baseScale = ShipExhaust.ScaleFor(hull != null ? hull.sprite : null, index);
        flame = go.transform;
    }

    void LateUpdate()
    {
        if (flame == null) return;

        // Never draw the idle flame under the real boost flame.
        bool boosting = boostObject != null && boostObject.activeInHierarchy;
        flameRenderer.enabled = !boosting;
        if (boosting) return;

        float target = idleScale;

        if (respondToPause)
        {
            bool flying = !buttonClicks.playerDied &&
                          (TouchInput.IsPressed || score.pauseCounter <= 0);

            if (!flying)
            {
                target = pausedScale;
            }
            else
            {
                float speedK = Mathf.Clamp01(moveBackGround.speed / 0.35f);
                target = idleScale + speedBoost * speedK;
            }
        }

        // Unscaled so the flame keeps guttering while the world is frozen.
        float wobble = 1f + Mathf.Sin((Time.unscaledTime + seed) * 22f) * flicker;
        Vector3 want = new Vector3(baseScale.x * Mathf.Sqrt(target) * wobble,
                                   baseScale.y * target * wobble, 1f);

        flame.localScale = Vector3.Lerp(flame.localScale, want,
                                        1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
    }
}

// Attaches a thruster to the player's ship once it exists.
//
// gameS1's ship carries movePlayer; tutorialS5's carries movePlayerInTut
// instead -- looking up only the former meant this never found the tutorial
// ship at all, so it never got the idle/boost flame the main game gets and
// was left showing whatever flame (or none) the 2016 scene had baked in.
public class ShipThrusterAttach : MonoBehaviour
{
    float giveUp = 6f;

    void Update()
    {
        GameObject player = FindPlayer();
        if (player != null)
        {
            if (player.GetComponent<ShipThruster>() == null)
                player.AddComponent<ShipThruster>();
            Destroy(gameObject);
            return;
        }

        giveUp -= Time.unscaledDeltaTime;
        if (giveUp <= 0f) Destroy(gameObject);
    }

    static GameObject FindPlayer()
    {
        var main = Object.FindFirstObjectByType<movePlayer>();
        if (main != null) return main.gameObject;
        var tut = Object.FindFirstObjectByType<movePlayerInTut>();
        return tut != null ? tut.gameObject : null;
    }
}

public static class ShipThrusterBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        new GameObject("~ShipThrusterAttach").AddComponent<ShipThrusterAttach>();
    }
}
