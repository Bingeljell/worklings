using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the activity half of the brain against reference output captured
/// from the Swift original: what an observed event does to the pet, and what the
/// activity context does to plain decay.
///
/// The fixtures that matter are the ones two plausible implementations would get
/// differently. A **working** context burns hunger and energy faster. An
/// **away** context drains trust at one of two rates either side of the grace
/// period, and exactly on the boundary. A **focus session** is measured between
/// the events' own timestamps, is refused below the minimum, and stops counting
/// at the moment the user walked away rather than at the moment the block ended.
/// **Log Work** has a cooldown and a daily cap, and its XP is charged against the
/// event's own day — so a pre-midnight event drained after midnight books into
/// the day the work actually happened.
public partial class ObserveProbe : Node
{
    private static readonly System.DateTimeOffset Base = PetStateCodec.FromSwiftDate(800_000_000);
    private readonly System.Text.StringBuilder o = new();

    private static string F(double v) =>
        v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

    private static string T(System.DateTimeOffset? t) =>
        t is System.DateTimeOffset d ? F((d - Base).TotalSeconds) : "-";

    private static System.DateTimeOffset At(double seconds) => Base.AddSeconds(seconds);

    private void Show(string label, PetState s) =>
        o.AppendLine($"{label}: needs={F(s.Needs.Hunger)} {F(s.Needs.Energy)} "
                   + $"{F(s.Needs.Happiness)} {F(s.Needs.Trust)} xp={F(s.TotalXP)} lv={s.Level} "
                   + $"log={s.WorkLog.Value} lastLog={T(s.LastWorkLogAt)}");

    private static PetState Pet(
        double hunger = 20, double energy = 80, double happiness = 70, double trust = 50,
        double xp = 0, System.DateTimeOffset? at = null) =>
        new PetState(
            name: "Fixture",
            family: PetFamily.Wildkin,
            needs: new PetNeeds(hunger, energy, happiness, trust),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: at ?? Base,
            totalXP: xp);

    private static ActivityContext Context(
        bool working = false, bool awaiting = false, bool present = true,
        double? away = null, double? since = null, double? last = 0) =>
        new(isWorking: working, isAwaitingInput: awaiting, isUserPresent: present,
            lastEventAt: last is double l ? At(l) : null,
            awaySince: away is double a ? At(a) : null,
            workingSince: since is double w ? At(w) : null);

    public override void _Ready()
    {
        var brain = new PetBrain();

        o.AppendLine("== advance under a context ==");
        foreach (var (label, context) in new (string, ActivityContext)[]
                 {
                     ("quiet", ActivityContext.Quiet),
                     ("working", Context(working: true, since: 0)),
                     ("away 30m", Context(present: false, away: 2 * 3600 - 1800)),
                     ("away exactly 1h", Context(present: false, away: 2 * 3600 - 3600)),
                     ("away 90m", Context(present: false, away: 2 * 3600 - 5400)),
                     ("away and working", Context(working: true, present: false,
                                                  away: 0, since: 0)),
                 })
        {
            Show($"+2h {label}", brain.Advance(Pet(), At(2 * 3600), context));
        }

        o.AppendLine("== observe, every kind ==");
        foreach (var kind in ActivityEventKindExtensions.AllCases)
        {
            var r = brain.Observe(
                SimulatedActivitySource.Event(kind, At(3600)), Pet(), At(3600));
            Show($"{kind.RawValue()} [{Reaction(r)}]", r.State);
        }

        o.AppendLine("== observe on a tired pet ==");
        foreach (var kind in ActivityEventKindExtensions.AllCases)
        {
            var r = brain.Observe(
                SimulatedActivitySource.Event(kind, At(3600)),
                Pet(85, 12, 30, 25), At(3600));
            Show($"{kind.RawValue()} [{Reaction(r)}]", r.State);
        }

        o.AppendLine("== a focus session ==");
        // Minutes worked, measured from workingSince to the event's timestamp.
        foreach (double minutes in new double[] { 0, 9, 10, 11, 45, 600 })
        {
            var context = Context(working: true, since: 0, last: 0);
            var r = brain.Observe(
                SimulatedActivitySource.Event(ActivityEventKind.WorkEnded, At(minutes * 60)),
                Pet(), At(minutes * 60), context);
            Show($"{F(minutes)}m [{Reaction(r)}]", r.State);
        }
        // No open block: nothing to pay for.
        {
            var r = brain.Observe(
                SimulatedActivitySource.Event(ActivityEventKind.WorkEnded, At(3600)),
                Pet(), At(3600), Context(working: true, since: null));
            Show($"no workingSince [{Reaction(r)}]", r.State);
        }
        // Ended while away: the session stops at the moment they left, so an
        // hour of absence is not paid as an hour of focus.
        {
            var context = Context(working: true, present: false, away: 1800, since: 0);
            var r = brain.Observe(
                SimulatedActivitySource.Event(ActivityEventKind.WorkEnded, At(3600)),
                Pet(), At(3600), context);
            Show($"ended while away [{Reaction(r)}]", r.State);
        }
        // Away began before the block did, so there is nothing to truncate to
        // and the earlier of the two is the absence — which is the point of the
        // min: it can only ever shorten the session.
        {
            var context = Context(working: true, present: false, away: 0, since: 1800);
            var r = brain.Observe(
                SimulatedActivitySource.Event(ActivityEventKind.WorkEnded, At(3600)),
                Pet(), At(3600), context);
            Show($"away before the block [{Reaction(r)}]", r.State);
        }

        o.AppendLine("== logging work ==");
        var logged = Pet();
        for (int i = 1; i <= 8; i++)
        {
            double t = i * 3600;
            var r = brain.Observe(
                SimulatedActivitySource.Event(ActivityEventKind.WorkLogged, At(t)),
                logged, At(t));
            logged = r.State;
            Show($"log {i} [{Reaction(r)}]", logged);
        }

        o.AppendLine("== log availability ==");
        Avail(brain, "never logged", Pet(), 0);
        var justLogged = brain.Observe(
            SimulatedActivitySource.Event(ActivityEventKind.WorkLogged, At(0)),
            Pet(), At(0)).State;
        foreach (double minutes in new double[] { 0, 1, 29, 29.5, 30, 45 })
        {
            Avail(brain, $"{F(minutes)}m after a log", justLogged, minutes * 60);
        }
        Avail(brain, "at the daily cap", logged, 8 * 3600 + 3600);

        o.AppendLine("== scaled rates ==");
        foreach (double factor in new double[] { 1, 60, 0.5, 0 })
        {
            var r = new PetSimulationRates().Scaled(factor);
            o.AppendLine($"x{F(factor)}: hunger={F(r.HungerPerHour)} energy={F(r.EnergyPerHour)} "
                       + $"happiness={F(r.HappinessPerHour)} offline={F(r.MaximumOfflineHours)} "
                       + $"away={F(r.AwayTrustPerHour)} grace={F(r.AwayGracePeriodHours)} "
                       + $"longAway={F(r.LongAwayTrustPerHour)} "
                       + $"cooldown={F(r.WorkLogCooldownMinutes)} cap={r.WorkLogDailyCap} "
                       + $"gain={F(r.WorkLogHappinessGain)}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }

    private static string Reaction(PetActivityResponse r) =>
        r.Reaction is PetReaction reaction ? reaction.RawValue() : "-";

    private void Avail(PetBrain brain, string label, PetState state, double at)
    {
        var a = brain.WorkLogAvailability(state, At(at));
        o.AppendLine($"{label}: enabled={(a.IsEnabled ? "true" : "false")} "
                   + $"why={a.Explanation ?? "-"}");
    }
}
