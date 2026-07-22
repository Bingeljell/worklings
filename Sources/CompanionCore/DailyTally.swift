import Foundation

/// A value that is only meaningful for the calendar day named by `date`.
///
/// This is the one place the "valid only today, ignored when stale" pattern
/// lives — Log Work's daily count and the per-source daily XP ledger both use
/// it instead of each hand-rolling a paired value/date and its own same-day
/// check. A stale tally is never proactively reset in storage; callers read
/// through `current(on:default:)`, which returns the default once the stored
/// day has passed, so the save needs no day-rollover side effect.
public struct DailyTally<Value: Codable & Equatable & Sendable>: Codable, Equatable, Sendable {
    public let date: Date?
    public let value: Value

    public init(date: Date? = nil, value: Value) {
        self.date = date
        self.value = value
    }

    /// The stored value if `date` names the same calendar day as `day`,
    /// otherwise `fallback`. `day` is the reference day — usually "now", but
    /// the XP ledger passes the day an event actually happened so a backlogged
    /// event books into its own day rather than the day it was delivered.
    public func current(
        on day: Date,
        default fallback: Value,
        calendar: Calendar = .current
    ) -> Value {
        guard let date, calendar.isDate(date, inSameDayAs: day) else {
            return fallback
        }
        return value
    }
}
