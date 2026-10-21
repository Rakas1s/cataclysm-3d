using Cataclysm3D.Importer.Tiles;

namespace Cataclysm3D.Tests;

public sealed class TilesetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "c3d-tiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Writes a PNG signature and IHDR, which is all the parser reads.</summary>
    private static void FakePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        File.WriteAllBytes(path, b);
    }

    private string Write(string relative, string text)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A packed tileset: two sheets, the second with tall offset sprites.</summary>
    private Tileset Packed()
    {
        FakePng(Path.Combine(_dir, "packed", "normal.png"), 64, 64); // 2×2 sprites: 0..3
        FakePng(Path.Combine(_dir, "packed", "tall.png"), 64, 64); // 2×1 sprites of 32×64: 4..5
        FakePng(Path.Combine(_dir, "packed", "filler.png"), 32, 32); // 6
        Write("packed/tile_config.json", """
            {
              "tile_info": [ { "width": 32, "height": 32, "pixelscale": 1 } ],
              "tiles-new": [
                { "file": "normal.png", "tiles": [
                    { "id": "t_floor", "fg": 0 },
                    { "id": ["t_wall", "t_wall_alias"], "fg": 1, "bg": 0, "multitile": true,
                      "additional_tiles": [
                        { "id": "center", "fg": 2 },
                        { "id": "edge", "fg": [2, 3] },
                        { "id": "corner", "fg": 3 } ] },
                    { "id": "t_grass", "fg": [ { "weight": 1, "sprite": 0 }, { "weight": 3, "sprite": 3 } ] },
                    { "id": "t_grass_season_winter", "fg": 1 },
                    { "id": "f_chair", "fg": [0, 1, 2, 3], "rotates": true }
                ] },
                { "file": "tall.png", "sprite_width": 32, "sprite_height": 64, "sprite_offset_x": 0, "sprite_offset_y": -32,
                  "tiles": [ { "id": "f_fridge", "fg": 5 } ] },
                { "file": "filler.png", "tiles": [ { "id": "t_floor", "fg": 6 }, { "id": "f_lamp", "fg": 6 } ] },
                { "file": "fallback.png", "tiles": [], "ascii": [ { "offset": 0, "bold": false, "color": "BLACK" } ] }
              ]
            }
            """);
        return Tileset.Load(Path.Combine(_dir, "packed"), "Packed");
    }

    /// <summary>An unpacked ("compose") tileset like UndeadPeople.</summary>
    private Tileset Unpacked()
    {
        string root = Path.Combine(_dir, "unpacked");
        Write("unpacked/tile_info.json", """
            [ { "pixelscale": 1, "width": 32, "height": 32 },
              { "normal_terrain.png": {} },
              { "tall_furniture.png": { "sprite_width": 32, "sprite_height": 48, "sprite_offset_y": -16 } },
              { "normal_ultica_filler.png": { "filler": true } } ]
            """);
        FakePng(Path.Combine(root, "pngs_normal_terrain_32x32", "walls", "t_wall", "t_wall.png"), 32, 32);
        FakePng(Path.Combine(root, "pngs_normal_terrain_32x32", "walls", "t_wall", "t_wall_center.png"), 32, 32);
        Write("unpacked/pngs_normal_terrain_32x32/walls/t_wall/t_wall.json", """
            { "id": "t_wall", "fg": ["t_wall"], "bg": [], "rotates": false, "multitile": true,
              "additional_tiles": [ { "id": "center", "fg": ["t_wall_center"], "bg": ["t_wall"] } ] }
            """);
        FakePng(Path.Combine(root, "pngs_tall_furniture_32x48", "f_fridge.png"), 32, 48);
        Write("unpacked/pngs_tall_furniture_32x48/f_fridge.json", """
            [ { "id": ["f_fridge", "f_fridge_off"], "fg": "f_fridge" } ]
            """);
        FakePng(Path.Combine(root, "pngs_normal_ultica_filler_32x32", "f_fridge_filler.png"), 32, 32);
        FakePng(Path.Combine(root, "pngs_normal_ultica_filler_32x32", "f_sink.png"), 32, 32);
        Write("unpacked/pngs_normal_ultica_filler_32x32/filler.json", """
            [ { "id": "f_fridge", "fg": "f_fridge_filler" }, { "id": "f_sink", "fg": [ { "weight": 2, "sprite": "f_sink" } ] } ]
            """);
        return Tileset.Load(root, "Unpacked");
    }

    [Fact]
    public void PackedConfigAssignsSpriteIndicesAcrossSheets()
    {
        var set = Packed();
        Assert.Equal(7, set.Sprites.Count);
        Assert.Equal((32, 32), (set.TileWidth, set.TileHeight));

        var tall = set.Sprite(5);
        Assert.EndsWith("tall.png", tall.File);
        Assert.Equal((32, 0, 32, 64, 0, -32), (tall.X, tall.Y, tall.Width, tall.Height, tall.OffsetX, tall.OffsetY));
        Assert.Equal((32, 32), (set.Sprite(3).X, set.Sprite(3).Y));

        Assert.True(set.TryGet("t_wall_alias", out var alias));
        Assert.Equal(1, alias.Fg.Variants[0].Sprites[0]);
        Assert.Equal(0, alias.Bg.Variants[0].Sprites[0]);
        Assert.True(alias.Multitile);
        Assert.Equal(new[] { 2, 3 }, alias.Subtiles["edge"].Fg.Variants[0].Sprites);
    }

    [Fact]
    public void FillerSheetsOnlyAddMissingIds()
    {
        var set = Packed();
        Assert.Equal(0, set.Tiles["t_floor"].Fg.Variants[0].Sprites[0]);
        Assert.Equal(6, set.Tiles["f_lamp"].Fg.Variants[0].Sprites[0]);
    }

    [Fact]
    public void ParsesWeightedAndRotationLists()
    {
        var set = Packed();
        var grass = set.Tiles["t_grass"].Fg;
        Assert.Equal(2, grass.Variants.Count);
        Assert.Equal(3, grass.Variants[1].Weight);
        var chair = set.Tiles["f_chair"];
        Assert.True(chair.Rotates);
        Assert.Equal(new[] { 0, 1, 2, 3 }, chair.Fg.Variants[0].Sprites);
    }

    [Fact]
    public void UnpackedLayoutReadsSheetSizesAndSpriteNames()
    {
        var set = Unpacked();
        var fridge = set.Tiles["f_fridge_off"];
        var rect = set.Sprite(fridge.Fg.Variants[0].Sprites[0]);
        Assert.EndsWith("f_fridge.png", rect.File);
        Assert.Equal((32, 48, -16), (rect.Width, rect.Height, rect.OffsetY));

        var wall = set.Tiles["t_wall"];
        Assert.True(wall.Multitile);
        var center = wall.Subtiles["center"];
        Assert.EndsWith("t_wall_center.png", set.Sprite(center.Fg.Variants[0].Sprites[0]).File);
        Assert.EndsWith("t_wall.png", set.Sprite(center.Bg.Variants[0].Sprites[0]).File);
    }

    [Fact]
    public void UnpackedFillerDoesNotOverrideRealArt()
    {
        var set = Unpacked();
        Assert.EndsWith("f_fridge.png", set.Sprite(set.Tiles["f_fridge"].Fg.Variants[0].Sprites[0]).File);
        Assert.Equal(2, set.Tiles["f_sink"].Fg.Variants[0].Weight);
    }

    [Theory]
    [InlineData("pngs_normal_terrain_32x32", "normal_terrain")]
    [InlineData("pngs_tall_msx_filler_32x48", "tall_msx_filler")]
    [InlineData("pngs_huge_160x128", "huge")]
    public void SheetFolderNamesMatchTileInfoKeys(string folder, string sheet) =>
        Assert.Equal(sheet, Tileset.SheetName(folder));

    // ---- Resolution -----------------------------------------------------------------------

    private SpriteResolver Resolver(Dictionary<string, string>? looksLike = null)
    {
        looksLike ??= new();
        return new SpriteResolver(new[] { Unpacked(), Packed() }, id => looksLike.GetValueOrDefault(id));
    }

    [Fact]
    public void PrimaryTilesetWinsThenFallbackTileset()
    {
        var r = Resolver();
        Assert.Equal("Unpacked", r.Resolve("f_fridge")!.Tileset.Name);
        var floor = r.Resolve("t_floor")!;
        Assert.Equal("Packed", floor.Tileset.Name);
        Assert.Equal("t_floor", floor.MatchedId);
    }

    [Fact]
    public void FallsBackThroughLooksLikeChain()
    {
        var r = Resolver(new() { ["t_floor_fancy"] = "t_floor_odd", ["t_floor_odd"] = "t_wall" });
        var hit = r.Resolve("t_floor_fancy")!;
        Assert.Equal("t_wall", hit.MatchedId);
        Assert.Equal("Unpacked", hit.Tileset.Name);
        Assert.Equal("t_floor_fancy", hit.RequestedId);
    }

    [Fact]
    public void ExactIdInFallbackTilesetBeatsLooksLikeInPrimary()
    {
        // t_floor exists only in the fallback; its looks_like (t_wall) exists in the primary.
        var r = Resolver(new() { ["t_floor"] = "t_wall" });
        Assert.Equal("Packed", r.Resolve("t_floor")!.Tileset.Name);
    }

    [Fact]
    public void PrimaryOnlyResolutionFollowsLooksLikeWithinThePrimary()
    {
        var r = Resolver(new() { ["t_floor"] = "t_wall" });
        // t_floor itself is only in the fallback; within the primary, its looks_like wins.
        var hit = r.ResolvePrimary("t_floor")!;
        Assert.Equal(("Unpacked", "t_wall"), (hit.Tileset.Name, hit.MatchedId));
        Assert.Null(r.ResolvePrimary("f_lamp"));
    }

    [Fact]
    public void LooksLikeCyclesAndMissingIdsResolveToNull()
    {
        var r = Resolver(new() { ["a"] = "b", ["b"] = "a" });
        Assert.Null(r.Resolve("a"));
        Assert.Null(r.Resolve("t_nothing_like_this"));
    }

    [Fact]
    public void SeasonalIdWinsWhenPresent()
    {
        var r = Resolver();
        Assert.Equal("t_grass_season_winter", r.Resolve("t_grass", "winter")!.MatchedId);
        Assert.Equal("t_grass", r.Resolve("t_grass", "summer")!.MatchedId);
    }

    [Theory]
    [InlineData(false, false, false, false, "unconnected", 0)]
    [InlineData(true, true, true, true, "center", 0)]
    [InlineData(true, false, true, false, "edge", 0)]
    [InlineData(false, true, false, true, "edge", 1)]
    [InlineData(false, true, true, false, "corner", 0)]
    [InlineData(true, true, false, false, "corner", 1)]
    [InlineData(true, false, false, true, "corner", 2)]
    [InlineData(false, false, true, true, "corner", 3)]
    [InlineData(false, false, true, false, "end_piece", 0)]
    [InlineData(true, false, false, false, "end_piece", 2)]
    [InlineData(false, true, true, true, "t_connection", 0)]
    public void MultitilePiecesFollowCdda(bool n, bool e, bool s, bool w, string subtile, int rotation) =>
        Assert.Equal((subtile, rotation), Multitile.Subtile(Multitile.Mask(n, e, s, w)));

    [Fact]
    public void ChooseUsesRotationListsOrTurnsSingleSprites()
    {
        var packed = Packed();
        var wall = new ResolvedTile(packed, packed.Tiles["t_wall"], "t_wall", "t_wall");

        var edgeEw = SpriteResolver.Choose(wall, 0, 0, Multitile.Mask(false, true, false, true));
        Assert.Equal(("edge", 3, 0), (edgeEw.Subtile, edgeEw.Fg, edgeEw.Rotation));
        Assert.Equal(0, edgeEw.Bg); // inherited from the parent entry

        var corner = SpriteResolver.Choose(wall, 0, 0, Multitile.Mask(true, true, false, false));
        Assert.Equal(("corner", 3, 1), (corner.Subtile, corner.Fg, corner.Rotation));

        // No end_piece in this tileset: falls back to the base sprite.
        var end = SpriteResolver.Choose(wall, 0, 0, Multitile.Mask(true, false, false, false));
        Assert.Equal(1, end.Fg);

        var chair = new ResolvedTile(packed, packed.Tiles["f_chair"], "f_chair", "f_chair");
        Assert.Equal(2, SpriteResolver.Choose(chair, 5, 5, rotation: 2).Fg);
    }

    [Fact]
    public void WeightedVariantsAreStablePerPosition()
    {
        var packed = Packed();
        var grass = new ResolvedTile(packed, packed.Tiles["t_grass"], "t_grass", "t_grass");
        var picks = new List<int>();
        for (int i = 0; i < 400; i++)
            picks.Add(SpriteResolver.Choose(grass, i % 20, i / 20).Fg);
        Assert.Equal(picks, Enumerable.Range(0, 400).Select(i => SpriteResolver.Choose(grass, i % 20, i / 20).Fg));
        int heavy = picks.Count(p => p == 3);
        Assert.InRange(heavy, 220, 380); // weight 3 of 4
    }

    // ---- The real tilesets, when the snapshot is fetched ----------------------------------

    private static string? SnapshotGfx()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string gfx = Path.Combine(d.FullName, "cdda-snapshot", "gfx");
            if (Directory.Exists(Path.Combine(gfx, "UndeadPeople")) && Directory.Exists(Path.Combine(gfx, "UltimateCataclysm")))
                return gfx;
        }
        return null;
    }

    [Fact]
    public void LoadsTheSnapshotTilesets()
    {
        if (SnapshotGfx() is not { } gfx)
            return; // snapshot not fetched (CI); covered by the fixtures above
        var up = Tileset.Load(Path.Combine(gfx, "UndeadPeople"));
        var ultica = Tileset.Load(Path.Combine(gfx, "UltimateCataclysm"));
        Assert.True(up.Tiles.Count > 10000);
        Assert.True(ultica.Tiles.Count > 5000);

        var r = new SpriteResolver(new[] { up, ultica }, _ => null);
        foreach (var id in new[] { "t_wall_w", "t_floor", "t_door_c", "t_window", "f_table", "f_chair", "f_fridge", "f_bed", "t_grass", "t_shingle_flat_roof" })
        {
            var hit = r.Resolve(id);
            Assert.NotNull(hit);
            Assert.Same(up, hit!.Tileset);
            var choice = SpriteResolver.Choose(hit, 3, 4, Multitile.Mask(true, false, true, false));
            Assert.True(choice.Fg >= 0, id);
            Assert.True(File.Exists(up.Sprite(choice.Fg).File), id);
        }
        var fridge = up.Sprite(up.Tiles["f_fridge"].Fg.Variants[0].Sprites[0]);
        Assert.True(fridge.Height > 32); // a tall sprite, drawn up out of its tile
    }
}
