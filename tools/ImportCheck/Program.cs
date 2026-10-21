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

var dirs = args.Length > 0 ? args : new[] { "cdda-snapshot/data/core", "cdda-snapshot/data/json" };
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
