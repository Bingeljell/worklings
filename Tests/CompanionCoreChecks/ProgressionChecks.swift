import CompanionCore
import Foundation

enum ProgressionChecks {
    static func run(context: inout CheckContext) {
        checkNewPetProgressionDefaults(context: &context)
        checkCurveIsMonotonicAndUnbounded(context: &context)
        checkLevelDerivesFromTotalXP(context: &context)
        checkConditionMultiplierFloorAndScale(context: &context)
        checkDailyWakeGrantsXP(context: &context)
        checkCareActionGrantsXP(context: &context)
        checkTooTiredToPlayGrantsNoXP(context: &context)
        checkWorkLoggedGrantsXP(context: &context)
        checkFocusSessionBelowMinimumDurationGrantsNoXP(context: &context)
        checkFocusSessionAboveMinimumDurationGrantsXP(context: &context)
        checkPerSourceDailyCapBlocksFurtherGrants(context: &context)
        checkOverallDailyCapBlocksGrantsAcrossSources(context: &context)
        checkDailyCapsResetOnNewDay(context: &context)
        checkLevelUpAppliesClassWeightedStatGrowth(context: &context)
        checkSelectingClassPreservesEverythingElse(context: &context)
        checkFamilySelectionPreservesProgression(context: &context)
        checkRenamingPreservesProgression(context: &context)
    }

    private static let start = Date(timeIntervalSinceReferenceDate: 50_000)

    private static func fullHealthState(totalXP: Double = 0, petClass: PetClass = .tinkerer) -> PetState {
        var state = PetState.newPet(now: start)
        state = PetState(
            schemaVersion: state.schemaVersion,
            name: state.name,
            family: state.family,
            needs: PetNeeds(hunger: 0, energy: 100, happiness: 100, trust: 100),
            preferences: state.preferences,
            lastUpdatedAt: state.lastUpdatedAt,
            totalXP: totalXP,
            petClass: petClass
        )
        return state
    }

    private static func checkNewPetProgressionDefaults(context: inout CheckContext) {
        let state = PetState.newPet(now: start)

        context.expectEqual(state.totalXP, 0, "a new pet starts with no XP")
        context.expectEqual(state.level, 1, "a new pet starts at level 1")
        context.expectEqual(state.petClass, .wellspring, "a new pet defaults to Wellspring")
        context.expectEqual(state.stats.vitality, PetStats.startingValue, "stats start at the baseline")
        context.expectEqual(state.stats.wit, PetStats.startingValue, "every stat starts at the baseline")
        context.expect(state.dailyXPBySource.isEmpty, "no XP has been granted today")
    }

    private static func checkCurveIsMonotonicAndUnbounded(context: inout CheckContext) {
        context.expectEqual(PetProgressionCurve.totalXPRequired(forLevel: 1), 0, "level 1 requires no XP")

        var previous = PetProgressionCurve.totalXPRequired(forLevel: 1)
        var isMonotonic = true
        for level in 2...30 {
            let required = PetProgressionCurve.totalXPRequired(forLevel: level)
            if required <= previous {
                isMonotonic = false
            }
            previous = required
        }
        context.expect(isMonotonic, "the XP curve strictly increases with no assumed ceiling")
    }

    private static func checkLevelDerivesFromTotalXP(context: inout CheckContext) {
        let levelTwoThreshold = PetProgressionCurve.totalXPRequired(forLevel: 2)

        context.expectEqual(
            PetProgressionCurve.level(forTotalXP: 0),
            1,
            "zero XP is level 1"
        )
        context.expectEqual(
            PetProgressionCurve.level(forTotalXP: levelTwoThreshold - 1),
            1,
            "just under the threshold stays at the prior level"
        )
        context.expectEqual(
            PetProgressionCurve.level(forTotalXP: levelTwoThreshold),
            2,
            "reaching the threshold advances the level"
        )
    }

    private static func checkConditionMultiplierFloorAndScale(context: inout CheckContext) {
        let full = PetNeeds(hunger: 0, energy: 100, happiness: 100, trust: 100)
        let empty = PetNeeds(hunger: 100, energy: 0, happiness: 0, trust: 0)
        let half = PetNeeds(hunger: 50, energy: 50, happiness: 50, trust: 50)

        context.expectApproximatelyEqual(full.xpMultiplier(floor: 0.2), 1.0, "full wellbeing earns full XP rate")
        context.expectApproximatelyEqual(empty.xpMultiplier(floor: 0.2), 0.2, "neglect floors, never zeroes, the rate")
        context.expectApproximatelyEqual(half.xpMultiplier(floor: 0.2), 0.5, "mid wellbeing earns a proportional rate")
    }

    private static func checkDailyWakeGrantsXP(context: inout CheckContext) {
        let brain = PetBrain()
        let state = fullHealthState()

        let response = brain.observe(SystemActivitySource.event(.dailyWake, at: start), on: state, at: start)

        context.expectApproximatelyEqual(
            response.state.totalXP,
            PetProgressionRates().dailyWakeXP,
            "dailyWake grants its full XP amount at full wellbeing"
        )
    }

    private static func checkCareActionGrantsXP(context: inout CheckContext) {
        let brain = PetBrain()
        let state = fullHealthState()

        let result = brain.perform(.pet, on: state, at: start)

        context.expectApproximatelyEqual(
            result.state.totalXP,
            PetProgressionRates().careActionXP,
            "a successful care action grants a small trickle of XP"
        )
    }

    private static func checkTooTiredToPlayGrantsNoXP(context: inout CheckContext) {
        let brain = PetBrain()
        var state = fullHealthState()
        state = PetState(
            schemaVersion: state.schemaVersion,
            name: state.name,
            family: state.family,
            needs: PetNeeds(hunger: 0, energy: 5, happiness: 100, trust: 100),
            preferences: state.preferences,
            lastUpdatedAt: state.lastUpdatedAt,
            totalXP: state.totalXP,
            petClass: state.petClass
        )

        let result = brain.perform(.play(.chase), on: state, at: start)

        context.expectEqual(result.reaction, .tooTiredToPlay, "an exhausted pet refuses to play")
        context.expectEqual(result.state.totalXP, 0, "a refused action never reaches the XP grant")
    }

    private static func checkWorkLoggedGrantsXP(context: inout CheckContext) {
        let brain = PetBrain()
        let state = fullHealthState()

        let response = brain.observe(ManualActivitySource.event(.workLogged, at: start), on: state, at: start)

        context.expectApproximatelyEqual(
            response.state.totalXP,
            PetProgressionRates().workLoggedXP,
            "logging work grants XP alongside its fixed Happiness gain"
        )
    }

    private static func checkFocusSessionBelowMinimumDurationGrantsNoXP(context: inout CheckContext) {
        let brain = PetBrain()
        let state = fullHealthState()
        let workingContext = ActivityContext.quiet.reducing(
            ManualActivitySource.event(.workStarted, at: start)
        )

        let shortSessionEnd = start.addingTimeInterval(5 * 60)
        let response = brain.observe(
            ManualActivitySource.event(.workEnded, at: shortSessionEnd),
            on: state,
            at: shortSessionEnd,
            context: workingContext
        )

        context.expectEqual(
            response.state.totalXP,
            0,
            "a focus session shorter than the minimum duration grants no XP"
        )
    }

    private static func checkFocusSessionAboveMinimumDurationGrantsXP(context: inout CheckContext) {
        let brain = PetBrain()
        let state = fullHealthState()
        let workingContext = ActivityContext.quiet.reducing(
            ManualActivitySource.event(.workStarted, at: start)
        )

        let longSessionEnd = start.addingTimeInterval(20 * 60)
        let response = brain.observe(
            ManualActivitySource.event(.workEnded, at: longSessionEnd),
            on: state,
            at: longSessionEnd,
            context: workingContext
        )

        context.expect(
            response.state.totalXP > 0,
            "a focus session past the minimum duration grants proportional XP"
        )
    }

    private static func checkPerSourceDailyCapBlocksFurtherGrants(context: inout CheckContext) {
        let rates = PetProgressionRates(careActionDailyCap: 5, overallDailyCap: 1_000)
        let brain = PetBrain(progressionRates: rates)
        var state = fullHealthState()
        var now = start

        for _ in 0..<10 {
            state = brain.perform(.pet, on: state, at: now).state
            now = now.addingTimeInterval(60)
        }

        context.expectApproximatelyEqual(
            state.totalXP,
            5,
            "repeated care actions stop granting XP once the per-source daily cap is reached"
        )
    }

    private static func checkOverallDailyCapBlocksGrantsAcrossSources(context: inout CheckContext) {
        let rates = PetProgressionRates(
            careActionDailyCap: 1_000,
            taskCompletedDailyCap: 1_000,
            overallDailyCap: 10
        )
        let brain = PetBrain(progressionRates: rates)
        var state = fullHealthState()

        state = brain.perform(.pet, on: state, at: start).state
        state = brain.observe(
            SystemActivitySource.event(.taskCompleted, at: start),
            on: state,
            at: start
        ).state

        context.expectApproximatelyEqual(
            state.totalXP,
            10,
            "the overall daily cap holds across different XP sources combined"
        )
    }

    private static func checkDailyCapsResetOnNewDay(context: inout CheckContext) {
        let rates = PetProgressionRates(careActionDailyCap: 5, overallDailyCap: 1_000)
        let brain = PetBrain(progressionRates: rates)
        var state = fullHealthState()

        state = brain.perform(.pet, on: state, at: start).state
        state = brain.perform(.pet, on: state, at: start.addingTimeInterval(60)).state
        let cappedXP = state.totalXP

        let nextDay = start.addingTimeInterval(24 * 3_600)
        state = brain.perform(.pet, on: state, at: nextDay).state

        context.expect(
            state.totalXP > cappedXP,
            "a new calendar day resets every source's daily XP bookkeeping"
        )
    }

    private static func checkLevelUpAppliesClassWeightedStatGrowth(context: inout CheckContext) {
        let levelTwoThreshold = PetProgressionCurve.totalXPRequired(forLevel: 2)
        let rates = PetProgressionRates(
            dailyWakeXP: levelTwoThreshold,
            overallDailyCap: levelTwoThreshold * 2,
            signatureStatGainPerLevel: 3,
            otherStatGainPerLevel: 1
        )
        let brain = PetBrain(progressionRates: rates)
        let state = fullHealthState(petClass: .tinkerer)

        let response = brain.observe(SystemActivitySource.event(.dailyWake, at: start), on: state, at: start)

        context.expectEqual(response.state.level, 2, "enough XP in one grant advances the level")
        context.expectEqual(
            response.state.stats.wit,
            PetStats.startingValue + 3,
            "the class's signature stat grows fastest on level-up"
        )
        context.expectEqual(
            response.state.stats.power,
            PetStats.startingValue + 1,
            "every other stat still grows, just slower"
        )
    }

    private static func checkSelectingClassPreservesEverythingElse(context: inout CheckContext) {
        let state = fullHealthState(totalXP: 500, petClass: .wellspring)
        let switched = state.selectingClass(.juggernaut)

        context.expectEqual(switched.petClass, .juggernaut, "class selection changes the mechanical identity")
        context.expectEqual(switched.totalXP, state.totalXP, "class selection preserves earned XP")
        context.expectEqual(switched.stats, state.stats, "class selection never retroactively changes stats")
        context.expectEqual(switched.needs, state.needs, "class selection preserves needs")
    }

    private static func checkFamilySelectionPreservesProgression(context: inout CheckContext) {
        let state = fullHealthState(totalXP: 500)
        let switched = state.selectingFamily(.elemental)

        context.expectEqual(switched.totalXP, state.totalXP, "family selection preserves earned XP")
        context.expectEqual(switched.petClass, state.petClass, "family selection preserves class")
        context.expectEqual(switched.stats, state.stats, "family selection preserves stats")
    }

    private static func checkRenamingPreservesProgression(context: inout CheckContext) {
        let state = fullHealthState(totalXP: 500)
        let renamed = state.renamed(to: "Ember")

        context.expectEqual(renamed.totalXP, state.totalXP, "renaming preserves earned XP")
        context.expectEqual(renamed.petClass, state.petClass, "renaming preserves class")
        context.expectEqual(renamed.stats, state.stats, "renaming preserves stats")
    }
}
