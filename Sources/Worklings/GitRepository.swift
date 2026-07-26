import Foundation

/// A thin, synchronous wrapper over the `git` CLI for a single repository path.
///
/// It reads only commit identifiers and their ancestry — never a commit
/// message, diff, or file path — so the local-git source's structural privacy
/// promise holds at the exact boundary where it shells out. Every call is best
/// effort: a missing repo, a detached state, or an absent `git` returns nil or
/// a safe default rather than throwing, because a watcher must never disrupt
/// the app.
enum GitRepository {
    private static let executableURL = URL(fileURLWithPath: "/usr/bin/git")

    private struct Invocation {
        let status: Int32
        let output: String
    }

    /// A hung git — a stale network mount, a held index lock — must never wedge
    /// the caller. If a call overruns this, we terminate it and return nil.
    private static let timeout: TimeInterval = 5

    /// Carries the process, its pipe, and the drained output across the
    /// background reader so the dispatched closure captures one Sendable box
    /// rather than the non-Sendable `Process`/`Pipe` directly.
    private final class InvocationBox: @unchecked Sendable {
        let process: Process
        let outPipe: Pipe
        var output = Data()
        init(process: Process, outPipe: Pipe) {
            self.process = process
            self.outPipe = outPipe
        }
    }

    /// Runs `git -C <path> <arguments>`, capturing trimmed stdout. stderr is
    /// discarded. Returns nil if the process could not be launched, or if it
    /// exceeded `timeout` (in which case it is terminated) — so a wedged git can
    /// never block, whether this is called on the main thread or off it.
    private static func run(_ arguments: [String], inDirectory path: String) -> Invocation? {
        let process = Process()
        process.executableURL = executableURL
        process.arguments = ["-C", path] + arguments
        let outPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = FileHandle.nullDevice

        do {
            try process.run()
        } catch {
            return nil
        }

        // Drain stdout and await exit on a background thread; the caller waits
        // with a timeout. Reading off-thread means a timeout-terminate is never
        // blocked behind a stuck read. Outputs here are tiny (a SHA, a count).
        let box = InvocationBox(process: process, outPipe: outPipe)
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            box.output = box.outPipe.fileHandleForReading.readDataToEndOfFile()
            box.process.waitUntilExit()
            done.signal()
        }

        if done.wait(timeout: .now() + timeout) == .timedOut {
            process.terminate()
            return nil
        }

        let output = String(decoding: box.output, as: UTF8.self)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return Invocation(status: process.terminationStatus, output: output)
    }

    /// Whether the path is inside a git working tree we can watch.
    static func isRepository(atPath path: String) -> Bool {
        guard let result = run(["rev-parse", "--is-inside-work-tree"], inDirectory: path) else {
            return false
        }
        return result.status == 0 && result.output == "true"
    }

    /// The absolute path of the repository's `.git` directory — the thing to
    /// watch. Resolves correctly for linked worktrees, whose `.git` is a file
    /// pointing elsewhere.
    static func gitDirectoryPath(atPath path: String) -> String? {
        guard let result = run(["rev-parse", "--absolute-git-dir"], inDirectory: path),
              result.status == 0, !result.output.isEmpty
        else {
            return nil
        }
        return result.output
    }

    /// The current HEAD commit SHA, or nil if there is none yet (an empty repo)
    /// or git could not be reached.
    static func head(atPath path: String) -> String? {
        guard let result = run(["rev-parse", "HEAD"], inDirectory: path),
              result.status == 0, !result.output.isEmpty
        else {
            return nil
        }
        return result.output
    }

    /// Whether `old` is an ancestor of `new` — i.e. `new` was reached by adding
    /// commits on top, not by rewriting history (amend/reset/rebase).
    static func isAncestor(_ old: String, of new: String, atPath path: String) -> Bool {
        guard let result = run(
            ["merge-base", "--is-ancestor", old, new],
            inDirectory: path
        ) else {
            return false
        }
        return result.status == 0
    }

    /// How many commits `new` is ahead of `old` (`git rev-list --count old..new`).
    static func commitsAhead(from old: String, to new: String, atPath path: String) -> Int {
        guard let result = run(
            ["rev-list", "--count", "\(old)..\(new)"],
            inDirectory: path
        ), result.status == 0 else {
            return 0
        }
        return Int(result.output) ?? 0
    }
}
