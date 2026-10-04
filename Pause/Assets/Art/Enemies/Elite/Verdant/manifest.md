# Verdant Elite Ships

## Resin Warden

`resin_warden.png` is an art-only, seven-cell 1344 x 192 strip. It is not
wired to enemy behaviour; the gameplay implementation can select its states
without changing the player ship assets.

| Cell | State |
| --- | --- |
| 0 | Landed / resting on the world surface |
| 1 | Grounded idle, with restrained reactor mechanics |
| 2 | Lift-off, with ignition and a clear rise from the surface |
| 3 | Airborne hover / straight pursuit |
| 4 | Airborne bank left |
| 5 | Airborne bank right |
| 6 | Second-heart damaged but still flight-capable state |

The Warden is a complete top-down Verdant bio-industrial craft: corroded
gunmetal, resin pressure chambers, copper plumbing, root-cable braces, and
contained lime/magenta energy. Every frame contains one complete ship; the
damaged cell retains the full controllable hull rather than substituting a
destruction burst.
