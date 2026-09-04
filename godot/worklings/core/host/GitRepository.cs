using Godot;

namespace Worklings.Core.Host;

/// A thin, synchronous wrapper over the `git` CLI for one repository path.
///
/// **It reads only commit identifiers and their ancestry** — never a message, a
/// diff, or a file path. That is the local-git source's structural privacy
/// promise holding at the exact boundary where it shells out, and it is why
/// every command here is a `rev-parse`, a `merge-base`, or a `rev-list --count`.
///
/// Every call is best effort. A missing repo, a detached state, an absent `git`,
/// or a wedged one returns null or a safe default rather than throwing: a
/// watcher must never be able to disrupt the app it is watching for.
///
/// Ported in behaviour from Sources/Worklings/GitRepository.swift. That one
/// hardcodes `/usr/bin/git`; this resolves `git` on PATH, so the same code has a
/// chance of working on the two platforms nothing here has ever run on.
public static class GitRepository
{
    /// A hung git — a stale network mount, a held index lock — must never wedge
    /// the caller. Everything here runs off the main thread anyway, but a
    /// background thread stuck forever is still a leak.
    private const int TimeoutMilliseconds = 5000;

    private readonly record struct Invocation(int Status, string Output);

    /// Runs `git -C <path> <arguments>` and captures trimmed stdout. stderr is
    /// discarded — a failure is a status code here, never a message worth
    /// showing.
    private static Invocation? Run(string path, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(path);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(start);
            if (process is null)
            {
                return null;
            }
            // Output here is a SHA or a count, so reading it whole before
            // waiting cannot fill the pipe and deadlock.
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return new Invocation(process.ExitCode, output.Trim());
        }
        catch (System.Exception)
        {
            // No git on PATH, no permission, no such directory. All the same
            // answer: this is not a repository we can watch.
            return null;
        }
    }

    public static bool IsRepository(string path) =>
        Run(path, "rev-parse", "--is-inside-work-tree") is { Status: 0, Output: "true" };

    /// The working tree's root, canonicalised, so a subdirectory, a `..`-laden
    /// path and a symlink into the same repo all collapse to one identity. That
    /// identity is what the registry keys on — without it the same repository
    /// connects twice and pays twice.
    public static string? TopLevel(string path)
    {
        if (Run(path, "rev-parse", "--show-toplevel") is not { Status: 0 } result
            || result.Output.Length == 0)
        {
            return null;
        }
        try
        {
            return System.IO.Path.GetFullPath(
                new System.IO.DirectoryInfo(result.Output).LinkTarget ?? result.Output);
        }
        catch (System.Exception)
        {
            return result.Output;
        }
    }

    /// The current HEAD, or null for an empty repo or an unreachable git.
    public static string? Head(string path)
    {
        if (Run(path, "rev-parse", "HEAD") is not { Status: 0 } result
            || result.Output.Length == 0)
        {
            return null;
        }
        return result.Output;
    }

    /// Whether `old` is an ancestor of `current` — whether HEAD moved by having
    /// commits added on top, rather than by an amend, a reset or a rebase
    /// rewriting what was there.
    public static bool IsAncestor(string old, string current, string path) =>
        Run(path, "merge-base", "--is-ancestor", old, current) is { Status: 0 };

    /// How many commits `current` is ahead of `old` that were **committed at or
    /// after `since`**.
    ///
    /// The recency filter is the whole trick behind "only commits made while
    /// watching". Fast-forwarding over old history — a pull, a branch checkout —
    /// advances HEAD by commits carrying old dates, which fall outside the
    /// window and earn nothing. A commit just made is inside it and counts.
    ///
    /// A null `old` counts from the root: the repo was empty when watching
    /// began, so its first commits have no baseline to range from.
    public static int RecentCommitCount(
        string? old, string current, System.DateTimeOffset since, string path)
    {
        // git's approxidate takes @<epoch>, which is unambiguous and free of
        // every timezone and formatter question a written date would raise.
        //
        // One sharp edge: `--since=@0` returns NOTHING rather than everything —
        // git reads epoch zero as no date at all. Callers must pass a real
        // instant, never a zero sentinel meaning "since forever", or the count
        // fails in the quiet direction.
        string sinceArgument = $"--since=@{since.ToUnixTimeSeconds()}";
        string range = old is null ? current : $"{old}..{current}";
        if (Run(path, "rev-list", "--count", sinceArgument, range) is not { Status: 0 } result
            || !int.TryParse(result.Output, out int count))
        {
            return 0;
        }
        return count;
    }
}
