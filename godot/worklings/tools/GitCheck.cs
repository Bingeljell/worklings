using Godot;
using Worklings.Core.Host;

/// Drives the git watcher against a real repository, whose path comes from
/// `WORKLINGS_GIT_CHECK`.
///
/// Not a probe with a stored reference — `GitCommitDelta`'s judgements are
/// already diffed against Swift in `sources_probe`. What this checks is the
/// half that talks to git: that the CLI wrapper reads what it claims to, and
/// that the no-retro-credit rule actually holds against a repository with
/// history in it.
public partial class GitCheck : Node
{
    public override async void _Ready()
    {
        string path = System.Environment.GetEnvironmentVariable("WORKLINGS_GIT_CHECK") ?? "";
        if (path.Length == 0)
        {
            GD.Print("set WORKLINGS_GIT_CHECK to a repository path");
            GetTree().Quit();
            return;
        }

        GD.Print($"isRepository: {GitRepository.IsRepository(path)}");
        GD.Print($"topLevel: {(GitRepository.TopLevel(path) is null ? "-" : "resolved")}");
        string? head = GitRepository.Head(path);
        GD.Print($"head: {(head is null ? "-" : head[..7])}");
        if (head is not null)
        {
            GD.Print($"isAncestor(head, head): {GitRepository.IsAncestor(head, head, path)}");
            // A wide window and a one-second one. The first proves the count
            // works at all; the second is the recency filter that makes a
            // fast-forward over old history worth nothing.
            GD.Print($"commits in the last year: "
                   + $"{GitRepository.RecentCommitCount(null, head, System.DateTimeOffset.Now.AddYears(-1), path)}");
            GD.Print($"commits in the last second: "
                   + $"{GitRepository.RecentCommitCount(null, head, System.DateTimeOffset.Now.AddSeconds(-1), path)}");
        }

        var session = new PetSession(
            System.DateTimeOffset.Now,
            save: new SaveLocation(
                ProjectSettings.GlobalizePath("user://git-check/pet-state.json"),
                IsShared: false, Reason: "git check"));
        double xpBefore = session.State.TotalXP;

        var watcher = new GitCommitWatcher(session);
        if (watcher.Connect(path) is string refusal)
        {
            GD.Print($"connect refused: {refusal}");
        }
        AddChild(watcher);

        // A short poll while the check watches, so a commit made during the run
        // is seen without waiting the production fifteen seconds.
        watcher.PollSeconds = 2;

        // The first pass baselines a repository that already has history in it,
        // and must pay nothing. Everything after it is live watching, so a
        // commit made while this runs must pay exactly once.
        double seconds = double.TryParse(
            System.Environment.GetEnvironmentVariable("WORKLINGS_GIT_CHECK_SECONDS"),
            out double parsed) ? parsed : 3;

        for (double elapsed = 0; elapsed < seconds; elapsed += 1)
        {
            await ToSignal(GetTree().CreateTimer(1.0), "timeout");
            GD.Print($"  t+{elapsed + 1:F0}s  XP earned: {session.State.TotalXP - xpBefore:F2}");
        }

        watcher.Disconnect(GitRepository.TopLevel(path)!);
        GetTree().Quit();
    }
}
