import CompanionCore
import Foundation

enum CombatChecks {
    static func run(context: inout CheckContext) {
        checkDefaultRates(context: &context)
        checkMaxHPFromVitality(context: &context)
        checkStrikeDamageAndFloor(context: &context)
        checkHitChanceAndClamp(context: &context)
        checkCritChanceAndClamp(context: &context)
        checkSeededGeneratorReplays(context: &context)
        checkSeededGeneratorDivergesBySeed(context: &context)
        checkChanceBoundsAndDistribution(context: &context)
        checkFullConditionPetKeepsFullStats(context: &context)
        checkNeglectScalesStatsAndHP(context: &context)
        checkFoeIgnoresCondition(context: &context)
        checkCombatantDamageAndHealClamp(context: &context)
        checkStrikeIsDeterministicForASeed(context: &context)
        checkMissDealsNoDamageHitDealsSome(context: &context)
        checkStrikeDamageStaysInBounds(context: &context)
        checkStrikeHitRateApproximatesTheFormula(context: &context)
        checkBraceMitigationReducesDamage(context: &context)
        checkSignatureAlwaysHitsAndHitsHarder(context: &context)
        checkBestiaryMatchesTheSpec(context: &context)
        checkEncounterReplaysIdentically(context: &context)
        checkAggressivePetBeatsMote(context: &context)
        checkOutmatchedPetIsDefeated(context: &context)
        checkFasterCombatantActsFirst(context: &context)
        checkDecisionPointAndUnleashConsumeSignature(context: &context)
        checkExitTierDerivation(context: &context)
        checkVictoryGrantsXPAndMovesConditionsByTier(context: &context)
        checkDefeatIsDownedWithNoXP(context: &context)
        checkOutcomeStaysInsideTheReversibleEnvelope(context: &context)
        checkPetCombatantBuildsFromState(context: &context)
        checkDelveEntryEligibility(context: &context)
    }

    private static func midHealthPet() -> PetState {
        PetState.newPet(now: Date(timeIntervalSinceReferenceDate: 0))
            .applying(needs: PetNeeds(hunger: 50, energy: 50, happiness: 50, trust: 50))
    }

    private static func aegisPet(_ rates: PetCombatRates) -> Combatant {
        Combatant.pet(name: "Pixel", baseStats: aegisStats, needs: fullHealth, rates: rates)
    }

    private static func firstStruckAttacker(in log: [CombatEvent]) -> String? {
        for event in log {
            if case let .struck(attacker, _, _) = event { return attacker }
        }
        return nil
    }

    private static func logContainsSignature(_ log: [CombatEvent]) -> Bool {
        log.contains { if case .signature = $0 { return true } else { return false } }
    }

    private static func logContainsDecision(_ log: [CombatEvent]) -> Bool {
        log.contains { if case .decisionPoint = $0 { return true } else { return false } }
    }

    private static let flickerStats = CombatStats(
        power: 6, defense: 2, agility: 14, wit: 4
    )
    private static func freshFlicker() -> Combatant {
        Combatant.foe(name: "Flicker", maxHP: 18, stats: flickerStats)
    }

    // A Level-3 Aegis: Guard 11 (signature), the rest at 7.
    private static let aegisStats = PetStats(
        vitality: 7, power: 7, defense: 11, agility: 7, wit: 7
    )
    private static let fullHealth = PetNeeds(
        hunger: 0, energy: 100, happiness: 100, trust: 100
    )

    // The worked Flicker example in docs/design/dungeons.md is the reference:
    // a Level-3 Aegis (Vitality/Power/Agility/Wit 7, Guard 11) versus a Flicker
    // (Guard 2, Agility 14). The numbers below trace that fight.

    private static func checkDefaultRates(context: inout CheckContext) {
        let rates = PetCombatRates()
        context.expectApproximatelyEqual(rates.baseHP, 20, "default baseHP")
        context.expectApproximatelyEqual(rates.vitalityToHP, 3, "default vitalityToHP")
        context.expectApproximatelyEqual(rates.powerScale, 1.5, "default powerScale")
        context.expectApproximatelyEqual(rates.guardScale, 1, "default guardScale")
        context.expectApproximatelyEqual(rates.baseHitChance, 0.75, "default baseHitChance")
        context.expectApproximatelyEqual(rates.critMultiplier, 1.5, "default critMultiplier")
    }

    private static func checkMaxHPFromVitality(context: inout CheckContext) {
        let rates = PetCombatRates()
        // Off-stat Vitality 7 → 20 + 21 = 41 (the worked Aegis).
        context.expectEqual(rates.maxHP(vitality: 7), 41, "maxHP at Vitality 7")
        // Signature Vitality 11 (a Level-3 Wellspring) → 20 + 33 = 53.
        context.expectEqual(rates.maxHP(vitality: 11), 53, "maxHP at Vitality 11")
    }

    private static func checkStrikeDamageAndFloor(context: inout CheckContext) {
        let rates = PetCombatRates()
        // Aegis Power 7 into Flicker Guard 2 → 10.5 − 2 = 8.5.
        context.expectApproximatelyEqual(
            rates.strikeDamage(power: 7, targetGuard: 2), 8.5,
            "strike damage, Power 7 vs Guard 2"
        )
        // Flicker Power 6 into Aegis Guard 11 → 9 − 11 < 1, floored to 1.
        context.expectApproximatelyEqual(
            rates.strikeDamage(power: 6, targetGuard: 11), 1,
            "strike damage floors at 1 against heavy Guard"
        )
    }

    private static func checkHitChanceAndClamp(context: inout CheckContext) {
        let rates = PetCombatRates()
        // Aegis Agility 7 vs Flicker Agility 14 → 0.75 + (−7)(0.03) = 0.54.
        context.expectApproximatelyEqual(
            rates.hitChance(attackerAgility: 7, defenderAgility: 14), 0.54,
            "hit chance, Agility 7 vs 14"
        )
        // Maverick Agility 11 vs Flicker 14 → 0.75 + (−3)(0.03) = 0.66.
        context.expectApproximatelyEqual(
            rates.hitChance(attackerAgility: 11, defenderAgility: 14), 0.66,
            "hit chance, Agility 11 vs 14"
        )
        // A huge Agility deficit clamps to the floor, not below.
        context.expectApproximatelyEqual(
            rates.hitChance(attackerAgility: 1, defenderAgility: 99), rates.hitChanceFloor,
            "hit chance clamps to the floor"
        )
        // A huge Agility surplus clamps to the ceiling, never a sure thing.
        context.expectApproximatelyEqual(
            rates.hitChance(attackerAgility: 99, defenderAgility: 1), rates.hitChanceCeiling,
            "hit chance clamps to the ceiling"
        )
    }

    private static func checkCritChanceAndClamp(context: inout CheckContext) {
        let rates = PetCombatRates()
        // Agility 7 → 0.07.
        context.expectApproximatelyEqual(
            rates.critChance(agility: 7), 0.07, "crit chance at Agility 7"
        )
        // Absurd Agility clamps to a certainty rather than exceeding 1.
        context.expectApproximatelyEqual(
            rates.critChance(agility: 500), 1, "crit chance clamps to 1"
        )
    }

    private static func checkSeededGeneratorReplays(context: inout CheckContext) {
        var a = SeededGenerator(seed: 42)
        var b = SeededGenerator(seed: 42)
        let sequenceA = (0..<16).map { _ in a.next() }
        let sequenceB = (0..<16).map { _ in b.next() }
        context.expectEqual(
            sequenceA, sequenceB,
            "the same seed replays the same sequence"
        )
    }

    private static func checkSeededGeneratorDivergesBySeed(context: inout CheckContext) {
        var a = SeededGenerator(seed: 1)
        var b = SeededGenerator(seed: 2)
        let sequenceA = (0..<16).map { _ in a.next() }
        let sequenceB = (0..<16).map { _ in b.next() }
        context.expect(
            sequenceA != sequenceB,
            "different seeds produce different sequences"
        )
    }

    private static func checkChanceBoundsAndDistribution(context: inout CheckContext) {
        var generator = SeededGenerator(seed: 7)
        // The extremes are certainties and never consume differently-shaped luck.
        context.expect(
            (0..<100).allSatisfy { _ in generator.chance(1) },
            "chance(1) always succeeds"
        )
        context.expect(
            (0..<100).allSatisfy { _ in !generator.chance(0) },
            "chance(0) never succeeds"
        )
        // A fair coin over many draws lands near half — deterministic under the
        // fixed seed, so this bound is stable, not flaky.
        var coin = SeededGenerator(seed: 12_345)
        let successes = (0..<1000).reduce(into: 0) { total, _ in
            if coin.chance(0.5) { total += 1 }
        }
        context.expect(
            (400...600).contains(successes),
            "chance(0.5) lands near half over many draws (got \(successes)/1000)"
        )
    }

    private static func checkFullConditionPetKeepsFullStats(context: inout CheckContext) {
        let rates = PetCombatRates()
        let pet = Combatant.pet(
            name: "Pixel", baseStats: aegisStats, needs: fullHealth, rates: rates
        )
        // Effectiveness 1.0 at full condition: stats and HP match the sheet.
        context.expectEqual(pet.stats.power, 7, "full-condition Power unscaled")
        context.expectEqual(pet.stats.defense, 11, "full-condition Guard unscaled")
        context.expectEqual(pet.maxHP, 41, "full-condition maxHP matches Vitality 7")
        context.expectEqual(pet.currentHP, 41, "pet starts at full HP")
    }

    private static func checkNeglectScalesStatsAndHP(context: inout CheckContext) {
        let rates = PetCombatRates()
        // All needs at 0 → effectiveness clamps to the 0.5 combat floor.
        let neglected = PetNeeds(hunger: 100, energy: 0, happiness: 0, trust: 0)
        context.expectApproximatelyEqual(
            rates.combatEffectiveness(needs: neglected), 0.5,
            "neglect clamps effectiveness to the combat floor"
        )
        let pet = Combatant.pet(
            name: "Pixel", baseStats: aegisStats, needs: neglected, rates: rates
        )
        // Stats halve (rounded): Power 7→4 (3.5 rounds to 4), Guard 11→6 (5.5→6),
        // and maxHP derives from the scaled Vitality 7→4 (3.5→4): 20 + 12 = 32.
        context.expectEqual(pet.stats.power, 4, "neglected Power scales down")
        context.expectEqual(pet.stats.defense, 6, "neglected Guard scales down")
        context.expectEqual(pet.maxHP, 32, "neglected maxHP scales down")
    }

    private static func checkFoeIgnoresCondition(context: inout CheckContext) {
        // A foe is built from its raw block; there is no condition to apply.
        let flicker = Combatant.foe(
            name: "Flicker", maxHP: 18,
            stats: CombatStats(power: 6, defense: 2, agility: 14, wit: 4)
        )
        context.expectEqual(flicker.maxHP, 18, "foe HP is authored directly")
        context.expectEqual(flicker.stats.agility, 14, "foe stats are raw")
    }

    private static func checkCombatantDamageAndHealClamp(context: inout CheckContext) {
        var c = Combatant.foe(
            name: "Mote", maxHP: 8,
            stats: CombatStats(power: 3, defense: 0, agility: 5, wit: 1)
        )
        c.takeDamage(3)
        context.expectEqual(c.currentHP, 5, "damage subtracts")
        context.expectApproximatelyEqual(c.hpFraction, 5.0 / 8.0, "hpFraction tracks HP")
        c.heal(100)
        context.expectEqual(c.currentHP, 8, "heal clamps at maxHP")
        c.takeDamage(999)
        context.expectEqual(c.currentHP, 0, "damage clamps at 0")
        context.expect(c.isDefeated, "0 HP is defeated")
    }

    private static func checkStrikeIsDeterministicForASeed(context: inout CheckContext) {
        let rates = PetCombatRates()
        let attacker = CombatStats(power: 7, defense: 11, agility: 7, wit: 7) // Aegis

        func run() -> [StrikeOutcome] {
            var generator = SeededGenerator(seed: 99)
            var target = freshFlicker()
            return (0..<12).map { _ in
                CombatResolver.resolveStrike(
                    attacker: attacker, defender: &target, rates: rates, using: &generator
                )
            }
        }
        context.expectEqual(run(), run(), "a seeded strike sequence replays identically")
    }

    private static func checkMissDealsNoDamageHitDealsSome(context: inout CheckContext) {
        let rates = PetCombatRates()
        let attacker = CombatStats(power: 7, defense: 11, agility: 7, wit: 7)
        var generator = SeededGenerator(seed: 3)
        var target = freshFlicker()
        var sawMiss = false
        var sawHit = false
        for _ in 0..<200 where !(sawMiss && sawHit) {
            let before = target.currentHP
            let outcome = CombatResolver.resolveStrike(
                attacker: attacker, defender: &target, rates: rates, using: &generator
            )
            if outcome.didHit {
                sawHit = true
                context.expect(outcome.damage >= 1, "a hit deals at least 1")
                context.expect(target.currentHP < before || before == 0, "a hit lowers HP")
            } else {
                sawMiss = true
                context.expectEqual(outcome.damage, 0, "a miss deals 0")
                context.expectEqual(target.currentHP, before, "a miss leaves HP unchanged")
            }
            if target.isDefeated { target = freshFlicker() }
        }
        context.expect(sawMiss && sawHit, "both a hit and a miss occur over many strikes")
    }

    private static func checkStrikeDamageStaysInBounds(context: inout CheckContext) {
        let rates = PetCombatRates()
        // Juggernaut Power 11 vs Flicker Guard 2: base 14.5.
        let attacker = CombatStats(power: 11, defense: 7, agility: 7, wit: 7)
        let base = rates.strikeDamage(power: 11, targetGuard: 2) // 14.5
        let maxNonCrit = Int((base * (1 + rates.strikeVariance)).rounded())
        let maxCrit = Int((base * (1 + rates.strikeVariance) * rates.critMultiplier).rounded())
        var generator = SeededGenerator(seed: 55)
        for _ in 0..<300 {
            var target = freshFlicker()
            let outcome = CombatResolver.resolveStrike(
                attacker: attacker, defender: &target, rates: rates, using: &generator
            )
            guard outcome.didHit else { continue }
            let ceiling = outcome.didCrit ? maxCrit : maxNonCrit
            context.expect(
                outcome.damage >= 1 && outcome.damage <= ceiling,
                "strike damage \(outcome.damage) within [1, \(ceiling)] (crit \(outcome.didCrit))"
            )
        }
    }

    private static func checkStrikeHitRateApproximatesTheFormula(context: inout CheckContext) {
        let rates = PetCombatRates()
        // Aegis Agility 7 vs Flicker 14 → 0.54 expected hit rate.
        let attacker = CombatStats(power: 7, defense: 11, agility: 7, wit: 7)
        var generator = SeededGenerator(seed: 2_024)
        var hits = 0
        let trials = 2000
        for _ in 0..<trials {
            var target = freshFlicker()
            if CombatResolver.resolveStrike(
                attacker: attacker, defender: &target, rates: rates, using: &generator
            ).didHit { hits += 1 }
        }
        let rate = Double(hits) / Double(trials)
        context.expect(
            abs(rate - 0.54) < 0.05,
            "observed hit rate \(rate) is near the 0.54 formula"
        )
    }

    private static func checkBraceMitigationReducesDamage(context: inout CheckContext) {
        let rates = PetCombatRates()
        // A hard-hitting, reliably-landing attacker so we find a hit to compare.
        let attacker = CombatStats(power: 12, defense: 7, agility: 60, wit: 7)
        // Same seed → identical hit/crit/swing rolls, so full vs braced isolates
        // only the multiplier.
        for seed in UInt64(0)..<40 {
            var full = SeededGenerator(seed: seed)
            var target1 = freshFlicker()
            let a = CombatResolver.resolveStrike(
                attacker: attacker, defender: &target1, rates: rates,
                damageMultiplier: 1, using: &full
            )
            guard a.didHit else { continue }
            var braced = SeededGenerator(seed: seed)
            var target2 = freshFlicker()
            let b = CombatResolver.resolveStrike(
                attacker: attacker, defender: &target2, rates: rates,
                damageMultiplier: rates.braceMitigation, using: &braced
            )
            context.expect(b.damage <= a.damage, "bracing never increases damage taken")
            context.expect(b.damage >= 1, "a braced hit still lands for at least 1")
            return
        }
        context.expect(false, "expected at least one hit to compare brace mitigation")
    }

    private static func checkSignatureAlwaysHitsAndHitsHarder(context: inout CheckContext) {
        let rates = PetCombatRates()
        let attacker = CombatStats(power: 7, defense: 11, agility: 7, wit: 7)
        // Guaranteed hit every time, regardless of the target's evasion.
        var generator = SeededGenerator(seed: 8)
        var allHit = true
        var sawAboveBase = false
        let base = rates.strikeDamage(power: 7, targetGuard: flickerStats.defense) // 8.5
        for _ in 0..<50 {
            var target = freshFlicker()
            let outcome = CombatResolver.resolveSignature(
                attacker: attacker, defender: &target, rates: rates, using: &generator
            )
            if !outcome.didHit { allHit = false }
            // ×1.5 means it clears the un-multiplied base even at the low end of variance.
            if Double(outcome.damage) > base { sawAboveBase = true }
        }
        context.expect(allHit, "the Signature always lands")
        context.expect(sawAboveBase, "the Signature hits harder than a base Strike")
    }

    private static func checkBestiaryMatchesTheSpec(context: inout CheckContext) {
        // The stat blocks are the reward the whole design leans on; guard them.
        context.expectEqual(CacheWarren.mote.maxHP, 30, "Mote HP (tuned for a few rounds)")
        context.expectEqual(CacheWarren.flicker.stats.agility, 14, "Flicker is fast")
        context.expectEqual(CacheWarren.monolith.stats.defense, 12, "Monolith is armoured")
        context.expectApproximatelyEqual(CacheWarren.flicker.rewardXP, 25, "Flicker reward")
        context.expectEqual(CacheWarren.encounters.count, 3, "three regular encounters")
        // makeCombatant yields a full-HP fighter from the block.
        let mote = CacheWarren.mote.makeCombatant()
        context.expectEqual(mote.currentHP, 30, "a fresh foe starts at full HP")
        context.expectEqual(mote.currentHP, mote.maxHP, "fresh foe is undamaged")
    }

    private static func checkEncounterReplaysIdentically(context: inout CheckContext) {
        let rates = PetCombatRates()
        func fight() -> CombatEncounter {
            var encounter = CombatEncounter(
                pet: aegisPet(rates), foe: CacheWarren.flicker,
                approach: .aggressive, rates: rates, seed: 314
            )
            encounter.runToCompletion()
            return encounter
        }
        // The whole encounter is Equatable, so this compares log, HP, and the
        // generator's final state at once.
        context.expectEqual(fight(), fight(), "a seeded encounter replays identically")
    }

    private static func checkAggressivePetBeatsMote(context: inout CheckContext) {
        let rates = PetCombatRates()
        var encounter = CombatEncounter(
            pet: aegisPet(rates), foe: CacheWarren.mote,
            approach: .aggressive, rates: rates, seed: 7
        )
        encounter.runToCompletion()
        context.expectEqual(encounter.status, .petVictory, "the pet beats a Mote")
        context.expect(encounter.foe.isDefeated, "the Mote is defeated")
        context.expect(encounter.pet.currentHP > 0, "the pet survives a Mote")
    }

    private static func checkOutmatchedPetIsDefeated(context: inout CheckContext) {
        let rates = PetCombatRates()
        // A deliberately feeble fighter against the mini-boss.
        let weakling = Combatant(
            name: "Sprout",
            stats: CombatStats(power: 1, defense: 0, agility: 1, wit: 1),
            maxHP: 6, currentHP: 6
        )
        var encounter = CombatEncounter(
            pet: weakling, foe: CacheWarren.monolith,
            approach: .aggressive, rates: rates, seed: 7
        )
        encounter.runToCompletion()
        context.expectEqual(encounter.status, .petDefeat, "an outmatched pet is downed")
        context.expect(encounter.pet.isDefeated, "the pet is at 0 HP")
    }

    private static func checkFasterCombatantActsFirst(context: inout CheckContext) {
        let rates = PetCombatRates()
        // The pet's Agility 7 is slower than the Flicker's 14, so the Flicker
        // opens the first round.
        var encounter = CombatEncounter(
            pet: aegisPet(rates), foe: CacheWarren.flicker,
            approach: .aggressive, rates: rates, seed: 1
        )
        encounter.step() // resolve round 1
        context.expectEqual(
            firstStruckAttacker(in: encounter.log), "Flicker",
            "the faster combatant strikes first"
        )
    }

    private static func checkDecisionPointAndUnleashConsumeSignature(context: inout CheckContext) {
        let rates = PetCombatRates()
        // The Monolith is a long fight, so a cadence decision point is reached;
        // Unleashing there must fire the Signature exactly once.
        var encounter = CombatEncounter(
            pet: aegisPet(rates), foe: CacheWarren.monolith,
            approach: .aggressive, rates: rates, seed: 1
        )
        var unleashed = false
        var guardCount = 0
        loop: while guardCount < 400 {
            switch encounter.status {
            case .ongoing:
                encounter.step()
            case .awaitingDecision:
                encounter.decide(approach: .aggressive, unleash: !unleashed)
                unleashed = true
            case .petVictory, .petDefeat:
                break loop
            }
            guardCount += 1
        }
        context.expect(logContainsDecision(encounter.log), "a decision point was reached")
        context.expect(unleashed, "the fight paused for a decision")
        context.expect(logContainsSignature(encounter.log), "Unleash fired the Signature")
        context.expect(!encounter.signatureReady, "the Signature is consumed after use")
    }

    private static func checkExitTierDerivation(context: inout CheckContext) {
        context.expectEqual(ExitTier.forOutcome(victory: true, hpFraction: 0.95), .flawless, "≥90% is flawless")
        context.expectEqual(ExitTier.forOutcome(victory: true, hpFraction: 0.50), .solid, "mid HP is solid")
        context.expectEqual(ExitTier.forOutcome(victory: true, hpFraction: 0.20), .barely, "low HP is barely")
        context.expectEqual(ExitTier.forOutcome(victory: false, hpFraction: 0.90), .downed, "a loss is always downed")
    }

    private static func checkVictoryGrantsXPAndMovesConditionsByTier(context: inout CheckContext) {
        let rates = PetCombatRates()
        let base = midHealthPet()
        var encounter = CombatEncounter(
            pet: aegisPet(rates), foe: CacheWarren.mote,
            approach: .aggressive, rates: rates, seed: 7
        )
        encounter.runToCompletion()
        let resolution = base.applyingOutcome(of: encounter, foe: CacheWarren.mote, rates: rates)

        context.expectApproximatelyEqual(
            resolution.state.totalXP, base.totalXP + CacheWarren.mote.rewardXP,
            "a win grants the foe's reward XP"
        )
        // Whatever tier was reached, the needs moved by exactly that tier's delta
        // (needs start mid, so nothing clamps here).
        let delta = rates.exitConditionDelta(for: resolution.tier)
        context.expectApproximatelyEqual(resolution.state.needs.energy, 50 + delta.energy, "energy moved by tier")
        context.expectApproximatelyEqual(resolution.state.needs.happiness, 50 + delta.happiness, "happiness moved by tier")
        context.expectApproximatelyEqual(resolution.state.needs.trust, 50 + delta.trust, "trust moved by tier")
        context.expectApproximatelyEqual(resolution.state.needs.fullness, 50 + delta.fullness, "fullness moved by tier")
    }

    private static func checkDefeatIsDownedWithNoXP(context: inout CheckContext) {
        let rates = PetCombatRates()
        let base = midHealthPet()
        let weakling = Combatant(
            name: "Sprout",
            stats: CombatStats(power: 1, defense: 0, agility: 1, wit: 1),
            maxHP: 6, currentHP: 6
        )
        var encounter = CombatEncounter(
            pet: weakling, foe: CacheWarren.monolith,
            approach: .aggressive, rates: rates, seed: 7
        )
        encounter.runToCompletion()
        let resolution = base.applyingOutcome(of: encounter, foe: CacheWarren.monolith, rates: rates)

        context.expectEqual(resolution.tier, .downed, "a defeat is downed")
        context.expectApproximatelyEqual(resolution.xpGained, 0, "a defeat grants no XP")
        context.expectApproximatelyEqual(resolution.state.totalXP, base.totalXP, "XP is unchanged on a loss")
        context.expect(resolution.state.needs.happiness < 50, "a downed exit lowers Happiness")
        context.expect(resolution.state.needs.energy < 50, "a downed exit costs Energy")
    }

    private static func checkOutcomeStaysInsideTheReversibleEnvelope(context: inout CheckContext) {
        let rates = PetCombatRates()
        // A best-case delta on a maxed-out pet clamps at 100, never above.
        let flawless = rates.exitConditionDelta(for: .flawless)
        let ceilinged = PetNeeds(
            hunger: 0 - flawless.fullness,
            energy: 100 + flawless.energy,
            happiness: 100 + flawless.happiness,
            trust: 100 + flawless.trust
        )
        context.expectApproximatelyEqual(ceilinged.happiness, 100, "gains clamp at 100")
        context.expectApproximatelyEqual(ceilinged.fullness, 100, "fullness clamps at 100")
        // A worst-case delta on an empty pet clamps at 0, never below (never broken).
        let downed = rates.exitConditionDelta(for: .downed)
        let floored = PetNeeds(
            hunger: 100 - downed.fullness,
            energy: 0 + downed.energy,
            happiness: 0 + downed.happiness,
            trust: 0 + downed.trust
        )
        context.expectApproximatelyEqual(floored.energy, 0, "losses clamp at 0")
        context.expectApproximatelyEqual(floored.happiness, 0, "happiness never goes negative")
        context.expectApproximatelyEqual(floored.fullness, 0, "fullness clamps at 0")
    }

    private static func checkPetCombatantBuildsFromState(context: inout CheckContext) {
        let rates = PetCombatRates()
        let state = PetState.newPet(now: Date(timeIntervalSinceReferenceDate: 0))
            .applying(needs: fullHealth)
        let fromState = Combatant.pet(from: state, rates: rates)
        let direct = Combatant.pet(
            name: state.name, baseStats: state.stats, needs: state.needs, rates: rates
        )
        context.expectEqual(fromState, direct, "pet(from:) matches building from the parts")
        context.expectEqual(fromState.name, state.name, "the combatant takes the pet's name")
    }

    private static func checkDelveEntryEligibility(context: inout CheckContext) {
        let rates = PetCombatRates()
        let t0 = Date(timeIntervalSinceReferenceDate: 0)
        // A new pet is Level 1 — below the Level-3 gate.
        let fresh = PetState.newPet(now: t0).applying(needs: fullHealth)
        context.expectEqual(
            rates.delveBlock(for: fresh), .belowGateLevel(required: 3),
            "a below-gate pet is blocked"
        )
        context.expect(!rates.canEnterDelve(fresh), "cannot enter below the gate")

        // 300 XP reaches Level 3; healthy needs clear the refusal.
        let ready = fresh.applying(addingXP: 300)
        context.expect(ready.level >= 3, "300 XP reaches the gate level")
        context.expect(rates.canEnterDelve(ready), "a healthy, leveled pet may enter")
        context.expect(rates.delveBlock(for: ready) == nil, "no block when eligible")

        // A single critical need refuses the delve even at level.
        let neglected = ready.applying(
            needs: PetNeeds(hunger: 95, energy: 5, happiness: 50, trust: 50)
        )
        context.expectEqual(
            rates.delveBlock(for: neglected), .needsCare,
            "a critical need blocks entry"
        )
    }
}
