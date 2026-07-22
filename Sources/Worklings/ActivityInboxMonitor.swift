import CompanionCore
import Foundation

/// Watches the inbox spool directory external adapters drop event files into,
/// feeding each valid event to the session and deleting every file it
/// inspects — valid or not — so the directory never accumulates. Reads only
/// the fields `ActivityInbox` validates; a rejected file is logged by reason
/// and removed without influencing the pet.
@MainActor
final class ActivityInboxMonitor {
    private let session: PetSession
    private let directoryURL: URL
    private var directorySource: DispatchSourceFileSystemObject?
    /// Files that could not be deleted, remembered so a stuck file cannot be
    /// re-delivered on every subsequent drain.
    private var undeletableFileNames: Set<String> = []

    init(session: PetSession, directoryURL: URL = ActivityInboxMonitor.defaultDirectoryURL()) {
        self.session = session
        self.directoryURL = directoryURL
    }

    var inboxPath: String {
        directoryURL.path
    }

    /// Creates the directory if needed, drains anything already waiting, and
    /// begins watching. Fails closed: if the directory cannot be created or
    /// opened, the monitor logs and stays inert rather than erroring the app.
    func start() {
        guard directorySource == nil else {
            return
        }

        do {
            try FileManager.default.createDirectory(
                at: directoryURL,
                withIntermediateDirectories: true
            )
        } catch {
            NSLog("Worklings could not create the activity inbox: %@", String(describing: error))
            return
        }

        let descriptor = open(directoryURL.path, O_EVTONLY)
        guard descriptor >= 0 else {
            NSLog("Worklings could not open the activity inbox for watching.")
            return
        }

        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: descriptor,
            eventMask: .write,
            queue: .main
        )
        source.setEventHandler { [weak self] in
            self?.drain()
        }
        source.setCancelHandler {
            close(descriptor)
        }
        directorySource = source
        source.resume()

        drain()
    }

    func stop() {
        directorySource?.cancel()
        directorySource = nil
    }

    private func drain() {
        let fileManager = FileManager.default
        let fileURLs: [URL]
        do {
            fileURLs = try fileManager.contentsOfDirectory(
                at: directoryURL,
                includingPropertiesForKeys: nil,
                options: [.skipsHiddenFiles]
            )
        } catch {
            NSLog("Worklings could not read the activity inbox: %@", String(describing: error))
            return
        }

        let eventFileURLs = fileURLs.filter { $0.pathExtension == "json" }

        // Decode the whole batch before delivering any of it, so events can
        // be handed to the session in event-timestamp order regardless of how
        // adapters happened to name their files.
        var events: [ActivityEvent] = []
        for fileURL in eventFileURLs {
            let fileName = fileURL.lastPathComponent
            guard !undeletableFileNames.contains(fileName) else {
                continue
            }

            if let data = try? Data(contentsOf: fileURL) {
                switch ActivityInbox.decode(data, receivedAt: Date()) {
                case .success(let event):
                    events.append(event)
                case .failure(let rejection):
                    NSLog(
                        "Worklings discarded inbox file %@: %@",
                        fileName,
                        String(describing: rejection)
                    )
                }
            }

            do {
                try fileManager.removeItem(at: fileURL)
            } catch {
                undeletableFileNames.insert(fileName)
                NSLog("Worklings could not remove inbox file %@.", fileName)
            }
        }

        for event in ActivityInbox.ordered(events) {
            session.receive(event)
        }
    }

    static func defaultDirectoryURL() -> URL {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["WORKLINGS_INBOX_DIR"] {
            return URL(fileURLWithPath: override, isDirectory: true)
        }
        #endif

        let fileManager = FileManager.default
        let applicationSupportURL = fileManager.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first ?? fileManager.temporaryDirectory

        return applicationSupportURL
            .appendingPathComponent("Worklings", isDirectory: true)
            .appendingPathComponent("inbox", isDirectory: true)
    }
}
