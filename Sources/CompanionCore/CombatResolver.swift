/// What a single Strike did, kept as data so the app can narrate it (a miss, a
/// hit, a crit) without re-deriving anything.
public struct StrikeOutcome: Equatable, Sendable {
    public let didHit: Bool
    public let didCrit: Bool
    public let damage: Int

    public init(didHit: Bool, didCrit: Bool, damage: Int) {
        self.didHit = didHit
        self.didCrit = didCrit
        self.damage = damage
    }

    public static let miss = StrikeOutcome(didHit: false, didCrit: false, damage: 0)
}

/// Resolves single combat actions against the seeded stream. Pure but for the
/// generator it threads through, so a fight replays identically from its seed.
///
/// The order of draws is fixed and documented: **hit, then crit, then the
/// damage swing.** Keeping that order stable is what makes a seeded fight
/// reproducible — a reordering would reshuffle every downstream roll.
public enum CombatResolver {
    /// Resolves one Strike from `attacker` against `defender`, mutating the
    /// defender's HP and returning what happened. `damageMultiplier` scales the
    /// final damage — the loop passes `braceMitigation` when the defender is
    /// Bracing, so a braced blow lands for less.
    public static func resolveStrike(
        attacker: CombatStats,
        defender: inout Combatant,
        rates: PetCombatRates,
        damageMultiplier: Double = 1,
        guaranteedHit: Bool = false,
        using generator: inout SeededGenerator
    ) -> StrikeOutcome {
        // A Phase slips the next blow entirely (Flicker), consuming the phase and
        // spending no roll.
        if defender.isPhasing {
            defender.consumePhasing()
            return .miss
        }

        // A guaranteed hit (Monolith's telegraphed Slam; later, Overbear) skips the
        // accuracy roll entirely, so it can't be dodged or evaded.
        if !guaranteedHit {
            let hitChance = rates.hitChance(
                attackerAgility: attacker.agility,
                defenderAgility: defender.effectiveStats.agility
            ) - defender.evasionChance
            guard generator.chance(hitChance) else {
                return .miss
            }
        }

        let didCrit = generator.chance(rates.critChance(agility: attacker.agility))

        let base = rates.strikeDamage(
            power: attacker.power,
            targetGuard: defender.effectiveStats.defense
        )
        let swing = Double.random(
            in: -rates.strikeVariance...rates.strikeVariance,
            using: &generator
        )
        var value = base * (1 + swing)
        if didCrit {
            value *= rates.critMultiplier
        }
        value *= max(0, damageMultiplier)
        let damage = max(1, Int(value.rounded()))

        defender.takeDamage(damage)
        return StrikeOutcome(didHit: true, didCrit: didCrit, damage: damage)
    }

    /// Resolves the once-per-encounter Signature: a guaranteed hit (no dodge, no
    /// crit) at `signatureMultiplier` damage. In v1 every class shares this; the
    /// per-class ability versions land later. Still draws its damage swing from
    /// the seeded stream so it stays reproducible.
    public static func resolveSignature(
        attacker: CombatStats,
        defender: inout Combatant,
        rates: PetCombatRates,
        using generator: inout SeededGenerator
    ) -> StrikeOutcome {
        let base = rates.strikeDamage(
            power: attacker.power,
            targetGuard: defender.effectiveStats.defense
        )
        let swing = Double.random(
            in: -rates.strikeVariance...rates.strikeVariance,
            using: &generator
        )
        let value = base * (1 + swing) * rates.signatureMultiplier
        let damage = max(1, Int(value.rounded()))
        defender.takeDamage(damage)
        return StrikeOutcome(didHit: true, didCrit: false, damage: damage)
    }
}
