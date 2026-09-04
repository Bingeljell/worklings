using Godot;

namespace Worklings.Core.Host;

/// Which directory adapters drop event files into, and whether it is the real
/// one.
///
/// **The same rule the save follows, for a sharper reason.** The inbox is
/// drain-and-delete: whoever watches it consumes the files. A test run pointed
/// at the real inbox would not merely read the app's events, it would eat them —
/// an adapter's milestone would vanish into an editor session and never reach
/// the pet the user is actually looking at.
///
/// So the shipped app watches the real directory and everything else watches a
/// copy's worth of nothing: an empty test inbox under `user://`, which starts
/// empty rather than being seeded, because seeding it would mean stealing the
/// files to copy them.
public readonly record struct InboxLocation(string Path, bool IsShared, string Reason)
{
    /// Points the watcher somewhere else entirely. Named to match the Swift
    /// app's debug override, so one adapter under test can feed either build.
    public const string OverrideVariable = "WORKLINGS_INBOX_DIR";

    /// The Swift app's directory name, and it has to stay that: an adapter
    /// writes to one path, and both builds have to be reading it.
    private const string DirectoryName = "inbox";

    public static InboxLocation Resolve()
    {
        string? overridden = System.Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new InboxLocation(overridden, IsShared: false, $"{OverrideVariable} is set");
        }

        // The same "am I the shipped app" question SaveLocation asks, and it has
        // to stay the same answer — a build writing the real save while watching
        // a test inbox, or the reverse, is a pet whose history has a hole in it.
        bool isTheApp = OS.HasFeature("template") && DisplayServer.GetName() != "headless";
        if (isTheApp)
        {
            return new InboxLocation(SharedPath(), IsShared: true, "running as the app");
        }

        return new InboxLocation(
            ProjectSettings.GlobalizePath("user://test-inbox"),
            IsShared: false,
            "test run, on an inbox of its own");
    }

    /// Beside the save, because an adapter finds one by finding the other.
    public static string SharedPath() =>
        System.IO.Path.Combine(SaveLocation.SharedDirectory(), DirectoryName);
}
