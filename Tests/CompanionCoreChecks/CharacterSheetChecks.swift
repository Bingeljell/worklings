import CompanionCore
import Foundation

/// Checks for the Character Screen's readout.
///
/// The sheet is the one place a player *sees* the base → sheet → combat ladder,
/// so these guard the property that makes the screen trustworthy: each rung is
/// reported separately, and the combat rung is the arena's own arithmetic rather
/// than a second implementation that could drift away from it.
enum CharacterSheetChecks {
    static func run(context: inout CheckContext) {
        checkRowsSplitBaseFromGear(context: &context)
        checkSignatureStatIsMarkedOnce(context: &context)
        checkCombatReadoutMatchesTheCombatantTheArenaBuilds(context: &context)
        checkNeglectDiminishesTheCombatRungOnly(context: &context)
        checkAttunedItemsAreListedForTheMatchingFamily(context: &context)
        checkBareSheetReportsNoGear(context: &context)
    }

    // MARK: Fixtures

    private static let combatRates = PetCombatRates()
    private static let itemRates = ItemRates()

    private static let flatStats = PetStats(
        vitality: 10, power: 10, defense: 10, agility: 10, wit: 10
    )
    private static let fullHealth = PetNeeds(
        hunger: 0, energy: 100, happiness: 100, trust: 100
    )
    private static let neglected = PetNeeds(
        hunger: 90, energy: 15, happiness: 15, trust: 40
    )

    private static func state(
        family: PetFamily = .wildkin,
        petClass: PetClass = .juggernaut,
        needs: PetNeeds = fullHealth,
        loadout: Loadout = .empty
    ) -> PetState {
        PetState(
            name: "Sheet",
            family: family,
            needs: needs,
            preferences: PetPreferences(favouriteFood: .berries, favouritePlayActivity: .puzzle),
            lastUpdatedAt: Date(timeIntervalSinceReferenceDate: 0),
            petClass: petClass,
            stats: flatStats,
            ownedItems: Item.allCases,
            loadout: loadout
        )
    }

    private static func sheet(for state: PetState) -> CharacterSheet {
        CharacterSheet.make(state: state, combatRates: combatRates, itemRates: itemRates)
    }

    // MARK: The ladder

    /// Gear shows up as its own column, never folded into the base — the whole
    /// point of the row is that a player can see what the equipment bought and
    /// what they'd still have without it.
    private static func checkRowsSplitBaseFromGear(context: inout CheckContext) {
        let equipped = sheet(for: state(loadout: Loadout(tool: .crackedWhetstone)))
        guard let power = equipped.rows.first(where: { $0.stat == .power }) else {
            context.expect(false, "the sheet reports a row for Power")
            return
        }

        context.expectEqual(power.base, 10, "the Power row keeps the persisted base")
        context.expectEqual(
            power.gearBonus,
            itemRates.baseModifier,
            "the Power row reports the whetstone's modifier separately"
        )
        context.expectEqual(
            power.effective,
            10 + itemRates.baseModifier,
            "the Power row's effective value is base plus gear"
        )
        context.expectEqual(
            equipped.gearPointTotal,
            itemRates.baseModifier,
            "one equipped item totals its own modifier and nothing else"
        )

        let untouched = equipped.rows.filter { $0.stat != .power }
        context.expect(
            untouched.allSatisfy { $0.gearBonus == 0 && $0.effective == 10 },
            "a mono-stat item leaves every other row at its base"
        )
    }

    /// Exactly one row carries the signature mark, and it is the class's — the
    /// screen leans on this to make class identity legible at a glance.
    private static func checkSignatureStatIsMarkedOnce(context: inout CheckContext) {
        for petClass in PetClass.allCases {
            let rows = sheet(for: state(petClass: petClass)).rows
            let signatures = rows.filter(\.isSignature)
            context.expectEqual(
                signatures.map(\.stat),
                [petClass.signatureStat],
                "\(petClass.displayName) marks exactly its signature stat"
            )
        }
    }

    /// The load-bearing one: the readout is the arena's combatant, not a parallel
    /// calculation. Compared against a freshly built `Combatant` so the two can
    /// only agree by actually being the same arithmetic.
    private static func checkCombatReadoutMatchesTheCombatantTheArenaBuilds(
        context: inout CheckContext
    ) {
        let geared = state(
            loadout: Loadout(tool: .crackedWhetstone, ward: .warmBackupCoal, charm: .quickstepCharm)
        )
        let readout = sheet(for: geared).combat
        let combatant = Combatant.pet(from: geared, rates: combatRates, itemRates: itemRates)

        context.expectEqual(
            readout.maxHP,
            combatant.maxHP,
            "the sheet's max HP is the combatant's max HP"
        )
        context.expectApproximatelyEqual(
            readout.critChance,
            combatRates.critChance(agility: combatant.stats.agility),
            "the sheet's crit chance reads the combatant's agility"
        )
        context.expectEqual(
            readout.strike,
            Int(combatRates.strikeDamage(power: combatant.stats.power, targetGuard: 0).rounded()),
            "the sheet's strike is an unmitigated hit at the combatant's power"
        )
    }

    /// Neglect scales what walks into the fight, and *only* that: the base and
    /// gear rungs are untouched, so a hungry Workling's sheet still shows the
    /// numbers it will have back once it's cared for.
    private static func checkNeglectDiminishesTheCombatRungOnly(context: inout CheckContext) {
        let loadout = Loadout(tool: .crackedWhetstone)
        let healthy = sheet(for: state(needs: fullHealth, loadout: loadout))
        let hungry = sheet(for: state(needs: neglected, loadout: loadout))

        context.expectEqual(
            hungry.rows,
            healthy.rows,
            "condition never moves the base or gear rungs"
        )
        context.expect(
            hungry.combat.maxHP < healthy.combat.maxHP,
            "a neglected Workling takes less HP into the fight"
        )
        context.expect(
            hungry.combat.isDiminished && !healthy.combat.isDiminished,
            "only the neglected sheet reports itself diminished"
        )
    }

    /// Attunement is listed for the family that matches and nobody else — the
    /// screen's ✦ mark is driven by this rather than by re-deriving the pairing.
    private static func checkAttunedItemsAreListedForTheMatchingFamily(
        context: inout CheckContext
    ) {
        let loadout = Loadout(tool: .crackedWhetstone)  // Power → Relicborn

        context.expectEqual(
            sheet(for: state(family: .relicborn, loadout: loadout)).attunedItems,
            [.crackedWhetstone],
            "a Relicborn's whetstone reads as attuned"
        )
        context.expect(
            sheet(for: state(family: .wildkin, loadout: loadout)).attunedItems.isEmpty,
            "the same whetstone is unattuned on a Wildkin"
        )
        context.expect(
            sheet(for: state(family: .relicborn, loadout: loadout)).gearPointTotal
                > sheet(for: state(family: .wildkin, loadout: loadout)).gearPointTotal,
            "the attuned wearer's gear total carries the rider"
        )
    }

    /// With nothing equipped the screen has nothing to headline, and says so
    /// through the sheet rather than by inspecting the loadout itself.
    private static func checkBareSheetReportsNoGear(context: inout CheckContext) {
        let bare = sheet(for: state(loadout: .empty))

        context.expect(!bare.hasGearEquipped, "an empty loadout reports no gear equipped")
        context.expectEqual(bare.gearPointTotal, 0, "an empty loadout is worth no stat points")
        context.expect(
            bare.rows.allSatisfy { $0.effective == $0.base },
            "every row's effective value is its base when nothing is equipped"
        )
    }
}
