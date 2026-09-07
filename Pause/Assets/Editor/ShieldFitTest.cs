using UnityEditor;
using UnityEngine;

public static class ShieldFitTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SF] PASS  " : "[SF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        // Pure math: doubling the hull's world footprint should double the
        // resulting local scale, and padding should behave linearly.
        float baseScale = collisionDetection.ComputeShieldLocalScale(
            new Vector2(1f, 1f), 1f, new Vector2(1f, 1f), 0.35f);
        float doubledHull = collisionDetection.ComputeShieldLocalScale(
            new Vector2(2f, 2f), 1f, new Vector2(1f, 1f), 0.35f);
        Check("doubling hull extents roughly doubles the shield scale",
              Mathf.Abs(doubledHull - baseScale * 2f) < 0.001f);

        float noPadding = collisionDetection.ComputeShieldLocalScale(
            new Vector2(1f, 1f), 1f, new Vector2(1f, 1f), 0f);
        Check("zero padding means the shield exactly matches the hull radius",
              Mathf.Abs(noPadding - 1f) < 0.001f);

        Check("degenerate shield sprite returns 0 rather than throwing",
              collisionDetection.ComputeShieldLocalScale(Vector2.one, 1f, Vector2.zero, 0.3f) == 0f);

        // Real prefabs: every ship's shield must end up proportioned the
        // same way once fitted, whatever its hull scale or art size --
        // this is the actual regression guard for ship2's 2x outlier.
        //
        // Ships 1-3 carry no sprite baked into the prefab -- lifeControler
        // assigns it at runtime via shopingShips.DamageSpritesFor, which is
        // exactly why the fit itself had to move off Start() and onto a
        // poll. Sourced the same way here so they are covered too, not
        // skipped as "no sprite to test."
        float? firstRatio = null;
        for (int i = 1; i <= 11; i++)
        {
            string path = "Assets/Resources/Prefabs/Ships/inGameShips/ship" + i + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var cd = prefab.GetComponent<collisionDetection>();
            var hullSr = prefab.GetComponent<SpriteRenderer>();
            var shieldGo = prefab.transform.Find("Shield");
            if (cd == null || hullSr == null || shieldGo == null) continue;
            var shieldSr = shieldGo.GetComponent<SpriteRenderer>();
            if (shieldSr == null || shieldSr.sprite == null) continue;

            Sprite hullSprite = hullSr.sprite;
            if (hullSprite == null)
            {
                var frames = shopingShips.DamageSpritesFor(i);
                hullSprite = (frames != null && frames.Length > 0) ? frames[0] : null;
            }
            Check("ship" + i + " has a hull sprite to fit against (prefab or runtime lookup)", hullSprite != null);
            if (hullSprite == null) continue;

            float parentScale = Mathf.Max(prefab.transform.lossyScale.x, prefab.transform.lossyScale.y);
            float fitted = collisionDetection.ComputeShieldLocalScale(
                hullSprite.bounds.extents, parentScale, shieldSr.sprite.bounds.extents, cd.shieldPadding);

            float hullWorldR = Mathf.Max(hullSprite.bounds.extents.x, hullSprite.bounds.extents.y) * parentScale;
            float shieldWorldR = Mathf.Max(shieldSr.sprite.bounds.extents.x, shieldSr.sprite.bounds.extents.y)
                                  * parentScale * fitted;
            float ratio = shieldWorldR / hullWorldR;

            Check("ship" + i + " fitted shield covers the hull (ratio " + ratio.ToString("F2") + ")", ratio > 1f);
            if (firstRatio == null) firstRatio = ratio;
            else Check("ship" + i + " shield ratio matches the others (" + ratio.ToString("F2") +
                       " vs " + firstRatio.Value.ToString("F2") + ")",
                       Mathf.Abs(ratio - firstRatio.Value) < 0.01f);
        }

        Debug.Log("[SF] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
