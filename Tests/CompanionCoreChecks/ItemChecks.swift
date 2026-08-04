import CompanionCore
import Foundation

/// Checks for gear: the slot/attunement catalogue, the read-time effective-stat
/// fold, the loadout invariants, and the persistence round-trip.
///
/// The load-bearing property is the founding rule — gear never touches persisted
/// base stats — so several of these assert what *didn't* change as firmly as what
/// did.
enum ItemChecks {
    static func run(context: inout CheckContext) {
        checkEveryItemIsMonoStatAndSlotted(context: &context)
        checkAttunementReadsALargerModifier(context: &context)
        checkFoldAddsToEffectiveNeverToBase(context: &context)
        checkEmptyLoadoutIsAnIdentityFold(context: &context)
        checkStackedSlotsSumPerStat(context: &context)
        checkItemOnlyFitsItsOwnSlot(context: &context)
        checkCannotEquipUnownedItem(context: &context)
        checkNewPetStartsWithEquippedStarter(context: &context)
        checkPreGearSaveReadsAsStarterLoadout(context: &context)
        checkGearRoundTripsThroughCoding(context: &context)
        checkCombatantFightsWithGearScaledByCondition(context: &context)
    }

    // MARK: Fixtures

    private static let rates = ItemRates()
    private static let combatRates = PetCombatRates()

    private static let flatStats = PetStats(
        vitality: 10, power: 10, defense: 10, agility: 10, wit: 10
    )
    private static let fullHealth = PetNeeds(
        hunger: 0, energy: 100, happiness: 100, trust: 100
    )

    private static func state(
        family: PetFamily = .wildkin,
        owning items: [Item] = Item.allCases,
        loadout: Loadout = .empty
    ) -> PetState {
        PetState(
            name: "Gearbox",
            family: family,
            needs: fullHealth,
            preferences: PetPreferences(favouriteFood: .berries, favouritePlayActivity: .puzzle),
            lastUpdatedAt: Date(timeIntervalSinceReferenceDate: 0),
            stats: flatStats,
            ownedItems: items,
            loadout: loadout
        )
    }

    // MARK: Catalogue

    /// Every v1 item is exactly one slot and one primary stat, and each of the
    /// five stats has an item — the shape the design locked, before any numbers.
    private static func checkEveryItemIsMonoStatAndSlotted(context: inout CheckContext) {
        let stats = Set(Item.allCases.map(\.stat))
        context.expectEqual(
            stats, Set(PetStatKind.allCases),
            "every primary stat has exactly one base item"
        )
        context.expectEqual(
            Item.allCases.count, PetStatKind.allCases.count,
            "the base set is one item per stat, no more"
        )
        for slot in ItemSlot.allCases {
            let members = Item.all(in: slot)
            context.expect(
                !members.isEmpty && members.allSatisfy { $0.slot == slot },
                "\(slot.displayName) lists only items that belong to it"
            )
        }
    }

    /// The attunement rider is real but soft: a matching family reads more than a
    /// non-matching one, and the gap is smaller than the base — a nudge, not a gate.
    private static func checkAttunementReadsALargerModifier(context: inout CheckContext) {
        let whetstone = Item.crackedWhetstone  // Power → Relicborn
        let attuned = rates.modifier(for: whetstone, family: .relicborn)
        let plain = rates.modifier(for: whetstone, family: .wildkin)

        context.expectEqual(attuned, rates.baseModifier + rates.attunementBonus,
                            "an attuned family reads base + rider")
        context.expectEqual(plain, rates.baseModifier,
                            "a non-attuned family reads the universal base")
        context.expect(attuned > plain, "attunement is a real advantage")
        context.expect(rates.attunementBonus < rates.baseModifier,
                       "the rider is smaller than the base — synergy nudges, it doesn't gate")
        context.expect(rates.isAttuned(whetstone, family: .relicborn),
                       "the attunement flag agrees with the modifier")

        // Guard and Agility attune to families that don't exist in code yet, so
        // they must read the universal base for everyone rather than silently
        // matching some family.
        for item in [Item.dentedBuckler, .quickstepCharm] {
            let readings = PetFamily.allCases.map { rates.modifier(for: item, family: $0) }
            context.expect(
                readings.allSatisfy { $0 == rates.baseModifier },
                "\(item.displayName) is universal-only until its family ships"
            )
        }
    }

    // MARK: The read-time fold

    /// The founding rule: equipping moves the effective sheet and leaves the
    /// persisted base untouched, so no gear change ever needs a stat migration.
    private static func checkFoldAddsToEffectiveNeverToBase(context: inout CheckContext) {
        let geared = state(family: .relicborn)
            .equipping(.crackedWhetstone)  // +Power, attuned to Relicborn

        context.expectEqual(geared.stats, flatStats,
                            "equipping never rewrites the persisted base stats")
        context.expectEqual(
            geared.effectiveStats(rates: rates).power,
            flatStats.power + rates.baseModifier + rates.attunementBonus,
            "the attuned Power item shows up in effective Power"
        )
        context.expectEqual(
            geared.effectiveStats(rates: rates).wit, flatStats.wit,
            "a mono-stat item leaves every other stat alone"
        )

        // Unequipping returns the sheet exactly, with nothing left behind.
        let stripped = geared.clearingSlot(.tool)
        context.expectEqual(
            stripped.effectiveStats(rates: rates), flatStats,
            "removing the item removes its whole contribution"
        )
    }

    private static func checkEmptyLoadoutIsAnIdentityFold(context: inout CheckContext) {
        let bare = state(loadout: .empty)
        context.expectEqual(
            bare.effectiveStats(rates: rates), flatStats,
            "no gear equipped means effective stats are the base stats"
        )
        context.expect(Loadout.empty.isEmpty, "the empty loadout reports itself empty")
        context.expectEqual(
            Loadout.empty.modifiers(family: .wildkin, rates: rates), [:],
            "an empty loadout contributes nothing"
        )
    }

    /// Three filled slots fold together, and two items on the same stat sum
    /// rather than one shadowing the other.
    private static func checkStackedSlotsSumPerStat(context: inout CheckContext) {
        let full = Loadout()
            .equipping(.crackedWhetstone)  // Tool,  +Power
            .equipping(.dentedBuckler)     // Ward,  +Guard
            .equipping(.quickstepCharm)    // Charm, +Agility
        let sheet = flatStats.effective(loadout: full, family: .wildkin, rates: rates)

        context.expectEqual(sheet.power, flatStats.power + rates.baseModifier,
                            "the Tool's Power lands")
        context.expectEqual(sheet.defense, flatStats.defense + rates.baseModifier,
                            "the Ward's Guard lands")
        context.expectEqual(sheet.agility, flatStats.agility + rates.baseModifier,
                            "the Charm's Agility lands")
        context.expectEqual(sheet.vitality, flatStats.vitality,
                            "an unrepresented stat is untouched by a full loadout")

        // Both Ward items favour survival; swapping one for the other moves a
        // different stat, which is the whole point of the slot being a choice.
        // The Coal attunes to Wildkin, so this swap also shows the rider landing
        // inside a full loadout rather than only in isolation.
        let coal = full.equipping(.warmBackupCoal)
        let coalSheet = flatStats.effective(loadout: coal, family: .wildkin, rates: rates)
        context.expectEqual(coalSheet.defense, flatStats.defense,
                            "swapping the Ward drops the previous item's Guard")
        context.expectEqual(
            coalSheet.vitality,
            flatStats.vitality + rates.baseModifier + rates.attunementBonus,
            "the new Ward's Vitality takes its place, at the attuned rate"
        )
    }

    // MARK: Loadout invariants

    private static func checkItemOnlyFitsItsOwnSlot(context: inout CheckContext) {
        // The Whetstone is a Tool; forcing it into the Ward slot is rejected.
        let attempted = Loadout().equipping(.crackedWhetstone, in: .ward)
        context.expectEqual(attempted, .empty,
                            "an item can't be forced into a slot it doesn't belong to")

        // The same guard holds when a save is constructed with a mismatch.
        let mismatched = Loadout(tool: .rubberDuck, ward: nil, charm: nil)
        context.expect(mismatched.tool == nil,
                       "a mismatched stored slot is dropped on read, not honoured")

        let placed = Loadout().equipping(.rubberDuck)
        context.expectEqual(placed.charm, .rubberDuck,
                            "an item lands in its own slot without being told which")
        context.expectEqual(placed[.charm], .rubberDuck,
                            "the slot subscript reads back what was equipped")
    }

    private static func checkCannotEquipUnownedItem(context: inout CheckContext) {
        let poor = state(owning: [.rubberDuck])
        let attempted = poor.equipping(.crackedWhetstone)
        context.expectEqual(attempted.loadout, poor.loadout,
                            "equipping an unowned item leaves the state unchanged")

        // Constructing a state that wears something it doesn't own is corrected
        // on the way in rather than persisting a phantom bonus.
        let phantom = state(
            owning: [.rubberDuck],
            loadout: Loadout().equipping(.crackedWhetstone)
        )
        context.expect(phantom.loadout.tool == nil,
                       "a loadout referencing an unowned item is dropped")
        context.expectEqual(phantom.effectiveStats(rates: rates).power, flatStats.power,
                            "a phantom item grants no stats")

        let acquired = poor.acquiring(.crackedWhetstone).equipping(.crackedWhetstone)
        context.expectEqual(acquired.loadout.tool, .crackedWhetstone,
                            "acquiring the item first makes the equip stick")
        context.expectEqual(
            poor.acquiring(.rubberDuck).ownedItems, poor.ownedItems,
            "acquiring an item already owned doesn't duplicate it"
        )
    }

    // MARK: Persistence

    private static func checkNewPetStartsWithEquippedStarter(context: inout CheckContext) {
        let fresh = PetState.newPet(now: Date(timeIntervalSinceReferenceDate: 0))
        context.expectEqual(fresh.ownedItems, [PetState.starterItem],
                            "a new Workling owns exactly the starter item")
        context.expectEqual(fresh.loadout[PetState.starterItem.slot], PetState.starterItem,
                            "the starter arrives equipped, so the effect is visible at once")
        context.expect(
            fresh.effectiveStats(rates: rates).value(for: PetState.starterItem.stat)
                > fresh.stats.value(for: PetState.starterItem.stat),
            "the starter item actually moves the sheet"
        )
    }

    /// A save written before gear existed has no item fields at all. It must read
    /// as the starter loadout — what a pet created today gets — rather than as an
    /// empty inventory the player could never fill.
    private static func checkPreGearSaveReadsAsStarterLoadout(context: inout CheckContext) {
        let legacy = """
        {
          "schemaVersion": 2,
          "name": "Pixel",
          "family": "wildkin",
          "needs": { "hunger": 15, "energy": 80, "happiness": 70, "trust": 50 },
          "preferences": { "favouriteFood": "berries", "favouritePlayActivity": "puzzle" },
          "lastUpdatedAt": 0,
          "workLog": { "value": 0 },
          "totalXP": 0,
          "petClass": "wellspring",
          "dailyXP": { "value": {} }
        }
        """
        guard let data = legacy.data(using: .utf8),
              let decoded = try? JSONDecoder().decode(PetState.self, from: data) else {
            context.expect(false, "a pre-gear save decodes")
            return
        }
        context.expectEqual(decoded.ownedItems, [PetState.starterItem],
                            "a pre-gear save reads as owning the starter item")
        context.expectEqual(decoded.loadout[PetState.starterItem.slot], PetState.starterItem,
                            "a pre-gear save reads as wearing the starter item")
        context.expectEqual(decoded.stats, PetStats(),
                            "the pre-gear save's own fields are untouched by the gear default")
    }

    private static func checkGearRoundTripsThroughCoding(context: inout CheckContext) {
        let original = state(family: .elemental)
            .equipping(.crackedWhetstone)
            .equipping(.warmBackupCoal)
            .equipping(.rubberDuck)

        guard let data = try? JSONEncoder().encode(original),
              let decoded = try? JSONDecoder().decode(PetState.self, from: data) else {
            context.expect(false, "a geared state round-trips through JSON")
            return
        }
        context.expectEqual(decoded.loadout, original.loadout,
                            "every equipped slot survives a save/load")
        context.expectEqual(decoded.ownedItems, original.ownedItems,
                            "the inventory survives a save/load")
        context.expectEqual(
            decoded.effectiveStats(rates: rates), original.effectiveStats(rates: rates),
            "the folded sheet is identical after a round-trip"
        )
    }

    // MARK: Combat

    /// Gear reaches the fight, and it folds in *before* the condition multiplier —
    /// so a neglected Workling's equipment is scaled down with everything else and
    /// nobody can gear their way out of care.
    private static func checkCombatantFightsWithGearScaledByCondition(context: inout CheckContext) {
        let bare = state(loadout: .empty)
        let geared = bare.equipping(.crackedWhetstone)

        let barePet = Combatant.pet(from: bare, rates: combatRates, itemRates: rates)
        let gearedPet = Combatant.pet(from: geared, rates: combatRates, itemRates: rates)
        context.expect(gearedPet.stats.power > barePet.stats.power,
                       "an equipped Tool raises the combatant's Power")

        // Same gear, poor condition: the geared Power must come out lower than at
        // full health, which only holds if gear folds in before the multiplier.
        let neglected = PetState(
            name: geared.name,
            family: geared.family,
            needs: PetNeeds(hunger: 100, energy: 0, happiness: 0, trust: 0),
            preferences: geared.preferences,
            lastUpdatedAt: geared.lastUpdatedAt,
            stats: flatStats,
            ownedItems: geared.ownedItems,
            loadout: geared.loadout
        )
        let neglectedPet = Combatant.pet(from: neglected, rates: combatRates, itemRates: rates)
        context.expect(neglectedPet.stats.power < gearedPet.stats.power,
                       "condition still scales a geared fighter down")
        context.expect(neglectedPet.maxHP < gearedPet.maxHP,
                       "the same holds for Vitality-derived HP")
    }
}
