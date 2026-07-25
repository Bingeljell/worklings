import CompanionCore
import Foundation

enum ActivitySourceChecks {
    static func run(context: inout CheckContext) {
        checkSystemSourceId(context: &context)
        checkDailyWakeFirstLaunch(context: &context)
        checkDailyWakeSameDay(context: &context)
        checkDailyWakeNextDay(context: &context)
        checkDailyWakeAcrossMidnightBoundary(context: &context)
        checkPresenceGoesIdle(context: &context)
        checkPresenceStaysIdle(context: &context)
        checkPresenceReturns(context: &context)
        checkPresenceStaysPresent(context: &context)
        checkGitSourceId(context: &context)
        checkGitForwardCommitEmitsPerCommit(context: &context)
        checkGitFreshConnectEmitsNothing(context: &context)
        checkGitUnchangedHeadEmitsNothing(context: &context)
        checkGitHistoryRewriteEmitsNothing(context: &context)
    }

    private static var utcCalendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }

    private static func checkSystemSourceId(context: inout CheckContext) {
        let event = SystemActivitySource.event(.dailyWake, at: Date(timeIntervalSinceReferenceDate: 0))
        context.expectEqual(event.sourceId, "system", "real events carry the system source id")
    }

    private static func checkDailyWakeFirstLaunch(context: inout CheckContext) {
        let now = Date(timeIntervalSinceReferenceDate: 100_000)
        context.expect(
            DailyWakeTracker.shouldWake(lastWakeAt: nil, now: now, calendar: utcCalendar),
            "a pet with no recorded wake always wakes"
        )
    }

    private static func checkDailyWakeSameDay(context: inout CheckContext) {
        let calendar = utcCalendar
        let morning = calendar.date(from: DateComponents(year: 2026, month: 7, day: 20, hour: 9))!
        let evening = calendar.date(from: DateComponents(year: 2026, month: 7, day: 20, hour: 22))!

        context.expect(
            !DailyWakeTracker.shouldWake(lastWakeAt: morning, now: evening, calendar: calendar),
            "a second launch on the same day does not wake again"
        )
    }

    private static func checkDailyWakeNextDay(context: inout CheckContext) {
        let calendar = utcCalendar
        let yesterday = calendar.date(from: DateComponents(year: 2026, month: 7, day: 20, hour: 22))!
        let today = calendar.date(from: DateComponents(year: 2026, month: 7, day: 21, hour: 8))!

        context.expect(
            DailyWakeTracker.shouldWake(lastWakeAt: yesterday, now: today, calendar: calendar),
            "the first launch on a new calendar day wakes again"
        )
    }

    private static func checkDailyWakeAcrossMidnightBoundary(context: inout CheckContext) {
        let calendar = utcCalendar
        let beforeMidnight = calendar.date(from: DateComponents(year: 2026, month: 7, day: 20, hour: 23, minute: 59))!
        let afterMidnight = calendar.date(from: DateComponents(year: 2026, month: 7, day: 21, hour: 0, minute: 1))!

        context.expect(
            DailyWakeTracker.shouldWake(lastWakeAt: beforeMidnight, now: afterMidnight, calendar: calendar),
            "a session running past midnight wakes on the next tick after the boundary"
        )
    }

    private static func checkPresenceGoesIdle(context: inout CheckContext) {
        context.expectEqual(
            PresenceEvaluator.signal(idleSeconds: 301, wasIdle: false),
            .wentIdle,
            "crossing the idle threshold while present signals wentIdle"
        )
        context.expectEqual(
            PresenceEvaluator.signal(idleSeconds: 299, wasIdle: false),
            nil,
            "staying under the idle threshold emits nothing"
        )
    }

    private static func checkPresenceStaysIdle(context: inout CheckContext) {
        context.expectEqual(
            PresenceEvaluator.signal(idleSeconds: 600, wasIdle: true),
            .stillIdle,
            "remaining idle signals stillIdle so the absence can be kept alive"
        )
    }

    private static func checkPresenceReturns(context: inout CheckContext) {
        context.expectEqual(
            PresenceEvaluator.signal(idleSeconds: 2, wasIdle: true),
            .returned,
            "dropping below the idle threshold while away signals returned"
        )
    }

    private static func checkPresenceStaysPresent(context: inout CheckContext) {
        context.expectEqual(
            PresenceEvaluator.signal(idleSeconds: 0, wasIdle: false),
            nil,
            "staying present emits nothing"
        )
    }

    private static func checkGitSourceId(context: inout CheckContext) {
        let event = GitActivitySource.event(.milestone, at: Date(timeIntervalSinceReferenceDate: 0))
        context.expectEqual(event.sourceId, "git", "commits carry the git source id")
        context.expectEqual(event.kind, .milestone, "a commit surfaces as a milestone")
    }

    private static func checkGitForwardCommitEmitsPerCommit(context: inout CheckContext) {
        context.expectEqual(
            GitCommitDelta.milestonesToEmit(
                oldSHA: "aaa", newSHA: "bbb", oldIsAncestorOfNew: true, commitsAhead: 1
            ),
            1,
            "a single new commit on top of the last-seen HEAD emits one milestone"
        )
        context.expectEqual(
            GitCommitDelta.milestonesToEmit(
                oldSHA: "aaa", newSHA: "bbb", oldIsAncestorOfNew: true, commitsAhead: 5
            ),
            5,
            "a batch landed at once is credited per commit"
        )
    }

    private static func checkGitFreshConnectEmitsNothing(context: inout CheckContext) {
        context.expectEqual(
            GitCommitDelta.milestonesToEmit(
                oldSHA: nil, newSHA: "bbb", oldIsAncestorOfNew: false, commitsAhead: 99
            ),
            0,
            "a freshly connected repo with history behind it grants nothing (no retro-credit)"
        )
    }

    private static func checkGitUnchangedHeadEmitsNothing(context: inout CheckContext) {
        context.expectEqual(
            GitCommitDelta.milestonesToEmit(
                oldSHA: "aaa", newSHA: "aaa", oldIsAncestorOfNew: true, commitsAhead: 0
            ),
            0,
            "an unchanged HEAD (e.g. a non-commit .git write) emits nothing"
        )
    }

    private static func checkGitHistoryRewriteEmitsNothing(context: inout CheckContext) {
        context.expectEqual(
            GitCommitDelta.milestonesToEmit(
                oldSHA: "aaa", newSHA: "ccc", oldIsAncestorOfNew: false, commitsAhead: 1
            ),
            0,
            "an amend/reset/rebase that rewrites rather than advances history emits nothing"
        )
    }
}
