import Foundation

/// Resolves the absolute path of an adapter script so a written config can
/// point at it. In a packaged `.app` the scripts ship inside
/// `Contents/Resources/adapters/` (copied there by the packaging step); running
/// from source (`swift run`) there is no such bundle, so it falls back to the
/// repo checkout located relative to this source file. Absolute either way,
/// which is what a hook `command` needs.
enum AdapterLocator {
    static func path(for name: String) -> String {
        if let resource = Bundle.main.resourceURL {
            let bundled = resource.appendingPathComponent("adapters/\(name)")
            if FileManager.default.fileExists(atPath: bundled.path) {
                return bundled.path
            }
        }

        // Dev fallback: this file lives at <repo>/Sources/Worklings/AdapterLocator.swift.
        let repoRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent() // Worklings
            .deletingLastPathComponent() // Sources
            .deletingLastPathComponent() // repo root
        return repoRoot.appendingPathComponent("scripts/adapters/\(name)").path
    }
}
