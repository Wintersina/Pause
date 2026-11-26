# Drone progression

Every ship begins a run with three visible dormant drones. An AIMED core appears immediately above the ship once flight is playable. AIMED chooses a target when firing; its bullets keep that heading afterward. STRAIGHT fires forward and SWEEP alternates 25 degrees left/right.

The opening modifier sequence is TWIN, TWIN, ARC, scheduled at 2, 8 and 14 playable seconds with the shared four-second pickup spacing. The starter core occupies that spacing too, so the first TWIN normally appears around four seconds. Earlier kill drops consume the same opening sequence. The first two TWIN pickups activate the other dormant drones with the preferred mode and two shots each; ARC upgrades the weakest eligible drone.

Modifiers activate dormant slots first, then upgrade the lowest eligible modifier level with a stable slot-order tie break. RAPID affects the whole squad. Subsequent coin drops have a 20% chance to offer three directional cores: collect one and the others disappear. The current preferred mode occupies the centre. Matching cores upgrade the weakest matching drone to core level four (+25% fire rate per level after the first); alternative modes explicitly reprogram a drone. Labels show the recipient and old/new mode before collection. Fully capped matching cores pay dust.

Each boss drops one recruit. It joins with the preferred mode and half the squad's average modifier levels, rounded down. Existing drones keep their builds. The squad caps at twelve; further recruits pay five dust. All run progress resets on a new run, and carries across worlds/loops.

Upgrades highlight their recipient, reset its firing timer, show the actual before/after benefit, and emphasize its next three volleys. Four projectile silhouettes mark accumulated modifier milestones (4, 12, 25 levels), alongside recoil, dual TWIN muzzle flashes and stronger hit/heart-break/kill bursts. Quarter-heart damage uses the same direct math for ordinary enemies, elites and bosses; enemies have no weapon-hit cooldown.

The HUD shows the selected drone's modifier row and a compact mode pip per slot. Nearby pickups select their recipient. Extra wings hover closer with a smaller drift range and fold inside camera edges. Heart turn-backs use a staggered cadence, retaining an owed turn until a safe gap is available. Projectile pools reserve 128 drone bullets plus 40 ultimate projectiles; twelve-drone stress checks cover firing, reuse and corner visibility.

Basic enemies retain two hearts. After 45% of the flight, authored tier-three hulls can gain three hearts from Frost onward; tier-four hulls can gain four in Ember. Loops unlock those tiers across worlds. Gold/blue plates distinguish these enemies. Scout wings expand from two to three/four members at four/eight active drones, subject to density and pilot caps. Health does not increase on individual coin pickups.

Review: `DroneSquadReview.Export` renders dormant/active/mode-choice/recruit/twelve-drone frames on a 480×854 phone and a Flip cover. `HeartQuarterPreview.ExportCombat` renders real quarter-heart hits and killing hits. Automated fixtures supplement these exports; they do not replace human playtesting.

Spending a pause opens five seconds of double enemy/elite kill points, including the arrival kill. The flight clock holds that window while paused; another pause refreshes it without stacking above ×2. Pickups, passive flight points, boss bonuses and escape penalties keep their normal values. Cooldown-only blinks with no pauses do not grant the bonus.

Fusion notices use the actual contributing atom icons with a small combo name below (for example OVERCLOCK), replacing the large recipe/discovery text. Re-triggering replaces the same compact notice; it freezes with the world and clears on death.
