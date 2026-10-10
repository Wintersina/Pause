using System;
using System.Collections.Generic;
using UnityEngine;

// FIXED-MASS POOLS FOR THE THEMED HAZARDS (docs/world-attacks-design.md FR8).
//
// A jet, a band, a ring, a strike or a lash is a component on a GameObject
// built ONCE; firing one takes a free item and releases it when it ends. No
// instantiation, no GetComponent and no allocation per frame after the
// constructor. EliteShots does the same for projectiles; this is the shared
// shape for the new primitives so each core (plan phases 1a-1e) does not
// reinvent it:
//
//   var pool = AttackPools.Get("jets", 6, root => AttackJet.Create(root));
//   var jet  = pool.Take();           // null when all are busy: a busy screen skips one
//   ...                               // the item activates itself; Release when it ends
//   pool.Release(jet);
//
// AttackPools.ClearAll() releases everything (a world change, the death
// domino, a portal, a loop, a replay: WorldLeakTest).
public interface IAttackPool
{
    string Name { get; }
    int Capacity { get; }
    int ActiveCount { get; }
    bool Alive { get; }
    void ReleaseAll();
}

public sealed class AttackPool<T> : IAttackPool where T : Component
{
    readonly T[] items;
    readonly bool[] taken;
    readonly Transform root;
    public string Name { get; }
    public int Capacity => items.Length;
    public int ActiveCount { get; private set; }
    public int Takes { get; private set; }
    public int Skipped { get; private set; }
    public bool Alive => root != null;
    public IReadOnlyList<T> All => items;

    public AttackPool(string name, int size, Transform parent, Func<Transform, T> make)
    {
        Name = name;
        root = new GameObject("~" + name).transform;
        if (parent != null) root.SetParent(parent, false);
        items = new T[size];
        taken = new bool[size];
        for (int i = 0; i < size; i++)
        {
            items[i] = make(root);
            items[i].gameObject.SetActive(false);
        }
    }

    // A free item, activated; null when every one is busy.
    public T Take()
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (taken[i] || items[i] == null) continue;
            taken[i] = true;
            ActiveCount++;
            Takes++;
            items[i].gameObject.SetActive(true);
            return items[i];
        }
        Skipped++;
        return null;
    }

    public void Release(T item)
    {
        if (item == null) return;
        for (int i = 0; i < items.Length; i++)
        {
            if (!ReferenceEquals(items[i], item) || !taken[i]) continue;
            taken[i] = false;
            ActiveCount--;
            item.gameObject.SetActive(false);
            return;
        }
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (!taken[i]) continue;
            taken[i] = false;
            if (items[i] != null) items[i].gameObject.SetActive(false);
        }
        ActiveCount = 0;
    }
}

public static class AttackPools
{
    static readonly List<IAttackPool> all = new List<IAttackPool>(8);
    static readonly Dictionary<string, IAttackPool> byName = new Dictionary<string, IAttackPool>();

    public static IReadOnlyList<IAttackPool> All => all;

    // The named pool, built on first use (or when a scene change destroyed the old one).
    public static AttackPool<T> Get<T>(string name, int size, Func<Transform, T> make) where T : Component
    {
        IAttackPool p;
        if (byName.TryGetValue(name, out p) && p.Alive) return (AttackPool<T>)p;
        if (p != null) all.Remove(p);
        var pool = new AttackPool<T>(name, size, EliteSystem.Root, make);
        byName[name] = pool;
        all.Add(pool);
        return pool;
    }

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (all[i].Alive) n += all[i].ActiveCount;
            return n;
        }
    }

    public static void ClearAll()
    {
        for (int i = 0; i < all.Count; i++) if (all[i].Alive) all[i].ReleaseAll();
        AttackPreview.EndAll();
    }

    // Tests: forget every pool (their objects went with the scene).
    public static void Forget()
    {
        all.Clear();
        byName.Clear();
    }
}
