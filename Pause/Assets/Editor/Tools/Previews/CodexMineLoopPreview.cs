using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Codex detail view of each world's rail mine over time (the idle loop, then the arming tell, which plays after a few seconds):
// one 2x-zoomed crop of the art box every 0.25 s for 9 s, to $CODEX_MINE_DIR/<world>-tNN.png.
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod CodexMineLoopPreview.Run
public static class CodexMineLoopPreview
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("CODEX_MINE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/CodexMineLoop";
        Directory.CreateDirectory(dir);
        int code = 0;
        try
        {
            using (new TestHarness.Sandbox())
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                DeveloperUnlocks.SetEnabled(true);
                Codex.Reload();
                var panel = CodexPanel.Open(null);
                panel.Refresh();
                panel.SkipAnimations();
                for (int w = 0; w < 7; w++)
                {
                    var def = EnemyRoster.One(w, EnemyRole.Mine);
                    var e = def == null ? null : Codex.Find(def.codexId);
                    if (e == null) { Debug.Log("[CODEX-MINE] no entry for world " + w); continue; }
                    UnityEngine.Random.InitState(7);
                    panel.ShowGrid();
                    panel.SkipAnimations();
                    for (int s = 0; s <= 36; s++)
                    {
                        float at = s * .25f;
                        var tex = CodexPreview.Capture(panel, 800, 1280, () =>
                        {
                            if (s == 0) { panel.ShowDetail(e); panel.SkipAnimations(); }
                            else for (int k = 0; k < 8; k++) panel.TickAnimations(1f / 32f);
                        }, 1);
                        File.WriteAllBytes(Path.Combine(dir, w + "-t" + s.ToString("00") + ".png"), tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                    Debug.Log("[CODEX-MINE] world " + w + " done");
                }
            }
        }
        catch (Exception ex) { Debug.LogException(ex); code = 1; }
        EditorApplication.Exit(code);
    }
}
