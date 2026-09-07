using UnityEditor;
using UnityEngine;

public static class ShipLivesIndicatorTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SL] PASS  " : "[SL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        // collisionDetection.Start() is what normally sets these; it never runs
        // in this isolated test, so the real preconditions are set explicitly.
        collisionDetection.MAXLIFE = 3;

        // A Retro80s ship (has its own damage art) should get no hearts at all.
        // Destroy() is deferred (and Play-mode oriented) so the component may
        // still be structurally attached right after this call even in the
        // correct-behaviour case -- what actually matters is that Build()
        // did not run, i.e. no heart children exist.
        PlayerPrefs.SetInt("spawnShip", 3);
        var withArt = new GameObject("WithDamageArt", typeof(SpriteRenderer));
        var indicatorA = withArt.AddComponent<ShipLivesIndicator>();
        indicatorA.SendMessage("Start");
        int strayHearts = 0;
        foreach (Transform c in withArt.transform)
            if (c.name.StartsWith("Heart")) strayHearts++;
        Check("ship with its own damage art gets no hearts built", strayHearts == 0);
        Object.DestroyImmediate(withArt);

        // A legacy ship (no damage art) should get exactly MAXLIFE hearts,
        // all visible at full health.
        PlayerPrefs.SetInt("spawnShip", 8); // Lightning
        var legacy = new GameObject("Legacy", typeof(SpriteRenderer));
        var sr = legacy.GetComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Resources/ShipArt/Originals/Lightning.png"); // may be null; Build() must tolerate that
        var indicatorB = legacy.AddComponent<ShipLivesIndicator>();
        indicatorB.SendMessage("Start");

        int heartCount = 0;
        foreach (Transform c in legacy.transform)
            if (c.name.StartsWith("Heart")) heartCount++;
        Check("legacy ship gets exactly MAXLIFE hearts (" + heartCount + ")",
              heartCount == collisionDetection.MAXLIFE);

        int visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.gameObject.activeSelf) visible++;
        Check("all hearts start visible at full health (" + visible + "/" + heartCount + ")",
              visible == collisionDetection.MAXLIFE);

        // Simulate taking a hit and healing it back.
        int savedLife = collisionDetection.lifeCounter;
        collisionDetection.lifeCounter = 1;
        indicatorB.SendMessage("Update");
        visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.gameObject.activeSelf) visible++;
        Check("one hit hides exactly one heart (" + visible + " left)", visible == collisionDetection.MAXLIFE - 1);

        collisionDetection.lifeCounter = 0; // heal atom restoring a life
        indicatorB.SendMessage("Update");
        visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.gameObject.activeSelf) visible++;
        Check("healing brings a heart back (" + visible + " shown)", visible == collisionDetection.MAXLIFE);

        collisionDetection.lifeCounter = savedLife;
        Object.DestroyImmediate(legacy);
        PlayerPrefs.DeleteKey("spawnShip");

        Debug.Log("[SL] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
