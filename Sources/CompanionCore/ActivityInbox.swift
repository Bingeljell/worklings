import Foundation

/// The provider-neutral boundary external adapters write into: one small JSON
/// file per event, dropped into a local spool directory the app watches. The
/// contract has no fields for prompts, code, or any other content, so the
/// privacy promise is structural rather than a policy — an adapter physically
/// cannot hand the pet more than what happened and when.
///
/// This type owns decoding and validation only, so the trust boundary is pure
/// and deterministic; file watching and delivery live in the app target.
public enum ActivityInbox {
    /// Kinds an external adapter may emit. The app-owned lifecycle kinds stay
    /// internal: `dailyWake` belongs to `DailyWakeTracker`, presence kinds to
    /// the presence source, and `workLogged` to the user's own hand.
    public static let acceptedKinds: Set<ActivityEventKind> = [
        .workStarted,
        .workEnded,
        .taskCompleted,
        .taskFailed,
        .awaitingInput,
        .milestone,
    ]

    /// Ids the app itself emits under. A file claiming one could impersonate
    /// a self-reported or internal signal, so they are rejected outright.
    public static let reservedSourceIds: Set<String> = [
        SystemActivitySource.sourceId,
        ManualActivitySource.sourceId,
        SimulatedActivitySource.sourceId,
    ]

    /// An event older than the activity context's own expiry window could
    /// only ever be discarded downstream, so it is rejected at the boundary —
    /// this is what keeps a backlog written while the app was closed from
    /// replaying onto the pet at launch.
    public static let maxEventAge: TimeInterval = ActivityContext.defaultExpiryInterval

    /// Small allowance for clock skew between a writer and the app; anything
    /// further into the future is treated as a broken clock, not a signal.
    public static let maxFutureSkew: TimeInterval = 2 * 60

    /// A valid payload is tens of bytes; anything larger is not a good-faith
    /// event and is rejected before parsing.
    public static let maxPayloadBytes = 4096

    public static let maxSourceIdLength = 64

    public enum Rejection: Error, Equatable, Sendable {
        case payloadTooLarge
        case unreadablePayload
        case unknownKind
        case kindNotAccepted
        case invalidSourceId
        case reservedSourceId
        case invalidTimestamp
        case staleTimestamp
        case futureTimestamp
    }

    private struct Payload: Decodable {
        let kind: String
        let sourceId: String
        let timestamp: String?
    }

    /// Turns one dropped file's bytes into a normalized event, or the precise
    /// reason it was refused. A missing timestamp means "just now" from the
    /// writer's point of view and resolves to `now`.
    public static func decode(
        _ data: Data,
        receivedAt now: Date
    ) -> Result<ActivityEvent, Rejection> {
        guard data.count <= maxPayloadBytes else {
            return .failure(.payloadTooLarge)
        }

        guard let payload = try? JSONDecoder().decode(Payload.self, from: data) else {
            return .failure(.unreadablePayload)
        }

        guard let kind = ActivityEventKind(rawValue: payload.kind) else {
            return .failure(.unknownKind)
        }
        guard acceptedKinds.contains(kind) else {
            return .failure(.kindNotAccepted)
        }

        let sourceId = payload.sourceId.lowercased()
        guard isValidSourceId(sourceId) else {
            return .failure(.invalidSourceId)
        }
        guard !reservedSourceIds.contains(sourceId) else {
            return .failure(.reservedSourceId)
        }

        let timestamp: Date
        if let rawTimestamp = payload.timestamp {
            guard let parsed = parseTimestamp(rawTimestamp) else {
                return .failure(.invalidTimestamp)
            }
            timestamp = parsed
        } else {
            timestamp = now
        }

        guard now.timeIntervalSince(timestamp) <= maxEventAge else {
            return .failure(.staleTimestamp)
        }
        guard timestamp.timeIntervalSince(now) <= maxFutureSkew else {
            return .failure(.futureTimestamp)
        }

        return .success(ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId))
    }

    /// Delivery order for a drained batch: by event timestamp, oldest first.
    /// Filenames carry no ordering contract, so without this a workEnded file
    /// that happens to sort before its workStarted sibling would be reduced
    /// first, dropping the session and leaving the context stuck "working".
    public static func ordered(_ events: [ActivityEvent]) -> [ActivityEvent] {
        events.sorted { $0.timestamp < $1.timestamp }
    }

    /// Lowercase alphanumerics plus `.`, `_`, `-`, starting alphanumeric, at
    /// most `maxSourceIdLength` characters. Ids are lowercased before this
    /// check so `"Codex"` and `"codex"` are the same adapter, not two.
    public static func isValidSourceId(_ sourceId: String) -> Bool {
        guard !sourceId.isEmpty, sourceId.count <= maxSourceIdLength else {
            return false
        }

        let allowed = Set("abcdefghijklmnopqrstuvwxyz0123456789._-")
        guard sourceId.allSatisfy({ allowed.contains($0) }) else {
            return false
        }

        let first = sourceId.first!
        return first.isLetter || first.isNumber
    }

    private static func parseTimestamp(_ raw: String) -> Date? {
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = fractional.date(from: raw) {
            return date
        }

        let whole = ISO8601DateFormatter()
        whole.formatOptions = [.withInternetDateTime]
        return whole.date(from: raw)
    }
}
