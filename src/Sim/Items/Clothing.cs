namespace Cataclysm3D.Sim.Items;

/// <summary>Which layer a worn item sits on, innermost first (CDDA's SKINTIGHT, normal, BELTED).</summary>
public enum WornLayer { Skintight, Normal, Belted }

/// <summary>
/// A piece of clothing you have on. Only its look and name for now: armor, encumbrance and
/// warmth come with items in M5.
/// </summary>
public sealed record Clothing(string Id, string Name, WornLayer Layer);
