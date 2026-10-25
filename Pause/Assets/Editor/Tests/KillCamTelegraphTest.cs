using System.Collections.Generic;
using UnityEngine;

// The kill cam must replay a telegraph exactly as it was drawn live. The rail
// mine's aim line / beam is a parallelogram (RailMineLaser.Span: a rotated,
// non-uniformly scaled frame over a rotated sprite), which no single transform
// can hold: the replay once dropped the shear and showed a straight bar. This
// builds each telegraph kind the project draws (the angled rail laser's tiled
// aim line and its stretched beam, the boss / pattern marker bar: a tiled
// sprite rotated and widened, at 30 / 45 / 90 degrees, and a generic sheared
// pair), records them, replays them, and compares the replayed renderer's
// world matrix, size, draw mode and colour with the live one.
public static class KillCamTelegraphTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[KCT] PASS  " : "[KCT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    sealed class Live { public string name; public SpriteRenderer sr; public float angleOffAxis; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var made = new List<Object>();
        var prevVisible = KillCamRecorder.RequireVisible;
        KillCamRecorder.RequireVisible = false;
        try { Run(made); }
        finally
        {
            KillCamRecorder.RequireVisible = prevVisible;
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            if (KillCamRecorder.Instance != null) Object.DestroyImmediate(KillCamRecorder.Instance.gameObject);
        }
        return fails;
    }

    static Sprite MakeSprite(List<Object> made)
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4, 0, SpriteMeshType.FullRect);
        made.Add(tex);
        made.Add(sprite);
        return sprite;
    }

    // The rail laser's own hierarchy: laser root (turned so +y runs along the beam) > frame > art.
    static SpriteRenderer RailLaser(List<Object> made, Sprite sprite, float degrees, bool tiled, Transform holder)
    {
        float rad = degrees * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        var root = new GameObject("~KCTRail" + degrees + (tiled ? "T" : "S"));
        root.transform.SetParent(holder, false);
        root.transform.position = new Vector3(-3f, 1f, 0f);
        root.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        var frame = new GameObject("Frame");
        frame.transform.SetParent(root.transform, false);
        var art = new GameObject("Art");
        art.transform.SetParent(frame.transform, false);
        var sr = art.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (tiled) RailMineLaser.SpanTiled(sr, .3f, 9f / Mathf.Max(.05f, Mathf.Abs(dir.x)), dir, true);
        else RailMineLaser.Span(art.transform, .6f, 9f / Mathf.Max(.05f, Mathf.Abs(dir.x)), dir);
        return sr;
    }

    // BossPatternMarkers.Line / BossProjectiles' dotted sight: a tiled strip, turned and widened.
    static SpriteRenderer TiledBar(Sprite sprite, float degrees, Transform holder)
    {
        var go = new GameObject("~KCTBar" + degrees);
        go.transform.SetParent(holder, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = new Vector2(.25f, 6f);
        go.transform.position = new Vector3(2f, -1f, 0f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, degrees - 90f);
        go.transform.localScale = new Vector3(3f, 1f, 1f);
        return sr;
    }

    static float OffAxis(Matrix4x4 m)
    {
        // the strip's long axis (sprite +y) in the world, degrees from the nearest screen axis
        float ang = Mathf.Atan2(m.m11, m.m01) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(Mathf.Round(ang / 90f) * 90f, ang));
    }

    static bool Near(Matrix4x4 a, Matrix4x4 b, float tol)
    {
        return Mathf.Abs(a.m00 - b.m00) < tol && Mathf.Abs(a.m01 - b.m01) < tol && Mathf.Abs(a.m10 - b.m10) < tol &&
               Mathf.Abs(a.m11 - b.m11) < tol && Mathf.Abs(a.m03 - b.m03) < tol && Mathf.Abs(a.m13 - b.m13) < tol;
    }

    static void Run(List<Object> made)
    {
        var sprite = MakeSprite(made);
        var holder = new GameObject("~KCTHolder");
        made.Add(holder);
        var rec = KillCamRecorder.Ensure();
        made.Add(rec.gameObject);
        rec.Clear();

        var live = new List<Live>();
        foreach (float deg in new[] { 30f, 45f, 60f, 120f })
        {
            live.Add(new Live { name = "rail aim line (tiled parallelogram) " + deg, sr = RailLaser(made, sprite, deg, true, holder.transform) });
            live.Add(new Live { name = "rail beam (stretched parallelogram) " + deg, sr = RailLaser(made, sprite, deg, false, holder.transform) });
        }
        foreach (float deg in new[] { 30f, 45f, 90f }) live.Add(new Live { name = "tiled marker bar " + deg, sr = TiledBar(sprite, deg, holder.transform) });

        // sheared: a non-uniform parent over a rotated child
        var shearParent = new GameObject("~KCTShear");
        shearParent.transform.SetParent(holder.transform, false);
        shearParent.transform.localScale = new Vector3(4f, .5f, 1f);
        var shearChild = new GameObject("c", typeof(SpriteRenderer));
        shearChild.transform.SetParent(shearParent.transform, false);
        shearChild.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
        shearChild.GetComponent<SpriteRenderer>().sprite = sprite;
        live.Add(new Live { name = "generic sheared pair", sr = shearChild.GetComponent<SpriteRenderer>() });

        // three frames: the telegraph lengthens and its tint pulses
        var pulses = new[] { .35f, 1f, .6f };
        for (int f = 0; f < 3; f++)
        {
            foreach (var l in live)
            {
                var c = l.sr.color; c.a = pulses[f]; l.sr.color = c;
                if (l.sr.drawMode == SpriteDrawMode.Tiled && l.name.StartsWith("tiled marker"))
                    l.sr.size = new Vector2(.25f, 6f + f);
            }
            rec.Capture(f / 60f);
        }
        var clip = rec.Clip(10f);
        Check("the tape holds the telegraph frames", clip != null && clip.frameCount == 3);
        if (clip == null) return;

        var stageHolder = new GameObject("~KCTStage");
        made.Add(stageHolder);
        var stage = new KillCamStage(stageHolder.transform, "Stage");

        // replay the LAST frame exactly (the live renderers still show it)
        stage.Draw(clip, clip.EndTime);
        foreach (var l in live)
        {
            int id = l.sr.GetInstanceID();
            int idx = -1;
            for (int i = 0; i < clip.count[2]; i++) if (clip.Entry(2, i).track == id) idx = i;
            if (idx < 0) { Check(l.name + ": recorded", false); continue; }
            var rp = stage.SpriteAt(idx);
            var lm = l.sr.transform.localToWorldMatrix;
            var rm = rp.transform.localToWorldMatrix;
            Check(l.name + ": replayed world matrix equals the live one (angle, length, width, shear)", Near(lm, rm, 2e-3f * Mathf.Max(1f, Mathf.Abs(lm.m00) + Mathf.Abs(lm.m11) + Mathf.Abs(lm.m01) + Mathf.Abs(lm.m10))));
            Check(l.name + ": draw mode, size and colour match",
                  rp.drawMode == l.sr.drawMode && (l.sr.drawMode == SpriteDrawMode.Simple || (rp.size - l.sr.size).sqrMagnitude < 1e-6f) &&
                  Mathf.Abs(rp.color.a - l.sr.color.a) < .01f && rp.sprite == sprite);
            float liveOff = OffAxis(lm), replayOff = OffAxis(rm);
            Check(l.name + ": an angled telegraph stays angled (" + liveOff.ToString("F1") + " vs " + replayOff.ToString("F1") + " deg off axis)",
                  Mathf.Abs(liveOff - replayOff) < .5f);
        }

        // between frames the matrix is blended element by element, not snapped straight
        stage.Draw(clip, (clip.time[0] + clip.time[1]) * .5f);
        var railIdx = -1;
        int railId = live[0].sr.GetInstanceID();
        for (int i = 0; i < clip.count[0]; i++) if (clip.Entry(0, i).track == railId) railIdx = i;
        var e0 = clip.Entry(0, railIdx);
        int cur = 0;
        var e1 = clip.Entry(1, clip.Match(1, railId, ref cur));
        var mid = stage.SpriteAt(railIdx).transform.localToWorldMatrix;
        float wantM01 = (e0.m01 + e1.m01) * .5f, wantM10 = (e0.m10 + e1.m10) * .5f;
        Check("a blended frame keeps the shear (mid matrix is the average)", Mathf.Abs(mid.m01 - wantM01) < 2e-3f && Mathf.Abs(mid.m10 - wantM10) < 2e-3f);
        Check("a blended frame keeps the pulsing tint", Mathf.Abs(stage.SpriteAt(railIdx).color.a - (e0.color.a + e1.color.a) * .5f / 255f) < .02f);

        // the decomposition itself
        float phi, theta, s1, s2;
        KillCamStage.Decompose(1.2f, -.4f, .9f, 2.1f, out phi, out theta, out s1, out s2);
        var rebuilt = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, phi * Mathf.Rad2Deg)) * Matrix4x4.Scale(new Vector3(s1, s2, 1f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg));
        Check("the shear split rebuilds any 2x2 matrix", Mathf.Abs(rebuilt.m00 - 1.2f) < 1e-4f && Mathf.Abs(rebuilt.m01 + .4f) < 1e-4f && Mathf.Abs(rebuilt.m10 - .9f) < 1e-4f && Mathf.Abs(rebuilt.m11 - 2.1f) < 1e-4f);

        stage.Destroy();
    }
}
