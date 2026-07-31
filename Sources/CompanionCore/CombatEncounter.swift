/// The standing strategy the pet fights on between decisions.
public enum Approach: Equatable, Hashable, Sendable, CaseIterable {
    case aggressive  // bias Strike
    case careful     // Brace when hurt, else Strike
    case clever      // Strike, holding the Signature for a chosen moment
}

/// One thing the pet can do on its turn.
public enum CombatAction: Equatable, Sendable {
    case strike
    case brace
    case signature
}

/// Why the fight paused for input.
public enum DecisionReason: Equatable, Sendable {
    case cadence   // the every-few-rounds reassess beat
    case lowHP     // the pet is faltering
}

/// Where the encounter is right now.
public enum CombatStatus: Equatable, Sendable {
    case ongoing
    case awaitingDecision(DecisionReason)
    case petVictory
    case petDefeat
}

/// A structured record of what happened, one entry at a time, so the app can
/// narrate and animate each beat (map a strike to the Strike/Hurt poses, a
/// defeat to Victory/Downed) without re-deriving anything.
public enum CombatEvent: Equatable, Sendable {
    case encounterBegan(pet: String, foe: String)
    case roundBegan(Int)
    case struck(attacker: String, defender: String, outcome: StrikeOutcome)
    case signature(attacker: String, defender: String, outcome: StrikeOutcome)
    case braced(who: String, regen: Int)
    case defeated(who: String)
    case decisionPoint(DecisionReason)
    case encounterEnded(victory: Bool)
}

/// A single encounter: the pet versus one foe, resolved round by round against
/// the seeded stream. Deterministic — the same seed and inputs replay the same
/// fight — so it lives in `CompanionCore` and is fully checkable.
///
/// Drive it by calling `step()` until `status` is a decision or an ending. On a
/// decision, call `decide(...)`; on an ending, read `pet.hpFraction` for the
/// delve's exit tier. `runToCompletion()` is the headless convenience.
public struct CombatEncounter: Equatable, Sendable {
    public private(set) var pet: Combatant
    public private(set) var foe: Combatant
    public private(set) var approach: Approach
    public private(set) var round: Int
    public private(set) var status: CombatStatus
    public private(set) var log: [CombatEvent]

    private let rates: PetCombatRates
    private var generator: SeededGenerator
    private var signatureAvailable: Bool
    private var pendingSignature: Bool
    private var promptedLowHP: Bool
    private var lastCadenceRound: Int

    public init(
        pet: Combatant,
        foe: Foe,
        approach: Approach,
        rates: PetCombatRates,
        seed: UInt64
    ) {
        self.pet = pet
        self.foe = foe.makeCombatant()
        self.approach = approach
        self.round = 0
        self.status = .ongoing
        self.rates = rates
        self.generator = SeededGenerator(seed: seed)
        self.signatureAvailable = true
        self.pendingSignature = false
        self.promptedLowHP = false
        self.lastCadenceRound = 0
        self.log = [.encounterBegan(pet: pet.name, foe: self.foe.name)]
    }

    /// Whether the pet still has its once-per-encounter Signature.
    public var signatureReady: Bool { signatureAvailable }

    /// Advances the fight by one unit: either pausing for a decision, or
    /// resolving a full round (both combatants act, in initiative order). A
    /// no-op once the fight is awaiting a decision or over.
    public mutating func step() {
        guard status == .ongoing else { return }
        if let reason = pendingDecision() {
            status = .awaitingDecision(reason)
            log.append(.decisionPoint(reason))
            return
        }
        resolveRound()
    }

    /// Resolves a pending decision: adopt an Approach, and optionally Unleash the
    /// Signature on the next round. A no-op unless a decision is pending.
    public mutating func decide(approach: Approach, unleash: Bool) {
        guard case .awaitingDecision(let reason) = status else { return }
        self.approach = approach
        if unleash, signatureAvailable {
            pendingSignature = true
        }
        switch reason {
        case .lowHP: promptedLowHP = true
        case .cadence: lastCadenceRound = round
        }
        status = .ongoing
    }

    /// Runs the fight to an ending without further input, keeping the current
    /// Approach at every decision. For headless use and tests.
    public mutating func runToCompletion(maxRounds: Int = 200) {
        var safety = 0
        let limit = maxRounds * 4
        while safety < limit {
            switch status {
            case .ongoing:
                step()
            case .awaitingDecision:
                decide(approach: approach, unleash: false)
            case .petVictory, .petDefeat:
                return
            }
            safety += 1
        }
    }

    // MARK: - Internals

    private func pendingDecision() -> DecisionReason? {
        if !promptedLowHP, pet.hpFraction < rates.lowHPEventThreshold {
            return .lowHP
        }
        if round > 0,
           round % rates.decisionCadenceRounds == 0,
           lastCadenceRound != round {
            return .cadence
        }
        return nil
    }

    private mutating func resolveRound() {
        round += 1
        log.append(.roundBegan(round))

        // Age any timed effects (Snare, Blur, Phase, Harden) at the top of the
        // round, before anyone acts, and drop the expired ones.
        pet.tickStatuses()
        foe.tickStatuses()

        let petAction = chosenPetAction()
        let bracing = petAction == .brace

        // Higher Agility acts first; the pet wins ties.
        let petFirst = pet.stats.agility >= foe.stats.agility
        if petFirst {
            performPet(petAction)
            if status == .ongoing { performFoe(petIsBracing: bracing) }
        } else {
            performFoe(petIsBracing: bracing)
            if status == .ongoing { performPet(petAction) }
        }
    }

    private mutating func chosenPetAction() -> CombatAction {
        if pendingSignature {
            pendingSignature = false
            if signatureAvailable {
                return .signature
            }
        }
        switch approach {
        case .aggressive:
            return .strike
        case .careful:
            return pet.hpFraction < rates.carefulBraceThreshold ? .brace : .strike
        case .clever:
            return .strike
        }
    }

    private mutating func performPet(_ action: CombatAction) {
        switch action {
        case .strike:
            let outcome = CombatResolver.resolveStrike(
                attacker: pet.effectiveStats, defender: &foe, rates: rates, using: &generator
            )
            log.append(.struck(attacker: pet.name, defender: foe.name, outcome: outcome))
        case .brace:
            pet.heal(rates.braceRegen)
            log.append(.braced(who: pet.name, regen: rates.braceRegen))
        case .signature:
            signatureAvailable = false
            let outcome = CombatResolver.resolveSignature(
                attacker: pet.effectiveStats, defender: &foe, rates: rates, using: &generator
            )
            log.append(.signature(attacker: pet.name, defender: foe.name, outcome: outcome))
        }
        resolveDefeatIfAny()
    }

    private mutating func performFoe(petIsBracing: Bool) {
        let outcome = CombatResolver.resolveStrike(
            attacker: foe.effectiveStats, defender: &pet, rates: rates,
            damageMultiplier: petIsBracing ? rates.braceMitigation : 1,
            using: &generator
        )
        log.append(.struck(attacker: foe.name, defender: pet.name, outcome: outcome))
        resolveDefeatIfAny()
    }

    private mutating func resolveDefeatIfAny() {
        guard status == .ongoing else { return }
        if foe.isDefeated {
            log.append(.defeated(who: foe.name))
            log.append(.encounterEnded(victory: true))
            status = .petVictory
        } else if pet.isDefeated {
            log.append(.defeated(who: pet.name))
            log.append(.encounterEnded(victory: false))
            status = .petDefeat
        }
    }
}
