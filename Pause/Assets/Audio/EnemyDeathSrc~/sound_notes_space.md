# Space World enemy death sounds — corrective pass

The 45 approved v1 WAVs are saved in `original_v1/`. The v2 files keep their 15 identities, three deterministic variants each, nominal timing, soft attacks and 20 ms end fade. Their loudest A-weighted 50 ms windows now target -15.5 dBFS, or -14.0 dBFS for Bastion and the four elites. Peak normalization and fixed RMS targets have been retired.

The Rift Lancer charge is a broadband electrical thwack with irregular noise crackle. Orbit Reaver uses overlapping noisy metal tears in place of its focused plate ring. Steel Claw has shorter, lower-level, more damped plate modes. Eventide Bastion and Beacon Rock were checked and retained their material body; their output levels were corrected.

See `verification_space_v2.md` for the full before/after table and scream mix checks. `sheet_space_v2.png` shows before/after spectrograms. `audition_space.wav` plays variant zero in roster order; `audition_space_all.wav` plays all variants. Both use 1.2 s gaps.

The existing game C# still synthesizes cues at runtime; loading these Resources WAVs requires a later integration change.

## Needle — `space_fighter_1`

Evokes single dry snap, air fizz.

Main noise band: 220-1800 Hz. Nominal duration: 0.30 s.

Layers: Small low pressure pop; short dry mid-band snap; softened airy noise fizz; two or three minute fragments.

## Steel Claw — `space_fighter_2`

Evokes metal crunch, short clang, shrapnel.

Main noise band: 140-1750 Hz. Nominal duration: 0.45 s.

Layers: Low punch; dense gunmetal noise crunch; brief damped broad plate modes; sparse shrapnel rattle.

## Twin Claw — `space_fighter_3`

Evokes two 60 ms spaced crunches, steel tear.

Main noise band: 95-1500 Hz. Nominal duration: 0.50 s.

Layers: Two separately textured low crunches about 60 ms apart; deeper noise body; short torn plate resonance; metal fragments.

## Warden — `space_fighter_4`

Evokes low shield whump, muffled hull, energy sigh.

Main noise band: 75-1100 Hz. Nominal duration: 0.60 s.

Layers: Low electrical noise whump; delayed low thump and muffled hull burst; progressively darker noise sigh; minor metal scatter.

## Steel Hound — `space_chaser`

Evokes servo wind down, clunk, sparks.

Main noise band: 95-1500 Hz. Nominal duration: 0.55 s.

Layers: Eight overlapping descending noise bands with slow modulation; late mechanical clunk; a few low-level sparks.

## Bile Mite — `space_alien`

Evokes wet splat, low bubbles, slimy tail.

Main noise band: 85-1300 Hz. Nominal duration: 0.55 s.

Layers: Low organic thump; two wobbly filtered wet-noise splats; small soft bubble bursts; darker slimy burble tail.

## Bastion — `space_big`

Evokes sub thump, dull boom, tearing plates, reactor thud.

Main noise band: 45-740 Hz. Nominal duration: 1.10 s.

Layers: Deep low thump; long dark boom; three tearing metal-plate bursts with brief modes; settling fragments; reactor thud-hum noise.

## Rail Mine — `space_mine`

Evokes bass thoomp, arc, tiny shrapnel.

Main noise band: 80-1450 Hz. Nominal duration: 0.50 s.

Layers: Compact bass thoomp; band-limited arc burst; scattered short crackles; tiny metal fragments.

## Beacon Rock — `space_rock_crater`

Evokes one crack, a few chunks, dust.

Main noise band: 75-1400 Hz. Nominal duration: 0.50 s.

Layers: One low split crack; five or six larger mineral fragments; lightly filtered dust tail.

## Cluster Rock — `space_rock_cluster`

Evokes staggered cracks, many stones, dust.

Main noise band: 100-1600 Hz. Nominal duration: 0.65 s.

Layers: Three staggered rock fractures; many small stone grains; a longer dusty tail.

## Coal Rock — `space_rock_dark`

Evokes dense thud, coal crumble.

Main noise band: 48-950 Hz. Nominal duration: 0.50 s.

Layers: Dense low coal thud; dark crumbly crackle; small close-spaced mineral bits; subdued dust.

## Eventide Bastion — `space_elite_eventide_bastion`

Evokes shield failure, large hull explosion.

Main noise band: 45-860 Hz. Nominal duration: 0.90 s.

Layers: Shield-pressure failure in dark noise; delayed heavy low hull detonation; brief torn plate mode; settling metal.

## Orbit Reaver — `space_elite_orbit_reaver`

Evokes tearing metal, quick whoosh burst.

Main noise band: 85-1150 Hz. Nominal duration: 0.70 s.

Layers: Fast low punch; rasping, broadband metal tear; four overlapping descending whoosh-noise pieces and fragments.

## Rift Lancer — `space_elite_rift_lancer`

Evokes rising noise charge, crack, ion crackle.

Main noise band: 80-1250 Hz. Nominal duration: 0.80 s.

Layers: Short broadband electrical thwack; low discharge; irregular noise crackle and dim low fade.

## Singularity Hauler — `space_elite_singularity_hauler`

Evokes inward noise swell, boom, warbling low drone.

Main noise band: 40-670 Hz. Nominal duration: 1.00 s.

Layers: Five inward swelling dark-noise layers; delayed heavy boom; modulated low pressure drone; sparse debris and dying hum.
