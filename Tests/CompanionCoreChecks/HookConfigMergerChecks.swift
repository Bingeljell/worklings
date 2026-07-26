import CompanionCore
import Foundation

enum HookConfigMergerChecks {
    static func run(context: inout CheckContext) {
        checkMergesIntoEmptyConfig(context: &context)
        checkPreservesOtherTopLevelKeys(context: &context)
        checkPreservesExistingUnrelatedHook(context: &context)
        checkPreservesUserEntryUnderMappedEvent(context: &context)
        checkIsIdempotent(context: &context)
        checkDisconnectRemovesOnlyOurs(context: &context)
        checkDisconnectRestoresPreConnectShape(context: &context)
        checkRefusesToOverwriteUnparseableConfig(context: &context)
    }

    private static let adapter = "/Applications/Worklings.app/Contents/Resources/adapters/claude-code-hook"
    private static let rtk = "/Users/x/.claude/hooks/rtk-rewrite.sh"

    private static func hooks(_ data: Data) -> [String: Any] {
        let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
        return root["hooks"] as? [String: Any] ?? [:]
    }

    private static func commands(_ data: Data, event: String) -> [String] {
        let entries = hooks(data)[event] as? [[String: Any]] ?? []
        return entries.flatMap { entry in
            (entry["hooks"] as? [[String: Any]] ?? []).compactMap { $0["command"] as? String }
        }
    }

    private static func connect(_ json: String) -> Data {
        (try? HookConfigMerger.connected(
            configJSON: Data(json.utf8),
            adapterPath: adapter,
            mappings: HookConfigMerger.claudeCodeMappings
        )) ?? Data()
    }

    private static func checkMergesIntoEmptyConfig(context: inout CheckContext) {
        let out = (try? HookConfigMerger.connected(
            configJSON: Data(),
            adapterPath: adapter,
            mappings: HookConfigMerger.claudeCodeMappings
        )) ?? Data()

        context.expect(
            commands(out, event: "Stop").contains("\(adapter) taskCompleted"),
            "connecting an empty config writes our Stop -> taskCompleted hook"
        )
        context.expect(
            commands(out, event: "SessionStart").contains("\(adapter) workStarted"),
            "connecting an empty config writes every mapped event"
        )
        context.expect(
            HookConfigMerger.isConnected(configJSON: out, adapterPath: adapter),
            "a freshly connected config reports as connected"
        )
    }

    private static func checkPreservesOtherTopLevelKeys(context: inout CheckContext) {
        let out = connect(#"{"model":"opus","theme":"dark"}"#)
        let root = (try? JSONSerialization.jsonObject(with: out)) as? [String: Any] ?? [:]

        context.expectEqual(root["model"] as? String, "opus", "connecting preserves unrelated top-level keys")
        context.expectEqual(root["theme"] as? String, "dark", "connecting preserves every unrelated key")
    }

    private static func checkPreservesExistingUnrelatedHook(context: inout CheckContext) {
        let out = connect(#"{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"\#(rtk)"}]}]}}"#)

        context.expect(
            commands(out, event: "PreToolUse").contains(rtk),
            "a pre-existing unrelated hook (rtk PreToolUse) survives connecting"
        )
        context.expect(
            commands(out, event: "SessionStart").contains("\(adapter) workStarted"),
            "our hooks are added alongside the preserved ones"
        )
    }

    private static func checkPreservesUserEntryUnderMappedEvent(context: inout CheckContext) {
        let out = connect(#"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"/user/my-stop-hook"}]}]}}"#)
        let stop = commands(out, event: "Stop")

        context.expect(stop.contains("/user/my-stop-hook"), "a user's own hook under a mapped event is preserved")
        context.expect(stop.contains("\(adapter) taskCompleted"), "our hook is appended under the same event")
        context.expectEqual(stop.count, 2, "the user's entry and ours coexist, neither dropped")
    }

    private static func checkIsIdempotent(context: inout CheckContext) {
        let once = connect(#"{}"#)
        let twice = (try? HookConfigMerger.connected(
            configJSON: once,
            adapterPath: adapter,
            mappings: HookConfigMerger.claudeCodeMappings
        )) ?? Data()

        let ours = commands(twice, event: "Stop").filter { $0.contains("claude-code-hook") }
        context.expectEqual(ours.count, 1, "connecting twice never duplicates our entries")
    }

    private static func checkDisconnectRemovesOnlyOurs(context: inout CheckContext) {
        let connected = connect(#"{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"\#(rtk)"}]}],"Stop":[{"hooks":[{"type":"command","command":"/user/my-stop-hook"}]}]}}"#)
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: connected, adapterPath: adapter)) ?? Data()

        context.expect(commands(disconnected, event: "PreToolUse").contains(rtk), "disconnect leaves the rtk hook intact")
        context.expect(commands(disconnected, event: "Stop").contains("/user/my-stop-hook"), "disconnect leaves the user's Stop hook intact")
        context.expect(
            !HookConfigMerger.isConnected(configJSON: disconnected, adapterPath: adapter),
            "disconnect removes all of our entries"
        )
        context.expect(
            hooks(disconnected)["SessionStart"] == nil,
            "an event that held only our entry is dropped on disconnect"
        )
    }

    private static func checkDisconnectRestoresPreConnectShape(context: inout CheckContext) {
        let original = #"{"model":"opus","hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"\#(rtk)"}]}]}}"#
        let connected = connect(original)
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: connected, adapterPath: adapter)) ?? Data()
        let root = (try? JSONSerialization.jsonObject(with: disconnected)) as? [String: Any] ?? [:]

        context.expectEqual(root["model"] as? String, "opus", "connect-then-disconnect preserves unrelated keys")
        context.expect(commands(disconnected, event: "PreToolUse").contains(rtk), "connect-then-disconnect restores the original hooks")
        let leftovers = ["SessionStart", "Stop", "Notification", "SessionEnd"].compactMap { hooks(disconnected)[$0] }
        context.expect(leftovers.isEmpty, "no trace of our hooks remains after disconnect")
    }

    private static func checkRefusesToOverwriteUnparseableConfig(context: inout CheckContext) {
        context.expectThrows("connecting refuses to overwrite a config that is not valid JSON") {
            _ = try HookConfigMerger.connected(
                configJSON: Data("not json { oops".utf8),
                adapterPath: adapter,
                mappings: HookConfigMerger.claudeCodeMappings
            )
        }
    }
}
