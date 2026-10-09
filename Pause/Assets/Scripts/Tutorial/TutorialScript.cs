using System.Collections.Generic;
using System.Text;

// Everything the tutorial robot says, in order, and what the player has to do
// before it moves on. Edit the table in Steps; nothing else needs touching.
//
//   id       - stable name for the step (tests and logs)
//   line     - what the robot says. ASCII only, <= 60 characters. Wrap a word
//              in *asterisks* to highlight it in sodium orange.
//   advance  - the player action that completes the step (see TutorialAdvance)
//   amount   - how much of that action (seconds, or a count)
//   cue      - what the scene does to set the action up (see TutorialCue)
//
// A step completes once its line has been fully spoken, the player has had a
// moment to read it, and the advance condition is met. Conditions count from
// the moment the step starts, so doing the thing while the robot is still
// talking counts.
public static class TutorialScript
{
    public static readonly TutorialStep[] Steps =
    {
        //        id          line                                             advance when the player...            cue
        new TutorialStep("hold",     "*Hold* anywhere to fly.",                       TutorialAdvance.FlySeconds, 1.2f,     TutorialCue.TouchPulse),
        new TutorialStep("freeze",   "Let go. Time *freezes*!",                       TutorialAdvance.LetGo, .6f,           TutorialCue.None),
        new TutorialStep("teleport", "Touch to teleport. Each costs a *pause*.",      TutorialAdvance.SpendPause, 1f,       TutorialCue.PointAtPauses),
        new TutorialStep("dust",     "*Star dust*! Grab it, that's your cash.",       TutorialAdvance.CollectStar, 1f,      TutorialCue.SpawnStars),
        new TutorialStep("heal",     "Glowing *atoms* help! *Green* fixes hull.",       TutorialAdvance.CollectGreenAtom, 1f, TutorialCue.SpawnGreenAtom),
        new TutorialStep("shield",   "*Blue* wraps you in a shield.",                 TutorialAdvance.CollectBlueAtom, 1f,  TutorialCue.SpawnBlueAtom),
        new TutorialStep("refill",   "*Red* refills pauses. Zero means no freezing!", TutorialAdvance.CollectRedAtom, 1f,   TutorialCue.SpawnRedAtom),
        new TutorialStep("enemies",  "An *alien*! Dodge it, or teleport onto it!",    TutorialAdvance.EnemyGone, 1f,        TutorialCue.SpawnEnemy),
        new TutorialStep("power",    "Atoms charge your *weapon*. Grab them!",        TutorialAdvance.FirePower, 1f,        TutorialCue.SpawnChargeAtoms),
    };

    public const int MaxSteps = 9;

    // The power step's charge, in atoms: Hints arms the ship's weapon so that
    // this many green / blue atoms (ShipPowerController.AtomCutSeconds each)
    // fill it, and it goes off once.
    public const int PowerAtoms = 3;
    public const int MaxLineLength = 60;

    // The single rule every advance condition goes through. `start` is the
    // snapshot taken when the step began, `now` the current one.
    public static bool IsMet(TutorialStep step, TutorialSignals start, TutorialSignals now)
    {
        switch (step.advance)
        {
            case TutorialAdvance.FlySeconds:
                return now.flySeconds - start.flySeconds >= step.amount;
            case TutorialAdvance.LetGo:
                // Frozen right now, and for long enough to see the world stop.
                return !now.worldMoving && now.frozenSeconds >= step.amount;
            case TutorialAdvance.Touch:
                return now.presses - start.presses >= step.amount;
            case TutorialAdvance.SpendPause:
                return now.pausesSpent - start.pausesSpent >= step.amount;
            case TutorialAdvance.CollectStar:
                return now.starsCollected - start.starsCollected >= step.amount;
            case TutorialAdvance.CollectGreenAtom:
                return now.greenAtomsCollected - start.greenAtomsCollected >= step.amount;
            case TutorialAdvance.CollectBlueAtom:
                return now.blueAtomsCollected - start.blueAtomsCollected >= step.amount;
            case TutorialAdvance.CollectRedAtom:
                return now.redAtomsCollected - start.redAtomsCollected >= step.amount;
            case TutorialAdvance.EnemyGone:
                return now.enemiesGone - start.enemiesGone >= step.amount;
            case TutorialAdvance.FirePower:
                return now.powersFired - start.powersFired >= step.amount;
        }
        return false;
    }

    // ---------------------------------------------------------------------
    // Turning a line into something the robot can speak
    // ---------------------------------------------------------------------

    // Sodium orange (TutorialPalette.Orange); a const so the rich text is
    // built without allocation per line. TutorialRobotTest checks it matches.
    public const string HighlightColor = "#F2862B";

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
    FlySeconds,      // fly (finger down, world moving) for `amount` seconds in total
    LetGo,           // lift the finger and stay frozen for `amount` seconds
    Touch,           // put a finger down `amount` times
    SpendPause,      // the pause counter drops by `amount`
    CollectStar,     // pick up `amount` star-dust stars
    CollectGreenAtom, // pick up `amount` green (heal) atoms
    CollectBlueAtom, // pick up `amount` blue (shield) atoms
    CollectRedAtom,  // pick up `amount` red (pause) atoms
    EnemyGone,       // `amount` tutorial enemies gone: blasted, rammed or dodged off the bottom
    FirePower,       // the ship's weapon (the ultimate) goes off `amount` times
}

public enum TutorialCue
{
    None,
    TouchPulse,      // a pulsing "touch here" ring while the world is frozen
    PointAtPauses,   // arrow at the PAUSES readout, plus the touch pulse
    SpawnStars,      // start the star clusters and point at the dust readout
    SpawnGreenAtom,  // keep a green (heal) atom coming until one is caught, arrow on it
    SpawnBlueAtom,   // same for the blue (shield) atom
    SpawnRedAtom,    // same for the red (pause) atom
    SpawnEnemy,      // one alien drops slowly through the ship's lane (TutorialEnemy), arrow on it
    SpawnChargeAtoms, // arm the weapon for PowerAtoms atoms and keep green / blue atoms coming
}

public struct TutorialStep
{
    public readonly string id;
    public readonly string line;
    public readonly TutorialAdvance advance;
    public readonly float amount;
    public readonly TutorialCue cue;

    public TutorialStep(string id, string line, TutorialAdvance advance, float amount, TutorialCue cue)
    {
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
    public float flySeconds;      // total time with a finger down
    public float frozenSeconds;   // how long the world has been frozen right now (0 while moving)
    public int presses;           // finger-down edges
    public int pausesSpent;
    public int starsCollected;
    public int greenAtomsCollected;
    public int blueAtomsCollected;
    public int redAtomsCollected;
    public int enemiesGone;
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
