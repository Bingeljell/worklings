namespace Worklings.Core.Pet;

/// What kind of thing happened. Content-free by construction: the whole
/// vocabulary is these ten names, so nothing an adapter observes can smuggle a
/// prompt, a file path or a line of code into the pet.
///
/// Ported from Sources/CompanionCore/ActivityEvent.swift.
public enum ActivityEventKind
{
    DailyWake,
    WorkStarted,
    WorkEnded,
    TaskCompleted,
    TaskFailed,
    AwaitingInput,
    Milestone,
    UserIdle,
    UserReturned,
    WorkLogged,
}

public static class ActivityEventKindExtensions
{
    public static readonly ActivityEventKind[] AllCases =
    {
        ActivityEventKind.DailyWake, ActivityEventKind.WorkStarted,
        ActivityEventKind.WorkEnded, ActivityEventKind.TaskCompleted,
        ActivityEventKind.TaskFailed, ActivityEventKind.AwaitingInput,
        ActivityEventKind.Milestone, ActivityEventKind.UserIdle,
        ActivityEventKind.UserReturned, ActivityEventKind.WorkLogged,
    };

    /// The Swift `rawValue`. It is the wire name — an adapter writes it into the
    /// inbox — so it is spelled out rather than derived from the C# name.
    public static string RawValue(this ActivityEventKind kind) => kind switch
    {
        ActivityEventKind.DailyWake => "dailyWake",
        ActivityEventKind.WorkStarted => "workStarted",
        ActivityEventKind.WorkEnded => "workEnded",
        ActivityEventKind.TaskCompleted => "taskCompleted",
        ActivityEventKind.TaskFailed => "taskFailed",
        ActivityEventKind.AwaitingInput => "awaitingInput",
        ActivityEventKind.Milestone => "milestone",
        ActivityEventKind.UserIdle => "userIdle",
        ActivityEventKind.UserReturned => "userReturned",
        ActivityEventKind.WorkLogged => "workLogged",
        _ => kind.ToString(),
    };

    public static ActivityEventKind? FromRawValue(string raw)
    {
        foreach (var kind in AllCases)
        {
            if (kind.RawValue() == raw) return kind;
        }
        return null;
    }

    public static string DisplayName(this ActivityEventKind kind) => kind switch
    {
        ActivityEventKind.DailyWake => "Daily Wake",
        ActivityEventKind.WorkStarted => "Work Started",
        ActivityEventKind.WorkEnded => "Work Ended",
        ActivityEventKind.TaskCompleted => "Task Completed",
        ActivityEventKind.TaskFailed => "Task Failed",
        ActivityEventKind.AwaitingInput => "Awaiting Input",
        ActivityEventKind.Milestone => "Milestone",
        ActivityEventKind.UserIdle => "User Idle",
        ActivityEventKind.UserReturned => "User Returned",
        ActivityEventKind.WorkLogged => "Log Work",
        _ => kind.ToString(),
    };
}

/// A normalized, content-free activity signal. Carries what happened and when,
/// never prompts, code, file paths, or any other user content.
public sealed class ActivityEvent : System.IEquatable<ActivityEvent>
{
    public ActivityEventKind Kind { get; }
    public System.DateTimeOffset Timestamp { get; }
    public string SourceId { get; }

    public ActivityEvent(ActivityEventKind kind, System.DateTimeOffset timestamp, string sourceId)
    {
        Kind = kind;
        Timestamp = timestamp;
        SourceId = sourceId;
    }

    public bool Equals(ActivityEvent? other) =>
        other is not null && Kind == other.Kind
        && Timestamp == other.Timestamp && SourceId == other.SourceId;

    public override bool Equals(object? obj) => Equals(obj as ActivityEvent);

    public override int GetHashCode() => System.HashCode.Combine(Kind, Timestamp, SourceId);
}

/// Short-lived state derived from recent activity events. Never persisted;
/// long-lived relationship state stays in `PetState`.
///
/// A class, not a struct. `new ActivityContext()` on a struct would run the
/// implicit parameterless constructor and produce a context that claims the user
/// is absent, working since the year 1, which is a trap the port has already
/// stepped in once.
public sealed class ActivityContext : System.IEquatable<ActivityContext>
{
    public const double DefaultExpiryInterval = 30 * 60;

    public bool IsWorking { get; }
    public bool IsAwaitingInput { get; }
    public bool IsUserPresent { get; }

    /// When the current, unbroken absence began, or `null` while present.
    ///
    /// Distinct from `LastEventAt` on purpose: a presence source repeats
    /// "still away" to keep the context from expiring, and each of those
    /// refreshes `LastEventAt`. If the absence were read off that, a two-hour
    /// absence would look one minute old for as long as it lasted.
    public System.DateTimeOffset? AwaySince { get; }

    /// When the current, unbroken work block began, or `null` while not working.
    /// Lets `WorkEnded` compute a session's real duration even though
    /// `LastEventAt` may have been refreshed by an unrelated event during the
    /// block — a milestone landing mid-session, say.
    public System.DateTimeOffset? WorkingSince { get; }

    public System.DateTimeOffset? LastEventAt { get; }

    /// Nothing has happened, and that is not the same as nothing being known:
    /// the user is assumed present until something says otherwise.
    public static ActivityContext Quiet => new(
        isWorking: false,
        isAwaitingInput: false,
        isUserPresent: true,
        awaySince: null,
        workingSince: null,
        lastEventAt: null);

    public ActivityContext(
        bool isWorking,
        bool isAwaitingInput,
        bool isUserPresent,
        System.DateTimeOffset? lastEventAt,
        System.DateTimeOffset? awaySince = null,
        System.DateTimeOffset? workingSince = null)
    {
        IsWorking = isWorking;
        IsAwaitingInput = isAwaitingInput;
        IsUserPresent = isUserPresent;
        AwaySince = awaySince;
        WorkingSince = workingSince;
        LastEventAt = lastEventAt;
    }

    public ActivityContext Reducing(ActivityEvent evt)
    {
        switch (evt.Kind)
        {
            case ActivityEventKind.DailyWake:
            case ActivityEventKind.UserReturned:
            {
                // Returning from an absence shifts an open work block's start
                // forward by the time spent away, so the absence never counts as
                // focus time: a block worked 10 minutes, idled 30, worked 5
                // reads as 15 minutes, not 45.
                var adjusted = WorkingSince;
                if (WorkingSince is System.DateTimeOffset since
                    && AwaySince is System.DateTimeOffset away
                    && away > since)
                {
                    double gap = (evt.Timestamp - away).TotalSeconds;
                    adjusted = since.AddSeconds(System.Math.Max(0, gap));
                }
                return new ActivityContext(
                    isWorking: IsWorking,
                    isAwaitingInput: IsAwaitingInput,
                    isUserPresent: true,
                    lastEventAt: evt.Timestamp,
                    awaySince: null,
                    workingSince: adjusted);
            }
            case ActivityEventKind.WorkStarted:
                return Updating(isWorking: true, isAwaitingInput: false, at: evt.Timestamp);
            case ActivityEventKind.WorkEnded:
                return Updating(isWorking: false, isAwaitingInput: false, at: evt.Timestamp);
            case ActivityEventKind.TaskCompleted:
            case ActivityEventKind.TaskFailed:
                return Updating(isAwaitingInput: false, at: evt.Timestamp);
            case ActivityEventKind.AwaitingInput:
                return Updating(isAwaitingInput: true, at: evt.Timestamp);
            case ActivityEventKind.Milestone:
            case ActivityEventKind.WorkLogged:
                return Updating(at: evt.Timestamp);
            case ActivityEventKind.UserIdle:
                return new ActivityContext(
                    isWorking: IsWorking,
                    isAwaitingInput: IsAwaitingInput,
                    isUserPresent: false,
                    lastEventAt: evt.Timestamp,
                    // Only the first idle starts the clock. A repeat is a
                    // heartbeat, not a new absence.
                    awaySince: IsUserPresent ? evt.Timestamp : AwaySince,
                    workingSince: WorkingSince);
            default:
                return this;
        }
    }

    /// Returns `Quiet` when no event has arrived within the interval, so a stale
    /// work block cannot keep influencing the simulation forever. Under normal
    /// operation a live presence source keeps touching `LastEventAt` throughout a
    /// genuine absence, so this is a fallback for abnormal termination — a crash,
    /// a missed `WorkEnded` — not the everyday path.
    public ActivityContext Expiring(
        System.DateTimeOffset now, double interval = DefaultExpiryInterval)
    {
        if (LastEventAt is not System.DateTimeOffset last
            || (now - last).TotalSeconds > System.Math.Max(interval, 0))
        {
            return Quiet;
        }
        return this;
    }

    /// `null` means "leave this one alone", which is Swift's optional parameter
    /// doing the same job. `isWorking` carries the extra weight: turning work on
    /// starts the block only if it was not already running, and turning it off
    /// clears the block entirely.
    private ActivityContext Updating(
        bool? isWorking = null,
        bool? isAwaitingInput = null,
        System.DateTimeOffset at = default)
    {
        System.DateTimeOffset? nextWorkingSince;
        if (isWorking is bool wanted)
        {
            nextWorkingSince = wanted ? (IsWorking ? WorkingSince : at) : null;
        }
        else
        {
            nextWorkingSince = WorkingSince;
        }

        return new ActivityContext(
            isWorking: isWorking ?? IsWorking,
            isAwaitingInput: isAwaitingInput ?? IsAwaitingInput,
            isUserPresent: IsUserPresent,
            lastEventAt: at,
            awaySince: AwaySince,
            workingSince: nextWorkingSince);
    }

    public bool Equals(ActivityContext? other) =>
        other is not null
        && IsWorking == other.IsWorking
        && IsAwaitingInput == other.IsAwaitingInput
        && IsUserPresent == other.IsUserPresent
        && AwaySince == other.AwaySince
        && WorkingSince == other.WorkingSince
        && LastEventAt == other.LastEventAt;

    public override bool Equals(object? obj) => Equals(obj as ActivityContext);

    public override int GetHashCode() => System.HashCode.Combine(
        IsWorking, IsAwaitingInput, IsUserPresent, AwaySince, WorkingSince, LastEventAt);
}

/// The pet's response to an observed activity event: possibly changed state and,
/// for events worth celebrating or consoling, a visible reaction.
public sealed class PetActivityResponse
{
    public PetState State { get; }
    public PetReaction? Reaction { get; }

    public PetActivityResponse(PetState state, PetReaction? reaction)
    {
        State = state;
        Reaction = reaction;
    }
}

/// A deterministic event source for tuning reactions and driving checks before
/// any real adapter exists.
public static class SimulatedActivitySource
{
    public const string SourceId = "simulated";

    public static ActivityEvent Event(ActivityEventKind kind, System.DateTimeOffset timestamp) =>
        new(kind, timestamp, SourceId);
}
