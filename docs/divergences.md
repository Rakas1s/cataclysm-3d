# Divergences from CDDA

Every deliberate rule change from vanilla CDDA goes here, with the reason.

| System | CDDA behavior | Ours | Why |
|---|---|---|---|
| Looking | No facing | Free mouse-look, costs no moves | First-person camera |
| Mapgen (M1) | Places items, monsters, nested chunks, loot, vehicles | Terrain and furniture only | Not implemented yet; symbols for those tables are recognized, not placed |
| Mapgen (M1) | `predecessor_mapgen` generates the underlying terrain first | `t_null` keeps `fill_ter` (or dirt) | Not implemented yet |
| Smashing (M1) | Bash strength from Str + wielded weapon; noise alerts monsters | Fixed unarmed strength 10 vs CDDA's `str_min`–`str_max` roll; no noise yet | No character stats or sound until M5/M6 |
| Locked doors (M1) | Open from inside; lockpicking and keys from outside | Open from inside (standing on a roofed tile); smash from outside | Lockpicking needs items and skills |
| Mapgen (M2) | Parameters with `"scope": "overmap_special"` are rolled once per building, so every floor shares them | Rolled per z-level, so floors of one house can differ (e.g. paint) | Each level is a separate mapgen run for now |
| Overmap (M2) | Overmap generation grows cities along roads from `city_building`s in any rotation, with CDDA's road mapgen | A fixed six-house town: two rows of single-tile buildings facing a straight, hand-laid road (sidewalk, pavement, dashed line) | Full overmap generation is post-MVP; multi-tile buildings and east/west rotations come later |
| Z-levels (M2) | Stepping onto open air (`NO_FLOOR`) drops you to the level below, with fall damage | Open air is impassable ("You'd fall") | No falling or damage yet |
| Stairs (M2) | You take stairs with `<` / `>` while standing on them | Walking onto stairs or a ladder takes it right away; arriving on the other end doesn't, so you can step off. `<` / `>` still work | Fewer keypresses in first person |
| Stairs (M2) | Going up or down lands on matching stairs found across the map, or prompts when there are none | The nearest matching stairs within 12 tiles, else straight up/down if walkable; stairs and ladders both cost 100 moves | Close enough for CDDA houses, whose stairs line up exactly |
| Climbing (M2) | Climbing a downspout or gutter drop is an examine/climb action with a chance to fail | `<` / `>` next to any `CLIMBABLE` terrain always succeeds, costs 200 moves, and lands on the nearest walkable tile above/below it | No skills or failure rolls yet |
