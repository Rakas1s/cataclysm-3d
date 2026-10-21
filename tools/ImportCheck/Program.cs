// Loads the CDDA snapshot, resolves every definition and reports counts and problems.
// Usage: dotnet run --project tools/ImportCheck [path-to-data-dir ...]
using System.Diagnostics;
using Cataclysm3D.Importer;

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

return failures == 0 ? 0 : 1;
