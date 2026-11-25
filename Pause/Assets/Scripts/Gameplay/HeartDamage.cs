using UnityEngine;

// One damage scale for coin bullets, enemies, elites and bosses. 100% is one heart;
// 25% is one section. Integer quarter units avoid fractional drift and hidden extra scaling.
public static class HeartDamage
{
    public const int PerHeart = 4;
    public const float Quarter = .25f;
    public static int Units(float hearts) => hearts <= 0f ? 0 : Mathf.CeilToInt(hearts * PerHeart - .0001f);
    public static int WholeHearts(int units) => (Mathf.Max(0, units) + PerHeart - 1) / PerHeart;
    public static float Hearts(int units) => Mathf.Max(0, units) * Quarter;
    public static float InQuarters(float hearts) => Hearts(Units(hearts));
}
