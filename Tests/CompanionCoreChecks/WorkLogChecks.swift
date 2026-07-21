import CompanionCore
import Foundation

enum WorkLogChecks {
    static func run(context: inout CheckContext) {
        checkNewPetCanLogWork(context: &context)
        checkLoggingWorkAppliesFixedGain(context: &context)
        checkCooldownBlocksImmediateRelog(context: &context)
        checkCooldownClearsAfterInterval(context: &context)
        checkDailyCapBlocksFurtherLogging(context: &context)
        checkDailyCapResetsOnNewDay(context: &context)
        checkFamilySelectionPreservesWorkLogState(context: &context)
        checkRenamingPreservesWorkLogState(context: &context)
        checkManualSourceId(context: &context)
        checkWorkLoggedContextIsNeutral(context: &context)
    }

    private static let start = Date(timeIntervalSinceReferenceDate: 20_000)

    private static func tightRates() -> PetSimulationRates {
        PetSimulationRates(workLogCooldownMinutes: 10, workLogDailyCap: 2)
    }

    private static func checkNewPetCanLogWork(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        context.expect(
            brain.workLogAvailability(state: state, at: start).isEnabled,
            "a fresh pet can log work immediately"
        )
    }

    private static func checkLoggingWorkAppliesFixedGain(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        let response = brain.observe(
            ManualActivitySource.event(.workLogged, at: start),
            on: state,
            at: start
        )

        context.expectEqual(response.reaction, .loggedWork, "logging work is acknowledged")
        context.expectApproximatelyEqual(
            response.state.needs.happiness,
            73,
            "logging work grants the fixed happiness gain"
        )
        context.expectEqual(
            response.state.lastWorkLogAt,
            start,
            "logging work records when it happened"
        )
        context.expectEqual(
            response.state.workLogCountToday,
            1,
            "logging work counts toward today's tally"
        )
    }

    private static func checkCooldownBlocksImmediateRelog(context: inout CheckContext) {
        let brain = PetBrain(rates: tightRates())
        let state = PetState.newPet(now: start)

        let logged = brain.observe(
            ManualActivitySource.event(.workLogged, at: start),
            on: state,
            at: start
        ).state
        let availability = brain.workLogAvailability(state: logged, at: start.addingTimeInterval(60))

        context.expect(!availability.isEnabled, "logging again immediately is blocked by the cooldown")
        context.expect(availability.explanation != nil, "the cooldown explains itself")
    }

    private static func checkCooldownClearsAfterInterval(context: inout CheckContext) {
        let brain = PetBrain(rates: tightRates())
        let state = PetState.newPet(now: start)

        let logged = brain.observe(
            ManualActivitySource.event(.workLogged, at: start),
            on: state,
            at: start
        ).state
        let availability = brain.workLogAvailability(
            state: logged,
            at: start.addingTimeInterval(11 * 60)
        )

        context.expect(availability.isEnabled, "logging becomes available again once the cooldown passes")
    }

    private static func checkDailyCapBlocksFurtherLogging(context: inout CheckContext) {
        let brain = PetBrain(rates: tightRates())
        var state = PetState.newPet(now: start)
        var now = start

        for _ in 0..<2 {
            state = brain.observe(
                ManualActivitySource.event(.workLogged, at: now),
                on: state,
                at: now
            ).state
            now = now.addingTimeInterval(11 * 60)
        }

        context.expectEqual(state.workLogCountToday, 2, "two credited logs reach the tight test cap")
        context.expect(
            !brain.workLogAvailability(state: state, at: now).isEnabled,
            "the daily cap blocks logging after enough credited entries"
        )
    }

    private static func checkDailyCapResetsOnNewDay(context: inout CheckContext) {
        let brain = PetBrain(rates: tightRates())
        var state = PetState.newPet(now: start)
        var now = start

        for _ in 0..<2 {
            state = brain.observe(
                ManualActivitySource.event(.workLogged, at: now),
                on: state,
                at: now
            ).state
            now = now.addingTimeInterval(11 * 60)
        }

        let nextDay = now.addingTimeInterval(24 * 3_600)
        context.expect(
            brain.workLogAvailability(state: state, at: nextDay).isEnabled,
            "a new calendar day resets the daily cap"
        )
    }

    private static func checkFamilySelectionPreservesWorkLogState(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)
        let logged = brain.observe(
            ManualActivitySource.event(.workLogged, at: start),
            on: state,
            at: start
        ).state

        let switched = logged.selectingFamily(.elemental)

        context.expectEqual(
            switched.lastWorkLogAt,
            logged.lastWorkLogAt,
            "switching family preserves the work log cooldown timestamp"
        )
        context.expectEqual(
            switched.workLogCountToday,
            logged.workLogCountToday,
            "switching family preserves today's work log count"
        )
    }

    private static func checkRenamingPreservesWorkLogState(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)
        let logged = brain.observe(
            ManualActivitySource.event(.workLogged, at: start),
            on: state,
            at: start
        ).state

        let renamed = logged.renamed(to: "Ember")

        context.expectEqual(
            renamed.lastWorkLogAt,
            logged.lastWorkLogAt,
            "renaming preserves the work log cooldown timestamp"
        )
        context.expectEqual(
            renamed.workLogCountToday,
            logged.workLogCountToday,
            "renaming preserves today's work log count"
        )
    }

    private static func checkManualSourceId(context: inout CheckContext) {
        let event = ManualActivitySource.event(.workLogged, at: start)
        context.expectEqual(event.sourceId, "manual", "manual events carry the manual source id")
    }

    private static func checkWorkLoggedContextIsNeutral(context: inout CheckContext) {
        let working = ActivityContext.quiet.reducing(
            SimulatedActivitySource.event(.workStarted, at: start)
        )
        let afterLog = working.reducing(
            ManualActivitySource.event(.workLogged, at: start.addingTimeInterval(60))
        )

        context.expect(afterLog.isWorking, "logging work does not end an open work block")
        context.expectEqual(
            afterLog.lastEventAt,
            start.addingTimeInterval(60),
            "logging work refreshes the last-event timestamp"
        )
    }
}
