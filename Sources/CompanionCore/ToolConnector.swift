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
        /// The config kept changing underneath us (another program or the user
        /// editing it) across every write attempt, so we stopped rather than
        /// risk overwriting a live edit with a stale merge.
        case configChangedDuringWrite(String)
    }

    /// How many times a write re-reads and retries when the config changed
    /// between our read and our write, before giving up and throwing.
    private static let maxWriteAttempts = 4

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

    /// Whether this tool carries our hooks at all, live or stale.
    public enum ConnectionState: Equatable {
        /// No hooks of ours are present.
        case notConnected
        /// Our hooks are present and point at an adapter that exists and is
        /// executable — the normal connected state.
        case live
        /// Our hooks are present but the adapter they point at is gone (the app
        /// was moved or deleted). The wiring is "ours" but no longer runnable;
        /// re-connecting repoints it at the current adapter.
        case stale
        /// The config file exists but could not be inspected — it is unreadable
        /// (permission/I-O) or not valid JSON. We cannot confirm *or* deny that
        /// our hooks are in it, so it must never be silently reported as
        /// "not connected": a caller cleaning up should treat this as a failure
        /// to resolve, not as "nothing to do."
        case unknown
    }

    public func isConnected() -> Bool {
        let state = connectionState()
        return state == .live || state == .stale
    }

    /// Distinguishes *"is this hook ours?"* (ownership, by the adapter file name,
    /// which survives an app relocation) from *"does it point at the currently
    /// installed adapter?"* (liveness) — and, crucially, from *"could we even
    /// read the file?"* A missing config is `.notConnected`; a present one we
    /// cannot read or parse is `.unknown`, never a false `.notConnected`.
    public func connectionState() -> ConnectionState {
        let data: Data
        do {
            guard let existing = try readExistingConfig() else { return .notConnected }
            data = existing
        } catch {
            return .unknown // present but unreadable (permission/I-O)
        }

        // An empty or whitespace-only file holds nothing of ours.
        let isBlank = data.allSatisfy { $0 == 0x20 || $0 == 0x0A || $0 == 0x0D || $0 == 0x09 }
        if data.isEmpty || isBlank { return .notConnected }

        // Present but not parseable JSON: our hooks might be in there, we just
        // can't tell. Fail loud (unknown), never a false "not connected".
        guard (try? JSONSerialization.jsonObject(with: data)) is [String: Any] else {
            return .unknown
        }

        let paths = HookConfigMerger.ourHookExecutablePaths(configJSON: data, adapterPath: adapterPath)
        guard !paths.isEmpty else { return .notConnected }
        let anyLive = paths.contains { FileManager.default.isExecutableFile(atPath: $0) }
        return anyLive ? .live : .stale
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
        return try commit { existing in
            // Merge onto the latest on-disk bytes. If this throws (unparseable
            // or unfamiliar structure) we return before writing or backing up,
            // so the file is left exactly as it was.
            try HookConfigMerger.connected(
                configJSON: existing,
                adapterPath: adapterPath,
                mappings: mappings,
                style: style
            )
        }
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
        return try commit { current in
            try HookConfigMerger.disconnected(
                configJSON: current,
                adapterPath: adapterPath
            )
        }
    }

    /// Reads the config, applies `transform`, and writes the result back
    /// atomically — closing the read→write race. Each attempt backs up the
    /// current file, then re-reads and compares against what `transform` was
    /// computed from, then — only if they still match — writes. The confirming
    /// re-read is the *last* thing before the atomic write, with nothing (not
    /// even the backup) between it and the rename, so a program (Claude, Codex)
    /// or the user editing the file during the backup can no longer be
    /// overwritten: the mismatch is caught and the attempt retries on the new
    /// bytes. After `maxWriteAttempts` racing passes it throws
    /// `configChangedDuringWrite` instead of writing stale data. Returns the
    /// backup URL if a file existed.
    ///
    /// A vanishing window remains between that confirming re-read and the rename;
    /// closing it fully would need file locking, which these tools don't
    /// coordinate on. Re-reading immediately before the write shrinks it to
    /// microseconds, which is what the safety requirement asks for.
    private func commit(transform: (Data) throws -> Data) throws -> URL? {
        var existing = try readExistingConfig() ?? Data()
        for _ in 0 ..< Self.maxWriteAttempts {
            let updated = try transform(existing)
            // Back up first, then confirm the file is still what we merged from
            // *immediately* before writing — the backup is no longer inside the
            // check→write window.
            let backup = try backUpExisting()
            let current = try readExistingConfig() ?? Data()
            if current == existing {
                try write(updated)
                return backup
            }
            // The file changed between our read and now: drop the possibly-stale
            // backup and retry on the newer contents rather than overwrite them.
            if let backup {
                try? FileManager.default.removeItem(at: backup)
            }
            existing = current
        }
        throw ConnectorError.configChangedDuringWrite(configURL.path)
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
