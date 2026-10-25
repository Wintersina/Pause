using System.Collections.Generic;
using UnityEngine;

// Draws a KillCamClip at any moment of its clock: a pool of plain
// SpriteRenderers takes each frame's entries (blended toward the next frame
// by track, so very slow motion stays smooth rather than stepping), and the
// scrolling quads get their recorded texture offsets -- the live scene's own
// rails (taken back after with Hide), or an archived clip's rebuilt quads.
// Nothing is simulated: the stage only shows what was recorded.
//
// Used by the live kill cam (KillCam, over the frozen board, Offset zero) and
// by the codex's replay of the saved high-score death (CodexReplayViewer, off
// to the side of the menu scene). The pool only grows (to the busiest frame);
// a draw allocates nothing.
public sealed class KillCamStage
{
    readonly Transform root;
    readonly List<SpriteRenderer> pool = new List<SpriteRenderer>(256);
    readonly List<Transform> poolTf = new List<Transform>(256);
    readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    readonly List<Renderer> driven = new List<Renderer>(8);
    readonly List<Vector2> drivenOriginal = new List<Vector2>(8);
    MeshRenderer[] quads;
    Material defaultMaterial;
    int shown;

    // Added to every recorded position (the codex draws the clip away from its scene).
    public Vector3 Offset;

    public Transform Root { get { return root; } }
    public int Shown { get { return shown; } }
    public int PoolSize { get { return pool.Count; } }
    public SpriteRenderer SpriteAt(int i) { return pool[i]; }

    public KillCamStage(Transform parent, string name)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        root = go.transform;
    }

    void EnsurePool(int n)
    {
        while (pool.Count < n)
        {
            var go = new GameObject("~KillCamSprite" + pool.Count, typeof(SpriteRenderer));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            var sr = go.GetComponent<SpriteRenderer>();
            if (defaultMaterial == null) defaultMaterial = sr.sharedMaterial;
            sr.enabled = false;
            pool.Add(sr);
            poolTf.Add(go.transform);
        }
    }

    // Shows clip time `t` (clip clock, game seconds).
    public void Draw(KillCamClip clip, float t)
    {
        if (clip == null || clip.frameCount <= 0) { Hide(); return; }
        float a;
        int f = clip.FrameAt(t, out a);
        int g = Mathf.Min(f + 1, clip.frameCount - 1);
        if (g == f) a = 0f;

        int n = clip.count[f];
        EnsurePool(n);
        int cursor = 0;
        for (int i = 0; i < n; i++)
        {
            var e = clip.Entry(f, i);
            var sr = pool[i];
            if (e.sprite == null) { sr.enabled = false; continue; }
            Vector3 p = e.pos;
            float m00 = e.m00, m01 = e.m01, m10 = e.m10, m11 = e.m11;
            Color c = e.color;
            if (a > 0f)
            {
                int j = clip.Match(g, e.track, ref cursor);
                if (j >= 0)
                {
                    var b = clip.Entry(g, j);
                    p = Vector3.LerpUnclamped(p, b.pos, a);
                    m00 = Mathf.LerpUnclamped(m00, b.m00, a);
                    m01 = Mathf.LerpUnclamped(m01, b.m01, a);
                    m10 = Mathf.LerpUnclamped(m10, b.m10, a);
                    m11 = Mathf.LerpUnclamped(m11, b.m11, a);
                    c = Color.LerpUnclamped(c, b.color, a);
                }
            }
            sr.sprite = e.sprite;
            sr.sharedMaterial = e.material != null ? e.material : defaultMaterial;
            if (e.source != null && e.source.HasPropertyBlock())
            {
                e.source.GetPropertyBlock(block);
                sr.SetPropertyBlock(block);
            }
            else if (sr.HasPropertyBlock()) sr.SetPropertyBlock(null);
            sr.color = c;
            sr.flipX = (e.flags & KillCamEntry.FlipX) != 0;
            sr.flipY = (e.flags & KillCamEntry.FlipY) != 0;
            var mode = e.DrawMode;
            if (sr.drawMode != mode) sr.drawMode = mode;
            if (mode != SpriteDrawMode.Simple) sr.size = e.size;
            sr.sortingLayerID = e.layer;
            sr.sortingOrder = e.order;

            float sx = Mathf.Sqrt(m00 * m00 + m10 * m10);
            float angle = Mathf.Atan2(m10, m00) * Mathf.Rad2Deg;
            float det = m00 * m11 - m01 * m10;
            float sy = sx > 1e-6f ? det / sx : Mathf.Sqrt(m01 * m01 + m11 * m11);
            var tf = poolTf[i];
            tf.SetPositionAndRotation(p + Offset, Quaternion.Euler(0f, 0f, angle));
            tf.localScale = new Vector3(sx, sy, 1f);
            sr.enabled = true;
        }
        for (int i = n; i < shown; i++) pool[i].enabled = false;
        shown = n;

        DrawMeshes(clip, f, g, a);
    }

    void DrawMeshes(KillCamClip clip, int f, int g, float a)
    {
        int n = clip.meshCount[f];
        for (int i = 0; i < n; i++)
        {
            var e = clip.MeshEntry(f, i);
            Vector2 off = e.offset;
            if (a > 0f)
            {
                int j = clip.MatchMesh(g, e.track);
                if (j >= 0) off = LerpWrapped(off, clip.MeshEntry(g, j).offset, a);
            }
            if (clip.meshTracks != null)
            {
                var quad = Quad(clip, e.track);
                if (quad != null) quad.sharedMaterial.mainTextureOffset = off;
            }
            else if (e.renderer != null)
            {
                var mat = e.renderer.sharedMaterial;
                if (mat == null) continue;
                if (!driven.Contains(e.renderer))
                {
                    driven.Add(e.renderer);
                    drivenOriginal.Add(mat.mainTextureOffset);
                }
                mat.mainTextureOffset = off;
            }
        }
    }

    // A texture offset blended the short way round its wrap (a rail's v runs 0..1 and wraps).
    public static Vector2 LerpWrapped(Vector2 from, Vector2 to, float a)
    {
        float dx = to.x - from.x, dy = to.y - from.y;
        if (dx > .5f) dx -= 1f; else if (dx < -.5f) dx += 1f;
        if (dy > .5f) dy -= 1f; else if (dy < -.5f) dy += 1f;
        return new Vector2(from.x + dx * a, from.y + dy * a);
    }

    // An archived clip's quad for mesh track `index` (built on first use).
    MeshRenderer Quad(KillCamClip clip, int index)
    {
        if (index < 0 || index >= clip.meshTracks.Length) return null;
        if (quads == null || quads.Length != clip.meshTracks.Length) { DestroyQuads(); quads = new MeshRenderer[clip.meshTracks.Length]; }
        if (quads[index] != null) { quads[index].enabled = true; return quads[index]; }
        var track = clip.meshTracks[index];
        if (track == null || track.material == null) return null;
        var go = new GameObject("~KillCamQuad" + index, typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = root.gameObject.layer;
        go.transform.SetParent(root, false);
        go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = track.material;
        mr.sortingLayerID = track.layer;
        mr.sortingOrder = track.order;
        go.transform.position = track.center + Offset;
        go.transform.localScale = new Vector3(track.size.x, track.size.y, 1f);
        quads[index] = mr;
        return mr;
    }

    // Everything the stage drew goes; the live rails get their own offsets back.
    public void Hide()
    {
        for (int i = 0; i < shown; i++) if (pool[i] != null) pool[i].enabled = false;
        shown = 0;
        for (int i = 0; i < driven.Count; i++)
        {
            var r = driven[i];
            if (r != null && r.sharedMaterial != null) r.sharedMaterial.mainTextureOffset = drivenOriginal[i];
        }
        driven.Clear();
        drivenOriginal.Clear();
        if (quads != null) foreach (var q in quads) if (q != null) q.enabled = false;
    }

    void DestroyQuads()
    {
        if (quads == null) return;
        foreach (var q in quads) if (q != null) Kill(q.gameObject);
        quads = null;
    }

    public void Destroy()
    {
        Hide();
        DestroyQuads();
        pool.Clear();
        poolTf.Clear();
        if (root != null) Kill(root.gameObject);
    }

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
