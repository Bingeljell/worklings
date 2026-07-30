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
    /// defender's HP and returning what happened.
    public static func resolveStrike(
        attacker: CombatStats,
        defender: inout Combatant,
        rates: PetCombatRates,
        using generator: inout SeededGenerator
    ) -> StrikeOutcome {
        let hitChance = rates.hitChance(
            attackerAgility: attacker.agility,
            defenderAgility: defender.stats.agility
        )
        guard generator.chance(hitChance) else {
            return .miss
        }

        let didCrit = generator.chance(rates.critChance(agility: attacker.agility))

        let base = rates.strikeDamage(
            power: attacker.power,
            targetGuard: defender.stats.defense
        )
        let swing = Double.random(
            in: -rates.strikeVariance...rates.strikeVariance,
            using: &generator
        )
        var value = base * (1 + swing)
        if didCrit {
            value *= rates.critMultiplier
        }
        let damage = max(1, Int(value.rounded()))

        defender.takeDamage(damage)
        return StrikeOutcome(didHit: true, didCrit: didCrit, damage: damage)
    }
}
