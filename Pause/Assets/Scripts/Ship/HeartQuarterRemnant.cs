using UnityEngine;

// A dying enemy releases its already-created quarter sprites so the final hit remains visible.
// No new sprite/texture generation; this short-lived group owns at most one orbit's eight pieces.
public sealed class HeartQuarterRemnant : MonoBehaviour
{
    readonly SpriteRenderer[] pieces = new SpriteRenderer[8];
    readonly int[] parts = new int[8];
    readonly float[] ages = new float[8];
    readonly Vector3[] origins = new Vector3[8], scales = new Vector3[8];
    readonly Quaternion[] rotations = new Quaternion[8];
    HeartQuarterFrames.Set frames;
    int count;
    public void Adopt(SpriteRenderer piece, HeartQuarterFrames.Set art, int part, float age, Vector3 origin)
    {
        frames = art;
        piece.transform.SetParent(transform, true);
        pieces[count] = piece; parts[count] = part; ages[count] = age;
        origins[count] = origin; scales[count] = piece.transform.lossyScale; rotations[count] = piece.transform.rotation;
        count++;
    }
    void Update() { Step(Time.deltaTime); }
    public void Step(float dt)
    {
        bool live = false;
        for (int i = 0; i < count; i++)
        {
            ages[i] += Mathf.Max(0f, dt);
            bool show = ages[i] < HeartQuarterFrames.BreakSeconds;
            pieces[i].enabled = show;
            if (show)
            {
                live = true;
                pieces[i].transform.position = origins[i] + frames.Drift(parts[i], ages[i], scales[i], rotations[i]);
                pieces[i].sprite = frames.LossFrames[parts[i]][HeartQuarterFrames.FrameAt(ages[i])];
            }
        }
        if (!live)
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
