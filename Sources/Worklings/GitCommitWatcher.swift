import CompanionCore
import Foundation

/// Watches the `.git` directory of each connected repository and turns forward
/// commits into `milestone` events for the pet.
///
/// Mirrors `ActivityInboxMonitor`'s discipline: FSEvents fire on the main
/// queue, the (brief) git work runs off the main actor, and only commit
/// identifiers and ancestry are ever read — never a message or diff (see
/// `GitCommitDelta`). Connecting a repository is itself the opt-in, so this is
/// independent of the "Accept Work Tool Events" inbox toggle.
///
/// No retro-credit: on connect and on start, the current HEAD is recorded as a
/// silent baseline, so only commits made while the app is watching count. A
/// `.git` directory is chatty (index and lock churn), so each repo's check is
/// debounced and coalesced.
@MainActor
final class GitCommitWatcher {
    private let session: PetSession
    private let registry: ConnectedRepoRegistry
    private var sources: [String: DispatchSourceFileSystemObject] = [:]
    private var pendingChecks: [String: Task<Void, Never>] = [:]
    private var isRunning = false

    private static let debounce: Duration = .milliseconds(600)

    init(session: PetSession, registry: ConnectedRepoRegistry = ConnectedRepoRegistry()) {
        self.session = session
        self.registry = registry
    }

    func connectedRepoPaths() -> [String] {
        registry.all().map(\.path)
    }

    /// Begins watching every connected repo, resyncing each baseline to its
    /// current HEAD without emitting — commits made while the app was closed
    /// are not retro-credited.
    func start() {
        guard !isRunning else {
            return
        }
        isRunning = true
        for repo in registry.all() {
            resyncBaselineAndWatch(path: repo.path)
        }
    }

    func stop() {
        isRunning = false
        for source in sources.values {
            source.cancel()
        }
        sources.removeAll()
        for task in pendingChecks.values {
            task.cancel()
        }
        pendingChecks.removeAll()
    }

    /// Connects a repo: records a silent baseline at the current HEAD and starts
    /// watching. Returns false (and does nothing) if the path is not a git
    /// repository. A no-op if already connected.
    @discardableResult
    func connect(path: String) -> Bool {
        guard GitRepository.isRepository(atPath: path) else {
            return false
        }
        guard !registry.contains(path: path) else {
            return true
        }
        // Baseline starts nil and is set to the current HEAD by the off-main
        // resync below, so we never retro-credit history already behind the
        // repo, and the connecting click is not blocked on git.
        registry.add(path: path, lastSeenSHA: nil)
        if isRunning {
            resyncBaselineAndWatch(path: path)
        }
        return true
    }

    func disconnect(path: String) {
        sources[path]?.cancel()
        sources[path] = nil
        pendingChecks[path]?.cancel()
        pendingChecks[path] = nil
        registry.remove(path: path)
    }

    /// Resolves HEAD and the `.git` directory off the main actor, then applies
    /// the baseline and installs the watch back on main. No git call ever runs
    /// on the main thread here, so neither launch nor a connect click can be
    /// blocked by a slow or wedged repository.
    private func resyncBaselineAndWatch(path: String) {
        Task { [weak self] in
            let info = await Self.repoInfo(path: path)
            guard let self, self.isRunning else {
                return
            }
            self.registry.updateLastSeenSHA(path: path, sha: info.head)
            if let gitDirectory = info.gitDirectory {
                self.installWatch(path: path, gitDirectory: gitDirectory)
            }
        }
    }

    private nonisolated static func repoInfo(path: String) async -> (head: String?, gitDirectory: String?) {
        (GitRepository.head(atPath: path), GitRepository.gitDirectoryPath(atPath: path))
    }

    private func installWatch(path: String, gitDirectory: String) {
        guard sources[path] == nil else {
            return
        }

        let descriptor = open(gitDirectory, O_EVTONLY)
        guard descriptor >= 0 else {
            NSLog("Worklings could not open %@ for git watching.", gitDirectory)
            return
        }

        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: descriptor,
            eventMask: [.write, .rename, .delete],
            queue: .main
        )
        source.setEventHandler { [weak self] in
            self?.scheduleCheck(path: path)
        }
        source.setCancelHandler {
            close(descriptor)
        }
        sources[path] = source
        source.resume()
    }

    /// Coalesces a burst of `.git` writes into one debounced check per repo.
    private func scheduleCheck(path: String) {
        let oldSHA = registry.lastSeenSHA(path: path)
        pendingChecks[path]?.cancel()
        pendingChecks[path] = Task { [weak self] in
            try? await Task.sleep(for: Self.debounce)
            guard !Task.isCancelled else {
                return
            }

            let outcome = await Self.evaluate(path: path, oldSHA: oldSHA)

            guard !Task.isCancelled, let self else {
                return
            }
            self.registry.updateLastSeenSHA(path: path, sha: outcome.newSHA)
            guard outcome.milestones > 0 else {
                return
            }
            // One milestone per genuine new commit; the per-source daily count
            // in PetBrain applies the diminishing returns from here.
            let now = Date()
            for _ in 0..<outcome.milestones {
                self.session.receive(GitActivitySource.event(.milestone, at: now))
            }
        }
    }

    /// The git-touching half, run off the main actor. Pure decision lives in
    /// `GitCommitDelta`; this only gathers the facts it needs.
    private nonisolated static func evaluate(
        path: String,
        oldSHA: String?
    ) async -> (newSHA: String?, milestones: Int) {
        guard let newSHA = GitRepository.head(atPath: path) else {
            return (nil, 0)
        }
        let isAncestor = oldSHA.map { GitRepository.isAncestor($0, of: newSHA, atPath: path) } ?? false
        let ahead = oldSHA.map { GitRepository.commitsAhead(from: $0, to: newSHA, atPath: path) } ?? 0
        let milestones = GitCommitDelta.milestonesToEmit(
            oldSHA: oldSHA,
            newSHA: newSHA,
            oldIsAncestorOfNew: isAncestor,
            commitsAhead: ahead
        )
        return (newSHA, milestones)
    }
}
