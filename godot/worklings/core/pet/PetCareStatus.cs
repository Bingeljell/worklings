using System.Collections.Generic;

namespace Worklings.Core.Pet;

/// How loudly a need is asking for something. Ordered, and compared as an order
/// — `>= Urgent` is the question the ambient layer asks.
///
/// Ported from Sources/CompanionCore/PetCareStatus.swift.
public enum PetUrgency
{
    None = 0,
    Notice = 1,
    Urgent = 2,
    Critical = 3,
}

public static class PetUrgencyExtensions
{
    /// Swift prints an enum case by its declared name, and the probe diffs
    /// against that. Lower-camel is the convention every RawValue here follows.
    public static string RawValue(this PetUrgency urgency) =>
        char.ToLowerInvariant(urgency.ToString()[0]) + urgency.ToString().Substring(1);
}

public enum PetNeedKind
{
    Hunger,
    Energy,
    Happiness,
    Trust,
}

public static class PetNeedKindExtensions
{
    public static readonly PetNeedKind[] AllCases =
    {
        PetNeedKind.Hunger, PetNeedKind.Energy, PetNeedKind.Happiness, PetNeedKind.Trust,
    };

    public static string RawValue(this PetNeedKind kind) => kind switch
    {
        PetNeedKind.Hunger => "hunger",
        PetNeedKind.Energy => "energy",
        PetNeedKind.Happiness => "happiness",
        PetNeedKind.Trust => "trust",
        _ => kind.ToString(),
    };

    public static string DisplayName(this PetNeedKind kind) => kind switch
    {
        PetNeedKind.Hunger => "Hunger",
        PetNeedKind.Energy => "Energy",
        PetNeedKind.Happiness => "Happiness",
        PetNeedKind.Trust => "Trust",
        _ => kind.ToString(),
    };

    /// A body need rather than a feeling. Hunger and exhaustion outrank sadness
    /// at the same urgency — you cannot cheer up a starving animal.
    internal static bool IsPhysical(this PetNeedKind kind) =>
        kind == PetNeedKind.Hunger || kind == PetNeedKind.Energy;

    /// The tie-break within one priority rank, so two equally urgent needs
    /// always come out in the same order rather than in whatever order the sort
    /// happened to leave them.
    internal static int StableOrder(this PetNeedKind kind) => kind switch
    {
        PetNeedKind.Hunger => 0,
        PetNeedKind.Energy => 1,
        PetNeedKind.Trust => 2,
        PetNeedKind.Happiness => 3,
        _ => 4,
    };
}

/// One need, over its threshold, with the words for it.
public sealed class PetNeedCondition : System.IEquatable<PetNeedCondition>
{
    public PetNeedKind Kind { get; }
    public PetUrgency Urgency { get; }
    public double Value { get; }
    public string Phrase { get; }

    public PetNeedCondition(PetNeedKind kind, PetUrgency urgency, double value, string phrase)
    {
        Kind = kind;
        Urgency = urgency;
        Value = value;
        Phrase = phrase;
    }

    public bool Equals(PetNeedCondition? other) =>
        other is not null && Kind == other.Kind && Urgency == other.Urgency
        && Value == other.Value && Phrase == other.Phrase;

    public override bool Equals(object? obj) => Equals(obj as PetNeedCondition);

    public override int GetHashCode() => System.HashCode.Combine(Kind, Urgency, Value, Phrase);
}

public enum PetCareActionKind
{
    Feed,
    Play,
    Pet,
    Sleep,
}

public static class PetCareActionKindExtensions
{
    public static readonly PetCareActionKind[] AllCases =
    {
        PetCareActionKind.Feed, PetCareActionKind.Play,
        PetCareActionKind.Pet, PetCareActionKind.Sleep,
    };

    public static string RawValue(this PetCareActionKind kind) => kind switch
    {
        PetCareActionKind.Feed => "feed",
        PetCareActionKind.Play => "play",
        PetCareActionKind.Pet => "pet",
        PetCareActionKind.Sleep => "sleep",
        _ => kind.ToString(),
    };
}

/// What the pet needs, ranked, and one sentence saying so.
///
/// The thresholds here are the only place the app decides that 75 hunger is
/// "hungry" and 90 is "very hungry". Everything that shows condition — the
/// hover summary, the ambient desktop layer, the menu's disabled actions —
/// reads it from here rather than re-deriving it from raw numbers, which is how
/// three surfaces end up disagreeing about whether the pet is fine.
public sealed class PetCareStatus
{
    public IReadOnlyList<PetNeedCondition> Conditions { get; }
    public string HoverSummary { get; }

    public PetCareStatus(IReadOnlyList<PetNeedCondition> conditions, string hoverSummary)
    {
        Conditions = conditions;
        HoverSummary = hoverSummary;
    }

    public static PetCareStatus Make(PetState state)
    {
        var found = new List<PetNeedCondition>();
        foreach (var condition in new[]
                 {
                     HungerCondition(state.Needs.Hunger),
                     EnergyCondition(state.Needs.Energy),
                     HappinessCondition(state.Needs.Happiness),
                     TrustCondition(state.Needs.Trust),
                 })
        {
            if (condition is not null) found.Add(condition);
        }
        found.Sort(Compare);

        // At most two. A pet that needs four things at once still says one
        // sentence a person can read at a glance.
        string summary;
        if (found.Count == 0)
        {
            summary = state.Mood == PetMood.Happy
                ? $"{state.Name} is happy."
                : $"{state.Name} is doing well.";
        }
        else if (found.Count == 1)
        {
            summary = $"{state.Name} is {found[0].Phrase}.";
        }
        else
        {
            summary = $"{state.Name} is {found[0].Phrase} and {found[1].Phrase}.";
        }

        return new PetCareStatus(found, summary);
    }

    /// The one condition the ambient layer is allowed to show. Anything milder
    /// than urgent is not worth putting on the desktop.
    public PetNeedCondition? AmbientCondition
    {
        get
        {
            foreach (var condition in Conditions)
            {
                if (condition.Urgency >= PetUrgency.Urgent) return condition;
            }
            return null;
        }
    }

    public PetActionAvailability Availability(PetCareActionKind action, PetState state)
    {
        switch (action)
        {
            case PetCareActionKind.Feed:
                if (state.Needs.Hunger <= 0)
                {
                    return new PetActionAvailability(false, $"{state.Name} is already full.");
                }
                break;
            case PetCareActionKind.Play:
                if (state.Needs.Energy < 15)
                {
                    return new PetActionAvailability(false, $"{state.Name} needs a nap first.");
                }
                break;
            case PetCareActionKind.Sleep:
                if (state.Needs.Energy >= 100)
                {
                    return new PetActionAvailability(false, $"{state.Name} is fully rested.");
                }
                break;
            case PetCareActionKind.Pet:
                break;
        }

        return new PetActionAvailability(true);
    }

    private static PetNeedCondition? HungerCondition(double value) => value switch
    {
        >= 90 => new(PetNeedKind.Hunger, PetUrgency.Critical, value, "very hungry"),
        >= 75 => new(PetNeedKind.Hunger, PetUrgency.Urgent, value, "hungry"),
        >= 55 => new(PetNeedKind.Hunger, PetUrgency.Notice, value, "a little hungry"),
        _ => null,
    };

    private static PetNeedCondition? EnergyCondition(double value) => value switch
    {
        <= 10 => new(PetNeedKind.Energy, PetUrgency.Critical, value, "exhausted"),
        <= 20 => new(PetNeedKind.Energy, PetUrgency.Urgent, value, "sleepy"),
        <= 45 => new(PetNeedKind.Energy, PetUrgency.Notice, value, "getting tired"),
        _ => null,
    };

    private static PetNeedCondition? HappinessCondition(double value) => value switch
    {
        <= 15 => new(PetNeedKind.Happiness, PetUrgency.Critical, value, "very unhappy"),
        <= 30 => new(PetNeedKind.Happiness, PetUrgency.Urgent, value, "sad"),
        <= 45 => new(PetNeedKind.Happiness, PetUrgency.Notice, value, "a little lonely"),
        _ => null,
    };

    private static PetNeedCondition? TrustCondition(double value) => value switch
    {
        <= 10 => new(PetNeedKind.Trust, PetUrgency.Critical, value, "in need of reassurance"),
        <= 20 => new(PetNeedKind.Trust, PetUrgency.Urgent, value, "wary"),
        <= 35 => new(PetNeedKind.Trust, PetUrgency.Notice, value, "a little unsure"),
        _ => null,
    };

    /// Swift's `sorted(by:)` takes "comes first"; C#'s takes a comparison. Same
    /// order: priority rank, then the stable per-kind tie-break.
    private static int Compare(PetNeedCondition a, PetNeedCondition b)
    {
        int rankA = PriorityRank(a);
        int rankB = PriorityRank(b);
        if (rankA != rankB) return rankA.CompareTo(rankB);
        return a.Kind.StableOrder().CompareTo(b.Kind.StableOrder());
    }

    /// Urgency first, and within each urgency the body before the feeling — so a
    /// critically hungry pet is listed ahead of a critically sad one, and an
    /// urgent physical need still outranks a critical emotional one only when
    /// its rank says so. The interleaving is the point: 0-1 critical, 2-3
    /// urgent, 4-5 notice.
    private static int PriorityRank(PetNeedCondition condition) => condition.Urgency switch
    {
        PetUrgency.Critical => condition.Kind.IsPhysical() ? 0 : 1,
        PetUrgency.Urgent => condition.Kind.IsPhysical() ? 2 : 3,
        PetUrgency.Notice => condition.Kind.IsPhysical() ? 4 : 5,
        _ => 6,
    };
}

/// Whether an action can be taken right now, and if not, why not in words the
/// menu can show.
public sealed class PetActionAvailability
{
    public bool IsEnabled { get; }
    public string? Explanation { get; }

    public PetActionAvailability(bool isEnabled, string? explanation = null)
    {
        IsEnabled = isEnabled;
        Explanation = explanation;
    }
}
