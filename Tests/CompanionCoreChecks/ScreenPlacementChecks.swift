import CompanionCore
import CoreGraphics

enum ScreenPlacementChecks {
    static func run(context: inout CheckContext) {
        checkDefaultOrigin(context: &context)
        checkClampedOrigin(context: &context)
        checkOversizedWindow(context: &context)
        checkRoamingIntent(context: &context)
        checkRoamingOrigin(context: &context)
        checkRoamingReflectsAtEdge(context: &context)
    }

    private static func checkDefaultOrigin(context: inout CheckContext) {
        let actual = ScreenPlacement.defaultOrigin(
            windowSize: CGSize(width: 100, height: 80),
            visibleFrame: CGRect(x: 0, y: 40, width: 1_000, height: 700),
            margin: 20
        )

        context.expectEqual(
            actual,
            CGPoint(x: 880, y: 60),
            "default origin uses the lower-right visible area"
        )
    }

    private static func checkClampedOrigin(context: inout CheckContext) {
        let actual = ScreenPlacement.clampedOrigin(
            proposed: CGPoint(x: 900, y: -100),
            windowSize: CGSize(width: 180, height: 140),
            visibleFrame: CGRect(x: 50, y: 30, width: 800, height: 600),
            margin: 10
        )

        context.expectEqual(
            actual,
            CGPoint(x: 660, y: 40),
            "proposed origin is clamped inside the visible area"
        )
    }

    private static func checkOversizedWindow(context: inout CheckContext) {
        let actual = ScreenPlacement.clampedOrigin(
            proposed: CGPoint(x: 500, y: 500),
            windowSize: CGSize(width: 600, height: 600),
            visibleFrame: CGRect(x: 100, y: 50, width: 400, height: 300),
            margin: 12
        )

        context.expectEqual(
            actual,
            CGPoint(x: 112, y: 62),
            "oversized window uses the minimum safe origin"
        )
    }

    private static func checkRoamingIntent(context: inout CheckContext) {
        let first = PetRoamingPlanner.intent(sequenceNumber: 0)
        let repeated = PetRoamingPlanner.intent(sequenceNumber: 4)

        context.expectEqual(first, repeated, "roaming pattern repeats deterministically")
        context.expectEqual(first.horizontalOffset, -0.24, "first roaming step moves left")
        context.expectEqual(first.verticalOffset, 0, "first roaming step stays level")
        context.expectEqual(first.restDuration, 7, "first roaming step rests before moving")
        context.expectEqual(first.travelDuration, 2.8, "first roaming step has travel timing")
    }

    private static func checkRoamingOrigin(context: inout CheckContext) {
        let actual = ScreenPlacement.roamingOrigin(
            from: CGPoint(x: 600, y: 300),
            intent: PetRoamingIntent(
                horizontalOffset: 0.25,
                verticalOffset: -0.1,
                restDuration: 8,
                travelDuration: 2
            ),
            windowSize: CGSize(width: 200, height: 200),
            visibleFrame: CGRect(x: 0, y: 0, width: 1_200, height: 800),
            margin: 20
        )

        context.expectEqual(
            actual,
            CGPoint(x: 840, y: 244),
            "roaming offsets scale to the available visible frame"
        )
    }

    private static func checkRoamingReflectsAtEdge(context: inout CheckContext) {
        let actual = ScreenPlacement.roamingOrigin(
            from: CGPoint(x: 780, y: 120),
            intent: PetRoamingIntent(
                horizontalOffset: 0.2,
                verticalOffset: 0,
                restDuration: 8,
                travelDuration: 2
            ),
            windowSize: CGSize(width: 200, height: 200),
            visibleFrame: CGRect(x: 0, y: 0, width: 1_000, height: 700),
            margin: 20
        )

        context.expectEqual(
            actual,
            CGPoint(x: 628, y: 120),
            "roaming reflects inward when the requested direction meets an edge"
        )
    }
}
