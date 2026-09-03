using Godot;

namespace Worklings.Core.Host;

/// Which file the Workling is read from and written to, and whether that file is
/// the real one.
///
/// **The rule: only the shipped app writes the real save.** A Workling is one
/// pet across every build that can open it, so an exported `.app` or `.exe`
/// reads and writes exactly the file the Swift app does — same path, same
/// format, one pet. But a run from the editor, from a terminal, or headless is a
/// *test*, and a test that can rewrite a real pet with 9,000 XP on it is one
/// stray autoplay loop away from being a data-loss bug.
///
/// So a test run reads the real save and writes to a copy. Real stats to play
/// against, the whole load/resolve/save chain still exercised, and the file
/// itself untouched. Delete the copy to re-seed it from the real save.
///
/// This is the one thing in `core/` that asks the engine a question, which is
/// why it lives under `host/` rather than `pet/` — "how was I launched" is a
/// property of the shell around the game, not of the Workling.
public readonly record struct SaveLocation(string Path, bool IsShared, string Reason)
{
    /// Points both the real path and the test copy somewhere else entirely.
    /// Writable, because naming a file explicitly is a statement of intent —
    /// this is how a fixture or a second pet gets loaded.
    public const string OverrideVariable = "WORKLINGS_SAVE";

    /// The Swift app's filename, and it has to stay that. Anything else and the
    /// two builds hold separate pets while appearing to share a directory.
    private const string FileName = "pet-state.json";
    private const string DirectoryName = "Worklings";

    public static SaveLocation Resolve()
    {
        string? overridden = System.Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new SaveLocation(overridden, IsShared: false, $"{OverrideVariable} is set");
        }

        string shared = SharedPath();

        // "template" is Godot's word for an exported build: it is false in the
        // editor and false when the editor binary runs a project from a terminal,
        // and true only in an actual shipped app. The headless check is the
        // second half — an exported build driven by a script is still a test.
        bool isTheApp = OS.HasFeature("template") && DisplayServer.GetName() != "headless";
        if (isTheApp)
        {
            return new SaveLocation(shared, IsShared: true, "running as the app");
        }

        string copy = ProjectSettings.GlobalizePath("user://test-save/pet-state.json");
        SeedFromShared(shared, copy);
        return new SaveLocation(copy, IsShared: false, "test run, on a copy of the real save");
    }

    /// Where the Workling lives for every build on this machine. macOS has to
    /// match `WorklingsDirectories.applicationSupport()` in the Swift app
    /// exactly; the other two are each platform's equivalent, and on Windows and
    /// Linux the Godot build is the first thing to write there.
    ///
    /// Spelled out per platform rather than taken from .NET's
    /// `SpecialFolder.ApplicationData`, which resolves to `~/.config` on macOS —
    /// the right answer for a Unix program and the wrong file for this one.
    public static string SharedPath()
    {
        string home = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile);

        string directory = OS.GetName() switch
        {
            "macOS" => System.IO.Path.Combine(
                home, "Library", "Application Support", DirectoryName),
            "Windows" => System.IO.Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ApplicationData),
                DirectoryName),
            _ => System.IO.Path.Combine(
                System.Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                    ?? System.IO.Path.Combine(home, ".local", "share"),
                DirectoryName),
        };
        return System.IO.Path.Combine(directory, FileName);
    }

    /// Copies the real save into the test copy the first time, so a test run
    /// starts from the actual pet rather than from a stand-in. Only ever copies
    /// *from* the real file, never back to it.
    private static void SeedFromShared(string shared, string copy)
    {
        if (System.IO.File.Exists(copy) || !System.IO.File.Exists(shared))
        {
            return;
        }
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
            System.IO.File.Copy(shared, copy);
        }
        catch (System.Exception error)
        {
            // A test run that cannot seed still runs, from the demo pet. Nothing
            // here is worth failing a launch over.
            GD.PushWarning($"Could not copy the save for testing: {error.Message}");
        }
    }
}
