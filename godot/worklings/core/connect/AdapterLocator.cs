using Godot;

namespace Worklings.Core.Connect;

/// Finds the absolute path of an adapter script, so a hook config can point at
/// one.
///
/// Absolute either way, because that is what a hook `command` needs: the tool
/// runs it from the user's project directory, not from ours.
///
/// **Two places to look, in this order.** In a packaged app the scripts sit in
/// `Contents/Resources/adapters/`, put there by the export step — they cannot
/// live in `res://`, because that is inside the `.pck` and nothing inside a
/// `.pck` is a file the operating system can execute. Running from source there
/// is no bundle, so it falls back to the repo checkout, located from `res://`.
///
/// Ported from Sources/Worklings/AdapterLocator.swift, which resolves the bundle
/// through `Bundle.main` and the repo through `#filePath`. Godot has neither, so
/// both halves are derived from paths it does give us.
public static class AdapterLocator
{
    public const string ClaudeCodeAdapter = "worklings-claude-code-activity-hook";
    public const string CodexAdapter = "worklings-codex-activity-hook";

    public static string Path(string name)
    {
        if (BundledResources() is string resources)
        {
            string bundled = System.IO.Path.Combine(resources, "adapters", name);
            if (System.IO.File.Exists(bundled))
            {
                return bundled;
            }
        }
        return System.IO.Path.Combine(RepoScripts(), "adapters", name);
    }

    /// `Contents/Resources` of the running app bundle, or null when this is not
    /// one. Derived from the executable rather than from `res://`, which in an
    /// exported build points inside the archive.
    private static string? BundledResources()
    {
        if (!OS.HasFeature("template"))
        {
            return null;
        }
        string executable = OS.GetExecutablePath();
        // <app>/Contents/MacOS/<binary> -> <app>/Contents/Resources
        string? macOS = System.IO.Path.GetDirectoryName(executable);
        string? contents = macOS is null ? null : System.IO.Path.GetDirectoryName(macOS);
        return contents is null ? null : System.IO.Path.Combine(contents, "Resources");
    }

    /// The repo's `scripts/` directory, from a source run. `res://` globalises
    /// to `<repo>/godot/worklings/`, so the repo root is two levels above it.
    private static string RepoScripts()
    {
        string project = ProjectSettings.GlobalizePath("res://");
        var directory = new System.IO.DirectoryInfo(project);
        string? root = directory.Parent?.Parent?.FullName;
        return System.IO.Path.Combine(root ?? project, "scripts");
    }
}

/// The two tools we can wire ourselves into.
public enum ConnectableTool
{
    ClaudeCode,
    Codex,
}

public static class ConnectableToolExtensions
{
    public static string DisplayName(this ConnectableTool tool) => tool switch
    {
        ConnectableTool.ClaudeCode => "Claude Code",
        _ => "Codex",
    };

    /// The file each tool keeps its hooks in.
    ///
    /// Codex's is a dedicated `hooks.json` rather than its `config.toml`, which
    /// is never touched — a TOML file with comments and formatting a person
    /// cares about is not something to rewrite programmatically.
    public static string ConfigPath(this ConnectableTool tool)
    {
        string home = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile);
        return tool switch
        {
            ConnectableTool.ClaudeCode =>
                System.IO.Path.Combine(home, ".claude", "settings.json"),
            _ => System.IO.Path.Combine(home, ".codex", "hooks.json"),
        };
    }

    public static ToolConnector Connector(this ConnectableTool tool) => tool switch
    {
        ConnectableTool.ClaudeCode => new ToolConnector(
            tool.ConfigPath(),
            AdapterLocator.Path(AdapterLocator.ClaudeCodeAdapter),
            HookConfigMerger.ClaudeCodeMappings,
            // Claude Code takes an argv array, so the path never goes near a
            // shell that could re-parse it.
            HookCommandStyle.ExecForm),
        _ => new ToolConnector(
            tool.ConfigPath(),
            AdapterLocator.Path(AdapterLocator.CodexAdapter),
            HookConfigMerger.CodexMappings,
            // Codex takes only a shell string.
            HookCommandStyle.ShellForm),
    };
}
