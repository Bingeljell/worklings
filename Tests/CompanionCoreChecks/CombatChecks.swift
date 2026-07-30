import CompanionCore

enum CombatChecks {
    static func run(context: inout CheckContext) {
        checkDefaultRates(context: &context)
        checkMaxHPFromVitality(context: &context)
        checkStrikeDamageAndFloor(context: &context)
        checkHitChanceAndClamp(context: &context)
        checkCritChanceAndClamp(context: &context)
    }

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
}
