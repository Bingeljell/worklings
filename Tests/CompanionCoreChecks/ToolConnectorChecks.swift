import CompanionCore
import Foundation

enum ToolConnectorChecks {
    static func run(context: inout CheckContext) {
        checkConnectCreatesFreshConfigWithoutBackup(context: &context)
        checkConnectBacksUpAndMergesExisting(context: &context)
        checkConnectLeavesUnparseableConfigUntouched(context: &context)
        checkDisconnectRemovesOurs(context: &context)
    }

    private static let adapter = "/Applications/Worklings.app/Contents/Resources/adapters/claude-code-hook"

    private static func tempDir() -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("worklings-connector-\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private static func connector(_ configURL: URL) -> ToolConnector {
        ToolConnector(configURL: configURL, adapterPath: adapter, mappings: HookConfigMerger.claudeCodeMappings)
    }

    private static func backups(in dir: URL) -> [URL] {
        let all = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
        return all.filter { $0.lastPathComponent.contains("worklings-backup") }
    }

    private static func checkConnectCreatesFreshConfigWithoutBackup(context: inout CheckContext) {
        let dir = tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let config = dir.appendingPathComponent("settings.json")
        let c = connector(config)

        do {
            let backup = try c.connect()
            context.expect(backup == nil, "connecting a non-existent config makes no backup")
            context.expect(c.isConnected(), "connecting a fresh config writes our hooks")
            context.expect(backups(in: dir).isEmpty, "no backup file is left behind for a fresh connect")
        } catch {
            context.expect(false, "connecting a fresh config should succeed: \(error)")
        }
    }

    private static func checkConnectBacksUpAndMergesExisting(context: inout CheckContext) {
        let dir = tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let config = dir.appendingPathComponent("settings.json")
        let original = Data(#"{"model":"opus"}"#.utf8)
        try? original.write(to: config)

        do {
            let backup = try connector(config).connect()
            context.expect(backup != nil, "connecting an existing config makes a backup")
            if let backup {
                context.expectEqual(
                    try? Data(contentsOf: backup),
                    original,
                    "the backup holds the original bytes verbatim"
                )
            }
            context.expect(connector(config).isConnected(), "the live config now carries our hooks")
            let root = (try? JSONSerialization.jsonObject(with: try Data(contentsOf: config))) as? [String: Any]
            context.expectEqual(root?["model"] as? String, "opus", "the merged config still has the user's keys")
        } catch {
            context.expect(false, "connecting an existing config should succeed: \(error)")
        }
    }

    private static func checkConnectLeavesUnparseableConfigUntouched(context: inout CheckContext) {
        let dir = tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let config = dir.appendingPathComponent("settings.json")
        let garbage = Data("not json { oops".utf8)
        try? garbage.write(to: config)

        context.expectThrows("connecting refuses to write over an unparseable config") {
            _ = try connector(config).connect()
        }
        context.expectEqual(
            try? Data(contentsOf: config),
            garbage,
            "an unparseable config is left byte-for-byte untouched"
        )
        context.expect(
            backups(in: dir).isEmpty,
            "no backup is written when the connect is refused"
        )
    }

    private static func checkDisconnectRemovesOurs(context: inout CheckContext) {
        let dir = tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let config = dir.appendingPathComponent("settings.json")
        let c = connector(config)

        do {
            _ = try c.connect()
            context.expect(c.isConnected(), "precondition: connected")
            _ = try c.disconnect()
            context.expect(!c.isConnected(), "disconnecting removes our hooks from the live config")
        } catch {
            context.expect(false, "connect/disconnect round trip should succeed: \(error)")
        }
    }
}
