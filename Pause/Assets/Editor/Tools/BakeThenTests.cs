using UnityEditor;

// Batch entry used while adding a ship: bake the hull silhouettes + hitboxes
// (ShieldSilhouetteBaker: normally a delayCall on hull import, which a
// -batchmode -quit run never reaches), then run the -suites list.
//
//   scripts/unity-batch.sh -executeMethod BakeThenTests.Run -suites A,B,C
public static class BakeThenTests
{
    public static void Run()
    {
        ShieldSilhouetteBaker.BakeIfStale();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AllTests.RunSuites();
    }
}
