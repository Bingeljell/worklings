/// The dungeon combat model's tunable numbers and the core resolution formulas.
///
/// Same posture as `PetProgressionRates`: every value here is first-pass alpha
/// tuning — a knob, held now and retuned from real play later without touching
/// the mechanism. The design and the full knob list live in
/// `docs/design/dungeons.md`.
///
/// This first slice covers only the four core formulas (max HP, strike damage,
/// hit chance, crit chance). Damage variance, the condition-effectiveness
/// multiplier, actions, and the resolver arrive in later slices, and their
/// knobs land here alongside these.
public struct PetCombatRates: Equatable, Sendable {
    /// Flat combat HP every combatant starts from, before Vitality.
    public let baseHP: Double
    /// Combat HP added per point of Vitality.
    public let vitalityToHP: Double
    /// Strike damage per point of Power.
    public let powerScale: Double
    /// Strike damage removed per point of the target's Guard.
    public let guardScale: Double
    /// Symmetric random swing applied to a Strike's damage, e.g. 0.15 = ±15%.
    public let strikeVariance: Double
    /// Base hit chance before the attacker/defender Agility difference.
    public let baseHitChance: Double
    /// Hit chance gained per point of Agility advantage over the defender.
    public let agilityToHit: Double
    /// Lower bound on hit chance, so nothing is ever a guaranteed miss.
    public let hitChanceFloor: Double
    /// Upper bound on hit chance, so nothing is ever a guaranteed hit.
    public let hitChanceCeiling: Double
    /// Crit chance gained per point of Agility.
    public let critChancePerAgility: Double
    /// Damage multiplier applied on a crit.
    public let critMultiplier: Double
    /// Lower bound on the condition→combat effectiveness multiplier. Higher than
    /// the progression `conditionMultiplierFloor` so neglect weakens a fighter
    /// without crippling it — see the closed loop in the dungeon design.
    public let combatEffectivenessFloor: Double
    /// Damage multiplier applied to a Strike that lands on a Bracing target,
    /// e.g. 0.5 halves it. The patient option's payoff.
    public let braceMitigation: Double
    /// Flat HP a Bracing combatant regains that round.
    public let braceRegen: Int
    /// Damage multiplier on the once-per-encounter Signature, which always hits.
    public let signatureMultiplier: Double
    /// Rounds between the steady "reassess" decision points.
    public let decisionCadenceRounds: Int
    /// HP fraction below which the "faltering" decision point fires (once).
    public let lowHPEventThreshold: Double
    /// HP fraction below which a Careful Approach chooses Brace over Strike.
    public let carefulBraceThreshold: Double

    public init(
        baseHP: Double = 20,
        vitalityToHP: Double = 3,
        powerScale: Double = 1.5,
        guardScale: Double = 1,
        strikeVariance: Double = 0.15,
        baseHitChance: Double = 0.75,
        agilityToHit: Double = 0.03,
        hitChanceFloor: Double = 0.25,
        hitChanceCeiling: Double = 0.95,
        critChancePerAgility: Double = 0.01,
        critMultiplier: Double = 1.5,
        combatEffectivenessFloor: Double = 0.5,
        braceMitigation: Double = 0.5,
        braceRegen: Int = 2,
        signatureMultiplier: Double = 1.5,
        decisionCadenceRounds: Int = 3,
        lowHPEventThreshold: Double = 0.3,
        carefulBraceThreshold: Double = 0.5
    ) {
        self.baseHP = max(baseHP, 0)
        self.vitalityToHP = max(vitalityToHP, 0)
        self.powerScale = max(powerScale, 0)
        self.guardScale = max(guardScale, 0)
        self.strikeVariance = min(max(strikeVariance, 0), 1)
        self.baseHitChance = min(max(baseHitChance, 0), 1)
        self.agilityToHit = max(agilityToHit, 0)
        self.hitChanceFloor = min(max(hitChanceFloor, 0), 1)
        self.hitChanceCeiling = min(max(hitChanceCeiling, 0), 1)
        self.critChancePerAgility = max(critChancePerAgility, 0)
        self.critMultiplier = max(critMultiplier, 1)
        self.combatEffectivenessFloor = min(max(combatEffectivenessFloor, 0), 1)
        self.braceMitigation = min(max(braceMitigation, 0), 1)
        self.braceRegen = max(braceRegen, 0)
        self.signatureMultiplier = max(signatureMultiplier, 1)
        self.decisionCadenceRounds = max(decisionCadenceRounds, 1)
        self.lowHPEventThreshold = min(max(lowHPEventThreshold, 0), 1)
        self.carefulBraceThreshold = min(max(carefulBraceThreshold, 0), 1)
    }

    /// The condition→combat multiplier: full condition fights at 100%, neglect
    /// scales down to `combatEffectivenessFloor`. Reuses the same needs average
    /// the progression multiplier uses, so care means one consistent thing.
    public func combatEffectiveness(needs: PetNeeds) -> Double {
        needs.xpMultiplier(floor: combatEffectivenessFloor)
    }

    /// A combatant's maximum combat HP. This is a transient pool, unrelated to
    /// the condition needs — see the closed loop in the dungeon design.
    public func maxHP(vitality: Int) -> Int {
        Int((baseHP + Double(vitality) * vitalityToHP).rounded())
    }

    /// The base damage of a Strike before variance and crit, floored at 1 so a
    /// heavily armoured target still takes chip damage rather than none.
    public func strikeDamage(power: Int, targetGuard: Int) -> Double {
        max(1, Double(power) * powerScale - Double(targetGuard) * guardScale)
    }

    /// The chance a Strike lands, shaded by the Agility gap and clamped so the
    /// result is always a real gamble.
    public func hitChance(attackerAgility: Int, defenderAgility: Int) -> Double {
        let raw = baseHitChance
            + Double(attackerAgility - defenderAgility) * agilityToHit
        return min(max(raw, hitChanceFloor), hitChanceCeiling)
    }

    /// The chance a landed Strike crits, from the attacker's Agility, clamped to
    /// a probability.
    public func critChance(agility: Int) -> Double {
        min(max(Double(agility) * critChancePerAgility, 0), 1)
    }
}
