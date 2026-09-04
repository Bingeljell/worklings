using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// Watches the spool directory adapters drop event files into, hands every valid
/// event to the session, and deletes every file it inspects — valid or not — so
/// the directory can never accumulate.
///
/// **Polling, not watching.** The Swift app uses a `DispatchSource` on the
/// directory's file descriptor and wakes only when something changes. Godot
/// offers no equivalent, so this looks every couple of seconds. That is worse in
/// principle and invisible in practice: an event arriving up to two seconds
/// after it happened is well inside the tolerance of a creature that reacts by
/// thinking a thought.
///
/// Ported in behaviour from Sources/Worklings/ActivityInboxMonitor.swift; the
/// rules it enforces live in `ActivityInbox`, which is the ported trust
/// boundary. Nothing here decides what is acceptable — it only decides what to
/// open, and what to do with what it finds.
public sealed partial class ActivityInboxWatcher : Node
{
    /// How often to look. Two seconds is a compromise between a pet that feels
    /// responsive and a directory listing every frame.
    [Export] public double PollSeconds { get; set; } = 2;

    private readonly PetSession _session;
    private readonly InboxLocation _location;

    /// Files that could not be deleted, remembered so a stuck file cannot be
    /// re-delivered on every subsequent pass — which would otherwise turn one
    /// undeletable milestone into a milestone every two seconds, forever.
    private readonly HashSet<string> _undeletable = new();

    private double _timer;
    private bool _working;

    public ActivityInboxWatcher(PetSession session)
    {
        _session = session;
        _location = InboxLocation.Resolve();
    }

    public string Path => _location.Path;

    public override void _Ready()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_location.Path);
        }
        catch (System.Exception error)
        {
            // Fails closed and quiet. A pet that cannot watch an inbox is still
            // a pet; refusing to launch over it would be worse than the missing
            // feature.
            GD.PushWarning($"Could not create the activity inbox at {_location.Path}: "
                         + $"{error.Message}. Nothing will be delivered.");
            _working = false;
            SetProcess(false);
            return;
        }

        _working = true;
        GD.Print($"inbox: {_location.Path} "
               + $"({(_location.IsShared ? "the real inbox" : "not the real inbox")}"
               + $" — {_location.Reason})");
        // Drains anything already waiting, including a backlog written while the
        // app was closed — which ActivityInbox then refuses for being stale.
        Drain();
    }

    public override void _Process(double delta)
    {
        if (!_working) return;
        _timer -= delta;
        if (_timer > 0) return;
        _timer = PollSeconds;
        Drain();
    }

    /// One pass: list, read, decode, delete, deliver.
    ///
    /// Blocking, on the main thread, unlike Swift's — which does everything but
    /// the delivery off-actor. The files are tens of bytes and there are almost
    /// never more than a handful, so the honest tradeoff is a simpler watcher.
    /// If a drain is ever slow enough to be felt, this is the thing to move.
    private void Drain()
    {
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(_location.Path, "*.json");
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"Could not read the activity inbox: {error.Message}");
            return;
        }

        var events = new List<ActivityEvent>();
        foreach (string path in files)
        {
            string name = System.IO.Path.GetFileName(path);
            if (name.StartsWith(".") || _undeletable.Contains(name))
            {
                continue;
            }

            byte[]? data = ReadIfSane(path, name);
            if (data is not null)
            {
                var result = ActivityInbox.Decode(data, System.DateTimeOffset.Now);
                if (result.Event is ActivityEvent evt)
                {
                    events.Add(evt);
                }
                else
                {
                    // Named by reason. This is the only diagnostic an adapter
                    // author will ever get.
                    GD.Print($"inbox discarded {name}: {result.Rejection!.Value.RawValue()}");
                }
            }

            // Deleted whether or not it was any good, so nothing accumulates.
            try
            {
                System.IO.File.Delete(path);
            }
            catch (System.Exception error)
            {
                _undeletable.Add(name);
                GD.PushWarning($"Could not remove inbox file {name}: {error.Message}");
            }
        }

        // Ordered by the events' own timestamps, never by filename. Filenames
        // carry no ordering contract, and a workEnded that happens to sort
        // before its workStarted would drop the session and leave the context
        // stuck working.
        foreach (var evt in ActivityInbox.Ordered(events))
        {
            _session.Receive(evt, System.DateTimeOffset.Now);
        }
    }

    /// Reads a file only once it is worth reading.
    ///
    /// The size is checked **before** the bytes are, so an oversized or hostile
    /// file is never loaded into memory. Empty is refused too, and that is doing
    /// more work than it looks: a fifo left in the directory reports a length of
    /// zero, and opening one for reading blocks until something writes to it —
    /// which on the main thread would freeze the pet outright. An empty regular
    /// file cannot be valid JSON either, so one rule covers both.
    ///
    /// Swift asks the file system for `isRegularFile` and gets sockets and
    /// devices refused for free. .NET exposes no equivalent on Unix, so this is
    /// the closest honest approximation.
    private static byte[]? ReadIfSane(string path, string name)
    {
        try
        {
            var info = new System.IO.FileInfo(path);
            if (!info.Exists
                || (info.Attributes & System.IO.FileAttributes.ReparsePoint) != 0
                || info.Length <= 0
                || info.Length > ActivityInbox.MaxPayloadBytes)
            {
                GD.Print($"inbox discarded {name}: not a plain file within the size limit");
                return null;
            }
            return System.IO.File.ReadAllBytes(path);
        }
        catch (System.Exception error)
        {
            GD.Print($"inbox discarded {name}: {error.Message}");
            return null;
        }
    }
}
