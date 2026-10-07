using System.Collections.Generic;
using UnityEngine;

// A detached, three-drawing death pose for Space enemies. The original
// seven-frame idle/tell/hit strip remains unchanged while this finishes.
public class EnemyDeathFlipbook : MonoBehaviour
{
    const float FlashSeconds = .08f;
    const float RuptureSeconds = .11f;
    const float SmokeSeconds = .2f;

    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
    Sprite[] frames;
    SpriteRenderer renderer;
    float age;

    public static Sprite[] Frames(EnemyDef def)
    {
        if (def == null || def.world != 0) return null;
        Sprite[] result;
        if (cache.TryGetValue(def.key, out result) && result != null && result[0] != null) return result;
        var texture = Resources.Load<Texture2D>("Enemies/Death/" + def.key);
        if (texture == null || texture.width != texture.height * 3) return null;
        float side = texture.height;
        float ppu = side / Mathf.Max(.01f, def.FrameWorldSize);
        result = new Sprite[3];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Sprite.Create(texture, new Rect(i * side, 0f, side, side), new Vector2(.5f, .5f), ppu);
            result[i].name = def.key + "_death_" + i;
        }
        cache[def.key] = result;
        return result;
    }

    public static void Spawn(GameObject target)
    {
        if (!Application.isPlaying || target == null) return;
        var def = EnemyIdentity.Of(target);
        var frames = Frames(def);
        if (frames == null) return;
        var source = target.GetComponent<SpriteRenderer>();
        var go = new GameObject(def.key + " death");
        go.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
        go.transform.localScale = target.transform.lossyScale;
        var effect = go.AddComponent<EnemyDeathFlipbook>();
        effect.frames = frames;
        effect.renderer = go.AddComponent<SpriteRenderer>();
        effect.renderer.sprite = frames[0];
        effect.renderer.sortingLayerID = source.sortingLayerID;
        effect.renderer.sortingOrder = Mathf.Max(source.sortingOrder, 67);
    }

    void Update()
    {
        if (frames == null || renderer == null) return;
        age += Time.deltaTime;
        if (age < FlashSeconds) renderer.sprite = frames[0];
        else if (age < FlashSeconds + RuptureSeconds) renderer.sprite = frames[1];
        else if (age < FlashSeconds + RuptureSeconds + SmokeSeconds) renderer.sprite = frames[2];
        else Destroy(gameObject);
    }
}
