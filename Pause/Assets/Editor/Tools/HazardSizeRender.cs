using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Evidence renders for the hazard sizes (HazardSize), drawn by the game
// camera at a phone's 1080x2520 (CameraFit: the device's pixels per unit).
// Nothing here is gameplay.
//
//   HAZARD_SIZES_OUT=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod HazardSizeRender.Run
//
// Per world:
//   <world>-field.png   the real spawner flown headless (EnemyDensityProbe's
//                       stepping) at HUD 20 for a while: the board as it
//                       comes, mixed sizes and all, plus the lane guides
//   <world>-sheet.png   each of the world's rocks in a row at its smallest,
//                       typical and largest size, side by side
//   <world>-sheet-x3.png the sheet's rocks cropped and blown up 3x (nearest),
//                       to judge the pixels at each size
// and "[HAZARD-RENDER] ..." lines with the sizes drawn.
public static class HazardSizeRender
{
    const float Dt = 1f / 60f;
    const int W = 1080, H = 2520;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("HAZARD_SIZES_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = "Logs/hazard-sizes";
        Directory.CreateDirectory(outDir);
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        var cam = Camera.main;
        var walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        EnemyThreat.ForceShooting = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        float ppu = H / (2f * cam.orthographicSize);
        Debug.Log(string.Format("[HAZARD-RENDER] {0}x{1}: {2:F1} px per unit; a rock frame ({3} u, 192 px) at size 1 draws {4:F1} px ({5:F2} texels per screen pixel)",
                                W, H, ppu, EnemyRoster.FrameWorldSize(EnemyRole.Rock), EnemyRoster.FrameWorldSize(EnemyRole.Rock) * ppu,
                                192f / (EnemyRoster.FrameWorldSize(EnemyRole.Rock) * ppu)));
        try
        {
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                var theme = WorldManager.Worlds[w];
                PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
                var wbgo = new GameObject("~SizeBackdrop");
                var wb = wbgo.AddComponent<WorldBackdrop>();
                WorldPainter.Apply(theme);
                foreach (var wall in walls)
                {
                    var s = wall.transform.localScale;
                    s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
                    wall.transform.localScale = s;
                    RailFit.RefreshTextureTiling(wall);
                }
                BossRails.Measure();
                wb.Show(theme.displayName, false);
                for (int i = 0; i < 30; i++) wb.Step(Dt);
                string tag = outDir + "/" + theme.displayName.ToLowerInvariant();

                Field(cam, w, tag + "-field.png");
                Sheet(cam, w, ppu, tag + "-sheet.png", tag + "-sheet-x3.png");
                Object.DestroyImmediate(wbgo);
            }
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            SpawnSpace.ClockOverride = null;
            EnemyDensityProbe.Clear();
            moveBackGround.speed = 0f;
        }
    }

    // The real spawner, flown for a while at HUD 20 in world `w`.
    static void Field(Camera cam, int w, string path)
    {
        EnemyDensityProbe.Clear();
        EnemyDensityProbe.chasers.Clear();
        LoopDifficulty.Reset();
        Random.InitState(5150 + w);
        const int hud = 20;
        moveBackGround.speed = hud / 100f;
        var board = EnemyDensityProbe.NewBoard();
        var ship = new GameObject("~SizeShip").transform;
        float levelSecond = EnemyDensityProbe.LevelSecondFor(hud);
        float t = 0f;
        // fly until the board holds a good field of rocks (at most 40 s)
        for (int f = 0; f < 60 * 40; f++)
        {
            t += Dt;
            EnemyDensityProbe.Elapsed.SetValue(board, levelSecond);
            EnemyDensityProbe.StepBoard(board, ship, t, hud * .3f, _ => new Vector3(0f, -50f, 0f));
            if (f > 60 * 12 && RocksInView(out _) >= 7) break;
        }
        ship.position = new Vector3(0f, -50f, 0f);
        // (the tumble only runs in play mode: give the tumblers a pose)
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
            if (id.Def != null && id.Def.role == EnemyRole.Rock && !id.Def.floating)
                id.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        int n = RocksInView(out string sizes);
        Debug.Log("[HAZARD-RENDER] " + WorldManager.Worlds[w].displayName + " field: " + n + " rocks in view, sizes " + sizes);
        Shot(cam, path);
        var guides = Guides();
        Shot(cam, path.Replace(".png", "-guides.png"));
        Object.DestroyImmediate(guides);
        Object.DestroyImmediate(ship.gameObject);
        EnemyDensityProbe.Clear();
        SpawnSpace.ClockOverride = null;
    }

    static int RocksInView(out string sizes)
    {
        var list = new List<string>();
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id.Def == null || id.Def.role != EnemyRole.Rock) continue;
            float y = id.transform.position.y;
            if (y < CameraFit.ViewBottom || y > CameraFit.ViewTop) continue;
            list.Add(id.Def.key.Substring(id.Def.key.LastIndexOf('_') + 1) + " " + id.Scale.ToString("F2"));
        }
        sizes = string.Join(", ", list);
        return list.Count;
    }

    // Each rock of world `w` at its smallest, typical and largest size.
    static void Sheet(Camera cam, int w, float ppu, string path, string zoomPath)
    {
        var rocks = EnemyRoster.For(w, EnemyRole.Rock);
        var root = new GameObject("~SizeSheet");
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        float rowGap = (top - bottom - 2f) / Mathf.Max(1, rocks.Count);
        var cells = new List<RectInt>();
        for (int r = 0; r < rocks.Count; r++)
        {
            var def = rocks[r];
            var b = def.Behaviour;
            float[] sizes = { HazardSize.Min(b), b.sizeTypical, HazardSize.Max(b) };
            float y = top - 1.2f - rowGap * (r + .5f);
            var line = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                float x = (k - 1) * 1.55f;
                var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity, sizes[k]);
                go.transform.SetParent(root.transform, true);
                var brain = go.GetComponent<EnemyBrain>();
                if (brain != null) brain.enabled = false;
                float frame = def.FrameWorldSize * sizes[k];
                line.Add(string.Format("{0:F2}x = {1:F0} px ({2:F2} texels/px)", sizes[k], frame * ppu, 192f / (frame * ppu)));
                int px = Mathf.RoundToInt((x + cam.orthographicSize * cam.aspect) * ppu);
                int py = Mathf.RoundToInt((y - bottom) * ppu);
                int half = Mathf.RoundToInt(frame * ppu * .55f);
                cells.Add(new RectInt(px - half, py - half, half * 2, half * 2));
            }
            Debug.Log("[HAZARD-RENDER] " + def.key + ": " + string.Join(" | ", line));
        }
        var tex = Shot(cam, path, keep: true);
        // the cells blown up 3x, nearest, in a grid (3 per row)
        int cell = 0;
        foreach (var c in cells) cell = Mathf.Max(cell, c.width);
        int zw = cell * 3 * 3, zh = cell * 3 * Mathf.CeilToInt(cells.Count / 3f);
        var zoom = new Texture2D(zw, zh, TextureFormat.RGB24, false);
        var bg = new Color[zw * zh];
        for (int i = 0; i < bg.Length; i++) bg[i] = new Color(.08f, .06f, .12f);
        zoom.SetPixels(bg);
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            int ox = (i % 3) * cell * 3, oy = zh - (i / 3 + 1) * cell * 3;
            for (int y = 0; y < c.height; y++)
                for (int x = 0; x < c.width; x++)
                {
                    int sx = Mathf.Clamp(c.x + x, 0, W - 1), sy = Mathf.Clamp(c.y + y, 0, H - 1);
                    Color p = tex.GetPixel(sx, sy);
                    for (int dy = 0; dy < 3; dy++)
                        for (int dx = 0; dx < 3; dx++)
                            zoom.SetPixel(ox + x * 3 + dx, oy + y * 3 + dy, p);
                }
        }
        zoom.Apply();
        File.WriteAllBytes(zoomPath, zoom.EncodeToPNG());
        Object.DestroyImmediate(zoom);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(root);
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(id.gameObject);
    }

    static Texture2D white;

    static GameObject Guides()
    {
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        var sprite = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        var g = new GameObject("~SizeGuides");
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        foreach (float side in new[] { -1f, 1f })
        {
            var go = new GameObject("guide");
            go.transform.SetParent(g.transform, false);
            go.transform.position = new Vector3(side * SpawnLane.LaneHalf, (top + bottom) * .5f, 0f);
            go.transform.localScale = new Vector3(.025f, top - bottom, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(.2f, 1f, 1f, .9f);
            sr.sortingOrder = 200;
        }
        // each rock's collider, drawn as a thin outline
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            BoxCollider2D box;
            if (!id.TryGetComponent(out box)) continue;
            Vector2 size = Vector2.Scale(box.size, (Vector2)id.transform.lossyScale);
            Vector3 c = id.transform.position;
            float a = id.transform.eulerAngles.z;
            for (int e = 0; e < 4; e++)
            {
                var go = new GameObject("box");
                go.transform.SetParent(g.transform, false);
                bool horizontal = e < 2;
                float sign = e % 2 == 0 ? 1f : -1f;
                Vector3 off = horizontal ? new Vector3(0f, sign * size.y * .5f, 0f) : new Vector3(sign * size.x * .5f, 0f, 0f);
                go.transform.position = c + Quaternion.Euler(0f, 0f, a) * off;
                go.transform.rotation = Quaternion.Euler(0f, 0f, a);
                go.transform.localScale = horizontal ? new Vector3(size.x, .015f, 1f) : new Vector3(.015f, size.y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = new Color(1f, .95f, .2f, .85f);
                sr.sortingOrder = 201;
            }
        }
        return g;
    }

    static Texture2D Shot(Camera cam, string path, bool keep = false)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        if (keep) return tex;
        Object.DestroyImmediate(tex);
        return null;
    }
}
