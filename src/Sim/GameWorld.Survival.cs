using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;

namespace Cataclysm3D.Sim;

/// <summary>How a sleep ended.</summary>
public enum SleepOutcome { Refused, CouldNotSleep, Rested, WokeUp, Interrupted }

/// <summary>
/// Survival: needs ticking with the clock, eating and drinking (CDDA's <c>E</c>), and sleeping
/// (bed or floor) with time passing until you're rested or something wakes you.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>A hostile monster this close that can see you wakes you up.</summary>
    public const int WakeRadius = 6;

    /// <summary>CDDA re-rolls falling asleep every half hour; we give up after this many tries.</summary>
    public const int SleepTries = 4;

    /// <summary><c>comfort</c> an item with CDDA's <c>SLEEP_AID</c> flag (a pillow, a blanket) adds to your spot.</summary>
    public const int SleepAidComfort = 4;

    private NeedStatus _lastThirst, _lastHunger, _lastSleepiness;

    /// <summary>
    /// Your speed: 100, less what your needs cost (thirst, hunger, a full belly) and, when you
    /// carry more than you can, 25 per extra capacity's worth (CDDA's <c>recalc_speed_bonus</c>).
    /// </summary>
    public void UpdateSpeed()
    {
        var p = Player;
        long cap = p.WeightCapacityMg, carried = p.Inventory.WeightMg;
        int overload = carried > cap ? (int)(25 * (carried - cap) / cap) : 0;
        p.Speed = Math.Max(25, Actor.BaseSpeed + p.Needs.SpeedPenalty - overload);
    }

    /// <summary>Runs the needs ticks due since the last turn and reports changes.</summary>
    private void UpdateNeeds()
    {
        var needs = Player.Needs;
        if (needs.Update(Scheduler.Turn, Player.Asleep) == 0 || Player.IsDead)
            return;
        UpdateSpeed();
        if (!Player.Asleep)
        {
            Announce(needs.ThirstStatus, ref _lastThirst, "thirsty", "You're dehydrated!", "Your mouth is parched.");
            Announce(needs.HungerStatus, ref _lastHunger, "hungry", "You're famished!", "You're starving!");
            Announce(needs.SleepinessStatus, ref _lastSleepiness, "tired", "You're dead tired.", "You're exhausted.");
        }

        // CDDA's check_needs_extremes.
        if (!Player.Asleep && needs.Sleepiness >= Needs.MassiveSleepiness)
        {
            Log("Survivor sleep now.");
            needs.Sleepiness -= 10;
            Player.Asleep = true;
        }
        if (needs.Thirst >= Needs.DeadlyThirst && needs.StomachWaterMl <= 0)
            Die("You die of dehydration...");
        else if (needs.StoredKcal <= 0)
            Die("You starve to death...");
    }

    private void Die(string message)
    {
        Player.Body.Damage(Player.Body.IndexOf("torso"), Player.Body.Hp("torso"));
        Player.IsDead = true;
        Log(message);
        _events.Add(new PlayerDiedEvent());
    }

    /// <summary>Logs when a need gets worse ("You're getting thirsty.").</summary>
    private void Announce(NeedStatus now, ref NeedStatus last, string word, string serious, string critical)
    {
        if (now.Severity > last.Severity)
            Log(now.Severity switch
            {
                NeedSeverity.Mild => $"You're getting {word}.",
                NeedSeverity.Serious => serious,
                _ => critical,
            });
        last = now;
    }

    // ---- eating and drinking ------------------------------------------------------------

    public string? WhyCantEat(Item item)
    {
        if (Busy() is { } busy)
            return busy;
        if (!InReach(item))
            return $"The {item.DisplayName} is out of reach.";
        if (item.Edible is not { } food)
            return item.Contents.Count == 0 && item.Type.IsContainer
                ? $"The {item.Type.Name} is empty."
                : $"You can't eat the {item.DisplayName}.";
        var c = food.Type.Comestible!;
        if (c.Kind == ComestibleKind.Med)
            return $"The {food.Type.Name} is medicine; medicine isn't in yet.";
        if (Player.Needs.Fullness + VolumePerCharge(food) / (double)Needs.StomachCapacityMl > 1)
            return IsDrink(food) ? "You're full. You can't drink any more." : "You're full. You can't eat another bite.";
        return null;
    }

    private static bool IsDrink(Item food) => food.Type.Comestible!.Kind == ComestibleKind.Drink || food.Type.IsLiquid;

    private static long VolumePerCharge(Item food) => food.Type.VolumeMl;

    /// <summary>
    /// CDDA's <c>get_consume_time</c>: a second per 5 ml of food, per 15 ml of drink, in moves.
    /// A serving of water takes 16 seconds.
    /// </summary>
    public static int ConsumeMoves(Item food) =>
        Math.Max(1, (int)(VolumePerCharge(food) / (IsDrink(food) ? 15 : 5))) * 100;

    /// <summary>
    /// Eats or drinks one serving (one charge) of an item, or of what's in it: its water, bulk and
    /// calories go into your stomach. The empty container stays.
    /// </summary>
    public bool Eat(Item item)
    {
        if (Refused(WhyCantEat(item)))
            return false;
        var food = item.Edible!;
        var c = food.Type.Comestible!;
        int cost = ConsumeMoves(food);
        Player.Needs.Consume(c.Calories, c.Quench, VolumePerCharge(food));
        food.Charges--;
        if (food.Charges <= 0)
            RemoveEverywhere(food);
        Player.Inventory.Touch();
        Log(IsDrink(food) ? $"You drink the {food.Type.Name}." : $"You eat the {food.Type.Name}.");
        Spend(cost);
        return true;
    }

    /// <summary>Removes an item from wherever it is: your inventory, a container in it, or the ground.</summary>
    private void RemoveEverywhere(Item item)
    {
        if (Player.Inventory.Remove(item))
            return;
        foreach (var held in Player.Worn.Append(Player.Wielded).Concat(Player.Inventory.CarriedItems).OfType<Item>())
            if (held.Contents.Remove(item))
                return;
        foreach (var g in ItemsInReach())
        {
            if (g.Item == item)
            {
                Map.Items.Remove(g.Pos, item);
                return;
            }
            if (g.Item.Contents.Remove(item))
            {
                Map.Items.Touch(g.Pos);
                return;
            }
        }
    }

    // ---- sleep ------------------------------------------------------------------------

    /// <summary>
    /// CDDA's <c>human_comfort_at</c> for your tile: the furniture's <c>comfort</c> (a bed is 5),
    /// else the terrain's less its move cost (a floor is -1, grass -2), plus 4 for a pillow or
    /// blanket lying there.
    /// </summary>
    public int ComfortHere()
    {
        var p = Player.Pos;
        var level = Map.Level(p.Z);
        int comfort = level.FurnitureAt(p.X, p.Y) is { } f ? f.Comfort : level.TerrainAt(p.X, p.Y).Comfort - level.MoveCost(p.X, p.Y);
        if (Map.Items.At(p).Any(i => i.Type.Has("SLEEP_AID")))
            comfort += SleepAidComfort;
        return comfort;
    }

    public string? WhyCantSleep()
    {
        if (Busy() is { } busy)
            return busy;
        if (_monsters.Any(m => !m.IsDead && m.Type.IsHostile && PlayerSees(m)))
            return "You can't sleep with monsters about!";
        if (Player.Needs.SleepScore(ComfortHere()) + 8 <= 0)
            return Player.Needs.Sleepiness < Needs.Tired ? "You aren't tired enough to sleep." : "You can't get comfortable enough to sleep here.";
        return null;
    }

    /// <summary>
    /// Lies down and tries to sleep, as CDDA's <c>try_sleep</c>: the fall-asleep roll is made now
    /// and every half hour. Asleep, you sleep until rested (shortly after sleepiness reaches 0),
    /// 10 hours at most, or until a monster comes close, you get hurt, or thirst wakes you. Time
    /// passes a turn at a time, so the world goes on.
    /// </summary>
    public SleepOutcome Sleep()
    {
        if (Refused(WhyCantSleep()))
            return SleepOutcome.Refused;
        var needs = Player.Needs;
        int comfort = ComfortHere();
        string spot = Map.Level(Player.Pos.Z).FurnitureAt(Player.Pos.X, Player.Pos.Y) is { Comfort: > 0 } bed ? $"the {bed.Name}" : "the ground";
        Log($"You lie down on {spot} to sleep.");
        int hp = TotalHp();
        long start = Scheduler.Turn, asleepAt = -1, wakeAt = long.MaxValue;
        int tries = 0;
        var outcome = SleepOutcome.Rested;
        for (long t = 0; !Player.IsDead; t++)
        {
            if (!Player.Asleep && t % 1800 == 0)
            {
                if (tries++ == SleepTries)
                {
                    outcome = SleepOutcome.CouldNotSleep;
                    Log("You can't fall asleep and get up.");
                    break;
                }
                if (needs.RollFallAsleep(comfort, Rng))
                {
                    Player.Asleep = true;
                    asleepAt = Scheduler.Turn;
                    Log("You fall asleep.");
                }
            }
            Scheduler.SubmitPlayerAction(100);
            if (TotalHp() < hp)
            {
                outcome = SleepOutcome.Interrupted;
                Log(Player.Asleep ? "You wake up in pain!" : "Something hurts you!");
                break;
            }
            if (MonsterNearby() is { } m)
            {
                outcome = SleepOutcome.Interrupted;
                Player.Asleep = false;
                UpdateSight();
                Log(PlayerSees(m) ? $"You wake up and see the {m.Type.Name}!" : "Something wakes you up!");
                break;
            }
            if (!Player.Asleep)
                continue;
            // Rested: CDDA wakes you dice(3, 100) turns after sleepiness bottoms out.
            if (needs.Sleepiness <= 0 && wakeAt == long.MaxValue)
                wakeAt = Scheduler.Turn + Rng.Next(1, 101) + Rng.Next(1, 101) + Rng.Next(1, 101);
            if (Scheduler.Turn >= wakeAt || Scheduler.Turn - asleepAt >= Needs.MaxSleepSeconds)
            {
                Log("You wake up.");
                break;
            }
            if (needs.Thirst > Needs.Dehydrated)
            {
                outcome = SleepOutcome.WokeUp;
                Log("You wake up, desperately thirsty.");
                break;
            }
        }
        Player.Asleep = false;
        UpdateSight();
        long slept = Scheduler.Turn - start;
        if (slept >= 60)
            Log($"({slept / 3600} h {slept / 60 % 60:00} min passed.)");
        return outcome;
    }

    /// <summary>A hostile monster within <see cref="WakeRadius"/> that has seen you, if any.</summary>
    private Monster? MonsterNearby() =>
        _monsters.FirstOrDefault(m => !m.IsDead && m.Type.IsHostile && m.Pos.Z == Player.Pos.Z
                                      && Math.Max(Math.Abs(m.Pos.X - Player.Pos.X), Math.Abs(m.Pos.Y - Player.Pos.Y)) <= WakeRadius
                                      && MonsterSees(m, Player));
}
