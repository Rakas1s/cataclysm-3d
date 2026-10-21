using System.Diagnostics;
using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>Loads the slice of the CDDA snapshot the game uses so far: tiles, palettes and house mapgen.</summary>
public sealed class Content
{
    public ContentDatabase Database { get; } = new();
    public TileRegistry Registry { get; }
    public MapgenGenerator Mapgen { get; }
    public IReadOnlyList<string> Houses { get; }
    public long LoadMilliseconds { get; }

    public Content()
    {
        var clock = Stopwatch.StartNew();
        string root = FindSnapshot();
        Database.LoadDirectory(Path.Combine(root, "furniture_and_terrain"));
        Database.LoadDirectory(Path.Combine(root, "mapgen_palettes"));
        Database.LoadDirectory(Path.Combine(root, "mapgen", "house"));
        Database.LoadFile(Path.Combine(root, "regional_map_settings.json"));

        Registry = TileTypeLoader.Load(Database);
        var library = new MapgenLibrary(Database);
        Mapgen = new MapgenGenerator(Database, Registry, library);

        // Ground floors only; roofs and basements are separate z-levels (M2).
        Houses = library.OmTerrains
            .Where(o => !o.Contains("roof") && !o.Contains("basement")
                        && !System.Text.RegularExpressions.Regex.IsMatch(o, @"_[2-9]$"))
            .Order(StringComparer.Ordinal)
            .ToList();
        LoadMilliseconds = clock.ElapsedMilliseconds;
    }

    private static string FindSnapshot()
    {
        // game/ sits next to cdda-snapshot/ in the repo.
        string game = ProjectSettings.GlobalizePath("res://");
        string path = Path.GetFullPath(Path.Combine(game, "..", "cdda-snapshot", "data", "json"));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"CDDA snapshot not found at {path}. Run tools/fetch-cdda-snapshot.ps1.");
        return path;
    }
}
