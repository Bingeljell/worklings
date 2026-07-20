import CompanionCore
import Foundation

enum ActivityChecks {
    static func run(context: inout CheckContext) {
        checkContextReduction(context: &context)
        checkContextExpiry(context: &context)
        checkWorkingDrain(context: &context)
        checkAwayTrustDrain(context: &context)
        checkUserReturnedStopsAwayDrain(context: &context)
        checkTaskCompletedCelebration(context: &context)
        checkTaskFailedSetback(context: &context)
        checkMilestonePride(context: &context)
        checkDailyWakeGreeting(context: &context)
        checkStructuralEventsStayQuiet(context: &context)
        checkUserReturnedReactionOnly(context: &context)
        checkSimulatedScriptDeterminism(context: &context)
    }

    private static let start = Date(timeIntervalSinceReferenceDate: 10_000)

    private static func event(_ kind: ActivityEventKind, offsetMinutes: Double = 0) -> ActivityEvent {
        SimulatedActivitySource.event(kind, at: start.addingTimeInterval(offsetMinutes * 60))
    }

    private static func checkContextReduction(context: inout CheckContext) {
        let working = ActivityContext.quiet.reducing(event(.workStarted))
        context.expect(working.isWorking, "workStarted marks the context as working")
        context.expectEqual(working.lastEventAt, start, "reduction records the event time")

        let awaiting = working.reducing(event(.awaitingInput, offsetMinutes: 1))
        context.expect(awaiting.isAwaitingInput, "awaitingInput marks the agent as blocked")
        context.expect(awaiting.isWorking, "awaitingInput keeps the work block open")

        let resolved = awaiting.reducing(event(.taskCompleted, offsetMinutes: 2))
        context.expect(!resolved.isAwaitingInput, "taskCompleted clears awaiting input")
        context.expect(resolved.isWorking, "taskCompleted keeps the work block open")

        let ended = resolved.reducing(event(.workEnded, offsetMinutes: 3))
        context.expect(!ended.isWorking, "workEnded closes the work block")
        context.expect(!ended.isAwaitingInput, "workEnded clears awaiting input")

        let away = ended.reducing(event(.userIdle, offsetMinutes: 4))
        context.expect(!away.isUserPresent, "userIdle marks the user as away")

        let back = away.reducing(event(.userReturned, offsetMinutes: 5))
        context.expect(back.isUserPresent, "userReturned marks the user as present")
    }

    private static func checkContextExpiry(context: inout CheckContext) {
        let working = ActivityContext.quiet.reducing(event(.workStarted))

        let fresh = working.expiring(at: start.addingTimeInterval(29 * 60))
        context.expectEqual(fresh, working, "a recent context survives expiry")

        let stale = working.expiring(at: start.addingTimeInterval(31 * 60))
        context.expectEqual(stale, .quiet, "a stale context resets to quiet")

        context.expectEqual(
            ActivityContext.quiet.expiring(at: start),
            .quiet,
            "the quiet context stays quiet"
        )
    }

    private static func checkWorkingDrain(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)
        let later = start.addingTimeInterval(2 * 3_600)
        let workingContext = ActivityContext.quiet.reducing(event(.workStarted))

        let quiet = brain.advance(state, to: later)
        let working = brain.advance(state, to: later, context: workingContext)

        context.expectApproximatelyEqual(
            quiet.needs.hunger,
            23,
            "quiet hours drain fullness at the baseline rate"
        )
        context.expectApproximatelyEqual(
            working.needs.hunger,
            25,
            "working hours drain fullness faster"
        )
        context.expectApproximatelyEqual(
            quiet.needs.energy,
            74,
            "quiet hours drain energy at the baseline rate"
        )
        context.expectApproximatelyEqual(
            working.needs.energy,
            72.2,
            "working hours drain energy faster too"
        )
    }

    private static func checkAwayTrustDrain(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)
        let later = start.addingTimeInterval(2 * 3_600)
        let awayContext = ActivityContext.quiet.reducing(event(.userIdle))

        let present = brain.advance(state, to: later)
        let away = brain.advance(state, to: later, context: awayContext)

        context.expectApproximatelyEqual(
            present.needs.trust,
            50,
            "trust holds steady while the user is present"
        )
        context.expectApproximatelyEqual(
            away.needs.trust,
            46,
            "trust drains while the user is away"
        )
    }

    private static func checkUserReturnedStopsAwayDrain(context: inout CheckContext) {
        let brain = PetBrain()
        let awayStart = PetState.newPet(now: start)
        let userReturns = start.addingTimeInterval(1 * 3_600)
        let awayContext = ActivityContext.quiet.reducing(event(.userIdle))

        let afterAway = brain.advance(awayStart, to: userReturns, context: awayContext)
        let returnedContext = awayContext.reducing(event(.userReturned, offsetMinutes: 60))

        let laterStillPresent = userReturns.addingTimeInterval(1 * 3_600)
        let afterReturn = brain.advance(afterAway, to: laterStillPresent, context: returnedContext)

        context.expectApproximatelyEqual(
            afterAway.needs.trust,
            48,
            "one away hour drains trust once"
        )
        context.expectApproximatelyEqual(
            afterReturn.needs.trust,
            48,
            "trust stops draining once the user returns"
        )
    }

    private static func checkTaskCompletedCelebration(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        let response = brain.observe(event(.taskCompleted), on: state, at: start)

        context.expectEqual(response.reaction, .celebratedTask, "a completed task is celebrated")
        context.expectApproximatelyEqual(
            response.state.needs.happiness,
            74,
            "a completed task lifts happiness"
        )
        context.expectEqual(
            response.state.needs.hunger,
            state.needs.hunger,
            "a completed task does not feed the pet"
        )
    }

    private static func checkTaskFailedSetback(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        let response = brain.observe(event(.taskFailed), on: state, at: start)

        context.expectEqual(response.reaction, .sharedSetback, "a failed task is shared, not ignored")
        context.expectApproximatelyEqual(
            response.state.needs.happiness,
            67,
            "a failed task dents happiness slightly"
        )
        context.expectApproximatelyEqual(
            response.state.needs.hunger,
            19,
            "a failed task costs a little fullness too"
        )
        context.expectApproximatelyEqual(
            response.state.needs.energy,
            77,
            "a failed task costs a little energy too"
        )

        let low = PetState(
            name: "Pixel",
            needs: PetNeeds(hunger: 15, energy: 80, happiness: 1, trust: 50),
            preferences: state.preferences,
            lastUpdatedAt: start
        )
        let clamped = brain.observe(event(.taskFailed), on: low, at: start)
        context.expectEqual(
            clamped.state.needs.happiness,
            0,
            "setback happiness clamps at zero"
        )
    }

    private static func checkMilestonePride(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        let response = brain.observe(event(.milestone), on: state, at: start)

        context.expectEqual(response.reaction, .proudOfMilestone, "a milestone earns pride")
        context.expectApproximatelyEqual(
            response.state.needs.happiness,
            76,
            "a milestone lifts happiness strongly"
        )
        context.expectApproximatelyEqual(
            response.state.needs.trust,
            52,
            "a milestone builds trust"
        )
    }

    private static func checkDailyWakeGreeting(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        let response = brain.observe(event(.dailyWake), on: state, at: start)

        context.expectEqual(response.reaction, .happyToSeeYou, "the first wake of a day is greeted")
        context.expectApproximatelyEqual(
            response.state.needs.happiness,
            73,
            "a daily wake lifts happiness"
        )
        context.expectApproximatelyEqual(
            response.state.needs.trust,
            51,
            "a daily wake builds a little trust"
        )
    }

    private static func checkStructuralEventsStayQuiet(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        for kind in [ActivityEventKind.workStarted, .workEnded, .awaitingInput, .userIdle] {
            let response = brain.observe(event(kind), on: state, at: start)
            context.expectEqual(
                response.reaction,
                nil,
                "\(kind.rawValue) produces no visible reaction"
            )
            context.expectEqual(
                response.state.needs,
                state.needs,
                "\(kind.rawValue) does not move needs"
            )
        }
    }

    private static func checkUserReturnedReactionOnly(context: inout CheckContext) {
        let brain = PetBrain()
        let state = PetState.newPet(now: start)

        let response = brain.observe(event(.userReturned), on: state, at: start)

        context.expectEqual(response.reaction, .gladYouAreBack, "a returning user is welcomed")
        context.expectEqual(
            response.state.needs,
            state.needs,
            "a welcome does not move needs, so presence cannot be farmed"
        )
    }

    private static func checkSimulatedScriptDeterminism(context: inout CheckContext) {
        let first = SimulatedActivitySource.demoScript(startingAt: start)
        let second = SimulatedActivitySource.demoScript(startingAt: start)

        context.expectEqual(first, second, "the demo script is deterministic")
        context.expect(!first.isEmpty, "the demo script emits events")
        context.expect(
            zip(first, first.dropFirst()).allSatisfy { $0.timestamp <= $1.timestamp },
            "demo script timestamps never move backward"
        )
        context.expect(
            first.allSatisfy { $0.sourceId == SimulatedActivitySource.sourceId },
            "demo script events carry the simulated source id"
        )
    }
}
