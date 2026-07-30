import CompanionCore

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
}
