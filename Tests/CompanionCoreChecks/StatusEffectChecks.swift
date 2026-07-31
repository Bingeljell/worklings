import CompanionCore

enum StatusEffectChecks {
    static func run(context: inout CheckContext) {
        checkEffectiveStatsFold(context: &context)
        checkEffectiveStatsClampAtZero(context: &context)
        checkEvasionChance(context: &context)
        checkPhasingLifecycle(context: &context)
        checkTickAgesAndExpires(context: &context)
        checkPhaseSlipsAStrike(context: &context)
        checkFullEvasionForcesMiss(context: &context)
        checkGuardBuffMitigates(context: &context)
    }

    private static func combatant(_ stats: CombatStats, hp: Int = 100) -> Combatant {
        Combatant.foe(name: "Dummy", maxHP: hp, stats: stats)
    }

    private static func checkEffectiveStatsFold(context: inout CheckContext) {
        var c = combatant(CombatStats(power: 5, defense: 4, agility: 6, wit: 3))
        c.apply(StatusEffect(kind: .agilityDebuff, magnitude: 4, remainingRounds: 2))
        c.apply(StatusEffect(kind: .guardBuff, magnitude: 3, remainingRounds: 1))
        context.expectEqual(c.effectiveStats.agility, 2, "agility debuff lowers effective agility")
        context.expectEqual(c.effectiveStats.defense, 7, "guard buff raises effective defense")
        context.expectEqual(c.effectiveStats.power, 5, "power is untouched by these effects")
        context.expectEqual(c.effectiveStats.wit, 3, "wit is untouched by these effects")
    }

    private static func checkEffectiveStatsClampAtZero(context: inout CheckContext) {
        var c = combatant(CombatStats(power: 5, defense: 1, agility: 3, wit: 1))
        c.apply(StatusEffect(kind: .agilityDebuff, magnitude: 10, remainingRounds: 1))
        context.expectEqual(c.effectiveStats.agility, 0, "a heavy debuff floors effective agility at 0")
    }

    private static func checkEvasionChance(context: inout CheckContext) {
        var c = combatant(CombatStats(power: 1, defense: 1, agility: 1, wit: 1))
        context.expectApproximatelyEqual(c.evasionChance, 0, "no evasion by default")
        c.apply(StatusEffect(kind: .evasion, magnitude: 25, remainingRounds: 3))
        c.apply(StatusEffect(kind: .evasion, magnitude: 15, remainingRounds: 3))
        context.expectApproximatelyEqual(c.evasionChance, 0.40, "evasion magnitudes sum into a chance")
    }

    private static func checkPhasingLifecycle(context: inout CheckContext) {
        var c = combatant(CombatStats(power: 1, defense: 1, agility: 1, wit: 1))
        context.expect(!c.isPhasing, "no phase by default")
        c.apply(StatusEffect(kind: .phasing, magnitude: 0, remainingRounds: 2))
        context.expect(c.isPhasing, "phase is active once applied")
        c.consumePhasing()
        context.expect(!c.isPhasing, "consuming removes the phase")
    }

    private static func checkTickAgesAndExpires(context: inout CheckContext) {
        var c = combatant(CombatStats(power: 1, defense: 1, agility: 5, wit: 1))
        c.apply(StatusEffect(kind: .agilityDebuff, magnitude: 2, remainingRounds: 1))
        c.apply(StatusEffect(kind: .guardBuff, magnitude: 2, remainingRounds: 2))
        c.tickStatuses()
        context.expectEqual(c.statuses.count, 1, "a one-round effect expires after a tick")
        context.expect(c.statuses.first?.kind == .guardBuff, "the longer effect remains")
        context.expectEqual(c.statuses.first?.remainingRounds, 1, "the remaining effect aged by one round")
    }

    private static func checkPhaseSlipsAStrike(context: inout CheckContext) {
        let rates = PetCombatRates()
        var generator = SeededGenerator(seed: 5)
        var defender = combatant(CombatStats(power: 3, defense: 2, agility: 4, wit: 1))
        defender.apply(StatusEffect(kind: .phasing, magnitude: 0, remainingRounds: 2))
        let before = defender.currentHP
        let outcome = CombatResolver.resolveStrike(
            attacker: CombatStats(power: 9, defense: 1, agility: 9, wit: 1),
            defender: &defender, rates: rates, using: &generator
        )
        context.expect(!outcome.didHit, "a phasing defender slips the strike")
        context.expectEqual(defender.currentHP, before, "no damage lands through a phase")
        context.expect(!defender.isPhasing, "the phase is consumed by the slip")
    }

    private static func checkFullEvasionForcesMiss(context: inout CheckContext) {
        let rates = PetCombatRates()
        var generator = SeededGenerator(seed: 9)
        var defender = combatant(CombatStats(power: 3, defense: 2, agility: 4, wit: 1))
        defender.apply(StatusEffect(kind: .evasion, magnitude: 100, remainingRounds: 3))
        let outcome = CombatResolver.resolveStrike(
            attacker: CombatStats(power: 9, defense: 1, agility: 20, wit: 1),
            defender: &defender, rates: rates, using: &generator
        )
        context.expect(!outcome.didHit, "full evasion drops the hit chance to a certain miss")
    }

    private static func checkGuardBuffMitigates(context: inout CheckContext) {
        // The Signature is a guaranteed hit, so this isolates the Guard change
        // from the hit roll; it reads effective Guard, so Harden mitigates it too.
        let rates = PetCombatRates()
        let attacker = CombatStats(power: 12, defense: 1, agility: 6, wit: 1)
        let target = CombatStats(power: 1, defense: 2, agility: 1, wit: 1)

        func signatureDamage(guardBuff: Int) -> Int {
            var generator = SeededGenerator(seed: 3)
            var defender = combatant(target, hp: 500)
            if guardBuff > 0 {
                defender.apply(StatusEffect(kind: .guardBuff, magnitude: guardBuff, remainingRounds: 3))
            }
            return CombatResolver.resolveSignature(
                attacker: attacker, defender: &defender, rates: rates, using: &generator
            ).damage
        }

        let plain = signatureDamage(guardBuff: 0)
        let hardened = signatureDamage(guardBuff: 6)
        context.expect(plain > 0, "the Signature lands in the baseline")
        context.expect(hardened < plain, "a guard buff mitigates the incoming damage")
    }
}
