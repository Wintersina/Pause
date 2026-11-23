using UnityEngine;

// A thin straight line between two world points, for player FX that must visibly connect to what they affect
// (the Warden's aim lines, the UFO's tractor core). One SpriteRenderer on its own root (never a child of an
// enemy, so it can never read as an outline), a crisp 4 px white cell stretched and rotated: no glow, no bloom.
// Plain object, owned and Hide()/Destroy()ed by whoever made it; nothing is allocated per frame.
public sealed class AttackLink
{
    const int Cell = 4;
    static Sprite cell;

    GameObject root;
    SpriteRenderer sr;
    bool broken;
    readonly int order;
    Color tint;

    public bool Visible { get; private set; }
    public Vector3 From { get; private set; }
    public Vector3 To { get; private set; }
    public float Thickness { get; private set; }
    public float Alpha { get; private set; }
    public bool Alive { get { return root != null; } }
    public Transform Root { get { return root != null ? root.transform : null; } }

    public AttackLink(Color tint, int order)
    {
        this.tint = tint;
        this.order = order;
    }

    static Sprite Cellsprite()
    {
        if (cell != null) return cell;
        var tex = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[Cell * Cell];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply(false, true);
        cell = Sprite.Create(tex, new Rect(0, 0, Cell, Cell), new Vector2(.5f, .5f), Cell, 0, SpriteMeshType.FullRect);   // one world unit across
        return cell;
    }

    void Build()
    {
        root = new GameObject("~attackLink", typeof(SpriteRenderer));
        sr = root.GetComponent<SpriteRenderer>();
        sr.sprite = Cellsprite();
        sr.sortingOrder = order;
        root.SetActive(false);
    }

    public void SetTint(Color c) { tint = c; }

    // Draws the line from a to b, `thickness` units wide at `alpha` opacity.
    public void Set(Vector3 a, Vector3 b, float thickness, float alpha)
    {
        if (broken) return;
        if (root == null)
        {
            // One attempt: a Build that throws half way would otherwise leave an object behind on every frame's call.
            try { Build(); }
            catch (System.Exception e)
            {
                broken = true;
                Debug.LogError("AttackLink: could not build the line, it stays off: " + e);
                Destroy();
                return;
            }
        }
        Vector3 d = b - a;
        float len = Mathf.Max(.01f, ((Vector2)d).magnitude);
        a.z = b.z = 0f;
        From = a; To = b; Thickness = thickness; Alpha = alpha;
        var t = root.transform;
        t.position = (a + b) * .5f;
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);   // the cell's long axis is local y
        t.localScale = new Vector3(thickness, len, 1f);
        sr.color = new Color(tint.r, tint.g, tint.b, alpha);
        if (!Visible) { root.SetActive(true); Visible = true; }
    }

    public void Hide()
    {
        Visible = false;
        if (root != null && root.activeSelf) root.SetActive(false);
    }

    public void Destroy()
    {
        Visible = false;
        if (root == null) return;
        if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root);
        root = null;
        sr = null;
    }
}
