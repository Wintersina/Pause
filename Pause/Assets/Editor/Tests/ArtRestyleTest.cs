using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// The Akira restyle of the UI and the pixel-art pickup family
// (docs/art-style.md). Checks the art is on palette, the icons import
// cleanly, the pickups kept their world sizes and colliders, the green heal
// atom's original pixels and the protected logos are untouched, and the
// pickup flipbooks freeze with the world.
public static class ArtRestyleTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AR] PASS  " : "[AR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float timeScale = Time.timeScale;
        try
        {
            ProtectedFilesUntouched();
            UiArtOnPalette();
            PixelAtomsOnPalette();
            IconsSquareAndMipFree();
            PickupsKeepSizeAndColliders();
            GreenAtomOriginalUntouched();
            IdleFreezesWithTheWorld();
            PauseOverlayFrames();
        }
        finally { Time.timeScale = timeScale; }
        Debug.Log("[AR] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------
    // Protected assets: byte-identical to master (SHA-256 taken on master).
    // ------------------------------------------------------------------

    static readonly (string path, string sha)[] Protected =
    {
        ("Assets/Art/UI/Title/pause_title_2.png", "fbc73021f06f4dd3a79d8d42e1bd22870723f49d8d16a2474df8896b1ab7d305"),
        ("Assets/Art/UI/Title/pause_title_2.png.meta", "005e199d4579224cc74902d945901813061003e2979944897b6354f5f3a382a8"),
        ("Assets/Art/UI/Splash/HapticGate.png", "73531a210c4b0c8fbedc6effae9391e3f88ff1346aac642b190d5f5545225c69"),
        ("Assets/Art/UI/Splash/HapticGate.png.meta", "cae4da2d42f6c5bd74eb153a43d6122d851dc85a395e2aa161d472e88211dfd7"),
        ("../docs/pause-title.png", "40aa922dcef477e46a0545b2c867dd124f4a74fc9db61181b168a9694d249736"),
    };

    // The green heal atom's art: the user's benchmark, never redrawn.
    const string GreenAtomPath = "Assets/Art/Resources/Pickups/heal_atom_green.png";
    const string GreenAtomSha = "29e6d27f0c0136ebd28d1b9a6a22f4b5d3f8c26469cbeb96997e16336c9db2b0";

    static string Sha256(string path)
    {
        if (!File.Exists(path)) return "missing";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(File.ReadAllBytes(path));
        var sb = new System.Text.StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    static void ProtectedFilesUntouched()
    {
        foreach (var p in Protected)
            Check(p.path + " is byte-identical to master", Sha256(p.path) == p.sha);
    }

    // ------------------------------------------------------------------
    // Palette compliance
    // ------------------------------------------------------------------

    // Guide palette (§1), the UI sample tones (CARD, MUTED) and the white /
    // grey template tones that are tinted at runtime.
    static readonly string[] UiPalette =
    {
        "070A16", "0E1424", "1A1F45", "2A2E6B", "3A2A5C", "D8232C", "86121F", "FF5B45", "F2862B", "FFB43C",
        "A9481A", "1FB5B9", "6EF2EE", "0F5E6A", "FF2E88", "8E1450", "140C14", "F4EAD4", "2C2D40", "1A1A28",
        "5A5C78", "5A6A88", "262D44", "A3B4CC", "151B30", "8C93B8",
        "FFFFFF", "BFBFBF", "B8B8B8", "2C3346", "B4B4B4",
    };

    // The pixel-atom family: INK + four ramps (shadow, base, light, kick),
    // plus the green atom's own ramp and ink for its overlays and burst.
    static readonly string[] PixelAtomPalette =
    {
        "140C14", "F4EAD4",
        "0F5E6A", "1FB5B9", "6EF2EE",   // shield: TEAL_SH TEAL CYAN
        "86121F", "D8232C", "FF5B45",   // pause: RED_SH RED RED_HI
        "A9481A", "F2862B", "FFB43C",   // dust: SODIUM_SH SODIUM AMBER
        "322056", "7051B7", "B99AFF",   // cooldown (violet capacitor): VIOLET_SH VIOLET VIOLET_HI
        "29A805", "7EE702", "B4F246", "FDFDFD", "00021B",   // heal (sampled from the original)
    };

    static List<Color32> Parse(string[] hex)
    {
        var list = new List<Color32>();
        foreach (var h in hex)
        {
            int v = System.Convert.ToInt32(h, 16);
            list.Add(new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255));
        }
        return list;
    }

    static Color32[] Pixels(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!File.Exists(path) || !tex.LoadImage(File.ReadAllBytes(path))) return null;
            return tex.GetPixels32();
        }
        finally { Object.DestroyImmediate(tex); }
    }

    static int Dist(Color32 a, Color32 b)
    {
        return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
    }

    // Anti-aliasing tolerance: a colour on (or within 12 of) the blend
    // between two palette colours is an edge pixel, not an off-palette fill.
    static bool OnBlend(Color32 c, List<Color32> pal)
    {
        for (int i = 0; i < pal.Count; i++)
            for (int j = i + 1; j < pal.Count; j++)
            {
                Vector3 p0 = new Vector3(pal[i].r, pal[i].g, pal[i].b), p1 = new Vector3(pal[j].r, pal[j].g, pal[j].b);
                Vector3 v = p1 - p0, x = new Vector3(c.r, c.g, c.b);
                float t = Mathf.Clamp01(Vector3.Dot(x - p0, v) / Mathf.Max(1f, v.sqrMagnitude));
                Vector3 q = p0 + v * t;
                if (Mathf.Max(Mathf.Abs(x.x - q.x), Mathf.Max(Mathf.Abs(x.y - q.y), Mathf.Abs(x.z - q.z))) <= 12f)
                    return true;
            }
        return false;
    }

    static IEnumerable<string> RestyledUiPngs()
    {
        foreach (var dir in new[] { "Assets/Art/Resources/QuickActions", "Assets/Art/Resources/QuickActions/Shine",
                                    "Assets/Art/Resources/DeathPanel", "Assets/Art/Resources/Hud",
                                    "Assets/Art/UI/Dock/Resources/Dock", "Assets/Art/Resources/PauseGlow",
                                    "Assets/Art/Resources/PauseGlowFx", "Assets/Art/Resources/Tutorial" })
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.png")) yield return f.Replace('\\', '/');
        yield return "Assets/Art/UI/Pause/paused_1.png";
    }

    static void UiArtOnPalette()
    {
        var pal = Parse(UiPalette);
        int count = 0;
        foreach (var path in RestyledUiPngs())
        {
            var px = Pixels(path);
            if (px == null) { Check(path + " decodes", false); continue; }
            count++;
            var verdict = new Dictionary<int, (bool exact, bool blend)>();
            int opaque = 0, exact = 0, blend = 0;
            foreach (var c in px)
            {
                if (c.a < 200) continue;   // soft edges and translucent glows
                opaque++;
                int key = (c.r << 16) | (c.g << 8) | c.b;
                if (!verdict.TryGetValue(key, out var v))
                {
                    bool e = false;
                    foreach (var p in pal) if (Dist(c, p) <= 6) { e = true; break; }
                    v = (e, e || OnBlend(c, pal));
                    verdict[key] = v;
                }
                if (v.exact) exact++;
                if (v.blend) blend++;
            }
            if (opaque == 0) continue;
            float fe = exact / (float)opaque, fb = blend / (float)opaque;
            Check(Path.GetFileName(path) + " is flat guide palette (" + fe.ToString("P0") + " exact, " +
                  fb.ToString("P1") + " incl. AA edges)", fe >= .8f && fb >= .99f);
        }
        Check("restyled UI art was found (" + count + " files)", count >= 60);
    }

    static void PixelAtomsOnPalette()
    {
        var pal = Parse(PixelAtomPalette);
        var files = new List<string>(Directory.GetFiles("Assets/Art/Resources/Pickups/Atoms", "*.png"));
        files.Add("Assets/Art/Pickups/atom3a.png");
        files.Add("Assets/Art/Pickups/pauseAtom.png");
        files.Add("Assets/Art/Pickups/StarDustLarge.png");
        files.Add("Assets/Art/Pickups/StarDustSmall.png");
        foreach (var path in files)
        {
            var px = Pixels(path);
            if (px == null) { Check(path + " decodes", false); continue; }
            int bad = 0;
            foreach (var c in px)
            {
                if (c.a == 0) continue;
                bool ok = c.a == 255;
                if (ok)
                {
                    ok = false;
                    foreach (var p in pal) if (c.r == p.r && c.g == p.g && c.b == p.b) { ok = true; break; }
                }
                if (!ok) bad++;
            }
            Check(Path.GetFileName(path) + " is crisp pixel art on the pixel-atom palette (" + bad + " stray pixels)", bad == 0);

            var imp = AssetImporter.GetAtPath(path.Replace('\\', '/')) as TextureImporter;
            Check(Path.GetFileName(path) + " imports Point-filtered, mip-free, uncompressed",
                  imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled &&
                  imp.textureCompression == TextureImporterCompression.Uncompressed);
        }
        Check("pixel-atom frames were found (" + files.Count + ")", files.Count >= 80);
    }

    // ------------------------------------------------------------------
    // Icons
    // ------------------------------------------------------------------

    static void IconsSquareAndMipFree()
    {
        var files = new List<string>(Directory.GetFiles("Assets/Art/Resources/QuickActions", "*.png"));
        files.AddRange(Directory.GetFiles("Assets/Art/Resources/QuickActions/Shine", "*.png"));
        Check("quick-action icons, glyphs and shine frames exist (" + files.Count + ")", files.Count >= 13);
        foreach (var f in files)
        {
            string path = f.Replace('\\', '/');
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(Path.GetFileName(path) + " is square", tex != null && tex.width == tex.height);
            Check(Path.GetFileName(path) + " has no mipmaps", imp != null && !imp.mipmapEnabled);
        }
        foreach (var icon in new[] { PauseQuickActions.ReplayIconPath, PauseQuickActions.HomeIconPath })
            for (int i = 0; i < PauseQuickActions.ShineFrames; i++)
                Check(PauseQuickActions.ShinePath(icon, i) + " loads",
                      Resources.Load<Sprite>(PauseQuickActions.ShinePath(icon, i)) != null);
    }

    // ------------------------------------------------------------------
    // Pickups: world size, colliders, flipbooks
    // ------------------------------------------------------------------

    static void PickupsKeepSizeAndColliders()
    {
        Pickup("atom3a", PickupKind.Shield, .28f, new Vector2(.28f, .28f));
        Pickup("pauseAtom", PickupKind.Pause, .28f, new Vector2(.28f, .28f));
        Pickup("LargeStar_1", PickupKind.Dust, .256f, new Vector2(8f, 6f));
        Pickup("smStar_1", PickupKind.DustSmall, .064f, new Vector2(10f, 7f));
    }

    static void Pickup(string prefab, PickupKind kind, float worldSize, Vector2 colliderLocal)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/" + prefab + ".prefab");
        Check(prefab + " prefab loads", go != null);
        if (go == null) return;
        var sr = go.GetComponent<SpriteRenderer>();
        Vector3 size = sr != null && sr.sprite != null ? sr.sprite.bounds.size : Vector3.zero;
        float scale = go.transform.localScale.x;
        Check(prefab + " still draws " + worldSize + " world units (" + (size.x * scale).ToString("F4") + " x " +
              (size.y * scale).ToString("F4") + ")",
              Mathf.Abs(size.x * scale - worldSize) < worldSize * .01f && Mathf.Abs(size.y * scale - worldSize) < worldSize * .01f);
        var box = go.GetComponent<BoxCollider2D>();
        Check(prefab + " collider unchanged " + colliderLocal, box != null && (box.size - colliderLocal).sqrMagnitude < 1e-6f
              && box.offset == Vector2.zero);
        var book = go.GetComponent<PickupFlipbook>();
        Check(prefab + " carries its " + kind + " idle flipbook", book != null && book.kind == kind);
        var frames = PickupArt.Frames(PickupArt.IdleName(kind), PickupArt.IdleTicks(kind).Length);
        bool all = true;
        foreach (var f in frames) all &= f != null && Mathf.Abs(f.bounds.size.x - size.x) < .0001f;
        Check(prefab + " idle frames all load at the prefab sprite's size", all);
        var burst = PickupArt.Frames(PickupArt.BurstName(kind), PickupArt.BurstTicks.Length);
        bool burstOk = true;
        foreach (var f in burst) burstOk &= f != null;
        Check(prefab + " pickup burst frames load", burstOk);
    }

    // ------------------------------------------------------------------
    // The green atom: original pixels untouched, animation layered on top
    // ------------------------------------------------------------------

    static void GreenAtomOriginalUntouched()
    {
        Check("heal_atom_green.png is byte-identical to master", Sha256(GreenAtomPath) == GreenAtomSha);
        var green = HealAtom.Spawn(Vector3.zero);
        try
        {
            var sr = green.GetComponent<SpriteRenderer>();
            Check("the green atom's own sprite is the original texture",
                  sr.sprite != null && AssetDatabase.GetAssetPath(sr.sprite.texture) == GreenAtomPath);
            Check("its crop is unchanged (HealAtom.ArtRect)",
                  sr.sprite != null && sr.sprite.rect == HealAtom.ArtRect(1254, 1254));
            Check("its colour is untouched (no tint pulse)", sr.color == Color.white);

            var book = green.GetComponent<PickupFlipbook>();
            Check("the green atom has its overlay flipbook", book != null && book.kind == PickupKind.Heal);
            var overlay = green.transform.Find("HealGlint");
            var osr = overlay != null ? overlay.GetComponent<SpriteRenderer>() : null;
            Check("overlays draw on a child, above the original", osr != null && osr.sortingOrder > sr.sortingOrder);

            // Frame 0 is the original: its overlay is fully transparent, so the
            // composite is pixel-identical to the untouched art.
            var px = Pixels("Assets/Art/Resources/Pickups/Atoms/heal_glint_0.png");
            bool clear = px != null;
            if (px != null) foreach (var c in px) clear &= c.a == 0;
            Check("green atom frame 0 is the original, pixel for pixel (empty overlay)", clear);

            // The overlay covers exactly the original sprite's footprint.
            var f1 = Resources.Load<Sprite>(PickupArt.Root + "heal_glint_1");
            Check("overlay frames cover the original sprite's footprint",
                  f1 != null && Mathf.Abs(f1.bounds.size.x - sr.sprite.bounds.size.x) < sr.sprite.bounds.size.x * .01f);

            int lit = 0;
            for (int i = 1; i < PickupArt.HealIdleTicks.Length; i++)
            {
                var p = Pixels("Assets/Art/Resources/Pickups/Atoms/heal_glint_" + i + ".png");
                if (p == null) continue;
                foreach (var c in p) if (c.a > 0) { lit++; break; }
            }
            Check("the green atom's idle actually animates (" + lit + " overlay frames drawn)", lit >= 8);
        }
        finally { Object.DestroyImmediate(green); }
    }

    // ------------------------------------------------------------------
    // Idle flipbooks run on game time: frozen at timeScale 0
    // ------------------------------------------------------------------

    static void IdleFreezesWithTheWorld()
    {
        var update = typeof(PickupFlipbook).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var made = new List<GameObject>();
        try
        {
            foreach (var name in new[] { "atom3a", "pauseAtom", "LargeStar_1", "smStar_1" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/" + name + ".prefab");
                if (prefab != null) made.Add(Object.Instantiate(prefab));
            }
            made.Add(HealAtom.Spawn(Vector3.zero));

            foreach (var go in made)
            {
                var book = go.GetComponent<PickupFlipbook>();
                if (book == null) { Check(go.name + " has a flipbook", false); continue; }
                book.Advance(0f);   // settle on frame 0 (edit mode skips Awake)
                Time.timeScale = 0f;
                int before = book.Frame;
                Sprite shown = book.Target != null ? book.Target.sprite : null;
                for (int i = 0; i < 200; i++) update.Invoke(book, null);
                Check(go.name + ": no frame advances at timeScale 0",
                      book.Frame == before && (book.Target == null || book.Target.sprite == shown));

                Time.timeScale = 1f;
                book.Advance(.5f);
                Check(go.name + ": it does animate on game time (frame " + book.Frame + " of " + book.FrameCount + ")",
                      book.Frame != before && book.FrameCount >= 10);
            }
        }
        finally
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        }
    }

    static void PauseOverlayFrames()
    {
        var variants = Resources.LoadAll<Sprite>("PauseGlow");
        Check("PauseGlow still holds exactly the two variants (" + variants.Length + ")", variants.Length == 2);
        foreach (var v in variants)
        {
            var frames = PausedOverlayAnim.FramesFor(v.name);
            Check(v.name + " has its pop-in and glint flipbook", frames != null && frames.Length == 6);
            if (frames != null)
                Check(v.name + " flipbook frames match the variant's size",
                      Mathf.Abs(frames[2].bounds.size.x - v.bounds.size.x) < .001f);
        }
    }
}
