using UnityEngine;

// Shared tuning every elite reads (all worlds, all brains): one knob for how often they attack.
public static class EliteTuning
{
    // Attacks per minute x this: every gap between two attacks (the cooldown after an attack, after a hide, the first one, the
    // roaming brains' pauses) is divided by it. 1.7 -> 1.2 (Oct 20): the fights ran too busy; each elite now also has its own tempo (script.tempo). The wind-ups, the telegraphs and the hazards themselves are untouched.
    public static float AttackRate = 1.2f;

    public static float Gap(float seconds) { return seconds / Mathf.Max(.1f, AttackRate); }
}
