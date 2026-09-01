namespace Worklings.Core.Combat;

/// A named, timed modifier on a combatant — the shared primitive behind the
/// enemy abilities (Snag's Snare, Flicker's Blur/Phase, Monolith's Harden) and,
/// later, the per-class pet abilities. An effect is applied to a Combatant,
/// folded into its effective stats (which the resolver reads instead of the raw
/// block), and ticked down once per round until it expires.
public enum StatusEffectKind
{
    /// Lowers effective Agility — initiative, accuracy and crit all sag. Snare.
    AgilityDebuff,
    /// Raises effective Guard (mitigation). Monolith's Harden.
    GuardBuff,
    /// A flat extra chance to be missed, in percentage points. Flicker's Blur.
    Evasion,
    /// The next incoming attack misses outright, then this is consumed. Phase.
    Phasing,
}

/// One active effect: what it is, how strong, and how many more rounds it lasts.
///
/// Ported from Sources/CompanionCore/StatusEffect.swift. Kept as a struct with
/// value semantics to match the Swift original — combatants hold lists of these
/// and copy them between rounds.
public readonly struct StatusEffect : System.IEquatable<StatusEffect>
{
    public StatusEffectKind Kind { get; }
    public int Magnitude { get; }
    public int RemainingRounds { get; }

    /// A permanent effect never ages or expires — for passives like Flicker's
    /// Blur and Monolith's phase Harden that last the rest of the fight.
    public bool IsPermanent { get; }

    public StatusEffect(
        StatusEffectKind kind,
        int magnitude,
        int remainingRounds = 0,
        bool isPermanent = false)
    {
        Kind = kind;
        Magnitude = System.Math.Max(0, magnitude);
        RemainingRounds = System.Math.Max(0, remainingRounds);
        IsPermanent = isPermanent;
    }

    public bool IsExpired => !IsPermanent && RemainingRounds <= 0;

    /// Ages the effect by one round (permanent effects are untouched).
    /// Swift mutated in place; a readonly struct returns the aged copy instead,
    /// so callers must assign the result.
    public StatusEffect Ticked()
    {
        if (IsPermanent || RemainingRounds <= 0) return this;
        return new StatusEffect(Kind, Magnitude, RemainingRounds - 1, IsPermanent);
    }

    public bool Equals(StatusEffect other) =>
        Kind == other.Kind
        && Magnitude == other.Magnitude
        && RemainingRounds == other.RemainingRounds
        && IsPermanent == other.IsPermanent;

    public override bool Equals(object? obj) => obj is StatusEffect other && Equals(other);

    public override int GetHashCode() =>
        System.HashCode.Combine(Kind, Magnitude, RemainingRounds, IsPermanent);
}
