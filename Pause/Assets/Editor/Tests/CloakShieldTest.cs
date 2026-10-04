using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Phase Cloak and the blue-atom shield, and the HUD without the shield timer.
//
//   - Cloak used to only bump collisionDetection.invTimer, but hazards are
//     gated on atomCheck, so it never protected the ship. It now runs its own
//     cloak clock and hazards check collisionDetection.Invulnerable.
//   - Cloak keeps its own lavender look: it never raises the contour shield,
//     the boost, or touches the shield's timer.
//   - The two overlap in either order and each ends on its own schedule:
//     no early cancel, no stuck invulnerability.
//   - The blue-atom countdown text (gotAtomText) is gone from gameS1 and
//     tutorialS5, and gameS1's read-out panel is one row shorter.
public static class CloakShieldTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CS] PASS  " : "[CS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);
    static readonly MethodInfo Tick = typeof(collisionDetection).GetMethod("turnTextsOff", Inst);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CloakAloneIsRealInvulnerability();
        CloakExpiryRestoresVulnerability();
        BlueAtomDuringCloak();
        CloakDuringShield();
        RecloakNeverShortens();
        ResetClearsCloak();

        HudHasNoShieldTimer("Assets/Scenes/gameS1.unity", "gameS1");
        HudHasNoShieldTimer("Assets/Scenes/tutorialS5.unity", "tutorialS5");
        GameHudPanelTightened();

        Debug.Log("[CS] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------

    sealed class Rig
    {
        public GameObject ship;
        public BoxCollider2D box;
        public collisionDetection cd;
        public ShipPowerController power;
        public ShipShield Shield { get { return ship.GetComponent<ShipShield>(); } }

        public void Hit(string name, string tag = "Astr")
        {
            var go = new GameObject(name, typeof(CircleCollider2D));
            go.tag = tag;
            go.transform.position = ship.transform.position + Vector3.right * .3f;
            Trigger.Invoke(cd, new object[] { go.GetComponent<Collider2D>() });
            if (go != null) Object.DestroyImmediate(go);
        }

        public void BlueAtom()
        {
            var atom = new GameObject("atom3a(Clone)", typeof(CircleCollider2D));
            atom.tag = "pickUp";
            Trigger.Invoke(cd, new object[] { atom.GetComponent<Collider2D>() });
            if (atom != null) Object.DestroyImmediate(atom);
        }

        public void Frame() { Tick.Invoke(cd, null); }

        // Jade Phantom's secret power (SecretPowerController.BeginPhaseCloak).
        public const float CloakSeconds = 4f;
        public void Cloak() { SecretPowerController.BeginPhaseCloak(ship.transform, 6, CloakSeconds); }

        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(ship);
            foreach (var name in new[] { "~PowerFx", "~CloakAura", "~fx", "~TestExplosion(Clone)" })
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                    Object.DestroyImmediate(go);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
        }
    }

    static Rig MakeRig()
    {
        var r = new Rig();
        r.ship = new GameObject("ship3(Clone)", typeof(SpriteRenderer));
        var sprite = shopingShips.SpriteFor(3, 0);
        r.ship.GetComponent<SpriteRenderer>().sprite = sprite;
        float s = shopingShips.NormalizedHullScale(sprite);
        r.ship.transform.localScale = new Vector3(s, s, 1f);
        r.ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        r.box = r.ship.AddComponent<BoxCollider2D>();
        r.box.isTrigger = true;
        r.cd = r.ship.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = r.ship.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);   // as collisionDetection.Start leaves it
        r.power = r.ship.AddComponent<ShipPowerController>();
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.cloakTimer = 0f;
        return r;
    }

    // ------------------------------------------------------------------
    // Cloak
    // ------------------------------------------------------------------

    static void CloakAloneIsRealInvulnerability()
    {
        var r = MakeRig();
        Check("before Cloak a fresh ship is vulnerable", !collisionDetection.Invulnerable);

        r.Cloak();
        Check("Cloak runs for its intended duration (" + collisionDetection.cloakTimer + "s)",
              Mathf.Approximately(collisionDetection.cloakTimer, Rig.CloakSeconds));
        Check("Cloak makes the ship invulnerable", collisionDetection.Cloaked && collisionDetection.Invulnerable);
        Check("Cloak keeps its own look: no blue-atom shield, no boost",
              !collisionDetection.atomCheck && (r.Shield == null || !r.Shield.IsUp) && !r.cd.boost.activeSelf);
        Check("Cloak leaves the shield's own timer alone (music and blink read it)",
              collisionDetection.invTimer == 0f);
        Check("Cloak's lavender aura is drawn", GameObject.Find("~CloakAura") != null);

        r.Hit("rock");
        Check("a hazard hitting a cloaked ship does no damage", collisionDetection.lifeCounter == 0);
        r.Hit("alien1(Clone)", "Enimey");
        Check("an enemy hitting a cloaked ship does no damage either", collisionDetection.lifeCounter == 0);
        r.Hit("mine(Clone)", "Enimey");
        Check("nor does a mine", collisionDetection.lifeCounter == 0);
        Check("absorbing under Cloak alone leaves the hull hitbox in charge",
              r.box.enabled && (r.Shield == null || !r.Shield.IsUp));
        r.Dispose();
    }

    static void CloakExpiryRestoresVulnerability()
    {
        var r = MakeRig();
        r.Cloak();
        collisionDetection.TickCloak(3.9f);
        Check("still cloaked just before the end", collisionDetection.Invulnerable);
        collisionDetection.TickCloak(.2f);
        Check("Cloak expiring ends invulnerability (timer clamped at 0)",
              !collisionDetection.Cloaked && !collisionDetection.Invulnerable && collisionDetection.cloakTimer == 0f);
        r.Hit("rock");
        Check("after Cloak a hazard damages the hull again", collisionDetection.lifeCounter == 1);

        // The running-world frame tick drains it too.
        collisionDetection.cloakTimer = 4f;
        r.Frame();
        Check("the per-frame tick runs the cloak clock (never up, never negative)",
              collisionDetection.cloakTimer <= 4f && collisionDetection.cloakTimer >= 0f);
        r.Dispose();
    }

    static void BlueAtomDuringCloak()
    {
        var r = MakeRig();
        r.Cloak();
        r.BlueAtom();
        Check("a blue atom during Cloak still raises the 5.8 s shield",
              collisionDetection.atomCheck && Mathf.Approximately(collisionDetection.invTimer, 5.8f) &&
              r.Shield != null && r.Shield.IsUp);
        Check("and does not cancel or reset Cloak",
              Mathf.Approximately(collisionDetection.cloakTimer, Rig.CloakSeconds));

        r.Hit("rock");
        Check("both up: no damage, and the shield shows the hit",
              collisionDetection.lifeCounter == 0 && r.Shield.FlashShowing);

        // Cloak ends first: the shield carries on.
        collisionDetection.TickCloak(4.1f);
        Check("Cloak ending under a shield leaves the shield up and the ship invulnerable",
              !collisionDetection.Cloaked && collisionDetection.atomCheck && r.Shield.IsUp &&
              collisionDetection.Invulnerable);
        r.Hit("rock2");
        Check("shield alone still absorbs", collisionDetection.lifeCounter == 0);

        collisionDetection.invTimer = 0f;
        r.Frame();
        Check("then the shield running out ends invulnerability",
              !collisionDetection.atomCheck && !r.Shield.IsUp && !collisionDetection.Invulnerable && r.box.enabled);
        r.Hit("rock3");
        Check("and hazards damage again", collisionDetection.lifeCounter == 1);
        r.Dispose();
    }

    static void CloakDuringShield()
    {
        var r = MakeRig();
        r.BlueAtom();
        collisionDetection.invTimer = 2f;   // part-way through the shield
        r.Cloak();
        Check("Cloak during a shield neither stretches nor cuts the shield's timer",
              Mathf.Approximately(collisionDetection.invTimer, 2f) && collisionDetection.atomCheck && r.Shield.IsUp);
        Check("and the shield's expiry blink still follows its own timer",
              Mathf.Approximately(r.Shield.Remaining, 2f));

        // Shield ends first: Cloak carries on.
        collisionDetection.invTimer = 0f;
        r.Frame();
        Check("the shield running out under Cloak drops the shield but not Cloak",
              !collisionDetection.atomCheck && !r.Shield.IsUp && collisionDetection.Cloaked &&
              collisionDetection.Invulnerable && collisionDetection.cloakTimer > 3f);
        Check("the hull hitbox is back in charge", r.box.enabled && !r.Shield.ShieldCollider.enabled);
        r.Hit("rock");
        Check("Cloak alone still absorbs", collisionDetection.lifeCounter == 0);

        collisionDetection.TickCloak(4.1f);
        Check("then Cloak ending leaves nothing stuck on", !collisionDetection.Invulnerable);
        r.Hit("rock2");
        Check("and hazards damage again", collisionDetection.lifeCounter == 1);
        r.Dispose();
    }

    static void RecloakNeverShortens()
    {
        collisionDetection.cloakTimer = 1f;
        collisionDetection.BeginCloak(4f);
        Check("re-cloaking refreshes to the full duration", Mathf.Approximately(collisionDetection.cloakTimer, 4f));
        collisionDetection.BeginCloak(2f);
        Check("a shorter cloak never cuts a longer one short", Mathf.Approximately(collisionDetection.cloakTimer, 4f));
        collisionDetection.cloakTimer = 0f;
    }

    static void ResetClearsCloak()
    {
        collisionDetection.cloakTimer = 3f;
        GameStateReset.Clear();
        Check("GameStateReset.Clear() ends any Cloak", collisionDetection.cloakTimer == 0f && !collisionDetection.Invulnerable);
    }

    // ------------------------------------------------------------------
    // HUD
    // ------------------------------------------------------------------

    static void HudHasNoShieldTimer(string path, string scene)
    {
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Check(scene + ": the blue-atom timer text (gotAtomText) is gone", SceneUtil.FindAny("gotAtomText") == null);

        int timerTexts = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (EditorUtility.IsPersistent(t) || !t.gameObject.scene.IsValid()) continue;
            string n = t.gameObject.name.ToLowerInvariant();
            if (n.Contains("atomtimer") || n.Contains("gotatom") || n.Contains("shieldtimer") ||
                n.Contains("cloak") || n.Contains("invtimer"))
                timerTexts++;
        }
        Check(scene + ": no shield/Cloak countdown text anywhere in the scene", timerTexts == 0);
        Check("collisionDetection no longer has a timer text to drive",
              typeof(collisionDetection).GetField("atomTimerText") == null);

        // The other read-outs are all still there and styled.
        var go = new GameObject("~HudTimerTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");
        var speed = SceneUtil.FindAny("SpeedText");
        var pause = SceneUtil.FindAny("PauseCounter") ?? SceneUtil.FindAny("PausesRemainingText");
        Check(scene + ": SPEED and PAUSES read-outs kept",
              speed != null && pause != null &&
              speed.GetComponent<Text>().text.StartsWith("SPEED") &&
              pause.GetComponent<Text>().text.StartsWith("PAUSES"));
        // Star dust shows when the run ends (Flight Complete), not in the HUD.
        Check(scene + ": the star dust row (CurrecnyGatheredText) is gone", SceneUtil.FindAny("CurrecnyGatheredText") == null);
        Check(scene + ": no star dust text anywhere in the in-run HUD", !HudShowsDust(styler.HudRoot));
        Check(scene + ": the pause bar is kept", pause != null && pause.transform.Find("PauseBar") != null);
        Object.DestroyImmediate(go);
    }

    // Any text under the read-out that reads like a star dust figure.
    public static bool HudShowsDust(RectTransform hudRoot)
    {
        if (hudRoot == null) return false;
        foreach (var t in hudRoot.GetComponentsInChildren<Text>(true))
        {
            string s = (t.text ?? "").ToUpperInvariant();
            string n = t.gameObject.name.ToLowerInvariant();
            if (s.Contains("★") || s.Contains("DUST") || n.Contains("currecny") || n.Contains("currency") || n.Contains("dust"))
                return true;
        }
        return false;
    }

    static void GameHudPanelTightened()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var speed = SceneUtil.FindAny("SpeedText");
        if (speed == null) { Check("gameS1: SpeedText found", false); return; }
        var rows = (RectTransform)speed.transform.parent;          // PanelTexts
        var panel = (RectTransform)rows.parent;                      // Model Panel

        var go = new GameObject("~HudTightTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");

        // The run score (ScoreHud) takes the top row; the star dust row is
        // gone (dust shows at the end of the run): SCORE, SPEED, PAUSES.
        Check("gameS1: the read-out stacks exactly SCORE, SPEED, PAUSES (" + rows.childCount + " rows)",
              rows.childCount == 3 && rows.GetChild(0).name == ScoreHud.RowName &&
              rows.GetChild(1).name == "SpeedText" && rows.GetChild(2).name == "PauseCounter");

        // The scene's 2-row stack (91 / 73) plus one row (33) and one gap
        // (7) for SCORE, with the panel's own padding round it unchanged.
        Check("gameS1: the panel is one row taller for SCORE (" + panel.rect.height + ", scene 91)",
              Mathf.Approximately(panel.rect.height, 131f) && Mathf.Approximately(rows.rect.height, 113f));
        float topPad = (panel.rect.height - rows.rect.height) * .5f - rows.anchoredPosition.y;
        float bottomPad = (panel.rect.height - rows.rect.height) * .5f + rows.anchoredPosition.y;
        Check("gameS1: padding round the rows unchanged (top " + topPad.ToString("F1") + ", bottom " +
              bottomPad.ToString("F1") + ")",
              Mathf.Abs(topPad - 13.5f) < .1f && Mathf.Abs(bottomPad - 4.5f) < .1f);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rows);
        bool fits = true;
        float lowest = float.MaxValue;
        for (int i = 0; i < rows.childCount; i++)
        {
            var row = (RectTransform)rows.GetChild(i);
            var text = row.GetComponent<Text>();
            if (row.rect.height + .5f < text.preferredHeight) fits = false;
            lowest = Mathf.Min(lowest, row.localPosition.y + row.rect.yMin);
        }
        Check("gameS1: every row still fits its text", fits);
        Check("gameS1: the last row reaches the bottom of the stack (no empty gap where the timer or star dust was)",
              Mathf.Abs(lowest - rows.rect.yMin) < 1f);

        Object.DestroyImmediate(go);
    }
}
