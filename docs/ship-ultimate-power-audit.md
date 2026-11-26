# Ultimate progression audit

One ultimate cast against a stationary, unlimited-health boss at near (0, -1) and far (0, 1.4), radius 0.7, with the ship at (0, -3), stepped for 20 seconds at 60 Hz. No drone fire, secret powers, coins or incoming projectiles. Damage is in boss hearts (one weight = one heart); the per-minute column uses the better of the two positions. This is a controlled benchmark, not guaranteed live DPS. Ordinary enemies touched by an ultimate die outright. Dove needs enemy projectiles/collision targets to express its control/reflect value.

| ID | Dock position | Ship | Price | Ultimate | Boss damage near/far, base → full | Mean cooldown base → full | Boss damage / min base → full |
|---|---|---|---:|---|---:|---:|---:|
| 01 | 1 | Neon Comet | 0 | COMET STREAK | 1/1 → 1/1 | 45 → 29.25 s | 1.33 → 2.05 |
| 02 | 2 | Volt Viper | 300 | CHAIN LIGHTNING | 1/1 → 1/1 | 45 → 29.25 s | 1.33 → 2.05 |
| 03 | 6 | Solar Fang | 7,050 | HEAT WAVE | 1.5/1.5 → 3/3 | 45 → 29.25 s | 2 → 6.15 |
| 04 | 10 | Crimson Halo | 11,100 | RAIL GUN | 1/1 → 1/1 | 45 → 29.25 s | 1.33 → 2.05 |
| 05 | 13 | Ion Lancer | 16,100 | ION LASER | 1/1 → 1/1 | 45 → 29.25 s | 1.33 → 2.05 |
| 06 | 14 | Jade Phantom | 22,200 | FROST NOVA | 1/1 → 1/1 | 45 → 29.25 s | 1.33 → 2.05 |
| 07 | 15 | Gold Warden | 29,250 | WARDEN SENTRIES | 3/2 → 3.86/1.93 | 45 → 29.25 s | 4 → 7.93 |
| 08 | 3 | Lightning | 1,200 | SKY JAVELINS | 0.33/0.33 → 0.33/0.33 | 45 → 29.25 s | 0.44 → 0.68 |
| 09 | 4 | Ligher | 5,000 | BLOWTORCH | 4.8/0 → 7.4/0 | 45 → 29.25 s | 6.4 → 15.18 |
| 10 | 5 | Paranoid | 6,100 | ARCANE EYE | 0.25/0.25 → 0.17/0.17 | 45 → 29.25 s | 0.33 → 0.34 |
| 11 | 7 | Ninja | 8,150 | BOOMERANG STAR | 0/0 → 0/1 | 45 → 29.25 s | 0 → 2.05 |
| 12 | 8 | Saboteur | 9,000 | PROXIMITY MINES | 0.25/0.25 → 0.5/0.33 | 45 → 29.25 s | 0.33 → 1.03 |
| 13 | 9 | UFO | 10,000 | ABDUCT & HURL | 1/1 → 1/1 | 45 → 29.25 s | 1.33 → 2.05 |
| 14 | 11 | Dove | 12,050 | GALE WINGS | 0/0.5 → 0/0.5 | 45 → 29.25 s | 0.67 → 1.03 |
| 15 | 12 | Turtle | 15,000 | SHELL DISC | 0.5/0.75 → 0.25/1 | 45 → 29.25 s | 1 → 2.05 |
| 16 | 16 | Paragon | IAP | NUKE | 3/3 → 3/3 | 45 → 29.25 s | 4 → 6.15 |

## What the measurements mean

The printed bay number is the internal ship ID. Dock position is a separate price-sorted order: bay 03 is Solar Fang; the third berth is Lightning. Bay 15 is Turtle; the fifteenth berth is Gold Warden. The price change uses the printed bay numbers: Turtle is 15,000, ships 03–15 scale by 15,000/6,150, rounded to 50 dust. Skin prices for those ships use the same factor, rounded to 25; ships 01/02 and premium pricing stay unchanged. Uniform scaling preserves the dock order and its health/start-speed tiers.

These casts use real attack runners and an `IBossAttackTarget` probe. This excludes boss armour, movement, incoming fire, pickups, time spent repositioning, secret powers, and drones. A zero at one position means the cast missed or was out of range; it does not mean the weapon has no damage. The per-minute column is cast damage divided by mean charge time, not measured sustained live DPS. Cooldown is uniformly a random 30–60 seconds at stock, with upgrade cuts reducing its mean to 29.25 seconds at full upgrade. Capacitor atoms and free shots can change that in play.

Normal enemies touched by an ultimate die outright. Elites currently take a whole heart per positive ultimate contact while sharing an authored firing allowance; fractional weights therefore do not translate to fractional elite hearts. Bosses take the actual weighted sum without that firing allowance. This makes a single “attack power” number misleading. Drone damage remains direct quarter-heart damage.

## Solar Fang change

Solar Fang's wave is thicker (0.7 → 1.0) and faster (5 → 6 units/second). Its boss damage now scales **1.5 → 1.75 → 2 → 2.5 → 3 hearts** across the five dock tiers; previously the entire volley stayed at one boss heart. The second wave divides the tier's total between waves. All values and wave shares remain multiples of a quarter heart. Existing normal-enemy instant kills and elite firing allowances remain intact. Dock upgrade labels name the added boss damage.

## Suggested next balance pass

- **Neon Comet / Volt Viper:** keep their approachable baseline. Extra streaks and jumps improve coverage, but a small per-tier boss-damage floor would make colour purchases affect a single target too.
- **Solar Fang:** the implemented wave change now supplies clear direct growth. Compare its faster screen sweep and three-heart full volley against actual boss fights before raising it again.
- **Crimson Halo / Ion Lancer / Jade Phantom:** most single-target growth still comes from cooldown; add explicit damage milestones alongside wider rails, extra passes, and utility rather than quietly dividing the same total.
- **Gold Warden:** preserve long-lived sentry identity. Its target distance and projectile travel time change how much of a deployment connects. Consider a minimum full-tier cast value so an upgrade does not lose output in a fixed-distance benchmark.
- **Lightning:** the centre lane still contributes only about one-third heart against a small central target even when more lanes unlock. Keep the wide coverage, but consider raising each javelin to a quarter-based damage schedule and increasing the centre lane at milestones.
- **Ligher:** it already has high output when close; range is the tradeoff. Avoid a raw buff based on its far-target zero. Give range/uptime upgrades clear visuals and compare risk-adjusted live results.
- **Paranoid:** additional marks dilute the per-mark share against a lone target. Retain crowd coverage while adding a single-target minimum; this is a strong candidate for a focused correction.
- **Ninja:** the orbit can miss a centreline target. Judge it with a spatial target grid and return-path hits before deciding on raw damage. The full upgrade reaching a target the base misses is a geometry change, not uniform DPS growth.
- **Saboteur:** more mines increase board coverage; few necessarily trigger on one enemy. Consider tiered blast damage alongside the current mine count, with a clear cap.
- **UFO:** its throw and splash need a clustered field to show their value; add direct elite/boss milestones separately from extra grabs.
- **Dove:** preserve its displacement and projectile-return role. Test incoming volleys and shove collisions; a sterile boss probe omits those benefits.
- **Turtle:** bounce paths make damage position-dependent. Consider a minimum useful boss contact per throw before increasing the bounce total.
- **Paragon:** its second warhead divides a three-heart total rather than doubling it. Keep that explicit in the Codex; premium extras and automatic shield should be considered separately.

Before changing every attack, use three fixtures per tier: a lone boss, a spread-out enemy field, and a clustered field with incoming projectiles. Track ordinary kills, elite hearts, boss hearts, time to impact, charge time and coverage. Set an explicit damage/coverage goal for every purchase so the new drone unlocks at upgrades 3 and 4 complement the weapon gains.

## Every upgrade tier

Near/far values are boss hearts per cast. The final column uses the better sampled position. The CSV has the full shots, reach, duration, width and speed values for each tier.

| Bay | Ship | Tier | Near / far hearts | Mean charge | Sampled hearts/min |
|---|---|---:|---:|---:|---:|
| 01 | Neon Comet | 0 | 1 / 1 | 45 s | 1.33 |
| 01 | Neon Comet | 1 | 1 / 1 | 45 s | 1.33 |
| 01 | Neon Comet | 2 | 1 / 1 | 38.25 s | 1.57 |
| 01 | Neon Comet | 3 | 1 / 1 | 38.25 s | 1.57 |
| 01 | Neon Comet | 4 | 1 / 1 | 29.25 s | 2.05 |
| 02 | Volt Viper | 0 | 1 / 1 | 45 s | 1.33 |
| 02 | Volt Viper | 1 | 1 / 1 | 45 s | 1.33 |
| 02 | Volt Viper | 2 | 1 / 1 | 38.25 s | 1.57 |
| 02 | Volt Viper | 3 | 1 / 1 | 38.25 s | 1.57 |
| 02 | Volt Viper | 4 | 1 / 1 | 29.25 s | 2.05 |
| 03 | Solar Fang | 0 | 1.5 / 1.5 | 45 s | 2 |
| 03 | Solar Fang | 1 | 1.75 / 1.75 | 45 s | 2.33 |
| 03 | Solar Fang | 2 | 2 / 2 | 38.25 s | 3.14 |
| 03 | Solar Fang | 3 | 2.5 / 2.5 | 38.25 s | 3.92 |
| 03 | Solar Fang | 4 | 3 / 3 | 29.25 s | 6.15 |
| 04 | Crimson Halo | 0 | 1 / 1 | 45 s | 1.33 |
| 04 | Crimson Halo | 1 | 1 / 1 | 38.25 s | 1.57 |
| 04 | Crimson Halo | 2 | 1 / 1 | 38.25 s | 1.57 |
| 04 | Crimson Halo | 3 | 1 / 1 | 29.25 s | 2.05 |
| 04 | Crimson Halo | 4 | 1 / 1 | 29.25 s | 2.05 |
| 05 | Ion Lancer | 0 | 1 / 1 | 45 s | 1.33 |
| 05 | Ion Lancer | 1 | 1 / 1 | 45 s | 1.33 |
| 05 | Ion Lancer | 2 | 1 / 1 | 38.25 s | 1.57 |
| 05 | Ion Lancer | 3 | 1 / 1 | 38.25 s | 1.57 |
| 05 | Ion Lancer | 4 | 1 / 1 | 29.25 s | 2.05 |
| 06 | Jade Phantom | 0 | 1 / 1 | 45 s | 1.33 |
| 06 | Jade Phantom | 1 | 1 / 1 | 45 s | 1.33 |
| 06 | Jade Phantom | 2 | 1 / 1 | 38.25 s | 1.57 |
| 06 | Jade Phantom | 3 | 1 / 1 | 38.25 s | 1.57 |
| 06 | Jade Phantom | 4 | 1 / 1 | 29.25 s | 2.05 |
| 07 | Gold Warden | 0 | 3 / 2 | 45 s | 4 |
| 07 | Gold Warden | 1 | 4 / 2 | 45 s | 5.33 |
| 07 | Gold Warden | 2 | 4 / 2 | 38.25 s | 6.28 |
| 07 | Gold Warden | 3 | 3.86 / 1.93 | 38.25 s | 6.06 |
| 07 | Gold Warden | 4 | 3.86 / 1.93 | 29.25 s | 7.93 |
| 08 | Lightning | 0 | 0.33 / 0.33 | 45 s | 0.44 |
| 08 | Lightning | 1 | 0.67 / 0.67 | 45 s | 0.89 |
| 08 | Lightning | 2 | 0.67 / 0.67 | 38.25 s | 1.05 |
| 08 | Lightning | 3 | 0.33 / 0.33 | 38.25 s | 0.52 |
| 08 | Lightning | 4 | 0.33 / 0.33 | 29.25 s | 0.68 |
| 09 | Ligher | 0 | 4.8 / 0 | 45 s | 6.4 |
| 09 | Ligher | 1 | 6 / 0 | 45 s | 8 |
| 09 | Ligher | 2 | 6 / 0 | 38.25 s | 9.41 |
| 09 | Ligher | 3 | 7.4 / 0 | 38.25 s | 11.61 |
| 09 | Ligher | 4 | 7.4 / 0 | 29.25 s | 15.18 |
| 10 | Paranoid | 0 | 0.25 / 0.25 | 45 s | 0.33 |
| 10 | Paranoid | 1 | 0.2 / 0.2 | 45 s | 0.27 |
| 10 | Paranoid | 2 | 0.2 / 0.2 | 38.25 s | 0.31 |
| 10 | Paranoid | 3 | 0.17 / 0.17 | 38.25 s | 0.26 |
| 10 | Paranoid | 4 | 0.17 / 0.17 | 29.25 s | 0.34 |
| 11 | Ninja | 0 | 0 / 0 | 45 s | 0 |
| 11 | Ninja | 1 | 0 / 1 | 45 s | 1.33 |
| 11 | Ninja | 2 | 0 / 1 | 38.25 s | 1.57 |
| 11 | Ninja | 3 | 0 / 1 | 38.25 s | 1.57 |
| 11 | Ninja | 4 | 0 / 1 | 29.25 s | 2.05 |
| 12 | Saboteur | 0 | 0.25 / 0.25 | 45 s | 0.33 |
| 12 | Saboteur | 1 | 0.2 / 0.4 | 45 s | 0.53 |
| 12 | Saboteur | 2 | 0.2 / 0.4 | 38.25 s | 0.63 |
| 12 | Saboteur | 3 | 0.33 / 0.17 | 38.25 s | 0.52 |
| 12 | Saboteur | 4 | 0.5 / 0.33 | 29.25 s | 1.03 |
| 13 | UFO | 0 | 1 / 1 | 45 s | 1.33 |
| 13 | UFO | 1 | 1 / 1 | 45 s | 1.33 |
| 13 | UFO | 2 | 1 / 1 | 38.25 s | 1.57 |
| 13 | UFO | 3 | 1 / 1 | 38.25 s | 1.57 |
| 13 | UFO | 4 | 1 / 1 | 29.25 s | 2.05 |
| 14 | Dove | 0 | 0 / 0.5 | 45 s | 0.67 |
| 14 | Dove | 1 | 0 / 0.5 | 45 s | 0.67 |
| 14 | Dove | 2 | 0 / 0.5 | 38.25 s | 0.78 |
| 14 | Dove | 3 | 0 / 0.5 | 38.25 s | 0.78 |
| 14 | Dove | 4 | 0 / 0.5 | 29.25 s | 1.03 |
| 15 | Turtle | 0 | 0.5 / 0.75 | 45 s | 1 |
| 15 | Turtle | 1 | 0.5 / 0.75 | 45 s | 1 |
| 15 | Turtle | 2 | 0.75 / 0.75 | 38.25 s | 1.18 |
| 15 | Turtle | 3 | 0.25 / 0.75 | 38.25 s | 1.18 |
| 15 | Turtle | 4 | 0.25 / 1 | 29.25 s | 2.05 |
| 16 | Paragon | 0 | 3 / 3 | 45 s | 4 |
| 16 | Paragon | 1 | 3 / 3 | 45 s | 4 |
| 16 | Paragon | 2 | 3 / 3 | 38.25 s | 4.71 |
| 16 | Paragon | 3 | 3 / 3 | 38.25 s | 4.71 |
| 16 | Paragon | 4 | 3 / 3 | 29.25 s | 6.15 |
