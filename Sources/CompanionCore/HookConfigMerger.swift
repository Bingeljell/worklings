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

    /// How the hook command is written, so a path with a space or a shell
    /// metacharacter can never break or be interpreted when the hook runs.
    public enum CommandStyle: Sendable, Equatable {
        /// `{"command": <path>, "args": [<kind>]}` — the tool spawns the
        /// executable directly with no shell, so the path is passed verbatim and
        /// needs no quoting. Claude Code supports this and it is the safest form.
        case execForm
        /// `{"command": "'<path>' <kind>"}` — the tool runs the string through a
        /// shell, so the path is single-quoted to survive spaces and metachars.
        /// Used for Codex, which documents only the shell form.
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

    private static func ourEntry(adapterPath: String, mapping: Mapping, style: CommandStyle) -> [String: Any] {
        let hook: [String: Any]
        switch style {
        case .execForm:
            // No shell: the path is the whole command, the kind a separate arg.
            hook = ["type": "command", "command": adapterPath, "args": [mapping.kind]]
        case .shellForm:
            hook = ["type": "command", "command": "\(singleQuoted(adapterPath)) \(mapping.kind)"]
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

    private static func hookIsOurs(_ hook: [String: Any], adapterPath: String) -> Bool {
        guard let command = hook["command"] as? String else {
            return false
        }
        // Exec form: `command` is the executable itself (args are separate).
        // Shell form: the executable is the first, possibly single-quoted, token.
        let executable: String?
        if hook["args"] != nil {
            executable = command
        } else {
            executable = executablePath(ofCommand: command)
        }
        guard let executable else { return false }
        // Match by exact file name of the invoked executable — so a moved or
        // reinstalled app bundle (whose absolute path changed) still recognizes
        // and cleans up its own hooks. The adapter names are Worklings-namespaced
        // (`worklings-…-activity-hook`), so an exact file-name match is a reliable
        // ownership signal: a differently-named user script — whether it merely
        // contains our name as a substring or reuses a generic stem like
        // `codex-hook` — is never claimed.
        return (executable as NSString).lastPathComponent == (adapterPath as NSString).lastPathComponent
    }

    /// The executable (first shell word) of a hook command. Reverses the POSIX
    /// quoting `singleQuoted` applies, so a path containing a space *or* an
    /// apostrophe round-trips: an embedded quote is written as `'\''` (close,
    /// backslash-escaped quote, reopen), and this walks single-quoted spans and
    /// backslash escapes to recover the literal path. A plain unquoted command
    /// (a hand-written config) reduces to its first whitespace-delimited token.
    private static func executablePath(ofCommand command: String) -> String? {
        var result = ""
        var sawToken = false
        var index = command.startIndex
        let end = command.endIndex

        // Skip leading whitespace before the first token.
        while index < end, command[index] == " " || command[index] == "\t" {
            index = command.index(after: index)
        }

        loop: while index < end {
            let character = command[index]
            switch character {
            case " ", "\t":
                break loop // top-level whitespace ends the first word
            case "'":
                // A single-quoted span: everything up to the next quote is literal.
                sawToken = true
                index = command.index(after: index)
                while index < end, command[index] != "'" {
                    result.append(command[index])
                    index = command.index(after: index)
                }
                guard index < end else { return nil } // unterminated quote
                index = command.index(after: index) // consume the closing quote
            case "\\":
                // A backslash escapes the next character — this is how an embedded
                // quote survives between single-quoted spans (the `\'` in `'\''`).
                sawToken = true
                index = command.index(after: index)
                guard index < end else { return nil }
                result.append(command[index])
                index = command.index(after: index)
            default:
                sawToken = true
                result.append(character)
                index = command.index(after: index)
            }
        }
        return sawToken ? result : nil
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
