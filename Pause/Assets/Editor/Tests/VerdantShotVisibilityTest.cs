using System.Collections.Generic;
using System.IO;
using UnityEngine;

// THE BLOOM QUEEN'S THREE PAINTED ATTACK SHOTS (Resources/Bosses/Verdant_attackshots.png rows 1 thorn, 2 spore cloud, 3 gourd) stand out of
// every Verdant backdrop variant, and her shot motion is calmer (VerdantShotTuning).
//
//   * contrast: for each installed Verdant variant (v1 autumn canopy, v2 lime swamp, v3 ruined city, v4 night forest) at six scroll offsets
//     (fixed seeds), each of the three shots, in both frames, on a 4 x 6 grid of placements (the HostileProjectileTest sample): the WORST
//     placement still has >= MinWorst pixels at HostileProjectileTest.MinContrast against the backdrop behind it (the boss-shot bound of
//     HostileProjectileTest is MinStandOutPixels; this asks for a comfortable margin on top). No outline or rim is involved: art > 0 shots wear none.
//   * VSHOT_TABLE=file appends "variant,shot,frame,worst,mean" rows (the before / after table); VSHOT_GALLERY=dir saves 1080 x 2340 frames of
//     the three shots over each variant.
//   * the wiggle: the tuning constants are within the calmer bounds, a homing seed's path is straighter than the old steering, stays inside
//     the rails, the hit radii are untouched.
public static class VerdantShotVisibilityTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[VSHOT] PASS  " : "[VSHOT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const int MinWorst = 8;
    public const int RegradeFloor = 100;   // the 2026-10-27 regrade measures 137+ worst case (thorn was 16): a repaint that loses most of it fails
    static readonly int[] Offsets = { 600, 1200, 1800, 2400, 3000, 3600 };
    static readonly string[] ShotNames = { "thorn", "spore", "gourd" };
    static readonly BossShotStyle[] Styles = { BossShotStyle.Bolt, BossShotStyle.Shard, BossShotStyle.Shard };

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Contrast();
                Gallery();
                Wiggle();
            }
            finally { BackdropVariants.For("Verdant").Force = 0; BossEncounter.ResetRun(); BossRails.Reset(); }
        }
        Debug.Log("[VSHOT] failures: " + fails);
        return fails;
    }

    static void Contrast()
    {
        const int RW = HostileProjectileTest.RW, RH = HostileProjectileTest.RH;
        var spec = BackdropCatalog.For("Verdant");
        var picker = BackdropVariants.For("Verdant");
        var boss = BossCatalog.ForWorld(2);
        string table = System.Environment.GetEnvironmentVariable("VSHOT_TABLE");
        int variants = 0;
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            if (spec.variantSets > 0 && !picker.Installed(v)) continue;
            variants++;
            var worst = new int[3]; var sum = new long[3]; var cnt = new int[3];
            for (int a = 0; a < 3; a++) worst[a] = int.MaxValue;
            var cam = HostileProjectileTest.Fresh(2);
            cam.aspect = RW / (float)RH;
            picker.Force = spec.variantSets > 0 ? v : 0;
            var backdrop = new GameObject("~Backdrop");
            var wb = backdrop.AddComponent<WorldBackdrop>();
            Random.InitState(9100 + v * 31);
            wb.Show("Verdant", false);
            var pool = new BossProjectilePool(40, 1);
            var rt = new RenderTexture(RW, RH, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(RW, RH, TextureFormat.RGB24, false);
            int done = 0;
            foreach (int off in Offsets)
            {
                while (done < off) { wb.Step(1f / 60f); done++; }
                for (int frame = 0; frame < 2; frame++)
                {
                    for (int art = 1; art <= 3; art++)
                    {
                        var list = new List<BossProjectile>();
                        for (int gx = 0; gx < 4; gx++)
                            for (int gy = 0; gy < 6; gy++)
                            {
                                var ps = new PShot { style = Styles[art - 1], art = art, rail = BossRailMode.Pass };
                                var s = pool.FireShot(boss, new Vector3(-2.1f + gx * 1.4f, -4f + gy * 1.6f, 0f), Vector2.zero, ref ps, false);
                                if (s != null) { if (frame == 1) s.Step(.17f); list.Add(s); }
                            }
                        pool.Root.SetActive(false);
                        Color[] bg = HostileProjectileTest.Grab(cam, rt, tex);
                        pool.Root.SetActive(true);
                        Color[] fg = HostileProjectileTest.Grab(cam, rt, tex);
                        foreach (var s in list)
                        {
                            Vector3 c = cam.WorldToScreenPoint(s.Glow.transform.position);
                            float half = s.transform.lossyScale.x * .5f * (RH / (cam.orthographicSize * 2f));
                            int n = HostileProjectileTest.StandOut(fg, bg, c, half);
                            int i = art - 1;
                            if (n < worst[i]) worst[i] = n;
                            sum[i] += n; cnt[i]++;
                        }
                        foreach (var s in list) s.Recycle();
                    }
                }
            }
            for (int a = 0; a < 3; a++)
            {
                float mean = cnt[a] > 0 ? sum[a] / (float)cnt[a] : 0f;
                Debug.Log("[VSHOT] variant " + v + " " + ShotNames[a] + ": worst " + worst[a] + " px, mean " + mean.ToString("F0") + " px (6 offsets, 2 frames, 24 placements)");
                if (!string.IsNullOrEmpty(table)) File.AppendAllText(table, v + "," + ShotNames[a] + "," + worst[a] + "," + mean.ToString("F0") + "\n");
                Check("variant " + v + " " + ShotNames[a] + " stands out: worst " + worst[a] + " px at " + HostileProjectileTest.MinContrast + ":1 (need " + MinWorst + ", regrade floor " + RegradeFloor + ")", worst[a] >= MinWorst && worst[a] >= RegradeFloor);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            pool.Dispose();
            Object.DestroyImmediate(backdrop);
            picker.Force = 0;
        }
        Check("every installed Verdant variant was covered (" + variants + ")", variants > 0);
    }

    // VSHOT_GALLERY=dir: 1080 x 2340 frames (both drawings) of the three shots over each variant, for the before / after crops.
    static void Gallery()
    {
        string dir = System.Environment.GetEnvironmentVariable("VSHOT_GALLERY");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        const int W = 1080, H = 2340;
        var spec = BackdropCatalog.For("Verdant");
        var picker = BackdropVariants.For("Verdant");
        var boss = BossCatalog.ForWorld(2);
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            if (spec.variantSets > 0 && !picker.Installed(v)) continue;
            var cam = HostileProjectileTest.Fresh(2);
            cam.aspect = W / (float)H;
            picker.Force = spec.variantSets > 0 ? v : 0;
            var backdrop = new GameObject("~Backdrop");
            var wb = backdrop.AddComponent<WorldBackdrop>();
            Random.InitState(9100 + v * 31);
            wb.Show("Verdant", false);
            var pool = new BossProjectilePool(40, 1);
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < 400 * (pass + 1); i++) wb.Step(1f / 60f);
                var list = new List<BossProjectile>();
                for (int art = 1; art <= 3; art++)
                    for (int row = 0; row < 4; row++)
                    {
                        var ps = new PShot { style = Styles[art - 1], art = art, rail = BossRailMode.Pass };
                        var s = pool.FireShot(boss, new Vector3(-1.4f + (art - 1) * 1.4f, -3.4f + row * 2.3f, 0f), Vector2.zero, ref ps, false);
                        if (s != null) { if (row % 2 == 1) s.Step(.17f); list.Add(s); }
                    }
                pool.Root.SetActive(false);
                cam.Render();
                var old = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = old;
                File.WriteAllBytes(Path.Combine(dir, "v" + v + "_p" + pass + "_bg.png"), tex.EncodeToPNG());
                pool.Root.SetActive(true);
                cam.Render();
                old = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = old;
                File.WriteAllBytes(Path.Combine(dir, "v" + v + "_p" + pass + ".png"), tex.EncodeToPNG());
                foreach (var s in list) s.Recycle();
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            pool.Dispose();
            Object.DestroyImmediate(backdrop);
            picker.Force = 0;
        }
    }

    // ---- the wiggle ----

    // A seed from (0, 3) at `speed` homing on a ship that weaves left and right at 4 u/s; returns the number of times its lateral velocity
    // reverses, the largest |x| it reaches, and its path length / displacement (1 = straight).
    static void Fly(bool soft, float homeDeg, float homeSeconds, out int reversals, out float maxX, out float wander)
    {
        Vector2 p = new Vector2(.3f, 3f), v = new Vector2(.4f, -1f).normalized * 2.4f;
        reversals = 0; maxX = 0f;
        float t = 0f, dt = 1f / 60f, len = 0f, lastSign = 0f;
        Vector2 start = p;
        while (p.y > -5.5f && t < 8f)
        {
            Vector2 ship = new Vector2(Mathf.Clamp(Mathf.PingPong(t * 4f, 4.8f) - 2.4f, -2.4f, 2.4f), -3.6f);
            if (t < homeSeconds)
            {
                Vector2 to = ship - p;
                float cur = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg, want = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
                float h = (cur + BossProjectile.HomeTurn(Mathf.DeltaAngle(cur, want), homeDeg * dt, to.magnitude, soft)) * Mathf.Deg2Rad;
                v = new Vector2(Mathf.Cos(h), Mathf.Sin(h)) * v.magnitude;
            }
            Vector2 n = p + v * dt;
            len += (n - p).magnitude; p = n; t += dt;
            maxX = Mathf.Max(maxX, Mathf.Abs(p.x));
            float sg = Mathf.Abs(v.x) < .05f ? lastSign : Mathf.Sign(v.x);
            if (lastSign != 0f && sg != lastSign) reversals++;
            if (sg != 0f) lastSign = sg;
        }
        wander = len / Mathf.Max(.01f, (p - start).magnitude);
    }

    static void Wiggle()
    {
        Check("old / new homing: 70 / 80 / 90 deg/s halved to 35 / 40 / 45",
              VerdantShotTuning.HomeSpore <= VerdantShotTuning.OldHomeSpore * .5f + .01f && VerdantShotTuning.HomeOvergrowth <= VerdantShotTuning.OldHomeOvergrowth * .5f + .01f &&
              VerdantShotTuning.HomeStorm <= VerdantShotTuning.OldHomeStorm * .5f + .01f && VerdantShotTuning.HomeSpore >= 25f);
        Check("fan tilt (amp) cut to half or less, still a visible tilt (3..4 deg)",
              VerdantShotTuning.FanAmpOvergrowth <= VerdantShotTuning.OldFanAmpOvergrowth * .5f + .01f && VerdantShotTuning.FanAmpStorm <= VerdantShotTuning.OldFanAmpStorm * .5f + .01f &&
              VerdantShotTuning.FanAmpStorm >= 3f);
        Check("the flip is slower than the old 2 ticks", VerdantShotTuning.FlipTicks[0] > VerdantShotTuning.OldFlipTicks && VerdantShotTuning.FlipTicks.Length == 2);
        var slots = BossPatternTables.For(2);
        bool wired = true; int seeds = 0;
        foreach (var slot in slots)
            if (slot.pattern != null)
                foreach (var st in slot.pattern.streams)
                {
                    if (st.kind == StreamKind.Seeds) { seeds++; wired &= st.homeDeg <= VerdantShotTuning.HomeStorm + .01f; }
                    if (st.kind == StreamKind.Fan && st.art > 0) wired &= st.amp <= VerdantShotTuning.FanAmpOvergrowth + .01f;
                }
        Check("the pattern table uses the tuned constants", wired && seeds == 3);

        int rOld, rNew; float xOld, xNew, wOld, wNew;
        Fly(false, VerdantShotTuning.OldHomeStorm, 1.8f, out rOld, out xOld, out wOld);
        Fly(true, VerdantShotTuning.HomeStorm, 1.8f, out rNew, out xNew, out wNew);
        Debug.Log("[VSHOT] homing seed on a weaving ship: old " + rOld + " lateral reversals, wander " + wOld.ToString("F3") + ", max |x| " + xOld.ToString("F2") +
                  "; new " + rNew + ", wander " + wNew.ToString("F3") + ", max |x| " + xNew.ToString("F2"));
        Check("a homing seed reverses sideways no more often, and wanders less", rNew <= rOld && wNew <= wOld + 1e-4f);
        Check("a homing seed stays inside the playfield and rails (|x| < 3.6)", xNew < 3.6f);
        Check("soft steering leaves an unrelated boss's homing alone", Mathf.Abs(BossProjectile.HomeTurn(30f, 1f, .2f, false) - 1f) < 1e-5f);
        Check("a seed on the ship stops steering; a far one eases", BossProjectile.HomeTurn(30f, 1f, .5f, true) == 0f && BossProjectile.HomeTurn(3f, 1f, 3f, true) < .5f &&
              Mathf.Abs(BossProjectile.HomeTurn(30f, 1f, 3f, true) - 1f) < 1e-5f);
        Check("the hit radii are unchanged (.13 bolt, .12 shard)", BossConfig.BoltHitRadius == .13f && BossConfig.ShardHitRadius == .12f);
    }
}
