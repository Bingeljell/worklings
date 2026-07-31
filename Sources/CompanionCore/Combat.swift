/// The four stats a combatant actually fights with. Vitality is folded into
/// `maxHP` at build time and Wit stays mostly latent until abilities, so a
/// `Combatant` carries only what the resolver reads each turn.
public struct CombatStats: Equatable, Sendable {
    public let power: Int
    /// The mitigation stat. Named `defense` internally to avoid the `guard`
    /// keyword, exactly as `PetStats` does; the design vocabulary calls it Guard.
    public let defense: Int
    public let agility: Int
    public let wit: Int

    public init(power: Int, defense: Int, agility: Int, wit: Int) {
        self.power = power
        self.defense = defense
        self.agility = agility
        self.wit = wit
    }
}

/// One side of a fight: a name, its combat stats, and a transient HP pool. HP is
/// unrelated to the pet's condition needs — it resets each delve and can never
/// zero-out real wellbeing (see the closed loop in the dungeon design).
public struct Combatant: Equatable, Sendable {
    public let name: String
    public let stats: CombatStats
    public let maxHP: Int
    public private(set) var currentHP: Int
    /// Active timed modifiers (Snare, Blur, Phase, Harden, …). Empty by default,
    /// folded into `effectiveStats` and ticked once per round.
    public private(set) var statuses: [StatusEffect] = []

    public init(name: String, stats: CombatStats, maxHP: Int, currentHP: Int) {
        self.name = name
        self.stats = stats
        self.maxHP = max(maxHP, 0)
        self.currentHP = min(max(currentHP, 0), self.maxHP)
    }

    public var isDefeated: Bool { currentHP <= 0 }

    /// Share of HP remaining, `0...1` — the basis of the delve's exit tier.
    public var hpFraction: Double {
        maxHP > 0 ? Double(currentHP) / Double(maxHP) : 0
    }

    public mutating func takeDamage(_ amount: Int) {
        currentHP = max(0, currentHP - max(0, amount))
    }

    public mutating func heal(_ amount: Int) {
        currentHP = min(maxHP, currentHP + max(0, amount))
    }
}

extension Combatant {
    /// Builds the pet's combatant: its base stats scaled by the
    /// condition→combat effectiveness, with max HP derived from the scaled
    /// Vitality. Starts at full HP. `defense` maps to Guard, the same stat the
    /// design vocabulary names everywhere else.
    public static func pet(
        name: String,
        baseStats: PetStats,
        needs: PetNeeds,
        rates: PetCombatRates
    ) -> Combatant {
        let effectiveness = rates.combatEffectiveness(needs: needs)
        func scaled(_ value: Int) -> Int {
            Int((Double(value) * effectiveness).rounded())
        }
        let maxHP = rates.maxHP(vitality: scaled(baseStats.vitality))
        return Combatant(
            name: name,
            stats: CombatStats(
                power: scaled(baseStats.power),
                defense: scaled(baseStats.defense),
                agility: scaled(baseStats.agility),
                wit: scaled(baseStats.wit)
            ),
            maxHP: maxHP,
            currentHP: maxHP
        )
    }

    /// Builds the pet's combatant straight from its live `PetState` — name, the
    /// earned stat sheet, and current condition — so the app never has to
    /// unpack the state itself.
    public static func pet(from state: PetState, rates: PetCombatRates) -> Combatant {
        pet(name: state.name, baseStats: state.stats, needs: state.needs, rates: rates)
    }

    /// Builds a foe from its stat block. Foe HP is authored directly (the
    /// bestiary lists it), not derived from Vitality, and condition never scales
    /// a foe — only the pet is cared for.
    public static func foe(
        name: String,
        maxHP: Int,
        stats: CombatStats
    ) -> Combatant {
        Combatant(name: name, stats: stats, maxHP: maxHP, currentHP: maxHP)
    }
}

// MARK: - Status effects

extension Combatant {
    /// Stats after active effects: Snare lowers Agility, Harden raises Guard.
    /// Power and Wit are untouched for now. The resolver reads these, never the
    /// raw block, so every timed modifier lands in one place.
    public var effectiveStats: CombatStats {
        var agility = stats.agility
        var defense = stats.defense
        for effect in statuses {
            switch effect.kind {
            case .agilityDebuff: agility -= effect.magnitude
            case .guardBuff: defense += effect.magnitude
            case .evasion, .phasing: break
            }
        }
        return CombatStats(
            power: stats.power,
            defense: max(0, defense),
            agility: max(0, agility),
            wit: stats.wit
        )
    }

    /// Extra chance (0…1) for an incoming attack to miss, from Blur-style evasion.
    public var evasionChance: Double {
        let points = statuses
            .filter { $0.kind == .evasion }
            .reduce(0) { $0 + $1.magnitude }
        return Double(points) / 100
    }

    /// Whether a Phase is up, ready to slip the next incoming attack.
    public var isPhasing: Bool {
        statuses.contains { $0.kind == .phasing }
    }

    /// Applies a new timed effect.
    public mutating func apply(_ effect: StatusEffect) {
        statuses.append(effect)
    }

    /// Ages every effect one round and drops the expired ones. Called once at the
    /// top of each round.
    public mutating func tickStatuses() {
        for index in statuses.indices {
            statuses[index].tick()
        }
        statuses.removeAll { $0.isExpired }
    }

    /// Consumes one Phase after it has slipped an attack.
    public mutating func consumePhasing() {
        if let index = statuses.firstIndex(where: { $0.kind == .phasing }) {
            statuses.remove(at: index)
        }
    }
}
