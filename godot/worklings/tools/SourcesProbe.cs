using Godot;
using Worklings.Core.Pet;

/// Compares the activity sources' pure decision logic against reference output
/// captured from the Swift original.
///
/// None of this touches git, the clock or the input system — that is the point.
/// Each of these four is the *decision* a source has to make, lifted out of the
/// I/O that feeds it, so the rules that matter are checkable: which HEAD
/// movements are forward progress, when a burst of events collapses to one
/// emote, when a new day has started, and where the idle threshold is crossed
/// in each direction.
public partial class SourcesProbe : Node
{
    private static readonly System.DateTimeOffset Base = PetStateCodec.FromSwiftDate(800_000_000);
    private readonly System.Text.StringBuilder o = new();

    private static System.DateTimeOffset At(double seconds) => Base.AddSeconds(seconds);
    private static string B(bool b) => b ? "true" : "false";

    public override void _Ready()
    {
        o.AppendLine("== source ids ==");
        foreach (var (label, evt) in new (string, ActivityEvent)[]
                 {
                     ("system", SystemActivitySource.Event(ActivityEventKind.WorkStarted, At(0))),
                     ("manual", ManualActivitySource.Event(ActivityEventKind.WorkLogged, At(0))),
                     ("git", GitActivitySource.Event(ActivityEventKind.Milestone, At(0))),
                     ("simulated", SimulatedActivitySource.Event(ActivityEventKind.Milestone, At(0))),
                 })
        {
            o.AppendLine($"{label}: id={evt.SourceId} kind={evt.Kind.RawValue()}");
        }

        o.AppendLine("== git commit delta ==");
        foreach (var (label, oldSha, newSha, ancestor, ahead, cap) in
                 new (string, string?, string, bool, int, int)[]
                 {
                     ("unchanged head", "aaa", "aaa", true, 0, 10),
                     ("unchanged head, phantom count", "aaa", "aaa", true, 5, 10),
                     ("one commit", "aaa", "bbb", true, 1, 10),
                     ("three commits", "aaa", "bbb", true, 3, 10),
                     // An amend or a rebase moved HEAD without adding to it.
                     ("rewritten history", "aaa", "bbb", false, 3, 10),
                     ("over the cap", "aaa", "bbb", true, 40, 10),
                     ("cap of zero", "aaa", "bbb", true, 3, 0),
                     ("negative cap", "aaa", "bbb", true, 3, -5),
                     ("negative count", "aaa", "bbb", true, -2, 10),
                     // A repo that was empty when watching began: its first
                     // commits are credited, and ancestry is not consulted.
                     ("empty repo, first commit", null, "bbb", false, 1, 10),
                     ("empty repo, several", null, "bbb", false, 4, 10),
                     ("empty repo, none yet", null, "bbb", true, 0, 10),
                 })
        {
            int n = GitCommitDelta.MilestonesToEmit(oldSha, newSha, ancestor, ahead, cap);
            o.AppendLine($"{label}: {n}");
        }

        o.AppendLine("== emote throttle ==");
        o.AppendLine($"never emoted: {B(EmoteThrottle.ShouldEmote(null, At(0)))}");
        foreach (double seconds in new double[] { 0, 1, 4.9, 5, 5.1, 60 })
        {
            o.AppendLine($"+{seconds:F1}s: "
                       + $"{B(EmoteThrottle.ShouldEmote(At(0), At(seconds)))}");
        }
        // A non-positive interval disables the throttle rather than blocking
        // everything, which is the failure it would be easy to write instead.
        o.AppendLine($"zero interval: {B(EmoteThrottle.ShouldEmote(At(0), At(0), 0))}");
        o.AppendLine($"negative interval: {B(EmoteThrottle.ShouldEmote(At(0), At(0), -5))}");
        o.AppendLine($"custom 60s at 30s: {B(EmoteThrottle.ShouldEmote(At(0), At(30), 60))}");
        o.AppendLine($"custom 60s at 60s: {B(EmoteThrottle.ShouldEmote(At(0), At(60), 60))}");

        o.AppendLine("== daily wake ==");
        o.AppendLine($"never woken: {B(DailyWakeTracker.ShouldWake(null, At(0)))}");
        foreach (double hours in new double[] { 0, 1, 6, 12, 18, 24, 48 })
        {
            o.AppendLine($"+{hours:F1}h: "
                       + $"{B(DailyWakeTracker.ShouldWake(At(0), At(hours * 3600)))}");
        }

        o.AppendLine("== presence ==");
        foreach (double idle in new double[] { 0, 60, 299, 300, 301, 3600 })
        {
            foreach (bool wasIdle in new[] { false, true })
            {
                var signal = PresenceEvaluator.Signal(idle, wasIdle);
                o.AppendLine($"idle {idle:F0}s wasIdle {B(wasIdle)}: "
                           + $"{(signal is PresenceSignal s ? s.RawValue() : "-")}");
            }
        }
        foreach (double idle in new double[] { 59, 60 })
        {
            var signal = PresenceEvaluator.Signal(idle, false, 60);
            o.AppendLine($"custom threshold 60, idle {idle:F0}s: "
                       + $"{(signal is PresenceSignal s ? s.RawValue() : "-")}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
