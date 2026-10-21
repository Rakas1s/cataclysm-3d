using Cataclysm3D.Sim;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// The sky by the game clock: a sun that crosses from east to west (warm and low at dawn and
/// dusk), a dim blue moon at night, and sky, ambient light and haze that fade with them. Sight
/// itself is the sim's call (<see cref="Sim.Vision.FieldOfView"/>); this only makes the picture
/// match it.
/// </summary>
public partial class DayNight : Node3D
{
    private readonly DirectionalLight3D _sun = new()
    {
        Name = "Sun",
        ShadowEnabled = true,
        DirectionalShadowMaxDistance = 80,
    };

    private readonly DirectionalLight3D _moon = new()
    {
        Name = "Moon",
        ShadowEnabled = true,
        DirectionalShadowMaxDistance = 60,
        LightColor = new Color(0.62f, 0.72f, 1f),
    };

    private readonly ProceduralSkyMaterial _sky = new()
    {
        GroundBottomColor = new Color(0.2f, 0.2f, 0.18f),
        GroundHorizonColor = new Color(0.55f, 0.55f, 0.5f),
    };

    private Godot.Environment _environment = null!;
    private long _shownMinute = long.MinValue;

    private static readonly Color DayTop = new(0.38f, 0.5f, 0.62f);
    private static readonly Color DayHorizon = new(0.68f, 0.7f, 0.7f);
    private static readonly Color DuskTop = new(0.22f, 0.25f, 0.42f);
    private static readonly Color DuskHorizon = new(0.92f, 0.56f, 0.34f);
    private static readonly Color NightTop = new(0.008f, 0.012f, 0.03f);
    private static readonly Color NightHorizon = new(0.03f, 0.04f, 0.07f);
    private static readonly Color NightAmbient = new(0.42f, 0.5f, 0.75f);
    private static readonly Color DayAmbient = new(0.75f, 0.78f, 0.82f);
    private static readonly Color DuskAmbient = new(0.95f, 0.75f, 0.6f);
    private static readonly Color NoonSun = new(1f, 0.96f, 0.88f);
    private static readonly Color LowSun = new(1f, 0.55f, 0.3f);

    public override void _Ready()
    {
        AddChild(_sun);
        AddChild(_moon);
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            SsaoEnabled = true,
            SsaoRadius = 0.6f,
            SsaoIntensity = 1.6f,
            FogEnabled = true,
            FogDensity = 0.006f,
        };
        AddChild(new WorldEnvironment { Environment = _environment });
    }

    /// <summary>Moves the sun and moon to <paramref name="now"/>. Cheap to call every frame.</summary>
    public void Show(GameTime now)
    {
        // A minute moves the sun a quarter of a degree; no need to redo it more often.
        long minute = now.Seconds / GameTime.SecondsPerMinute;
        if (minute == _shownMinute)
            return;
        _shownMinute = minute;

        float sunAlt = (float)Celestial.SunAltitude(now);
        float day = SmoothStep(-6, 8, sunAlt);
        float dusk = Mathf.Clamp(1 - Mathf.Abs(sunAlt - 1) / 9, 0, 1);

        Aim(_sun, Celestial.SunAzimuth(now), Math.Max(sunAlt, 2));
        _sun.LightEnergy = 1.15f * SmoothStep(-1, 12, sunAlt);
        _sun.LightColor = LowSun.Lerp(NoonSun, SmoothStep(3, 30, sunAlt));
        _sun.Visible = _sun.LightEnergy > 0.001f;

        float moonAlt = (float)Celestial.MoonAltitude(now);
        Aim(_moon, Celestial.MoonAzimuth(now), Math.Max(moonAlt, 5));
        _moon.LightEnergy = 0.16f * Celestial.MoonQuarters(now) / 4 * SmoothStep(-2, 10, moonAlt) * (1 - day);
        _moon.Visible = _moon.LightEnergy > 0.001f;

        var top = NightTop.Lerp(DayTop, day).Lerp(DuskTop, dusk * 0.6f);
        var horizon = NightHorizon.Lerp(DayHorizon, day).Lerp(DuskHorizon, dusk * 0.7f);
        _sky.SkyTopColor = top;
        _sky.SkyHorizonColor = horizon;
        _sky.GroundHorizonColor = horizon.Darkened(0.2f);
        _sky.GroundBottomColor = top.Darkened(0.6f);

        // Ambient follows CDDA's natural light (on a square-root curve, as eyes adapt), so a
        // moonlit street is dim blue but readable, and a moonless night nearly black. Rooms the
        // sim says are too dark are hidden by the fog of war, not by this.
        float natural = Mathf.Clamp(Celestial.SunLight(now) / Celestial.DaylightLevel, 0, 1);
        _environment.AmbientLightColor = NightAmbient.Lerp(DayAmbient, day).Lerp(DuskAmbient, dusk * 0.35f);
        _environment.AmbientLightEnergy = 0.02f + 0.7f * Mathf.Sqrt(natural);
        _environment.FogLightColor = horizon.Lerp(top, 0.4f);
    }

    /// <summary>Points a directional light from a compass bearing and altitude (degrees) down at the world.</summary>
    private static void Aim(DirectionalLight3D light, double azimuth, double altitude)
    {
        // Tile x grows east (+X) and y south (+Z), so north is -Z.
        float az = Mathf.DegToRad((float)azimuth), alt = Mathf.DegToRad((float)altitude);
        var toward = new Vector3(Mathf.Sin(az) * Mathf.Cos(alt), Mathf.Sin(alt), -Mathf.Cos(az) * Mathf.Cos(alt));
        light.Basis = Basis.LookingAt(-toward, Mathf.Abs(toward.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    private static float SmoothStep(float from, float to, float x) => Mathf.SmoothStep(from, to, x);
}
