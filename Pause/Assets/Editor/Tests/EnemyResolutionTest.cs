using System.IO;
using UnityEditor;
using UnityEngine;

// ENEMY ART MUST BE REAL RESOLUTION. The pixel-duplication gate of BackdropResolutionTest applied to every
// shipped enemy texture: roster enemies, elites, bosses (atlas, damage stages, death strips, damage fx),
// mine atlases and every death strip, all worlds. A sheet is a nearest-neighbour k x k block upscale (k = 2, 3, 4)
// when, at some phase, >= 97% of its opaque texels equal the first texel of their block; adjacent identical
// texel pairs stay under a ceiling; and nothing is imported clamped well below its own size (up to 1.3x).
//
// Flat UI graphics are not sprites and are skipped by name: the boss name cards (*_card), the warning banner
// and the shot-bar strips (*_shots), which are large flat fills where identical neighbours are the design.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EnemyResolutionTest.Run
public static class EnemyResolutionTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ERES] PASS  " : "[ERES] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Resources/";
    static readonly string[] Dirs = { "Enemies", "Elites", "Bosses" };
    const float BlockFraction = 0.97f;
    const float PairCeiling = 0.75f;          // measured worst 0.68 (Space boss damage fx); flat cards are skipped
    const int MinOpaque = 300;

    static bool FlatUi(string rel)
    {
        return rel.EndsWith("_card.png") || rel.EndsWith("_shots.png") || rel.EndsWith("/warning.png");
    }

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var d in Dirs)
            foreach (var png in Directory.GetFiles(Root + d, "*.png", SearchOption.AllDirectories))
            {
                string path = png.Replace('\\', '/');
                string rel = path.Substring(Root.Length);
                if (FlatUi(rel)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                int W = tex.width, H = tex.height;
                var px = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                float worst = 0; int worstK = 0;
                for (int k = 2; k <= 4; k++)
                {
                    float s = BackdropResolutionTest.BlockScore(px, W, 0, 0, W, H, k);
                    if (s > worst) { worst = s; worstK = k; }
                }
                float pairs = BackdropResolutionTest.PairDup(px, W, H);
                bool ok = worst < BlockFraction && pairs <= PairCeiling;
                Check(rel + ": block score " + worst.ToString("0.000") + " (k=" + worstK + ") < " + BlockFraction + ", identical pairs " +
                      pairs.ToString("0.00") + " <= " + PairCeiling, ok);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null)
                    Check(rel + ": import max size " + imp.maxTextureSize + " does not clamp " + W + "x" + H + " by more than ~20%",
                          imp.maxTextureSize * 1.3f >= System.Math.Max(W, H));
                n++;
            }
        Check("audited at least 200 enemy textures (" + n + ")", n >= 200);
        Debug.Log("[ERES] failures: " + fails);
        return fails;
    }
}
