#!/bin/zsh
cd /Users/sina/Developer/Pause/.claude/ws-storm-overlays
D=/Users/sina/Developer/Pause/.claude/ws-storm-overlays/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle_overlays/src~; R=/Users/sina/Developer/Pause/.claude/ws-storm-overlays/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle_overlays
source $D/jobs.sh
REFS=($R/refs/s1_supercell.png $R/refs/s5_overlay_rain.png)
job() { n=$1; b=$2
  [ -f $D/$n.png ] && return
  { cat $D/common.md; echo; echo "$b"; echo; echo "Output: write the final PNG to $D/$n.png"; } > $D/prompt_$n.md
  zsh scripts/codex-art.sh $PWD $D/prompt_$n.md $D/$n.lastmsg.txt ${REFS[@]} > $D/$n.log 2>&1
  echo "$n exit $? $(ls $D/$n.png 2>&1|tail -1|cut -c1-20)" >> $D/progress.txt
}
job rain_a "$(rain 'mostly fine far streaks with a few near ones, steady rain.')"
job rain_b "$(rain 'heavier, denser medium streaks with gusting lean, some drifting spray.')"
job rain_c "$(rain 'light rain: thin sparse streaks with a few wind-dragged mist wisps.')"
job rain_d "$(rain 'squall: long, strongly slanted streaks in loose diagonal bands with gaps between bands.')"
job wx_banks "WEATHER SHEET 1 (2x2 grid, 4 cells): four different realistic storm CLOUD BANK sprites seen from above: dark rolling cloud masses with soft noisy edges, volumetric shading, internal faint cool glow, each a different shape and asymmetric (a long sheared bank, a bulky rounded mass with a torn edge, a ragged arc, a layered bank), fading softly to transparent at their edges. $BG_T"
job wx_wisps "WEATHER SHEET 2 (2x2 grid, 4 cells): cells 1-3 are three different CLOUD WISPS (thin wind-torn cirrus-like shreds of pale steel-grey cloud, stretched and sheared, different shapes), cell 4 is one MIST puff (a soft low translucent mist patch). All soft edged, translucent. $BG_T"
job wx_gusts "WEATHER SHEET 3 (2x2 grid, 4 cells): cell 1 a soft MIST bank (translucent low fog drift), cell 2 and 3 two different GUST sprites (wind-sheared streaks of translucent mist and rain, leaning right, long and thin, like wind visibly blowing spray), cell 4 a PALL: a large dark translucent rain-cloud shadow patch with faint vertical rain shafts hanging from it. Soft translucent edges. $BG_T"
job wx_rain "WEATHER SHEET 4 (2x2 grid, 4 cells): four different heavy RAIN SHEET sprites: curtains of dense slanted rain falling from a darker cloud edge, with depth (fine far streaks and a few bright near ones), translucent grey-blue, soft irregular edges, each a different shape and slant. $BG_T"
job lt_bolts "LIGHTNING SHEET 1 (2x2 grid, 4 cells): four different realistic cloud-to-cloud LIGHTNING bolts seen from above: thin, jagged, irregular, with a few branches, white-hot thin core (#F4F8FF) and a cool blue-white glow, plus a faint violet-white halo; each asymmetric and different (one long diagonal, one wide forked, one tight, one with a fine crown of branches). Realistic, not a cartoon zigzag. $BG_K"
job lt_strikes "LIGHTNING SHEET 2 (2x2 grid, 4 cells): cells 1-2 two different thin realistic lightning bolts (one near-vertical with branches, one diagonal), cells 3-4 two different realistic GROUND STRIKES seen from above: a thin branching bolt ending in a bright impact flare with a small ring of cool white sparks and glow. White-hot cores, cool blue-white glow, faint violet-white halo. $BG_K"
job lt_flashes "LIGHTNING SHEET 3 (2x2 grid, 4 cells): cells 1-2 two different SHEET-LIGHTNING FLASHES: soft irregular blooms of cool white-blue light as if a cloud were lit from inside, with faint fine veins of lightning inside, strongest in the middle and fading to pure black at the cell edges (no hard edge); cell 3 an ARC: a thin curving electric arc crackling across a gap with tiny branches; cell 4 a small distant flicker (a pale violet-white cloud glow with one thin vein). $BG_K"
job amb_a "AMBIENT SHEET A (4x2 grid, 8 cells): 4 cells of fine RAIN STREAK CLUSTERS (2 to 3 thin wind-driven streaks of different length each, translucent steel-white, leaning right) and 4 cells of single falling RAINDROPS with a thin streak tail (round water drop highlights, translucent, tiny, different sizes). Small, subtle elements. $BG_T"
job amb_b "AMBIENT SHEET B (4x2 grid, 8 cells): 4 cells of thin curved wind-swept WISP lines (delicate curling shreds of translucent mist, different curves, no outlines) and 4 cells of SPRAY SPLASH DOTS (tiny scattered water droplets and flecks of different sizes, translucent steel-white, with a few tiny streak tails). Small, subtle elements. $BG_T"
echo ALLDONE >> $D/progress.txt
