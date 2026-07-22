import Foundation

/// The one resolver for Worklings' on-disk footprint, so the save store and
/// the activity inbox can never drift into different base directories.
enum WorklingsDirectories {
    /// The user's Application Support directory, with the temporary
    /// directory as the same last-resort fallback everywhere.
    static func applicationSupportBase() -> URL {
        let fileManager = FileManager.default
        return fileManager.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first ?? fileManager.temporaryDirectory
    }

    /// `Application Support/Worklings`, the home of everything the app
    /// stores.
    static func applicationSupport() -> URL {
        applicationSupportBase().appendingPathComponent("Worklings", isDirectory: true)
    }
}
