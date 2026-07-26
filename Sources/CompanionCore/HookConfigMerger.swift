import Foundation

/// Merges Worklings' command-hook entries into a tool's JSON hook config. Both
/// tools we target use the same shape — a top-level `"hooks"` object mapping an
/// event name to an array of `{ "hooks": [ { "type": "command", "command" } ] }`
/// — so one merger serves Claude Code's `settings.json` and Codex's `hooks.json`.
///
/// Pure and total on its inputs, so the "never brick an existing config"
/// contract is provable by checks: every existing key, every existing hook, and
/// every unrelated entry under a mapped event is preserved; re-connecting is
/// idempotent; and a config that is present but not valid JSON makes `connected`
/// throw rather than overwrite something it cannot understand. The app target
/// owns reading the file, backing it up, and writing it atomically.
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
    }

    /// Returns `configJSON` with our hook entry for each mapping merged into the
    /// top-level `"hooks"` object. Existing content is preserved; our own prior
    /// entries (matched by the adapter's file name) are replaced rather than
    /// duplicated, so this is idempotent. Empty/whitespace input is treated as
    /// an empty object. Throws `unparseableConfig` if the input is non-empty and
    /// not a JSON object.
    public static func connected(
        configJSON: Data,
        adapterPath: String,
        mappings: [Mapping]
    ) throws -> Data {
        var root = try object(from: configJSON)
        var hooks = root["hooks"] as? [String: Any] ?? [:]
        let marker = adapterName(adapterPath)

        for mapping in mappings {
            var entries = (hooks[mapping.event] as? [[String: Any]]) ?? []
            entries.removeAll { entryIsOurs($0, adapterName: marker) }
            entries.append([
                "hooks": [
                    ["type": "command", "command": "\(adapterPath) \(mapping.kind)"]
                ]
            ])
            hooks[mapping.event] = entries
        }

        root["hooks"] = hooks
        return try data(from: root)
    }

    /// Returns `configJSON` with only our hook entries (matched by the adapter's
    /// file name) removed, leaving every other key, event, and entry intact. An
    /// event left with no entries is dropped, and an emptied `"hooks"` object is
    /// removed, so disconnecting restores the file to its pre-connect shape.
    public static func disconnected(
        configJSON: Data,
        adapterPath: String
    ) throws -> Data {
        var root = try object(from: configJSON)
        guard let hooks = root["hooks"] as? [String: Any] else {
            return try data(from: root)
        }
        let marker = adapterName(adapterPath)

        var kept: [String: Any] = [:]
        for (event, value) in hooks {
            guard let entries = value as? [[String: Any]] else {
                kept[event] = value // preserve anything not shaped like our entries
                continue
            }
            let remaining = entries.filter { !entryIsOurs($0, adapterName: marker) }
            if !remaining.isEmpty {
                kept[event] = remaining
            }
        }

        if kept.isEmpty {
            root["hooks"] = nil
        } else {
            root["hooks"] = kept
        }
        return try data(from: root)
    }

    /// Whether any of our hook entries are present.
    public static func isConnected(configJSON: Data, adapterPath: String) -> Bool {
        guard let root = try? object(from: configJSON),
              let hooks = root["hooks"] as? [String: Any] else {
            return false
        }
        let marker = adapterName(adapterPath)
        for value in hooks.values {
            guard let entries = value as? [[String: Any]] else { continue }
            if entries.contains(where: { entryIsOurs($0, adapterName: marker) }) {
                return true
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

    // MARK: - Helpers

    private static func adapterName(_ path: String) -> String {
        (path as NSString).lastPathComponent
    }

    /// An entry is ours if any of its command hooks invokes our adapter script,
    /// matched by file name so a moved bundle path still cleans up.
    private static func entryIsOurs(_ entry: [String: Any], adapterName: String) -> Bool {
        guard let hooks = entry["hooks"] as? [[String: Any]] else {
            return false
        }
        return hooks.contains { hook in
            guard let command = hook["command"] as? String else {
                return false
            }
            return command.contains(adapterName)
        }
    }

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
