using System.Diagnostics;
using Cataclysm3D.Game.World;
using Cataclysm3D.Importer;
using Cataclysm3D.Importer.Creatures;
using Cataclysm3D.Importer.Items;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>
/// Loads the slice of the CDDA snapshot the game uses so far: tiles, palettes, house mapgen, city
/// buildings, monsters, body parts, items and item groups, and the starting profession's kit.
/// </summary>
public sealed class Content
{
    public ContentDatabase Database { get; } = new();
    public TileRegistry Registry { get; }
    public MapgenGenerator Mapgen { get; }
    public TownGenerator Towns { get; }
    public MonsterRegistry Monsters { get; }
    public ItemRegistry Items { get; }
    public IReadOnlyList<BodyPartType> BodyParts { get; }

    /// <summary>The kit a new survivor starts with (CDDA's Unemployed profession), by gender.</summary>
    public List<Item> StartingKit(bool male, Random rng) => Outfit.Starting(Database, Items, Outfit.DefaultProfession, male, rng);

    /// <summary>A random CDDA hair style in a natural color.</summary>
    public Hair? RandomHair(Random rng) => Outfit.RandomHair(Database, rng);

    /// <summary>Tileset sprites for rendering, or null when no tileset is fetched.</summary>
    public TileArt? Art { get; }

    public long LoadMilliseconds { get; }

    public Content()
    {
        var clock = Stopwatch.StartNew();
        string json = FindSnapshot();
        TownContent.Load(Database, json);
        MonsterLoader.LoadContent(Database, json);
        Outfit.LoadContent(Database, json);
        ItemLoader.LoadContent(Database, json);
        Registry = TileTypeLoader.Load(Database);
        Items = ItemLoader.Registry(Database);
        Mapgen = new MapgenGenerator(Database, Registry, new MapgenLibrary(Database)) { Items = Items };
        Towns = new TownGenerator(Mapgen, Registry, CityBuilding.LoadAll(Database));
        Monsters = MonsterLoader.Registry(Database);
        BodyParts = MonsterLoader.BodyParts(Database);
        LoadMilliseconds = clock.ElapsedMilliseconds;
        Art = TileArt.Load(RepoRoot, Registry, id => Monsters.Type(id)?.LooksLike
            ?? (Database.Contains(ContentDatabase.ItemFamily, id) ? Items.Type(id)?.LooksLike : null));
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
