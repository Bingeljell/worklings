import Foundation

/// One connected repository: where it lives, and the HEAD it was last seen at
/// so a later check can tell what moved.
struct ConnectedRepo: Codable, Equatable {
    let path: String
    var lastSeenSHA: String?
}

/// The set of git repositories the user has explicitly connected, persisted as
/// JSON in UserDefaults.
///
/// The app is not sandboxed, so a plain filesystem path is enough to watch and
/// to run git against. A sandboxed build would instead store a security-scoped
/// bookmark here and resolve it on load — a change contained entirely to this
/// type.
struct ConnectedRepoRegistry {
    private static let defaultsKey = "connectedGitRepos"
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func all() -> [ConnectedRepo] {
        guard let data = defaults.data(forKey: Self.defaultsKey),
              let repos = try? JSONDecoder().decode([ConnectedRepo].self, from: data)
        else {
            return []
        }
        return repos
    }

    func contains(path: String) -> Bool {
        all().contains { $0.path == path }
    }

    private func save(_ repos: [ConnectedRepo]) {
        guard let data = try? JSONEncoder().encode(repos) else {
            return
        }
        defaults.set(data, forKey: Self.defaultsKey)
    }

    /// Adds a repo if not already present, recording its baseline HEAD.
    func add(path: String, lastSeenSHA: String?) {
        var repos = all()
        guard !repos.contains(where: { $0.path == path }) else {
            return
        }
        repos.append(ConnectedRepo(path: path, lastSeenSHA: lastSeenSHA))
        save(repos)
    }

    func remove(path: String) {
        save(all().filter { $0.path != path })
    }

    /// Advances a repo's baseline to a newly observed HEAD. A nil SHA is
    /// ignored so a transient git failure never wipes the baseline (which would
    /// make the next real commit look like a fresh connect and earn nothing).
    func updateLastSeenSHA(path: String, sha: String?) {
        guard let sha else {
            return
        }
        var repos = all()
        guard let index = repos.firstIndex(where: { $0.path == path }) else {
            return
        }
        repos[index].lastSeenSHA = sha
        save(repos)
    }

    func lastSeenSHA(path: String) -> String? {
        all().first { $0.path == path }?.lastSeenSHA
    }
}
