import Foundation

/// Merges Worklings' command-hook entries into a tool's JSON hook config. Both
/// tools we target use the same shape — a top-level `"hooks"` object mapping an
/// event name to an array of `{ "hooks": [ { "type": "command", "command" } ] }`
/// — so one merger serves Claude Code's `settings.json` and Codex's `hooks.json`.
///
/// Pure and total on its inputs, so the "never brick an existing config"
/// contract is provable by checks. It never *erases* structure it does not
/// recognize: a config that is present but not valid JSON, a `"hooks"` value
/// that is not an object, or a mapped event whose value is not the expected
/// array all make `connected` throw rather than overwrite. Ownership is matched
/// at the level of an individual command hook, by the adapter's exact file name,
/// so re-connecting is idempotent, disconnecting removes only our hooks (a
/// sibling hook sharing the same entry survives), and a similarly-named user
/// script (`my-worklings-codex-activity-hook-wrapper`) is never mistaken for
/// ours. The adapter names are Worklings-namespaced precisely so that matching
/// on the file name alone — which is what lets a relocated app bundle still
/// clean up its own hooks — cannot accidentally claim an unrelated executable.
public enum HookConfigMerger {
    public struct Mapping: Sendable, Equatable {
        public let event: String
        public let kind: String
        /// Optional matcher restricting which sub-events fire the hook (e.g.
        /// Claude's Notification fires for many types, only some of which mean
        /// "awaiting the user"). Nil fires on every occurrence of the event.
        public let matcher: String?
        public init(event: String, kind: String, matcher: String? = nil) {
            self.event = event
            self.kind = kind
            self.matcher = matcher
        }
    }

    /// How the hook command is written. Both forms *guard* the adapter with a
    /// `[ -x … ]` existence test, so if the app is deleted (the adapter is gone)
    /// the hook degrades to a silent no-op instead of a launch error — the same
    /// convention dotfile tools use for lines they inject into files they don't
    /// own. Both keep the path shell-safe (a space or metacharacter can never
    /// break or be interpreted).
    public enum CommandStyle: Sendable, Equatable {
        /// For a tool that accepts an argv array (Claude Code). We spawn
        /// `/bin/sh -c '<guard>' sh <path> <kind>` and pass the adapter path as a
        /// quoted positional argument (`$1`), so the shell never re-parses it.
        case execForm
        /// For a tool that accepts only a shell string (Codex). The guard is
        /// written inline and the path single-quoted; a missing adapter prints an
        /// empty JSON object (a valid Stop payload) rather than failing.
        case shellForm
    }

    public enum MergeError: Error, Equatable {
        /// The config is present but not a JSON object — refuse to overwrite it.
        case unparseableConfig
        /// `"hooks"`, or a mapped event's value, is present but not the shape we
        /// understand — refuse rather than erase the user's data.
        case unexpectedStructure
    }

    public static func connected(
        configJSON: Data,
        adapterPath: String,
        mappings: [Mapping],
        style: CommandStyle
    ) throws -> Data {
        var root = try object(from: configJSON)
        var hooks = try hooksObject(from: root)

        for mapping in mappings {
            var entries = try existingEntries(in: hooks, event: mapping.event)
            // Strip our own prior hooks (a sibling hook in a shared entry is
            // preserved) so re-connecting is idempotent, then append ours.
            entries = strippingOurHooks(from: entries, adapterPath: adapterPath)
            entries.append(ourEntry(adapterPath: adapterPath, mapping: mapping, style: style))
            hooks[mapping.event] = entries
        }

        root["hooks"] = hooks
        return try data(from: root)
    }

    /// Returns `configJSON` with only our command hooks removed, at the hook
    /// level: a sibling hook sharing an entry with ours is kept, an entry left
    /// with no hooks is dropped, and an emptied `"hooks"` object is removed.
    /// Unfamiliar structure is left untouched (nothing of ours could live in it,
    /// since `connected` would have refused to write it).
    public static func disconnected(
        configJSON: Data,
        adapterPath: String
    ) throws -> Data {
        var root = try object(from: configJSON)
        guard let hooks = root["hooks"] as? [String: Any] else {
            return try data(from: root)
        }

        var kept: [String: Any] = [:]
        for (event, value) in hooks {
            guard let entries = value as? [[String: Any]] else {
                kept[event] = value // preserve anything not shaped like our entries
                continue
            }
            let remaining = strippingOurHooks(from: entries, adapterPath: adapterPath)
            if !remaining.isEmpty {
                kept[event] = remaining
            }
        }

        root["hooks"] = kept.isEmpty ? nil : kept
        return try data(from: root)
    }

    /// Whether any of our command hooks are present.
    public static func isConnected(configJSON: Data, adapterPath: String) -> Bool {
        guard let root = try? object(from: configJSON),
              let hooks = root["hooks"] as? [String: Any] else {
            return false
        }
        for value in hooks.values {
            guard let entries = value as? [[String: Any]] else { continue }
            for entry in entries {
                guard let hookList = entry["hooks"] as? [[String: Any]] else { continue }
                if hookList.contains(where: { hookIsOurs($0, adapterPath: adapterPath) }) {
                    return true
                }
            }
        }
        return false
    }

    // MARK: - Convenience mappings

    /// Claude Code's full lifecycle. `Notification` carries no content we read,
    /// and is matched to only the types that actually mean the user is being
    /// awaited — not auth/elicitation-result notifications.
    public static let claudeCodeMappings: [Mapping] = [
        Mapping(event: "SessionStart", kind: "workStarted"),
        Mapping(event: "Stop", kind: "taskCompleted"),
        Mapping(
            event: "Notification",
            kind: "awaitingInput",
            matcher: "permission_prompt|idle_prompt|elicitation_dialog|agent_needs_input"
        ),
        Mapping(event: "SessionEnd", kind: "workEnded")
    ]

    /// Codex's lifecycle; it has no documented "awaiting input" event.
    public static let codexMappings: [Mapping] = [
        Mapping(event: "SessionStart", kind: "workStarted"),
        Mapping(event: "Stop", kind: "taskCompleted"),
        Mapping(event: "SessionEnd", kind: "workEnded")
    ]

    // MARK: - Building our entry

    /// The guard script for the argv form: run the adapter only if it still
    /// exists and is executable, passing the kind through. The path and kind are
    /// positional arguments (`$1`, `$2`), never spliced into the script text, so
    /// the shell cannot re-parse or word-split them.
    private static let argvGuardScript = "if [ -x \"$1\" ]; then exec \"$1\" \"$2\"; fi"

    private static func ourEntry(adapterPath: String, mapping: Mapping, style: CommandStyle) -> [String: Any] {
        let hook: [String: Any]
        switch style {
        case .execForm:
            // /bin/sh runs the guard; the path and kind are positional args, so a
            // deleted adapter is a silent no-op and the path needs no quoting.
            hook = [
                "type": "command",
                "command": "/bin/sh",
                "args": ["-c", Self.argvGuardScript, "sh", adapterPath, mapping.kind]
            ]
        case .shellForm:
            // Inline guard in a single shell string: run the (single-quoted) path
            // if it exists, else print an empty JSON object so a deleted adapter
            // still returns a valid, content-free success instead of erroring.
            let quoted = singleQuoted(adapterPath)
            hook = [
                "type": "command",
                "command": "if [ -x \(quoted) ]; then \(quoted) \(mapping.kind); else printf '{}'; fi"
            ]
        }
        var entry: [String: Any] = ["hooks": [hook]]
        if let matcher = mapping.matcher {
            entry["matcher"] = matcher
        }
        return entry
    }

    /// POSIX single-quoting: everything inside is literal; an embedded quote is
    /// closed, escaped, and reopened. Neutralizes spaces and metacharacters.
    private static func singleQuoted(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    // MARK: - Reading structure (refuse, never erase)

    private static func hooksObject(from root: [String: Any]) throws -> [String: Any] {
        guard let value = root["hooks"] else { return [:] }
        guard let hooks = value as? [String: Any] else { throw MergeError.unexpectedStructure }
        return hooks
    }

    private static func existingEntries(in hooks: [String: Any], event: String) throws -> [[String: Any]] {
        guard let value = hooks[event] else { return [] }
        guard let entries = value as? [[String: Any]] else { throw MergeError.unexpectedStructure }
        return entries
    }

    // MARK: - Ownership (hook-level, exact file name)

    /// Removes our command hooks from each entry, preserving sibling hooks and
    /// dropping an entry only when it is left with none.
    private static func strippingOurHooks(
        from entries: [[String: Any]],
        adapterPath: String
    ) -> [[String: Any]] {
        var result: [[String: Any]] = []
        for var entry in entries {
            guard let hookList = entry["hooks"] as? [[String: Any]] else {
                result.append(entry) // entries we don't understand are left alone
                continue
            }
            let kept = hookList.filter { !hookIsOurs($0, adapterPath: adapterPath) }
            if kept.count == hookList.count {
                result.append(entry) // nothing of ours here
            } else if !kept.isEmpty {
                entry["hooks"] = kept // a sibling hook remains
                result.append(entry)
            }
            // else: the entry held only our hook(s) — drop it
        }
        return result
    }

    /// Every executable path our command hooks point at, across the whole
    /// config. Empty when none are ours. Lets a caller tell "connected and
    /// pointing at a live adapter" from "connected but the adapter path is dead"
    /// (the app was moved or deleted) — ownership (the file name) and liveness
    /// (does that path still exist) are deliberately separate questions.
    public static func ourHookExecutablePaths(configJSON: Data, adapterPath: String) -> [String] {
        guard let root = try? object(from: configJSON),
              let hooks = root["hooks"] as? [String: Any] else {
            return []
        }
        let target = (adapterPath as NSString).lastPathComponent
        var paths: [String] = []
        for value in hooks.values {
            guard let entries = value as? [[String: Any]] else { continue }
            for entry in entries {
                guard let hookList = entry["hooks"] as? [[String: Any]] else { continue }
                for hook in hookList {
                    for candidate in adapterCandidatePaths(in: hook)
                    where (candidate as NSString).lastPathComponent == target {
                        paths.append(candidate)
                    }
                }
            }
        }
        return paths
    }

    private static func hookIsOurs(_ hook: [String: Any], adapterPath: String) -> Bool {
        let target = (adapterPath as NSString).lastPathComponent
        // Ownership is by the adapter's distinctive file name, found anywhere a
        // hook could name it: the `command` itself (an old exec-form config), a
        // single-quoted word inside the command string (the guarded shell form,
        // and the old shell form), or an element of `args` (the guarded argv
        // form). A moved/reinstalled bundle keeps the file name, so it is still
        // recognized; the name is Worklings-namespaced (`worklings-…-activity-hook`),
        // so a differently-named user script — even one reusing a generic stem
        // like `codex-hook` — is never claimed.
        return adapterCandidatePaths(in: hook).contains {
            ($0 as NSString).lastPathComponent == target
        }
    }

    /// Every string in a hook that could be the adapter path: the whole command,
    /// each shell word of the command (recovering a single-quoted path), and each
    /// `args` element. Ownership and liveness both scan these for our file name.
    private static func adapterCandidatePaths(in hook: [String: Any]) -> [String] {
        var candidates: [String] = []
        if let command = hook["command"] as? String {
            candidates.append(command)                        // old exec form: command is the path
            candidates.append(contentsOf: shellWords(command)) // shell forms: path is a quoted word
        }
        if let args = hook["args"] as? [String] {
            candidates.append(contentsOf: args)               // guarded argv form: path is an arg
        }
        return candidates
    }

    /// Splits a shell command string into words, honoring single quotes, double
    /// quotes, and backslash escapes — enough to recover a single-quoted path
    /// (including one whose value contains an apostrophe, written `'\''`).
    private static func shellWords(_ command: String) -> [String] {
        var words: [String] = []
        var current = ""
        var hasWord = false
        var index = command.startIndex
        let end = command.endIndex

        func take() { index = command.index(after: index) }

        while index < end {
            let character = command[index]
            switch character {
            case " ", "\t", "\n":
                if hasWord { words.append(current); current = ""; hasWord = false }
                take()
            case "'":
                hasWord = true
                take()
                while index < end, command[index] != "'" { current.append(command[index]); take() }
                if index < end { take() } // closing quote
            case "\"":
                hasWord = true
                take()
                while index < end, command[index] != "\"" {
                    if command[index] == "\\", command.index(after: index) < end {
                        take(); current.append(command[index]); take(); continue
                    }
                    current.append(command[index]); take()
                }
                if index < end { take() } // closing quote
            case "\\":
                hasWord = true
                take()
                if index < end { current.append(command[index]); take() }
            default:
                hasWord = true
                current.append(character)
                take()
            }
        }
        if hasWord { words.append(current) }
        return words
    }

    // MARK: - JSON

    private static func object(from data: Data) throws -> [String: Any] {
        let isBlank = data.allSatisfy { $0 == 0x20 || $0 == 0x0A || $0 == 0x0D || $0 == 0x09 }
        if data.isEmpty || isBlank {
            return [:]
        }
        guard let parsed = try? JSONSerialization.jsonObject(with: data),
              let object = parsed as? [String: Any] else {
            throw MergeError.unparseableConfig
        }
        return object
    }

    private static func data(from object: [String: Any]) throws -> Data {
        try JSONSerialization.data(
            withJSONObject: object,
            options: [.prettyPrinted, .sortedKeys]
        )
    }
}
