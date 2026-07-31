/// A named, timed modifier on a combatant — the shared primitive behind the
/// enemy abilities (Snag's Snare, Flicker's Blur/Phase, Monolith's Harden) and,
/// later, the per-class pet abilities. An effect is applied to a `Combatant`,
/// folded into its `effectiveStats` (which the resolver reads instead of the raw
/// block), and ticked down once per round until it expires.
public enum StatusEffectKind: Equatable, Sendable {
    /// Lowers effective Agility — initiative, accuracy, and crit all sag. Snare.
    case agilityDebuff
    /// Raises effective Guard (mitigation). Monolith's Harden.
    case guardBuff
    /// A flat extra chance to be missed, in percentage points. Flicker's Blur.
    case evasion
    /// The next incoming attack misses outright, then this is consumed. Phase.
    case phasing
}

/// One active effect: what it is, how strong, and how many more rounds it lasts.
public struct StatusEffect: Equatable, Sendable {
    public let kind: StatusEffectKind
    public let magnitude: Int
    public private(set) var remainingRounds: Int
    /// A permanent effect never ages or expires — for passives like Flicker's Blur
    /// and Monolith's phase Harden that last the rest of the fight.
    public let isPermanent: Bool

    public init(
        kind: StatusEffectKind,
        magnitude: Int,
        remainingRounds: Int = 0,
        isPermanent: Bool = false
    ) {
        self.kind = kind
        self.magnitude = max(0, magnitude)
        self.remainingRounds = max(0, remainingRounds)
        self.isPermanent = isPermanent
    }

    public var isExpired: Bool { isPermanent ? false : remainingRounds <= 0 }

    /// Ages the effect by one round (permanent effects are untouched).
    mutating func tick() {
        guard !isPermanent, remainingRounds > 0 else { return }
        remainingRounds -= 1
    }
}
