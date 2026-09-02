namespace Worklings.Core.Pet;

/// A value that is only meaningful for the calendar day named by Date.
///
/// This is the one place the "valid only today, ignored when stale" pattern
/// lives — Log Work's daily count and the per-source daily XP ledger both use
/// it instead of each hand-rolling a paired value/date and its own same-day
/// check. A stale tally is never proactively reset in storage; callers read
/// through Current, which returns the default once the stored day has passed,
/// so the save needs no day-rollover side effect.
///
/// Ported from Sources/CompanionCore/DailyTally.swift. Swift's Date is an
/// instant, so DateTimeOffset is the analogue rather than DateTime — which
/// would let a stored instant lose its offset and compare against the wrong
/// calendar day.
public sealed class DailyTally<TValue> : System.IEquatable<DailyTally<TValue>>
{
    public System.DateTimeOffset? Date { get; }
    public TValue Value { get; }

    public DailyTally(TValue value, System.DateTimeOffset? date = null)
    {
        Date = date;
        Value = value;
    }

    /// The stored value if Date names the same calendar day as `day`, otherwise
    /// `fallback`. `day` is the reference day — usually "now", but the XP ledger
    /// passes the day an event actually happened so a backlogged event books
    /// into its own day rather than the day it was delivered.
    ///
    /// Swift compares through `Calendar.current`, which is the *local* calendar,
    /// so both instants are converted to local time before the day is read off.
    public TValue Current(System.DateTimeOffset day, TValue fallback)
    {
        if (Date is not System.DateTimeOffset stored
            || stored.ToLocalTime().Date != day.ToLocalTime().Date)
        {
            return fallback;
        }
        return Value;
    }

    public bool Equals(DailyTally<TValue>? other) =>
        other is not null
        && Date == other.Date
        && System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(Value, other.Value);

    public override bool Equals(object? obj) => Equals(obj as DailyTally<TValue>);

    public override int GetHashCode() => System.HashCode.Combine(Date, Value);
}
