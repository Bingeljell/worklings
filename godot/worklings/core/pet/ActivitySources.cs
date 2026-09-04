namespace Worklings.Core.Pet;

/// Real activity events, tagged distinctly from `SimulatedActivitySource` so a
/// live session and a debug rehearsal are never confused in the event stream.
///
/// Ported from Sources/CompanionCore/ActivitySources.swift.
public static class SystemActivitySource
{
    public const string SourceId = "system";

    public static ActivityEvent Event(ActivityEventKind kind, System.DateTimeOffset timestamp) =>
        new(kind, timestamp, SourceId);
}

/// Events the user explicitly logs by hand — self-reported, and therefore tagged
/// distinctly from externally verifiable sources so fairness rules can treat
/// them differently later.
public static class ManualActivitySource
{
    public const string SourceId = "manual";

    public static ActivityEvent Event(ActivityEventKind kind, System.DateTimeOffset timestamp) =>
        new(kind, timestamp, SourceId);
}

/// Commits in a repository the user explicitly connected, surfaced as
/// `Milestone` events. Tagged distinctly from `manual` and `simulated` so
/// fairness rules can later weigh a local commit differently from a
/// self-reported log.
public static class GitActivitySource
{
    public const string SourceId = "git";

    public static ActivityEvent Event(ActivityEventKind kind, System.DateTimeOffset timestamp) =>
        new(kind, timestamp, SourceId);
}

/// The pure decision behind the local-git source: given a change in a
/// repository's HEAD, how many `Milestone` events does it represent?
///
/// Deliberately free of any git invocation, so it is deterministically
/// checkable. The app supplies the facts by shelling out to git; this decides
/// what the pet should see. It reasons only over commit identifiers and their
/// ancestry — never a message, a diff or a path — so the source's structural
/// privacy is legible right here.
public static class GitCommitDelta
{
    /// `oldSha` is the previously observed HEAD, or null when the repo had **no
    /// commits** at the moment watching began. The no-retro-credit rule for a
    /// repo *with* history is enforced upstream — the watcher silently sets the
    /// baseline to the current HEAD on connect and at launch — so null here only
    /// ever means a repo that started empty, and its first commit counts.
    ///
    /// `oldIsAncestorOfNew` is false for an amend, a reset, or a rebase that
    /// rewrote history rather than adding to it. That is not forward progress
    /// and emits nothing. Ignored when `oldSha` is null: there is no baseline to
    /// descend from.
    ///
    /// `commitsAhead` is how many *recently committed* commits `newSha` is ahead
    /// of `oldSha` — the watcher passes a recency-filtered count, so a pull or a
    /// checkout that fast-forwards over old history earns nothing and only
    /// commits actually made while watching do.
    ///
    /// `maxPerCheck` bounds the result, so one HEAD movement can never emit an
    /// unbounded burst.
    ///
    /// This answers "what does this HEAD movement represent", not "should we
    /// credit it now" — the timing rule belongs to the caller.
    public static int MilestonesToEmit(
        string? oldSha,
        string newSha,
        bool oldIsAncestorOfNew,
        int commitsAhead,
        int maxPerCheck = 10)
    {
        if (oldSha is not null)
        {
            // Known baseline: only forward progress counts.
            if (newSha == oldSha) return 0;
            if (!oldIsAncestorOfNew) return 0;
        }
        return System.Math.Min(
            System.Math.Max(0, commitsAhead), System.Math.Max(0, maxPerCheck));
    }
}

/// Rate-limits the pet's *expressive* reaction, so many events landing close
/// together — a batch of commits, an agent finishing turn after turn, several
/// sources firing at once — produce one emote rather than a robotic stutter.
///
/// Purely a presentation concern: XP and needs still accrue per event upstream;
/// only the reaction is gated. Pure and deterministic so the window is checkable
/// without a real clock; the caller owns remembering `lastEmoteAt`.
public static class EmoteThrottle
{
    public const double DefaultMinimumInterval = 5;

    /// Always true if the pet has not emoted yet, or if the interval is
    /// non-positive, so throttling never swallows the very first reaction.
    public static bool ShouldEmote(
        System.DateTimeOffset? lastEmoteAt,
        System.DateTimeOffset now,
        double minimumInterval = DefaultMinimumInterval)
    {
        if (minimumInterval <= 0 || lastEmoteAt is not System.DateTimeOffset last)
        {
            return true;
        }
        return (now - last).TotalSeconds >= minimumInterval;
    }
}

/// Decides whether the first interaction of a new calendar day has happened,
/// independent of how many times the app has launched that day. The caller owns
/// persisting `lastWakeAt`; this only makes the determination.
public static class DailyWakeTracker
{
    /// Swift compares through `Calendar.current`, which is the *local* calendar,
    /// so both instants are converted to local time before the day is read off —
    /// the same rule `DailyTally` follows, and for the same reason.
    public static bool ShouldWake(
        System.DateTimeOffset? lastWakeAt, System.DateTimeOffset now)
    {
        if (lastWakeAt is not System.DateTimeOffset last)
        {
            return true;
        }
        return last.ToLocalTime().Date != now.ToLocalTime().Date;
    }
}

/// What a presence poll should do: fire a one-time transition event, keep an
/// ongoing absence alive without repeating its reaction, or nothing.
public enum PresenceSignal
{
    WentIdle,
    StillIdle,
    Returned,
}

/// Turns raw system idle seconds into a presence signal. Pure and deterministic
/// so the threshold crossing is checkable without a real clock or real input
/// events; the caller owns polling and remembering `wasIdle`.
public static class PresenceEvaluator
{
    public const double DefaultIdleThreshold = 5 * 60;

    /// Null means "nothing to do" — present, and already known to be present.
    public static PresenceSignal? Signal(
        double idleSeconds, bool wasIdle, double threshold = DefaultIdleThreshold)
    {
        if (idleSeconds >= threshold)
        {
            return wasIdle ? PresenceSignal.StillIdle : PresenceSignal.WentIdle;
        }
        return wasIdle ? PresenceSignal.Returned : null;
    }
}

public static class PresenceSignalExtensions
{
    public static string RawValue(this PresenceSignal signal) => signal switch
    {
        PresenceSignal.WentIdle => "wentIdle",
        PresenceSignal.StillIdle => "stillIdle",
        PresenceSignal.Returned => "returned",
        _ => signal.ToString(),
    };
}
