# CDDA snapshot

Pinned copy of CDDA content used by the importer. Not committed; run:

```powershell
./tools/fetch-cdda-snapshot.ps1
```

This sparse-clones the pinned CDDA tag and copies `data/json`, `data/core` and CDDA's `LICENSE.txt`
here, plus every tileset listed in `config/tilesets.json` under `gfx/` (UndeadPeople first, Ultica
as fallback). `SNAPSHOT.txt` records the exact commits.
