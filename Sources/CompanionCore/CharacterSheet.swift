/// The character sheet as a *readout*: everything the Character Screen shows
/// about a Workling's numbers, derived in one place so the UI only lays it out.
///
/// This exists because the stat story is a ladder, and every rung of it is
/// interesting to a player:
///
/// ```text
/// base      persisted; level + class growth
///   + gear  equipped modifiers            = sheet   (what the screen headlines)
///   × condition                            = combat  (what the resolver reads)
/// ```
///
/// A screen that showed only one rung would either hide what gear bought or hide
/// what neglect costs. So the sheet carries the base, the gear delta, and the
/// combat numbers side by side, and derives the last of those by building the
/// *actual* `Combatant` — the same call the arena makes. The screen therefore
/// cannot drift from the fight: if the resolver's idea of the pet changes, this
/// changes with it.
public struct CharacterSheet: Equatable, Sendable {
    /// One stat's row: where it started, what gear added, and whether the class
    /// leans on it.
    public struct StatRow: Equatable, Sendable {
        public let stat: PetStatKind
        /// The persisted number — never touched by gear.
        public let base: Int
        /// What equipped items add, including any attunement rider.
        public let gearBonus: Int
        /// Whether this is the class's signature stat, which grows fastest.
        public let isSignature: Bool

        public init(stat: PetStatKind, base: Int, gearBonus: Int, isSignature: Bool) {
            self.stat = stat
            self.base = base
            self.gearBonus = gearBonus
            self.isSignature = isSignature
        }

        /// The sheet value: base plus gear, before condition.
        public var effective: Int { base + gearBonus }
    }

    /// What the pet actually walks into an encounter with, after condition
    /// scaling — read off a real `Combatant` rather than recomputed here.
    public struct CombatReadout: Equatable, Sendable {
        public let maxHP: Int
        /// An unmitigated strike (against a hypothetical zero-Guard target), so
        /// the number moves with Power without inventing a specific foe.
        public let strike: Int
        public let critChance: Double
        /// The condition multiplier, `0…1`, that scaled everything above.
        public let effectiveness: Double

        public init(maxHP: Int, strike: Int, critChance: Double, effectiveness: Double) {
            self.maxHP = maxHP
            self.strike = strike
            self.critChance = critChance
            self.effectiveness = effectiveness
        }

        /// Whether condition is currently costing the pet anything — the screen
        /// only nags when there is something to nag about.
        public var isDiminished: Bool { effectiveness < 1 }
    }

    public let name: String
    public let family: PetFamily
    public let petClass: PetClass
    public let level: Int
    public let progress: PetProgressionCurve.Progress
    public let rows: [StatRow]
    public let combat: CombatReadout
    /// Equipped items whose family matches the wearer's — the screen marks these
    /// so the soft synergy is discoverable rather than hidden arithmetic.
    public let attunedItems: [Item]

    /// Total stat points gear is contributing, for the one-line summary.
    public var gearPointTotal: Int {
        rows.reduce(0) { $0 + $1.gearBonus }
    }

    public var hasGearEquipped: Bool { gearPointTotal > 0 }

    public static func make(
        state: PetState,
        combatRates: PetCombatRates = PetCombatRates(),
        itemRates: ItemRates = ItemRates()
    ) -> CharacterSheet {
        let bonuses = state.loadout.modifiers(family: state.family, rates: itemRates)
        let rows = PetStatKind.allCases.map { stat in
            StatRow(
                stat: stat,
                base: state.stats.value(for: stat),
                gearBonus: bonuses[stat] ?? 0,
                isSignature: state.petClass.signatureStat == stat
            )
        }

        // Built, not recomputed: the arena's own constructor, so a change to how
        // condition or gear enters the fight shows up here for free.
        let combatant = Combatant.pet(from: state, rates: combatRates, itemRates: itemRates)
        let readout = CombatReadout(
            maxHP: combatant.maxHP,
            strike: Int(
                combatRates.strikeDamage(power: combatant.stats.power, targetGuard: 0).rounded()
            ),
            critChance: combatRates.critChance(agility: combatant.stats.agility),
            effectiveness: combatRates.combatEffectiveness(needs: state.needs)
        )

        return CharacterSheet(
            name: state.name,
            family: state.family,
            petClass: state.petClass,
            level: state.level,
            progress: PetProgressionCurve.progress(forTotalXP: state.totalXP),
            rows: rows,
            combat: readout,
            attunedItems: state.loadout.equipped.filter {
                itemRates.isAttuned($0, family: state.family)
            }
        )
    }
}
