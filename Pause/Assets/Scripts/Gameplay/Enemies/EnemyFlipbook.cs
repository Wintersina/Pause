using UnityEngine;

// Plays an enemy's flipbook strip with limited-animation timing: every drawing
// is held for a whole number of 24 fps ticks (on 2s and 3s), never tweened.
//
//   idle (frames 0-3) loops: key pose hold, anticipation, snap, settle
//     Steel Hound uses only its two steady hover poses (frames 0-1).
//   tell (frames 4-5) is the attack/anticipation beat, triggered per role:
//     Accent      rocks: a random glint/pulse every few seconds
//     Periodic    fighters, big: a wind-up every few seconds
//     NearPlayer  mines (arming), aliens (chomp): loops while the ship is close
//     Chasing     other chasers: loop a lunge while hunting
//     IdleOnly    Steel Hound: stays in its subtle hover loop while hunting
//   An enemy that attacks (EnemyBrain) is BrainDriven instead: it plays no
//   tell of its own -- the brain holds cell 4 for the windup (a mine loops
//   4-5, waking -> charging) and cell 5 for the release (Drive).
//   hit (frame 6) is a flat BONE flash, shown by Flash() -- for anything that
//   hits an enemy without destroying it on the same frame (weapons, shields).
//
// Runs on scaled time, so it freezes with the world at timeScale 0 and slows
// with the ultimate's slow motion like everything else on the board.
[DisallowMultipleComponent]
public class EnemyFlipbook : MonoBehaviour
{
    public const float TickSeconds = 1f / 24f;

    public enum TellMode { Accent, Periodic, NearPlayer, Chasing, IdleOnly }
    public enum DrivePhase { None, Windup, Release }

    public TellMode tellMode = TellMode.Periodic;
    [Tooltip("NearPlayer: world-unit distance to the ship that triggers the tell.")]
    public float nearDistance = 2.6f;
    [Tooltip("Accent/Periodic: seconds between tells (min, max).")]
    public Vector2 tellGap = new Vector2(2.5f, 5f);

    enum State { Idle, Tell, Hit }

    protected SpriteRenderer sr;
    Sprite[] frames;
    int[] idleTicks = { 6, 3, 2, 3 };
    int[] tellTicks = { 2, 3 };
    State state;
    int step;
    float hold;
    float untilTell;
    ChaserEnemy chaser;
    DrivePhase drive;
    bool driveLoops;

    // Its tell belongs to its attack: no timed / proximity tell.
    public bool BrainDriven { get; private set; }

    // The brain says whether this individual attacks at all (a Bile Mite
    // that is not a spitter keeps its ordinary near-pilot tell).
    public void SetBrainDriven(bool driven) { BrainDriven = driven; }

    public DrivePhase Driving => drive;
    public int CurrentFrame { get; private set; }
    public bool Telling => state == State.Tell;
    public bool HasFrames => frames != null && frames.Length >= EnemyRoster.FrameCount && frames[0] != null;

    protected virtual void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (!HasFrames)
        {
            var def = EnemyIdentity.Of(gameObject);
            if (def != null) Init(def);
        }
    }

    public void Init(EnemyDef def)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        frames = EnemyArt.Frames(def);
        idleTicks = EnemyRoster.FlipbookIdleTicks(def);
        tellTicks = EnemyRoster.TellTicks(def.role);
        tellMode = ModeFor(def.role);
        if (def.key == "space_chaser") tellMode = TellMode.IdleOnly;
        switch (def.role)
        {
            case EnemyRole.Rock: tellGap = new Vector2(3f, 7f); break;
            case EnemyRole.Big: tellGap = new Vector2(3f, 5.5f); break;
            case EnemyRole.Alien: nearDistance = 2.2f; break;
            case EnemyRole.Mine: nearDistance = 2.8f; break;
        }
        chaser = GetComponent<ChaserEnemy>();
        // an attacker's tell belongs to its attack (EnemyBrain.Drive)
        var behaviour = EnemyBehaviours.For(def);
        // (a mine keeps arming near the ship as well: the same waking ->
        // charging loop its windup plays)
        BrainDriven = behaviour != null && behaviour.Attacks && def.role != EnemyRole.Chaser && def.role != EnemyRole.Mine;
        driveLoops = def.role == EnemyRole.Mine;
        drive = DrivePhase.None;
        state = State.Idle;
        // Desynchronise neighbours, except aliens: a line of invaders
        // wiggling in lockstep is the point.
        step = def.role == EnemyRole.Alien ? 0 : Random.Range(0, idleTicks.Length);
        hold = idleTicks[step] * TickSeconds;
        untilTell = Random.Range(tellGap.x, tellGap.y);
        Show(step);
    }

    public static TellMode ModeFor(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return TellMode.Accent;
            case EnemyRole.Mine: case EnemyRole.Alien: return TellMode.NearPlayer;
            case EnemyRole.Chaser: return TellMode.Chasing;
            default: return TellMode.Periodic;
        }
    }

    // A one- to two-tick white pop; the flipbook returns to its idle after.
    public void Flash()
    {
        if (!HasFrames) return;
        state = State.Hit;
        hold = EnemyRoster.HitTicks * TickSeconds;
        Show(EnemyRoster.HitFrame);
    }

    public static void Flash(GameObject go)
    {
        var fb = go != null ? go.GetComponent<EnemyFlipbook>() : null;
        if (fb != null) fb.Flash();
    }

    // Starts the tell now (the next idle step boundary would anyway).
    public void Tell()
    {
        if (!HasFrames || state == State.Hit) return;
        state = State.Tell;
        step = 0;
        hold = tellTicks[0] * TickSeconds;
        Show(EnemyRoster.TellFrame);
    }

    // The brain's attack takes the drawing over: Windup shows the tell
    // (cell 4; a mine loops 4-5), Release the discharge (cell 5), None hands
    // it back to the idle loop. A hit flash still shows over it.
    public void Drive(DrivePhase phase)
    {
        if (!HasFrames || drive == phase) return;
        drive = phase;
        if (state == State.Hit) return;   // the flash finishes first
        ShowDriven();
    }

    void ShowDriven()
    {
        step = 0;
        switch (drive)
        {
            case DrivePhase.Windup:
                state = State.Tell;
                hold = tellTicks[0] * TickSeconds;
                Show(EnemyRoster.TellFrame);
                break;
            case DrivePhase.Release:
                state = State.Tell;
                hold = tellTicks[1] * TickSeconds;
                Show(EnemyRoster.TellFrame + 1);
                break;
            default:
                state = State.Idle;
                hold = idleTicks[0] * TickSeconds;
                untilTell = Random.Range(tellGap.x, tellGap.y);
                Show(0);
                break;
        }
    }

    void Update()
    {
        Advance(Time.deltaTime);
    }

    // Public so edit-mode tests can step it deterministically.
    public void Advance(float dt)
    {
        if (!HasFrames || dt <= 0f) return;
        if (drive != DrivePhase.None && state != State.Hit)
        {
            // held by the brain; only a mine's windup animates (4 <-> 5)
            if (drive != DrivePhase.Windup || !driveLoops) return;
            hold -= dt;
            if (hold > 0f) return;
            step = 1 - step;
            hold += tellTicks[step] * TickSeconds;
            Show(EnemyRoster.TellFrame + step);
            return;
        }
        untilTell -= dt;
        hold -= dt;
        int guard = 0;
        while (hold <= 0f && guard++ < 16)
        {
            switch (state)
            {
                case State.Hit:
                    if (drive != DrivePhase.None) { ShowDriven(); return; }
                    state = State.Idle;
                    step = 0;
                    hold += idleTicks[0] * TickSeconds;
                    Show(0);
                    break;
                case State.Idle:
                    if (WantsTell())
                    {
                        state = State.Tell;
                        step = 0;
                        hold += tellTicks[0] * TickSeconds;
                        Show(EnemyRoster.TellFrame);
                        break;
                    }
                    step = (step + 1) % idleTicks.Length;
                    hold += idleTicks[step] * TickSeconds;
                    Show(step);
                    break;
                case State.Tell:
                    if (step == 0)
                    {
                        step = 1;
                        hold += tellTicks[1] * TickSeconds;
                        Show(EnemyRoster.TellFrame + 1);
                    }
                    else if (Loops() && WantsTell())
                    {
                        step = 0;
                        hold += tellTicks[0] * TickSeconds;
                        Show(EnemyRoster.TellFrame);
                    }
                    else
                    {
                        state = State.Idle;
                        step = 0;
                        hold += idleTicks[0] * TickSeconds;
                        untilTell = Random.Range(tellGap.x, tellGap.y);
                        Show(0);
                    }
                    break;
            }
        }
    }

    bool Loops() => tellMode == TellMode.NearPlayer || tellMode == TellMode.Chasing;

    protected virtual bool WantsTell()
    {
        if (BrainDriven) return false;
        switch (tellMode)
        {
            case TellMode.NearPlayer:
                var p = Player();
                return p != null && (p.position - transform.position).sqrMagnitude < nearDistance * nearDistance;
            case TellMode.Chasing:
                return chaser != null && chaser.IsChasing;
            case TellMode.IdleOnly:
                return false;
            default:
                return untilTell <= 0f;
        }
    }

    void Show(int frame)
    {
        CurrentFrame = frame;
        if (sr != null && frames != null && frame < frames.Length) sr.sprite = frames[frame];
    }

    static Transform player;
    static float nextLookup;

    static Transform Player()
    {
        if (player != null) return player;
        if (Time.unscaledTime < nextLookup) return null;
        nextLookup = Time.unscaledTime + .5f;
        var mover = Object.FindFirstObjectByType<movePlayer>();
        player = mover != null ? mover.transform : null;
        return player;
    }
}
