using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Worklings.Core.Host;

/// One connected repository: where it lives, and the HEAD it was last seen at,
/// so a later check can tell what moved.
public sealed class ConnectedRepo
{
    public string Path { get; set; } = "";
    public string? LastSeenSha { get; set; }
}

/// The repositories the user has explicitly connected.
///
/// **Connecting is the opt-in.** There is no separate toggle for the git source,
/// because pointing the app at a repository is already a deliberate act — and a
/// setting that has to be found and turned on after connecting is a feature
/// people conclude is broken.
///
/// Stored as JSON under `user://`, where the Swift app uses `UserDefaults`. The
/// list is therefore per-build rather than shared, which is the right answer:
/// two apps watching the same repository would each pay for the same commit.
///
/// Paths are stored canonicalised by the caller — `GitRepository.TopLevel` — so
/// a subdirectory and a symlink cannot register the same repo twice.
public sealed class ConnectedRepoRegistry
{
    private const string DefaultPath = "user://connected-repos.json";

    private readonly string _path;

    /// The default file is the one the running pet uses. A check or a tool must
    /// pass its own path — the two share a `user://` directory, and an automated
    /// run that writes into the real list leaves the player watching a
    /// throwaway repository from a scratch directory with no idea where it came
    /// from. Which is exactly what happened.
    public ConnectedRepoRegistry(string? path = null) => _path = path ?? DefaultPath;

    public IReadOnlyList<ConnectedRepo> All()
    {
        using var file = FileAccess.Open(_path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return System.Array.Empty<ConnectedRepo>();
        }
        try
        {
            return JsonSerializer.Deserialize<List<ConnectedRepo>>(file.GetAsText())
                   ?? new List<ConnectedRepo>();
        }
        catch (JsonException)
        {
            // A corrupt list is an empty list. Nothing here is worth failing a
            // launch over, and the user can connect again.
            GD.PushWarning($"Could not read {_path}; no repositories are connected.");
            return System.Array.Empty<ConnectedRepo>();
        }
    }

    public bool Contains(string path)
    {
        foreach (var repo in All())
        {
            if (repo.Path == path) return true;
        }
        return false;
    }

    /// Adds a repo if it is not already there, with no baseline — the watcher
    /// sets that to the current HEAD on its first pass, which is what makes
    /// "no credit for history that already existed" true.
    public void Add(string path)
    {
        if (Contains(path))
        {
            return;
        }
        var repos = new List<ConnectedRepo>(All()) { new() { Path = path } };
        Save(repos);
    }

    public void Remove(string path)
    {
        var kept = new List<ConnectedRepo>();
        foreach (var repo in All())
        {
            if (repo.Path != path) kept.Add(repo);
        }
        Save(kept);
    }

    public string? LastSeenSha(string path)
    {
        foreach (var repo in All())
        {
            if (repo.Path == path) return repo.LastSeenSha;
        }
        return null;
    }

    /// Advances a repo's baseline to a newly observed HEAD. A null SHA is
    /// ignored, so a transient git failure never wipes the baseline — which
    /// would make the next real commit look like a fresh connect and earn
    /// nothing.
    public void UpdateLastSeenSha(string path, string? sha)
    {
        if (sha is null)
        {
            return;
        }
        var repos = new List<ConnectedRepo>(All());
        foreach (var repo in repos)
        {
            if (repo.Path == path) repo.LastSeenSha = sha;
        }
        Save(repos);
    }

    private void Save(List<ConnectedRepo> repos)
    {
        using var file = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Could not write {_path}; connected repositories will not persist.");
            return;
        }
        file.StoreString(JsonSerializer.Serialize(
            repos, new JsonSerializerOptions { WriteIndented = true }));
    }
}
