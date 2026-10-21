# CDDA-3D — Build Plan (working title)

A first-person, turn-based, voxel 3D reimplementation of *Cataclysm: Dark Days Ahead* in Godot 4 (C#),
driven by CDDA's own JSON content — the same treatment Gen1Recomp gives Pokémon: a hand-written
native engine, original game data as the source of truth, a new presentation layer on top.

## Decisions (from interview)

| Topic | Decision |
|---|---|
| Engine | Clean-room rewrite in C#; CDDA's `data/json` is the content source |
| Data | Bundled, pinned snapshot of the **latest stable** CDDA release |
| Time | Turn-based first person; world waits for the player |
| Movement | 8-direction tile steps, smooth step animation, free mouse-look (looking costs no time) |
| Art | Voxel terrain (1 tile = 1 block column); **all** entities auto-voxelized from tileset sprites |
| Tileset | [UndeadPeople (MSX++ UnDeadPeople Edition v2)](https://github.com/Goodgulf-PL/UndeadPeopleTileset_Unpacked_New_Version) primary, Ultica fallback (both CC-BY-SA 3.0) |
| CDDA pin | `0.I-1` (latest stable tag) |
| UI | Modern overlay panels, keyboard-first, CDDA hotkeys |
| MVP must-haves | Z-levels, day/night + lighting/vision, sound propagation |
| Combat (MVP) | Melee + firearms |
| Repo | New public GitHub repo, CC-BY-SA 3.0 |
| Roles | Claude: engineering. You: references, assets/styles, design direction, playtesting |

## Architecture

```
cdda-snapshot/ (vendored data/json + gfx tileset, pinned tag)
        │
   [Importer]  C#, editor/CLI tool
   resolves copy-from / abstract / extend / delete, units, colors
        │  → normalized cache (typed C# records, serialized)
        ▼
   [Sim Core]  pure C#, no Godot types, deterministic, unit-tested
   map (submaps, z-levels) · creatures · items · turns/move-points
   vision/light · sound · combat · needs · mapgen · save/load
        │  events (moved, attacked, sound, tile changed, …)
        ▼
   [Presentation]  Godot
   chunk voxel mesher · entity voxel models · lights/fog · spatial audio
   camera/step animation · UI panels · input → sim actions
```

Key rule: the **Sim Core never references Godot**. That keeps it testable, lets us run headless
simulations, and lets a top-down debug view (or the CDDA ASCII view later) share the same sim.

## Core systems

### Data import
- Pin to the latest stable CDDA tag; vendor `data/json`, `data/core`, and one tileset (Ultica) into `cdda-snapshot/`.
- Importer handles CDDA's inheritance (`copy-from`, `abstract`, `extend`, `delete`, `relative`, `proportional`), unit strings (`"250 g"`, `"1 L"`), and id lookups.
- MVP filter: load everything, but only *implement behavior* for the subset of fields the MVP needs; log unknown fields so coverage is measurable.

### World & mapgen
- CDDA geometry: submap = 12×12 tiles, overmap terrain tile = 24×24, z-levels −10..+10.
- MVP: generate a small fixed overmap region (one town + forest/field edges + an evac shelter) instead of full overmap gen.
- Implement CDDA JSON mapgen: `rows`, palettes, `terrain`/`furniture`, `items`/item groups, `place_monsters`, rotation. Nested mapgen in a later milestone.

### Turns & time
- CDDA move-point model: speed 100 → 100 moves/turn; actions cost moves; 1 turn = 1 second.
- Loop: player issues action → sim advances creatures until the player can act again → presentation plays queued events quickly (step tweens, attack anims), skippable.
- Long actions (reading, crafting, waiting) run as activities with interrupts (monster seen, noise).

### Voxel presentation
- 1 tile = 1 m × 1 m footprint; z-level height ≈ 3 m (wall voxels at a finer grid, e.g. 16 per metre, for detail).
- Terrain/furniture → **archetypes** (floor, wall, window, door, fence, tree, counter, …) chosen from flags (`WALL`, `DOOR`, `TRANSPARENT`, `FLAT`, …) + per-id overrides.
- Per-submap greedy-meshed chunks, rebuilt on tile change.
- **Sprite→voxel pipeline** (offline tool): take each tileset sprite, extrude by alpha with depth heuristics, optional front/side sprite pairs, cache as `.vox`/mesh. Hand-made overrides replace generated ones where you supply better art.
- Above-floor levels and roofs fade out when indoors so interiors stay readable.

### Vision, light, day/night
- Sim computes CDDA-style light map + shadowcasting FOV per turn (authoritative for gameplay).
- Presentation: sun/moon directional light by time of day, real lights for lamps/flashlights/fires, and a fog-of-war mask that hides tiles/creatures the sim says you can't see (remembered tiles shown desaturated).

### Sound
- Sim sound events with volume (dB-ish) attenuated per tile and by walls; monsters investigate loud sources; deaf/sleep checks.
- Presentation: positional 3D audio + on-screen "you hear …" messages for unseen sources.

### Combat
- Melee: to-hit, bash/cut/stab damage, armor, per-body-part HP (simplified CDDA formulas, documented divergences).
- Firearms: guns, magazines, ammo types, reload, dispersion/recoil, aim-by-looking (mouse-look picks target tile/creature), gunfire noise feeding sound.

### Character & needs
- Stats, a handful of skills, hunger/thirst/fatigue/pain, body-part HP, eating/drinking/sleeping.

### UI (modern panels, CDDA keys)
- HUD: body status, needs, time/weather, messages log, held item.
- Panels: inventory (`i`), wield (`w`), wear (`W`), pick up (`g`), examine (`e`), fire (`f`), reload (`r`), eat (`E`), look (`x`), character (`@`), map (`m`).

### Save/load
- Versioned save of sim state + world seed; deterministic replay of mapgen from seed.

## Milestones

| # | Goal | Done when |
|---|---|---|
| M0 | Repo + skeleton | Godot 4 .NET project, Sim Core library, test project, CI build, LICENSE/CREDITS, pinned CDDA snapshot |
| M1 | Importer + one building | Importer loads all JSON with inheritance resolved; one mapgen house renders as voxels; walk it in first person (8-dir steps, mouse-look) |
| M2 | Town + z-levels | Fixed overmap region with a town; stairs, basements, roofs; chunk streaming |
| M3 | Light & vision | Day/night cycle, FOV fog, lamps/flashlight; sprite→voxel pipeline producing furniture/items |
| M4 | Monsters + turn loop | Move-point turn system; zombies with basic AI; melee combat; death |
| M5 | Items & survival | Item spawns from item groups, inventory/wield/wear, eat/drink/sleep, needs |
| M6 | Firearms + sound | Guns/ammo/reload/aim; sound propagation drawing zombies |
| M7 | MVP: Evac shelter + town | Start in evac shelter, loot town, survive, save/load. Playtest build |

Post-MVP candidates: crafting, construction, vehicles, full overmap gen, NPCs, mutations/bionics, CDDA mod loading, top-down/ASCII view toggle.

## Licensing & naming
- CDDA content and tilesets are **CC-BY-SA 3.0** → this project's bundled data is CC-BY-SA, with full attribution to CleverRaven and contributors in `CREDITS.md`. Code under CC-BY-SA as well (simplest, matches upstream) — or a compatible choice we confirm before first public commit.
- Use a distinct project name ("built on Cataclysm: DDA data"), not something that reads as an official CDDA release.

## What I need from you (references & assets)
1. **Style refs:** 5–10 images for the voxel look you want (e.g. Teardown, Cube World, Vintage Story, MagicaVoxel scenes), plus mood refs for lighting/night.
2. ~~**Tileset choice**~~ — decided: UndeadPeople, Ultica fallback.
3. **Priority hand-made models:** zombie (basic), player hands/arms, 2–3 melee weapons, 2 guns — either asset packs (CC0/CC-BY preferred) or MagicaVoxel commissions.
4. **Audio:** CC0/CC-BY packs for footsteps, zombie vocalizations, gunshots, doors, ambience.
5. **UI refs:** any game whose overlay panels you like.
6. **Playtest feel:** after M1 and M4, notes on step speed, camera height, head-bob, animation skip.

## Open questions / risks
- **Scope creep:** CDDA is enormous; we measure "coverage" via the importer's unknown-field log and only implement what milestones need.
- **Sprite extrusion quality:** auto-voxels from 32×32 sprites may look flat; budget time for depth heuristics and hand overrides.
- **Faithfulness vs. 3D:** some CDDA rules (e.g. vision through windows, crouching, vehicles) need 3D-specific decisions — log each divergence in `docs/divergences.md`.
- **Repo name** and final license choice — needed before M0.
