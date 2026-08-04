/// A full delve: the chain of encounters that turns single fights into a
/// journey. It carries the pet's combat HP across encounters, regenerates a
/// little between them, accrues per-encounter XP, and — between fights — lets the
/// player **bank** (leave safely with what they've earned) or **push deeper**
/// toward the mini-boss (the press-your-luck beat). The exit tier and the
/// condition aftermath are computed **once**, from the HP the pet walks out with,
/// exactly as `docs/design/dungeons.md` specifies.
///
/// Like `CombatEncounter`, this is a pure, deterministic value type in
/// `CompanionCore`: the same pet, foes, and seed replay the same delve, so it is
/// fully checkable headless. The app drives it by reading `currentFoe` /
/// `makeEncounter(...)`, running that encounter (with animation), then handing the
/// result back via `recordOutcome(...)` and choosing `bank()` / `pushDeeper()`.
public struct Delve: Equatable, Sendable {
    /// Where the delve is right now.
    public enum Status: Equatable, Sendable {
        /// The opening briefing — not yet descended into the first fight.
        case briefing
        /// Currently fighting the encounter at `index`.
        case inEncounter
        /// Cleared a non-boss encounter; the player chooses bank vs push.
        case awaitingPushChoice
        /// Ended on a win — banked early, or the mini-boss went down.
        case completed(ExitTier)
        /// Ended on a loss — the pet was downed and retreated.
        case retreated
    }

    // MARK: Definition (fixed for the delve)

    /// The regular encounters, in order, then `boss` as the final one.
    public let foes: [Foe]
    public let boss: Foe
    private let petName: String
    private let petStats: CombatStats
    private let petMaxHP: Int
    /// The condition→combat multiplier, fixed at entry so mid-delve care changes
    /// don't retroactively alter a fight in progress.
    private let effectiveness: Double
    private let rates: PetCombatRates
    private let baseSeed: UInt64

    // MARK: Running state

    /// Which encounter is current: `0..<foes.count` are the regulars, `foes.count`
    /// is the boss.
    public private(set) var index: Int
    /// The pet's combat HP carried into the current encounter.
    public private(set) var carriedHP: Int
    /// Kill XP banked from cleared encounters so far (before any completion bonus).
    public private(set) var accumulatedXP: Double
    /// How many encounters have been cleared.
    public private(set) var clearedCount: Int
    public private(set) var status: Status

    // MARK: Init

    public init(
        pet: Combatant,
        foes: [Foe],
        boss: Foe,
        effectiveness: Double,
        rates: PetCombatRates,
        baseSeed: UInt64
    ) {
        self.foes = foes
        self.boss = boss
        self.petName = pet.name
        self.petStats = pet.stats
        self.petMaxHP = pet.maxHP
        self.effectiveness = min(max(effectiveness, 0), 1)
        self.rates = rates
        self.baseSeed = baseSeed
        self.index = 0
        self.carriedHP = pet.currentHP
        self.accumulatedXP = 0
        self.clearedCount = 0
        self.status = .briefing
    }

    /// The Cache Warren — the first dungeon's fixed chain — built from a pet
    /// combatant and its condition effectiveness. The one entry point the app and
    /// the checks both use.
    public static func cacheWarren(
        pet: Combatant,
        effectiveness: Double,
        rates: PetCombatRates,
        baseSeed: UInt64
    ) -> Delve {
        Delve(
            pet: pet, foes: CacheWarren.encounters, boss: CacheWarren.boss,
            effectiveness: effectiveness, rates: rates, baseSeed: baseSeed
        )
    }

    // MARK: Reading the current position

    /// Every encounter in order, regulars then boss.
    public var allFoes: [Foe] { foes + [boss] }

    /// The foe for the current index, or nil once the delve has ended.
    public var currentFoe: Foe? {
        index < allFoes.count ? allFoes[index] : nil
    }

    /// Whether the current encounter is the mini-boss (the last one).
    public var isBossEncounter: Bool { index == foes.count }

    /// A 1-based position for narration ("encounter 2 of 4").
    public var encounterNumber: Int { index + 1 }
    public var totalEncounters: Int { allFoes.count }

    /// Whether the delve has finished, either way.
    public var isFinished: Bool {
        switch status {
        case .completed, .retreated: return true
        case .briefing, .inEncounter, .awaitingPushChoice: return false
        }
    }

    // MARK: Driving the delve

    /// Leaves the briefing and begins the first encounter.
    public mutating func descend() {
        guard status == .briefing else { return }
        status = .inEncounter
    }

    /// Builds the `CombatEncounter` for the current index, starting the pet at its
    /// carried HP (a fresh combatant, so transient statuses from the last fight
    /// don't linger). Returns nil unless an encounter is actually current.
    public func makeEncounter(approach: Approach) -> CombatEncounter? {
        guard status == .inEncounter, let foe = currentFoe else { return nil }
        let pet = Combatant(
            name: petName, stats: petStats, maxHP: petMaxHP, currentHP: carriedHP
        )
        return CombatEncounter(
            pet: pet, foe: foe, approach: approach, rates: rates, seed: encounterSeed
        )
    }

    /// Records the result of the current encounter (which the caller ran to an
    /// ending). On a win, banks the foe's XP and either ends the delve (boss) or
    /// pauses for the bank/push choice (regular). On a loss, the delve retreats.
    public mutating func recordOutcome(petVictory: Bool, petHPRemaining: Int) {
        guard status == .inEncounter, let foe = currentFoe else { return }
        carriedHP = min(max(petHPRemaining, 0), petMaxHP)
        guard petVictory else {
            status = .retreated
            return
        }
        accumulatedXP += foe.rewardXP
        clearedCount += 1
        if isBossEncounter {
            status = .completed(currentExitTier(victory: true))
        } else {
            status = .awaitingPushChoice
        }
    }

    /// Convenience: record straight from a finished `CombatEncounter`.
    public mutating func recordOutcome(of encounter: CombatEncounter) {
        recordOutcome(
            petVictory: encounter.status == .petVictory,
            petHPRemaining: encounter.pet.currentHP
        )
    }

    /// Bank the run: leave safely with everything earned, forfeiting the boss's
    /// completion bonus. Only valid at the bank/push choice.
    public mutating func bank() {
        guard status == .awaitingPushChoice else { return }
        status = .completed(currentExitTier(victory: true))
    }

    /// Push deeper: regenerate a little HP and advance to the next encounter.
    public mutating func pushDeeper() {
        guard status == .awaitingPushChoice else { return }
        carriedHP = min(petMaxHP, carriedHP + interEncounterRegen)
        index += 1
        status = .inEncounter
    }

    // MARK: Resolving the finished delve

    /// The final result to write back, or nil while the delve is still running.
    /// The condition delta and any completion bonus are applied **here, once** —
    /// per-encounter fights never move the pet's needs.
    public func resolution(applyingTo state: PetState) -> DelveResolution? {
        let tier: ExitTier
        switch status {
        case .completed(let t): tier = t
        case .retreated: tier = .downed
        case .briefing, .inEncounter, .awaitingPushChoice: return nil
        }

        let bossDefeated = isCompletedThroughBoss
        let bonus = bossDefeated ? rates.delveCompletionXP : 0
        let totalXP = accumulatedXP + bonus

        // The boss is the capstone, so it's the only thing that drops gear: the
        // completion bonus and the drop together are what banking forfeits, which
        // is what gives the press-your-luck beat teeth. Restricting drops to items
        // not yet owned means a delve always either widens the loadout or doesn't
        // pretend to.
        let drop = bossDefeated ? Self.drop(excluding: state.ownedItems, seed: baseSeed) : nil

        let delta = rates.exitConditionDelta(for: tier)
        // Fullness rises as hunger falls, so a Fullness gain is a hunger cut —
        // the same conversion the single-encounter write-back uses.
        let updatedNeeds = PetNeeds(
            hunger: state.needs.hunger - delta.fullness,
            energy: state.needs.energy + delta.energy,
            happiness: state.needs.happiness + delta.happiness,
            trust: state.needs.trust + delta.trust
        )
        var updated = state.applying(needs: updatedNeeds, addingXP: totalXP)
        if let drop {
            updated = updated.acquiring(drop)
        }
        return DelveResolution(
            state: updated,
            tier: tier,
            xpGained: totalXP,
            clearedCount: clearedCount,
            bossDefeated: bossDefeated,
            banked: !bossDefeated && tier != .downed,
            itemDropped: drop
        )
    }

    /// Picks one item the pet doesn't already own, deterministically from the
    /// delve's seed — so a replayed delve awards the same thing, the same way
    /// every roll in it replays. Nil once the base set is complete; a real drop
    /// table (per-foe, per-delve, with rates) is content for later.
    private static func drop(excluding owned: [Item], seed: UInt64) -> Item? {
        let candidates = Item.allCases.filter { !owned.contains($0) }
        guard !candidates.isEmpty else { return nil }
        var generator = SeededGenerator(seed: seed)
        return candidates.randomElement(using: &generator)
    }

    // MARK: Internals

    /// The seed for the current encounter — the base seed decorrelated by index so
    /// the four fights don't share a stream, while the whole delve stays a pure
    /// function of `baseSeed`.
    private var encounterSeed: UInt64 {
        baseSeed &+ UInt64(index) &* 0x9E37_79B9_7F4A_7C15
    }

    /// The exit tier from the HP the pet currently holds (a win exit; a loss is
    /// always `.downed`, handled where the retreat is recorded).
    private func currentExitTier(victory: Bool) -> ExitTier {
        let fraction = petMaxHP > 0 ? Double(carriedHP) / Double(petMaxHP) : 0
        return ExitTier.forOutcome(victory: victory, hpFraction: fraction)
    }

    /// Whether the delve ended by clearing every encounter including the boss (as
    /// opposed to a voluntary early bank or a retreat).
    private var isCompletedThroughBoss: Bool {
        if case .completed = status {
            return clearedCount >= allFoes.count
        }
        return false
    }

    /// Flat HP restored between encounters: the doc's `fraction × maxHP ×
    /// effectiveness` — a rested, happy Workling recovers more mid-delve.
    private var interEncounterRegen: Int {
        Int((Double(petMaxHP) * rates.interEncounterRegenFraction * effectiveness).rounded())
    }
}

/// The write-back of a finished delve: the updated state (XP added, needs moved
/// **once**), the tier reached, and a little metadata for the end screen.
public struct DelveResolution: Equatable, Sendable {
    public let state: PetState
    public let tier: ExitTier
    /// Total XP granted, including the completion bonus when the boss fell.
    public let xpGained: Double
    public let clearedCount: Int
    /// The mini-boss was defeated — the full delve was completed.
    public let bossDefeated: Bool
    /// The player left voluntarily with a win (not a boss clear, not a retreat).
    public let banked: Bool
    /// The gear the boss gave up, already added to `state`. Nil when the boss
    /// wasn't beaten, or when there's nothing left in the base set to award.
    public let itemDropped: Item?

    public init(
        state: PetState,
        tier: ExitTier,
        xpGained: Double,
        clearedCount: Int,
        bossDefeated: Bool,
        banked: Bool,
        itemDropped: Item? = nil
    ) {
        self.state = state
        self.tier = tier
        self.xpGained = xpGained
        self.clearedCount = clearedCount
        self.bossDefeated = bossDefeated
        self.banked = banked
        self.itemDropped = itemDropped
    }
}
