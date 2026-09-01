namespace Worklings.Core.Combat;

/// The four stats a combatant actually fights with. Vitality is folded into
/// MaxHP at build time and Wit stays mostly latent until abilities, so a
/// Combatant carries only what the resolver reads each turn.
///
/// Ported from Sources/CompanionCore/Combat.swift.
public readonly struct CombatStats : System.IEquatable<CombatStats>
{
    public int Power { get; }

    /// The mitigation stat. Named Defense internally to avoid Swift's `guard`
    /// keyword, exactly as PetStats does; the design vocabulary calls it Guard.
    /// Kept under the same name here so the two implementations read alike.
    public int Defense { get; }

    public int Agility { get; }
    public int Wit { get; }

    public CombatStats(int power, int defense, int agility, int wit)
    {
        Power = power;
        Defense = defense;
        Agility = agility;
        Wit = wit;
    }

    public bool Equals(CombatStats other) =>
        Power == other.Power && Defense == other.Defense
        && Agility == other.Agility && Wit == other.Wit;

    public override bool Equals(object? obj) => obj is CombatStats o && Equals(o);
    public override int GetHashCode() => System.HashCode.Combine(Power, Defense, Agility, Wit);
}
