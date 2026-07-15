import CompanionCore
import CoreGraphics

enum ScreenPlacementChecks {
    static func run(context: inout CheckContext) {
        checkDefaultOrigin(context: &context)
        checkClampedOrigin(context: &context)
        checkOversizedWindow(context: &context)
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
}
