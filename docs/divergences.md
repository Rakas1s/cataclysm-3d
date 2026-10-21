# Divergences from CDDA

Every deliberate rule change from vanilla CDDA goes here, with the reason.

| System | CDDA behavior | Ours | Why |
|---|---|---|---|
| Looking | No facing | Free mouse-look, costs no moves | First-person camera |
| Mapgen (M1) | Places items, monsters, nested chunks, loot, vehicles | Terrain and furniture only | Not implemented yet; symbols for those tables are recognized, not placed |
| Mapgen (M1) | `predecessor_mapgen` generates the underlying terrain first | `t_null` keeps `fill_ter` (or dirt) | Overmap-level generation comes in M2 |
| Smashing (M1) | Bash strength from Str + wielded weapon; noise alerts monsters | Fixed unarmed strength 10 vs CDDA's `str_min`–`str_max` roll; no noise yet | No character stats or sound until M5/M6 |
| Locked doors (M1) | Open from inside; lockpicking and keys from outside | Open from inside (standing on a roofed tile); smash from outside | Lockpicking needs items and skills |
| Roofs (M1) | Separate `_roof` z-level mapgen | Flat ceiling slab on any tile whose terrain has a `roof` | Z-levels are M2 |
