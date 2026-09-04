using Godot;
using Worklings.Core.Pet;

/// Compares `ActivityContext`'s reducer against reference output captured from
/// the Swift original.
///
/// The reducer is small and almost entirely edge cases: which fields a kind
/// touches, which it deliberately leaves alone, and the two clocks — `awaySince`
/// and `workingSince` — that are kept apart from `lastEventAt` precisely so a
/// repeated "still away" heartbeat cannot erase how long an absence has been
/// running. None of that is visible in a type signature, so it is worth a probe
/// before anything is built on top of it.
public partial class ActivityProbe : Node
{
    /// Everything is printed as an offset in seconds from here. Formatting a
    /// date is where a Swift/C# diff fills with false positives; an interval is
    /// the same number on both sides.
    private static readonly System.DateTimeOffset Base = System.DateTimeOffset.Parse(
        "2026-09-04T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind);

    private readonly System.Text.StringBuilder _o = new();

    private static System.DateTimeOffset At(double seconds) => Base.AddSeconds(seconds);

    private static string T(System.DateTimeOffset? t) =>
        t is System.DateTimeOffset d ? (d - Base).TotalSeconds.ToString("F1") : "-";

    private static string B(bool b) => b ? "true" : "false";

    private void Dump(string label, ActivityContext c)
    {
        _o.AppendLine($"{label} working {B(c.IsWorking)} awaiting {B(c.IsAwaitingInput)} "
            + $"present {B(c.IsUserPresent)} away {T(c.AwaySince)} "
            + $"since {T(c.WorkingSince)} last {T(c.LastEventAt)}");
    }

    private ActivityContext Step(ActivityContext c, ActivityEventKind kind, double at)
    {
        var next = c.Reducing(SimulatedActivitySource.Event(kind, At(at)));
        Dump($"  {kind.RawValue()}@{at:F1}", next);
        return next;
    }

    public override void _Ready()
    {
        _o.AppendLine("-- kinds --");
        foreach (var kind in ActivityEventKindExtensions.AllCases)
        {
            _o.AppendLine($"{kind.RawValue()} \"{kind.DisplayName()}\"");
        }

        _o.AppendLine("-- event --");
        var evt = SimulatedActivitySource.Event(ActivityEventKind.Milestone, At(42));
        _o.AppendLine($"kind {evt.Kind.RawValue()} at {T(evt.Timestamp)} source {evt.SourceId}");
        _o.AppendLine($"equal {B(evt.Equals(SimulatedActivitySource.Event(ActivityEventKind.Milestone, At(42))))} "
            + $"differsByTime {B(evt.Equals(SimulatedActivitySource.Event(ActivityEventKind.Milestone, At(43))))}");

        _o.AppendLine("-- a working day --");
        var c = ActivityContext.Quiet;
        Dump("  quiet", c);
        c = Step(c, ActivityEventKind.WorkStarted, 0);
        // A milestone mid-block must not move the block's start, or the session
        // it belongs to shrinks every time something good happens in it.
        c = Step(c, ActivityEventKind.Milestone, 300);
        c = Step(c, ActivityEventKind.AwaitingInput, 600);
        c = Step(c, ActivityEventKind.TaskCompleted, 900);
        c = Step(c, ActivityEventKind.UserIdle, 1200);
        // The heartbeat. `last` moves, `away` does not.
        c = Step(c, ActivityEventKind.UserIdle, 1500);
        // Back after 1800s away, so an open block's start slides 1800s forward.
        c = Step(c, ActivityEventKind.UserReturned, 3000);
        c = Step(c, ActivityEventKind.TaskFailed, 3300);
        c = Step(c, ActivityEventKind.WorkEnded, 3600);
        c = Step(c, ActivityEventKind.WorkStarted, 3900);
        // Already working: the block keeps its original start.
        c = Step(c, ActivityEventKind.WorkStarted, 4200);
        c = Step(c, ActivityEventKind.DailyWake, 4500);
        c = Step(c, ActivityEventKind.WorkLogged, 4800);

        _o.AppendLine("-- away before the block began --");
        var d = ActivityContext.Quiet;
        d = Step(d, ActivityEventKind.UserIdle, 10);
        // Work starting while away leaves the absence clock running.
        d = Step(d, ActivityEventKind.WorkStarted, 20);
        // awaySince is older than workingSince, so there is nothing to shift.
        d = Step(d, ActivityEventKind.UserReturned, 100);

        _o.AppendLine("-- expiry --");
        void Expiry(string label, ActivityContext ctx, double now, double? interval)
        {
            var got = interval is double i ? ctx.Expiring(At(now), i) : ctx.Expiring(At(now));
            Dump($"  {label}", got);
        }
        Expiry("same instant", c, 4800, null);
        Expiry("on the boundary", c, 4800 + ActivityContext.DefaultExpiryInterval, null);
        Expiry("one second past", c, 4801 + ActivityContext.DefaultExpiryInterval, null);
        Expiry("custom, inside", c, 4860, 60);
        Expiry("custom, outside", c, 4861, 60);
        Expiry("zero interval", c, 4800, 0);
        Expiry("zero interval, later", c, 4801, 0);
        // A negative interval clamps to zero rather than expiring everything.
        Expiry("negative interval", c, 4800, -600);
        Expiry("negative interval, later", c, 4801, -600);
        Expiry("quiet never expires further", ActivityContext.Quiet, 9999, null);

        GD.Print(_o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
