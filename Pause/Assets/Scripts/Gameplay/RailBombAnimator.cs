using UnityEngine;

// The rail mine's flipbook: the current world's mine from EnemyRoster
// (Resources/Enemies/<world>_mine.png), idling dormant -> lit -> pulse and
// arming to its burst while the ship is close. Mounting and movement remain
// the responsibility of RailMineMount and the straight-line scroller.
public class RailBombAnimator : EnemyFlipbook
{
    protected override void Awake()
    {
        // An old mine Animator would overwrite the flipbook's sprite.
        var legacy = GetComponent<Animator>();
        if (legacy != null) legacy.enabled = false;

        base.Awake();
        if (!HasFrames)
        {
            var def = EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Mine);
            if (def != null) Init(def);
        }
        if (sr != null) sr.sortingOrder = 12;
    }
}
