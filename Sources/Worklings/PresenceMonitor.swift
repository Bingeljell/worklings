import CompanionCore
import CoreGraphics
import Foundation

/// Polls macOS system-wide input idle time and turns threshold crossings into
/// activity events. Reads only elapsed time since the last input event, never
/// keystrokes, window contents, or which app is active, and needs no special
/// permission to do so.
@MainActor
final class PresenceMonitor {
    private let session: PetSession
    private let idleThreshold: TimeInterval
    private let pollInterval: TimeInterval
    private var wasIdle = false
    private var task: Task<Void, Never>?

    init(
        session: PetSession,
        idleThreshold: TimeInterval = PresenceEvaluator.defaultIdleThreshold,
        pollInterval: TimeInterval = 15
    ) {
        self.session = session
        self.idleThreshold = idleThreshold
        self.pollInterval = pollInterval
    }

    func start() {
        guard task == nil else {
            return
        }
        task = Task { [weak self] in
            while !Task.isCancelled {
                self?.checkPresence()
                try? await Task.sleep(for: .seconds(self?.pollInterval ?? 15))
            }
        }
    }

    func stop() {
        task?.cancel()
        task = nil
    }

    private func checkPresence() {
        let idleSeconds = Self.systemIdleSeconds()
        guard let kind = PresenceEvaluator.transition(
            idleSeconds: idleSeconds,
            wasIdle: wasIdle,
            threshold: idleThreshold
        ) else {
            return
        }

        wasIdle = kind == .userIdle
        session.receive(SystemActivitySource.event(kind, at: Date()))
    }

    /// `kCGAnyInputEventType`, expressed as its raw value because Swift does
    /// not expose the constant: seconds since the last input event of any
    /// kind, system-wide, with no content or per-app visibility.
    private static func systemIdleSeconds() -> TimeInterval {
        let anyInputEventType = CGEventType(rawValue: 0xFFFFFFFF) ?? .null
        return CGEventSource.secondsSinceLastEventType(
            .combinedSessionState,
            eventType: anyInputEventType
        )
    }
}
