namespace Cataclysm3D.Sim.Creatures;

/// <summary>How bad a need is, for coloring: fine, a warning, serious, critical.</summary>
public enum NeedSeverity { None, Mild, Serious, Critical }

/// <summary>One need as the sidebar shows it: "Thirsty", in a color.</summary>
public readonly record struct NeedStatus(string Text, NeedSeverity Severity);

/// <summary>
/// Hunger, thirst and sleepiness as CDDA 0.I keeps them (<c>Character::update_body</c>,
/// <c>update_stomach</c>, <c>update_needs</c>), updated every five minutes of game time:
/// <list type="bullet">
/// <item>Thirst rises by 1 a tick awake, half that asleep. What you drink sits in your stomach
/// and passes 250 ml a tick, taking a fifth of its milliliters off your thirst.</item>
/// <item>Your body burns its stored calories at its basal rate (1738 kcal a day for a new
/// survivor). Food goes to the stomach, then the guts, then storage, over half-hour steps.
/// Hunger is shown from how full your stomach is and when you last ate.</item>
/// <item>Sleepiness rises by 1 a tick awake and falls by about 2 a tick asleep.</item>
/// </list>
/// See docs/divergences.md for the simplifications.
/// </summary>
public sealed class Needs
{
    /// <summary>Seconds per needs tick; CDDA updates needs every 5 minutes.</summary>
    public const int TickSeconds = 300;

    /// <summary>A new survivor's fat store (<c>get_healthy_kcal</c>, 175 cm) and resting metabolism (<c>get_bmr</c>).</summary>
    public const int HealthyKcal = 118_153;
    public const int BmrKcalPerDay = 1_738;

    /// <summary><c>KCAL_PER_KG</c> and a new survivor's height, for CDDA's body-fat BMI.</summary>
    private const double KcalPerKg = 3500 * 2.20462;
    private const double HeightM = 1.75;

    /// <summary>The stomach's capacity and what a new survivor starts with in it and their guts.</summary>
    public const int StomachCapacityMl = 2_500;

    /// <summary>CDDA's thirst labels (<c>display::thirst_text_color</c>) and death.</summary>
    public const int Thirsty = 40, VeryThirsty = 80, Dehydrated = 240, Parched = 520, DeadlyThirst = 1_200;

    /// <summary>CDDA's <c>sleepiness_levels</c>: Tired, Dead Tired, Exhausted, and passing out.</summary>
    public const int Tired = 191, DeadTired = 383, Exhausted = 575, MassiveSleepiness = 1_000;

    /// <summary>Sleepiness paid back per tick asleep once sleep is deep (CDDA's recovery rate 1 + 1/(25 - 24)).</summary>
    public const int SleepRecovery = 2;

    /// <summary>The longest you sleep in one go (CDDA's <c>fall_asleep</c> gives 10 hours).</summary>
    public const int MaxSleepSeconds = 10 * 3600;

    private long _lastTick = -1, _ticks;
    private double _thirstRemainder, _sleepRemainder, _burnRemainder;

    public int Thirst { get; set; }

    /// <summary>CDDA 0.I's <c>sleepiness</c> (fatigue in older versions).</summary>
    public int Sleepiness { get; set; }

    public double StoredKcal { get; set; } = HealthyKcal;

    /// <summary>What's in your stomach: solid volume, water and calories, then calories in your guts.</summary>
    public double StomachMl { get; set; } = 475;
    public double StomachWaterMl { get; set; }
    public double StomachKcal { get; set; } = 800;
    public double GutsKcal { get; set; } = 300;

    /// <summary>Seconds since you last ate or drank; a new survivor has just eaten.</summary>
    public long SinceAte { get; set; }

    /// <summary>CDDA's fat BMI: stored fat over height squared. 5 is a healthy survivor.</summary>
    public double BmiFat => StoredKcal / KcalPerKg / (HeightM * HeightM);

    /// <summary>How full your stomach is, 0–1 and up.</summary>
    public double Fullness => (StomachMl + StomachWaterMl) / StomachCapacityMl;

    /// <summary>Thirst after what's already in your stomach (<c>get_instant_thirst</c>), which the sidebar shows.</summary>
    public int InstantThirst => Thirst - (int)(StomachWaterMl / 10);

    /// <summary>
    /// Catches the needs up to <paramref name="turn"/>: one update per five-minute boundary
    /// passed. Returns how many ticks ran.
    /// </summary>
    public int Update(long turn, bool asleep)
    {
        long tick = turn / TickSeconds;
        if (_lastTick < 0)
        {
            _lastTick = tick;
            return 0;
        }
        int ran = 0;
        for (; _lastTick < tick; _lastTick++, ran++)
            Tick(asleep);
        return ran;
    }

    /// <summary>One five-minute update.</summary>
    public void Tick(bool asleep)
    {
        _ticks++;
        SinceAte += TickSeconds;
        // Thirst: half as fast asleep. Water leaves the stomach 250 ml a tick.
        _thirstRemainder += asleep ? 0.5 : 1;
        Thirst += (int)_thirstRemainder;
        _thirstRemainder -= (int)_thirstRemainder;
        double water = Math.Min(250, StomachWaterMl);
        StomachWaterMl -= water;
        Thirst = Math.Max(-100, Thirst - (int)(water / 5));

        // Calories: the body burns its basal rate; every half hour the stomach passes a sixth of
        // its capacity and its calories to the guts, and the guts feed the body.
        _burnRemainder += BmrKcalPerDay / 288.0;
        double burn = Math.Floor(_burnRemainder);
        _burnRemainder -= burn;
        StoredKcal -= burn;
        if (_ticks % 6 == 0)
        {
            StomachMl = Math.Max(0, StomachMl - StomachCapacityMl / 6.0);
            double kcal = Math.Min(StomachKcal, Math.Max(5, StomachKcal / 6));
            StomachKcal -= kcal;
            GutsKcal += kcal;
            double absorbed = Math.Min(GutsKcal, Math.Max(BmrKcalPerDay / 24.0, GutsKcal * 0.05));
            GutsKcal -= absorbed;
            StoredKcal += absorbed;
        }

        // Sleepiness: up 1 a tick awake, down about 2 a tick asleep.
        if (asleep)
            Sleepiness = Math.Max(-1000, Sleepiness - SleepRecovery);
        else if (Sleepiness < 1050)
        {
            _sleepRemainder += 1;
            Sleepiness += (int)_sleepRemainder;
            _sleepRemainder -= (int)_sleepRemainder;
        }
    }

    /// <summary>
    /// Food or drink goes into the stomach: its water (quench × 5 ml), the rest of its volume, and
    /// its calories. Negative quench (salty food) takes effect at once, as in CDDA.
    /// </summary>
    public void Consume(int kcal, int quench, long volumeMl)
    {
        double water = quench * 5.0;
        if (water > 0)
            StomachWaterMl += water;
        else
            Thirst -= quench;
        StomachMl += Math.Max(0, volumeMl - Math.Max(0, water));
        StomachKcal += Math.Max(0, kcal);
        SinceAte = 0;
    }

    public NeedStatus ThirstStatus => InstantThirst switch
    {
        > Parched => new("Parched", NeedSeverity.Critical),
        > Dehydrated => new("Dehydrated", NeedSeverity.Serious),
        > VeryThirsty => new("Very thirsty", NeedSeverity.Mild),
        > Thirsty => new("Thirsty", NeedSeverity.Mild),
        < -60 => new("Turgid", NeedSeverity.None),
        < -20 => new("Hydrated", NeedSeverity.None),
        < 0 => new("Slaked", NeedSeverity.None),
        _ => new("", NeedSeverity.None),
    };

    /// <summary>Whether your body is short of calories (CDDA's <c>calorie_deficit</c>: fat BMI under 3).</summary>
    public bool CalorieDeficit => BmiFat < 3;

    /// <summary>
    /// CDDA 0.I's hunger label (<c>update_stomach</c>'s <c>hunger_*</c> effects): from how full
    /// your stomach is and how long ago you ate, and from your fat reserve once it runs low.
    /// </summary>
    public NeedStatus HungerStatus
    {
        get
        {
            bool justAte = SinceAte < 15 * 60, recentlyAte = SinceAte < 3 * 3600;
            double full = Fullness;
            if (!CalorieDeficit)
            {
                if (full >= 5.0 / 6) return new("Engorged", NeedSeverity.Mild);
                if (full >= 3.0 / 4) return new("Full", NeedSeverity.None);
                if (recentlyAte && full >= 3.0 / 8) return new("Satisfied", NeedSeverity.None);
                if (recentlyAte || StomachMl > 0) return new("", NeedSeverity.None);
                return BmiFat > 5 ? new("Peckish", NeedSeverity.None) : new("Hungry", NeedSeverity.Mild);
            }
            if (full >= 1) return new("Engorged", NeedSeverity.Mild);
            if (full >= 11.0 / 20) return new("Full", NeedSeverity.None);
            if (justAte && full > 0.5) return new("Satisfied", NeedSeverity.None);
            if (justAte) return new("Peckish", NeedSeverity.Mild);
            if (recentlyAte) return new("Hungry", NeedSeverity.Mild);
            return BmiFat < 2 ? new("Near starving", NeedSeverity.Critical) : new("Famished", NeedSeverity.Serious);
        }
    }

    public NeedStatus SleepinessStatus => Sleepiness switch
    {
        >= Exhausted => new("Exhausted", NeedSeverity.Critical),
        >= DeadTired => new("Dead Tired", NeedSeverity.Serious),
        >= Tired => new("Tired", NeedSeverity.Mild),
        _ => new("", NeedSeverity.None),
    };

    /// <summary>
    /// Speed lost to your needs (CDDA's <c>thirst_speed_penalty</c>, <c>kcal_speed_penalty</c>, and
    /// the Full / Engorged effects), as a negative number.
    /// </summary>
    public int SpeedPenalty
    {
        get
        {
            int penalty = 0;
            if (Thirst > Thirsty)
                penalty += (int)Lerp(Thirst, (40, 0), (300, -25), (600, -50), (1200, -75));
            if (StoredKcal <= HealthyKcal * 0.95)
                penalty += (int)Lerp(BmiFat, (0, -90), (1, -50), (2, -25), (3, 0));
            var hunger = HungerStatus.Text;
            penalty += hunger == "Engorged" ? -10 : hunger == "Full" ? -2 : 0;
            return penalty;
        }
    }

    /// <summary>Piecewise-linear interpolation over ascending points, clamped at the ends (CDDA's <c>multi_lerp</c>).</summary>
    private static double Lerp(double x, params (double X, double Y)[] points)
    {
        if (x <= points[0].X)
            return points[0].Y;
        for (int i = 1; i < points.Length; i++)
            if (x <= points[i].X)
                return points[i - 1].Y + (points[i].Y - points[i - 1].Y) * (x - points[i - 1].X) / (points[i].X - points[i - 1].X);
        return points[^1].Y;
    }

    /// <summary>
    /// CDDA's <c>can_sleep</c> roll: comfort, plus a term for how sleepy you are, plus
    /// <c>rng(-8, 8)</c>, must come out above 0. Checked when you lie down and every half hour after.
    /// </summary>
    public bool RollFallAsleep(int comfort, Random rng) => SleepScore(comfort) + rng.Next(-8, 9) > 0;

    /// <summary>The roll's fixed part. Below -8 you can't fall asleep at all.</summary>
    public int SleepScore(int comfort) =>
        comfort + (Sleepiness < Tired + 1 ? -(Tired + 1 - Sleepiness) / 4 : (Sleepiness - Tired + 1) / 16);
}
