/// A foe's authored stat block plus its kill reward. Data, not behaviour — the
/// resolver reads these numbers; a foe's turn logic and named abilities (Snare,
/// Blur, Slam) land in a later slice. Numbers are the held defaults from
/// `docs/design/dungeons.md`; they'll move to a data file if the bestiary grows.
public struct Foe: Equatable, Sendable {
    public let name: String
    public let maxHP: Int
    public let stats: CombatStats
    /// XP granted for defeating this foe.
    public let rewardXP: Double

    public init(name: String, maxHP: Int, stats: CombatStats, rewardXP: Double) {
        self.name = name
        self.maxHP = maxHP
        self.stats = stats
        self.rewardXP = max(rewardXP, 0)
    }

    /// A fresh combatant at full HP for this foe.
    public func makeCombatant() -> Combatant {
        Combatant.foe(name: name, maxHP: maxHP, stats: stats)
    }
}

/// The first delve's bestiary. A deliberate mechanic curve — a warm-up, a wall,
/// an accuracy test, then an endurance check — though v1 foes all just attack
/// until their abilities are built.
public enum CacheWarren {
    public static let mote = Foe(
        name: "Mote", maxHP: 30,
        stats: CombatStats(power: 4, defense: 1, agility: 6, wit: 1),
        rewardXP: 8
    )
    public static let snag = Foe(
        name: "Snag", maxHP: 30,
        stats: CombatStats(power: 7, defense: 6, agility: 3, wit: 3),
        rewardXP: 20
    )
    public static let flicker = Foe(
        name: "Flicker", maxHP: 18,
        stats: CombatStats(power: 6, defense: 2, agility: 14, wit: 4),
        rewardXP: 25
    )
    public static let monolith = Foe(
        name: "Monolith", maxHP: 90,
        stats: CombatStats(power: 12, defense: 12, agility: 2, wit: 2),
        rewardXP: 100
    )

    /// The three regular encounters, in order, then the mini-boss.
    public static let encounters = [mote, snag, flicker]
    public static let boss = monolith
}
