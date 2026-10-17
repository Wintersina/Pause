#!/usr/bin/env python3
# Writes the Codex prompt files (queue/NN_boss_piece.txt) and queue/order.txt (NN_boss_piece|outpath).
import os
S = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/bp/'
WT = '/Users/sina/Developer/Pause/.claude/worktrees/agent-a5b9f3f319d40b387/'
OUT = 'Pause/Assets/Art/BossProjectiles~/'
Q = S + 'queue/'
os.makedirs(Q, exist_ok=True)

COMMON = (
 'You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait, neon pixel art: hand-placed HD pixel art, crisp pixels, no blur, 4-6 tone ramps per material, bright glowing cores). '
 'Use the built-in IMAGE GENERATION tool. Work only in this worktree; do not edit, delete or commit anything existing. '
 'Save the final chosen image as a PNG to {out} (copy it from wherever the tool saved it) and print that exact path.\n\n'
 'GLOBAL RULES: hostile attack art must read instantly against pale or dark backdrops: a bright core with a DARK contrasting edge (a dark keyline of 2-3 px in a deep tone of the same material, never a pale or white outline, never a uniform pale rim). '
 'NO red (red means the player). NO pink or magenta unless the brief says it is the Space world. NO text, NO labels, NO UI boxes, NO grid lines, NO drop shadows, NO mock-up scenery. '
 'BACKGROUND: perfectly flat solid {key} everywhere that is not art (a chroma key); the art itself must never use that colour. Keep every cell/frame the same size and aligned on an exact grid, art centred in its cell with margin. '
 'After generating, inspect it; if cells are misaligned, a frame is cut off, or it looks blurry or like a photo/3D render, regenerate. Finish with one summary line.\n\n')

def piece_body(b):
    return ('PIECE: BEAM BODY, 4 animation frames side by side (left to right, equal size, equal gaps, each frame 1:2 wide:tall, so the whole image is about 2:1). '
            'Each frame is the SAME straight vertical beam segment, perfectly tileable top to bottom (the top edge continues seamlessly into the bottom edge: no tapering, no ends); the opaque beam is about 75 percent of the frame width and centred; the four frames differ only by the animated detail so it flickers/flows when cycled.\n' + b['body'])

def piece_flare(b):
    return ('PIECE: SOURCE FLARES, a grid of 4 columns x 4 rows of equal square cells (16 cells, order left-to-right then top-to-bottom). The beam points DOWN from the source; the cell centre is the source point. '
            'Rows 1-2 (8 cells): WINDUP 1..8: the charge-up gathering at the emitter, growing from a tiny spark in cell 1 to a full bright charge in cell 8. '
            'Row 3 (4 cells): MUZZLE a-d: the steady firing flare at the source, a 4-frame flickering loop of the same size. '
            'Row 4 (4 cells): FADE 1-4: the flare dying out after the beam stops, from the full muzzle down to almost nothing.\n' + b['flare'])

def piece_impact(b):
    return ('PIECE: IMPACT AND SPARKS, a grid of 4 columns x 2 rows of equal square cells (8 cells). The beam arrives from ABOVE and hits a surface BELOW; the burst sits with its base near the bottom of the cell. '
            'Row 1 (4 cells): IMPACT 1..4: the continuous burst where the beam ends (a 4-frame loop of the same size, flickering). '
            'Row 2 (4 cells): SPARK a-d: small separate flying particles/debris of the same material, a 4-frame loop, centred.\n' + b['impact'])

def piece_tell(b):
    return ('PIECE: TELEGRAPH, a row of 4 equal square cells. Cells 1-2: SIGHT a,b: a thin vertical dashed aiming line segment (dashes about 2 px wide, tile-able vertically, pointing down, two blink frames with the dashes offset by half a period), in the boss world cue colour. '
            'Cells 3-4: LOCK a,b: a small target reticle where the line ends (two pulse frames). ' + b['tell'])

def piece_shots(b):
    rows = b['shots']
    lines = ''.join('Row %d: %s\n' % (i + 1, r) for i, r in enumerate(rows))
    return ('PIECE: ATTACK PROJECTILES, a grid of 2 columns x %d rows of equal square cells (each row is ONE projectile type shown as 2 animation frames: column 1 and column 2, same size and orientation, differing in the animated detail: spin, flicker, trail). '
            'Every projectile is oriented pointing DOWN (travel direction toward the bottom of the cell), centred, filling about 60 percent of its cell, and each row must differ strongly from the others in SHAPE and MATERIAL (not just colour). Projectile types:\n%s' % (len(rows), lines) + b.get('shots_note', ''))

BOSSES = []

BOSSES.append(dict(key='desert', k='#FF00FF', world='Desert (pale bone-gold dunes)',
 body='THE DESERT BOSS "Dune-Eater" (feral strip-mining machine worm) fires a LENS BEAM: a focused-sunlight heat laser through a glass lens. Core: razor-straight white-gold (#FFF6D8 to #FFE9A8). Halo: glassy refracted shimmer with VERY THIN prismatic rainbow-edge fringes on both sides (hairline bands of pale cyan, pale mint, pale gold, a hairline of pale violet-blue at the outermost edge), restrained; faint wavy heat-refraction ripples and tiny sparkle motes drifting at the edges that differ per frame; outermost a 2-3 px dark sepia-brown/indigo (#2A2236) keyline so it reads as hostile on pale sand.',
 flare='Source: a glass lens/prism on the worm. WINDUP: sunlight converging into a bright focus point through a glass lens, a ring of lens flare and thin rainbow arcs growing. MUZZLE: a glassy lens flare, white-gold hot core with faint rainbow-edge spikes. FADE: the glass flare cooling, rainbow arcs shrinking. Dark keyline on the outer edge of the solid parts; no pink.',
 impact='IMPACT: a shimmering heat-haze scorch on sand: a white-gold hot spot with concentric heat ripples, a glassy fused-sand crater edge, a fountain of sand spray. SPARKS: glints of molten glass, tiny sand grains and sun-white sparkles. No pink.',
 tell='Sight dashes: pale sun-gold with a thin dark edge. Reticle: a small lens-ring crosshair in sun-gold.',
 shots=['SAND SPIT: a spinning ragged clump of gritty sand and pebbles with a short dusty trail (bolt-sized, long axis along travel).',
        'GLASS SHARD: a sharp fused-glass splinter, translucent pale cyan-gold with a white glint that travels along it between frames and a dark sepia edge (shard-sized, a short thick diamond).',
        'BURIED MINE: a rusted copper-brass proximity mine, a round riveted dome with 4 short spikes and a small pale-gold blinking light (frame 2 light brighter).']))

BOSSES.append(dict(key='storm', k='#FF00FF', world='Storm (violet-blue storm gas giant)',
 body='THE STORM BOSS "Tempest Engine" (turbine that makes storms) fires COIL BEAMS: a crackling ELECTRICAL ARC, not a laser column. A jagged forking lightning bolt running the full height: bright white-blue core (#FFFFFF into #BFE6FF), violet-blue and cyan corona, branching mini-arcs and sparks along the length that CHANGE completely each frame (different fork patterns, so it flickers violently). Tileable top to bottom. A 2-3 px dark deep-indigo (#14122E) edge hugging the outer corona so it reads on dark storm clouds. No pink.',
 flare='Source: two Tesla-style coil tips on the machine. WINDUP: coil tips glowing, small arcs jumping between the tips and crawling along the coil, growing from a spark to a crackling ball. MUZZLE: a bright electric flash with an ionised arc ring and arcs jumping, flickering. FADE: arcs dying, residual sparks. White-blue core, violet corona, dark indigo edge.',
 impact='IMPACT: a ground-strike electric burst: bright white flash, radial forking sparks, a scorch/ion ring, flickering. SPARKS: separate small zig-zag arcs and glowing sparks.',
 tell='Sight dashes: crackling white-violet dashes with a thin dark indigo edge. Reticle: a small ring with four tiny arcs jumping around it.',
 shots=['ARC BOLT: a short forking lightning zig-zag bolt with a glowing white-blue core and violet corona; the fork pattern differs between the two frames.',
        'HAIL: a faceted ice-grey hailstone with a bright cyan-white glint and a streak trail, dark indigo edge (shard-sized, compact, blocky, NOT lightning).',
        'COIL CHAIN: a chain-lightning link: a glowing orb node with two short arcs reaching out from opposite sides like a dumbbell, arcs differ per frame.']))

BOSSES.append(dict(key='verdant', k='#FF00FF', world='Verdant (jungle, lime/green)',
 body='(Used only if the vine needs a beam-like piece.) A thorny VINE whip segment: green bark with leaf lobes and thorns.',
 flare='', impact='', tell='',
 shots=['THORN: a hard woody thorn spike, dark green-brown bark with a pale-lime sharp tip and a small resin glint (bolt-sized, long and thin).',
        'SPORE CLOUD: a puffy cluster of glowing lime-green spore puffs with floating specks, drifting, soft cloud shape with a dark green edge (shard-sized).',
        'SAP GLOB: a heavy amber-green sap droplet with a glossy highlight and a short wobbling drip trail, dark green edge.'],
 lash='VINE LASH pieces for the Bloom Queen\'s whip. A grid of 8 cells in one row (each a 128-px style square cell, image 8:1): LINK a,b: one straight section of a thick living vine pointing DOWN, tileable end to end (top and bottom connect), bark texture with small leaf nubs, two frames that sway slightly; TIP a,b: the thorned bulb tip of the whip pointing down, two frames; ROOT a,b: the leafy bud collar where the vine leaves the boss, two frames; DASH a,b: a short dotted arc-preview segment in leaf green. Painted natural vine with DARK green-brown edge shading. ABSOLUTELY NO white, pale or light outline or rim anywhere on the vine: the edge of the vine is its own dark shaded bark. Moss green body, a few lime highlights.'))

BOSSES.append(dict(key='ember', k='#00FF00', world='Ember (volcanic, orange/lava)',
 body='THE EMBER BOSS "Cinder Drake" (volcanic wyrm) breathes a very long FLAMETHROWER stream from its jaw, not a laser. A roaring jet of fire running the full height: white-yellow core (#FFF7C0), bright orange body, deep red-brown smoky edge (#5A1E10) with licking flame tongues and heat shimmer that change per frame; tileable top to bottom; ember sparks spraying out along the length. The deep red-brown smoke edge is the dark edge; no pale rim. Dark red-orange flame is allowed here (it is fire, not the player ship).',
 flare='Source: the dragon\'s open jaw. WINDUP: a pilot flame growing in the maw with a heating glow, embers swirling in, from a tiny flame to a bright roaring gout. MUZZLE: a steady roaring flame burst at the mouth. FADE: the fire guttering out into smoke wisps and embers.',
 impact='IMPACT: a dispersing fireball / ember burst with licking flames and a scorch where the stream ends. SPARKS: separate flying embers and tiny flame licks.',
 tell='Sight dashes: dull orange-amber dashes with a dark brown edge. Reticle: a small heat-ring crosshair with a glowing centre.',
 shots=['MOLTEN BRASS SLUG: a rotating cannon round of molten brass, a rounded bullet with a glowing orange-white tip and a trail of sparks, 2 frames of sparks.',
        'MAGMA BOMB: a round cracked lava-rock bomb with glowing seams and a short sizzling fuse spark on top (frame 2: seams brighter).',
        'FLAME JET PUFF: a short teardrop gout of flame with a swirling white-yellow core and a few embers.',
        'STEAM PUFF: a soft billowing grey-white steam cloud puff with a faint orange underglow and a dark grey edge, rising.']))

BOSSES.append(dict(key='frost', k='#FF00FF', world='Frost (arctic ice, cyan/blue)',
 body='THE FROST BOSS "Hoarfrost Leviathan" fires a CRYO LANCE: a freezing beam made of stacked jagged ice crystals, not a coloured column. A lance of stacked frost crystals / ice shards running the full height with a pale-cyan to deep-blue gradient and a white-blue core; frost mist and snow flurry spraying off the edges; tiny drifting ice flakes; rime crystals growing outward along the beam; the crystal pattern shifts per frame. Tileable top to bottom. The outer edge is a 2-3 px DEEP NAVY (#0B2A55) keyline on the crystals (pale cyan highlights inside are fine, but NO uniform white or pale keyline). Cool colours only, no pink.',
 flare='Source: the leviathan\'s glaring eyes. WINDUP: frost gathering with cold vapour curling and ice crystals forming around a point, growing to a dense crystal star. MUZZLE: a bright icy flare with crystal spikes and vapour. FADE: crystals melting into mist.',
 impact='IMPACT: a frozen impact: an ice burst, a snow puff, a frost ring spreading on the ground with crystal shards. SPARKS: ice flakes and snow specks.',
 tell='Sight dashes: icy cyan dashes with a thin deep-navy edge. Reticle: a small snowflake-crosshair.',
 shots=['ICE SHARD: a long jagged ice spear of stacked crystal facets, pale-cyan to blue with a white glint, deep navy edge.',
        'FROST ORB: a round frozen orb with a swirling snow core and a ring of tiny frost crystals, shimmering.',
        'SNOW-CRYSTAL RING: a six-armed snowflake crystal star (a flat hexagonal snowflake), spinning between the frames.']))

BOSSES.append(dict(key='space', k='#00FF00', world='Space (indigo/violet nebula; PINK AND MAGENTA ARE ALLOWED here)',
 body='THE SPACE BOSS "Void Archon" fires POD LASERS as a void/plasma RAIL beam: a hard-edged plasma rail, a tight bright white core with magenta-violet energy bands pulsing along it like stacked electric rings, dark violet-black edge (#1A0B33). Tileable top to bottom. Clearly different from an organic or fire beam: mechanical, geometric.',
 flare='Source: a weapon pod. WINDUP: a ring of energy collapsing into a point with rotating plasma segments. MUZZLE: a hard plasma muzzle ring flare. FADE: the rings dissipating.',
 impact='IMPACT: a plasma burst with expanding ring shockwave. SPARKS: small violet plasma shards.',
 tell='Sight dashes: magenta-violet dashes with a dark edge. Reticle: a small circle with tick marks.',
 shots=['RAIL DART: a slim metallic armour-piercing dart with a bright magenta-white tip and a thin plasma trail.',
        'ION ARC: a crescent arc of violet-white ion energy that flickers between frames.',
        'PULSAR RING: a thin glowing expanding ring (an open circle of magenta plasma) with a bright pulse that travels around it.',
        'VOID ORB: a dark black-violet sphere with a thin bright magenta event-horizon ring and a swirl.']))

BOSSES.append(dict(key='tide', k='#FF00FF', world='Tide (ocean, teal/aqua with brass)',
 body='THE TIDE BOSS "Iron Kraken" fires a BEAK JET: a high-pressure WATER JET, not a laser. A tight bundle of rushing water: a bright white-aqua core with streaming foam, tapering streaks of teal flowing downward, fine spray droplets at the edges, small bubbles, different each frame (the flow ripples). Tileable top to bottom. A 2-3 px dark deep-teal/navy (#06303A) edge so it reads on pale water.',
 flare='Source: the kraken\'s brass beak nozzle. WINDUP: water pressure building: swirling water gathering with bubbles. MUZZLE: a burst of foam and spray at the nozzle. FADE: the jet dwindling to dripping spray.',
 impact='IMPACT: a splash plume with a foam ring and a mist cloud. SPARKS: flying droplets and bubbles.',
 tell='Sight dashes: aqua dashes with a dark teal edge. Reticle: a small ripple ring crosshair.',
 shots=['PRESSURE WATER BOLT: a compact jet-slug of water, a teardrop with a foam head and streaming tail, aqua with a white glint.',
        'BRASS RIVET HARPOON: a heavy barbed brass harpoon head with a riveted collar and a short rope-chain trail, dark iron edge.',
        'BUBBLE MINE: a glossy translucent bubble with a dark iron spiked core inside, floating, tiny highlight; frame 2 shimmers.']))

order = []
n = 0
def add(boss, piece, text):
    global n
    n += 1
    name = '%02d_%s_%s' % (n, boss['key'], piece)
    out = OUT + boss['key'] + '/' + piece + '_raw.png'
    full = COMMON.format(out=out, key=boss['k'] + ' (' + {'#FF00FF': 'pure magenta', '#00FF00': 'pure green'}[boss['k']] + ')') + 'WORLD: ' + boss['world'] + '\n' + text + '\n'
    open(Q + name + '.txt', 'w').write(full)
    order.append(name + '|' + out)

BY = {b['key']: b for b in BOSSES}
# priority: desert, storm, verdant(vine), ember, frost beams; then each boss's attack projectiles; then space, tide beams
for k in ['desert', 'storm']:
    b = BY[k]
    for p, f in [('body', piece_body), ('flare', piece_flare), ('impact', piece_impact), ('tell', piece_tell)]:
        add(b, p, f(b))
b = BY['verdant']; add(b, 'lash', b['lash'])
for k in ['ember', 'frost']:
    b = BY[k]
    for p, f in [('body', piece_body), ('flare', piece_flare), ('impact', piece_impact), ('tell', piece_tell)]:
        add(b, p, f(b))
for k in ['desert', 'storm', 'verdant', 'ember', 'frost']:
    add(BY[k], 'shots', piece_shots(BY[k]))
for k in ['space', 'tide']:
    b = BY[k]
    for p, f in [('body', piece_body), ('flare', piece_flare), ('impact', piece_impact), ('tell', piece_tell)]:
        add(b, p, f(b))
    add(b, 'shots', piece_shots(b))
open(Q + 'order.txt', 'w').write('\n'.join(order) + '\n')
print(len(order), 'prompts')
