using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Vision;

namespace Cataclysm3D.Tests;

public class ClockTests
{
    [Fact]
    public void GameStartsOnASpringMorning()
    {
        var t = GameTime.GameStart;
        Assert.Equal(Season.Spring, t.Season);
        Assert.Equal(1, t.DayOfSeason);
        Assert.Equal(1, t.Year);
        Assert.Equal(8, t.Hour);
        Assert.Equal("Spring, day 1, 08:00", t.ToString());
    }

    [Fact]
    public void OneTurnIsOneSecond()
    {
        var sim = new TurnScheduler(new PlayerActor());
        Assert.Equal(GameTime.GameStart, sim.Now);
        sim.SubmitPlayerAction(100 * 90);
        Assert.Equal(GameTime.GameStart.Plus(90), sim.Now);
        Assert.Equal(1, sim.Now.Minute - GameTime.GameStart.Minute);
    }

    [Fact]
    public void DaysSeasonsAndYearsRollOver()
    {
        Assert.Equal(2, GameTime.At(0, 23, 59).Plus(60).DayOfSeason);
        Assert.Equal(Season.Summer, GameTime.At(91, 0).Season);
        Assert.Equal(1, GameTime.At(91, 0).DayOfSeason);
        Assert.Equal(Season.Winter, GameTime.At(363, 12).Season);
        Assert.Equal(2, GameTime.At(364, 0).Year);
        Assert.Equal("Autumn, day 5, 17:30", GameTime.At(186, 17, 30).ToString());
    }

    [Fact]
    public void SunRisesAroundSixAndSetsAroundSixAtTheSpringEquinox()
    {
        var rise = Celestial.Sunrise(GameTime.GameStart);
        var set = Celestial.Sunset(GameTime.GameStart);
        Assert.InRange(rise.HourOfDay, 5.75, 6.25);
        Assert.InRange(set.HourOfDay, 17.75, 18.25);
        Assert.InRange(Celestial.SunAltitude(rise), -1, 1);
    }

    [Fact]
    public void SummerDaysAreLongerThanWinterDays()
    {
        var summer = GameTime.At(91 + 45, 12);
        var winter = GameTime.At(273 + 45, 12);
        Assert.True(Celestial.Sunrise(summer).HourOfDay < 5);
        Assert.True(Celestial.Sunset(summer).HourOfDay > 19);
        Assert.True(Celestial.Sunrise(winter).HourOfDay > 7);
        Assert.True(Celestial.Sunset(winter).HourOfDay < 17);
    }

    [Fact]
    public void SunPeaksInTheSouthAtNoon()
    {
        var noon = GameTime.At(0, 12);
        Assert.InRange(Celestial.SunAltitude(noon), 90 - Celestial.Latitude - 1, 90 - Celestial.Latitude + 1);
        Assert.InRange(Celestial.SunAzimuth(noon), 179, 181);
        Assert.InRange(Celestial.SunAzimuth(GameTime.At(0, 7)), 60, 120); // morning sun in the east
        Assert.InRange(Celestial.SunAzimuth(GameTime.At(0, 17)), 240, 300); // evening sun in the west
    }

    [Fact]
    public void DaylightIsBrightAndNightIsTooDarkWithoutTheMoon()
    {
        var noon = GameTime.At(0, 12);
        Assert.True(Celestial.SunLight(noon) > LightLevels.AmbientLit);
        Assert.InRange(Celestial.SunLight(GameTime.At(0, 13)), 50, Celestial.DaylightLevel);

        // The calendar starts on a half moon: starlight plus two quarters.
        var midnight = GameTime.At(0, 0);
        Assert.Equal(Celestial.NightLight(midnight), Celestial.SunLight(midnight));
        Assert.InRange(Celestial.SunLight(midnight), 5, 6);

        // Three weeks on, a new moon is too dark to see by.
        var newMoon = GameTime.At(22, 0);
        Assert.True(Celestial.MoonQuarters(newMoon) < 0.1f);
        Assert.True(Celestial.SunLight(newMoon) < LightLevels.AmbientLow);

        // A full moon a week after the start lights the night.
        var fullMoon = GameTime.At(7, 0);
        Assert.True(Celestial.MoonQuarters(fullMoon) > 3.9f);
        Assert.True(Celestial.SunLight(fullMoon) > 9.5f);
    }

    [Fact]
    public void LightRisesThroughDawnAndFallsThroughDusk()
    {
        float last = 0;
        for (int minute = 3 * 60; minute <= 11 * 60; minute += 15)
        {
            float light = Celestial.SunLight(GameTime.At(1, 0).Plus(minute * 60));
            Assert.True(light >= last, $"light fell at {minute / 60}:{minute % 60:00}");
            last = light;
        }
        last = float.MaxValue;
        for (int minute = 13 * 60; minute <= 22 * 60; minute += 15)
        {
            float light = Celestial.SunLight(GameTime.At(1, 0).Plus(minute * 60));
            // The waxing moon brightens the night a little every hour; ignore that.
            Assert.True(light <= last + 0.1f, $"light rose at {minute / 60}:{minute % 60:00}");
            last = light;
        }
        Assert.InRange(Celestial.SunLight(Celestial.Sunrise(GameTime.At(1, 0))), 38, 42);
    }
}
