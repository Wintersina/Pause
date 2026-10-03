using UnityEngine;

// Presents the four-frame mine for the current world's rail material. Mounting
// and movement remain the responsibility of RailMineMount and the existing
// straight-line scroller.
public class RailBombAnimator : MonoBehaviour
{
    SpriteRenderer renderer;
    static Sprite emberFrame1, emberFrame2;

    void Awake()
    {
        renderer = GetComponent<SpriteRenderer>();
        // The old mine Animator targets its legacy sprite frames. Disable it
        // so it cannot overwrite the themed runtime animation.
        var legacy = GetComponent<Animator>();
        if (legacy != null) legacy.enabled = false;
        ApplyFrame(0);
    }

    void Update()
    {
        ApplyFrame(Mathf.FloorToInt(Time.time * 6f));
    }

    void ApplyFrame(int frame)
    {
        if (renderer == null) return;
        int world = WorldManager.Instance != null ? WorldManager.CurrentIndex : 0;
        if (world == 3)
        {
            LoadEmberFrames();
            renderer.sprite = (frame % 2 == 0) ? emberFrame1 : emberFrame2;
        }
        else renderer.sprite = RailBombSprites.FrameForWorld(world, frame);
        renderer.sortingOrder = 12;
    }

    static void LoadEmberFrames()
    {
        if (emberFrame1 != null && emberFrame2 != null) return;
        emberFrame1 = SpriteFrom(Resources.Load<Texture2D>("Vfx/rail_mine_ember_1"));
        emberFrame2 = SpriteFrom(Resources.Load<Texture2D>("Vfx/rail_mine_ember_2"));
    }

    static Sprite SpriteFrom(Texture2D texture)
    {
        return texture == null ? null : Sprite.Create(texture,
            new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
    }
}
