/// A foe's turn logic, as data. Each case names an archetype and carries its
/// held knobs (from `docs/design/dungeons.md`); the encounter dispatches on it.
/// Every case except `.mindless` also just Strikes until its own slice lands, so
/// adding a case never changes an existing fight.
public enum FoeBehavior: Equatable, Sendable {
    /// Attacks every turn, no tactics. The Dungeon Scamp — teaches the loop.
    case mindless
    /// Mostly attacks; sometimes grabs to Snare (an Agility debuff). Snag.
    case grabber(snareChance: Double, snareMagnitude: Int, snareDuration: Int, grabCooldown: Int)
    /// Passive Blur (evasion) plus an occasional Phase, then over-extends into an
    /// Unleash opening. Flicker.
    case evasive(evasion: Int, phaseChance: Double, openingCooldown: Int)
    /// Slow; telegraphs a heavy Slam a turn ahead and Hardens (Guard) at HP-phase
    /// thresholds. Monolith.
    case colossus(slamMultiplier: Double, telegraphRounds: Int, hardenThresholds: [Double], hardenGuard: Int)
}

/// A foe's authored stat block, turn logic, and kill reward. Data, not code —
/// the resolver reads the numbers and the encounter dispatches on `behavior`.
/// Numbers are the held defaults from `docs/design/dungeons.md`.
public struct Foe: Equatable, Sendable {
    public let name: String
    public let maxHP: Int
    public let stats: CombatStats
    /// How this foe acts on its turn.
    public let behavior: FoeBehavior
    /// XP granted for defeating this foe.
    public let rewardXP: Double

    public init(
        name: String,
        maxHP: Int,
        stats: CombatStats,
        behavior: FoeBehavior = .mindless,
        rewardXP: Double
    ) {
        self.name = name
        self.maxHP = maxHP
        self.stats = stats
        self.behavior = behavior
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
        name: "Dungeon Scamp", maxHP: 30,
        stats: CombatStats(power: 4, defense: 1, agility: 6, wit: 1),
        behavior: .mindless,
        rewardXP: 8
    )
    public static let snag = Foe(
        name: "Snag", maxHP: 30,
        stats: CombatStats(power: 7, defense: 6, agility: 3, wit: 3),
        behavior: .grabber(snareChance: 0.4, snareMagnitude: 3, snareDuration: 2, grabCooldown: 2),
        rewardXP: 20
    )
    public static let flicker = Foe(
        name: "Flicker", maxHP: 18,
        stats: CombatStats(power: 6, defense: 2, agility: 14, wit: 4),
        behavior: .evasive(evasion: 30, phaseChance: 0.35, openingCooldown: 3),
        rewardXP: 25
    )
    public static let monolith = Foe(
        name: "Monolith", maxHP: 90,
        stats: CombatStats(power: 12, defense: 12, agility: 2, wit: 2),
        behavior: .colossus(slamMultiplier: 2.0, telegraphRounds: 1, hardenThresholds: [0.66, 0.33], hardenGuard: 4),
        rewardXP: 100
    )

    /// The three regular encounters, in order, then the mini-boss.
    public static let encounters = [mote, snag, flicker]
    public static let boss = monolith
}
