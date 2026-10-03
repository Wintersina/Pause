using UnityEngine;

// Steps a sprite through a frame table on *scaled* time: when the world
// freezes (timeScale 0, finger lifted) the pickups freeze with it.
public class PickupFlipbook : MonoBehaviour
{
    public PickupKind kind;

    SpriteRenderer target;
    Sprite[] frames;
    int[] ticks;
    int frame;
    float clock;

    public int Frame { get { return frame; } }
    public int FrameCount { get { return frames != null ? frames.Length : 0; } }
    public SpriteRenderer Target { get { return target; } }

    public static PickupFlipbook AddTo(GameObject go, PickupKind kind)
    {
        var book = go.GetComponent<PickupFlipbook>();
        if (book == null)
        {
            // AddComponent runs Awake at once, before `kind` can be set.
            adding = true;
            try { book = go.AddComponent<PickupFlipbook>(); }
            finally { adding = false; }
            book.kind = kind;
            book.Init();
        }
        return book;
    }

    static bool adding;

    void Awake() { if (!adding) Init(); }

    void Init()
    {
        if (frames != null) return;
        ticks = PickupArt.IdleTicks(kind);
        frames = PickupArt.Frames(PickupArt.IdleName(kind), ticks.Length);
        var own = GetComponent<SpriteRenderer>();
        if (kind == PickupKind.Heal)
        {
            // The green atom keeps its original sprite untouched; its
            // animation is a flipbook of light overlays drawn on top of it.
            var child = transform.Find("HealGlint");
            if (child == null)
            {
                child = new GameObject("HealGlint").transform;
                child.SetParent(transform, false);
            }
            target = child.GetComponent<SpriteRenderer>();
            if (target == null) target = child.gameObject.AddComponent<SpriteRenderer>();
            if (own != null) target.sortingOrder = own.sortingOrder + 1;
        }
        else
        {
            target = own;
        }
        Show(0);
    }

    void Update()
    {
        Advance(Time.unscaledDeltaTime * Time.timeScale);
    }

    // Exposed for tests: advance the clock by dt seconds of game time.
    public void Advance(float dt)
    {
        if (frames == null) Init();   // e.g. instantiated in edit mode, where Awake doesn't run
        if (frames == null || frames.Length == 0 || dt <= 0f) return;
        clock += dt;
        int guard = 0;
        while (clock >= ticks[frame] * PickupArt.Tick && guard++ < 64)
        {
            clock -= ticks[frame] * PickupArt.Tick;
            Show((frame + 1) % frames.Length);
        }
    }

    void Show(int index)
    {
        frame = index;
        if (target != null && frames != null && frames[index] != null) target.sprite = frames[index];
    }
}
