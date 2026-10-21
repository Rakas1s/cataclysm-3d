using System.Diagnostics;
using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>Loads the slice of the CDDA snapshot the game uses so far: tiles, palettes, house mapgen and city buildings.</summary>
public sealed class Content
{
    public ContentDatabase Database { get; } = new();
    public TileRegistry Registry { get; }
    public MapgenGenerator Mapgen { get; }
    public TownGenerator Towns { get; }
    public long LoadMilliseconds { get; }

    public Content()
    {
        var clock = Stopwatch.StartNew();
        TownContent.Load(Database, FindSnapshot());
        Registry = TileTypeLoader.Load(Database);
        Mapgen = new MapgenGenerator(Database, Registry, new MapgenLibrary(Database));
        Towns = new TownGenerator(Mapgen, Registry, CityBuilding.LoadAll(Database));
        LoadMilliseconds = clock.ElapsedMilliseconds;
    }

    public Town GenerateTown(int seed) => Towns.Generate(TownContent.SouthRow, TownContent.NorthRow, seed);

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
