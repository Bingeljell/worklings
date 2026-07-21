import Foundation

public enum ActivityEventKind: String, CaseIterable, Codable, Equatable, Sendable {
    case dailyWake
    case workStarted
    case workEnded
    case taskCompleted
    case taskFailed
    case awaitingInput
    case milestone
    case userIdle
    case userReturned
    case workLogged

    public var displayName: String {
        switch self {
        case .dailyWake: "Daily Wake"
        case .workStarted: "Work Started"
        case .workEnded: "Work Ended"
        case .taskCompleted: "Task Completed"
        case .taskFailed: "Task Failed"
        case .awaitingInput: "Awaiting Input"
        case .milestone: "Milestone"
        case .userIdle: "User Idle"
        case .userReturned: "User Returned"
        case .workLogged: "Log Work"
        }
    }
}

/// A normalized, content-free activity signal. Carries what happened and when,
/// never prompts, code, file paths, or any other user content.
public struct ActivityEvent: Equatable, Sendable {
    public let kind: ActivityEventKind
    public let timestamp: Date
    public let sourceId: String

    public init(kind: ActivityEventKind, timestamp: Date, sourceId: String) {
        self.kind = kind
        self.timestamp = timestamp
        self.sourceId = sourceId
    }
}

/// Short-lived state derived from recent activity events. Never persisted;
/// long-lived relationship state stays in `PetState`.
public struct ActivityContext: Equatable, Sendable {
    public static let defaultExpiryInterval: TimeInterval = 30 * 60

    public let isWorking: Bool
    public let isAwaitingInput: Bool
    public let isUserPresent: Bool
    /// When the current, unbroken absence began, or `nil` while present.
    /// Distinct from `lastEventAt`: a repeated `userIdle` "still away" touch
    /// refreshes `lastEventAt` to avoid expiry but must not reset this, or a
    /// long absence could never be told apart from a short one.
    public let awaySince: Date?
    /// When the current, unbroken work block began, or `nil` while not
    /// working. Lets `workEnded` compute a session's real duration even
    /// though `lastEventAt` may have been refreshed by an unrelated event
    /// during the block (e.g. a `milestone` while working).
    public let workingSince: Date?
    public let lastEventAt: Date?

    public static let quiet = ActivityContext(
        isWorking: false,
        isAwaitingInput: false,
        isUserPresent: true,
        awaySince: nil,
        workingSince: nil,
        lastEventAt: nil
    )

    public init(
        isWorking: Bool,
        isAwaitingInput: Bool,
        isUserPresent: Bool,
        awaySince: Date? = nil,
        workingSince: Date? = nil,
        lastEventAt: Date?
    ) {
        self.isWorking = isWorking
        self.isAwaitingInput = isAwaitingInput
        self.isUserPresent = isUserPresent
        self.awaySince = awaySince
        self.workingSince = workingSince
        self.lastEventAt = lastEventAt
    }

    public func reducing(_ event: ActivityEvent) -> ActivityContext {
        switch event.kind {
        case .dailyWake, .userReturned:
            return ActivityContext(
                isWorking: isWorking,
                isAwaitingInput: isAwaitingInput,
                isUserPresent: true,
                awaySince: nil,
                workingSince: workingSince,
                lastEventAt: event.timestamp
            )
        case .workStarted:
            return updating(isWorking: true, isAwaitingInput: false, at: event.timestamp)
        case .workEnded:
            return updating(isWorking: false, isAwaitingInput: false, at: event.timestamp)
        case .taskCompleted, .taskFailed:
            return updating(isAwaitingInput: false, at: event.timestamp)
        case .awaitingInput:
            return updating(isAwaitingInput: true, at: event.timestamp)
        case .milestone, .workLogged:
            return updating(at: event.timestamp)
        case .userIdle:
            return ActivityContext(
                isWorking: isWorking,
                isAwaitingInput: isAwaitingInput,
                isUserPresent: false,
                awaySince: isUserPresent ? event.timestamp : awaySince,
                workingSince: workingSince,
                lastEventAt: event.timestamp
            )
        }
    }

    /// Returns `.quiet` when no event has arrived within the interval, so a
    /// stale work block cannot keep influencing the simulation forever. Under
    /// normal operation a live presence source keeps touching `lastEventAt`
    /// throughout a genuine absence, so this is a fallback for abnormal
    /// termination (a crash, a missed `workEnded`), not the everyday path.
    public func expiring(
        at now: Date,
        after interval: TimeInterval = ActivityContext.defaultExpiryInterval
    ) -> ActivityContext {
        guard let lastEventAt,
              now.timeIntervalSince(lastEventAt) <= max(interval, 0) else {
            return .quiet
        }
        return self
    }

    private func updating(
        isWorking: Bool? = nil,
        isAwaitingInput: Bool? = nil,
        at timestamp: Date
    ) -> ActivityContext {
        let nextIsWorking = isWorking ?? self.isWorking
        let nextWorkingSince: Date?
        if let isWorking {
            nextWorkingSince = isWorking ? (self.isWorking ? workingSince : timestamp) : nil
        } else {
            nextWorkingSince = workingSince
        }

        return ActivityContext(
            isWorking: nextIsWorking,
            isAwaitingInput: isAwaitingInput ?? self.isAwaitingInput,
            isUserPresent: isUserPresent,
            awaySince: awaySince,
            workingSince: nextWorkingSince,
            lastEventAt: timestamp
        )
    }
}

/// The pet's response to an observed activity event: possibly changed state
/// and, for events worth celebrating or consoling, a visible reaction.
public struct PetActivityResponse: Equatable, Sendable {
    public let state: PetState
    public let reaction: PetReaction?

    public init(state: PetState, reaction: PetReaction?) {
        self.state = state
        self.reaction = reaction
    }
}

/// A deterministic event source for tuning reactions and driving checks
/// before any real adapter exists.
public enum SimulatedActivitySource {
    public static let sourceId = "simulated"

    public static func event(_ kind: ActivityEventKind, at timestamp: Date) -> ActivityEvent {
        ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId)
    }
}
