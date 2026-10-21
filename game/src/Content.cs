using System.Diagnostics;
using Cataclysm3D.Game.World;
using Cataclysm3D.Importer;
using Cataclysm3D.Importer.Creatures;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>Loads the slice of the CDDA snapshot the game uses so far: tiles, palettes, house mapgen, city buildings, monsters and body parts.</summary>
public sealed class Content
{
    public ContentDatabase Database { get; } = new();
    public TileRegistry Registry { get; }
    public MapgenGenerator Mapgen { get; }
    public TownGenerator Towns { get; }
    public MonsterRegistry Monsters { get; }
    public IReadOnlyList<BodyPartType> BodyParts { get; }

    /// <summary>Tileset sprites for rendering, or null when no tileset is fetched.</summary>
    public TileArt? Art { get; }

    public long LoadMilliseconds { get; }

    public Content()
    {
        var clock = Stopwatch.StartNew();
        string json = FindSnapshot();
        TownContent.Load(Database, json);
        MonsterLoader.LoadContent(Database, json);
        Registry = TileTypeLoader.Load(Database);
        Mapgen = new MapgenGenerator(Database, Registry, new MapgenLibrary(Database));
        Towns = new TownGenerator(Mapgen, Registry, CityBuilding.LoadAll(Database));
        Monsters = MonsterLoader.Registry(Database);
        BodyParts = MonsterLoader.BodyParts(Database);
        LoadMilliseconds = clock.ElapsedMilliseconds;
        Art = TileArt.Load(RepoRoot, Registry, id => Monsters.Type(id)?.LooksLike);
    }

    public Town GenerateTown(int seed) => Towns.Generate(TownContent.SouthRow, TownContent.NorthRow, seed);

    /// <summary>The repository root: game/ sits next to cdda-snapshot/ and config/.</summary>
    private static string RepoRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    private static string FindSnapshot()
    {
        string path = Path.Combine(RepoRoot, "cdda-snapshot", "data", "json");
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"CDDA snapshot not found at {path}. Run tools/fetch-cdda-snapshot.ps1.");
        return path;
    }
}
