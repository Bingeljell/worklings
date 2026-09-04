using Godot;

namespace Worklings.Core.Host;

/// When the pet last greeted a new day.
///
/// One line in one file, kept **outside the save** on purpose. It is a fact
/// about this machine's app rather than about the Workling, and the save is
/// shared byte-for-byte with the Swift build — adding a field to it would mean
/// changing a format two apps agree on for something neither of them needs to
/// know about the other.
///
/// The Swift app keeps this in `UserDefaults`, which Godot has no equivalent of.
/// A file under `user://` is the closest thing: per-user, per-app, and not the
/// pet. The two apps therefore wake independently, which is the right answer —
/// running the Godot build should not eat the Swift build's greeting.
///
/// Fails quiet in both directions. A stamp that cannot be read means the pet
/// says hello again; a stamp that cannot be written means it says hello again
/// tomorrow. Neither is worth a warning dialog over a "good morning".
public sealed class WakeStamp
{
    private const string Path = "user://last-wake.txt";

    public System.DateTimeOffset? Read()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return null;
        }
        string text = file.GetAsText().Trim();
        return System.DateTimeOffset.TryParse(
            text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    public void Write(System.DateTimeOffset now)
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Could not write {Path}; the pet will greet you again tomorrow.");
            return;
        }
        // Round-trip format, so the offset survives and a stamp written in one
        // timezone is not read back as a different instant in another.
        file.StoreString(now.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
    }
}
