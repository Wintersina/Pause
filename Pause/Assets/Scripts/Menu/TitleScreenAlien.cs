using UnityEngine;

// The alien drifting across the home screen (startS4): the Space world's
// roster alien (EnemyRoster "space_alien"), drawn from its EnemyArt flipbook
// and looping its idle through EnemyFlipbook, exactly as it flies in-game.
// It replaced the old alien1.prefab invader; TitleScreenMoveDown on the same
// object keeps the old drift (a slow rise up the screen).
//
// Purely cosmetic: no collider, tag or mover, so nothing in the menu can
// treat it as a hazard.
[RequireComponent(typeof(SpriteRenderer))]
public class TitleScreenAlien : MonoBehaviour
{
    public const string RosterKey = "space_alien";

    public EnemyFlipbook Flipbook { get; private set; }
    public EnemyDef Def { get; private set; }

    void Awake()
    {
        Build();
    }

    // Idempotent; public so edit-mode tests (where Awake doesn't run on an
    // opened scene) can build it.
    public EnemyFlipbook Build()
    {
        Def = EnemyRoster.Find(RosterKey);
        if (Def == null || EnemyArt.Frames(Def) == null) return null;
        Flipbook = GetComponent<EnemyFlipbook>();
        if (Flipbook == null) Flipbook = gameObject.AddComponent<EnemyFlipbook>();
        Flipbook.Init(Def);
        return Flipbook;
    }
}
