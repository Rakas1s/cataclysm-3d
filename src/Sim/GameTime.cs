namespace Cataclysm3D.Sim;

public enum Season { Spring, Summer, Autumn, Winter }

/// <summary>
/// A moment on CDDA's calendar: one turn is one second, a season is 91 days and a year four
/// seasons. Second 0 is midnight at the start of spring, day 1, year 1.
/// </summary>
public readonly record struct GameTime(long Seconds) : IComparable<GameTime>
{
    public const int SecondsPerMinute = 60;
    public const int SecondsPerHour = 3600;
    public const int SecondsPerDay = 86400;
    public const int DaysPerSeason = 91;
    public const int DaysPerYear = DaysPerSeason * 4;

    /// <summary>When a new game starts: spring, day 1, 8:00, like CDDA's default scenario start.</summary>
    public static readonly GameTime GameStart = At(0, 8);

    /// <summary>The time <paramref name="hour"/>:<paramref name="minute"/> on a day counted from 0 (spring, day 1).</summary>
    public static GameTime At(int day, int hour, int minute = 0) =>
        new((long)day * SecondsPerDay + hour * SecondsPerHour + minute * SecondsPerMinute);

    /// <summary>Days since the start of the calendar, counted from 0.</summary>
    public int Day => (int)(Seconds / SecondsPerDay);

    public int Year => Day / DaysPerYear + 1;
    public int DayOfYear => Day % DaysPerYear;
    public Season Season => (Season)(DayOfYear / DaysPerSeason);

    /// <summary>Day of the season, counted from 1 as CDDA shows it.</summary>
    public int DayOfSeason => DayOfYear % DaysPerSeason + 1;

    public int SecondOfDay => (int)(Seconds % SecondsPerDay);
    public int Hour => SecondOfDay / SecondsPerHour;
    public int Minute => SecondOfDay / SecondsPerMinute % 60;

    /// <summary>Hours since midnight, with the fraction: 13.5 is half past one in the afternoon.</summary>
    public double HourOfDay => SecondOfDay / (double)SecondsPerHour;

    public GameTime Plus(long seconds) => new(Seconds + seconds);

    public int CompareTo(GameTime other) => Seconds.CompareTo(other.Seconds);
    public static bool operator <(GameTime a, GameTime b) => a.Seconds < b.Seconds;
    public static bool operator >(GameTime a, GameTime b) => a.Seconds > b.Seconds;
    public static bool operator <=(GameTime a, GameTime b) => a.Seconds <= b.Seconds;
    public static bool operator >=(GameTime a, GameTime b) => a.Seconds >= b.Seconds;

    /// <summary>"Spring, day 1, 08:00".</summary>
    public override string ToString() => $"{Season}, day {DayOfSeason}, {Hour:00}:{Minute:00}";
}

/// <summary>
/// Where the sun and moon are and how much light they give, after CDDA's calendar and weather
/// code: the sun follows a real path for CDDA's default latitude (spring day 1 is the equinox),
/// and natural light is on CDDA's scale, where full daylight is 100 and a tile is too dark to see
/// below <see cref="Vision.LightLevels.AmbientLow"/>.
/// </summary>
public static class Celestial
{
    /// <summary>CDDA's default latitude, in degrees north.</summary>
    public const double Latitude = 42.36;

    /// <summary>Earth's axial tilt, which sets how far the sun's path moves with the seasons.</summary>
    private const double AxialTilt = 23.44;

    /// <summary>Outdoor light at noon on a clear day (CDDA's <c>default_daylight_level</c>).</summary>
    public const float DaylightLevel = 100f;

    /// <summary>Light a moonless night still has (starlight).</summary>
    public const float Starlight = 1f;

    /// <summary>CDDA's <c>MOONLIGHT_PER_QUARTER</c>: each lit quarter of the moon adds this much light.</summary>
    public const float MoonlightPerQuarter = 2.25f;

    public const double SynodicMonthDays = 29.530588;

    /// <summary>Where in its cycle the moon is when the calendar starts: 0.25 is a waxing half moon.</summary>
    private const double MoonPhaseAtStart = 0.25;

    private const double Rad = Math.PI / 180;

    /// <summary>The sun's declination: 0 at the spring and autumn equinoxes, +tilt at midsummer.</summary>
    private static double Declination(GameTime t) =>
        AxialTilt * Math.Sin(2 * Math.PI * (t.Seconds / (double)GameTime.SecondsPerDay) / GameTime.DaysPerYear);

    /// <summary>The sun's angle in degrees west of the meridian: 0 at noon, -90 at 6:00.</summary>
    private static double HourAngle(GameTime t) => (t.HourOfDay - 12) * 15;

    /// <summary>Degrees of the sun above the horizon; negative below it.</summary>
    public static double SunAltitude(GameTime t)
    {
        double lat = Latitude * Rad, dec = Declination(t) * Rad, h = HourAngle(t) * Rad;
        return Math.Asin(Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(h)) / Rad;
    }

    /// <summary>Compass bearing of the sun in degrees: 0 north, 90 east, 180 south.</summary>
    public static double SunAzimuth(GameTime t)
    {
        double lat = Latitude * Rad, dec = Declination(t) * Rad, h = HourAngle(t) * Rad;
        double az = Math.Atan2(-Math.Cos(dec) * Math.Sin(h), Math.Sin(dec) * Math.Cos(lat) - Math.Cos(dec) * Math.Sin(lat) * Math.Cos(h));
        return (az / Rad + 360) % 360;
    }

    /// <summary>
    /// Degrees of the moon above the horizon. Approximated as the sun's path delayed by the moon's
    /// phase: a full moon rises at sunset, a waxing half moon at noon.
    /// </summary>
    public static double MoonAltitude(GameTime t) => SunAltitude(MoonLag(t));

    /// <summary>Compass bearing of the moon in degrees, approximated like <see cref="MoonAltitude"/>.</summary>
    public static double MoonAzimuth(GameTime t) => SunAzimuth(MoonLag(t));

    private static GameTime MoonLag(GameTime t) => t.Plus(-(long)(MoonPhase(t) * GameTime.SecondsPerDay));

    /// <summary>When the sun rises on the day <paramref name="t"/> falls on.</summary>
    public static GameTime Sunrise(GameTime t) => SunCrossing(t, rising: true);

    /// <summary>When the sun sets on the day <paramref name="t"/> falls on.</summary>
    public static GameTime Sunset(GameTime t) => SunCrossing(t, rising: false);

    private static GameTime SunCrossing(GameTime t, bool rising)
    {
        var noon = GameTime.At(t.Day, 12);
        double lat = Latitude * Rad, dec = Declination(noon) * Rad;
        double cosH = Math.Clamp(-Math.Tan(lat) * Math.Tan(dec), -1, 1);
        double hours = Math.Acos(cosH) / Rad / 15;
        return noon.Plus((long)Math.Round((rising ? -hours : hours) * GameTime.SecondsPerHour));
    }

    /// <summary>The moon's phase from 0 to 1: 0 new, 0.5 full.</summary>
    public static double MoonPhase(GameTime t)
    {
        double phase = t.Seconds / (double)GameTime.SecondsPerDay / SynodicMonthDays + MoonPhaseAtStart;
        return phase - Math.Floor(phase);
    }

    /// <summary>How many quarters of the moon are lit, from 0 (new) to 4 (full).</summary>
    public static float MoonQuarters(GameTime t) => (float)(2 * (1 - Math.Cos(2 * Math.PI * MoonPhase(t))));

    /// <summary>Natural light on a clear night: starlight plus moonlight.</summary>
    public static float NightLight(GameTime t) => Starlight + MoonlightPerQuarter * MoonQuarters(t);

    /// <summary>
    /// Natural light outdoors. Night light until astronomical dawn (sun 18° below the horizon),
    /// rising to <see cref="Vision.LightLevels.AmbientMinimal"/> by civil dawn (-6°), to 40 at
    /// sunrise and to full daylight once the sun is 60° up; dusk mirrors it.
    /// </summary>
    public static float SunLight(GameTime t)
    {
        double alt = SunAltitude(t);
        float night = NightLight(t);
        if (alt <= -18)
            return night;
        if (alt <= -6)
            return Lerp(night, Math.Max(night, Vision.LightLevels.AmbientMinimal), (alt + 18) / 12);
        float civil = Math.Max(night, Vision.LightLevels.AmbientMinimal);
        if (alt <= 0)
            return Lerp(civil, 40, (alt + 6) / 6);
        return Lerp(40, DaylightLevel, Math.Min(alt / 60, 1));
    }

    private static float Lerp(float a, float b, double t) => a + (b - a) * (float)Math.Clamp(t, 0, 1);
}
