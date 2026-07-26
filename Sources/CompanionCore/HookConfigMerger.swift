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
/// script (`my-codex-hook-wrapper`) is never mistaken for ours.
public enum HookConfigMerger {
    public struct Mapping: Sendable, Equatable {
        public let event: String
        public let kind: String
        public init(event: String, kind: String) {
            self.event = event
            self.kind = kind
        }
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
        mappings: [Mapping]
    ) throws -> Data {
        var root = try object(from: configJSON)
        var hooks = try hooksObject(from: root)

        for mapping in mappings {
            var entries = try existingEntries(in: hooks, event: mapping.event)
            // Strip our own prior hooks (a sibling hook in a shared entry is
            // preserved) so re-connecting is idempotent, then append ours.
            entries = strippingOurHooks(from: entries, adapterPath: adapterPath)
            entries.append(ourEntry(adapterPath: adapterPath, kind: mapping.kind))
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

    /// Claude Code's full lifecycle. `Notification` carries no content we read.
    public static let claudeCodeMappings: [Mapping] = [
        Mapping(event: "SessionStart", kind: "workStarted"),
        Mapping(event: "Stop", kind: "taskCompleted"),
        Mapping(event: "Notification", kind: "awaitingInput"),
        Mapping(event: "SessionEnd", kind: "workEnded")
    ]

    /// Codex's lifecycle; it has no documented "awaiting input" event.
    public static let codexMappings: [Mapping] = [
        Mapping(event: "SessionStart", kind: "workStarted"),
        Mapping(event: "Stop", kind: "taskCompleted"),
        Mapping(event: "SessionEnd", kind: "workEnded")
    ]

    // MARK: - Building our entry

    private static func ourEntry(adapterPath: String, kind: String) -> [String: Any] {
        ["hooks": [["type": "command", "command": ourCommand(adapterPath: adapterPath, kind: kind)]]]
    }

    private static func ourCommand(adapterPath: String, kind: String) -> String {
        "\(adapterPath) \(kind)"
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
        guard let command = hook["command"] as? String,
              let executable = executablePath(ofCommand: command) else {
            return false
        }
        // Match by exact file name of the invoked executable — so a moved bundle
        // path still cleans up, but a differently-named user script that merely
        // contains our name as a substring is never claimed.
        return (executable as NSString).lastPathComponent == (adapterPath as NSString).lastPathComponent
    }

    /// The executable (first token) of a hook command, handling a single-quoted
    /// path (which is how we write paths that need shell-safety).
    private static func executablePath(ofCommand command: String) -> String? {
        let trimmed = command.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return nil }
        if trimmed.hasPrefix("'") {
            let afterQuote = trimmed.dropFirst()
            guard let end = afterQuote.firstIndex(of: "'") else { return nil }
            return String(afterQuote[..<end])
        }
        return trimmed.split(separator: " ", maxSplits: 1).first.map(String.init)
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
