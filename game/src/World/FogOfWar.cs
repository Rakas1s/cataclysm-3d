using Cataclysm3D.Sim.Vision;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Draws the sim's <see cref="FieldOfView"/> over the world. Every world material samples a small
/// texture holding one byte per tile per z-level: visible tiles draw normally, remembered ones
/// desaturated and dim (with a faint glow so memory shows at night too), and tiles never seen are
/// black. A face takes the state of the tile it faces, so a wall reads as seen from the room you
/// saw it from. Levels above the player aren't fogged: you see their outsides, and the cutaway
/// hides them when you're indoors. Only the texture changes when sight changes; no remeshing.
/// </summary>
public sealed class FogOfWar
{
    public const byte Unseen = 0;
    public const byte Remembered = 128;
    public const byte Visible = 255;

    private static readonly List<ShaderMaterial> Materials = new();
    private static ImageTexture? _texture;
    private static Vector4 _rect;

    private byte[] _data = Array.Empty<byte>();
    private WorldMap? _map;
    private int _version = -1;
    private int _z = int.MinValue;

    /// <summary>A world material that obeys the fog: vertex colors times an optional texture.</summary>
    public static ShaderMaterial CreateMaterial(float roughness, Texture2D? albedo = null, bool glass = false)
    {
        var material = new ShaderMaterial { Shader = glass ? GlassShader : OpaqueShader };
        material.SetShaderParameter("roughness", roughness);
        if (albedo is not null)
            material.SetShaderParameter("albedo_tex", albedo);
        Apply(material);
        Materials.Add(material);
        return material;
    }

    private static void Apply(ShaderMaterial material)
    {
        material.SetShaderParameter("fow_tex", _texture);
        material.SetShaderParameter("fow_rect", _rect);
        material.SetShaderParameter("level_height", WorldView.LevelHeight);
    }

    /// <summary>Redraws the fog if sight changed since the last call.</summary>
    public void Update(WorldMap map, FieldOfView fov)
    {
        int z = fov.Viewer.Z;
        if (ReferenceEquals(map, _map) && fov.Version == _version && z == _z)
            return;
        int w = map.Width, h = map.Height, levels = map.MaxZ - map.MinZ + 1;
        bool resized = _data.Length != w * h * levels || !ReferenceEquals(map, _map);
        _map = map;
        _version = fov.Version;
        _z = z;
        if (resized)
            _data = new byte[w * h * levels];

        int at = 0;
        for (int lz = map.MinZ; lz <= map.MaxZ; lz++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++, at++)
        {
            var p = new TilePos(x, y, lz);
            _data[at] = lz > z || fov.IsVisible(p) ? Visible : fov.HasSeen(p) ? Remembered : Unseen;
        }

        var image = Image.CreateFromData(w, h * levels, false, Image.Format.R8, _data);
        if (_texture is null || resized)
        {
            _texture = ImageTexture.CreateFromImage(image);
            _rect = new Vector4(w, h, map.MinZ, levels);
            foreach (var m in Materials)
                Apply(m);
        }
        else
        {
            _texture.Update(image);
        }
    }

    private static Shader OpaqueShader => _opaqueShader ??= new Shader { Code = Code(glass: false) };
    private static Shader GlassShader => _glassShader ??= new Shader { Code = Code(glass: true) };
    private static Shader? _opaqueShader;
    private static Shader? _glassShader;

    private static string Code(bool glass) => $$"""
        shader_type spatial;
        render_mode {{(glass ? "blend_mix, cull_disabled, depth_draw_opaque" : "cull_back")}};

        uniform sampler2D albedo_tex : source_color, filter_nearest, hint_default_white;
        uniform float roughness = 0.95;
        // One byte per tile per level: 0 unseen, 128 remembered, 255 visible.
        uniform sampler2D fow_tex : filter_nearest, hint_default_white;
        // Map width and height in tiles, lowest z-level, number of levels. Width 0: no fog.
        uniform vec4 fow_rect = vec4(0.0);
        uniform float level_height = 3.0;

        varying vec3 world_pos;
        varying vec3 world_normal;

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
        }

        vec3 srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
        }

        float fog_state() {
            if (fow_rect.x <= 0.0) return 1.0;
            // The tile this face looks out onto.
            vec3 p = world_pos + world_normal * 0.05;
            ivec2 t = ivec2(floor(p.xz));
            int level = int(floor(p.y / level_height)) - int(fow_rect.z);
            if (t.x < 0 || t.y < 0 || t.x >= int(fow_rect.x) || t.y >= int(fow_rect.y) || level < 0 || level >= int(fow_rect.w))
                return 1.0;
            return texelFetch(fow_tex, ivec2(t.x, t.y + level * int(fow_rect.y)), 0).r;
        }

        void fragment() {
            vec4 c = texture(albedo_tex, UV) * vec4(srgb_to_linear(COLOR.rgb), COLOR.a);
            vec3 albedo = c.rgb;
            float state = fog_state();
            if (state < 0.75) {
                float gray = dot(albedo, vec3(0.299, 0.587, 0.114));
                vec3 memory = mix(vec3(gray), albedo, 0.2);
                bool seen = state > 0.25;
                albedo = seen ? memory * 0.3 : vec3(0.0);
                EMISSION = seen ? memory * 0.05 : vec3(0.0);
                SPECULAR = 0.0;
            }
            ALBEDO = albedo;
            ROUGHNESS = roughness;
            {{(glass ? "ALPHA = c.a; METALLIC = 0.2;" : "")}}
        }
        """;
}
