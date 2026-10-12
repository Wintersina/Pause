using UnityEngine;

// Effect art for the HapticGate splash (the taps and the smash drawn over the
// gate film, see GateView / GateFilm), data driven by slot name.
//
// Each slot is a PNG in Resources/HapticGate/ (see docs/hapticgate-splash.md).
// If any slot is missing Load returns null and the card plays the film without effects.
//
//   gate_debris          1024x512   8x4 cells of 128 px debris sprites
//   gate_steam           1024x1024  4x4 cells of 256 px steam puffs
//
// (The old procedural door and its crack overlays are retired: the gate is now the
// approved film's own.)
public sealed class GateArt
{
    public const string DebrisSlot = "HapticGate/gate_debris";
    public const string SteamSlot = "HapticGate/gate_steam";

    public const int DebrisCols = 8, DebrisRows = 4, DebrisCell = 128;
    public const int SteamCols = 4, SteamRows = 4;

    public Sprite[] debris;
    public Sprite[] steam;
    public Sprite white, glow;

    // Codex's cells hold a drawing smaller than the cell; these scale the sprites to match.
    public const float DebrisDrawScale = 1.8f, SteamDrawScale = 1.7f;

    Texture2D whiteTex, glowTex;
    readonly System.Collections.Generic.List<Object> owned = new System.Collections.Generic.List<Object>();

    // Returns null if any slot's art is missing.
    public static GateArt Load()
    {
        var deb = Resources.Load<Texture2D>(DebrisSlot);
        var st = Resources.Load<Texture2D>(SteamSlot);
        if (deb == null || st == null) return null;

        var art = new GateArt();
        deb.filterMode = st.filterMode = FilterMode.Point;
        art.debris = Cells(deb, DebrisCols, DebrisRows, art);
        art.steam = Cells(st, SteamCols, SteamRows, art);
        art.MakeUtility();
        return art;
    }

    public void Release()
    {
        for (int i = 0; i < owned.Count; i++) Kill(owned[i]);
        owned.Clear();
        Kill(whiteTex); Kill(glowTex);
        whiteTex = glowTex = null;
    }

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
    }

    static Sprite[] Cells(Texture2D t, int cols, int rows, GateArt owner)
    {
        float cw = t.width / (float)cols, ch = t.height / (float)rows;
        var list = new Sprite[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var s = Sprite.Create(t, new Rect(c * cw, (rows - 1 - r) * ch, cw, ch), new Vector2(0.5f, 0.5f), 100f,
                                      0, SpriteMeshType.FullRect);
                list[r * cols + c] = s;
                owner.owned.Add(s);
            }
        return list;
    }

    void MakeUtility()
    {
        whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        var wp = new Color32[16];
        for (int i = 0; i < 16; i++) wp[i] = new Color32(255, 255, 255, 255);
        whiteTex.SetPixels32(wp); whiteTex.Apply(false, true);
        white = Sprite.Create(whiteTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        owned.Add(white);

        const int n = 64;
        glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var gp = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - n / 2 + 0.5f) * (x - n / 2 + 0.5f) + (y - n / 2 + 0.5f) * (y - n / 2 + 0.5f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a *= a;
                gp[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        glowTex.SetPixels32(gp); glowTex.Apply(false, true);
        glow = Sprite.Create(glowTex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        owned.Add(glow);
    }
}
