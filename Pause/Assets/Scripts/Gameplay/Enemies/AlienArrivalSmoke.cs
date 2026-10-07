using UnityEngine;

// A small puff when the Space alien first enters the camera. The drawings
// come from the same smoke atlas as ship damage, so they match the game FX.
[DisallowMultipleComponent]
public class AlienArrivalSmoke : MonoBehaviour
{
    const int PuffCount = 5;
    const float Duration = .65f;

    struct Puff
    {
        public SpriteRenderer renderer;
        public Vector3 position;
        public Vector3 velocity;
        public float delay;
    }

    readonly Puff[] puffs = new Puff[PuffCount];
    float age;
    bool started;

    void OnBecameVisible()
    {
        if (Application.isPlaying) Begin();
    }

    public void Begin()
    {
        if (started) return;
        started = true;
        var alien = GetComponent<SpriteRenderer>();
        for (int i = 0; i < PuffCount; i++)
        {
            var go = new GameObject("arrival smoke");
            go.transform.SetParent(transform, false);
            var puff = new Puff();
            puff.renderer = go.AddComponent<SpriteRenderer>();
            puff.renderer.sprite = ShipDamageFx.Frame(ShipDamageFx.RowSmoke, i % 4);
            puff.renderer.sortingLayerID = alien.sortingLayerID;
            puff.renderer.sortingOrder = alien.sortingOrder - 1;
            puff.position = transform.position + new Vector3((i - 2) * .13f, -.15f, 0f);
            puff.velocity = new Vector3((i - 2) * .24f, .28f + (i % 2) * .1f, 0f);
            puff.delay = i * .045f;
            go.transform.position = puff.position;
            go.transform.localScale = Vector3.one * (.23f + (i % 2) * .06f);
            puff.renderer.enabled = false;
            puffs[i] = puff;
        }
    }

    void Update() { Advance(Time.deltaTime); }

    public void Advance(float dt)
    {
        if (!started || dt <= 0f) return;
        age += dt;
        for (int i = 0; i < PuffCount; i++)
        {
            var puff = puffs[i];
            if (puff.renderer == null) continue;
            float t = (age - puff.delay) / Duration;
            if (t < 0f || t >= 1f)
            {
                puff.renderer.enabled = false;
                continue;
            }
            puff.renderer.enabled = true;
            puff.position += puff.velocity * dt;
            puff.renderer.transform.position = puff.position;
            puff.renderer.sprite = ShipDamageFx.Frame(ShipDamageFx.RowSmoke, Mathf.Min(3, Mathf.FloorToInt(t * 4f)));
            puff.renderer.color = new Color(.69f, .64f, .76f, .7f * (1f - t));
            puffs[i] = puff;
        }
        if (age >= Duration + .2f) enabled = false;
    }
}
