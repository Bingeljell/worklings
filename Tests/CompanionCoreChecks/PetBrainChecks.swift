import CompanionCore
import Foundation

enum PetBrainChecks {
    static func run(context: inout CheckContext) {
        checkNeedClamping(context: &context)
        checkFullnessPresentation(context: &context)
        checkNewPetDefaults(context: &context)
        checkFamilySelection(context: &context)
        checkRenaming(context: &context)
        checkMoodPriority(context: &context)
        checkDeterministicProgression(context: &context)
        checkBackwardClock(context: &context)
        checkOfflineCap(context: &context)
        checkFavouriteFood(context: &context)
        checkFavouritePlay(context: &context)
        checkExhaustedPlay(context: &context)
        checkSleepTradeoff(context: &context)
    }

    private static func checkNeedClamping(context: inout CheckContext) {
        let needs = PetNeeds(hunger: -20, energy: 130, happiness: -1, trust: 101)

        context.expectEqual(needs.hunger, 0, "hunger clamps to zero")
        context.expectEqual(needs.energy, 100, "energy clamps to one hundred")
        context.expectEqual(needs.happiness, 0, "happiness clamps to zero")
        context.expectEqual(needs.trust, 100, "trust clamps to one hundred")
    }

    private static func checkFullnessPresentation(context: inout CheckContext) {
        let full = PetNeeds(hunger: 0, energy: 80, happiness: 70, trust: 50)
        let hungry = PetNeeds(hunger: 80, energy: 80, happiness: 70, trust: 50)
        let empty = PetNeeds(hunger: 100, energy: 80, happiness: 70, trust: 50)

        context.expectEqual(full.fullness, 100, "zero hunger presents as full")
        context.expectEqual(hungry.fullness, 20, "fullness inverts hunger for display")
        context.expectEqual(empty.fullness, 0, "maximum hunger presents as empty")
    }

    private static func checkNewPetDefaults(context: inout CheckContext) {
        let now = Date(timeIntervalSinceReferenceDate: 1_000)
        let state = PetState.newPet(now: now)

        context.expectEqual(state.schemaVersion, 1, "new pet uses current schema")
        context.expectEqual(state.name, "Pixel", "new pet uses placeholder name")
        context.expectEqual(state.family, .wildkin, "existing pet default remains Wildkin")
        context.expectEqual(state.preferences.favouriteFood, .berries, "new pet has favourite food")
        context.expectEqual(state.preferences.favouritePlayActivity, .puzzle, "new pet has favourite play")
        context.expectEqual(state.lastUpdatedAt, now, "new pet records creation time")
    }

    private static func checkFamilySelection(context: inout CheckContext) {
        let state = PetState.newPet(now: Date(timeIntervalSinceReferenceDate: 1_500))
        let selected = state.selectingFamily(.elemental)

        context.expectEqual(selected.family, .elemental, "family selection changes appearance identity")
        context.expectEqual(selected.name, state.name, "family selection preserves pet name")
        context.expectEqual(selected.needs, state.needs, "family selection preserves needs")
        context.expectEqual(
            selected.preferences,
            state.preferences,
            "family selection preserves preferences"
        )
        context.expectEqual(
            selected.lastUpdatedAt,
            state.lastUpdatedAt,
            "family selection does not advance simulation time"
        )
    }

    private static func checkRenaming(context: inout CheckContext) {
        let state = PetState.newPet(now: Date(timeIntervalSinceReferenceDate: 1_600))
        let renamed = state.renamed(to: "  Ember  ")

        context.expectEqual(renamed.name, "Ember", "renaming trims surrounding whitespace")
        context.expectEqual(renamed.family, state.family, "renaming preserves family")
        context.expectEqual(renamed.needs, state.needs, "renaming preserves needs")
        context.expectEqual(
            renamed.lastUpdatedAt,
            state.lastUpdatedAt,
            "renaming does not advance simulation time"
        )

        context.expect(!PetState.isValidName(""), "an empty name is invalid")
        context.expect(!PetState.isValidName("   "), "a whitespace-only name is invalid")
        context.expect(
            !PetState.isValidName(String(repeating: "a", count: PetState.maximumNameLength + 1)),
            "a name past the maximum length is invalid"
        )
        context.expect(
            PetState.isValidName(String(repeating: "a", count: PetState.maximumNameLength)),
            "a name at the maximum length is valid"
        )

        let rejected = state.renamed(to: "   ")
        context.expectEqual(rejected.name, state.name, "renaming to an invalid name is a no-op")
    }

    private static func checkMoodPriority(context: inout CheckContext) {
        let hungryAndTired = makeState(
            needs: PetNeeds(hunger: 90, energy: 5, happiness: 90, trust: 90)
        )
        let tired = makeState(
            needs: PetNeeds(hunger: 10, energy: 15, happiness: 90, trust: 90)
        )
        let wary = makeState(
            needs: PetNeeds(hunger: 10, energy: 80, happiness: 80, trust: 10)
        )
        let happy = makeState(
            needs: PetNeeds(hunger: 10, energy: 80, happiness: 90, trust: 80)
        )

        context.expectEqual(hungryAndTired.mood, .hungry, "hunger takes priority over exhaustion")
        context.expectEqual(tired.mood, .sleepy, "low energy produces sleepy mood")
        context.expectEqual(wary.mood, .wary, "low trust produces wary mood")
        context.expectEqual(happy.mood, .happy, "strong needs produce happy mood")
    }

    private static func checkDeterministicProgression(context: inout CheckContext) {
        let start = Date(timeIntervalSinceReferenceDate: 2_000)
        let state = makeState(
            needs: PetNeeds(hunger: 10, energy: 80, happiness: 70, trust: 50),
            at: start
        )
        let result = PetBrain().advance(state, to: start.addingTimeInterval(2 * 3_600))

        context.expectApproximatelyEqual(result.needs.hunger, 18, "two hours increase hunger")
        context.expectApproximatelyEqual(result.needs.energy, 74, "two hours decrease energy")
        context.expectApproximatelyEqual(result.needs.happiness, 68, "two hours decrease happiness")
        context.expectApproximatelyEqual(result.needs.trust, 50, "healthy baseline preserves trust")
    }

    private static func checkBackwardClock(context: inout CheckContext) {
        let start = Date(timeIntervalSinceReferenceDate: 3_000)
        let state = makeState(at: start)
        let result = PetBrain().advance(state, to: start.addingTimeInterval(-60))

        context.expectEqual(result, state, "backward clock leaves state unchanged")
    }

    private static func checkOfflineCap(context: inout CheckContext) {
        let start = Date(timeIntervalSinceReferenceDate: 4_000)
        let rates = PetSimulationRates(
            hungerPerHour: 1,
            energyPerHour: 1,
            happinessPerHour: 1,
            maximumOfflineHours: 2
        )
        let state = makeState(
            needs: PetNeeds(hunger: 10, energy: 80, happiness: 70, trust: 50),
            at: start
        )
        let result = PetBrain(rates: rates).advance(
            state,
            to: start.addingTimeInterval(10 * 3_600)
        )

        context.expectApproximatelyEqual(result.needs.hunger, 12, "offline cap limits hunger change")
        context.expectApproximatelyEqual(result.needs.energy, 78, "offline cap limits energy change")
        context.expectEqual(
            result.lastUpdatedAt,
            start.addingTimeInterval(10 * 3_600),
            "offline cap still consumes elapsed period"
        )
    }

    private static func checkFavouriteFood(context: inout CheckContext) {
        let now = Date(timeIntervalSinceReferenceDate: 5_000)
        let state = makeState(
            needs: PetNeeds(hunger: 60, energy: 70, happiness: 50, trust: 40),
            at: now
        )
        let favourite = PetBrain().perform(.feed(.berries), on: state, at: now)
        let ordinary = PetBrain().perform(.feed(.biscuit), on: state, at: now)

        context.expectEqual(favourite.reaction, .lovedFood, "favourite food produces loved reaction")
        context.expectEqual(ordinary.reaction, .likedFood, "ordinary food produces liked reaction")
        context.expect(
            favourite.state.needs.hunger < ordinary.state.needs.hunger,
            "favourite food satisfies more hunger"
        )
        context.expect(
            favourite.state.needs.happiness > ordinary.state.needs.happiness,
            "favourite food adds more happiness"
        )
    }

    private static func checkFavouritePlay(context: inout CheckContext) {
        let now = Date(timeIntervalSinceReferenceDate: 6_000)
        let state = makeState(
            needs: PetNeeds(hunger: 20, energy: 80, happiness: 40, trust: 40),
            at: now
        )
        let favourite = PetBrain().perform(.play(.puzzle), on: state, at: now)
        let ordinary = PetBrain().perform(.play(.dance), on: state, at: now)

        context.expectEqual(favourite.reaction, .lovedPlay, "favourite play produces loved reaction")
        context.expectEqual(ordinary.reaction, .enjoyedPlay, "ordinary play produces enjoyed reaction")
        context.expect(
            favourite.state.needs.happiness > ordinary.state.needs.happiness,
            "favourite play adds more happiness"
        )
        context.expect(
            favourite.state.needs.energy < ordinary.state.needs.energy,
            "favourite play has its documented energy tradeoff"
        )
    }

    private static func checkExhaustedPlay(context: inout CheckContext) {
        let now = Date(timeIntervalSinceReferenceDate: 7_000)
        let state = makeState(
            needs: PetNeeds(hunger: 20, energy: 10, happiness: 40, trust: 40),
            at: now
        )
        let result = PetBrain().perform(.play(.puzzle), on: state, at: now)

        context.expectEqual(result.reaction, .tooTiredToPlay, "exhausted pet refuses play")
        context.expectEqual(result.state.needs, state.needs, "refused play does not change needs")
    }

    private static func checkSleepTradeoff(context: inout CheckContext) {
        let now = Date(timeIntervalSinceReferenceDate: 8_000)
        let state = makeState(
            needs: PetNeeds(hunger: 20, energy: 30, happiness: 40, trust: 40),
            at: now
        )
        let result = PetBrain().perform(.sleep, on: state, at: now)

        context.expectEqual(result.reaction, .rested, "sleep produces rested reaction")
        context.expect(result.state.needs.energy > state.needs.energy, "sleep restores energy")
        context.expect(result.state.needs.hunger > state.needs.hunger, "sleep increases hunger")
    }

    private static func makeState(
        needs: PetNeeds = PetNeeds(hunger: 15, energy: 80, happiness: 70, trust: 50),
        at date: Date = Date(timeIntervalSinceReferenceDate: 0)
    ) -> PetState {
        PetState(
            name: "Pixel",
            needs: needs,
            preferences: PetPreferences(
                favouriteFood: .berries,
                favouritePlayActivity: .puzzle
            ),
            lastUpdatedAt: date
        )
    }
}
