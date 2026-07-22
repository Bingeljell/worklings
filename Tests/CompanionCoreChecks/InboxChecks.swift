import CompanionCore
import Foundation

enum InboxChecks {
    static func run(context: inout CheckContext) {
        checkMinimalPayload(context: &context)
        checkExplicitTimestamp(context: &context)
        checkFractionalTimestamp(context: &context)
        checkSourceIdIsLowercased(context: &context)
        checkUnreadablePayload(context: &context)
        checkOversizePayload(context: &context)
        checkUnknownKind(context: &context)
        checkInternalKindsRejected(context: &context)
        checkReservedSourceIds(context: &context)
        checkInvalidSourceIds(context: &context)
        checkInvalidTimestamp(context: &context)
        checkStaleTimestamp(context: &context)
        checkFutureTimestamp(context: &context)
        checkFutureSkewTolerated(context: &context)
    }

    private static let now = Date(timeIntervalSinceReferenceDate: 800_000_000)

    private static func payload(_ json: String) -> Data {
        Data(json.utf8)
    }

    private static func checkMinimalPayload(context: inout CheckContext) {
        let result = ActivityInbox.decode(
            payload(#"{"kind": "taskCompleted", "sourceId": "codex"}"#),
            receivedAt: now
        )

        guard case .success(let event) = result else {
            context.expect(false, "a minimal payload without a timestamp decodes")
            return
        }
        context.expectEqual(event.kind, .taskCompleted, "the payload kind is preserved")
        context.expectEqual(event.sourceId, "codex", "the payload source id is preserved")
        context.expectEqual(event.timestamp, now, "a missing timestamp resolves to the receipt time")
    }

    private static func checkExplicitTimestamp(context: inout CheckContext) {
        let fiveMinutesAgo = now.addingTimeInterval(-300)
        let formatter = ISO8601DateFormatter()
        let json = #"{"kind": "workStarted", "sourceId": "codex", "timestamp": ""# +
            formatter.string(from: fiveMinutesAgo) + #""}"#

        guard case .success(let event) = ActivityInbox.decode(payload(json), receivedAt: now) else {
            context.expect(false, "a recent explicit timestamp decodes")
            return
        }
        context.expectEqual(
            event.timestamp.timeIntervalSinceReferenceDate.rounded(),
            fiveMinutesAgo.timeIntervalSinceReferenceDate.rounded(),
            "an explicit ISO8601 timestamp is parsed"
        )
    }

    private static func checkFractionalTimestamp(context: inout CheckContext) {
        let json = #"{"kind": "milestone", "sourceId": "codex", "timestamp": "2026-05-14T09:00:00.250Z"}"#
        let receivedAt = ISO8601DateFormatter().date(from: "2026-05-14T09:01:00Z")!

        context.expect(
            (try? ActivityInbox.decode(payload(json), receivedAt: receivedAt).get()) != nil,
            "a fractional-seconds ISO8601 timestamp is accepted"
        )
    }

    private static func checkSourceIdIsLowercased(context: inout CheckContext) {
        let result = ActivityInbox.decode(
            payload(#"{"kind": "milestone", "sourceId": "Codex"}"#),
            receivedAt: now
        )

        guard case .success(let event) = result else {
            context.expect(false, "a mixed-case source id decodes")
            return
        }
        context.expectEqual(
            event.sourceId,
            "codex",
            "source ids are lowercased so one adapter cannot appear as two"
        )
    }

    private static func checkUnreadablePayload(context: inout CheckContext) {
        context.expectEqual(
            ActivityInbox.decode(payload("not json"), receivedAt: now),
            .failure(.unreadablePayload),
            "garbage bytes are rejected as unreadable"
        )
        context.expectEqual(
            ActivityInbox.decode(payload(#"{"kind": "milestone"}"#), receivedAt: now),
            .failure(.unreadablePayload),
            "a payload missing sourceId is rejected as unreadable"
        )
    }

    private static func checkOversizePayload(context: inout CheckContext) {
        let oversized = Data(repeating: 0x20, count: ActivityInbox.maxPayloadBytes + 1)
        context.expectEqual(
            ActivityInbox.decode(oversized, receivedAt: now),
            .failure(.payloadTooLarge),
            "an oversize payload is rejected before parsing"
        )
    }

    private static func checkUnknownKind(context: inout CheckContext) {
        context.expectEqual(
            ActivityInbox.decode(
                payload(#"{"kind": "petBecameSentient", "sourceId": "codex"}"#),
                receivedAt: now
            ),
            .failure(.unknownKind),
            "an unknown kind is discarded so old adapters never break a newer app"
        )
    }

    private static func checkInternalKindsRejected(context: inout CheckContext) {
        for kind in ActivityEventKind.allCases where !ActivityInbox.acceptedKinds.contains(kind) {
            context.expectEqual(
                ActivityInbox.decode(
                    payload(#"{"kind": ""# + kind.rawValue + #"", "sourceId": "codex"}"#),
                    receivedAt: now
                ),
                .failure(.kindNotAccepted),
                "the app-owned kind \(kind.rawValue) cannot be injected externally"
            )
        }
    }

    private static func checkReservedSourceIds(context: inout CheckContext) {
        for sourceId in ActivityInbox.reservedSourceIds {
            context.expectEqual(
                ActivityInbox.decode(
                    payload(#"{"kind": "milestone", "sourceId": ""# + sourceId + #""}"#),
                    receivedAt: now
                ),
                .failure(.reservedSourceId),
                "the internal source id \(sourceId) cannot be impersonated"
            )
        }
    }

    private static func checkInvalidSourceIds(context: inout CheckContext) {
        let invalid = ["", "-codex", "co dex", "codex!", String(repeating: "a", count: 65)]
        for sourceId in invalid {
            context.expectEqual(
                ActivityInbox.decode(
                    payload(#"{"kind": "milestone", "sourceId": ""# + sourceId + #""}"#),
                    receivedAt: now
                ),
                .failure(.invalidSourceId),
                "the source id \"\(sourceId)\" is rejected"
            )
        }
    }

    private static func checkInvalidTimestamp(context: inout CheckContext) {
        context.expectEqual(
            ActivityInbox.decode(
                payload(#"{"kind": "milestone", "sourceId": "codex", "timestamp": "yesterday"}"#),
                receivedAt: now
            ),
            .failure(.invalidTimestamp),
            "a non-ISO8601 timestamp is rejected rather than defaulted"
        )
    }

    private static func checkStaleTimestamp(context: inout CheckContext) {
        let stale = now.addingTimeInterval(-(ActivityInbox.maxEventAge + 60))
        let json = #"{"kind": "taskCompleted", "sourceId": "codex", "timestamp": ""# +
            ISO8601DateFormatter().string(from: stale) + #""}"#

        context.expectEqual(
            ActivityInbox.decode(payload(json), receivedAt: now),
            .failure(.staleTimestamp),
            "a backlog written while the app was closed does not replay onto the pet"
        )
    }

    private static func checkFutureTimestamp(context: inout CheckContext) {
        let future = now.addingTimeInterval(ActivityInbox.maxFutureSkew + 60)
        let json = #"{"kind": "taskCompleted", "sourceId": "codex", "timestamp": ""# +
            ISO8601DateFormatter().string(from: future) + #""}"#

        context.expectEqual(
            ActivityInbox.decode(payload(json), receivedAt: now),
            .failure(.futureTimestamp),
            "a timestamp far in the future is treated as a broken clock"
        )
    }

    private static func checkFutureSkewTolerated(context: inout CheckContext) {
        let slightlyAhead = now.addingTimeInterval(30)
        let json = #"{"kind": "taskCompleted", "sourceId": "codex", "timestamp": ""# +
            ISO8601DateFormatter().string(from: slightlyAhead) + #""}"#

        context.expect(
            (try? ActivityInbox.decode(payload(json), receivedAt: now).get()) != nil,
            "small clock skew between a writer and the app is tolerated"
        )
    }
}
