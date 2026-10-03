using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Shared plumbing for the batch-mode *Test suites.
//
// Every suite exposes `int Execute()` (returns its failure count) and keeps a
// `Run()` batch entry point that calls Exit() with it, so both of these work:
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShopTest.Run
//   Unity -batchmode -quit -projectPath Pause -executeMethod AllTests.RunAll
//
// Running many suites in one editor process means one suite's leftovers --
// the scene it opened, the PlayerPrefs it wrote, the game's many static
// fields (playerDied, pauseCounter, lifeCounter, ...) -- would otherwise leak
// into the next. Sandbox puts all of that back the way it found it.
public static class TestHarness
{
    // Batch entry: non-zero exit code on any failure so CI/scripts notice.
    public static void Exit(int failures)
    {
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // `using var sandbox = new TestHarness.Sandbox();` at the top of Execute().
    public sealed class Sandbox : IDisposable
    {
        readonly Dictionary<string, object> prefs = new Dictionary<string, object>();
        readonly List<KeyValuePair<FieldInfo, object>> statics = new List<KeyValuePair<FieldInfo, object>>();
        readonly List<KeyValuePair<object, object[]>> collections = new List<KeyValuePair<object, object[]>>();
        readonly float timeScale;

        public Sandbox()
        {
            foreach (string key in PrefsKeys()) prefs[key] = ReadPref(key);
            foreach (var field in GameStaticFields())
            {
                object value;
                try { value = field.GetValue(null); }
                catch (Exception) { continue; /* type initializer threw; nothing to restore */ }

                // A readonly field can't be reassigned, but the collection it
                // holds can be put back. This matters for the static sprite
                // caches (shopingShips.runtimeSprites, OriginalShipArt.cache):
                // the editor destroys their Sprite.Create()d entries when the
                // next scene opens, and a stale entry is never reloaded.
                if (field.IsInitOnly)
                {
                    if (value is IDictionary dict)
                    {
                        var entries = new object[dict.Count];
                        dict.CopyTo(entries, 0);
                        collections.Add(new KeyValuePair<object, object[]>(dict, entries));
                    }
                    else if (value is IList list)
                    {
                        var items = new object[list.Count];
                        list.CopyTo(items, 0);
                        collections.Add(new KeyValuePair<object, object[]>(list, items));
                    }
                    continue;
                }
                statics.Add(new KeyValuePair<FieldInfo, object>(field, value));
            }
            timeScale = Time.timeScale;
        }

        public void Dispose()
        {
            // Drop whatever scene the suite opened or dirtied (never saved).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var pair in statics)
            {
                try { pair.Key.SetValue(null, pair.Value); }
                catch (Exception) { }
            }
            foreach (var pair in collections)
            {
                try
                {
                    if (pair.Key is IDictionary dict)
                    {
                        dict.Clear();
                        foreach (DictionaryEntry e in pair.Value) dict[e.Key] = e.Value;
                    }
                    else if (pair.Key is IList list && list.IsFixedSize)
                    {
                        for (int i = 0; i < pair.Value.Length; i++) list[i] = pair.Value[i];
                    }
                    else if (pair.Key is IList growable)
                    {
                        growable.Clear();
                        foreach (var item in pair.Value) growable.Add(item);
                    }
                }
                catch (Exception) { }
            }
            Time.timeScale = timeScale;

            foreach (var pair in prefs)
            {
                if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key);
                else if (pair.Value is string s) PlayerPrefs.SetString(pair.Key, s);
                else if (pair.Value is int i) PlayerPrefs.SetInt(pair.Key, i);
                else PlayerPrefs.SetFloat(pair.Key, (float)pair.Value);
            }
            PlayerPrefs.Save();
        }
    }

    // Every PlayerPrefs key the game writes that a suite can reach, directly
    // or through component Start()s (shop, WorldManager, DeveloperUnlocks).
    static IEnumerable<string> PrefsKeys()
    {
        var keys = new List<string>
        {
            "spawnShip", "HasDoneTut", "PlayerCurrecny", "HighestSpeed",
            WorldManager.PrefsCurrentWorld, WorldManager.PrefsHighestWorld,
            DeveloperUnlocks.EnabledKey, DeveloperUnlocks.SelectedWorldKey,
            DeveloperUnlocks.ChoiceBuildKey,
        };
        for (int i = 0; i <= shopingShips.shipTotal; i++) keys.Add("boughtship" + i);

        // Tiered achievement counts, plus the old per-tier keys they migrate from.
        foreach (AchievementCategory category in System.Enum.GetValues(typeof(AchievementCategory)))
        {
            keys.Add(AchievementTiers.CounterKey(category));
            keys.Add(AchievementSync.SyncedKey(category));
            foreach (var tier in AchievementTiers.For(category))
                keys.Add(AchievementTiers.LegacyProgressKey(tier.id));
        }

        // Account cloud-save bookkeeping.
        keys.Add(CloudSync.LastAccountKey);
        keys.Add(CloudSync.LocalSavedAtKey);
        keys.Add(CloudSync.LocalHashKey);
        keys.Add(CloudSync.BackupsKey);

        // DeveloperUnlocks keeps its own backup copies of the progress keys.
        var backed = new List<string>
        {
            "HasDoneTut", WorldManager.PrefsHighestWorld, WorldManager.PrefsCurrentWorld,
        };
        for (int i = 0; i <= shopingShips.shipTotal; i++) backed.Add("boughtship" + i);
        foreach (string k in backed)
        {
            keys.Add("developerBackup_" + k);
            keys.Add("developerBackup_" + k + "_exists");
        }
        return keys;
    }

    // PlayerPrefs has no type query; a typed getter returns the default for a
    // key stored as another type, so probe with impossible defaults.
    static object ReadPref(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return null;
        const string probe = "\u0001~unset~";
        string s = PlayerPrefs.GetString(key, probe);
        if (s != probe) return s;
        int i = PlayerPrefs.GetInt(key, int.MinValue);
        if (i != int.MinValue) return i;
        return PlayerPrefs.GetFloat(key);
    }

    // Statics of the game's own scripts (Assembly-CSharp), not Unity's or the
    // editor tests'.
    static IEnumerable<FieldInfo> GameStaticFields()
    {
        var gameAssembly = typeof(shopingShips).Assembly;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in gameAssembly.GetTypes())
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var field in type.GetFields(flags))
            {
                if (field.IsLiteral) continue;
                yield return field;
            }
        }
    }
}
