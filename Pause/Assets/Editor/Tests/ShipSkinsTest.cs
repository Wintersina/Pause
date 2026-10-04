using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Purchasable hull skins (ShipSkins, ShipHullArt, the dock's swatch row):
//   - every ship has every skin, each a full 27-frame sheet with the stock
//     sheet's exact silhouette, in palette colours only
//   - buying deducts once and saves; it can't happen without the dust
//   - the equipped skin persists per ship and is what spawns, flies and parks
//   - the dock previews a locked skin, Back reverts it, then deselects, then
//     goes home
//   - developer mode shows every skin owned without writing a real key
//   - the cloud save unions owned skins and takes equipped from the newer side
//   - sheets are decoded lazily and released when nothing shows them
public static class ShipSkinsTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SK] PASS  " : "[SK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // docs/art-style.md section 1 (akira.py), plus BONE's shadow.
    static readonly string[] guidePalette =
    {
        "#070A16", "#0E1424", "#1A1F45", "#2A2E6B", "#3A2A5C", "#D8232C", "#86121F", "#FF5B45",
        "#F2862B", "#FFB43C", "#A9481A", "#1FB5B9", "#6EF2EE", "#0F5E6A", "#FF2E88", "#8E1450",
        "#140C14", "#F4EAD4", "#2C2D40", "#1A1A28", "#5A5C78", "#5A6A88", "#262D44", "#A3B4CC",
        "#74409A", "#3A1E52", "#A86CD0", "#8FA84E", "#3E5229", "#D4E68E", "#C8FF3A", "#605878",
        "#2C2638", "#958AA4", "#2EE6A6", "#0E5A50", "#0A3238", "#B9AE98",
    };
    // Enemy-only colours a player hull must never wear (art-style.md 1.2).
    static readonly string[] enemyOnly =
    {
        "#FF2E88", "#8E1450", "#5A6A88", "#262D44", "#A3B4CC", "#74409A", "#3A1E52", "#A86CD0",
        "#8FA84E", "#3E5229", "#D4E68E", "#C8FF3A",
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        ResetSkins();

        Table();
        foreach (int id in ShipId.All) Sheets(id);
        PaletteCompliance();
        Purchases();
        EquipPerShip();
        ApplyHullUsesEquipped();
        LazyLoading();
        SkinHueApi();
        DeveloperMode();
        CloudMerge();
        DockFlow();

        ShipSkins.ClearPreview();
        ShipHullArt.ReleaseUnusedSkins();
        Debug.Log("[SK] failures: " + fails);
        return fails;
    }

    static void ResetSkins()
    {
        ShipSkins.ClearPreview();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int id = 0; id <= shopingShips.shipTotal; id++)
        {
            PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(id));
            PlayerPrefs.DeleteKey(ShipSkins.DeveloperEquippedKey(id));
            for (int n = 0; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
        ShipHullArt.ReleaseUnusedSkins();
    }

    static void Own(params int[] ids)
    {
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        foreach (int id in ids) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
    }

    // ------------------------------------------------------------- table

    static void Table()
    {
        Check("prices: stock free, palette 300, special 750",
              ShipSkins.PriceOf(ShipSkinKind.Stock) == 0f && ShipSkins.PriceOf(ShipSkinKind.Palette) == 300f &&
              ShipSkins.PriceOf(ShipSkinKind.Special) == 750f);
        foreach (int id in ShipId.All)
        {
            string who = id + " " + ShipId.NameOf(id);
            Check(who + " has " + ShipSkins.PerShip + " skins", ShipSkins.CountFor(id) == ShipSkins.PerShip);
            var names = new HashSet<string>();
            for (int n = 0; n < ShipSkins.CountFor(id); n++)
            {
                var s = ShipSkins.Get(id, n);
                names.Add(s.name);
                var want = n == 0 ? ShipSkinKind.Stock : n == ShipSkins.Special ? ShipSkinKind.Special : ShipSkinKind.Palette;
                Check(who + " skin " + n + " (" + s.name + ") is " + want, s.kind == want);
            }
            Check(who + " skin names are distinct", names.Count == ShipSkins.CountFor(id));
            var stock = ShipSkins.Get(id, 0);
            var r = ShipHullArt.RectFor(id);
            Check(who + " stock skin is its identity hue", stock.hue == r.hue && stock.hueShadow == r.hueShadow);
            Check(who + " special keeps the identity hue and adds a livery",
                  ShipSkins.Get(id, ShipSkins.Special).hue == r.hue && ShipSkins.Get(id, ShipSkins.Special).pattern != null);
            for (int n = 1; n < ShipSkins.Special; n++)
                Check(who + " palette skin " + ShipSkins.Get(id, n).name + " differs from stock",
                      Distance(ShipSkins.Get(id, n).primary, stock.primary) > 60f);
        }
    }

    static float Distance(Color a, Color b)
    {
        return Vector3.Distance(new Vector3(a.r, a.g, a.b) * 255f, new Vector3(b.r, b.g, b.b) * 255f);
    }

    // ------------------------------------------------------------- sheets

    static Texture2D Decode(byte[] png)
    {
        if (png == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return t.LoadImage(png) ? t : null;
    }

    static readonly Dictionary<int, Color32[]> stockPixels = new Dictionary<int, Color32[]>();

    static Color32[] Stock(int id)
    {
        Color32[] px;
        if (stockPixels.TryGetValue(id, out px)) return px;
        string path = AssetDatabase.GetAssetPath(ShipHullArt.SheetFor(id));
        var t = Decode(File.Exists(path) ? File.ReadAllBytes(path) : null);
        px = t != null ? t.GetPixels32() : null;
        if (t != null) Object.DestroyImmediate(t);
        stockPixels[id] = px;
        return px;
    }

    static void Sheets(int id)
    {
        string who = id + " " + ShipId.NameOf(id);
        var stock = Stock(id);
        for (int n = 1; n < ShipSkins.CountFor(id); n++)
        {
            string skin = who + " " + ShipSkins.Get(id, n).name;
            var png = Decode(ShipHullArt.SkinPng(id, n));
            Check(skin + " sheet exists (" + ShipHullArt.SkinFolder + ShipSkins.SheetName(id, n) + ")", png != null);
            if (png == null) continue;
            Check(skin + " sheet is " + ShipHullArt.Columns + "x" + ShipHullArt.States + " cells",
                  png.width == ShipHullArt.Columns * ShipHullArt.Cell && png.height == ShipHullArt.States * ShipHullArt.Cell);
            var px = png.GetPixels32();
            bool sameShape = stock != null && stock.Length == px.Length;
            bool recoloured = false;
            for (int i = 0; sameShape && i < px.Length; i++)
            {
                if (px[i].a != stock[i].a) sameShape = false;
                else if (!recoloured && px[i].a > 250 && (px[i].r != stock[i].r || px[i].g != stock[i].g || px[i].b != stock[i].b))
                    recoloured = true;
            }
            Check(skin + " has the stock silhouette pixel for pixel (shield, collider, nozzles unchanged)", sameShape);
            Check(skin + " is actually recoloured", recoloured);
            // every one of the 27 cells is drawn
            int w = png.width, drawn = 0;
            for (int s = 0; s < ShipHullArt.States; s++)
                for (int c = 0; c < ShipHullArt.Columns; c++)
                {
                    int x = c * ShipHullArt.Cell + ShipHullArt.Cell / 2, y = (ShipHullArt.States - 1 - s) * ShipHullArt.Cell;
                    bool any = false;
                    for (int yy = y; yy < y + ShipHullArt.Cell && !any; yy += 2)
                        for (int xx = x - 40; xx < x + 40 && !any; xx += 2)
                            any = px[yy * w + xx].a > 200;
                    if (any) drawn++;
                }
            Check(skin + " has all 27 frames drawn", drawn == ShipHullArt.States * ShipHullArt.Columns);
            Object.DestroyImmediate(png);

            var sprites = new HashSet<Sprite>();
            bool named = true;
            for (int s = 0; s < ShipHullArt.States; s++)
                for (int c = 0; c < ShipHullArt.Columns; c++)
                {
                    var sprite = ShipHullArt.Get(id, n, s, c);
                    if (sprite != null) sprites.Add(sprite);
                    named &= sprite != null && sprite.texture != null && sprite.texture.name == ShipSkins.SheetName(id, n);
                }
            Check(skin + " loads 27 distinct frames from its own sheet", sprites.Count == 27 && named);
            var rest = ShipHullArt.Get(id, n, 0, 0);
            var stockRest = ShipHullArt.Get(id, 0, 0, 0);
            Check(skin + " frames have the stock size and pivot",
                  rest != null && stockRest != null && rest.bounds == stockRest.bounds && rest.rect.size == stockRest.rect.size);
            ShipHullArt.Release(id, n);
        }
    }

    // ------------------------------------------------------------- palette

    static List<Color32> Palette(params string[] hex)
    {
        var list = new List<Color32>();
        foreach (var h in hex) list.Add(ShipSkins.Parse(h));
        return list;
    }

    static bool Near(Color32 c, List<Color32> palette, int tol)
    {
        foreach (var p in palette)
        {
            int dr = c.r - p.r, dg = c.g - p.g, db = c.b - p.b;
            if (dr * dr + dg * dg + db * db <= tol * tol) return true;
        }
        return false;
    }

    static void PaletteCompliance()
    {
        var guide = new HashSet<string>(guidePalette);
        var enemies = new HashSet<string>(enemyOnly);
        foreach (int id in ShipId.All)
        {
            var r = ShipHullArt.RectFor(id);
            for (int n = 0; n < ShipSkins.CountFor(id); n++)
            {
                var s = ShipSkins.Get(id, n);
                bool ok = true, friendly = true;
                foreach (var hex in new[] { s.hue, s.hueShadow, s.hueHighlight, s.accent, s.accentShadow, s.accentHighlight })
                {
                    if (hex == null) continue;
                    ok &= guide.Contains(hex) || hex == r.hue || hex == r.hueShadow || hex == r.hueHighlight;
                    friendly &= !enemies.Contains(hex);
                }
                Check(id + " " + s.name + " colours are guide palette (or the ship's own hue)", ok);
                Check(id + " " + s.name + " wears no enemy colour", friendly);
            }
            // the rendered pixels: flat fills only in guide + identity hue
            for (int n = 1; n < ShipSkins.CountFor(id); n++)
            {
                var png = Decode(ShipHullArt.SkinPng(id, n));
                if (png == null) continue;
                var palette = Palette(guidePalette);
                palette.AddRange(Palette(r.hue, r.hueShadow, r.hueHighlight));
                var px = png.GetPixels32();
                int w = png.width, h = png.height, flat = 0, off = 0;
                for (int y = 1; y < h - 1; y += 2)
                    for (int x = 1; x < w - 1; x += 2)
                    {
                        var c = px[y * w + x];
                        if (c.a < 250) continue;
                        bool uniform = true;
                        for (int dy = -1; dy <= 1 && uniform; dy++)
                            for (int dx = -1; dx <= 1 && uniform; dx++)
                            {
                                var q = px[(y + dy) * w + x + dx];
                                uniform = q.a >= 250 && Mathf.Abs(q.r - c.r) + Mathf.Abs(q.g - c.g) + Mathf.Abs(q.b - c.b) <= 12;
                            }
                        if (!uniform) continue;
                        flat++;
                        if (!Near(c, palette, 12)) off++;
                    }
                Check(id + " " + ShipSkins.Get(id, n).name + " flat fills are palette (" + off + " of " + flat + " off)",
                      flat > 0 && off <= flat / 200);
                // friendly red stays on every skin (lights / trim)
                var red = Palette("#D8232C");
                bool hasRed = false;
                for (int i = 0; i < px.Length && !hasRed; i += 3) hasRed = px[i].a >= 250 && Near(px[i], red, 6);
                Check(id + " " + ShipSkins.Get(id, n).name + " keeps the friendly red accent", hasRed);
                Object.DestroyImmediate(png);
            }
        }
    }

    // ------------------------------------------------------------- economy

    static void Purchases()
    {
        ResetSkins();
        Own(3);
        int id = 3, skin = 2;
        float price = ShipSkins.PriceOf(id, skin);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, price + 40f);
        int saves = PrefsSaver.SaveCount;
        var result = ShipSkins.TryPurchase(id, skin);
        Check("a skin purchase succeeds with enough dust", result == ShipSkins.PurchaseResult.Bought);
        Check("buying a skin deducts its price exactly once",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 40f));
        Check("buying writes skinOwned<id>_<n>", PlayerPrefs.GetInt("skinOwned3_2", 0) == 1);
        Check("buying equips it (shipSkin<id>)", PlayerPrefs.GetInt("shipSkin3", 0) == 2 && ShipSkins.Equipped(3) == 2);
        Check("buying saves immediately", PrefsSaver.SaveCount > saves);
        Check("buying an owned skin again is refused and charges nothing",
              ShipSkins.TryPurchase(id, skin) == ShipSkins.PurchaseResult.AlreadyOwned &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 40f));

        float special = ShipSkins.PriceOf(id, ShipSkins.Special);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, special - 1f);
        saves = PrefsSaver.SaveCount;
        Check("can't buy a skin without enough star dust",
              ShipSkins.TryPurchase(id, ShipSkins.Special) == ShipSkins.PurchaseResult.CantAfford);
        Check("a refused purchase takes no dust and owns nothing",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), special - 1f) &&
              !PlayerPrefs.HasKey("skinOwned3_4") && ShipSkins.Equipped(3) == 2);
        Check("the shortfall is reported", Mathf.Approximately(ShipSkins.ShortBy(id, ShipSkins.Special), 1f));
        Check("the stock skin is free and always owned", ShipSkins.IsOwned(3, 0) && ShipSkins.PriceOf(3, 0) == 0f);

        PlayerPrefs.DeleteKey(ShipId.OwnedKey(9));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 99999f);
        Check("skins of a ship you don't own can't be bought",
              ShipSkins.TryPurchase(9, 1) == ShipSkins.PurchaseResult.Invalid &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 99999f));
    }

    static void EquipPerShip()
    {
        ResetSkins();
        Own(2, 5);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 3), 1);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(5, 1), 1);
        int saves = PrefsSaver.SaveCount;
        Check("equipping an owned skin succeeds", ShipSkins.Equip(2, 3) && ShipSkins.Equip(5, 1));
        Check("equipping saves immediately", PrefsSaver.SaveCount > saves);
        Check("equipped skins persist per ship",
              PlayerPrefs.GetInt("shipSkin2") == 3 && PlayerPrefs.GetInt("shipSkin5") == 1 &&
              ShipSkins.Equipped(2) == 3 && ShipSkins.Equipped(5) == 1 && ShipSkins.Equipped(4) == 0);
        Check("a locked skin can't be equipped", !ShipSkins.Equip(2, 4) && ShipSkins.Equipped(2) == 3);
        PlayerPrefs.SetInt("shipSkin7", 2);
        Check("a saved but unowned skin falls back to stock", ShipSkins.Equipped(7) == 0);
        ShipSkins.Equip(2, 0);
        Check("re-equipping stock clears the key", !PlayerPrefs.HasKey("shipSkin2") && ShipSkins.Equipped(2) == 0);
    }

    static void ApplyHullUsesEquipped()
    {
        ResetSkins();
        Own(2);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 3), 1);
        ShipSkins.Equip(2, 3);
        var go = new GameObject("ship2(Clone)");
        go.AddComponent<SpriteRenderer>();
        go.AddComponent<BoxCollider2D>();
        spawnShips.ApplyHull(go, 2);
        var sr = go.GetComponent<SpriteRenderer>();
        string sheet = ShipSkins.SheetName(2, 3);
        Check("ApplyHull dresses the ship in its equipped skin (" + (sr.sprite != null ? sr.sprite.texture.name : "none") + ")",
              sr.sprite != null && sr.sprite.texture.name == sheet);
        float stockScale = shopingShips.NormalizedHullScale(ShipHullArt.Get(2, 0, 0, 0));
        Check("a skinned hull keeps the stock size", Mathf.Abs(go.transform.localScale.x - stockScale) < .0001f);
        var anim = new ShipHullAnimator(2, go.transform, 0);
        Check("the flying flipbook uses the equipped skin", anim.Current(1).texture.name == sheet);
        Check("damage art comes from the equipped skin", shopingShips.DamageSpritesFor(2)[2].texture.name == sheet);
        Object.DestroyImmediate(go);

        var other = new GameObject("ship4(Clone)");
        other.AddComponent<SpriteRenderer>();
        spawnShips.ApplyHull(other, 4);
        Check("a ship with no skin equipped flies stock",
              other.GetComponent<SpriteRenderer>().sprite.texture.name == ShipId.KeyOf(4));
        Object.DestroyImmediate(other);
        Check("the codex keeps the stock art", ShipHullArt.StockRest(2).texture.name == ShipId.KeyOf(2));
    }

    static void LazyLoading()
    {
        ResetSkins();
        Own(2, 6);
        Check("nothing equipped: no skin sheet is decoded", ShipHullArt.LoadedSkinSheets == 0);
        foreach (int id in ShipId.All) ShipHullArt.Get(id, 0, 0);
        Check("drawing every stock ship decodes no skin sheet", ShipHullArt.LoadedSkinSheets == 0);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 1), 1);
        ShipSkins.Equip(2, 1);
        ShipHullArt.Get(2, 0, 0);
        Check("an equipped skin decodes exactly its own sheet",
              ShipHullArt.LoadedSkinSheets == 1 && ShipHullArt.IsSkinLoaded(2, 1));
        ShipSkins.SetPreview(6, 4);
        ShipHullArt.Get(6, 0, 0);
        Check("a preview decodes only the previewed sheet", ShipHullArt.LoadedSkinSheets == 2 && ShipHullArt.IsSkinLoaded(6, 4));
        var sheet = ShipHullArt.SheetFor(6, 4);
        Check("decoded sheets are GPU-compressed with mips and no CPU copy",
              sheet != null && sheet.mipmapCount > 1 && !sheet.isReadable && sheet.format != TextureFormat.RGBA32);
        ShipSkins.ClearPreview();
        ShipHullArt.ReleaseUnusedSkins();
        Check("ending the preview releases its sheet, the equipped one stays",
              ShipHullArt.LoadedSkinSheets == 1 && !ShipHullArt.IsSkinLoaded(6, 4) && ShipHullArt.IsSkinLoaded(2, 1));
        ShipSkins.Equip(2, 0);
        ShipHullArt.ReleaseUnusedSkins();
        Check("back to stock releases the last skin sheet", ShipHullArt.LoadedSkinSheets == 0);
    }

    static void SkinHueApi()
    {
        ResetSkins();
        Own(5);
        var identity = ShipHullArt.IdentityHue(5);
        Check("SkinHue is the identity hue with stock on", ShipHullArt.SkinHue(5)[0] == identity[0]);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(5, 2), 1);
        int raised = 0;
        System.Action<int> onChanged = id => { if (id == 5) raised++; };
        ShipSkins.Changed += onChanged;
        ShipSkins.Equip(5, 2);
        ShipSkins.Changed -= onChanged;
        Check("ShipSkins.Changed fires on equip", raised == 1);
        Check("SkinHue follows the equipped skin",
              ShipHullArt.SkinHue(5)[0] == ShipSkins.Get(5, 2).primary && ShipSkins.Current(5).primary == ShipSkins.Get(5, 2).primary);
        Check("IdentityHue still returns the stock hue", ShipHullArt.IdentityHue(5)[0] == identity[0]);
        ShipSkins.SetPreview(5, 4);
        Check("SkinHue follows a dock preview", ShipSkins.Current(5).accentColor == ShipSkins.Get(5, 4).accentColor);
        ShipSkins.ClearPreview();
        Check("SkinHue(id, skin) reads any skin", ShipHullArt.SkinHue(5, 1)[0] == ShipSkins.Get(5, 1).primary);
    }

    // ------------------------------------------------------------- dev mode

    static void DeveloperMode()
    {
        ResetSkins();
        Own(4);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(4, 1), 1);
        ShipSkins.Equip(4, 1);
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        DeveloperUnlocks.SetEnabled(true);
        bool all = true;
        foreach (int id in ShipId.All)
            for (int n = 0; n < ShipSkins.PerShip; n++) all &= ShipSkins.IsOwned(id, n);
        Check("dev mode shows every skin as owned", all);
        bool noKeys = true;
        foreach (int id in ShipId.All)
            for (int n = 1; n < ShipSkins.PerShip; n++)
                noKeys &= PlayerPrefs.HasKey(ShipSkins.OwnedKey(id, n)) == (id == 4 && n == 1);
        Check("dev mode writes no skinOwned key", noKeys);
        Check("dev mode can't buy (nothing to charge)",
              ShipSkins.TryPurchase(4, 3) == ShipSkins.PurchaseResult.AlreadyOwned);
        ShipSkins.Equip(4, 4);
        ShipSkins.Equip(9, 2);
        Check("dev mode equips show at once", ShipSkins.Equipped(4) == 4 && ShipSkins.Equipped(9) == 2);
        Check("dev mode equips don't touch the real keys",
              PlayerPrefs.GetInt("shipSkin4") == 1 && !PlayerPrefs.HasKey("shipSkin9") && !PlayerPrefs.HasKey("skinOwned4_4"));
        Check("the cloud snapshot sees the real skins during dev mode",
              Contains(ProgressSnapshot.Capture(1).equippedSkins, 4 * ProgressSnapshot.SkinCode + 1) &&
              ProgressSnapshot.Capture(1).ownedSkins.Length == 1);
        DeveloperUnlocks.SetEnabled(false);
        Check("dev mode off restores the real skins",
              ShipSkins.Equipped(4) == 1 && ShipSkins.Equipped(9) == 0 && !ShipSkins.IsOwned(4, 4) &&
              ShipSkins.IsOwned(4, 1) && !PlayerPrefs.HasKey(ShipSkins.DeveloperEquippedKey(4)));
    }

    static bool Contains(int[] list, int v)
    {
        foreach (int x in list ?? new int[0]) if (x == v) return true;
        return false;
    }

    // ------------------------------------------------------------- cloud

    static void CloudMerge()
    {
        var local = new ProgressSnapshot { savedAtUtc = 100, ownedSkins = new[] { 201, 304 }, equippedSkins = new[] { 201 } };
        var cloud = new ProgressSnapshot { savedAtUtc = 200, ownedSkins = new[] { 304, 502 }, equippedSkins = new[] { 502 } };
        var m = ProgressMerge.Merge(local, cloud);
        Check("cloud merge unions owned skins",
              m.ownedSkins.Length == 3 && Contains(m.ownedSkins, 201) && Contains(m.ownedSkins, 304) && Contains(m.ownedSkins, 502));
        Check("cloud merge takes equipped skins from the newer save",
              m.equippedSkins.Length == 1 && m.equippedSkins[0] == 502);
        local.savedAtUtc = 300;
        Check("...whichever side is newer", ProgressMerge.Merge(local, cloud).equippedSkins[0] == 201);

        ResetSkins();
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 1), 1);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(3, 4), 1);
        Own(2, 3);
        ShipSkins.Equip(3, 4);
        var snap = ProgressSnapshot.Capture(5);
        string json = snap.ToJson();
        ProgressSnapshot parsed;
        Check("skins survive the JSON round trip",
              ProgressSnapshot.TryParse(json, out parsed) == ProgressSnapshot.ParseResult.Ok &&
              Contains(parsed.ownedSkins, 201) && Contains(parsed.ownedSkins, 304) && Contains(parsed.equippedSkins, 304));
        ResetSkins();
        parsed.Apply();
        Check("Apply writes the skin keys back",
              PlayerPrefs.GetInt("skinOwned2_1") == 1 && PlayerPrefs.GetInt("skinOwned3_4") == 1 &&
              PlayerPrefs.GetInt("shipSkin3") == 4 && !PlayerPrefs.HasKey("shipSkin2"));
        ProgressSnapshot old;
        ProgressSnapshot.TryParse("{\"schemaVersion\":1,\"savedAtUtc\":3,\"currency\":5}", out old);
        Check("a save from before skins parses as all stock", old != null && old.ownedSkins.Length == 0 && old.equippedSkins.Length == 0);
    }

    // ------------------------------------------------------------- dock

    static void DockFlow()
    {
        ResetSkins();
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        Own(3);
        PlayerPrefs.SetInt("spawnShip", 3);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(3, 1), 1);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("dock built", dock != null);
        if (dock == null) return;
        var cam = Camera.main;
        cam.aspect = 1080f / 1920f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, 1080, 1920);
        dock.Relayout();
        var popup = dock.popup;

        dock.Select(5);
        Check("an unowned ship shows BUY for the ship and no skin row",
              popup.CurrentMode == DockPopup.Mode.Buy && !popup.SkinRowVisible && dock.bays[5].Skin == 0);
        dock.Select(3);
        popup.SendMessage("LateUpdate");
        Check("an owned ship's popup gains the skin row", popup.SkinRowVisible && popup.CurrentMode == DockPopup.Mode.Launch);
        Check("the swatch row has one chip per skin", popup.swatches.Length == ShipSkins.PerShip);
        Check("owned swatches have a check, locked ones a price",
              popup.swatches[0].check.enabled && popup.swatches[1].check.enabled && !popup.swatches[1].price.enabled &&
              !popup.swatches[2].check.enabled && popup.swatches[2].price.enabled &&
              popup.swatches[2].price.text == "300" && popup.swatches[4].price.text == "750" && popup.swatches[2].dust.enabled);
        Check("swatches are in the skin's own colours",
              popup.swatches[1].body.color == ShipSkins.Get(3, 1).primary && popup.swatches[4].stripe.enabled &&
              !popup.swatches[2].stripe.enabled);
        Check("the swatches have cel press feedback", popup.swatches[3].root.GetComponent<CelPress>() != null);
        Rect r = popup.WorldRect;
        Check("the taller popup still fits the dock's safe view",
              r.yMin >= popup.safeView.yMin - .001f && r.yMax <= popup.safeView.yMax + .001f);

        // owned skin: tap to equip, saved at once
        int saves = PrefsSaver.SaveCount;
        popup.TapSwatch(1);
        Check("tapping an owned skin equips it and saves",
              PlayerPrefs.GetInt("shipSkin3") == 1 && PrefsSaver.SaveCount > saves && popup.CurrentMode == DockPopup.Mode.Launch);
        Check("the berth's hull repaints in it", dock.bays[3].Skin == 1 &&
              dock.bays[3].hull.sprite.texture.name == ShipSkins.SheetName(3, 1));

        // locked skin: preview
        popup.TapSwatch(4);
        Check("tapping a locked skin previews it on the hull",
              dock.bays[3].Skin == 4 && dock.bays[3].hull.sprite.texture.name == ShipSkins.SheetName(3, 4) && dock.PreviewingSkin);
        Check("a previewed locked skin offers BUY with its price",
              popup.CurrentMode == DockPopup.Mode.BuySkin && popup.SkinShown == 4 && popup.swatches[4].ring.enabled);
        Check("previewing writes nothing", PlayerPrefs.GetInt("shipSkin3") == 1 && !PlayerPrefs.HasKey("skinOwned3_4"));
        dock.bays[3].SnapPower();
        Check("the previewed hull stays powered up", dock.bays[3].Powered);

        // can't afford
        popup.Press();
        var message = popup.transform.Find("Panel/Message").GetComponent<Text>();
        Check("an unaffordable skin shakes NEED n MORE",
              message.enabled && message.text == "NEED 650 MORE" && !PlayerPrefs.HasKey("skinOwned3_4") &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 100f));

        // Back: revert preview, then deselect, then home
        var loads = new List<string>();
        BackNavigator.LoadScene = s => loads.Add(s);
        try
        {
            BackNavigator.Back();
            Check("Back leaves the preview: the equipped skin is back on the hull",
                  !dock.PreviewingSkin && dock.bays[3].Skin == 1 && dock.Selected == 3 && popup.Visible &&
                  popup.CurrentMode == DockPopup.Mode.Launch && loads.Count == 0);
            BackNavigator.Back();
            Check("the next Back deselects the ship", dock.Selected == 0 && !popup.Visible && loads.Count == 0);
            BackNavigator.Back();
            Check("the next Back goes home", loads.Count == 1 && loads[0] == BackNavigator.HomeScene);
        }
        finally { BackNavigator.ResetHooks(); }

        // previewing then leaving the ship also reverts
        dock.Select(3);
        popup.TapSwatch(2);
        dock.Tap(new Vector3(500f, 500f, 0f));
        Check("tapping away from a preview reverts it", !ShipSkins.IsPreviewing(3) && dock.bays[3].Skin == 1);

        // buy with enough dust
        dock.Select(3);
        popup.TapSwatch(2);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000f);
        saves = PrefsSaver.SaveCount;
        popup.Press();
        Check("BUY on a previewed skin deducts once, owns, equips and saves",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 700f) &&
              PlayerPrefs.GetInt("skinOwned3_2") == 1 && PlayerPrefs.GetInt("shipSkin3") == 2 &&
              PrefsSaver.SaveCount > saves && dock.bays[3].Skin == 2 && !dock.PreviewingSkin);
        dock.Buy(3);
        dock.BuySkin(3, 2);
        Check("buying again never charges twice", Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 700f));
        Check("only the shown skin sheets stay decoded", ShipHullArt.LoadedSkinSheets == 1);

        // dev mode in the dock
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        DeveloperUnlocks.SetEnabled(true);
        dock.Select(9);
        bool allOwned = popup.SkinRowVisible;
        foreach (var w in popup.swatches) allOwned &= w.owned && w.check.enabled && !w.price.enabled;
        Check("dev mode: every swatch shows owned", allOwned);
        popup.TapSwatch(3);
        Check("dev mode: equips in the dock without real keys", dock.bays[9].Skin == 3 && !PlayerPrefs.HasKey("shipSkin9"));
        DeveloperUnlocks.SetEnabled(false);
        Check("dev mode off: the berth goes back to its real skin", dock.bays[9].Skin == 0);

        // launch flies the equipped skin, never a preview
        PlayerPrefs.SetString("HasDoneTut", "true");
        dock.Select(3);
        popup.TapSwatch(4);
        dock.LiftOff();
        Check("launching ends an unbought preview", !ShipSkins.IsPreviewing(3) && dock.bays[3].Skin == 2);
    }
}
