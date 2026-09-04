using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// Watches each connected repository's HEAD and turns forward commits into
/// milestones.
///
/// **No retro-credit.** On the first pass over a repo, the current HEAD is
/// recorded as a silent baseline and nothing is emitted. Only commits made while
/// the app is actually watching ever count — which is also why the count is
/// filtered by commit date rather than by graph distance, so a `pull` that
/// fast-forwards over a hundred old commits earns exactly nothing.
///
/// The decision — what a HEAD movement represents — is `GitCommitDelta`, ported
/// and probed. This gathers the facts it needs and delivers the result.
///
/// **Polling on a background thread, not FSEvents.** The Swift app watches the
/// `.git` directory with a `DispatchSource` and debounces the churn. Godot has
/// no equivalent, so this asks every fifteen seconds — and asks from a worker
/// thread, because `git` on a network mount can hang for seconds and the pet
/// must not freeze while it does. Only the delivery happens on the main thread.
///
/// Ported in behaviour from Sources/Worklings/GitCommitWatcher.swift.
public sealed partial class GitCommitWatcher : Node
{
    /// How often to look. Fifteen seconds: a commit is not urgent, and each
    /// pass spawns a `git` process per repository.
    [Export] public double PollSeconds { get; set; } = 15;

    private readonly PetSession _session;
    private readonly ConnectedRepoRegistry _registry;

    /// When each repo's baseline was established, used as the `--since`
    /// boundary. In memory rather than on disk, so a relaunch resets it along
    /// with the baseline and can never retro-count.
    private readonly Dictionary<string, System.DateTimeOffset> _baselinedAt = new();

    /// One pass at a time. A slow repo must not stack passes behind it.
    private volatile bool _busy;

    /// What the worker found, waiting to be delivered on the main thread.
    private readonly List<Finding> _pending = new();

    private double _timer;

    private readonly record struct Finding(string Path, string? Sha, int Milestones);

    public GitCommitWatcher(PetSession session, ConnectedRepoRegistry? registry = null)
    {
        _session = session;
        _registry = registry ?? new ConnectedRepoRegistry();
    }

    public IReadOnlyList<ConnectedRepo> Connected => _registry.All();

    /// Connects a repository, or says why it could not.
    ///
    /// Canonicalised to the working tree's root first, so connecting a
    /// subdirectory or a symlinked path cannot register the same repo twice and
    /// double what it pays.
    public string? Connect(string path)
    {
        string? root = GitRepository.TopLevel(path);
        if (root is null || !GitRepository.IsRepository(root))
        {
            return $"{path} is not a git repository.";
        }
        if (_registry.Contains(root))
        {
            return $"{root} is already connected.";
        }
        _registry.Add(root);
        // Baselined on the next pass rather than here, so the one place that
        // decides "this is where we started counting" is the one place.
        GD.Print($"git: watching {root}");
        return null;
    }

    public void Disconnect(string path)
    {
        _registry.Remove(path);
        _baselinedAt.Remove(path);
    }

    public override void _Ready()
    {
        var connected = _registry.All();
        GD.Print(connected.Count == 0
            ? "git: no repositories connected."
            : $"git: {connected.Count} repositor{(connected.Count == 1 ? "y" : "ies")} connected.");
        // Immediately, so the baseline is set at launch rather than fifteen
        // seconds into it — a commit made in that window would otherwise be
        // counted against a baseline from the previous session.
        Poll();
    }

    public override void _Process(double delta)
    {
        Deliver();

        _timer -= delta;
        if (_timer > 0) return;
        _timer = PollSeconds;
        Poll();
    }

    private void Poll()
    {
        if (_busy) return;
        var repos = _registry.All();
        if (repos.Count == 0) return;

        // Snapshot everything the worker needs, so it never touches the
        // registry or the session from another thread.
        var work = new List<(string Path, string? Sha, System.DateTimeOffset? Since)>();
        foreach (var repo in repos)
        {
            // Null means "never baselined in this session", which is a distinct
            // thing from any instant — not a sentinel date. `--since=@0` makes
            // git return nothing at all rather than everything, so a sentinel
            // that could reach the command line would fail in the quiet
            // direction: no commits found, no milestones, no error.
            work.Add((repo.Path, repo.LastSeenSha,
                      _baselinedAt.TryGetValue(repo.Path, out var at)
                          ? at
                          : (System.DateTimeOffset?)null));
        }

        _busy = true;
        System.Threading.Tasks.Task.Run(() =>
        {
            var found = new List<Finding>();
            foreach (var (path, oldSha, since) in work)
            {
                found.Add(Evaluate(path, oldSha, since));
            }
            lock (_pending)
            {
                _pending.AddRange(found);
            }
            _busy = false;
        });
    }

    /// The git-touching half, off the main thread. Gathers facts only; the
    /// judgement is `GitCommitDelta`'s.
    private static Finding Evaluate(
        string path, string? oldSha, System.DateTimeOffset? since)
    {
        string? newSha = GitRepository.Head(path);
        if (newSha is null)
        {
            // An empty repo, or a git we could not reach. Either way there is
            // nothing to say and nothing to record.
            return new Finding(path, null, 0);
        }

        // No baseline in this session means the repo is being seen for the
        // first time. Record where it is and emit nothing — this is the
        // no-retro-credit rule, and it is why the first pass over a repository
        // with years of history in it is silent.
        if (since is not System.DateTimeOffset from)
        {
            return new Finding(path, newSha, 0);
        }

        bool isAncestor = oldSha is not null && GitRepository.IsAncestor(oldSha, newSha, path);
        int recent = GitRepository.RecentCommitCount(oldSha, newSha, from, path);
        int milestones = GitCommitDelta.MilestonesToEmit(
            oldSha, newSha, isAncestor, recent);
        return new Finding(path, newSha, milestones);
    }

    /// Applies whatever the worker found, on the main thread.
    private void Deliver()
    {
        List<Finding> found;
        lock (_pending)
        {
            if (_pending.Count == 0) return;
            found = new List<Finding>(_pending);
            _pending.Clear();
        }

        var now = System.DateTimeOffset.Now;
        foreach (var finding in found)
        {
            _registry.UpdateLastSeenSha(finding.Path, finding.Sha);
            if (finding.Sha is not null)
            {
                _baselinedAt[finding.Path] = now;
            }
            // One milestone per genuine new commit. The per-source daily count
            // in PetBrain applies the diminishing returns from there, so a
            // fifty-commit afternoon does not pay fifty times over.
            for (int i = 0; i < finding.Milestones; i++)
            {
                _session.Receive(GitActivitySource.Event(ActivityEventKind.Milestone, now), now);
            }
            if (finding.Milestones > 0)
            {
                GD.Print($"git: {finding.Milestones} commit"
                       + $"{(finding.Milestones == 1 ? "" : "s")} in {finding.Path}");
            }
        }
    }
}
