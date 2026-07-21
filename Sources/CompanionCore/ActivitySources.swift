import Foundation

/// Real activity events, tagged distinctly from `SimulatedActivitySource` so a
/// live session and a debug rehearsal are never confused in the event stream.
public enum SystemActivitySource {
    public static let sourceId = "system"

    public static func event(_ kind: ActivityEventKind, at timestamp: Date) -> ActivityEvent {
        ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId)
    }
}

/// Events the user explicitly logs by hand — self-reported, and therefore
/// tagged distinctly from externally verifiable sources so fairness rules
/// can treat them differently later.
public enum ManualActivitySource {
    public static let sourceId = "manual"

    public static func event(_ kind: ActivityEventKind, at timestamp: Date) -> ActivityEvent {
        ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId)
    }
}

/// Decides whether the first interaction of a new calendar day has happened,
/// independent of how many times the app has launched that day. The caller
/// owns persisting `lastWakeAt`; this function only makes the determination.
public enum DailyWakeTracker {
    public static func shouldWake(
        lastWakeAt: Date?,
        now: Date,
        calendar: Calendar = .current
    ) -> Bool {
        guard let lastWakeAt else {
            return true
        }
        return !calendar.isDate(lastWakeAt, inSameDayAs: now)
    }
}

/// What a presence poll should do: fire a one-time transition event, keep an
/// ongoing absence alive without repeating its reaction, or nothing.
public enum PresenceSignal: Equatable, Sendable {
    case wentIdle
    case stillIdle
    case returned
}

/// Turns raw system idle seconds into a presence signal. Pure and
/// deterministic so the threshold crossing is testable without a real clock
/// or real input events; the caller owns polling and remembering `wasIdle`.
public enum PresenceEvaluator {
    public static let defaultIdleThreshold: TimeInterval = 5 * 60

    public static func signal(
        idleSeconds: TimeInterval,
        wasIdle: Bool,
        threshold: TimeInterval = PresenceEvaluator.defaultIdleThreshold
    ) -> PresenceSignal? {
        if idleSeconds >= threshold {
            return wasIdle ? .stillIdle : .wentIdle
        }
        return wasIdle ? .returned : nil
    }
}
