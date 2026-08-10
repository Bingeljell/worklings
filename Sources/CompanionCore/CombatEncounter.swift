/// The standing strategy the pet fights on between decisions.
public enum Approach: Equatable, Hashable, Sendable, CaseIterable {
    /// Strike every round. No self-preservation, no held resources.
    case aggressive
    /// Brace while hurt, Strike once recovered. The thresholds are hysteretic —
    /// see `PetCombatRates.carefulResumeThreshold`.
    case careful
    /// Strike, holding the Signature until the foe is inside finishing range, then
    /// spending it unprompted.
    case clever

    /// One line on what this Approach actually does, so a surface never has to
    /// restate the rules and drift from them.
    public func summary(rates: PetCombatRates) -> String {
        func percent(_ fraction: Double) -> String { "\(Int((fraction * 100).rounded()))%" }
        switch self {
        case .aggressive:
            return "Strikes every round. No guard, no hedging."
        case .careful:
            return "Below \(percent(rates.carefulBraceThreshold)) HP, alternates Brace "
                + "and Strike — a braced round halves incoming damage and heals — "
                + "until back above \(percent(rates.carefulResumeThreshold))."
        case .clever:
            return "Strikes, holding the Signature until the foe is under "
                + "\(percent(rates.cleverFinisherThreshold)) HP, then unleashes it."
        }
    }
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
    case opening   // an evasive foe over-extended — the window to Unleash
    case telegraph // a heavy foe is winding up — Brace or eat it
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
    /// A grabber (Snag) seizes the pet instead of striking, Snaring its Agility.
    case grabbed(attacker: String, target: String, agilityLoss: Int)
    /// An evasive foe (Flicker) blurs aside — the pet's next blow will slip.
    case phased(who: String)
    /// A colossus (Monolith) winds up its Slam, telegraphed a turn ahead.
    case telegraphed(who: String)
    /// The wound-up Slam lands — a heavy, guaranteed hit.
    case slammed(attacker: String, defender: String, outcome: StrikeOutcome)
    /// A colossus Hardens at an HP phase, raising its Guard for the rest of the fight.
    case hardened(who: String, guardGain: Int)
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
    private let foeBehavior: FoeBehavior
    private var generator: SeededGenerator
    private var signatureAvailable: Bool
    private var pendingSignature: Bool
    private var promptedLowHP: Bool
    private var lastCadenceRound: Int
    /// Rounds remaining before a grabber (Snag) may Snare again.
    private var grabCooldownRemaining: Int
    /// Set when an evasive foe (Flicker) over-extends, so the next decision is the
    /// Unleash opening; cleared once that decision is taken.
    private var openingPending: Bool
    /// Rounds remaining before an evasive foe may Phase-and-open again.
    private var openingCooldownRemaining: Int
    /// Foe turns until a telegraphed Slam lands (0 = not winding up).
    private var slamCountdown: Int
    /// Set when a colossus telegraphs, so the next decision is the Brace-or-eat
    /// prompt; cleared once that decision is taken.
    private var slamTelegraphPending: Bool
    /// How many HP-phase Harden thresholds have already fired.
    private var hardenPhasesApplied: Int
    /// A one-shot guaranteed Brace queued from a telegraph decision.
    private var pendingBrace: Bool
    /// Whether a Careful pet is currently latched into Bracing. Held as state, not
    /// re-derived each round, because the threshold to *enter* the latch and the
    /// one to leave it deliberately differ.
    private var carefulBracing: Bool
    /// Whether the last Careful action inside the hurt band was a Brace, so the
    /// band alternates Brace/Strike rather than bracing forever.
    private var carefulBracedLastRound: Bool

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
        self.foeBehavior = foe.behavior
        self.generator = SeededGenerator(seed: seed)
        self.signatureAvailable = true
        self.pendingSignature = false
        self.promptedLowHP = false
        self.lastCadenceRound = 0
        self.grabCooldownRemaining = 0
        self.openingPending = false
        self.openingCooldownRemaining = 0
        self.slamCountdown = 0
        self.slamTelegraphPending = false
        self.hardenPhasesApplied = 0
        self.pendingBrace = false
        self.carefulBracing = false
        self.carefulBracedLastRound = false
        self.log = [.encounterBegan(pet: pet.name, foe: self.foe.name)]

        // Blur is a passive: an evasive foe carries its evasion for the whole
        // fight, on top of its native Agility.
        if case let .evasive(evasion, _, _) = foe.behavior {
            self.foe.apply(StatusEffect(kind: .evasion, magnitude: evasion, isPermanent: true))
        }
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
        case .opening: openingPending = false
        case .telegraph:
            slamTelegraphPending = false
            // Choosing Careful into a telegraph is a deliberate Brace against the
            // incoming Slam, not the usual hurt-only Brace.
            if approach == .careful, !unleash { pendingBrace = true }
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
        if slamTelegraphPending {
            return .telegraph
        }
        if openingPending {
            return .opening
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

        // Higher Agility acts first; the pet wins ties. Reads effective Agility so
        // a Snare (which sags initiative) actually costs the pet its turn order.
        let petFirst = pet.effectiveStats.agility >= foe.effectiveStats.agility
        if petFirst {
            performPet(petAction)
            if status == .ongoing { performFoe(petIsBracing: bracing) }
        } else {
            performFoe(petIsBracing: bracing)
            if status == .ongoing { performPet(petAction) }
        }
    }

    private mutating func chosenPetAction() -> CombatAction {
        if pendingBrace {
            pendingBrace = false
            return .brace
        }
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
            // Enter the hurt band when low, leave it only once genuinely recovered
            // — two thresholds, so it isn't a one-way door.
            if carefulBracing {
                carefulBracing = pet.hpFraction <= rates.carefulResumeThreshold
            } else {
                carefulBracing = pet.hpFraction < rates.carefulBraceThreshold
            }
            guard carefulBracing else {
                carefulBracedLastRound = false
                return .strike
            }
            // Inside the band, Brace and Strike *alternate*. Bracing every round
            // was the actual death spiral: against anything that outdamages the
            // regen the pet could neither heal out of the band nor hurt what was
            // holding it there, so the fight became unwinnable the moment it
            // dipped — and unwatchable, since the foe was the only one acting.
            carefulBracedLastRound.toggle()
            return carefulBracedLastRound ? .brace : .strike
        case .clever:
            // The held Signature, spent the moment the foe is inside finishing
            // range — the "chosen moment" the Approach is named for, chosen for you.
            if signatureAvailable, foe.hpFraction <= rates.cleverFinisherThreshold {
                return .signature
            }
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
            let regen = rates.braceRegenAmount(maxHP: pet.maxHP)
            pet.heal(regen)
            log.append(.braced(who: pet.name, regen: regen))
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
        // Dispatch on the foe's archetype. Each special behavior lands in its own
        // slice; until then every foe simply Strikes, exactly as before.
        switch foeBehavior {
        case .mindless:
            foeStrike(petIsBracing: petIsBracing)
        case let .colossus(slamMultiplier, telegraphRounds, hardenThresholds, hardenGuard):
            performColossus(
                slamMultiplier: slamMultiplier, telegraphRounds: telegraphRounds,
                hardenThresholds: hardenThresholds, hardenGuard: hardenGuard,
                petIsBracing: petIsBracing
            )
        case let .grabber(snareChance, snareMagnitude, snareDuration, grabCooldown):
            performGrab(
                snareChance: snareChance, snareMagnitude: snareMagnitude,
                snareDuration: snareDuration, grabCooldown: grabCooldown,
                petIsBracing: petIsBracing
            )
        case let .evasive(_, phaseChance, openingCooldown):
            performEvasive(
                phaseChance: phaseChance, openingCooldown: openingCooldown,
                petIsBracing: petIsBracing
            )
        }
        resolveDefeatIfAny()
    }

    /// An evasive foe (Flicker): it always darts in for chip damage, and — off
    /// cooldown — sometimes Phases, slipping the pet's next blow and over-extending
    /// into an Unleash opening. The opening only arms while the Signature is still
    /// in hand, since that's the whole point of the window.
    private mutating func performEvasive(
        phaseChance: Double, openingCooldown: Int, petIsBracing: Bool
    ) {
        foeStrike(petIsBracing: petIsBracing)
        if openingCooldownRemaining > 0 {
            openingCooldownRemaining -= 1
        } else if generator.chance(phaseChance) {
            foe.apply(StatusEffect(kind: .phasing, magnitude: 0, remainingRounds: 2))
            log.append(.phased(who: foe.name))
            if signatureAvailable { openingPending = true }
            openingCooldownRemaining = openingCooldown
        }
    }

    /// A grabber (Snag): off cooldown, it may seize the pet instead of striking,
    /// Snaring its Agility for a few rounds; otherwise it just attacks. The grab
    /// is spaced by a cooldown so it can't lock the pet down every turn.
    private mutating func performGrab(
        snareChance: Double, snareMagnitude: Int, snareDuration: Int,
        grabCooldown: Int, petIsBracing: Bool
    ) {
        if grabCooldownRemaining > 0 {
            grabCooldownRemaining -= 1
            foeStrike(petIsBracing: petIsBracing)
            return
        }
        if generator.chance(snareChance) {
            pet.apply(StatusEffect(
                kind: .agilityDebuff, magnitude: snareMagnitude, remainingRounds: snareDuration
            ))
            grabCooldownRemaining = grabCooldown
            log.append(.grabbed(attacker: foe.name, target: pet.name, agilityLoss: snareMagnitude))
        } else {
            foeStrike(petIsBracing: petIsBracing)
        }
    }

    /// A colossus (Monolith): slow but heavy. It Hardens as its HP crosses phase
    /// thresholds, and instead of ordinary attacks it winds up a telegraphed Slam
    /// one turn, then lands it — a guaranteed, doubled hit — the next.
    private mutating func performColossus(
        slamMultiplier: Double, telegraphRounds: Int,
        hardenThresholds: [Double], hardenGuard: Int, petIsBracing: Bool
    ) {
        applyHardenIfCrossed(thresholds: hardenThresholds, guardGain: hardenGuard)
        if slamCountdown > 0 {
            slamCountdown -= 1
            if slamCountdown == 0 {
                executeSlam(multiplier: slamMultiplier, petIsBracing: petIsBracing)
            }
            // Otherwise it is still winding up and does not attack this turn.
        } else {
            slamCountdown = max(1, telegraphRounds)
            slamTelegraphPending = true
            log.append(.telegraphed(who: foe.name))
        }
    }

    /// The wound-up Slam: a guaranteed hit at the Slam multiplier, halved if the
    /// pet Braced the blow.
    private mutating func executeSlam(multiplier: Double, petIsBracing: Bool) {
        let outcome = CombatResolver.resolveStrike(
            attacker: foe.effectiveStats, defender: &pet, rates: rates,
            damageMultiplier: multiplier * (petIsBracing ? rates.braceMitigation : 1),
            guaranteedHit: true, using: &generator
        )
        log.append(.slammed(attacker: foe.name, defender: pet.name, outcome: outcome))
    }

    /// Applies each Harden threshold once, in order, as the foe's HP drops past it
    /// — a single big hit can cross several at once.
    private mutating func applyHardenIfCrossed(thresholds: [Double], guardGain: Int) {
        while hardenPhasesApplied < thresholds.count,
              foe.hpFraction <= thresholds[hardenPhasesApplied] {
            foe.apply(StatusEffect(kind: .guardBuff, magnitude: guardGain, isPermanent: true))
            log.append(.hardened(who: foe.name, guardGain: guardGain))
            hardenPhasesApplied += 1
        }
    }

    /// The foe's plain attack — the baseline every archetype falls back to.
    private mutating func foeStrike(petIsBracing: Bool) {
        let outcome = CombatResolver.resolveStrike(
            attacker: foe.effectiveStats, defender: &pet, rates: rates,
            damageMultiplier: petIsBracing ? rates.braceMitigation : 1,
            using: &generator
        )
        log.append(.struck(attacker: foe.name, defender: pet.name, outcome: outcome))
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
