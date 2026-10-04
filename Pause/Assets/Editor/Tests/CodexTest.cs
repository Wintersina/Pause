using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The codex: catalogue completeness, discovery persistence, the hidden state
// of undiscovered entries, the home-screen entry point and the panel's layout on
// a range of screens. Edit mode only -- the panel and button are built
// directly, the same way Awake would build them in Play mode.
public static class CodexTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CDX] PASS  " : "[CDX] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.DeleteKey(Codex.PrefsKey);
        Codex.Reload();

        CheckCatalogue();
        CheckSpawnerCoverage();
        CheckDiscovery();
        CheckDeveloperMode();
        CheckHooksAndSources();
        CheckLayoutMath();
        CheckHomeAndPanel();

        Debug.Log("[CDX] failures: " + fails);
        return fails;
    }

    // ---- The table itself ----

    static void CheckCatalogue()
    {
        var entries = Codex.Entries;
        Check("catalogue has entries (" + entries.Length + ")", entries.Length > 30);

        var ids = new HashSet<string>();
        foreach (var e in entries)
        {
            Check(e.id + " id is unique", ids.Add(e.id));
            Check(e.id + " has a name", !string.IsNullOrEmpty(e.name) && e.name.Trim().Length > 0);
            Check(e.id + " has lore", !string.IsNullOrEmpty(e.lore) && e.lore.Trim().Length > 20);
            Check(e.id + " lore is short (<= 3 sentences, log excepted)",
                  e.category == CodexCategory.Log || SentenceCount(e.lore) <= 3);
            Check(e.id + " sprite resolves", e.Sprite != null);
            foreach (string m in e.matches)
                Check(e.id + " match '" + m + "' resolves back to it", Codex.IdForName(m) == e.id);
        }

        // Each entry should show art the player can tell apart: identical
        // image content (even under different file names) means one entry.
        var seenArt = new Dictionary<string, string>();
        foreach (var e in entries)
        {
            var sprite = e.Sprite;
            // The Hunter is defined by behaviour and may share a hull with its family.
            if (sprite == null || e.category == CodexCategory.Ships || e.category == CodexCategory.Log || e.id == "enemy_chaser") continue;
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            string content = string.IsNullOrEmpty(path) ? sprite.texture.GetInstanceID().ToString() : Hash(path);
            string key = content + "|" + sprite.rect;
            string other;
            Check(e.id + " has art distinct from other entries" + (seenArt.TryGetValue(key, out other) ? " (same as " + other + ")" : ""),
                  !seenArt.ContainsKey(key));
            seenArt[key] = e.id;
        }

        foreach (CodexCategory c in Enum.GetValues(typeof(CodexCategory)))
        {
            int total;
            Codex.DiscoveredIn(c, out total);
            Check(c + " category is populated (" + total + ")", total > 0);
        }
        Check("Pilot's Log entry exists", Codex.Find("log_pilot") != null);
        Check("every shop ship has an entry", CountIn(CodexCategory.Ships) == ShipId.Count);
        foreach (int ship in ShipId.All)
        {
            var e = Codex.Find(CodexCatalogue.ShipPrefix + ShipId.KeyOf(ship));
            Check("ship " + ShipId.KeyOf(ship) + " entry uses its roster name and id",
                  e != null && e.name == ShipId.NameOf(ship) && CodexCatalogue.ShipIndex(e.id) == ship);
        }
        Check("every world has an entry", CountIn(CodexCategory.Worlds) == WorldManager.Worlds.Length + 1);
        Check("the portal is an entry", Codex.Find(CodexCatalogue.PortalId) != null);
        Check("Pilot's Log lore tells the premise",
              Codex.Find("log_pilot").lore.Contains("black hole") && Codex.Find("log_pilot").lore.Contains("wormhole") &&
              Codex.Find("log_pilot").lore.Contains("Home"));
        Check("the green atom's lore says it repairs the hull", Codex.Find("atom_green").lore.Contains("hull"));
    }

    // ---- Everything the spawners can produce has an entry ----

    static void CheckSpawnerCoverage()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var spawner = UnityEngine.Object.FindFirstObjectByType<enmiesOnBoard>();
        Check("gameS1 has the enemy spawner", spawner != null);
        if (spawner != null)
            CheckMaps("enmiesOnBoard alien", spawner.alien1, "enemy_alien");

        // Every world's roster (EnemyRoster) -- what enmiesOnBoard actually
        // spawns -- maps to its own entry, built exactly as the spawner builds
        // it, in all four worlds (aliens and mines share their legacy object
        // names, so the per-world entry comes from EnemyIdentity).
        var built = new List<GameObject>();
        for (int world = 0; world < WorldManager.Worlds.Length; world++)
        {
            int roles = 0;
            foreach (EnemyRole role in Enum.GetValues(typeof(EnemyRole)))
                foreach (var def in EnemyRoster.For(world, role))
                {
                    roles++;
                    var go = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
                    go.name += "(Clone)";
                    built.Add(go);
                    CheckMaps(WorldManager.Worlds[world].displayName + " " + role + " " + def.key, go, def.codexId);
                    var e = Codex.Find(def.codexId);
                    Check(def.codexId + " is named and filed as its roster entry",
                          e != null && e.name == def.displayName &&
                          e.category == (def.IsHazard ? CodexCategory.Hazards : CodexCategory.Enemies));
                }
            Check(WorldManager.Worlds[world].displayName + " roster is covered (" + roles + " enemies)", roles >= 6);
        }
        foreach (var go in built) UnityEngine.Object.DestroyImmediate(go);
        Check("every codex enemy/hazard entry is a roster enemy",
              Array.TrueForAll(Codex.Entries, e =>
                  (e.category != CodexCategory.Enemies && e.category != CodexCategory.Hazards) ||
                  EnemyRoster.FindByCodexId(e.id) != null ||
                  BossCatalog.Find(e.id) != null));   // the secret end-of-level bosses
        foreach (string gone in new[] { "hazard_meteor_tiny", "hazard_meteor_small", "hazard_meteor_med",
                                        "enemy_black", "enemy_blue", "enemy_green", "enemy_red" })
            Check("retired entry " + gone + " is gone", Codex.Find(gone) == null);
        Check("family key drops the variant number", CodexCatalogue.FamilyOf("kn_enemyBlack3(Clone)") == "knenemyblack" &&
              CodexCatalogue.FamilyOf("kn_meteorGrey_big2") == "knmeteorgreybig");
        Check("an unknown family still gets a readable name", CodexCatalogue.FallbackName("kn_enemyPurple2") == "Enemy Purple");

        var goods = UnityEngine.Object.FindFirstObjectByType<spawnGoodStuff>();
        Check("gameS1 has the pickup spawner", goods != null);
        if (goods != null)
        {
            CheckMaps("small star dust", goods.smStar, "atom_stardust");
            CheckMaps("large star", goods.midStar, "atom_bigstar");
            CheckMaps("blue atom", goods.Atom, "atom_blue");
            CheckMaps("red atom", goods.redAtom, "atom_red");
        }

        // Runtime-built objects, named exactly as their spawners name them.
        var temp = new List<GameObject>();
        temp.Add(new GameObject(HealAtom.ObjectName));
        CheckMaps("green heal atom", temp[temp.Count - 1], "atom_green");
        temp.Add(new GameObject("mine"));
        CheckMaps("rail mine (enmiesOnBoard.spawnMine)", temp[temp.Count - 1], "hazard_mine");
        temp.Add(new GameObject("~Portal"));
        CheckMaps("portal (Portal.Spawn)", temp[temp.Count - 1], CodexCatalogue.PortalId);
        // A chaser built from any other hull (an inspector override) is still
        // the Space chaser entry.
        var chaser = new GameObject("someHull(Clone)");
        chaser.AddComponent<ChaserEnemy>();
        temp.Add(chaser);
        CheckMaps("chaser (override hull + ChaserEnemy)", chaser, "enemy_chaser");
        var clone = new GameObject("smStar_1(Clone)");
        temp.Add(clone);
        CheckMaps("a (Clone) suffix is ignored", clone, "atom_stardust");
        temp.Add(new GameObject("explosion_0(Clone)"));
        Check("unrelated objects map to nothing", Codex.IdFor(temp[temp.Count - 1]) == null);
        foreach (var go in temp) UnityEngine.Object.DestroyImmediate(go);
    }

    static void CheckMaps(string what, GameObject go, string expected)
    {
        string id = go != null ? Codex.IdFor(go) : null;
        bool ok = id != null && Codex.Find(id) != null && (expected == null || id == expected);
        Check(what + " -> codex entry (" + (id ?? "none") + ")", ok);
    }

    // ---- Discovery: persistence, idempotence, derived unlocks, tutorial ----

    static void CheckDiscovery()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.DeleteKey(Codex.PrefsKey);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        Codex.Reload();

        int events = 0;
        string lastEvent = null;
        Action<CodexEntry> handler = e => { events++; lastEvent = e.id; };
        Codex.Discovered += handler;
        try
        {
            var black = new GameObject("space_fighter_1(Clone)");
            Check("fresh profile: enemy undiscovered", !Codex.IsDiscovered("enemy_space_fighter_1"));
            Check("first contact discovers", Codex.Discover(black));
            Check("discovery raised one event for the right entry", events == 1 && lastEvent == "enemy_space_fighter_1");
            Check("second contact is a no-op", !Codex.Discover(black) && !Codex.Discover("enemy_space_fighter_1"));
            Check("no duplicate event", events == 1);
            UnityEngine.Object.DestroyImmediate(black);

            Check("discovery is written to PlayerPrefs '" + Codex.PrefsKey + "'",
                  PlayerPrefs.GetString(Codex.PrefsKey) == "enemy_space_fighter_1");
            Check("discovery marks the batched saver dirty", PrefsSaver.Dirty);
            Codex.Discover("atom_red");
            Check("list is comma separated, in discovery order",
                  PlayerPrefs.GetString(Codex.PrefsKey) == "enemy_space_fighter_1,atom_red");

            Codex.Reload();
            Check("discoveries survive a reload", Codex.IsDiscovered("enemy_space_fighter_1") && Codex.IsDiscovered("atom_red"));
            Check("a reload does not duplicate ids", PlayerPrefs.GetString(Codex.PrefsKey) == "enemy_space_fighter_1,atom_red");
            Check("still idempotent after a reload", !Codex.Discover("atom_red"));

            PlayerPrefs.SetString(Codex.PrefsKey, "hazard_mine, ,some_future_id,hazard_mine");
            Codex.Reload();
            Check("blank and duplicate ids in the saved list are ignored", Codex.IsDiscovered("hazard_mine"));
            Codex.Discover("atom_blue");
            Check("unknown ids from a newer build are kept",
                  PlayerPrefs.GetString(Codex.PrefsKey) == "hazard_mine,some_future_id,atom_blue");

            Check("unknown ids are ignored", !Codex.Discover("not_a_thing") && !Codex.Discover((string)null));
            Check("a null object is ignored", !Codex.Discover((GameObject)null));

            Check("Pilot's Log is unlocked from the start", Codex.IsDiscovered("log_pilot") && Codex.IsDiscovered("log_wormhole"));
            Check("the starter ship is owned, so discovered", Codex.IsDiscovered(CodexCatalogue.ShipPrefix + ShipId.KeyOf(ShipId.Starter)));
            Check("an unbought ship is not", !Codex.IsDiscovered(CodexCatalogue.ShipPrefix + ShipId.KeyOf(5)));
            PlayerPrefs.SetString("boughtship5", "True");
            Check("owning a ship discovers it", Codex.IsDiscovered(CodexCatalogue.ShipPrefix + ShipId.KeyOf(5)));

            Check("Frost undiscovered before reaching it", !Codex.IsDiscovered("world_frost") && !Codex.IsDiscovered(CodexCatalogue.PortalId));
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 2);
            Check("worlds already reached count (progress from before the codex)",
                  Codex.IsDiscovered("world_frost") && Codex.IsDiscovered("world_verdant") &&
                  !Codex.IsDiscovered("world_ember") && Codex.IsDiscovered(CodexCatalogue.PortalId));
            Check("entering a world discovers it (WorldManager hook id)", Codex.Discover(Codex.WorldId(3)) && Codex.IsDiscovered("world_ember"));

            int before = Codex.DiscoveredCount;
            Codex.Discover("hazard_rock_dark");
            Check("count goes up by one per discovery", Codex.DiscoveredCount == before + 1);
            Check("count never exceeds the total", Codex.DiscoveredCount <= Codex.Total);

            // The tutorial is practice: nothing met there counts.
            EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
            int evBefore = events;
            Check("tutorial contact does not discover", !Codex.Discover("enemy_space_fighter_2") && !Codex.IsDiscovered("enemy_space_fighter_2"));
            Check("tutorial contact raises no toast event", events == evBefore);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Check("... and the same contact counts in a real run", Codex.Discover("enemy_space_fighter_2"));

            // Locked entries keep their secrets.
            var locked = Codex.Find("enemy_space_fighter_4");
            Check("undiscovered: name hidden", Codex.DisplayName(locked) == Codex.LockedName && Codex.LockedName == "???");
            Check("undiscovered: lore hidden", Codex.DisplayLore(locked) == string.Empty);
            var found = Codex.Find("enemy_space_fighter_2");
            Check("discovered: real name and lore", Codex.DisplayName(found) == found.name && Codex.DisplayLore(found) == found.lore);
        }
        finally
        {
            Codex.Discovered -= handler;
        }
    }

    // ---- Developer mode: everything visible, nothing written ----

    static void CheckDeveloperMode()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString(Codex.PrefsKey, "enemy_space_fighter_1,atom_red");
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        Codex.Reload();
        int realCount = Codex.DiscoveredCount;
        Check("dev mode off: only the real set is discovered", realCount < Codex.Total && !Codex.IsDiscovered("enemy_space_fighter_4"));

        int events = 0;
        Action<CodexEntry> handler = e => events++;
        Codex.Discovered += handler;
        try
        {
            DeveloperUnlocks.SetEnabled(true);
            bool all = true;
            foreach (var e in Codex.Entries) all &= Codex.IsDiscovered(e) && Codex.DisplayName(e) == e.name && Codex.DisplayLore(e) == e.lore;
            Check("dev mode on: every entry is visible", all);
            Check("dev mode on: counter is N/N", Codex.DiscoveredCount == Codex.Total);
            Check("dev mode on: codexSeen is not rewritten", PlayerPrefs.GetString(Codex.PrefsKey) == "enemy_space_fighter_1,atom_red");
            Check("dev mode on: contacts record nothing and toast nothing",
                  !Codex.Discover("enemy_space_fighter_3") && events == 0 && PlayerPrefs.GetString(Codex.PrefsKey) == "enemy_space_fighter_1,atom_red");

            DeveloperUnlocks.SetEnabled(false);
            Codex.Reload();
            Check("dev mode off: back to the real discoveries", Codex.DiscoveredCount == realCount &&
                  Codex.IsDiscovered("enemy_space_fighter_1") && !Codex.IsDiscovered("enemy_space_fighter_4") && !Codex.IsDiscovered("enemy_space_fighter_3"));
            Check("dev mode off: codexSeen unchanged by the round trip", PlayerPrefs.GetString(Codex.PrefsKey) == "enemy_space_fighter_1,atom_red");
            Check("dev mode off: discovery works again", Codex.Discover("enemy_space_fighter_3") && events == 1);
        }
        finally
        {
            Codex.Discovered -= handler;
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        }
    }

    // ---- The one-line hooks and the animation sources ----

    static void CheckHooksAndSources()
    {
        string collision = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("collision hook: one line on every trigger", Regex.Matches(collision, @"Codex\.Discover\(hit\.gameObject\);").Count == 1);
        Check("ultimate-kill hook: one line in AwardDestroyedTarget", Regex.Matches(collision, @"Codex\.Discover\(target\);").Count == 1);
        string worlds = File.ReadAllText("Assets/Scripts/Worlds/WorldManager.cs");
        Check("world hooks on start and on advance",
              Regex.Matches(worlds, @"Codex\.Discover\(Codex\.WorldId\(CurrentIndex\)\);").Count == 2);

        var scaled = new Regex(@"Time\.(time|deltaTime|fixedDeltaTime|smoothDeltaTime)\b|WaitForSeconds\(");
        foreach (var file in new[] { "CodexPanel.cs", "CodexToast.cs", "CodexHomeButton.cs", "CodexUi.cs" })
        {
            string src = File.ReadAllText("Assets/Scripts/Codex/" + file);
            Check(file + " never reads scaled time", !scaled.IsMatch(src));
        }
        Check("CodexPanel animates on unscaled time", File.ReadAllText("Assets/Scripts/Codex/CodexPanel.cs").Contains("Time.unscaledTime"));
        Check("CodexToast animates on unscaled time", File.ReadAllText("Assets/Scripts/Codex/CodexToast.cs").Contains("Time.unscaledTime"));
    }

    // ---- Pure layout across screens ----

    static readonly (string name, Rect safe)[] Screens =
    {
        ("minimum 800x1280", new Rect(-400f, -640f, 800f, 1280f)),
        ("16:9 phone 1080x1920", new Rect(-400f, -711f, 800f, 1422f)),
        ("tall phone 1080x2340", new Rect(-400f, -866f, 800f, 1733f)),
        ("notch + home bar", new Rect(-400f, -835f, 800f, 1640f)),
        ("iPad 1536x2048", new Rect(-480f, -640f, 960f, 1280f)),
        ("landscape Mac 1600x900", new Rect(-1138f, -640f, 2276f, 1280f)),
        ("off-centre safe area", new Rect(-380f, -700f, 760f, 1500f)),
    };

    static void CheckLayoutMath()
    {
        foreach (var (name, safe) in Screens)
        {
            var l = CodexPanel.ComputeLayout(safe);
            var visual = Inset(l.panel, -CodexPanel.GlowMargin);
            Check(name + ": panel and glow fit the safe area", Contains(safe, visual));
            Check(name + ": panel is a usable size", l.panel.width >= 680f && l.panel.height >= 1100f);

            var local = new Rect(-l.panel.width * .5f, -l.panel.height * .5f, l.panel.width, l.panel.height);
            var rows = new[] { ("header", l.header), ("divider", l.divider), ("tabs", l.tabs), ("grid", l.body), ("back", l.back) };
            foreach (var (rn, r) in rows)
                Check(name + ": " + rn + " inside the panel", Contains(local, r));
            for (int i = 0; i < rows.Length; i++)
                for (int j = i + 1; j < rows.Length; j++)
                    Check(name + ": " + rows[i].Item1 + " clear of " + rows[j].Item1, !rows[i].Item2.Overlaps(rows[j].Item2));
            Check(name + ": detail inside the panel", Contains(local, l.detail));
            Check(name + ": detail clear of header and back", !l.detail.Overlaps(l.header) && !l.detail.Overlaps(l.back));

            float gridW = l.body.width - 2f * CodexPanel.GridInset;
            Check(name + ": >= 2 columns (" + l.columns + ")", l.columns >= 2);
            Check(name + ": cards fill the grid width",
                  Mathf.Abs(l.columns * l.cardWidth + (l.columns - 1) * CodexPanel.Gap - gridW) < .5f);
            Check(name + ": cards are tappable (>= 150 wide)", l.cardWidth >= 150f && l.cardHeight >= 150f);
            Check(name + ": at least two rows of cards visible", l.body.height >= 2f * l.cardHeight);
            Check(name + ": back button meets the 96-unit tap target", l.back.height >= 96f && l.back.width >= 96f);
            Check(name + ": tabs are >= 90 wide", l.tabWidth >= 90f);
        }
    }

    // ---- The home-screen entry point and the real panel ----

    static void CheckHomeAndPanel()
    {
        // The codex used to hang off the credits screen; it lives on the home
        // page now, and credits must not carry a second entry point.
        EditorSceneManager.OpenScene("Assets/Scenes/creditsS7.unity", OpenSceneMode.Single);
        Check("creditsS7 has no CODEX button",
              UnityEngine.Object.FindFirstObjectByType<CodexHomeButton>() == null && SceneUtil.FindAny("CodexButton") == null);

        PlayerPrefs.SetString(Codex.PrefsKey, "enemy_space_fighter_1,atom_stardust,hazard_mine");
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        Codex.Reload();

        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        var entry = UnityEngine.Object.FindFirstObjectByType<CodexHomeButton>();
        Check("startS4 has the CODEX button", entry != null);
        if (entry == null) return;
        var uiPanel = entry.transform.parent;
        Check("CODEX is a row of the home menu (UIPanel)", uiPanel != null && uiPanel.name == "UIPanel");
        Check("CODEX sits right under Credits",
              uiPanel.Find("CreditsButton") != null &&
              entry.transform.GetSiblingIndex() == uiPanel.Find("CreditsButton").GetSiblingIndex() + 1);

        // The title logo must not move, resize or recolour.
        var logo = SceneUtil.FindAny("menuTitle");
        var logoSr = logo != null ? logo.GetComponent<SpriteRenderer>() : null;
        Vector3 logoPos = logo != null ? logo.transform.position : Vector3.zero;
        Vector3 logoScale = logo != null ? logo.transform.localScale : Vector3.zero;
        Color logoColor = logoSr != null ? logoSr.color : Color.clear;
        Sprite logoSprite = logoSr != null ? logoSr.sprite : null;

        var menu = UnityEngine.Object.FindFirstObjectByType<startMenu>();
        menu.LayoutHome();
        var uiRect = (RectTransform)uiPanel;
        float top = uiRect.anchoredPosition.y + uiRect.sizeDelta.y * .5f;
        float h = ((RectTransform)SceneUtil.FindAny("MainMenuCanvas").transform).rect.height;
        float oldTop = -h * .08f + Mathf.Min(330f, h * .4f) * .5f;
        Check("home menu keeps its top edge under the logo (" + top + " vs " + oldTop + ")",
              Mathf.Abs(top - oldTop) < .5f);
        var quit = (RectTransform)SceneUtil.FindAny("QuitButton").transform;
        Check("the 5-row menu stays clear of Quit",
              uiRect.anchoredPosition.y - uiRect.sizeDelta.y * .5f > quit.anchoredPosition.y + quit.sizeDelta.y * .5f - h * .5f);

        entry.Build();
        entry.Build();   // idempotent
        Check("CODEX button is built once", entry.GetComponentsInChildren<Button>(true).Length == 1);
        Check("button reads Codex, like its neighbours", entry.Label != null && entry.Label.text == "Codex");
        var creditsText = uiPanel.Find("CreditsButton").GetComponentInChildren<Text>(true);
        Check("label matches the home buttons' font and colour",
              entry.Label.font == creditsText.font && entry.Label.color == creditsText.color);
        Check("counter shows discovered/total ('" + entry.Counter.text + "')",
              Regex.IsMatch(entry.Counter.text, @"^\d+/\d+ DISCOVERED$") &&
              entry.Counter.text == Codex.DiscoveredCount + "/" + Codex.Total + " DISCOVERED");

        Check("the logo did not move", logo != null && logo.transform.position == logoPos && logo.transform.localScale == logoScale);
        Check("the logo kept its art and colour", logoSr != null && logoSr.sprite == logoSprite && logoSr.color == logoColor);

        entry.Button.onClick.Invoke();
        var panel = CodexPanel.Current;
        Check("pressing CODEX opens the panel", panel != null && panel.IsOpen && panel.gameObject.activeSelf);
        if (panel == null) return;
        panel.SkipAnimations();
        Check("panel is fully shown after its intro", panel.IsOpen && panel.Panel.GetComponent<CanvasGroup>().alpha > .99f);
        Check("the open codex owns Back (top BackNavigator layer), not home's quit", BackNavigator.Top == (object)panel);
        Check("panel uses the cel frame",
              panel.Panel.Find("Frame").GetComponent<Image>().sprite != null &&
              panel.Panel.Find("Frame").GetComponent<Image>().sprite.name == "cx_panel");
        Check("panel opens on the Pilot's Log", panel.Category == CodexCategory.Log && panel.VisibleCards == 2);
        Check("panel counter matches", panel.Counter.text.StartsWith(Codex.DiscoveredCount + " / " + Codex.Total));

        // Locked vs discovered cards.
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        int lockedSeen = 0, foundSeen = 0;
        for (int i = 0; i < panel.VisibleCards; i++)
        {
            var e = panel.CardEntry(i);
            bool found = Codex.IsDiscovered(e);
            if (found)
            {
                foundSeen++;
                Check("discovered card " + e.id + " shows its name", panel.CardName(i).text == e.name);
                Check("discovered card " + e.id + " shows full-colour art", panel.CardArt(i).color == Color.white);
            }
            else
            {
                lockedSeen++;
                Check("locked card " + e.id + " shows ???", panel.CardName(i).text == "???");
                Check("locked card " + e.id + " is a dark silhouette",
                      panel.CardArt(i).sprite != null && panel.CardArt(i).color.r < .1f && panel.CardArt(i).color.g < .1f);
            }
        }
        Check("enemies tab mixes locked and discovered", lockedSeen > 0 && foundSeen > 0);

        // Developer mode on with the panel open: it redraws with everything revealed.
        string realSeen = PlayerPrefs.GetString(Codex.PrefsKey);
        DeveloperUnlocks.SetEnabled(true);
        bool allNamed = true;
        for (int i = 0; i < panel.VisibleCards; i++)
            allNamed &= panel.CardName(i).text == panel.CardEntry(i).name && panel.CardArt(i).color == Color.white;
        Check("dev mode: the open panel refreshes to show every entry", allNamed);
        Check("dev mode: panel counter reads N/N", panel.Counter.text.StartsWith(Codex.Total + " / " + Codex.Total));
        entry.Refresh();
        Check("dev mode: home counter reads N/N", entry.Counter.text == Codex.Total + "/" + Codex.Total + " DISCOVERED");
        DeveloperUnlocks.SetEnabled(false);
        int relocked = 0;
        for (int i = 0; i < panel.VisibleCards; i++) if (panel.CardName(i).text == "???") relocked++;
        Check("dev mode off: the open panel returns to the real discoveries", relocked == lockedSeen);
        Check("dev mode round trip leaves codexSeen untouched", PlayerPrefs.GetString(Codex.PrefsKey) == realSeen);

        // Detail view, locked: no name, no lore.
        int lockedIndex = -1;
        for (int i = 0; i < panel.VisibleCards; i++) if (!Codex.IsDiscovered(panel.CardEntry(i))) { lockedIndex = i; break; }
        panel.CardButton(lockedIndex).onClick.Invoke();
        panel.SkipAnimations();
        var lockedEntry = panel.DetailEntry;
        Check("tapping a card opens its detail", panel.InDetail && lockedEntry == panel.CardEntry(lockedIndex));
        Check("locked detail hides the name", panel.DetailName.text == "???");
        Check("locked detail hides the lore", !panel.DetailLore.text.Contains(lockedEntry.lore) &&
                                              !panel.DetailLore.text.Contains(lockedEntry.name));
        Check("locked detail art is a silhouette", panel.DetailArt.color.r < .1f);
        panel.Back();
        panel.SkipAnimations();
        Check("BACK from detail returns to the grid", !panel.InDetail && panel.IsOpen);

        // Detail view, discovered.
        panel.ShowDetail(Codex.Find("enemy_space_fighter_1"));
        panel.SkipAnimations();
        Check("discovered detail shows the name", panel.DetailName.text == "Needle");
        Check("discovered detail shows the lore", panel.DetailLore.text == Codex.Find("enemy_space_fighter_1").lore);
        panel.ShowGrid();

        // Layout of the real panel across screens: everything inside, text fits.
        foreach (var (name, safe) in Screens)
        {
            panel.ApplyLayout(safe);
            foreach (var c in CodexPanel.Tabs)
            {
                panel.ShowCategory(c);
                panel.SkipAnimations();
                Canvas.ForceUpdateCanvases();
                var l = panel.CurrentLayout;
                bool inside = true, names = true;
                for (int i = 0; i < panel.VisibleCards; i++)
                {
                    var r = PanelSpace(panel.Panel, panel.CardRect(i));
                    // Cards scroll vertically; horizontally they must sit in the grid.
                    inside &= r.xMin >= l.body.xMin - .5f && r.xMax <= l.body.xMax + .5f;
                    names &= FitsAt(panel.CardName(i), 11);
                }
                Check(name + " / " + c + ": cards sit inside the grid", inside);
                Check(name + " / " + c + ": card names fit", names);
            }
            for (int i = 0; i < CodexPanel.Tabs.Length; i++)
                Check(name + ": tab " + panel.TabLabel(i).text + " fits at >= 13px", FitsAt(panel.TabLabel(i), 13));

            // The wordiest entry's lore must fit its card at a readable size.
            CodexEntry wordiest = null;
            foreach (var e in Codex.Entries)
                if (Codex.IsDiscovered(e) && (wordiest == null || e.lore.Length > wordiest.lore.Length)) wordiest = e;
            panel.ShowDetail(wordiest);
            panel.SkipAnimations();
            Canvas.ForceUpdateCanvases();
            Check(name + ": longest lore (" + wordiest.id + ") fits at >= 18px", FitsAt(panel.DetailLore, 18));
            Check(name + ": detail name fits", FitsAt(panel.DetailName, 24));
            var lore = PanelSpace(panel.Panel, (RectTransform)panel.DetailLore.transform.parent);
            Check(name + ": lore card inside the detail area", Contains(Inset(panel.CurrentLayout.detail, -.5f), lore));
            var artFrame = PanelSpace(panel.Panel, (RectTransform)panel.DetailArt.transform.parent.parent);
            Check(name + ": detail art clear of the lore card", !artFrame.Overlaps(lore));
            panel.ShowGrid();
            panel.SkipAnimations();
        }

        panel.Close();
        panel.SkipAnimations();
        Check("closing hides the panel", !panel.IsOpen && !panel.gameObject.activeSelf);
        Check("closing hands Back back to the home screen", !BackNavigator.IsRegistered(panel));

        // Reopening reuses the same panel.
        entry.OpenCodex();
        Check("reopening reuses the panel", CodexPanel.Current == panel && panel.IsOpen);
        panel.Close();
        panel.SkipAnimations();

        // The toast builds and shows a name without any raycast targets.
        var toast = CodexToast.Build();
        toast.Enqueue(Codex.Find("enemy_space_fighter_1"));
        Check("toast shows the entry name", toast.Showing && toast.ShowingName == "Needle");
        bool blocks = toast.GetComponent<GraphicRaycaster>() != null;
        foreach (var g in toast.GetComponentsInChildren<Graphic>(true)) blocks |= g.raycastTarget;
        Check("toast can never block a touch", !blocks);
        UnityEngine.Object.DestroyImmediate(toast.gameObject);
    }

    // ---- helpers ----

    // True when the text, at the size best-fit would choose, shows every
    // character in its rect and that size is at least minSize.
    // Measured in canvas units (scale factor 1), the way best fit sizes it:
    // the largest size from the max down whose wrapped height fits the rect
    // and whose longest word fits a line. Passes when that size is >= minSize.
    static bool FitsAt(Text t, int minSize)
    {
        var rect = t.rectTransform.rect.size;
        if (rect.x <= 0f || rect.y <= 0f) return false;
        int max = t.resizeTextForBestFit ? t.resizeTextMaxSize : t.fontSize;
        int min = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
        int chosen = -1;
        for (int size = max; size >= min; size--)
        {
            if (Fits(t, size, rect)) { chosen = size; break; }
        }
        bool ok = chosen >= minSize;
        if (!ok) Debug.Log("[CDX] '" + t.text.Replace("\n", " ") + "' does not fit at >= " + minSize + " in " + rect);
        return ok;
    }

    static bool Fits(Text t, int size, Vector2 rect)
    {
        var gen = new TextGenerator();
        var s = Settings(t, size, new Vector2(rect.x, 100000f));
        if (t.horizontalOverflow == HorizontalWrapMode.Overflow)
        {
            var one = Settings(t, size, new Vector2(100000f, 100000f));
            one.horizontalOverflow = HorizontalWrapMode.Overflow;
            return gen.GetPreferredWidth(t.text, one) <= rect.x + .5f && gen.GetPreferredHeight(t.text, one) <= rect.y + .5f;
        }
        if (gen.GetPreferredHeight(t.text, s) > rect.y + .5f) return false;
        var word = Settings(t, size, new Vector2(100000f, 100000f));
        word.horizontalOverflow = HorizontalWrapMode.Overflow;
        foreach (string w in t.text.Split(' ', '\n'))
            if (w.Length > 0 && gen.GetPreferredWidth(w, word) > rect.x + .5f) return false;
        return true;
    }

    static TextGenerationSettings Settings(Text t, int size, Vector2 extents)
    {
        var s = t.GetGenerationSettings(extents);
        s.scaleFactor = 1f;
        s.resizeTextForBestFit = false;
        s.fontSize = size;
        s.verticalOverflow = VerticalWrapMode.Overflow;
        s.generateOutOfBounds = true;
        return s;
    }

    // Content hash of an image file on disk.
    static string Hash(string assetPath)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
            return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(assetPath)));
    }

    static int CountIn(CodexCategory c)
    {
        int total;
        Codex.DiscoveredIn(c, out total);
        return total;
    }

    static int SentenceCount(string s)
    {
        return Regex.Matches(s, @"[.!?](\s|$)").Count;
    }

    static GameObject Find(GameObject[] all, string name)
    {
        foreach (var go in all) if (go != null && go.name == name) return go;
        return null;
    }

    static Rect CanvasRect(RectTransform rt)
    {
        return new Rect(rt.anchoredPosition - rt.sizeDelta * .5f, rt.sizeDelta);
    }

    static Rect PanelSpace(RectTransform panel, RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = panel.InverseTransformPoint(corners[0]);
        Vector2 max = panel.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    static Rect Inset(Rect r, float by) { return Rect.MinMaxRect(r.xMin + by, r.yMin + by, r.xMax - by, r.yMax - by); }

    static bool Contains(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin - .01f && inner.xMax <= outer.xMax + .01f &&
               inner.yMin >= outer.yMin - .01f && inner.yMax <= outer.yMax + .01f;
    }
}
