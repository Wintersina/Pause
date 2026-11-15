using UnityEngine;

// Effect art for the HapticGate splash (the taps and the smash drawn over the
// gate film, see GateView / GateFilm), data driven by slot name.
//
// Each slot is a PNG in Resources/HapticGate/ (see docs/hapticgate-splash.md).
// If any slot is missing Load returns null and the card plays the film without effects.
//
//   gate_steam           1024x1024  4x4 cells of 256 px steam puffs
//
// The themed burst (star dust + tiny player ships) reuses in-game art, no slots of its own:
// the dock's star dust glyph (Dock/icon_dust) and the intact first cell of a few player hulls
// (ShipArt/<Key>/<Key>, the same sheets the game flies).
// (The old procedural door and its crack overlays are retired: the gate is now the
// approved film's own.)
public sealed class GateArt
{
    public const string SteamSlot = "HapticGate/gate_steam";

    public const int SteamCols = 4, SteamRows = 4;

    // Hulls that fly out of the burst (art keys under Resources/ShipArt).
    public static readonly string[] ShipKeys = { "NeonComet", "SolarFang", "IonLancer" };
    public const int ShipCell = 256;

    public Sprite[] steam;
    public Sprite[] ships;      // one intact, undamaged hull per ShipKeys entry (null entries if a sheet is missing)
    public Sprite dust;         // the star dust sparkle
    public Sprite white, glow;
    readonly System.Collections.Generic.List<Texture2D> shipTex = new System.Collections.Generic.List<Texture2D>();

    // Codex's cells hold a drawing smaller than the cell; these scale the sprites to match.
    public const float SteamDrawScale = 1.7f;

    Texture2D whiteTex, glowTex;
    readonly System.Collections.Generic.List<Object> owned = new System.Collections.Generic.List<Object>();

    // Returns null if any slot's art is missing.
    public static GateArt Load()
    {
        var st = Resources.Load<Texture2D>(SteamSlot);
        if (st == null) return null;

        var art = new GateArt();
        st.filterMode = FilterMode.Point;
        art.steam = Cells(st, SteamCols, SteamRows, art);
        art.MakeUtility();
        art.LoadThemed();
        return art;
    }

    public void Release()
    {
        for (int i = 0; i < owned.Count; i++) Kill(owned[i]);
        owned.Clear();
        for (int i = 0; i < shipTex.Count; i++)
            if (shipTex[i] != null && Application.isPlaying) Resources.UnloadAsset(shipTex[i]);
        shipTex.Clear();
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

    // The star dust glyph and the hull cells. Everything is loaded here, at scene start: a tap never loads.
    void LoadThemed()
    {
        dust = DockArt.Get("icon_dust");
        ships = new Sprite[ShipKeys.Length];
        for (int i = 0; i < ShipKeys.Length; i++)
        {
            var t = Resources.Load<Texture2D>("ShipArt/" + ShipKeys[i] + "/" + ShipKeys[i]);
            if (t == null) continue;
            shipTex.Add(t);
            t.wrapMode = TextureWrapMode.Clamp;
            // row 0 (intact), column 0 (the key pose); texture rows count from the bottom
            var s = Sprite.Create(t, new Rect(0, t.height - ShipCell, ShipCell, ShipCell), new Vector2(0.5f, 0.5f), ShipCell,
                                  0, SpriteMeshType.FullRect);
            s.name = "gate_ship_" + ShipKeys[i];
            ships[i] = s;
            owned.Add(s);
        }
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
