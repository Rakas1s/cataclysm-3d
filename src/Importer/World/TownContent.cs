namespace Cataclysm3D.Importer.World;

/// <summary>
/// The slice of the CDDA snapshot the town needs (tiles, palettes, house and basement mapgen,
/// city buildings) and the fixed M2 town layout. Shared by the game and tools/ImportCheck so
/// both generate the same town.
/// </summary>
public static class TownContent
{
    /// <summary>
    /// South of the road, facing north, then north of it, facing south. Picked for variety:
    /// basements, second floors, a plain bungalow and a garage. Houses whose ground floor is
    /// built from nested mapgen (house_05, …) would come out empty until nested chunks exist.
    /// </summary>
    public static readonly string[] SouthRow = { "house_two_story_basement", "bungalow01", "house_w_1" };
    public static readonly string[] NorthRow = { "2storyModern01", "house_08", "house_garage" };

    /// <param name="jsonRoot">The snapshot's <c>data/json</c> directory.</param>
    public static void Load(ContentDatabase db, string jsonRoot)
    {
        db.LoadDirectory(Path.Combine(jsonRoot, "furniture_and_terrain"));
        db.LoadDirectory(Path.Combine(jsonRoot, "mapgen_palettes"));
        db.LoadDirectory(Path.Combine(jsonRoot, "mapgen", "house"));
        db.LoadDirectory(Path.Combine(jsonRoot, "mapgen", "basement"));
        db.LoadFile(Path.Combine(jsonRoot, "overmap", "multitile_city_buildings.json"));
        db.LoadFile(Path.Combine(jsonRoot, "regional_map_settings.json"));
    }
}
