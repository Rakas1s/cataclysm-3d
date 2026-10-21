// Loads the CDDA snapshot, resolves every definition and reports counts and problems.
// Usage: dotnet run --project tools/ImportCheck [path-to-data-dir ...]
using System.Diagnostics;
using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;

// Usage: dotnet run --project tools/ImportCheck --print <om_terrain> [seed]
if (args.Length >= 2 && args[0] == "--print")
{
    var pdb = new ContentDatabase();
    pdb.LoadDirectory("cdda-snapshot/data/json");
    var preg = TileTypeLoader.Load(pdb);
    var result = new MapgenGenerator(pdb, preg, new MapgenLibrary(pdb))
        .Generate(args[1], args.Length > 2 ? int.Parse(args[2]) : 1);
    for (int y = 0; y < result.Map.Height; y++)
    {
        var line = new System.Text.StringBuilder();
        for (int x = 0; x < result.Map.Width; x++)
            line.Append(result.Map.FurnitureAt(x, y)?.Symbol ?? result.Map.TerrainAt(x, y).Symbol);
        Console.WriteLine(line);
    }
    var ids = new SortedSet<string>(StringComparer.Ordinal);
    for (int y = 0; y < result.Map.Height; y++)
    for (int x = 0; x < result.Map.Width; x++)
    {
        ids.Add(result.Map.TerrainAt(x, y).Id);
        if (result.Map.FurnitureAt(x, y) is { } f)
            ids.Add(f.Id);
    }
    Console.WriteLine(string.Join(" ", ids));
    foreach (var w in result.Warnings)
        Console.WriteLine($"warning: {w}");
    return 0;
}

// Usage: dotnet run --project tools/ImportCheck --town [seed] — prints every z-level of the M2 town.
if (args.Length >= 1 && args[0] == "--town")
{
    // The same content slice the game loads, so this prints exactly the game's town.
    var tdb = new ContentDatabase();
    TownContent.Load(tdb, "cdda-snapshot/data/json");
    var treg = TileTypeLoader.Load(tdb);
    var towns = new TownGenerator(new MapgenGenerator(tdb, treg, new MapgenLibrary(tdb)), treg, CityBuilding.LoadAll(tdb));
    var town = towns.Generate(TownContent.SouthRow, TownContent.NorthRow, args.Length > 1 ? int.Parse(args[1]) : 1);
    for (int z = town.Map.MaxZ; z >= town.Map.MinZ; z--)
    {
        Console.WriteLine($"z {z}:");
        var level = town.Map.Level(z);
        for (int y = 0; y < level.Height; y++)
        {
            var line = new System.Text.StringBuilder();
            for (int x = 0; x < level.Width; x++)
                line.Append(level.FurnitureAt(x, y)?.Symbol ?? level.TerrainAt(x, y).Symbol);
            Console.WriteLine(line);
        }
    }
    foreach (var lot in town.Lots)
        Console.WriteLine($"{lot.Building.Id} at {lot.X},{lot.Y} turned {lot.QuarterTurns * 90}°: " +
                          string.Join(", ", lot.Building.Levels.OrderBy(l => l.Key).Select(l => $"z{l.Key} {l.Value}")));
    foreach (var w in town.Warnings.Distinct().Take(30))
        Console.WriteLine($"warning: {w}");
    return 0;
}

// Usage: dotnet run --project tools/ImportCheck --loot [seed] — what the game's town spawns as loot.
if (args.Length >= 1 && args[0] == "--loot")
{
    var ldb = new ContentDatabase();
    var lclock = Stopwatch.StartNew();
    TownContent.Load(ldb, "cdda-snapshot/data/json");
    Cataclysm3D.Importer.Items.ItemLoader.LoadContent(ldb, "cdda-snapshot/data/json");
    Console.WriteLine($"Loaded in {lclock.ElapsedMilliseconds} ms");
    var lreg = TileTypeLoader.Load(ldb);
    var items = Cataclysm3D.Importer.Items.ItemLoader.Registry(ldb);
    var gen = new MapgenGenerator(ldb, lreg, new MapgenLibrary(ldb)) { Items = items };
    var town = new TownGenerator(gen, lreg, CityBuilding.LoadAll(ldb))
        .Generate(TownContent.SouthRow, TownContent.NorthRow, args.Length > 1 ? int.Parse(args[1]) : 1);
    Console.WriteLine($"Generated in {lclock.ElapsedMilliseconds} ms");
    var all = town.Map.Items.All.ToList();
    foreach (var lot in town.Lots)
    {
        var inLot = all.Where(t => t.Pos.X >= lot.X && t.Pos.X < lot.X + lot.Width && t.Pos.Y >= lot.Y && t.Pos.Y < lot.Y + lot.Height).ToList();
        Console.WriteLine($"{lot.Building.Id}: {inLot.Sum(t => t.Items.Count)} items on {inLot.Count} tiles");
    }
    var byId = all.SelectMany(t => t.Items).GroupBy(i => i.DisplayName).OrderByDescending(g => g.Count());
    Console.WriteLine(string.Join(", ", byId.Take(60).Select(g => $"{g.Key} x{g.Count()}")));
    Console.WriteLine($"Missing ids: {string.Join(", ", items.Missing.Take(40))} ({items.Missing.Count})");
    foreach (var w in town.Warnings.Where(w => w.Contains("item")).Distinct().Take(20))
        Console.WriteLine($"warning: {w}");
    return 0;
}

var dirs = args.Length > 0 ? args :new[] { "cdda-snapshot/data/core", "cdda-snapshot/data/json" };
var db = new ContentDatabase();
var clock = Stopwatch.StartNew();

foreach (var dir in dirs)
{
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Missing {dir}. Run tools/fetch-cdda-snapshot.ps1 first.");
        return 1;
    }
    db.LoadDirectory(dir);
}
Console.WriteLine($"Loaded in {clock.ElapsedMilliseconds} ms");

int failures = 0;
var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
foreach (var family in db.Families.ToList())
{
    try
    {
        counts[family] = db.All(family).Count();
    }
    catch (Exception e)
    {
        failures++;
        Console.WriteLine($"FAIL {family}: {e.Message}");
    }
}
Console.WriteLine($"Resolved in {clock.ElapsedMilliseconds} ms total");

Console.WriteLine();
foreach (var (family, count) in counts.OrderByDescending(c => c.Value).Take(25))
    Console.WriteLine($"{count,7}  {family}");
Console.WriteLine($"{counts.Count} families, {counts.Values.Sum()} definitions, {db.Unkeyed.Count} unkeyed");

Console.WriteLine();
Console.WriteLine($"{db.Warnings.Count} warnings");
foreach (var group in db.Warnings
             .Select(w => w[(w.IndexOf(": ", StringComparison.Ordinal) + 2)..])
             .GroupBy(w => System.Text.RegularExpressions.Regex.Replace(w, "\"[^\"]*\"", "\"…\""))
             .OrderByDescending(g => g.Count())
             .Take(15))
    Console.WriteLine($"{group.Count(),7}  {group.Key}");

// Generate every single-tile JSON mapgen and summarize what we can't place yet.
var registry = TileTypeLoader.Load(db);
var library = new MapgenLibrary(db);
var generator = new MapgenGenerator(db, registry, library);
int generated = 0, mapgenFailures = 0;
var mapgenWarnings = new Dictionary<string, int>(StringComparer.Ordinal);
clock.Restart();
foreach (var omt in library.OmTerrains)
{
    try
    {
        foreach (var w in generator.Generate(omt, seed: 1).Warnings)
        {
            string key = System.Text.RegularExpressions.Regex.Replace(w, "'.'|\"[^\"]*\"", "…");
            mapgenWarnings[key] = mapgenWarnings.GetValueOrDefault(key) + 1;
        }
        generated++;
    }
    catch (Exception e)
    {
        if (mapgenFailures++ < 10)
            Console.WriteLine($"MAPGEN FAIL {omt}: {e.Message}");
    }
}
Console.WriteLine();
Console.WriteLine($"Mapgen: {generated} generated, {mapgenFailures} failed in {clock.ElapsedMilliseconds} ms");
foreach (var (warning, count) in mapgenWarnings.OrderByDescending(w => w.Value).Take(10))
    Console.WriteLine($"{count,7}  {warning}");

return failures == 0 ? 0 : 1;
