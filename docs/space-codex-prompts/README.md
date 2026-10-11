# Space backdrop redo (feature/space-backdrop)

Critique: Space's set pieces (hi-res neon planets, stations, comets, asteroids) are on par with the other worlds, but the SKY tiles
(sky_01..04) read as uniform procedural fractal noise: no big shapes, no dust lanes, no depth, nothing like the concept render;
the only other background layers are two wisp sprites, one galaxy, static glint stars and tiny debris. Missing vs concept:
the derelict capital-ship wreck, ringed city planet with lit grid, dense layered nebula, animated twinkles, debris fields.

Jobs (run with .claude/skills/add-world/scripts/run_codex.sh <job> prompt_<x>.md <refs>): space-backdrop-a (4 sky tiles),
space-backdrop-b (wrecks + cities atlases), space-backdrop-c (nebula gas, twinkle, debris atlases).
Output lands in Pause/Assets/Art/Worlds/Space/backdrop_v4~/ of each codex-space-backdrop-<x> worktree.
