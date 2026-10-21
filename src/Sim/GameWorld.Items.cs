using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim;

/// <summary>An item you can reach on the ground: where it lies and what it is.</summary>
public readonly record struct GroundItem(TilePos Pos, Item Item);

/// <summary>
/// The player's item actions: picking up, dropping, wielding, wearing, taking off and stowing.
/// Each has a <c>WhyCant…</c> check that says in a sentence why it won't work (the UI uses it to
/// refuse drops), and the action itself, which spends CDDA's moves through the scheduler
/// (<c>item_location::obtain_cost</c>, <c>item_handling_cost</c>, <c>item_wear_cost</c>). You can
/// handle items on your tile and the eight around it, as CDDA's pick-up and advanced inventory
/// screens let you.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>CDDA's base handling costs: from your inventory, and off the map.</summary>
    public const int InventoryHandling = 100, MapHandling = 80;

    /// <summary>Extra moves per tile of distance when you take something off the map.</summary>
    public const int ReachCostPerTile = 100;

    /// <summary>Items on your tile and next to it, your own tile first.</summary>
    public List<GroundItem> ItemsInReach()
    {
        var list = new List<GroundItem>();
        foreach (var p in Reach())
            foreach (var item in Map.Items.At(p))
                list.Add(new GroundItem(p, item));
        return list;
    }

    /// <summary>Your tile, then the eight around it.</summary>
    public IEnumerable<TilePos> Reach()
    {
        var me = Player.Pos;
        yield return me;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if ((dx, dy) != (0, 0) && Map.Level(me.Z).InBounds(me.X + dx, me.Y + dy))
                yield return me with { X = me.X + dx, Y = me.Y + dy };
    }

    /// <summary>Where an item you can reach lies, or null if it's not on the ground within reach.</summary>
    public TilePos? GroundPosOf(Item item)
    {
        foreach (var p in Reach())
            if (Map.Items.At(p).Contains(item))
                return p;
        return null;
    }

    private bool InReach(Item item) => Player.Inventory.Has(item) || GroundPosOf(item) is not null;

    private string? Busy() =>
        Player.IsDead ? "You're dead." : !Scheduler.PlayerCanAct ? "You're busy." : null;

    /// <summary>
    /// Moves to get hold of an item where it is now: off the map (more for the next tile over),
    /// out of a pocket, or nothing extra if it's already in your hands.
    /// </summary>
    public int ObtainCost(Item item)
    {
        var inv = Player.Inventory;
        if (GroundPosOf(item) is { } at)
            return inv.HandlingCost(item, MapHandling) + ReachCostPerTile * Math.Max(Math.Abs(at.X - Player.Pos.X), Math.Abs(at.Y - Player.Pos.Y));
        if (inv.ContainerOf(item) is { } pocket)
            return inv.HandlingCost(item, pocket.Type.Pockets.Select(p => p.Moves).DefaultIfEmpty(InventoryHandling).Max());
        if (Player.Worn.Contains(item))
            return inv.TakeOffCost(item);
        return inv.HandlingCost(item, 0, bulk: false);
    }

    /// <summary>CDDA's <c>cost_to_move_item</c>: setting something down, more if your hands are full or you're overloaded.</summary>
    private int PutDownCost() =>
        (int)Math.Min(400, 50 + (Player.Wielded is null ? 0 : 20)
                           + Math.Max(0, (Player.Inventory.WeightMg - Player.WeightCapacityMg) / 100_000));

    /// <summary>CDDA's <c>item_store_cost</c>: putting something into a pocket.</summary>
    private int StoreCost(Item item) => Player.Inventory.HandlingCost(item, InventoryHandling);

    // ---- pick up and drop ---------------------------------------------------------------

    public string? WhyCantPickUp(Item item)
    {
        if (Busy() is { } busy)
            return busy;
        if (Player.Inventory.Has(item))
            return $"You already have the {item.DisplayName}.";
        if (GroundPosOf(item) is null)
            return $"The {item.DisplayName} is out of reach.";
        return Player.Inventory.WhyCantStow(item);
    }

    /// <summary>Puts an item from the ground into your pockets (CDDA's <c>g</c>).</summary>
    public bool PickUp(Item item)
    {
        if (Refused(WhyCantPickUp(item)))
            return false;
        int cost = ObtainCost(item);
        Map.Items.Remove(GroundPosOf(item)!.Value, item);
        var pocket = Player.Inventory.Stow(item)!;
        Log($"You pick up the {item.DisplayName} and put it in your {pocket.Type.Name}.{OverloadWarning()}");
        Spend(cost);
        return true;
    }

    /// <summary>
    /// Picks up everything on your tile that fits (CDDA's <c>g</c> with one pile); returns how
    /// many items you took. After the first, items from the same pile only cost their bulk.
    /// </summary>
    public int PickUpAllHere()
    {
        if (Refused(Busy()))
            return 0;
        var here = Map.Items.At(Player.Pos).ToList();
        if (here.Count == 0)
        {
            Log("There's nothing here to pick up.");
            return 0;
        }
        int taken = 0, cost = 0;
        Item? first = null;
        string? lastReason = null;
        foreach (var item in here)
        {
            if (Player.Inventory.WhyCantStow(item) is { } reason)
            {
                lastReason = reason;
                continue;
            }
            cost += first is null ? ObtainCost(item) : Math.Max(1, (int)Math.Min(200, item.VolumeMl / 20));
            Map.Items.Remove(Player.Pos, item);
            Player.Inventory.Stow(item);
            first ??= item;
            taken++;
        }
        if (taken > 0)
        {
            Log(taken == 1 ? $"You pick up the {first!.DisplayName}.{OverloadWarning()}" : $"You pick up {taken} items.{OverloadWarning()}");
            Spend(cost);
        }
        if (taken < here.Count && lastReason is not null)
            Log(taken == 0 ? lastReason : $"Some things don't fit. {lastReason}");
        return taken;
    }

    public string? WhyCantDrop(Item item, TilePos? at = null)
    {
        if (Busy() is { } busy)
            return busy;
        if (!Player.Inventory.Has(item))
            return $"You don't have the {item.DisplayName}.";
        var to = at ?? Player.Pos;
        if (!Reach().Contains(to))
            return "That's too far away.";
        if (to != Player.Pos && Map.Level(to.Z).MoveCost(to.X, to.Y) <= 0 && Map.Level(to.Z).FurnitureAt(to.X, to.Y) is null)
            return $"You can't put things in the {Map.TerrainAt(to).Name}.";
        return null;
    }

    /// <summary>Drops an item (from your pockets, hands or body) on your tile or next to it (CDDA's <c>d</c>).</summary>
    public bool Drop(Item item, TilePos? at = null)
    {
        if (Refused(WhyCantDrop(item, at)))
            return false;
        bool worn = Player.Worn.Contains(item);
        int cost = ObtainCost(item) + PutDownCost();
        Player.Inventory.Remove(item);
        Map.Items.Add(at ?? Player.Pos, item);
        Log(worn ? $"You take off the {item.DisplayName} and drop it." : $"You drop the {item.DisplayName}.");
        Spend(cost);
        return true;
    }

    // ---- wield -------------------------------------------------------------------------

    public string? WhyCantWield(Item item)
    {
        if (Busy() is { } busy)
            return busy;
        if (!InReach(item))
            return $"The {item.DisplayName} is out of reach.";
        if (Player.Worn.Contains(item))
            return $"Take off the {item.Type.Name} first.";
        return Player.Inventory.WhyCantWield(item);
    }

    /// <summary>
    /// Takes an item in your hands (CDDA's <c>w</c>) from your pockets or the ground. What you
    /// held goes into a pocket, or onto the ground if there's no room.
    /// </summary>
    public bool Wield(Item item)
    {
        if (Refused(WhyCantWield(item)))
            return false;
        int cost = ObtainCost(item);
        if (GroundPosOf(item) is { } from)
            Map.Items.Remove(from, item);
        Player.Inventory.Remove(item);
        string put = "";
        if (Player.Inventory.Unwield() is { } old)
        {
            cost += PutAway(old, out put);
            put = " " + put;
        }
        Player.Inventory.Wield(item);
        Log($"You wield the {item.DisplayName}.{put}{OverloadWarning()}");
        Spend(cost);
        return true;
    }

    public string? WhyCantUnwield() =>
        Busy() ?? (Player.Wielded is null ? "You aren't holding anything." : null);

    /// <summary>Puts away what you hold: into a pocket if it fits, else on the ground.</summary>
    public bool Unwield()
    {
        if (Refused(WhyCantUnwield()))
            return false;
        var held = Player.Inventory.Unwield()!;
        int cost = PutAway(held, out var what);
        Log(what);
        Spend(cost);
        return true;
    }

    /// <summary>Stows an item you just let go of, or drops it if no pocket has room. Returns the moves.</summary>
    private int PutAway(Item item, out string message)
    {
        if (Player.Inventory.Stow(item) is { } pocket)
        {
            message = $"You put the {item.DisplayName} in your {pocket.Type.Name}.";
            return StoreCost(item);
        }
        Map.Items.Add(Player.Pos, item);
        message = $"You have no room for the {item.DisplayName} and drop it.";
        return PutDownCost();
    }

    // ---- wear and take off --------------------------------------------------------------

    public string? WhyCantWear(Item item)
    {
        if (Busy() is { } busy)
            return busy;
        if (!InReach(item))
            return $"The {item.DisplayName} is out of reach.";
        return Player.Inventory.WhyCantWear(item);
    }

    /// <summary>Puts on an item from your pockets, hands or the ground (CDDA's <c>W</c>).</summary>
    public bool Wear(Item item)
    {
        if (Refused(WhyCantWear(item)))
            return false;
        // Getting it out of a pocket or off the floor, then putting it on.
        int cost = (Player.Wielded == item ? 0 : ObtainCost(item)) + Player.Inventory.WearCost(item);
        if (GroundPosOf(item) is { } from)
            Map.Items.Remove(from, item);
        Player.Inventory.Wear(item);
        Log($"You put on the {item.DisplayName}.{OverloadWarning()}");
        Spend(cost);
        return true;
    }

    public string? WhyCantTakeOff(Item item) =>
        Busy() ?? (Player.Worn.Contains(item) ? null : $"You aren't wearing the {item.DisplayName}.");

    /// <summary>
    /// Takes off a worn item (CDDA's <c>T</c>). It goes into a pocket if one has room, else into
    /// your hands if they're empty, else onto the ground. A bag keeps what's in it.
    /// </summary>
    public bool TakeOff(Item item)
    {
        if (Refused(WhyCantTakeOff(item)))
            return false;
        int cost = Player.Inventory.TakeOffCost(item);
        Player.Inventory.TakeOff(item);
        if (Player.Inventory.Stow(item) is { } pocket)
        {
            Log($"You take off the {item.DisplayName} and put it in your {pocket.Type.Name}.");
        }
        else if (Player.Wielded is null)
        {
            Player.Inventory.Wield(item);
            Log($"You take off the {item.DisplayName} and hold it.");
        }
        else
        {
            Map.Items.Add(Player.Pos, item);
            cost += PutDownCost();
            Log($"You take off the {item.DisplayName}. You have no room for it and drop it.");
        }
        Spend(cost);
        return true;
    }

    // ---- stow -------------------------------------------------------------------------

    /// <summary>Why an item can't go into your pockets from the ground, your hands or your body.</summary>
    public string? WhyCantStow(Item item)
    {
        if (Busy() is { } busy)
            return busy;
        if (Player.Worn.Contains(item))
            return WhyCantTakeOff(item);
        if (Player.Inventory.ContainerOf(item) is not null)
            return $"The {item.DisplayName} is already in your pockets.";
        if (!InReach(item))
            return $"The {item.DisplayName} is out of reach.";
        return Player.Inventory.WhyCantStow(item);
    }

    /// <summary>Puts an item into your pockets: picks it up, takes it off, or puts away what you hold.</summary>
    public bool Stow(Item item)
    {
        if (Player.Worn.Contains(item))
            return TakeOff(item);
        if (GroundPosOf(item) is not null)
            return PickUp(item);
        if (Refused(WhyCantStow(item)))
            return false;
        Player.Inventory.Remove(item);
        var pocket = Player.Inventory.Stow(item)!;
        Log($"You put the {item.DisplayName} in your {pocket.Type.Name}.");
        Spend(StoreCost(item));
        return true;
    }

    // ---- helpers ----------------------------------------------------------------------

    /// <summary>A note added to messages when you carry more than you comfortably can.</summary>
    private string OverloadWarning() =>
        Player.Inventory.WeightMg > Player.WeightCapacityMg ? " You're carrying too much!" : "";

    private bool Refused(string? reason)
    {
        if (reason is null)
            return false;
        Log(reason);
        return true;
    }

    /// <summary>Spends the player's moves, then lets the world run.</summary>
    private void Spend(int moves)
    {
        UpdateSpeed();
        Scheduler.SubmitPlayerAction(Math.Max(1, moves));
    }
}
