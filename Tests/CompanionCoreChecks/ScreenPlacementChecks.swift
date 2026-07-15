import CompanionCore
import CoreGraphics
import Darwin

@main
enum ScreenPlacementChecks {
    static func main() {
        var failures: [String] = []

        checkDefaultOrigin(failures: &failures)
        checkClampedOrigin(failures: &failures)
        checkOversizedWindow(failures: &failures)

        guard failures.isEmpty else {
            for failure in failures {
                fputs("FAIL: \(failure)\n", stderr)
            }
            exit(EXIT_FAILURE)
        }

        print("ScreenPlacement checks passed (3/3)")
    }

    private static func checkDefaultOrigin(failures: inout [String]) {
        let actual = ScreenPlacement.defaultOrigin(
            windowSize: CGSize(width: 100, height: 80),
            visibleFrame: CGRect(x: 0, y: 40, width: 1_000, height: 700),
            margin: 20
        )

        expect(
            actual,
            equals: CGPoint(x: 880, y: 60),
            scenario: "default origin uses the lower-right visible area",
            failures: &failures
        )
    }

    private static func checkClampedOrigin(failures: inout [String]) {
        let actual = ScreenPlacement.clampedOrigin(
            proposed: CGPoint(x: 900, y: -100),
            windowSize: CGSize(width: 180, height: 140),
            visibleFrame: CGRect(x: 50, y: 30, width: 800, height: 600),
            margin: 10
        )

        expect(
            actual,
            equals: CGPoint(x: 660, y: 40),
            scenario: "proposed origin is clamped inside the visible area",
            failures: &failures
        )
    }

    private static func checkOversizedWindow(failures: inout [String]) {
        let actual = ScreenPlacement.clampedOrigin(
            proposed: CGPoint(x: 500, y: 500),
            windowSize: CGSize(width: 600, height: 600),
            visibleFrame: CGRect(x: 100, y: 50, width: 400, height: 300),
            margin: 12
        )

        expect(
            actual,
            equals: CGPoint(x: 112, y: 62),
            scenario: "oversized window uses the minimum safe origin",
            failures: &failures
        )
    }

    private static func expect(
        _ actual: CGPoint,
        equals expected: CGPoint,
        scenario: String,
        failures: inout [String]
    ) {
        guard actual != expected else {
            return
        }

        failures.append("\(scenario); expected \(expected), received \(actual)")
    }
}
