namespace Worklings.Core.Pet;

/// Raised when a save carries a schema this build does not know how to read.
public sealed class UnsupportedSchemaException : System.Exception
{
    public int Found { get; }
    public int Supported { get; }

    public UnsupportedSchemaException(int found, int supported)
        : base($"unsupportedSchema(found: {found}, supported: {supported})")
    {
        Found = found;
        Supported = supported;
    }
}

/// The save file: one Workling, one JSON document on disk.
///
/// Ported from Sources/CompanionCore/PetStateFileStore.swift.
///
/// Deliberately knows nothing about Godot — it takes an absolute filesystem path,
/// so the scene layer resolves `user://` through `ProjectSettings.GlobalizePath`
/// and the probes point it at a scratch file. That keeps the save format testable
/// without a running engine, which is the whole reason it can be diffed against
/// Swift at all.
public sealed class PetStateFileStore
{
    public string FilePath { get; }

    public PetStateFileStore(string filePath)
    {
        FilePath = filePath;
    }

    /// The loaded Workling, or null when there is no save yet — a first run is
    /// not an error, it is the normal way a pet gets created.
    public PetState? Load()
    {
        if (!System.IO.File.Exists(FilePath))
        {
            return null;
        }

        var state = PetStateCodec.Decode(System.IO.File.ReadAllText(FilePath));

        // A save from a newer app may carry fields this build can't honour, so it
        // is rejected rather than silently downgraded — downgrading would write
        // the missing fields back as defaults and destroy them. Older saves are
        // migrated forward: the decoder already folded any legacy flat fields
        // into the unified tallies, leaving only the version to restamp.
        if (state.SchemaVersion == PetState.CurrentSchemaVersion)
        {
            return state;
        }
        if (state.SchemaVersion > PetState.CurrentSchemaVersion)
        {
            throw new UnsupportedSchemaException(
                state.SchemaVersion, PetState.CurrentSchemaVersion);
        }
        return state.UpgradedToSchema(PetState.CurrentSchemaVersion);
    }

    /// Writes through a sibling temp file and moves it into place, so a crash
    /// mid-write leaves the previous save intact rather than a truncated one.
    /// Swift gets this from `Data.write(options: .atomic)`; .NET has no
    /// equivalent flag, so the move is spelled out.
    public void Save(PetState state)
    {
        string? directory = System.IO.Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        string temporaryPath = FilePath + ".tmp";
        System.IO.File.WriteAllText(temporaryPath, PetStateCodec.Encode(state));
        System.IO.File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
