// The one "a world transition is in progress" signal.
//
// True from the moment a world's level is over and the way on has opened
// until the next world is live and the pilot has control again:
//
//   portal      WorldManager's Portal stage: the portal is open (boss over)
//               until the ship flies through it (Advance is instant)
//   planetfall  the planet's approach (the same Portal stage) and the whole
//               descent -- commit, entry shroud, cloud decks, the switch,
//               the breakthrough -- until it hands the ship back (Finish)
//   lift-off    the beat, the rise and the calm interlude (all inside the
//               Portal stage), then the gateway it opens (portal or
//               planetfall, as above)
//
// It is DERIVED from the live transition objects every time it is read --
// never a flag that something has to remember to clear -- so it can't be
// left stuck on: a scene reload destroys WorldManager / Planetfall /
// Liftoff (their statics read null through Unity's destroyed-object
// check), a dev skip (DevJumpToFinal) closes the portal and kills the
// sequences, and a finished sequence clears its Live itself. Allocation
// free; cheap enough to read every frame.
//
// ShipPowerController freezes the weapon charge on it: no ticking, no
// pickup cuts, no firing, no red-atom free shots; the charge already earned
// is kept and carries on afterwards.
public static class WorldTransition
{
    public static bool InProgress
    {
        get
        {
            var fall = Planetfall.Live;
            if (fall != null && fall.State != Planetfall.Stage.Done) return true;
            var lift = Liftoff.Live;
            if (lift != null && lift.State != Liftoff.Stage.Done) return true;
            var wm = WorldManager.Instance;
            return wm != null && wm.PortalIsOpen;
        }
    }
}
