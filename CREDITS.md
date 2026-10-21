# Credits

## Cataclysm: Dark Days Ahead

Game content (`data/json`), tilesets and the original game design come from
[Cataclysm: Dark Days Ahead](https://github.com/CleverRaven/Cataclysm-DDA) by CleverRaven and
over 1000 contributors, licensed under
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).

The exact CDDA release used is recorded in `cdda-snapshot/SNAPSHOT.txt` after fetching.

## Tilesets

The game draws its terrain and furniture from these tilesets' sprites. Floor, roof and wall
sprites are used as textures on the voxels, and furniture sprites are extruded into voxel
models (see `game/src/World/FurnitureModels.cs`). The generated models are adaptations of the
sprites, so they fall under the same license: **CC BY-SA 3.0**, attributed to the artists below.
The sprites are not changed on disk or redistributed by this repository. `tools/fetch-cdda-snapshot.ps1`
downloads them, and `cdda-snapshot/SNAPSHOT.txt` records the exact commits.

| Tileset | Role | Authors and source | License |
|---|---|---|---|
| MSX++ UnDeadPeople Edition v2 (UndeadPeople tileset) | Primary | The UndeadPeople tileset and MSX++ (MShockXotto+) artists, maintained at [Goodgulf-PL/UndeadPeopleTileset_Unpacked_New_Version](https://github.com/Goodgulf-PL/UndeadPeopleTileset_Unpacked_New_Version). Its filler sprites come from MShockXotto+ and UltimateCataclysm in [I-am-Erk/CDDA-Tilesets](https://github.com/I-am-Erk/CDDA-Tilesets). The full contributor notes are in its `credits.txt` and `duplicate.txt`, which the fetch script copies next to the sprites | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) |
| UltimateCataclysm (Ultica) | Fallback for ids UndeadPeople has no sprite for | The Ultica contributors, [I-am-Erk/CDDA-Tilesets](https://github.com/I-am-Erk/CDDA-Tilesets) (per-tile credits in its source folders), shipped in CDDA as `gfx/UltimateCataclysm`. Filler trees were originally drawn by Yar (opengameart.org) and edited by I-am-Erk | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) |

## Inspiration

- [Gen1Recomp / G1R Deluxe](https://github.com/bryanthaboi/gen1recomp): native reimplementation driven by original game data.
