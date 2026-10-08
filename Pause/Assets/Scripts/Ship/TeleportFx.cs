using System.Collections;
using UnityEngine;

// The pause-teleport: lift your finger, touch somewhere else, and the ship
// blinks across the screen.
//
// Until now that was a silent position snap. This gives it a departure and
// arrival flash, and lets the ship clear whatever it materialises on top of --
// so teleporting into a cluster is a real offensive option rather than a way to
// die instantly.
public class TeleportFx : MonoBehaviour
{
    [Tooltip("Anything tagged Enimey or Astr this close to the landing point is " +
             "destroyed on arrival.")]
    public static float BlastRadius = 0.95f;

    [Tooltip("Ignore tiny nudges -- only a real jump counts as a teleport.")]
    public static float MinimumJump = 0.85f;

    static TeleportFx runner;
    static AudioClip warpSound;

    public static void Play(Vector3 from, Vector3 to)
    {
        // a nudge is not a jump -- but a pause jump aimed at an elite always
        // connects, however short (EliteShip.TeleportStrike)
        if (Vector3.Distance(from, to) < MinimumJump) { StrikeElites(to); return; }

        Ensure();
        PlaySound();
        runner.StartCoroutine(Flash(from, 0.9f, 1.6f, new Color(0.55f, 0.85f, 1f, 0.85f)));
        runner.StartCoroutine(Flash(to, 1.7f, 0.5f, new Color(1f, 0.95f, 0.7f, 0.95f)));
        Strike(to);
    }

    // Feedback for a teleport refused on cooldown -- a dull, collapsing ring
    // rather than the bright flare of a successful jump, so the two never read
    // as the same thing.
    public static void Denied(Vector3 at)
    {
        Ensure();
        runner.StartCoroutine(Flash(at, 1.1f, 0.6f, new Color(0.75f, 0.78f, 0.85f, 0.45f)));
    }

    static void Ensure()
    {
        if (runner == null) runner = new GameObject("~TeleportFx").AddComponent<TeleportFx>();
    }

    // A compact 80s-style rising warp followed by a bright arrival chime.
    // The clip lives in Resources so this effect remains self-contained like
    // the portal visuals and needs no AudioSource wired in a scene.
    static void PlaySound()
    {
        if (warpSound == null) warpSound = Resources.Load<AudioClip>("Audio/teleport_warp");
        if (warpSound == null || runner == null) return;

        var source = runner.GetComponent<AudioSource>();
        if (source == null) source = runner.gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.PlayOneShot(warpSound, 0.78f);
    }

    // Teleport kills (tests, diagnostics).
    public static int Kills;

    // Fixed buffer for the landing-zone query: nothing allocates per blink.
    static readonly Collider2D[] landedOn = new Collider2D[32];
    static ContactFilter2D everything = NoFilter();

    static ContactFilter2D NoFilter()
    {
        var f = new ContactFilter2D();
        f.NoFilter();
        return f;
    }

    // Erases every hazard the ship materialised on top of. Each is a real
    // kill, paid like any other (collisionDetection.AwardDestroyedTarget:
    // kill points + ScoreRules.TeleportKillBonus with the chain and speed
    // multipliers, the "+N" popup, secret meter, codex, achievements, dust),
    // but blown apart with the teleport's own "erased by the pause" blast
    // (TeleportKillFx) instead of the weapons' cel explosion.
    //
    // The boss is not a hazard here: its body hitbox (an IShipAttackTarget)
    // is left alone -- a blink never costs it a hit point, as before, and no
    // longer knocks out its hitbox for the respawn delay either. Its shots
    // and lane beams are plain hazards and are erased (boss shots pay
    // ScoreRules.BossShot, beams nothing, exactly as a shielded ram does).
    public static int Strike(Vector3 at)
    {
        Physics2D.SyncTransforms();
        int n = Physics2D.OverlapCircle(at, BlastRadius, everything, landedOn);
        int kills = 0;
        for (int i = 0; i < n; i++)
        {
            var col = landedOn[i];
            if (col == null) continue;
            var go = col.gameObject;
            if (!ClearTarget.IsHazard(go)) continue;
            if (EliteShip.TeleportStrike(go, at)) continue;   // an elite loses a heart; its shot is erased
            if (go.GetComponent<IShipAttackTarget>() != null) continue;   // the boss body
            if (ShipAttackHits.AlreadyHit(go) || SeenBefore(go, i)) continue;
            Erase(go);
            kills++;
        }
        System.Array.Clear(landedOn, 0, n);
        return kills;
    }

    // Only the elites in the landing zone (a jump too short to flash).
    public static int StrikeElites(Vector3 at)
    {
        Physics2D.SyncTransforms();
        int n = Physics2D.OverlapCircle(at, BlastRadius, everything, landedOn);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var col = landedOn[i];
            if (col == null || SeenBefore(col.gameObject, i)) continue;
            var elite = col.GetComponent<EliteShip>();
            if (elite == null || !elite.InPlay) continue;
            if (EliteShip.TeleportStrike(col.gameObject, at)) hits++;
        }
        System.Array.Clear(landedOn, 0, n);
        return hits;
    }

    // One hazard with two colliders is still one kill (Destroy is deferred).
    static bool SeenBefore(GameObject go, int index)
    {
        for (int j = 0; j < index; j++)
            if (landedOn[j] != null && landedOn[j].gameObject == go) return true;
        return false;
    }

    static void Erase(GameObject go)
    {
        Kills++;
        RailBombAnimator.Burst(go);   // a rail mine flashes its burst frame first (no-op otherwise)
        TeleportKillFx.Spawn(go);
        collisionDetection.PlayExplosion();
        collisionDetection.AwardDestroyedTarget(go, ScoreRules.TeleportKillBonus);
        ClearTarget.Release(go);
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    // Unscaled time: a teleport begins while the world is still frozen.
    static IEnumerator Flash(Vector3 at, float startScale, float endScale, Color tint)
    {
        var go = new GameObject("~TeleportVortex");
        go.transform.position = at;

        var outer = MakeLayer(go.transform, "Vortex", tint, 60, 0);
        var inner = MakeLayer(go.transform, "Core", new Color(0.65f, 0.92f, 1f, tint.a * 0.72f), 61, 5);
        var sparks = MakeLayer(go.transform, "Sparks", new Color(1f, 0.46f, 1f, tint.a * 0.38f), 62, 10);
        inner.transform.localScale = Vector3.one * 0.72f;
        sparks.transform.localScale = Vector3.one * 1.15f;

        const float life = 0.32f;
        for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
        {
            float k = t / life;
            go.transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, k);
            int frame = Mathf.FloorToInt(t * 35f);
            outer.sprite = TeleportPortalSprites.FrameAt(frame);
            inner.sprite = TeleportPortalSprites.FrameAt(frame + 5);
            sparks.sprite = TeleportPortalSprites.FrameAt(frame + 10);
            outer.transform.localRotation = Quaternion.Euler(0, 0, frame * 16f);
            inner.transform.localRotation = Quaternion.Euler(0, 0, -frame * 10f);
            sparks.transform.localRotation = Quaternion.Euler(0, 0, frame * 24f);
            Fade(outer, tint.a * (1f - k));
            Fade(inner, tint.a * 0.72f * (1f - k));
            Fade(sparks, tint.a * 0.38f * (1f - k));
            yield return null;
        }
        Destroy(go);
    }

    static SpriteRenderer MakeLayer(Transform parent, string name, Color tint, int order, int frame)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TeleportPortalSprites.FrameAt(frame);
        sr.color = tint;
        sr.sortingOrder = order;
        return sr;
    }

    static void Fade(SpriteRenderer renderer, float alpha)
    {
        var color = renderer.color;
        color.a = alpha;
        renderer.color = color;
    }
}
