namespace Worklings.Core.Combat;

/// What a single Strike did, kept as data so the app can narrate it (a miss, a
/// hit, a crit) without re-deriving anything.
public readonly struct StrikeOutcome : System.IEquatable<StrikeOutcome>
{
    public bool DidHit { get; }
    public bool DidCrit { get; }
    public int Damage { get; }

    public StrikeOutcome(bool didHit, bool didCrit, int damage)
    {
        DidHit = didHit;
        DidCrit = didCrit;
        Damage = damage;
    }

    public static readonly StrikeOutcome Miss = new(false, false, 0);

    public bool Equals(StrikeOutcome other) =>
        DidHit == other.DidHit && DidCrit == other.DidCrit && Damage == other.Damage;

    public override bool Equals(object? obj) => obj is StrikeOutcome o && Equals(o);
    public override int GetHashCode() => System.HashCode.Combine(DidHit, DidCrit, Damage);
    public override string ToString() =>
        DidHit ? $"{(DidCrit ? "crit" : "hit")} {Damage}" : "miss";
}

/// Resolves single combat actions against the seeded stream. Pure but for the
/// generator it threads through, so a fight replays identically from its seed.
///
/// The order of draws is fixed and documented: hit, then crit, then the damage
/// swing. Keeping that order stable is what makes a seeded fight reproducible —
/// a reordering would reshuffle every downstream roll.
///
/// Ported from Sources/CompanionCore/CombatResolver.swift. The generator is
/// passed by `ref` to mirror Swift's `inout`: SeededGenerator is a struct, so
/// passing it by value would silently give every call its own private stream.
public static class CombatResolver
{
    /// Resolves one Strike from `attacker` against `defender`, mutating the
    /// defender's HP and returning what happened. `damageMultiplier` scales the
    /// final damage — the loop passes BraceMitigation when the defender is
    /// Bracing, so a braced blow lands for less.
    public static StrikeOutcome ResolveStrike(
        CombatStats attacker,
        Combatant defender,
        PetCombatRates rates,
        ref SeededGenerator generator,
        double damageMultiplier = 1,
        bool guaranteedHit = false)
    {
        // A Phase slips the next blow entirely (Flicker), consuming the phase
        // and spending no roll.
        if (defender.IsPhasing)
        {
            defender.ConsumePhasing();
            return StrikeOutcome.Miss;
        }

        // A guaranteed hit (Monolith's telegraphed Slam) skips the accuracy roll
        // entirely, so it cannot be dodged or evaded.
        if (!guaranteedHit)
        {
            double hitChance = rates.HitChance(
                attacker.Agility,
                defender.EffectiveStats.Agility) - defender.EvasionChance;
            if (!generator.Chance(hitChance)) return StrikeOutcome.Miss;
        }

        bool didCrit = generator.Chance(rates.CritChance(attacker.Agility));

        double base_ = rates.StrikeDamage(attacker.Power, defender.EffectiveStats.Defense);
        double swing = generator.NextDoubleClosed(-rates.StrikeVariance, rates.StrikeVariance);
        double value = base_ * (1 + swing);
        if (didCrit) value *= rates.CritMultiplier;
        value *= System.Math.Max(0, damageMultiplier);
        int damage = System.Math.Max(
            1, (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero));

        defender.TakeDamage(damage);
        return new StrikeOutcome(true, didCrit, damage);
    }

    /// Resolves the once-per-encounter Signature: a guaranteed hit (no dodge, no
    /// crit) at SignatureMultiplier damage. In v1 every class shares this; the
    /// per-class ability versions land later. Still draws its damage swing from
    /// the seeded stream so it stays reproducible.
    public static StrikeOutcome ResolveSignature(
        CombatStats attacker,
        Combatant defender,
        PetCombatRates rates,
        ref SeededGenerator generator)
    {
        double base_ = rates.StrikeDamage(attacker.Power, defender.EffectiveStats.Defense);
        double swing = generator.NextDoubleClosed(-rates.StrikeVariance, rates.StrikeVariance);
        double value = base_ * (1 + swing) * rates.SignatureMultiplier;
        int damage = System.Math.Max(
            1, (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero));
        defender.TakeDamage(damage);
        return new StrikeOutcome(true, false, damage);
    }
}
