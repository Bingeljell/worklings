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

        // Its own registry, never the player's. A check that connected a
        // scratch repository into the real list left it there, and the only clue
        // in the menu was a folder name nobody recognised.
        var watcher = new GitCommitWatcher(
            session, new ConnectedRepoRegistry("user://git-check/connected-repos.json"));
        if (watcher.Connect(path) is string refusal)
        {
            GD.Print($"connect refused: {refusal}");
        }
        // Connecting the same repo twice, connecting a second one, and pointing
        // at something that is not a repository: all three are things the menu
        // now lets you try, so all three are checked here.
        GD.Print($"same again: {watcher.Connect(path) ?? "CONNECTED TWICE"}");
        if (System.Environment.GetEnvironmentVariable("WORKLINGS_GIT_CHECK_SECOND")
            is string second && second.Length > 0)
        {
            GD.Print($"a second repo: {watcher.Connect(second) ?? "connected"}");
        }
        GD.Print($"not a repository: {watcher.Connect("/tmp") ?? "CONNECTED ANYWAY"}");
        foreach (var repo in watcher.Connected) GD.Print($"  watching {repo.Path}");

        // How each of those reads in the menu. The leaf alone was not enough: a
        // stray repository connected by a test run showed as "gitrepo2", which
        // named nothing and looked exactly like something the player had chosen.
        GD.Print("menu labels:");
        foreach (string sample in new[]
                 {
                     "/Users/nikhilshahane/projects/worklings",
                     "/Users/nikhilshahane/projects/deep/nested/thing",
                     "/private/tmp/claude-501/-Users-x/abc123/scratchpad/gitrepo2",
                     "/opt/src",
                     "/",
                 })
        {
            GD.Print($"  {sample}\n    -> {PetMenu.ShortPath(sample)}");
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
