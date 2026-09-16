using Godot;

namespace Worklings.Core.Host;

/// What build this is, for the player to read.
///
/// **The prerelease label had nowhere to live.** macOS allows at most three
/// dot-separated integers in `CFBundleShortVersionString`, so the bundle carries
/// `0.1.0` and the build number separately, and "alpha.13" existed only in the
/// Git tag and the DMG filename — nothing the running app could show you. A
/// tester looking at the window had no way to say which build they were on,
/// which is the whole reason a version is on screen at all.
///
/// It lives in `project.godot` under `application/config/version`, which is
/// Godot's own field for it, so it travels in the `.pck` and needs no build
/// step. **It is bumped by hand for a release, next to the build number in
/// `export_presets.cfg`** — see `docs/process/distribution.md`.
public static class AppVersion
{
    /// The full semantic version including the prerelease label, or an honest
    /// placeholder. A build that forgot to set it should say so rather than
    /// quietly claiming to be some other version.
    public static string Current
    {
        get
        {
            var setting = ProjectSettings.GetSetting("application/config/version");
            string version = setting.AsString();
            return string.IsNullOrWhiteSpace(version) ? "unversioned build" : version;
        }
    }

    /// How the version reads on screen.
    public static string Label => $"Worklings {Current}";
}
