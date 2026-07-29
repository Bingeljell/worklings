import Foundation

/// Writes and removes a tool's Worklings hook wiring on disk, safely. It merges
/// via `HookConfigMerger`, then adds the never-brick file-IO guarantees around
/// it: the merge runs *before* anything is touched, so an unparseable config
/// throws and leaves the file exactly as it was; an existing config is copied
/// to a timestamped backup before being overwritten; and the write is atomic
/// (temp file + rename), so a config is never left half-written.
public struct ToolConnector {
    public let configURL: URL
    public let adapterPath: String
    public let mappings: [HookConfigMerger.Mapping]
    public let style: HookConfigMerger.CommandStyle

    public enum ConnectorError: Error, Equatable {
        /// The adapter script we would wire the tool to is missing or not
        /// executable. Refusing to write means a tool is never pointed at a
        /// command that fails to launch — which for Codex (exit 2 blocks a turn)
        /// could stall the user's session.
        case adapterUnavailable(String)
        /// A config file exists on disk but could not be read (permission, I/O,
        /// allocation). We fail closed rather than treat it as empty: merging
        /// onto a blank base and then backing up would replace the live config.
        case existingConfigUnreadable(String)
    }

    public init(
        configURL: URL,
        adapterPath: String,
        mappings: [HookConfigMerger.Mapping],
        style: HookConfigMerger.CommandStyle
    ) {
        self.configURL = configURL
        self.adapterPath = adapterPath
        self.mappings = mappings
        self.style = style
    }

    public func isConnected() -> Bool {
        // Best-effort query: an unreadable config (throws) or a missing one (nil)
        // both count as "not connected" — we cannot confirm our hooks are there.
        guard let data = (try? readExistingConfig()) ?? nil else { return false }
        return HookConfigMerger.isConnected(configJSON: data, adapterPath: adapterPath)
    }

    /// Merges our hooks in. Returns the backup URL if one was made. Throws
    /// (leaving the file untouched, no backup written) if the existing config
    /// is present but not valid JSON.
    @discardableResult
    public func connect() throws -> URL? {
        // Never wire a tool to a command that cannot run: verify the adapter
        // exists and is executable before touching the config.
        guard FileManager.default.isExecutableFile(atPath: adapterPath) else {
            throw ConnectorError.adapterUnavailable(adapterPath)
        }
        // Read fail-closed: a present-but-unreadable config throws here, before
        // any backup or write, so we never merge our hooks onto an empty base
        // and clobber a config we simply could not read.
        let existing = try readExistingConfig() ?? Data()
        // Merge first: if this throws, we return before writing or backing up,
        // so the file on disk is untouched.
        let merged = try HookConfigMerger.connected(
            configJSON: existing,
            adapterPath: adapterPath,
            mappings: mappings,
            style: style
        )
        let backup = try backUpExisting()
        try write(merged)
        return backup
    }

    /// Removes only our hooks. Returns the backup URL if one was made. A no-op
    /// if there is no config file yet.
    @discardableResult
    public func disconnect() throws -> URL? {
        // No config yet → nothing of ours to remove. A present-but-unreadable
        // config throws (fail closed) rather than silently reporting success.
        guard let existing = try readExistingConfig(), !existing.isEmpty else {
            return nil
        }
        let updated = try HookConfigMerger.disconnected(
            configJSON: existing,
            adapterPath: adapterPath
        )
        let backup = try backUpExisting()
        try write(updated)
        return backup
    }

    /// Reads the existing config, distinguishing "no file yet" (returns nil)
    /// from "file is present but unreadable" (throws `existingConfigUnreadable`).
    /// This is the fail-closed boundary: callers must never treat an unreadable
    /// config as empty.
    private func readExistingConfig() throws -> Data? {
        guard FileManager.default.fileExists(atPath: configURL.path) else {
            return nil
        }
        do {
            return try Data(contentsOf: configURL)
        } catch {
            throw ConnectorError.existingConfigUnreadable(configURL.path)
        }
    }

    /// Copies the current config aside as a timestamped backup, if it exists.
    private func backUpExisting() throws -> URL? {
        guard FileManager.default.fileExists(atPath: configURL.path) else {
            return nil
        }
        let backupURL = configURL.deletingLastPathComponent()
            .appendingPathComponent("\(configURL.lastPathComponent).worklings-backup-\(Self.timestamp())")
        // Copy, not move, so the original stays in place until the atomic write.
        if FileManager.default.fileExists(atPath: backupURL.path) {
            try FileManager.default.removeItem(at: backupURL)
        }
        try FileManager.default.copyItem(at: configURL, to: backupURL)
        return backupURL
    }

    private func write(_ data: Data) throws {
        try FileManager.default.createDirectory(
            at: configURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try data.write(to: configURL, options: .atomic)
    }

    private static func timestamp() -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyyMMdd-HHmmssSSS"
        return formatter.string(from: Date())
    }
}
