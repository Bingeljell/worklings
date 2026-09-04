using System.Collections.Generic;

namespace Worklings.Core.Connect;

public enum ConnectorError
{
    /// The adapter we would wire the tool to is missing or not executable.
    /// Refusing to write means a tool is never pointed at a command that fails
    /// to launch — which for Codex, where a non-zero exit blocks a turn, could
    /// stall the user's session.
    AdapterUnavailable,

    /// A config exists but could not be read. We fail closed rather than treat
    /// it as empty: merging onto a blank base and then backing up would replace
    /// the live config with ours.
    ExistingConfigUnreadable,

    /// The config kept changing underneath us — another program, or the user
    /// editing it — across every attempt, so we stopped rather than overwrite a
    /// live edit with a stale merge.
    ConfigChangedDuringWrite,
}

public sealed class ConnectorException : System.Exception
{
    public ConnectorError Error { get; }
    public string Path { get; }

    public ConnectorException(ConnectorError error, string path)
        : base($"{error}: {path}")
    {
        Error = error;
        Path = path;
    }
}

/// Whether a tool carries our hooks at all, live or stale.
public enum ConnectionState
{
    /// No hooks of ours are present.
    NotConnected,

    /// Ours are present and point at an adapter that exists and is executable.
    Live,

    /// Ours are present but the adapter they name is gone — the app was moved or
    /// deleted. The wiring is still recognisably ours; reconnecting repoints it.
    Stale,

    /// The config exists but could not be inspected: unreadable, or not valid
    /// JSON. We can neither confirm nor deny that our hooks are in it, so this
    /// must never be reported as `NotConnected` — a caller cleaning up has to
    /// treat it as a failure to resolve rather than as nothing to do.
    Unknown,
}

/// Writes and removes a tool's Worklings hook wiring on disk, safely.
///
/// The merge is `HookConfigMerger`'s; this adds the **never brick a config**
/// guarantees around it. The merge runs *before* anything is touched, so an
/// unparseable config throws and leaves the file exactly as it was. An existing
/// config is copied to a timestamped backup before being replaced. And the write
/// is atomic — temp file, then rename — so a config is never left half written.
///
/// Ported from Sources/CompanionCore/ToolConnector.swift.
public sealed class ToolConnector
{
    public string ConfigPath { get; }
    public string AdapterPath { get; }
    public IReadOnlyList<HookMapping> Mappings { get; }
    public HookCommandStyle Style { get; }

    /// How many times a write re-reads and retries when the config changed
    /// between our read and our write, before giving up and throwing.
    private const int MaxWriteAttempts = 4;

    public ToolConnector(
        string configPath,
        string adapterPath,
        IReadOnlyList<HookMapping> mappings,
        HookCommandStyle style)
    {
        ConfigPath = configPath;
        AdapterPath = adapterPath;
        Mappings = mappings;
        Style = style;
    }

    public bool IsConnected() =>
        State() is ConnectionState.Live or ConnectionState.Stale;

    /// Distinguishes *is this hook ours* — ownership, by the adapter's file
    /// name, which survives the app being relocated — from *does it point at the
    /// adapter that is installed now*, and both from *could we even read the
    /// file*. A missing config is `NotConnected`; a present one we cannot read or
    /// parse is `Unknown`, never a false `NotConnected`.
    public ConnectionState State()
    {
        byte[]? data;
        try
        {
            data = ReadExistingConfig();
        }
        catch (ConnectorException)
        {
            return ConnectionState.Unknown;
        }
        if (data is null) return ConnectionState.NotConnected;

        bool blank = true;
        foreach (byte b in data)
        {
            if (b is not (0x20 or 0x0A or 0x0D or 0x09)) { blank = false; break; }
        }
        if (data.Length == 0 || blank) return ConnectionState.NotConnected;

        // Present but unparseable: our hooks might be in there and we cannot
        // tell. Fail loud, never a false "not connected".
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(data)
                is not System.Text.Json.Nodes.JsonObject)
            {
                return ConnectionState.Unknown;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return ConnectionState.Unknown;
        }

        var paths = HookConfigMerger.OurHookExecutablePaths(data, AdapterPath);
        if (paths.Count == 0) return ConnectionState.NotConnected;
        foreach (string path in paths)
        {
            if (IsExecutableFile(path)) return ConnectionState.Live;
        }
        return ConnectionState.Stale;
    }

    /// Merges our hooks in. Returns the backup path if one was made, and throws
    /// — leaving the file untouched and no backup written — if the existing
    /// config is present but not valid JSON.
    public string? Connect()
    {
        // Never wire a tool to a command that cannot run.
        if (!IsExecutableFile(AdapterPath))
        {
            throw new ConnectorException(ConnectorError.AdapterUnavailable, AdapterPath);
        }
        return Commit(existing => HookConfigMerger.Connected(
            existing, AdapterPath, Mappings, Style));
    }

    /// Removes only our hooks. Returns the backup path if one was made. A no-op
    /// if there is no config yet; a present-but-unreadable config throws rather
    /// than silently reporting success.
    public string? Disconnect()
    {
        byte[]? existing = ReadExistingConfig();
        if (existing is null || existing.Length == 0)
        {
            return null;
        }
        return Commit(current => HookConfigMerger.Disconnected(current, AdapterPath));
    }

    /// Reads, transforms, and writes back atomically — closing the read-to-write
    /// race.
    ///
    /// Each attempt backs up the current file, then **re-reads and compares**
    /// against what the transform was computed from, and only writes if they
    /// still match. The confirming re-read is the last thing before the atomic
    /// rename, with nothing — not even the backup — between it and the write, so
    /// a program or a user editing the file during the backup is caught rather
    /// than overwritten, and the attempt retries on the new bytes. After
    /// `MaxWriteAttempts` racing passes it throws instead of writing stale data.
    ///
    /// A vanishing window remains between that re-read and the rename. Closing
    /// it fully would need file locking, which these tools do not coordinate on;
    /// re-reading immediately before the write shrinks it to microseconds.
    private string? Commit(System.Func<byte[], byte[]> transform)
    {
        byte[] existing = ReadExistingConfig() ?? System.Array.Empty<byte>();
        for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            // If this throws — unparseable, or a structure we do not recognise —
            // we return before writing or backing up, so the file is left
            // exactly as it was.
            byte[] updated = transform(existing);

            string? backup = BackUpExisting();
            byte[] current = ReadExistingConfig() ?? System.Array.Empty<byte>();
            if (Same(current, existing))
            {
                Write(updated);
                return backup;
            }

            // It changed between our read and now. Drop the possibly-stale
            // backup and retry on the newer contents rather than overwrite them.
            if (backup is not null)
            {
                try { System.IO.File.Delete(backup); }
                catch (System.Exception) { /* a stray backup is not worth failing over */ }
            }
            existing = current;
        }
        throw new ConnectorException(ConnectorError.ConfigChangedDuringWrite, ConfigPath);
    }

    /// Distinguishes "no file yet" (null) from "present but unreadable" (throws).
    /// This is the fail-closed boundary: callers must never treat an unreadable
    /// config as empty.
    private byte[]? ReadExistingConfig()
    {
        if (!System.IO.File.Exists(ConfigPath))
        {
            return null;
        }
        try
        {
            return System.IO.File.ReadAllBytes(ConfigPath);
        }
        catch (System.Exception)
        {
            throw new ConnectorException(ConnectorError.ExistingConfigUnreadable, ConfigPath);
        }
    }

    /// Copies the current config aside as a timestamped backup, if it exists.
    /// A copy rather than a move, so the original stays in place until the
    /// atomic write replaces it.
    private string? BackUpExisting()
    {
        if (!System.IO.File.Exists(ConfigPath))
        {
            return null;
        }
        string backup = $"{ConfigPath}.worklings-backup-{Timestamp()}";
        if (System.IO.File.Exists(backup))
        {
            System.IO.File.Delete(backup);
        }
        System.IO.File.Copy(ConfigPath, backup);
        return backup;
    }

    private void Write(byte[] data)
    {
        string? directory = System.IO.Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // Temp file beside the target, then rename. Beside it rather than in the
        // system temp directory because a rename across filesystems is a copy,
        // and a copy is not atomic.
        string temporary = $"{ConfigPath}.worklings-tmp-{Timestamp()}";
        System.IO.File.WriteAllBytes(temporary, data);
        System.IO.File.Move(temporary, ConfigPath, overwrite: true);
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    /// Whether a path names a file we could actually run.
    ///
    /// On Unix that is the execute bit — for anyone, since the adapter may be
    /// group- or other-executable — matching Swift's `isExecutableFile`. Windows
    /// has no such bit and decides by extension, so existence is the honest
    /// answer there rather than a guess.
    public static bool IsExecutableFile(string path)
    {
        if (!System.IO.File.Exists(path)) return false;
        if (System.OperatingSystem.IsWindows()) return true;
        try
        {
            var mode = System.IO.File.GetUnixFileMode(path);
            return (mode & (System.IO.UnixFileMode.UserExecute
                          | System.IO.UnixFileMode.GroupExecute
                          | System.IO.UnixFileMode.OtherExecute)) != 0;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static string Timestamp() =>
        System.DateTime.Now.ToString(
            "yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
}
