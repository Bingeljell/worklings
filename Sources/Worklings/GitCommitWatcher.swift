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
    /// When each repo's baseline was last established, used as the `--since`
    /// boundary so only commits made after that moment (i.e. while watching)
    /// are counted. In-memory: at launch the baseline resyncs to the current
    /// HEAD and this resets to now, so a relaunch never retro-counts.
    private var lastCheckedAt: [String: Date] = [:]
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
    func connect(path: String) async -> Bool {
        // Canonicalize to the repo top-level (off the main actor, so the
        // connecting click is never blocked on git), so connecting a
        // subdirectory, a symlink, or a `..`-laden path can't register the same
        // repository twice and double its rewards.
        guard let root = await Self.resolveTopLevel(path) else {
            return false
        }
        guard !registry.contains(path: root) else {
            return true
        }
        // Baseline starts nil and is set to the current HEAD by the off-main
        // resync below, so we never retro-credit history already behind the repo.
        registry.add(path: root, lastSeenSHA: nil)
        if isRunning {
            resyncBaselineAndWatch(path: root)
        }
        return true
    }

    private nonisolated static func resolveTopLevel(_ path: String) async -> String? {
        GitRepository.topLevel(atPath: path)
    }

    func disconnect(path: String) {
        sources[path]?.cancel()
        sources[path] = nil
        pendingChecks[path]?.cancel()
        pendingChecks[path] = nil
        lastCheckedAt[path] = nil
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
            self.lastCheckedAt[path] = Date()
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
        let since = lastCheckedAt[path] ?? Date.distantPast
        pendingChecks[path]?.cancel()
        pendingChecks[path] = Task { [weak self] in
            try? await Task.sleep(for: Self.debounce)
            guard !Task.isCancelled else {
                return
            }

            let outcome = await Self.evaluate(path: path, oldSHA: oldSHA, since: since)

            guard !Task.isCancelled, let self else {
                return
            }
            self.registry.updateLastSeenSHA(path: path, sha: outcome.newSHA)
            if outcome.newSHA != nil {
                self.lastCheckedAt[path] = Date()
            }
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
        oldSHA: String?,
        since: Date
    ) async -> (newSHA: String?, milestones: Int) {
        guard let newSHA = GitRepository.head(atPath: path) else {
            return (nil, 0)
        }
        let isAncestor = oldSHA.map { GitRepository.isAncestor($0, of: newSHA, atPath: path) } ?? false
        // `from: oldSHA` may be nil — an empty-at-connect repo whose first
        // commit(s) count from the root; recentCommitCount handles that.
        let recent = GitRepository.recentCommitCount(from: oldSHA, to: newSHA, since: since, atPath: path)
        let milestones = GitCommitDelta.milestonesToEmit(
            oldSHA: oldSHA,
            newSHA: newSHA,
            oldIsAncestorOfNew: isAncestor,
            commitsAhead: recent
        )
        return (newSHA, milestones)
    }
}
