using UnityEngine;
using UnityEngine.SceneManagement;

// Releases the green healing atom -- rarely, and only a couple of times per
// planet, so repairing is a lucky break rather than something to farm.
public class HealAtomSpawner : MonoBehaviour
{
    [Tooltip("Most heal atoms a single planet will ever release.")]
    public int maxPerWorld = 2;

    [Tooltip("Earliest it can appear, in seconds of active flight.")]
    public float firstChanceAfter = 60f;

    [Tooltip("Random gap between release attempts.")]
    public Vector2 gapSeconds = new Vector2(70f, 130f);

    [Tooltip("Chance an attempt actually spawns one. Keeps it from becoming " +
             "predictable even once the timer is up.")]
    [Range(0f, 1f)] public float chance = 0.6f;

    int spawnedThisWorld;
    int lastWorld = -1;
    float timer;

    void Start()
    {
        timer = firstChanceAfter;
        lastWorld = CurrentWorld();
    }

    static int CurrentWorld()
    {
        return WorldManager.Instance != null ? WorldManager.CurrentIndex : 0;
    }

    void Update()
    {
        // A new planet gets a fresh allowance.
        int world = CurrentWorld();
        if (world != lastWorld)
        {
            lastWorld = world;
            spawnedThisWorld = 0;
            timer = firstChanceAfter;
        }

        if (spawnedThisWorld >= maxPerWorld) return;
        if (buttonClicks.playerDied) return;

        // Count only while the world is actually moving, like every other timer.
        bool running = TouchInput.IsPressed || score.pauseCounter <= 0;
        if (!running) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = Random.Range(gapSeconds.x, gapSeconds.y);

        // Damaged players need it; an undamaged one does not.
        if (collisionDetection.lifeCounter <= 0) return;
        if (Random.value > chance) return;

        HealAtom.Spawn(new Vector3(Random.Range(-2.2f, 2.2f), 7f, 0f));
        spawnedThisWorld++;
    }
}

public static class HealAtomBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (Object.FindFirstObjectByType<HealAtomSpawner>() != null) return;
        new GameObject("~HealAtomSpawner").AddComponent<HealAtomSpawner>();
    }
}
