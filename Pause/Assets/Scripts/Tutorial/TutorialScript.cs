using System.Collections.Generic;
using System.Text;

// Everything the tutorial robot says, in order, and what the player has to do
// before it moves on. Edit the table in Steps; nothing else needs touching.
//
//   id       - stable name for the step (tests and logs)
//   line     - what the robot says. ASCII only, <= 70 characters. Wrap a word
//              in *asterisks* to highlight it in sodium orange.
//   advance  - the player action that completes the step (see TutorialAdvance)
//   amount   - how much of that action (a distance, seconds, or a count)
//   cue      - what the scene does to set the action up (see TutorialCue)
//   par      - seconds a competent player needs (documentation + the pacing test)
//
// A step completes ONLY when its line has been fully spoken, the player has
// had a moment to read it, and the player has really done the thing. There
// is no timeout: nothing advances on a clock. An idle player is re-prompted
// (Hints.IdleHintSeconds) and, with the finger up, the whole tutorial is
// paused with the world. Conditions count from the moment the step starts,
// so doing the thing while the robot is still talking counts.
public static class TutorialScript
{
    // THE STEP TABLE (docs/tutorial-rebuild.md has the same table). "par" is
    // how long a competent player takes to do what the step asks (the line is
    // spoken meanwhile). There is no timeout: a step ends only on the action.
    // A step also waits for its line to be spoken and read (Hints.readSeconds).
    //
    // ATOM ORDER -- each atom is introduced once, by one scripted atom that
    // rushes in from the top as its line starts (no random atoms at all):
    //   1 green repair   the ship STARTS damaged, so the repair is seen working
    //   2 red pause      right after the pause lessons spent some (wall jump, alien blast)
    //   3 blue shield    the next thing that saves a run: a free hit
    //   4 violet charge  the weapon: last, because it is the reward -- one
    //                    atom charges the armed weapon and it goes off
    public static readonly TutorialStep[] Steps =
    {
        //        id         line                                                  advance when the player...          amount cue                          par
        new TutorialStep("hold",    "*Hold* anywhere to fly around.",                  TutorialAdvance.FlyDistance,      FlyDistanceUnits, TutorialCue.TouchPulse,     3.0f),
        new TutorialStep("dust",    "*Star dust*! Grab it, that's your cash.",         TutorialAdvance.CollectStar,      3f,   TutorialCue.SpawnStars,      4.0f),
        new TutorialStep("heal",    "The *green* atom repairs a heart.",               TutorialAdvance.HealHeart,        1f,   TutorialCue.SpawnGreenAtom,  4.0f),
        new TutorialStep("freeze",  "A wall! *Let go* to pause time.",       TutorialAdvance.WallFrozen,       FrozenSeconds, TutorialCue.SpawnWall,     4.0f),
        new TutorialStep("jump",    "Touch *above* the wall to jump over it.",         TutorialAdvance.CrossWall,        1f,   TutorialCue.PointAtWall,     4.0f),
        new TutorialStep("strike",  "*Let go*, then touch *on* the alien.", TutorialAdvance.TeleportKill,     1f,   TutorialCue.SpawnEnemy,      5.0f),
        new TutorialStep("refill",  "The *red* atom refills your pauses.",             TutorialAdvance.CollectRedAtom,   1f,   TutorialCue.SpawnRedAtom,    4.0f),
        new TutorialStep("shield",  "The *blue* atom wraps you in a shield.",          TutorialAdvance.CollectBlueAtom,  1f,   TutorialCue.SpawnBlueAtom,   4.0f),
        new TutorialStep("power",   "The *violet* atom charges your weapon.",          TutorialAdvance.FirePower,        1f,   TutorialCue.SpawnCapacitorAtom, 4.5f),
    };

    // The closing line, said once the last step is done, as the ship lifts off.
    public const string ClosingLine = "Nice flying! *Lift off*!";
    // Hold step: steer the ship at least this far (world units).
    public const float FlyDistanceUnits = 2.5f;
    // Freeze step: stay frozen this long with the wall in view.
    public const float FrozenSeconds = .5f;
    // The retry line when the wall was broken up instead of jumped.
    public const string WallRetryLine = "Not through it! *Let go*, touch *above* the wall.";

    // Said when the player has burned every pause before finishing the wall
    // or alien lesson: a red atom drops in and this asks them to grab it.
    public const string PauseRescueLine = "Out of pauses! *Grab* the red atom to go on.";

    public const int MaxSteps = 10;
    // One sentence per line: short enough for the bubble.
    public const int MaxLineLength = 70;

    // Seconds from the tutorial's start to its first line, and from its last
    // step to the Tutorial Complete card.
    public const float IntroSeconds = .6f;
    public const float EndingSeconds = .6f;

    // The atom order above, by atom kind (the test pins it to the steps).
    public static readonly TutorialAtom[] AtomOrder =
        { TutorialAtom.Green, TutorialAtom.Red, TutorialAtom.Blue, TutorialAtom.Cooldown };

    // Seconds the robot takes to say a line (RobotSpeaker's own pacing).
    public static float SpeakSeconds(SpokenLine l)
    {
        float t = RobotSpeaker.FirstSyllableDelay;
        for (int i = 0; i < l.SyllableCount; i++)
            t += RobotSpeaker.SyllableSeconds + (l.stressed[i] ? RobotSpeaker.StressExtra : 0f) + l.pauseAfter[i];
        return t;
    }

    // May the step end now? Its line has been spoken and read AND the player
    // did the thing. Never on a timer.
    public static bool CanAdvance(TutorialStep step, TutorialSignals start, TutorialSignals now,
                                  bool lineFinished, float sinceLineFinished, float readSeconds)
    {
        if (!lineFinished || sinceLineFinished < readSeconds) return false;
        return IsMet(step, start, now);
    }

    // The power step's charge: one violet atom's cut (ShipPowerController.
    // CooldownAtomCutSeconds) is exactly what the armed weapon needs.
    public const float ArmSeconds = ShipPowerController.CooldownAtomCutSeconds;

    // The single rule every advance condition goes through. `start` is the
    // snapshot taken when the step began, `now` the current one.
    public static bool IsMet(TutorialStep step, TutorialSignals start, TutorialSignals now)
    {
        switch (step.advance)
        {
            case TutorialAdvance.FlyDistance:
                return now.flyUnits - start.flyUnits >= step.amount;
            case TutorialAdvance.CollectStar:
                return now.starsCollected - start.starsCollected >= step.amount;
            case TutorialAdvance.HealHeart:
                return now.heartsHealed - start.heartsHealed >= step.amount;
            case TutorialAdvance.WallFrozen:
                return now.wallFrozenSeconds >= step.amount;
            case TutorialAdvance.CrossWall:
                return now.wallsCrossed - start.wallsCrossed >= step.amount;
            case TutorialAdvance.CollectGreenAtom:
                return now.greenAtomsCollected - start.greenAtomsCollected >= step.amount;
            case TutorialAdvance.CollectBlueAtom:
                return now.blueAtomsCollected - start.blueAtomsCollected >= step.amount;
            case TutorialAdvance.CollectRedAtom:
                return now.redAtomsCollected - start.redAtomsCollected >= step.amount;
            case TutorialAdvance.TeleportKill:
                return now.enemiesTeleported - start.enemiesTeleported >= step.amount;
            case TutorialAdvance.FirePower:
                return now.powersFired - start.powersFired >= step.amount;
        }
        return false;
    }

    // ---------------------------------------------------------------------
    // Turning a line into something the robot can speak
    // ---------------------------------------------------------------------

    // Amber (TutorialPalette.Orange); a const so the rich text is
    // built without allocation per line. TutorialRobotTest checks it matches.
    public const string HighlightColor = "#FFB83D";

    // Rich text for the bubble: *word* becomes a sodium-orange highlight.
    public static string ToRichText(string line)
    {
        var sb = new StringBuilder(line.Length + 32);
        bool open = false;
        foreach (char c in line)
        {
            if (c == '*')
            {
                sb.Append(open ? "</color>" : "<color=" + HighlightColor + ">");
                open = !open;
            }
            else sb.Append(c);
        }
        if (open) sb.Append("</color>");
        return sb.ToString();
    }

    public static string ToPlainText(string line)
    {
        return line.Replace("*", "");
    }

    // Splits a line (plain text) into syllables for the reveal and the voice.
    // A rough English heuristic is plenty: each vowel group is a beat, a
    // single consonant between two vowels starts the next beat, a cluster is
    // split down the middle, and a silent final "e" ("time", "there") does not
    // get a beat of its own. Spaces and punctuation ride on the syllable
    // before them, so every syllable ends where the reveal should stop.
    public static SpokenLine Speak(string line)
    {
        string plain = ToPlainText(line);
        var ends = new List<int>();
        var stressed = new List<bool>();
        var vowels = new List<char>();
        var pauses = new List<float>();

        bool highlightOpen = false;
        int plainIndex = 0;
        var highlighted = new bool[plain.Length];
        foreach (char c in line)
        {
            if (c == '*') { highlightOpen = !highlightOpen; continue; }
            highlighted[plainIndex++] = highlightOpen;
        }

        int i = 0;
        while (i < plain.Length)
        {
            // Leading non-letters (never happens mid-line: they attach to the
            // previous syllable) -- fold into the first syllable.
            int wordStart = i;
            while (wordStart < plain.Length && !IsLetter(plain[wordStart])) wordStart++;
            int wordEnd = wordStart;
            while (wordEnd < plain.Length && IsLetter(plain[wordEnd])) wordEnd++;
            if (wordStart >= plain.Length) break;

            var cuts = SyllableCuts(plain, wordStart, wordEnd);
            int tail = wordEnd;
            while (tail < plain.Length && !IsLetter(plain[tail])) tail++;

            bool contentWord = wordEnd - wordStart >= 4 || highlighted[wordStart];
            for (int s = 0; s < cuts.Count; s++)
            {
                int end = s + 1 < cuts.Count ? cuts[s + 1] : tail;
                ends.Add(end);
                stressed.Add(s == 0 && contentWord);
                vowels.Add(FirstVowel(plain, cuts[s], s + 1 < cuts.Count ? cuts[s + 1] : wordEnd));
                pauses.Add(s + 1 < cuts.Count ? 0f : PauseAfter(plain, wordEnd, tail));
            }
            i = tail;
        }

        // The last beat of an exclamation lands harder.
        if (plain.TrimEnd().EndsWith("!") && stressed.Count > 0) stressed[stressed.Count - 1] = true;

        var glyphs = new int[ends.Count];
        for (int s = 0; s < ends.Count; s++) glyphs[s] = CountGlyphs(plain, ends[s]);

        return new SpokenLine(ToRichText(line), plain, ends.ToArray(), glyphs, stressed.ToArray(),
                              vowels.ToArray(), pauses.ToArray(), CountGlyphs(plain, plain.Length));
    }

    static List<int> SyllableCuts(string s, int start, int end)
    {
        var cuts = new List<int> { start };
        // Vowel groups.
        var groups = new List<int>();   // index of each group's first vowel
        var groupEnds = new List<int>();
        int k = start;
        while (k < end)
        {
            if (IsVowel(s, k, start))
            {
                groups.Add(k);
                while (k < end && IsVowel(s, k, start)) k++;
                groupEnds.Add(k);
            }
            else k++;
        }
        // Silent final e: "time", "there", "one" ... but not "the" (one group).
        if (groups.Count > 1 && groups[groups.Count - 1] == end - 1 && char.ToLower(s[end - 1]) == 'e'
            && groupEnds[groups.Count - 1] - groups[groups.Count - 1] == 1)
        {
            groups.RemoveAt(groups.Count - 1);
            groupEnds.RemoveAt(groupEnds.Count - 1);
        }
        for (int g = 1; g < groups.Count; g++)
        {
            int consonants = groups[g] - groupEnds[g - 1];
            int cut = groupEnds[g - 1] + (consonants <= 1 ? 0 : consonants / 2);
            if (cut > cuts[cuts.Count - 1] && cut < end) cuts.Add(cut);
        }
        return cuts;
    }

    static bool IsLetter(char c) { return char.IsLetter(c) || c == '\''; }

    static bool IsVowel(string s, int i, int wordStart)
    {
        char c = char.ToLower(s[i]);
        if (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u') return true;
        return c == 'y' && i > wordStart;
    }

    static char FirstVowel(string s, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            char c = char.ToLower(s[i]);
            if ("aeiouy".IndexOf(c) >= 0) return c;
        }
        return 'a';
    }

    static float PauseAfter(string s, int from, int to)
    {
        float pause = 0f;
        for (int i = from; i < to; i++)
        {
            char c = s[i];
            if (c == ',') pause = System.Math.Max(pause, .12f);
            else if (c == '.' || c == '!' || c == '?') pause = System.Math.Max(pause, .24f);
        }
        return pause;
    }

    // Glyph quads UI Text emits for plain[0, end): one per visible character,
    // none for whitespace.
    public static int CountGlyphs(string plain, int end)
    {
        int n = 0;
        for (int i = 0; i < end && i < plain.Length; i++)
            if (!char.IsWhiteSpace(plain[i])) n++;
        return n;
    }
}

public enum TutorialAdvance
{
    FlyDistance,     // steer the ship `amount` world units with a finger down
    CollectStar,     // pick up `amount` star-dust pieces
    HealHeart,       // a green atom repairs a missing heart (`amount` times)
    WallFrozen,      // lift the finger (world frozen) `amount` seconds with the wall in view
    CrossWall,       // pause-jump from below the wall to above it, `amount` times
    CollectGreenAtom, // pick up `amount` green (heal) atoms
    CollectBlueAtom, // pick up `amount` blue (shield) atoms
    CollectRedAtom,  // pick up `amount` red (pause) atoms
    TeleportKill,    // pause-jump onto the alien and erase it (TeleportFx.Strike), `amount` times
    FirePower,       // the ship's weapon (the ultimate) goes off `amount` times
}

public enum TutorialCue
{
    None,
    TouchPulse,      // a pulsing "touch here" ring while the world is frozen
    SpawnStars,      // rush a stream of dust from the top, fast, onto the ship (arrows on every piece)
    SpawnGreenAtom,  // drop one green (heal) atom in from the top, arrow on it (the ship is already damaged)
    SpawnBlueAtom,   // one blue (shield) atom
    SpawnRedAtom,    // one red (pause) atom
    SpawnCapacitorAtom, // arm the weapon for one violet atom and drop that one atom in, arrow on it
    SpawnWall,       // a wall of aliens rushes in from the top and stops above the ship: no way round
    PointAtWall,     // the wall waits; the touch pulse says where to touch
    SpawnEnemy,      // one alien slides in from the top and hangs in the ship's reach, arrow on it
}

public struct TutorialStep
{
    public readonly string id;
    public readonly string line;
    public readonly TutorialAdvance advance;
    public readonly float amount;
    public readonly TutorialCue cue;
    public readonly float par;       // seconds a competent player needs

    public TutorialStep(string id, string line, TutorialAdvance advance, float amount, TutorialCue cue, float par)
    {
        this.par = par;
        this.id = id;
        this.line = line;
        this.advance = advance;
        this.amount = amount;
        this.cue = cue;
    }
}

// Running totals the tutorial watches. All times are unscaled seconds.
public struct TutorialSignals
{
    public bool pressed;
    public bool worldMoving;
    public float flyUnits;        // total distance steered with a finger down
    public float frozenSeconds;   // how long the world has been frozen right now (0 while moving)
    public float wallFrozenSeconds; // ... of that, with the wall in view
    public int presses;           // finger-down edges
    public int pausesSpent;
    public int starsCollected;
    public int heartsHealed;
    public int greenAtomsCollected;
    public int blueAtomsCollected;
    public int redAtomsCollected;
    public int wallsCrossed;
    public int enemiesTeleported;   // aliens erased by a pause-jump onto them
    public int powersFired;
}
// A line, pre-chewed for the speaker so nothing is computed while talking.
public sealed class SpokenLine
{
    public readonly string richText;
    public readonly string plainText;
    public readonly int[] syllableEnds;     // plain-text index each syllable reveals up to
    public readonly int[] glyphsVisible;    // glyph quads visible once that syllable is out
    public readonly bool[] stressed;
    public readonly char[] vowel;
    public readonly float[] pauseAfter;     // extra beat after the syllable (punctuation)
    public readonly int totalGlyphs;

    public SpokenLine(string richText, string plainText, int[] syllableEnds, int[] glyphsVisible,
                      bool[] stressed, char[] vowel, float[] pauseAfter, int totalGlyphs)
    {
        this.richText = richText;
        this.plainText = plainText;
        this.syllableEnds = syllableEnds;
        this.glyphsVisible = glyphsVisible;
        this.stressed = stressed;
        this.vowel = vowel;
        this.pauseAfter = pauseAfter;
        this.totalGlyphs = totalGlyphs;
    }

    public int SyllableCount { get { return syllableEnds.Length; } }
}
