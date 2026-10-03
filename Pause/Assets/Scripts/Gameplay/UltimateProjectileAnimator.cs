using UnityEngine;

// Adds life to an authored projectile tile while PowerFx owns its homing
// position. Each ship slot gets a different pulse/spin/trail treatment.
public class UltimateProjectileAnimator : MonoBehaviour
{
    public int shipIndex;
    SpriteRenderer main;
    SpriteRenderer[] echoes;
    SpriteRenderer[] spires;
    Vector3[] history;
    float age;

    void Awake()
    {
        main = GetComponent<SpriteRenderer>();
        echoes = new SpriteRenderer[2];
        history = new Vector3[2];
        spires = new SpriteRenderer[3];
        for (int i = 0; i < echoes.Length; i++)
        {
            var go = new GameObject("Afterimage", typeof(SpriteRenderer));
            go.transform.SetParent(transform.parent, true);
            echoes[i] = go.GetComponent<SpriteRenderer>();
            echoes[i].sprite = main != null ? main.sprite : null;
            echoes[i].sortingOrder = main != null ? main.sortingOrder - 1 : 69;
        }
        for (int i = 0; i < history.Length; i++) history[i] = transform.position;
        for (int i = 0; i < spires.Length; i++)
        {
            var go = new GameObject("Spiral", typeof(SpriteRenderer));
            go.transform.SetParent(transform.parent, true);
            spires[i] = go.GetComponent<SpriteRenderer>();
            spires[i].sprite = main != null ? main.sprite : null;
            spires[i].sortingOrder = main != null ? main.sortingOrder + 1 : 71;
        }
    }

    void LateUpdate()
    {
        age += Time.unscaledDeltaTime;
        int style = Mathf.Abs(shipIndex) % 5;
        if (main != null)
        {
            // Fast four-frame sprite cycle: every frame uses a different
            // illustrated projectile silhouette from the authored sheet.
            var frame = UltimateProjectileArt.FrameForShip(shipIndex,
                Mathf.FloorToInt(age * (11f + shipIndex % 4)));
            if (frame != null) main.sprite = frame;
        }
        // Slot-specific frequency means all sixteen roster shots have their
        // own cadence even when they share a broad movement family.
        float pulse = 1f + Mathf.Sin(age * (8f + Mathf.Abs(shipIndex) * 1.37f)) *
            (.10f + style * .018f);
        transform.localScale = Vector3.one * .72f * pulse;

        // PowerFx updates the facing toward the target first. This extra
        // rotation is visual-only and gets refreshed from the real heading on
        // the next frame, leaving homing accuracy untouched.
        if (style == 1 || style == 3)
            transform.Rotate(0f, 0f, (style == 1 ? 420f : 190f + shipIndex * 11f) * Time.unscaledDeltaTime);
        else if (style == 2)
            transform.Rotate(0f, 0f, Mathf.Sin(age * 13f) * 5f);

        for (int i = history.Length - 1; i > 0; i--) history[i] = history[i - 1];
        history[0] = transform.position;
        for (int i = 0; i < echoes.Length; i++)
        {
            if (echoes[i] == null) continue;
            echoes[i].sprite = main != null ? main.sprite : null;
            echoes[i].transform.position = history[i];
            echoes[i].transform.rotation = transform.rotation;
            echoes[i].transform.localScale = transform.localScale * (1f - (i + 1) * .20f);
            var c = main != null ? main.color : Color.white;
            c.a *= .24f / (i + 1);
            echoes[i].color = c;
        }
        // Three small satellites corkscrew around the live dart. Their orbit
        // makes every projectile read as drilling through space toward its
        // selected target, even in heavy slow motion.
        for (int i = 0; i < spires.Length; i++)
        {
            var s = spires[i]; if (s == null) continue;
            float a = age * (14f + shipIndex * .8f) + i * Mathf.PI * 2f / spires.Length;
            Vector3 offset = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (.13f + i * .025f);
            s.sprite = main != null ? main.sprite : null;
            s.transform.position = transform.position + offset;
            s.transform.localScale = transform.localScale * .22f;
            var c = main != null ? main.color : Color.white; c.a *= .62f; s.color = c;
        }
    }

    void OnDestroy()
    {
        if (echoes == null) return;
        foreach (var echo in echoes)
            if (echo != null) Destroy(echo.gameObject);
        if (spires != null) foreach (var spire in spires)
            if (spire != null) Destroy(spire.gameObject);
    }
}
