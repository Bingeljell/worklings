import CompanionCore
import Foundation

/// Watches the inbox spool directory external adapters drop event files into,
/// feeding each valid event to the session and deleting every file it
/// inspects — valid or not — so the directory never accumulates. Reads only
/// the fields `ActivityInbox` validates; a rejected file is logged by reason
/// and removed without influencing the pet.
///
/// The directory watch fires on the main queue, but all listing, reading,
/// decoding, and deleting happens off the main actor; only delivering the
/// decoded events to the session returns to it.
@MainActor
final class ActivityInboxMonitor {
    private let session: PetSession
    private let directoryURL: URL
    private var directorySource: DispatchSourceFileSystemObject?
    /// Files that could not be deleted, remembered so a stuck file cannot be
    /// re-delivered on every subsequent drain.
    private var undeletableFileNames: Set<String> = []
    /// Coalesces bursts: one drain runs at a time, and any watch events that
    /// arrive mid-drain fold into a single follow-up pass.
    private var isDraining = false
    private var needsAnotherDrain = false

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
            self?.scheduleDrain()
        }
        source.setCancelHandler {
            close(descriptor)
        }
        directorySource = source
        source.resume()

        scheduleDrain()
    }

    func stop() {
        directorySource?.cancel()
        directorySource = nil
    }

    private func scheduleDrain() {
        guard !isDraining else {
            needsAnotherDrain = true
            return
        }
        isDraining = true

        let directoryURL = directoryURL
        let skipped = undeletableFileNames
        Task { [weak self] in
            let outcome = await Self.collectEvents(in: directoryURL, skipping: skipped)

            guard let self else {
                return
            }
            self.undeletableFileNames.formUnion(outcome.undeletableFileNames)
            for event in ActivityInbox.ordered(outcome.events) {
                self.session.receive(event)
            }

            self.isDraining = false
            if self.needsAnotherDrain {
                self.needsAnotherDrain = false
                self.scheduleDrain()
            }
        }
    }

    /// The blocking half of a drain: list, read, decode, and delete, all off
    /// the main actor. Returns the decoded events unordered; the caller
    /// orders them by event timestamp before delivery.
    private nonisolated static func collectEvents(
        in directoryURL: URL,
        skipping: Set<String>
    ) async -> (events: [ActivityEvent], undeletableFileNames: Set<String>) {
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
            return ([], [])
        }

        var events: [ActivityEvent] = []
        var undeletable: Set<String> = []
        for fileURL in fileURLs where fileURL.pathExtension == "json" {
            let fileName = fileURL.lastPathComponent
            guard !skipping.contains(fileName) else {
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
                undeletable.insert(fileName)
                NSLog("Worklings could not remove inbox file %@.", fileName)
            }
        }

        return (events, undeletable)
    }

    static func defaultDirectoryURL() -> URL {
        #if DEBUG
        if let override = ProcessInfo.processInfo.environment["WORKLINGS_INBOX_DIR"] {
            return URL(fileURLWithPath: override, isDirectory: true)
        }
        #endif

        return WorklingsDirectories.applicationSupport()
            .appendingPathComponent("inbox", isDirectory: true)
    }
}
