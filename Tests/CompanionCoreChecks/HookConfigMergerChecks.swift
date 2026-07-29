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
        checkRefusesNonObjectHooks(context: &context)
        checkRefusesWrongShapedEvent(context: &context)
        checkPreservesSiblingHookInSharedEntry(context: &context)
        checkIgnoresSimilarlyNamedUserScript(context: &context)
        checkRecognizesRelocatedBundle(context: &context)
        checkDoesNotClaimForeignOrGenericName(context: &context)
        checkOurHookExecutablePathsReportsOnlyOurs(context: &context)
        checkExecFormNeedsNoQuoting(context: &context)
        checkShellFormQuotesPath(context: &context)
        checkShellFormRoundTripsApostrophePath(context: &context)
        checkNotificationHookIsMatched(context: &context)
    }

    private static let adapter = "/Applications/Worklings.app/Contents/Resources/adapters/worklings-claude-code-activity-hook"
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

    private static func connect(_ json: String, style: HookConfigMerger.CommandStyle = .execForm) -> Data {
        (try? HookConfigMerger.connected(
            configJSON: Data(json.utf8),
            adapterPath: adapter,
            mappings: HookConfigMerger.claudeCodeMappings,
            style: style
        )) ?? Data()
    }

    private static func checkMergesIntoEmptyConfig(context: inout CheckContext) {
        let out = connect("", style: .execForm)

        // Exec form: the command is the adapter path itself; the kind is an arg.
        context.expect(
            commands(out, event: "Stop").contains(adapter),
            "connecting an empty config writes our Stop hook"
        )
        context.expect(
            commands(out, event: "SessionStart").contains(adapter),
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
            commands(out, event: "SessionStart").contains(adapter),
            "our hooks are added alongside the preserved ones"
        )
    }

    private static func checkPreservesUserEntryUnderMappedEvent(context: inout CheckContext) {
        let out = connect(#"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"/user/my-stop-hook"}]}]}}"#)
        let stop = commands(out, event: "Stop")

        context.expect(stop.contains("/user/my-stop-hook"), "a user's own hook under a mapped event is preserved")
        context.expect(stop.contains(adapter), "our hook is appended under the same event")
        context.expectEqual(stop.count, 2, "the user's entry and ours coexist, neither dropped")
    }

    private static func checkIsIdempotent(context: inout CheckContext) {
        let once = connect(#"{}"#)
        let twice = (try? HookConfigMerger.connected(
            configJSON: once,
            adapterPath: adapter,
            mappings: HookConfigMerger.claudeCodeMappings,
            style: .execForm
        )) ?? Data()

        let ours = commands(twice, event: "Stop").filter { $0.contains("worklings-claude-code-activity-hook") }
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
                mappings: HookConfigMerger.claudeCodeMappings,
                style: .execForm
            )
        }
    }

    private static func checkRefusesNonObjectHooks(context: inout CheckContext) {
        context.expectThrows("a non-object \"hooks\" value is refused, not erased") {
            _ = try HookConfigMerger.connected(
                configJSON: Data(#"{"hooks":"oops"}"#.utf8),
                adapterPath: adapter,
                mappings: HookConfigMerger.claudeCodeMappings,
                style: .execForm
            )
        }
    }

    private static func checkRefusesWrongShapedEvent(context: inout CheckContext) {
        context.expectThrows("a mapped event with an unexpected value is refused, not erased") {
            _ = try HookConfigMerger.connected(
                configJSON: Data(#"{"hooks":{"Stop":"oops"}}"#.utf8),
                adapterPath: adapter,
                mappings: HookConfigMerger.claudeCodeMappings,
                style: .execForm
            )
        }
    }

    private static func checkPreservesSiblingHookInSharedEntry(context: inout CheckContext) {
        // Our command and a user's command share one entry's hooks array.
        let input = #"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"\#(adapter) taskCompleted"},{"type":"command","command":"/user/sibling"}]}]}}"#
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: Data(input.utf8), adapterPath: adapter)) ?? Data()
        let stop = commands(disconnected, event: "Stop")

        context.expect(stop.contains("/user/sibling"), "a sibling hook sharing our entry survives disconnect")
        context.expect(!stop.contains(where: { $0.contains("worklings-claude-code-activity-hook") }), "our hook is removed from the shared entry")
        context.expect(
            !HookConfigMerger.isConnected(configJSON: disconnected, adapterPath: adapter),
            "nothing of ours remains after disconnect"
        )
    }

    private static func checkIgnoresSimilarlyNamedUserScript(context: inout CheckContext) {
        // A user script whose name merely contains ours as a substring.
        let input = #"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"/usr/local/bin/my-worklings-claude-code-activity-hook-wrapper x"}]}]}}"#
        context.expect(
            !HookConfigMerger.isConnected(configJSON: Data(input.utf8), adapterPath: adapter),
            "a similarly-named wrapper is not mistaken for our hook"
        )
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: Data(input.utf8), adapterPath: adapter)) ?? Data()
        context.expect(
            commands(disconnected, event: "Stop").contains("/usr/local/bin/my-worklings-claude-code-activity-hook-wrapper x"),
            "disconnect leaves a similarly-named user script untouched"
        )
    }

    private static func checkShellFormRoundTripsApostrophePath(context: inout CheckContext) {
        // A path with an apostrophe is the case that used to break detection: the
        // quoter emits `'\''` for the embedded quote, and matching must parse it
        // back to the full literal path (not stop at the first inner quote).
        let apostrophe = "/Applications/Nikhil's Apps/worklings-codex-activity-hook"
        let out = (try? HookConfigMerger.connected(
            configJSON: Data(),
            adapterPath: apostrophe,
            mappings: HookConfigMerger.codexMappings,
            style: .shellForm
        )) ?? Data()

        context.expectEqual(
            firstHook(out, event: "Stop")?["command"] as? String,
            "'/Applications/Nikhil'\\''s Apps/worklings-codex-activity-hook' taskCompleted",
            "shell form quotes an apostrophe path as '\\''"
        )
        context.expect(
            HookConfigMerger.isConnected(configJSON: out, adapterPath: apostrophe),
            "an apostrophe-path hook is recognized as ours (so it isn't reconnected twice)"
        )
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: out, adapterPath: apostrophe)) ?? Data()
        context.expect(
            !HookConfigMerger.isConnected(configJSON: disconnected, adapterPath: apostrophe),
            "an apostrophe-path hook is cleanly removed on disconnect"
        )
    }

    private static func checkRecognizesRelocatedBundle(context: inout CheckContext) {
        // The hook was written when Worklings lived in /Applications; the user
        // later moved the app to ~/Applications, so the current adapter path has
        // a different directory but the same namespaced file name. Filename-based
        // ownership must still recognize and remove the previously-written hook —
        // this is the property the namespaced name is designed to keep safe.
        let written = "/Applications/Worklings.app/Contents/Resources/adapters/worklings-codex-activity-hook"
        let relocated = "\(NSHomeDirectory())/Applications/Worklings.app/Contents/Resources/adapters/worklings-codex-activity-hook"
        let input = #"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"'\#(written)' taskCompleted"}]}]}}"#

        context.expect(
            HookConfigMerger.isConnected(configJSON: Data(input.utf8), adapterPath: relocated),
            "a hook written at the app's old location is still recognized after the bundle moves"
        )
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: Data(input.utf8), adapterPath: relocated)) ?? Data()
        context.expect(
            !HookConfigMerger.isConnected(configJSON: disconnected, adapterPath: relocated),
            "a relocated bundle can clean up the hook it wrote at its old path"
        )
        context.expect(
            (hooks(disconnected)["Stop"]) == nil,
            "the event that held only our relocated hook is dropped on disconnect"
        )
    }

    private static func checkDoesNotClaimForeignOrGenericName(context: inout CheckContext) {
        // The whole point of the namespaced name: an unrelated executable that
        // reuses a generic stem — including the old pre-rename names `codex-hook`
        // / `claude-code-hook` — must never be claimed as ours, even if it sits
        // in a plausible hooks directory. isConnected stays false and disconnect
        // leaves it byte-for-byte intact.
        let foreign = [
            "/usr/local/bin/codex-hook",                 // old generic name, no longer ours
            "/usr/local/bin/claude-code-hook",           // old generic name, no longer ours
            "/Users/x/.codex/hooks/codex-activity-hook"  // similar but missing the worklings- prefix
        ]
        for command in foreign {
            let input = #"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"'\#(command)' taskCompleted"}]}]}}"#
            context.expect(
                !HookConfigMerger.isConnected(configJSON: Data(input.utf8), adapterPath: adapter),
                "a foreign/generic-named hook (\(command)) is not claimed as ours"
            )
            let disconnected = (try? HookConfigMerger.disconnected(configJSON: Data(input.utf8), adapterPath: adapter)) ?? Data()
            context.expect(
                commands(disconnected, event: "Stop").contains("'\(command)' taskCompleted"),
                "disconnect leaves a foreign/generic-named hook (\(command)) untouched"
            )
        }
    }

    private static func checkOurHookExecutablePathsReportsOnlyOurs(context: inout CheckContext) {
        // Powers the live-vs-stale distinction: report the executable path of
        // every hook of ours, and nothing of anyone else's.
        let out = connect(#"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"/user/other-hook"}]}]}}"#)
        let paths = HookConfigMerger.ourHookExecutablePaths(configJSON: out, adapterPath: adapter)

        context.expect(!paths.isEmpty, "our hook paths are reported after connecting")
        context.expect(paths.allSatisfy { $0 == adapter }, "every reported path is our adapter, not the user's hook")
        context.expectEqual(paths.count, 4, "one path per mapped Claude event (SessionStart/Stop/Notification/SessionEnd)")

        let foreignOnly = Data(#"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"/user/other-hook"}]}]}}"#.utf8)
        context.expect(
            HookConfigMerger.ourHookExecutablePaths(configJSON: foreignOnly, adapterPath: adapter).isEmpty,
            "a config with none of our hooks reports no paths"
        )
    }

    private static func checkNotificationHookIsMatched(context: inout CheckContext) {
        let out = connect("", style: .execForm)
        let notification = (hooks(out)["Notification"] as? [[String: Any]])?.first
        let matcher = notification?["matcher"] as? String

        context.expect(
            matcher?.contains("permission_prompt") == true,
            "the Notification hook is restricted to await-relevant notification types"
        )
        context.expect(
            matcher?.contains("auth_success") != true,
            "the Notification hook is not wired for auth/elicitation-result notifications"
        )
        let sessionStart = (hooks(out)["SessionStart"] as? [[String: Any]])?.first
        context.expect(
            sessionStart?["matcher"] == nil,
            "an event we want to fire on every occurrence carries no matcher"
        )
    }

    private static func firstHook(_ data: Data, event: String) -> [String: Any]? {
        let entries = hooks(data)[event] as? [[String: Any]] ?? []
        return (entries.last?["hooks"] as? [[String: Any]])?.last
    }

    private static func checkExecFormNeedsNoQuoting(context: inout CheckContext) {
        let spaced = "/tmp/My Tools/worklings-claude-code-activity-hook"
        let out = (try? HookConfigMerger.connected(
            configJSON: Data(),
            adapterPath: spaced,
            mappings: HookConfigMerger.claudeCodeMappings,
            style: .execForm
        )) ?? Data()
        let hook = firstHook(out, event: "Stop")

        context.expectEqual(hook?["command"] as? String, spaced, "exec form writes the path verbatim — no quoting")
        context.expectEqual((hook?["args"] as? [String])?.first, "taskCompleted", "exec form passes the kind as a separate arg")
        context.expect(
            HookConfigMerger.isConnected(configJSON: out, adapterPath: spaced),
            "exec-form hooks (with args) are recognized as ours"
        )
    }

    private static func checkShellFormQuotesPath(context: inout CheckContext) {
        let spaced = "/tmp/My Tools/worklings-codex-activity-hook"
        let out = (try? HookConfigMerger.connected(
            configJSON: Data(),
            adapterPath: spaced,
            mappings: HookConfigMerger.codexMappings,
            style: .shellForm
        )) ?? Data()

        context.expectEqual(
            firstHook(out, event: "Stop")?["command"] as? String,
            "'/tmp/My Tools/worklings-codex-activity-hook' taskCompleted",
            "shell form single-quotes the path so a space cannot break it"
        )
        context.expect(
            HookConfigMerger.isConnected(configJSON: out, adapterPath: spaced),
            "shell-form (single-quoted) hooks are recognized as ours"
        )
        let disconnected = (try? HookConfigMerger.disconnected(configJSON: out, adapterPath: spaced)) ?? Data()
        context.expect(
            !HookConfigMerger.isConnected(configJSON: disconnected, adapterPath: spaced),
            "shell-form hooks are cleanly removed (matching parses the quoted path)"
        )
    }
}
