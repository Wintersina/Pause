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
        CheckAnimationCatalogue();
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
        // The old alien1.prefab fallback is gone: every alien is the world's
        // roster alien (below), the Space one keeping the enemy_alien entry.
        Check("enmiesOnBoard has no alien1 prefab fallback", typeof(enmiesOnBoard).GetField("alien1") == null);
        Check("the Space roster alien is the enemy_alien entry",
              EnemyRoster.One(0, EnemyRole.Alien) != null && EnemyRoster.One(0, EnemyRole.Alien).codexId == "enemy_alien");

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
        Check("every codex enemy/hazard entry is a roster enemy, an elite or a boss",
              Array.TrueForAll(Codex.Entries, e =>
                  (e.category != CodexCategory.Enemies && e.category != CodexCategory.Hazards) ||
                  EnemyRoster.FindByCodexId(e.id) != null ||
                  EliteCatalog.FindByCodexId(e.id) != null ||   // the elite ships
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
        foreach (var file in new[] { "CodexPanel.cs", "CodexToast.cs", "CodexHomeButton.cs", "CodexUi.cs",
                                     "CodexAnimator.cs", "CodexAnimations.cs" })
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
        ("9:22 phone 1080x2640", new Rect(-400f, -978f, 800f, 1956f)),
        ("9:24 phone 1080x2880", new Rect(-400f, -1067f, 800f, 2133f)),
        ("Z Fold cover 968x2376", new Rect(-400f, -982f, 800f, 1964f)),
        ("notch + home bar", new Rect(-400f, -835f, 800f, 1640f)),
        ("iPad 1536x2048", new Rect(-480f, -640f, 960f, 1280f)),
        ("landscape Mac 1600x900", new Rect(-1138f, -640f, 2276f, 1280f)),
        ("off-centre safe area", new Rect(-380f, -700f, 760f, 1500f)),
        ("extra-tall 9:22 1080x2640", new Rect(-400f, -978f, 800f, 1956f)),
        ("extra-tall 9:24 1080x2880", new Rect(-400f, -1067f, 800f, 2133f)),
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

            // Sectioned tabs: jump chips over the scrolling list, both in the body.
            Check(name + ": chip row and list sit in the body", Contains(l.body, l.chips) && Contains(l.body, l.list));
            Check(name + ": chip row clear of the list and the tabs", !l.chips.Overlaps(l.list) && !l.chips.Overlaps(l.tabs));
            Check(name + ": chips are >= 90 wide (5 sections)",
                  (l.chips.width - CodexPanel.ChipGap * (CodexPanel.MaxSections - 1)) / CodexPanel.MaxSections >= 90f);
            Check(name + ": chips meet a 48-unit tap height", l.chips.height >= 48f);
            Check(name + ": a section header and two card rows fit the list",
                  l.list.height >= CodexPanel.SectionHeaderHeight + CodexPanel.SectionHeaderGap + 2f * l.cardHeight);
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
                if (!CodexPanel.IsSectioned(c)) continue;

                bool headers = true, headerText = true, chipsOk = true;
                var list = l.list;
                for (int s = 0; s < panel.SectionCount; s++)
                {
                    var r = PanelSpace(panel.Panel, panel.SectionHeaderRect(s));
                    headers &= r.xMin >= list.xMin - .5f && r.xMax <= list.xMax + .5f;
                    headerText &= FitsAt(panel.SectionLabel(s), 18) && FitsAt(panel.SectionCounter(s), 22);
                    var chip = PanelSpace(panel.Panel, panel.ChipRect(s));
                    chipsOk &= Contains(Inset(l.chips, -.5f), chip) && FitsAt(panel.ChipLabel(s), 13);
                }
                Check(name + " / " + c + ": section headers sit inside the list", headers);
                Check(name + " / " + c + ": section header label and counter fit", headerText);
                Check(name + " / " + c + ": jump chips fit their row and labels fit at >= 13px", chipsOk);
                var cards = PanelSpace(panel.Panel, panel.CardRect(0));
                Check(name + " / " + c + ": list clear of the chips", !Inset(cards, .5f).Overlaps(l.chips) &&
                      PanelSpace(panel.Panel, panel.Viewport).yMax <= l.chips.yMin + .5f);
                panel.SetScrollY(panel.SectionTop(1) + 1f);
                var pinned = PanelSpace(panel.Panel, panel.StickyRect);
                var view = PanelSpace(panel.Panel, panel.Viewport);
                Check(name + " / " + c + ": pinned header sits at the top of the list",
                      panel.StickySection == 1 && Mathf.Abs(pinned.yMax - view.yMax) < .5f &&
                      pinned.xMin >= view.xMin - .5f && pinned.xMax <= view.xMax + .5f);
                panel.SetScrollY(0f);
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

        CheckSections(panel, entry);
        CheckPanelAnimation(panel);

        panel.Close();
        panel.SkipAnimations();
        Check("closing hides the panel", !panel.IsOpen && !panel.gameObject.activeSelf);
        bool anyTicking = panel.DetailAnimator.Ticking;
        for (int i = 0; i < panel.VisibleCards; i++) anyTicking |= panel.CardAnimator(i).Ticking;
        Check("closing pauses every animation (panel inactive, nothing ticking)", !anyTicking && !panel.isActiveAndEnabled);
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

    // ---- ENEMIES / HAZARDS: world sections, secret bosses, chips, pinned header ----

    static readonly string[] WorldLabels = { "SPACE", "FROST", "VERDANT", "EMBER" };

    static void CheckSections(CodexPanel panel, CodexHomeButton home)
    {
        panel.ApplyLayout(Screens[1].safe);
        DeveloperUnlocks.SetEnabled(false);
        PlayerPrefs.SetString(Codex.PrefsKey, "enemy_space_fighter_1,enemy_frost_alien,hazard_mine,hazard_ember_mine");
        Codex.Reload();
        panel.Refresh();
        panel.ShowCategory(CodexCategory.Log);
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();

        // Order and contents: one section per world, nothing secret.
        Check("ENEMIES is sectioned", panel.Sectioned);
        Check("no boss met: ENEMIES shows SPACE, FROST, VERDANT, EMBER and no BOSSES (" + Labels(panel) + ")",
              Labels(panel) == "SPACE,FROST,VERDANT,EMBER");
        CheckWorldSections(panel, false);
        bool noBoss = true;
        for (int i = 0; i < panel.VisibleCards; i++) noBoss &= BossCatalog.Find(panel.CardEntry(i).id) == null;
        Check("no boss met: no boss card anywhere", noBoss);
        Check("SPACE counter is found/total ('" + panel.SectionCounter(0).text + "')",
              panel.SectionCounter(0).text == "1/" + panel.SectionAt(0).entries.Count);
        Check("FROST counter counts its own discoveries", panel.SectionCounter(1).text == "1/" + panel.SectionAt(1).entries.Count);
        Check("headers use each world's enemy light",
              panel.SectionAt(0).color == EnemyPalette.WorldLight(0) && panel.SectionAt(3).color == EnemyPalette.WorldLight(3));
        bool locked = true;
        for (int i = panel.SectionStart(2); i < panel.SectionStart(2) + panel.SectionAt(2).entries.Count; i++)
            locked &= panel.CardName(i).text == Codex.LockedName && panel.CardArt(i).color.r < .1f;
        Check("locked entries in a world section stay ??? silhouettes", locked);
        int fighters = 0;
        var space = panel.SectionAt(0).entries;
        for (int i = 0; i < space.Count; i++)
        {
            var def = EnemyRoster.FindByCodexId(space[i].id);
            if (def.role == EnemyRole.Fighter) { fighters++; if (def.tier != i + 1) fighters = -100; }
        }
        Check("a world section lists fighters by tier first, then chaser, alien and big",
              fighters == 4 && EnemyRoster.FindByCodexId(space[4].id).role == EnemyRole.Chaser &&
              EnemyRoster.FindByCodexId(space[5].id).role == EnemyRole.Alien &&
              EnemyRoster.FindByCodexId(space[space.Count - 1].id).role == EnemyRole.Big);
        Check("one jump chip per section", ActiveChips(panel) == 4 && panel.ChipLabel(3).text == "EMBER");

        // One boss met: the BOSSES section appears last with just that boss.
        var boss = BossCatalog.ForWorld(1);
        int totalBefore = Codex.Total;
        PlayerPrefs.SetString(Codex.PrefsKey, PlayerPrefs.GetString(Codex.PrefsKey) + "," + boss.id);
        Codex.Reload();
        panel.Refresh();
        panel.SkipAnimations();
        Check("one boss met: BOSSES is the last section (" + Labels(panel) + ")", Labels(panel) == "SPACE,FROST,VERDANT,EMBER,BOSSES");
        var bosses = panel.SectionCount == 5 ? panel.SectionAt(4) : null;
        Check("one boss met: only that boss is listed",
              bosses != null && bosses.entries.Count == 1 && bosses.entries[0].id == boss.id &&
              panel.CardEntry(panel.SectionStart(4)).id == boss.id && panel.VisibleCards == panel.SectionStart(4) + 1);
        Check("one boss met: BOSSES counts met bosses only, no hidden total ('" + panel.SectionCounter(4).text + "')",
              panel.SectionCounter(4).text == "1");
        CheckWorldSections(panel, true);
        Check("one boss met: the codex total grows by one", Codex.Total == totalBefore + 1);
        home.Refresh();
        Check("one boss met: home counter includes it", home.Counter.text == Codex.DiscoveredCount + "/" + Codex.Total + " DISCOVERED");
        Check("one boss met: a BOSSES jump chip", ActiveChips(panel) == 5 && panel.ChipLabel(4).text == "BOSSES");

        // Developer mode: all four bosses and the header, nothing written.
        string seen = PlayerPrefs.GetString(Codex.PrefsKey);
        DeveloperUnlocks.SetEnabled(true);
        panel.SkipAnimations();
        bool allBosses = panel.SectionCount == 5 && panel.SectionAt(4).entries.Count == BossCatalog.All.Length;
        for (int b = 0; allBosses && b < BossCatalog.All.Length; b++)
            allBosses &= panel.SectionAt(4).entries[b].id == BossCatalog.All[b].id &&
                         panel.CardName(panel.SectionStart(4) + b).text == Codex.Find(BossCatalog.All[b].id).name;
        Check("dev mode: BOSSES shows all four bosses, named", allBosses);
        Check("dev mode: BOSSES counter reads " + BossCatalog.All.Length, panel.SectionCounter(4).text == BossCatalog.All.Length.ToString());
        CheckWorldSections(panel, true);
        DeveloperUnlocks.SetEnabled(false);
        panel.SkipAnimations();
        Check("dev mode off: back to the one met boss, codexSeen untouched",
              panel.SectionCount == 5 && panel.SectionAt(4).entries.Count == 1 && PlayerPrefs.GetString(Codex.PrefsKey) == seen);

        // Pinned header: follows the section at the top, pushed by the next.
        panel.SetScrollY(0f);
        Check("at the top nothing is pinned (the first header is in place)", panel.StickySection == -1);
        panel.SetScrollY(panel.SectionTop(2) + 40f);
        Check("inside VERDANT its header is pinned", panel.StickySection == 2 && panel.StickyLabel.text == "VERDANT" &&
              panel.StickyCounter.text == panel.SectionCounter(2).text);
        panel.SetScrollY(panel.SectionTop(2) - 20f);
        Check("the next header pushes the pinned one up",
              panel.StickySection == 1 && Mathf.Abs(panel.StickyRect.anchoredPosition.y - (CodexPanel.SectionHeaderHeight - 20f)) < .5f);
        panel.SetScrollY(panel.SectionTop(1) + 100f);
        Check("well inside a section the pinned header sits flush", Mathf.Abs(panel.StickyRect.anchoredPosition.y) < .01f);

        // Scrolling allocates nothing.
        for (int k = 0; k < 50; k++) panel.SetScrollY(k * 40f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 400; k++) panel.SetScrollY((k % 100) * (panel.MaxScroll / 100f));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("scrolling through every section allocates nothing (" + allocated + " bytes)", allocated == 0);

        // Jump chips.
        for (int s = 0; s < panel.SectionCount; s++)
        {
            panel.SetScrollY(s == 0 ? panel.MaxScroll : 0f);
            panel.ChipButton(s).onClick.Invoke();
            Check(panel.ChipLabel(s).text + " chip highlights at once", panel.ActiveChip == s);
            panel.SkipAnimations();
            float target = panel.JumpTargetY(s);
            var header = PanelSpace(panel.Panel, panel.SectionHeaderRect(s));
            var view = PanelSpace(panel.Panel, panel.Viewport);
            bool atTop = Mathf.Abs(header.yMax - view.yMax) < 1f || (s == 0 && Mathf.Abs(header.yMax - (view.yMax - CodexPanel.GridInset)) < 1f);
            bool clamped = Mathf.Abs(target - panel.MaxScroll) < .5f;
            Check(panel.ChipLabel(s).text + " chip scrolls to its section (y " + panel.ScrollY + ", target " + target + ")",
                  Mathf.Abs(panel.ScrollY - target) < .5f && header.yMax <= view.yMax + 1f && header.yMin >= view.yMin - 1f &&
                  (atTop || clamped) && (clamped || s == 0 || panel.StickySection == s) && panel.ActiveChip == s);
        }
        Check("jumps animate over unscaled time", CodexPanel.JumpDuration > .1f && CodexPanel.JumpDuration < .6f &&
              File.ReadAllText("Assets/Scripts/Codex/CodexPanel.cs").Contains("jumpAt = Time.unscaledTime"));

        // Back from a detail returns to the list where it was.
        float y = Mathf.Min(panel.SectionTop(2) + 30f, panel.MaxScroll);
        panel.SetScrollY(y);
        int card = panel.SectionStart(2) + 1;
        panel.CardButton(card).onClick.Invoke();
        panel.SkipAnimations();
        Check("a card in a section opens its detail", panel.InDetail && panel.DetailEntry == panel.CardEntry(card));
        panel.Back();
        panel.SkipAnimations();
        Check("Back from detail keeps the list's scroll position (" + panel.ScrollY + " vs " + y + ")",
              !panel.InDetail && panel.IsOpen && Mathf.Abs(panel.ScrollY - y) < .01f && panel.StickySection == 2);
        panel.CardButton(card).onClick.Invoke();
        panel.SkipAnimations();
        BackNavigator.Back();
        panel.SkipAnimations();
        Check("system Back from detail keeps it too", !panel.InDetail && panel.IsOpen && Mathf.Abs(panel.ScrollY - y) < .01f);
        DeveloperUnlocks.SetEnabled(true);
        DeveloperUnlocks.SetEnabled(false);
        Check("a dev-mode redraw keeps the scroll position", Mathf.Abs(panel.ScrollY - Mathf.Min(y, panel.MaxScroll)) < .01f);

        // HAZARDS: the same world sections, rocks then the mine.
        panel.ShowCategory(CodexCategory.Hazards);
        panel.SkipAnimations();
        Check("HAZARDS is grouped by world (" + Labels(panel) + ")", panel.Sectioned && Labels(panel) == "SPACE,FROST,VERDANT,EMBER");
        bool grouped = true, mineLast = true;
        for (int w = 0; w < 4 && panel.SectionCount == 4; w++)
        {
            var want = new HashSet<string>();
            foreach (var d in EnemyRoster.All) if (d.world == w && d.IsHazard) want.Add(d.codexId);
            var got = new HashSet<string>();
            for (int i = 0; i < panel.SectionAt(w).entries.Count; i++) got.Add(panel.CardEntry(panel.SectionStart(w) + i).id);
            grouped &= want.SetEquals(got) && got.Count == panel.SectionAt(w).entries.Count;
            var es = panel.SectionAt(w).entries;
            mineLast &= EnemyRoster.FindByCodexId(es[es.Count - 1].id).role == EnemyRole.Mine;
        }
        Check("each HAZARDS world section holds exactly that world's rocks and mine", grouped);
        Check("rocks come before the mine", mineLast);
        Check("HAZARDS counters ('" + panel.SectionCounter(0).text + "', '" + panel.SectionCounter(3).text + "')",
              panel.SectionCounter(0).text == "1/" + panel.SectionAt(0).entries.Count &&
              panel.SectionCounter(3).text == "1/" + panel.SectionAt(3).entries.Count);

        // Plain tabs keep the plain grid.
        panel.ShowCategory(CodexCategory.Ships);
        panel.SkipAnimations();
        Check("other tabs stay a plain grid without chips", !panel.Sectioned && !panel.ChipsRoot.gameObject.activeSelf &&
              panel.StickySection == -1);
    }

    // Each world section holds exactly that world's roster enemies.
    static void CheckWorldSections(CodexPanel panel, bool bossesShown)
    {
        bool ok = panel.SectionCount >= 4;
        for (int w = 0; ok && w < 4; w++)
        {
            var want = new HashSet<string>();
            foreach (var d in EnemyRoster.All) if (d.world == w && !d.IsHazard) want.Add(d.codexId);
            var got = new HashSet<string>();
            var section = panel.SectionAt(w);
            for (int i = 0; i < section.entries.Count; i++)
            {
                var e = panel.CardEntry(panel.SectionStart(w) + i);
                ok &= e == section.entries[i];
                got.Add(e.id);
            }
            ok &= want.SetEquals(got) && got.Count == section.entries.Count && section.world == w;
        }
        Check("each world section holds exactly that world's roster enemies" + (bossesShown ? " (bosses shown)" : ""), ok);
    }

    static string Labels(CodexPanel panel)
    {
        var parts = new List<string>();
        for (int i = 0; i < panel.SectionCount; i++)
            parts.Add(panel.SectionLabel(i).gameObject.activeInHierarchy ? panel.SectionLabel(i).text : "(hidden)");
        return string.Join(",", parts);
    }

    static int ActiveChips(CodexPanel panel)
    {
        int n = 0;
        for (int i = 0; i < CodexPanel.MaxSections; i++) if (panel.ChipRect(i).gameObject.activeSelf) n++;
        return panel.ChipsRoot.gameObject.activeSelf ? n : 0;
    }

    // ---- Animated art: resolvers, loaders, single frames ----

    // Distinct idle drawings the entry's own game loader has, read directly
    // from that loader (not through the codex), so a loader that gains
    // frames must show up animating below.
    static int LoaderIdleFrames(CodexEntry e)
    {
        var drawings = new HashSet<Sprite>();
        switch (CodexAnimations.KindOf(e))
        {
            case CodexAnimKind.Enemy:
            case CodexAnimKind.Mine:
            {
                var elite = EliteCatalog.FindByCodexId(e.id);
                if (elite != null)
                {
                    var ef = EliteArt.Frames(elite);
                    if (ef != null) for (int i = 0; i < EliteArt.IdleFrames; i++) drawings.Add(ef[i]);
                    break;
                }
                var frames = EnemyArt.Frames(EnemyRoster.FindByCodexId(e.id));
                if (frames != null) for (int i = 0; i < Mathf.Min(EnemyRoster.TellFrame, frames.Length); i++) drawings.Add(frames[i]);
                break;
            }
            case CodexAnimKind.Boss:
                for (int i = 0; i < BossArt.IdleFrames; i++) drawings.Add(BossArt.Body(BossCatalog.Find(e.id), BossArt.Idle0 + i));
                break;
            case CodexAnimKind.Atom:
            {
                PickupKind kind;
                if (CodexAnimations.TryPickupKind(e.id, out kind))
                    foreach (var s in PickupArt.Frames(PickupArt.IdleName(kind), PickupArt.IdleTicks(kind).Length)) drawings.Add(s);
                break;
            }
            case CodexAnimKind.Ship:
                for (int d = 0; d < ShipHullArt.IdleDrawings; d++)
                    drawings.Add(ShipHullArt.Get(CodexCatalogue.ShipIndex(e.id), ShipSkins.Stock, 0, d));
                break;
            case CodexAnimKind.Portal:
                for (int i = 0; i < TeleportPortalSprites.FrameCount; i++) drawings.Add(TeleportPortalSprites.FrameAt(i));
                break;
            default:
                if (e.Sprite != null) drawings.Add(e.Sprite);
                break;
        }
        drawings.Remove(null);
        return drawings.Count;
    }

    static CodexAnimKind ExpectedKind(CodexEntry e)
    {
        switch (e.category)
        {
            case CodexCategory.Log: return CodexAnimKind.Log;
            case CodexCategory.Atoms: return CodexAnimKind.Atom;
            case CodexCategory.Ships: return CodexAnimKind.Ship;
            case CodexCategory.Worlds: return e.id == CodexCatalogue.PortalId ? CodexAnimKind.Portal : CodexAnimKind.World;
        }
        if (BossCatalog.Find(e.id) != null) return CodexAnimKind.Boss;
        if (EliteCatalog.FindByCodexId(e.id) != null) return CodexAnimKind.Enemy;
        return EnemyRoster.FindByCodexId(e.id).role == EnemyRole.Mine ? CodexAnimKind.Mine : CodexAnimKind.Enemy;
    }

    // A loose animator on a 200x200 box, outside any panel.
    static CodexAnimator TestAnimator(out GameObject root)
    {
        root = new GameObject("CodexAnimTest", typeof(RectTransform), typeof(Canvas));
        var box = CodexUi.NewRect("Box", root.transform);
        box.sizeDelta = new Vector2(200f, 200f);
        var img = CodexUi.NewImage("Art", box, null, Color.white);
        return CodexAnimator.On(img);
    }

    // Steps the animator for `seconds`; returns the distinct drawings it showed.
    static int Run(CodexAnimator a, float seconds, float dt = 1f / 30f)
    {
        var seen = new HashSet<Sprite>();
        if (a.Shown != null) seen.Add(a.Shown);
        for (float t = 0f; t < seconds; t += dt)
        {
            a.Advance(dt);
            if (a.Shown != null) seen.Add(a.Shown);
        }
        return seen.Count;
    }

    static void CheckAnimationCatalogue()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root;
        var animator = TestAnimator(out root);
        try
        {
            foreach (var e in Codex.Entries)
            {
                var a = CodexAnimations.For(e);
                Check(e.id + " resolves an animation (" + (a != null ? a.kind.ToString() : "none") + ")", a != null && a.HasArt);
                if (a == null) continue;
                Check(e.id + " uses its kind's resolver (" + ExpectedKind(e) + ")", a.kind == ExpectedKind(e));
                Check(e.id + " animation is cached, frames and all", CodexAnimations.For(e) == a);

                // Future-proof: whatever the loader holds plays here.
                int loader = LoaderIdleFrames(e);
                animator.Bind(a, false, false);
                int shown = Run(animator, a.IdleLoopSeconds * 1.5f + .5f);
                if (loader > 1)
                    Check(e.id + ": all " + loader + " idle drawings of its loader play in the codex (" + shown + ")",
                          a.Animates && shown == loader && animator.FrameChanges > 0);
                else
                    Check(e.id + ": single-drawing art stays on its one drawing (" + shown + ")", shown == 1);
            }

            // Who animates, and how.
            Check("log entries are static", !CodexAnimations.For(Codex.Find("log_pilot")).Animates);
            var frost = CodexAnimations.For(Codex.Find("world_frost"));
            Check("worlds pan slowly in their round window", frost.Animates && frost.fit == CodexAnimation.FitMode.Cover &&
                                                              frost.driftPeriod >= 10f);
            var portal = CodexAnimations.For(Codex.Find(CodexCatalogue.PortalId));
            Check("the portal plays its frames and turns", portal.distinctIdle > 1 && portal.spinDegreesPerSecond != 0f);
            int spinners = 0;
            foreach (int id in ShipId.All)
            {
                var sa = CodexAnimations.For(Codex.Find(CodexCatalogue.ShipPrefix + ShipId.KeyOf(id)));
                bool spins = sa.spinDegreesPerSecond != 0f;
                if (spins) spinners++;
                Check("ship " + ShipId.KeyOf(id) + (ShipExhaust.UsesWind(id) ? " spins" : " doesn't spin"), spins == ShipExhaust.UsesWind(id));
            }
            Check("the spinners (Ninja, UFO) turn in the codex", spinners == 2);
            var green = CodexAnimations.For(Codex.Find("atom_green"));
            Check("the green atom keeps its original drawing under its overlay loop",
                  green.under != null && green.under.texture.name == "heal_atom_green" && green.distinctIdle > 1);
            var mine = CodexAnimations.For(Codex.Find("hazard_mine"));
            Check("the rail mine idles dormant with its waking blink (RailMineArt)",
                  mine.kind == CodexAnimKind.Mine && mine.idle[0] == RailMineArt.Frame(0, RailMineArt.Dormant) &&
                  Array.IndexOf(mine.idle, RailMineArt.Frame(0, RailMineArt.Waking)) >= 0 && mine.HasTell);
            var needle = CodexAnimations.For(Codex.Find("enemy_space_fighter_1"));
            var def = EnemyRoster.FindByCodexId("enemy_space_fighter_1");
            Check("an enemy's loop is EnemyArt's frames on EnemyRoster's idle ticks",
                  needle.idle[0] == EnemyArt.Frame(def, 0) && needle.idle[3] == EnemyArt.Frame(def, 3) &&
                  Mathf.Abs(needle.idleHold[0] - EnemyRoster.IdleTicks(def.role)[0] * EnemyFlipbook.TickSeconds) < 1e-5f &&
                  needle.HasTell && needle.tells[0][0] == EnemyArt.Frame(def, EnemyRoster.TellFrame));
            var boss = CodexAnimations.For(Codex.Find(BossCatalog.All[0].id));
            Check("a boss's loop is BossArt's idle on BossArt.IdleTicks, with its three tell poses",
                  boss.idle.Length == BossArt.IdleFrames && boss.idle[1] == BossArt.Body(BossCatalog.All[0], BossArt.Idle0 + 1) &&
                  Mathf.Abs(boss.IdleLoopSeconds - BossArt.Seconds(BossArt.IdleTicks)) < 1e-4f && boss.tells.Length == 3);
            var hull = CodexAnimations.For(Codex.Find(CodexCatalogue.ShipPrefix + ShipId.KeyOf(ShipId.Starter)));
            Check("a ship loops ShipHullArt's idle table (stock skin)",
                  Mathf.Abs(hull.IdleLoopSeconds - ShipHullArt.IdleLoopTicks / ShipHullArt.TicksPerSecond) < 1e-4f &&
                  hull.idle[0] == ShipHullArt.StockRest(ShipId.Starter));

            // Single-frame and missing art: static, no errors.
            var one = Codex.Find("enemy_space_fighter_1").Sprite;
            var single = CodexAnimation.Loop(CodexAnimKind.Enemy, new[] { one, one, one }, new[] { .1f, .1f, .1f }).Finish();
            Check("art with one drawing doesn't count as animating", !single.Animates && single.distinctIdle == 1);
            animator.Bind(single, false, true);
            Check("a single drawing stays put (no changes, no error)",
                  Run(animator, 5f) == 1 && animator.FrameChanges == 0 && animator.Shown == one);
            Check("a loader with no frames resolves nothing to play",
                  CodexAnimation.Loop(CodexAnimKind.Enemy, new Sprite[] { null, null }, new[] { .1f, .1f }) == null);
            var missing = new CodexEntry("test_missing_art", "Missing", CodexCategory.Log, () => null, "Nothing to draw here at all.");
            var none = CodexAnimations.For(missing);
            animator.Bind(none, false, true);
            Run(animator, 1f);
            Check("an entry with no art at all binds and ticks without error", none != null && !none.Animates);
            animator.Bind(null, true, true);
            animator.Advance(.5f);
            Check("binding nothing is harmless", animator.FrameChanges == 0);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // ---- Animated art in the real panel ----

    static int IndexOf(CodexPanel panel, string id)
    {
        for (int i = 0; i < panel.VisibleCards; i++) if (panel.CardEntry(i).id == id) return i;
        return -1;
    }

    static void ScrollTo(CodexPanel panel, int card)
    {
        float centre = -panel.CardRect(card).anchoredPosition.y;
        panel.SetScrollY(Mathf.Clamp(centre - panel.Viewport.rect.height * .5f, 0f, panel.MaxScroll));
    }

    static void Tick(CodexPanel panel, float seconds, float dt = 1f / 30f)
    {
        for (float t = 0f; t < seconds; t += dt) panel.TickAnimations(dt);
    }

    static bool Ink(Graphic g) { return g == null || !g.enabled || (g.color.r < .1f && g.color.g < .1f && g.color.b < .2f); }

    // ---- Rendered silhouettes ("who's that Pokemon?") ----

    public struct ArtRender
    {
        public int covered;      // pixels the art touches at all
        public int opaque;       // pixels it covers completely (same over black and white)
        public int colours;      // distinct colours among the opaque pixels
        public Color32 first;    // one of them
        public bool rendered;
    }

    // Renders only `show` (plus the masks that clip it) from the panel's own
    // canvas, once over black and once over white. A pixel that comes out the
    // same over both is fully inside the drawing's alpha mask; a flat
    // silhouette has exactly one colour across all of those.
    public static ArtRender RenderArt(CodexPanel panel, Graphic[] show, string savePath = null)
    {
        var result = new ArtRender();
        var canvas = panel.GetComponent<Canvas>();
        if (canvas == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return result;

        var keep = new HashSet<Graphic>();
        foreach (var g in show) if (g != null && g.enabled && g.gameObject.activeInHierarchy) keep.Add(g);
        if (keep.Count == 0) return result;
        var hidden = new List<Graphic>();
        foreach (var g in canvas.GetComponentsInChildren<Graphic>(false))
        {
            if (!g.enabled || keep.Contains(g)) continue;
            var mask = g.GetComponent<Mask>();
            if (mask != null && mask.enabled) continue;   // writes the stencil the art is clipped by
            g.enabled = false;
            hidden.Add(g);
        }

        var mode = canvas.renderMode;
        var oldCam = canvas.worldCamera;
        float plane = canvas.planeDistance;
        var camGo = new GameObject("~CodexArtCam");
        camGo.transform.position = new Vector3(5000f, 5000f, -10f);   // away from every world sprite
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.enabled = false;
        const int W = 800, H = 1280;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        Canvas.ForceUpdateCanvases();

        var shots = new Color32[2][];
        var colours = new[] { Color.black, Color.white };
        for (int k = 0; k < 2; k++)
        {
            cam.backgroundColor = colours[k];
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            shots[k] = tex.GetPixels32();
            if (k == 0 && savePath != null) File.WriteAllBytes(savePath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        canvas.renderMode = mode;
        canvas.worldCamera = oldCam;
        canvas.planeDistance = plane;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camGo);
        foreach (var g in hidden) g.enabled = true;
        Canvas.ForceUpdateCanvases();

        var seen = new HashSet<int>();
        for (int i = 0; i < shots[0].Length; i++)
        {
            Color32 b = shots[0][i], w = shots[1][i];
            bool onBlack = b.r > 0 || b.g > 0 || b.b > 0;
            bool onWhite = w.r < 255 || w.g < 255 || w.b < 255;
            if (!onBlack && !onWhite) continue;
            result.covered++;
            if (Mathf.Abs(b.r - w.r) > 1 || Mathf.Abs(b.g - w.g) > 1 || Mathf.Abs(b.b - w.b) > 1) continue;
            result.opaque++;
            if (seen.Add((b.r << 16) | (b.g << 8) | b.b) && seen.Count == 1) result.first = b;
        }
        result.colours = seen.Count;
        result.rendered = true;
        return result;
    }

    static bool Flat(ArtRender r, out string why)
    {
        Color32 ink = (Color)CodexUi.Silhouette;
        bool inkOk = Mathf.Abs(r.first.r - ink.r) <= 1 && Mathf.Abs(r.first.g - ink.g) <= 1 && Mathf.Abs(r.first.b - ink.b) <= 1;
        why = r.opaque + "/" + r.covered + " px opaque, " + r.colours + " colour(s), first " + r.first;
        return r.rendered && r.opaque >= 200 && r.colours == 1 && inkOk;
    }

    static void CheckRenderedSilhouettes(CodexPanel panel)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Check("rendered silhouettes need a graphics device (run without -nographics)", false);
            return;
        }
        Check("the silhouette shader ships in Resources and is supported",
              Resources.Load<Shader>(CodexUi.SilhouetteShaderPath) != null && CodexUi.SilhouetteMaterial != null &&
              CodexUi.SilhouetteMaterial.shader.isSupported);

        // Locked cards: one flat ink colour inside the alpha mask, every
        // frame of the idle loop.
        foreach (var tab in new[] { CodexCategory.Enemies, CodexCategory.Hazards, CodexCategory.Atoms, CodexCategory.Ships })
        {
            panel.ShowCategory(tab);
            panel.SkipAnimations();
            int tested = 0;
            for (int i = 0; i < panel.VisibleCards && tested < 3; i++)
            {
                var e = panel.CardEntry(i);
                if (Codex.IsDiscovered(e)) continue;
                tested++;
                ScrollTo(panel, i);
                var a = panel.CardAnimator(i);
                Check(tab + " " + e.id + ": locked art draws through the silhouette material",
                      CodexUi.IsSilhouette(a.Image) && (a.Overlay == null || !a.Overlay.enabled || CodexUi.IsSilhouette(a.Overlay)));
                bool flat = true;
                string why = "";
                for (int frame = 0; frame < 3; frame++)
                {
                    var r = RenderArt(panel, new Graphic[] { a.Image, a.Overlay });
                    string w;
                    flat &= Flat(r, out w);
                    why += (frame > 0 ? " | " : "") + w;
                    Tick(panel, .25f);
                }
                Check(tab + " " + e.id + ": locked card renders one flat colour, no interior detail (" + why + ")", flat);
            }
            Check(tab + ": rendered some locked cards", tested > 0);
        }

        // Sanity: the same measurement sees the detail of a discovered card.
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        int found = -1;
        for (int i = 0; i < panel.VisibleCards; i++) if (Codex.IsDiscovered(panel.CardEntry(i))) { found = i; break; }
        if (found >= 0)
        {
            ScrollTo(panel, found);
            var a = panel.CardAnimator(found);
            Check("a discovered card uses the default material", !CodexUi.IsSilhouette(a.Image));
            var r = RenderArt(panel, new Graphic[] { a.Image, a.Overlay });
            Check("a discovered card renders its full detail (" + r.colours + " colours over " + r.opaque + " px)",
                  r.rendered && r.colours > 8);
        }

        // The locked detail view: the same silhouette, overlay included.
        panel.ShowCategory(CodexCategory.Atoms);
        panel.SkipAnimations();
        panel.ShowDetail(Codex.Find("atom_green"));
        panel.SkipAnimations();
        bool dflat = true;
        string dwhy = "";
        for (int frame = 0; frame < 3; frame++)
        {
            var r = RenderArt(panel, new Graphic[] { panel.DetailArt, panel.DetailAnimator.Overlay });
            string w;
            dflat &= Flat(r, out w);
            dwhy += (frame > 0 ? " | " : "") + w;
            Tick(panel, .25f);
        }
        Check("the locked detail view renders one flat colour (" + dwhy + ")", dflat);
        Check("the locked detail draws through the silhouette material",
              CodexUi.IsSilhouette(panel.DetailArt) &&
              (panel.DetailAnimator.Overlay == null || !panel.DetailAnimator.Overlay.enabled || CodexUi.IsSilhouette(panel.DetailAnimator.Overlay)));
        panel.ShowGrid();
        panel.SkipAnimations();
    }

    static void CheckPanelAnimation(CodexPanel panel)
    {
        panel.ApplyLayout(Screens[1].safe);
        DeveloperUnlocks.SetEnabled(true);   // bosses listed, everything revealed
        panel.SkipAnimations();

        // Every kind advances in both the card and the detail view.
        var probes = new List<(CodexCategory tab, string id)>
        {
            (CodexCategory.Enemies, "enemy_space_fighter_1"), (CodexCategory.Enemies, "enemy_frost_alien"),
            (CodexCategory.Hazards, "hazard_mine"), (CodexCategory.Hazards, "hazard_ember_mine"),
            (CodexCategory.Hazards, "hazard_rock_dark"),
        };
        foreach (var b in BossCatalog.All) probes.Add((CodexCategory.Enemies, b.id));
        foreach (var e in Codex.Entries)
            if (e.category == CodexCategory.Atoms || e.category == CodexCategory.Ships || e.category == CodexCategory.Worlds)
                probes.Add((e.category, e.id));
        foreach (var (tab, id) in probes)
        {
            panel.ShowCategory(tab);
            panel.SkipAnimations();
            int i = IndexOf(panel, id);
            Check(id + " has a card on " + tab, i >= 0);
            if (i < 0) continue;
            ScrollTo(panel, i);
            var a = panel.CardAnimator(i);
            int before = a.FrameChanges;
            float clockBefore = a.Clock;
            Tick(panel, 3f);
            // A world pans rather than flipping drawings: its clock runs.
            bool moved = a.FrameChanges > before || (a.Animation != null && a.Animation.distinctIdle <= 1 && a.Clock > clockBefore + 2.9f);
            Check(id + " card animates on screen (" + (a.FrameChanges - before) + " changes)", panel.CardOnScreen(i) && a.Ticking && moved);

            panel.ShowDetail(panel.CardEntry(i));
            panel.SkipAnimations();
            var d = panel.DetailAnimator;
            Check(id + " detail is bound to the same animation", d.Animation == a.Animation);
            Tick(panel, 3f);
            bool dmoved = d.FrameChanges > 0 || (d.Animation != null && d.Animation.distinctIdle <= 1 && d.Clock > 2.9f);
            Check(id + " detail animates (" + d.FrameChanges + " changes)", d.Ticking && dmoved);
            bool gridStill = true;
            for (int k = 0; k < panel.VisibleCards; k++) gridStill &= !panel.CardAnimator(k).Ticking;
            Check(id + ": the grid behind the detail view doesn't tick", gridStill);
            panel.ShowGrid();
            panel.SkipAnimations();
        }

        // The detail view plays the attack tell now and then; cards never do.
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        panel.SetScrollY(0f);
        int needle = IndexOf(panel, "enemy_space_fighter_1");
        bool cardTold = false;
        for (int k = 0; k < 360; k++) { panel.TickAnimations(1f / 30f); cardTold |= panel.CardAnimator(needle).Telling; }
        Check("cards play the idle only, never the tell", !cardTold);
        panel.ShowDetail(Codex.Find("enemy_space_fighter_1"));
        panel.SkipAnimations();
        bool told = false, back = false;
        for (int k = 0; k < 450; k++)
        {
            panel.TickAnimations(1f / 30f);
            if (panel.DetailAnimator.Telling) told = true;
            else if (told) back = true;
        }
        Check("the detail view plays the attack tell every few seconds and returns to idle", told && back);
        panel.ShowDetail(Codex.Find(BossCatalog.All[1].id));
        panel.SkipAnimations();
        bool bossTold = false;
        for (int k = 0; k < 300; k++) { panel.TickAnimations(1f / 30f); bossTold |= panel.DetailAnimator.Telling; }
        Check("a boss's detail plays its tell too (dev mode)", bossTold);
        panel.ShowGrid();
        panel.SkipAnimations();

        // timeScale 0 (the game's pause) doesn't stop the codex.
        float oldScale = Time.timeScale;
        Time.timeScale = 0f;
        int beforeFrozen = panel.CardAnimator(needle).FrameChanges;
        Tick(panel, 2f);
        Check("animation runs at timeScale 0 (" + (panel.CardAnimator(needle).FrameChanges - beforeFrozen) + " changes)",
              panel.CardAnimator(needle).FrameChanges > beforeFrozen);
        Time.timeScale = oldScale;
        string panelSrc = File.ReadAllText("Assets/Scripts/Codex/CodexPanel.cs");
        Check("the panel ticks its art on unscaled delta time", panelSrc.Contains("TickAnimations(Time.unscaledDeltaTime)"));
        foreach (var file in new[] { "CodexAnimator.cs", "CodexAnimations.cs" })
            Check(file + " never reads timeScale", !File.ReadAllText("Assets/Scripts/Codex/" + file).Contains("timeScale"));

        // Off-screen cards hold still.
        panel.SetScrollY(0f);
        int off = -1, on = -1;
        for (int i = 0; i < panel.VisibleCards; i++)
        {
            if (!panel.CardOnScreen(i) && panel.CardAnimator(i).Animates && off < 0) off = i;
            if (panel.CardOnScreen(i) && panel.CardAnimator(i).Animates && on < 0) on = i;
        }
        Check("the list has cards both on and off screen", off >= 0 && on >= 0);
        if (off >= 0 && on >= 0)
        {
            int was = panel.CardAnimator(off).FrameChanges;
            float clock = panel.CardAnimator(off).Clock;
            Tick(panel, 3f);
            Check("off-screen cards don't tick", !panel.CardAnimator(off).Ticking &&
                  panel.CardAnimator(off).FrameChanges == was && panel.CardAnimator(off).Clock == clock);
            Check("on-screen cards do", panel.CardAnimator(on).Ticking);
            ScrollTo(panel, off);
            panel.TickAnimations(1f / 30f);
            Check("a card scrolled into view starts ticking", panel.CardAnimator(off).Ticking);
        }

        // Zero allocations: scrolling and animating together.
        for (int k = 0; k < 120; k++) { panel.SetScrollY((k % 40) * (panel.MaxScroll / 40f)); panel.TickAnimations(1f / 60f); }
        long before0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 600; k++) { panel.SetScrollY((k % 100) * (panel.MaxScroll / 100f)); panel.TickAnimations(1f / 60f); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before0;
        Check("animating while scrolling allocates nothing (" + allocated + " bytes)", allocated == 0);
        panel.ShowDetail(Codex.Find(BossCatalog.All[0].id));
        panel.SkipAnimations();
        for (int k = 0; k < 120; k++) panel.TickAnimations(1f / 60f);
        before0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 600; k++) panel.TickAnimations(1f / 60f);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before0;
        Check("animating the detail view (tells included) allocates nothing (" + allocated + " bytes)", allocated == 0);
        panel.ShowGrid();
        panel.SkipAnimations();

        // Locked silhouettes animate and never show colour.
        DeveloperUnlocks.SetEnabled(false);
        PlayerPrefs.SetString(Codex.PrefsKey, "enemy_space_fighter_1");
        Codex.Reload();
        panel.Refresh();
        foreach (var tab in new[] { CodexCategory.Enemies, CodexCategory.Hazards, CodexCategory.Atoms, CodexCategory.Ships })
        {
            panel.ShowCategory(tab);
            panel.SkipAnimations();
            int lockedCards = 0, moving = 0;
            bool inkOnly = true;
            for (int i = 0; i < panel.VisibleCards; i++)
            {
                if (Codex.IsDiscovered(panel.CardEntry(i))) continue;
                lockedCards++;
                ScrollTo(panel, i);
                var a = panel.CardAnimator(i);
                int was = a.FrameChanges;
                for (int k = 0; k < 60; k++)
                {
                    panel.TickAnimations(1f / 30f);
                    inkOnly &= Ink(a.Image) && Ink(a.Overlay) && panel.CardName(i).text == Codex.LockedName;
                }
                if (a.FrameChanges > was || !a.Animates) moving++;
            }
            Check(tab + ": locked silhouettes animate (" + moving + "/" + lockedCards + ")", lockedCards > 0 && moving == lockedCards);
            Check(tab + ": locked silhouettes stay flat ink on every frame (art never revealed)", inkOnly);
        }
        panel.ShowCategory(CodexCategory.Atoms);
        panel.SkipAnimations();
        panel.ShowDetail(Codex.Find("atom_green"));
        panel.SkipAnimations();
        bool detailInk = true;
        int dWas = panel.DetailAnimator.FrameChanges;
        for (int k = 0; k < 120; k++)
        {
            panel.TickAnimations(1f / 30f);
            detailInk &= Ink(panel.DetailArt) && Ink(panel.DetailAnimator.Overlay) && panel.DetailName.text == Codex.LockedName;
        }
        Check("a locked detail animates as a silhouette (green atom, overlay too)",
              detailInk && panel.DetailAnimator.FrameChanges > dWas);
        panel.ShowGrid();
        panel.SkipAnimations();
        CheckRenderedSilhouettes(panel);
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        bool noBoss = true;
        for (int i = 0; i < panel.VisibleCards; i++) noBoss &= BossCatalog.Find(panel.CardEntry(i).id) == null;
        Check("bosses stay hidden until met (no silhouette card either)", noBoss);

        // The animated art stays inside its box on every screen.
        DeveloperUnlocks.SetEnabled(true);
        foreach (var (name, safe) in Screens)
        {
            panel.ApplyLayout(safe);
            foreach (var c in CodexPanel.Tabs)
            {
                panel.ShowCategory(c);
                panel.SkipAnimations();
                bool fits = true;
                for (int i = 0; i < panel.VisibleCards; i++)
                {
                    var a = panel.CardAnimator(i);
                    a.Advance(.4f);
                    fits &= ArtFits(a, panel.CardArtBox(i));
                }
                Check(name + " / " + c + ": animated card art fits its box", fits);
            }
            bool detailFits = true;
            foreach (var id in new[] { CodexCatalogue.ShipPrefix + "Ninja", "hazard_ember_mine", "atom_green", BossCatalog.All[2].id, "world_ember" })
            {
                panel.ShowDetail(Codex.Find(id));
                panel.SkipAnimations();
                panel.DetailAnimator.Advance(.3f);
                detailFits &= ArtFits(panel.DetailAnimator, (RectTransform)panel.DetailArt.transform.parent);
            }
            Check(name + ": animated detail art fits its frame", detailFits);
            panel.ShowGrid();
            panel.SkipAnimations();
        }
        DeveloperUnlocks.SetEnabled(false);
        panel.ApplyLayout(Screens[1].safe);
    }

    // The drawing (and overlay) inside the art box at any angle it turns
    // to; a world's cover art fills the box instead (the round mask clips it).
    static bool ArtFits(CodexAnimator a, RectTransform box)
    {
        var anim = a.Animation;
        if (anim == null || anim.fit == CodexAnimation.FitMode.Stretch) return true;
        float side = Mathf.Min(box.rect.width, box.rect.height);
        bool ok = true;
        foreach (var g in new Graphic[] { a.Image, a.Overlay })
        {
            if (g == null || !g.enabled) continue;
            var rt = g.rectTransform;
            if (anim.fit == CodexAnimation.FitMode.Cover)
            {
                ok &= rt.sizeDelta.x >= side - .5f && rt.sizeDelta.y >= side - .5f &&
                      Mathf.Abs(rt.anchoredPosition.y) <= (rt.sizeDelta.y - side) * .5f + .5f;
                continue;
            }
            Vector2 c = rt.anchoredPosition, h = rt.sizeDelta * .5f;
            if (anim.spinDegreesPerSecond != 0f)
                ok &= c.magnitude + h.magnitude <= side * .5f * Mathf.Sqrt(2f) + .5f && UnionDiagonalFits(anim, rt, side);
            else ok &= Mathf.Abs(c.x) + h.x <= side * .5f + .5f && Mathf.Abs(c.y) + h.y <= side * .5f + .5f;
        }
        if (!ok) Debug.Log("[CDX] art outside its box: " + anim.kind + " " + a.Image.rectTransform.sizeDelta + " in " + side);
        return ok;
    }

    // A turning drawing is scaled by its diagonal, so its circle fits the box.
    static bool UnionDiagonalFits(CodexAnimation anim, RectTransform rt, float side)
    {
        var s = rt.GetComponent<Image>().sprite;
        if (s == null) return true;
        float scale = rt.sizeDelta.x / Mathf.Max(.0001f, s.bounds.size.x);
        float diag = new Vector2(anim.union.size.x, anim.union.size.y).magnitude * scale;
        return diag <= side + .5f;
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
