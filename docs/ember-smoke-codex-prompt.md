# Codex prompt: clean the Ember / Verdant smoke loops (edit, do not repaint)

Repo: /Users/sina/Developer/Pause, branch art/ember-smoke-fixes (new worktree). Read docs/ember-smoke-fixes.md first.

Atlases are 1024x1024 sheets of 256x256 cells (json lists rects, y from the bottom). Work only in Assets/Art/Backgrounds/Resources/Worlds/<World>/Backdrop3/ (png + sources under Assets/Art/Worlds/<World>/backdrop_v3~/src~/). Keep every pixel that is not named below identical. Keep cell positions, names and json unchanged.

1. Ember eruption.png (eruptsmoke_b_00..07), lavafire.png (fountain_00..07), leaks.png (ember_rain_00..03, lava_bubble_00..03), lights.png (strobe_white_01..03): in each frame remove the detached hatched flat strip floating above the art (a connected blob >= 24 px wide, <= 12 px tall, not touching the main plume). Delete only that blob (set alpha 0).
2. Ember weather.png: smokepall_00 (cell x=512,y=0): the top edge at row 52 is cut flat; round and feather the top 14 rows into cloud puffs, alpha never above 56. ashgust_01 (x=0,y=0): round the cut top-left corner.
3. Verdant smoke.png smoke_a_00..07 and firesmoke.png wildsmoke_b_00..07: the art touches the right cell edge (x=254). Scale each frame to ~88% about the foot (128, 234) so the rightmost opaque column is <= 249, nearest-neighbour, keep the pixel grid.
4. Ember smoke.png: smoke_a foot flame at x=128.6 in all 8 frames (frames 02 and 06 have two feet: keep one), body lean at the top of the column within +-25 px and always to the right of the foot; smoke_b +-20 px; eruption.png eruptsmoke_a likewise (stem base x within 128.6 +-2 in all frames). Adjust by shifting/shearing existing pixels, not repainting.
5. Margin rule for everything touched: >= 6 px clear at the top and sides of the cell, foot of ground-attached plumes at (128, 236) +-1.

Then rerun the measure scripts (Ember: Assets/Art/Worlds/Ember/backdrop_v3~/src~/measure_points.py), update points.json, and run `scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites BackdropLoopGuardTest,EmberBackdropTest,VerdantBackdropTest,WorldBackdropTest`. In Assets/Editor/Tests/BackdropLoopGuardTest.cs empty KnownEdge / KnownSliver of what you fixed. Commit only the touched PNGs, jsons, points.json, the test. Do not merge.
