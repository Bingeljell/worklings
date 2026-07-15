import CompanionCore
import Foundation

enum PetCareStatusChecks {
    static func run(context: inout CheckContext) {
        checkHealthySummary(context: &context)
        checkThresholds(context: &context)
        checkPriorityAndLimit(context: &context)
        checkAmbientUrgency(context: &context)
        checkActionAvailability(context: &context)
    }

    private static func checkHealthySummary(context: inout CheckContext) {
        let status = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 20, energy: 80, happiness: 80, trust: 70)
            )
        )

        context.expectEqual(status.conditions.count, 0, "healthy needs produce no conditions")
        context.expectEqual(status.hoverSummary, "Pixel is happy.", "healthy hover summary is positive")
        context.expectEqual(status.ambientCondition, nil, "healthy state has no ambient condition")
    }

    private static func checkThresholds(context: inout CheckContext) {
        let notice = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 55, energy: 46, happiness: 46, trust: 36)
            )
        )
        let urgent = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 75, energy: 80, happiness: 70, trust: 50)
            )
        )
        let critical = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 20, energy: 10, happiness: 70, trust: 50)
            )
        )

        context.expectEqual(notice.conditions.first?.urgency, .notice, "notice boundary is inclusive")
        context.expectEqual(urgent.conditions.first?.urgency, .urgent, "urgent boundary is inclusive")
        context.expectEqual(critical.conditions.first?.urgency, .critical, "critical boundary is inclusive")
    }

    private static func checkPriorityAndLimit(context: inout CheckContext) {
        let status = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 80, energy: 5, happiness: 5, trust: 5)
            )
        )

        context.expectEqual(status.conditions.count, 4, "status retains all active conditions")
        context.expectEqual(status.conditions[0].kind, .energy, "critical physical need ranks first")
        context.expectEqual(status.conditions[1].kind, .trust, "critical relationship need ranks second")
        context.expectEqual(
            status.hoverSummary,
            "Pixel is exhausted and in need of reassurance.",
            "hover summary reports only the top two conditions"
        )
    }

    private static func checkAmbientUrgency(context: inout CheckContext) {
        let noticeOnly = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 60, energy: 80, happiness: 70, trust: 50)
            )
        )
        let urgent = PetCareStatus.make(
            state: makeState(
                needs: PetNeeds(hunger: 80, energy: 80, happiness: 70, trust: 50)
            )
        )

        context.expectEqual(noticeOnly.ambientCondition, nil, "notice does not create ambient alert")
        context.expectEqual(urgent.ambientCondition?.kind, .hunger, "urgent need becomes ambient alert")
    }

    private static func checkActionAvailability(context: inout CheckContext) {
        let fullState = makeState(
            needs: PetNeeds(hunger: 0, energy: 100, happiness: 70, trust: 50)
        )
        let tiredState = makeState(
            needs: PetNeeds(hunger: 40, energy: 10, happiness: 70, trust: 50)
        )

        let fullStatus = PetCareStatus.make(state: fullState)
        let tiredStatus = PetCareStatus.make(state: tiredState)

        context.expectEqual(
            fullStatus.availability(for: .feed, state: fullState),
            PetActionAvailability(isEnabled: false, explanation: "Pixel is already full."),
            "feed is disabled when hunger is zero"
        )
        context.expectEqual(
            fullStatus.availability(for: .sleep, state: fullState),
            PetActionAvailability(isEnabled: false, explanation: "Pixel is fully rested."),
            "sleep is disabled at full energy"
        )
        context.expectEqual(
            tiredStatus.availability(for: .play, state: tiredState),
            PetActionAvailability(isEnabled: false, explanation: "Pixel needs a nap first."),
            "play is disabled below fifteen energy"
        )
        context.expect(
            tiredStatus.availability(for: .pet, state: tiredState).isEnabled,
            "pet remains available"
        )
    }

    private static func makeState(needs: PetNeeds) -> PetState {
        PetState(
            name: "Pixel",
            needs: needs,
            preferences: PetPreferences(
                favouriteFood: .berries,
                favouritePlayActivity: .puzzle
            ),
            lastUpdatedAt: Date(timeIntervalSinceReferenceDate: 0)
        )
    }
}
