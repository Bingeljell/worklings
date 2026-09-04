using Godot;
using Worklings.Core.Host;
using Worklings.Core.Pet;

/// Checks the presence watcher against a driven idle clock, and prints what the
/// real one is saying.
///
/// Not a probe with a stored reference — `PresenceEvaluator`'s decisions are
/// already diffed against Swift in `sources_probe`. What this checks is the
/// wiring either side of it: that the CoreGraphics call returns a real number,
/// and that each signal reaches the session as the right kind of event.
public partial class PresenceCheck : Node
{
    public override void _Ready()
    {
        var session = new PetSession(
            System.DateTimeOffset.Now,
            save: new SaveLocation(
                ProjectSettings.GlobalizePath("user://presence-check/pet-state.json"),
                IsShared: false, Reason: "presence check"));

        double idle = 0;
        var watcher = new PresenceWatcher(session, () => idle) { IdleThreshold = 300 };
        AddChild(watcher);

        var start = System.DateTimeOffset.Now;
        var now = start;
        string T(System.DateTimeOffset? t) =>
            t is System.DateTimeOffset d ? $"+{(d - start).TotalSeconds:F0}s" : "-";

        foreach (double seconds in new double[] { 0, 120, 299, 300, 600, 3600, 5, 0 })
        {
            idle = seconds;
            watcher.Check(now);
            var c = session.Context;
            // `away` and `last` printed as instants, not as "set", because the
            // whole point of keeping them apart is that a repeated "still away"
            // moves ONE of them. If away ever tracks last, the absence clock has
            // been reset and a two-hour absence looks a minute old.
            GD.Print($"idle {seconds,6:F0}s -> present {c.IsUserPresent,-5} "
                   + $"away {T(c.AwaySince),-7} last {T(c.LastEventAt)}");
            now = now.AddSeconds(15);
        }

        var real = new PresenceWatcher(session);
        GD.Print($"real clock available: {real.IsAvailable} on {OS.GetName()}");
        if (real.Sample() is double sample)
        {
            GD.Print($"machine idle right now: {sample:F2}s");
        }
        GetTree().Quit();
    }
}
