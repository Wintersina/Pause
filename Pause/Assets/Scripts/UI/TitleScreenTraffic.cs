using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Idle traffic on the main menu.
//
// The title screen used to drift a single ship left-to-right on a loop. This
// flies the whole roster across the screen on their own headings, and when two
// of them cross paths they collide, explode and are gone -- so the menu is
// never quite the same twice.
//
// Purely cosmetic: it runs only in startS4 and touches no game state.
public class TitleScreenTraffic : MonoBehaviour
{
    [Tooltip("How many ships are in the air at once.")]
    public int trafficCount = 6;

    [Tooltip("World units per second.")]
    public Vector2 speedRange = new Vector2(0.5f, 1.5f);

    [Tooltip("How close two hulls get before they crash.")]
    public float crashDistance = 0.55f;

    [Tooltip("Pause before a destroyed ship is replaced.")]
    public Vector2 respawnDelay = new Vector2(0.6f, 2.2f);

    class Flyer
    {
        public GameObject go;
        public Vector3 velocity;
        public int hullIndex = -1;
        public float respawnAt;
    }

    readonly List<Flyer> flyers = new List<Flyer>();
    Sprite[] hulls;
    GameObject explosion;

    // Bounds a little outside the visible area so ships enter and leave cleanly.
    const float MinX = -3.4f, MaxX = 3.4f, MinY = -5.2f, MaxY = 5.2f;

    void Start()
    {
        hulls = LoadHulls();
        explosion = Resources.Load<GameObject>("Prefabs/explosion_0");

        for (int i = 0; i < trafficCount; i++)
        {
            var f = new Flyer();
            flyers.Add(f);
            Launch(f, insideView: true);
        }
    }

    static Sprite[] LoadHulls()
    {
        var found = new List<Sprite>();
        for (int i = 1; i < shopingShips.Roster.Length; i++)
        {
            var sprite = shopingShips.SpriteFor(i, 0);
            if (sprite != null) found.Add(sprite);
        }
        return found.ToArray();
    }

    void Launch(Flyer f, bool insideView = false)
    {
        if (hulls == null || hulls.Length == 0) return;

        if (f.go == null)
        {
            f.go = new GameObject("~TitleShip");
            f.go.AddComponent<SpriteRenderer>();

            // menu ships burn steadily; they are not subject to the pause
            var thruster = f.go.AddComponent<ShipThruster>();
            thruster.respondToPause = false;
            thruster.idleScale = 0.30f;
        }
        f.go.SetActive(true);

        f.hullIndex = PickUnusedHullIndex(f);
        var sr = f.go.GetComponent<SpriteRenderer>();
        sr.sprite = hulls[f.hullIndex];
        sr.sortingOrder = 2;
        sr.color = new Color(1f, 1f, 1f, 0.85f);

        // normalise wildly different source resolutions to a similar size
        float h = sr.sprite.bounds.size.y;
        float k = h > 0.001f ? Random.Range(0.5f, 0.8f) / h : 1f;
        f.go.transform.localScale = new Vector3(k, k, 1f);

        // Head off in an arbitrary direction rather than all marching in step.
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float speed = Random.Range(speedRange.x, speedRange.y);
        f.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;

        f.go.transform.position = insideView
            ? new Vector3(Random.Range(MinX, MaxX), Random.Range(MinY, MaxY), 0f)
            : EdgeOppositeTo(f.velocity);

        // A random heading at spawn, then held fixed -- no tumbling en
        // route. Independent of f.velocity's direction on purpose: it reads
        // as traffic on varied approach vectors, not every hull nose-first
        // along its own flight path.
        f.go.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
        f.respawnAt = 0f;
    }

    // No two ships in the air at once share a hull. Excludes only other
    // *active* flyers, not this one, so a respawn is free to reuse the hull
    // it just had rather than being forced off it for no reason.
    int PickUnusedHullIndex(Flyer self)
    {
        var taken = new HashSet<int>();
        foreach (var other in flyers)
            if (other != self && other.go != null && other.go.activeSelf)
                taken.Add(other.hullIndex);

        if (taken.Count >= hulls.Length) return Random.Range(0, hulls.Length); // every hull already in use

        int index;
        do { index = Random.Range(0, hulls.Length); } while (taken.Contains(index));
        return index;
    }

    // Enter from whichever edge the heading is coming from.
    static Vector3 EdgeOppositeTo(Vector3 velocity)
    {
        return velocity.x >= 0
            ? new Vector3(MinX, Random.Range(MinY, MaxY), 0f)
            : new Vector3(MaxX, Random.Range(MinY, MaxY), 0f);
    }

    void Update()
    {
        float now = Time.unscaledTime;

        foreach (var f in flyers)
        {
            if (f.go == null) continue;

            if (!f.go.activeSelf)
            {
                if (now >= f.respawnAt) Launch(f);
                continue;
            }

            f.go.transform.position += f.velocity * Time.unscaledDeltaTime;

            var p = f.go.transform.position;
            if (p.x < MinX - 0.6f || p.x > MaxX + 0.6f || p.y < MinY - 0.6f || p.y > MaxY + 0.6f)
                Launch(f);
        }

        DetectCrashes(now);
    }

    void DetectCrashes(float now)
    {
        for (int i = 0; i < flyers.Count; i++)
        {
            var a = flyers[i];
            if (a.go == null || !a.go.activeSelf) continue;

            for (int j = i + 1; j < flyers.Count; j++)
            {
                var b = flyers[j];
                if (b.go == null || !b.go.activeSelf) continue;

                if (Vector3.Distance(a.go.transform.position, b.go.transform.position) > crashDistance)
                    continue;

                Vector3 impact = (a.go.transform.position + b.go.transform.position) * 0.5f;
                Boom(impact);
                Retire(a, now);
                Retire(b, now);
                break;
            }
        }
    }

    void Boom(Vector3 at)
    {
        if (explosion == null) return;
        var fx = Instantiate(explosion, at, Quaternion.identity);
        Destroy(fx, 2f);
    }

    void Retire(Flyer f, float now)
    {
        f.go.SetActive(false);
        f.respawnAt = now + Random.Range(respawnDelay.x, respawnDelay.y);
    }
}

public static class TitleScreenTrafficBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "startS4") return;
        if (Object.FindFirstObjectByType<TitleScreenTraffic>() != null) return;
        new GameObject("~TitleScreenTraffic").AddComponent<TitleScreenTraffic>();
    }
}
