namespace Cataclysm3D.Sim.Creatures;

/// <summary>
/// Your hair: one of CDDA's hair style traits (<c>hair_medium</c>, …) and its color variant
/// (<c>brown</c>, …). Only looks, as in CDDA.
/// </summary>
public sealed record Hair(string Style, string Color);
