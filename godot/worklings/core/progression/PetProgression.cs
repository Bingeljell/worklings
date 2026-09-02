namespace Worklings.Core.Progression;

/// Which of the five stats a class's growth favors. Kept distinct from the
/// stats themselves so a class can point at one without a per-class switch
/// inside PetStats.
///
/// Ported from Sources/CompanionCore/PetProgression.swift.
public enum PetStatKind
{
    Vitality,
    Power,
    Defense,
    Agility,
    Wit,
}

public static class PetStatKindExtensions
{
    public static readonly PetStatKind[] AllCases =
    {
        PetStatKind.Vitality, PetStatKind.Power, PetStatKind.Defense,
        PetStatKind.Agility, PetStatKind.Wit,
    };

    /// The internal name is Defense, not Guard (a reserved word in Swift); the
    /// design vocabulary calls this stat "Guard" everywhere else, the same
    /// split already used for hunger/Fullness.
    public static string DisplayName(this PetStatKind kind) => kind switch
    {
        PetStatKind.Vitality => "Vitality",
        PetStatKind.Power => "Power",
        PetStatKind.Defense => "Guard",
        PetStatKind.Agility => "Agility",
        PetStatKind.Wit => "Wit",
        _ => kind.ToString(),
    };

    /// The Swift `rawValue`, which is what the JSON save format stores.
    public static string RawValue(this PetStatKind kind) => kind switch
    {
        PetStatKind.Vitality => "vitality",
        PetStatKind.Power => "power",
        PetStatKind.Defense => "defense",
        PetStatKind.Agility => "agility",
        PetStatKind.Wit => "wit",
        _ => kind.ToString().ToLowerInvariant(),
    };
}

/// The battle-facing character sheet. Only ever grows — see
/// PetProgressionCurve for how level-ups apply that growth. Gear and class
/// modifiers, when they exist, compute an *effective* value on top of these
/// base numbers rather than ever being persisted into them.
///
/// A class, not a struct, deliberately. Swift's `PetStats()` yields
/// StartingValue in every field; C#'s implicit parameterless struct
/// constructor ignores default arguments and yields all zeros, as does
/// `default(PetStats)` for any array or uninitialised field. That produced a
/// level-one Workling with 0 in every stat that built cleanly and fought
/// silently. A null reference throws where a zeroed struct does not.
public sealed class PetStats : System.IEquatable<PetStats>
{
    public const int StartingValue = 5;

    /// The Swift `PetStats()` default — every stat at StartingValue.
    public static PetStats Starting => new PetStats();

    public int Vitality { get; }
    public int Power { get; }
    public int Defense { get; }
    public int Agility { get; }
    public int Wit { get; }

    public PetStats(
        int vitality = StartingValue,
        int power = StartingValue,
        int defense = StartingValue,
        int agility = StartingValue,
        int wit = StartingValue)
    {
        Vitality = vitality;
        Power = power;
        Defense = defense;
        Agility = agility;
        Wit = wit;
    }

    public int Value(PetStatKind stat) => stat switch
    {
        PetStatKind.Vitality => Vitality,
        PetStatKind.Power => Power,
        PetStatKind.Defense => Defense,
        PetStatKind.Agility => Agility,
        PetStatKind.Wit => Wit,
        _ => 0,
    };

    /// Applies one level's worth of growth: signatureStat grows by
    /// signatureGain, every other stat grows by otherGain, so a class's
    /// identity is visible on the sheet from level one without any stat
    /// ever staying frozen.
    public PetStats Growing(PetStatKind signatureStat, int signatureGain, int otherGain)
    {
        int Gain(PetStatKind stat) => stat == signatureStat ? signatureGain : otherGain;
        return new PetStats(
            vitality: Vitality + Gain(PetStatKind.Vitality),
            power: Power + Gain(PetStatKind.Power),
            defense: Defense + Gain(PetStatKind.Defense),
            agility: Agility + Gain(PetStatKind.Agility),
            wit: Wit + Gain(PetStatKind.Wit));
    }

    public bool Equals(PetStats? other) =>
        other is not null
        && Vitality == other.Vitality && Power == other.Power && Defense == other.Defense
        && Agility == other.Agility && Wit == other.Wit;

    public override bool Equals(object? obj) => Equals(obj as PetStats);

    public override int GetHashCode() =>
        System.HashCode.Combine(Vitality, Power, Defense, Agility, Wit);

    public static bool operator ==(PetStats? a, PetStats? b) =>
        a is null ? b is null : a.Equals(b);

    public static bool operator !=(PetStats? a, PetStats? b) => !(a == b);
}

/// The mechanical-identity axis, separate from PetFamily (which stays
/// cosmetic). Each class has one signature stat that grows fastest on
/// level-up; every name is deliberately dual-coded, a term with real
/// currency in modern work/maker culture that also carries its own mythic
/// weight, independent of any RPG convention.
public enum PetClass
{
    Wellspring,
    Juggernaut,
    Aegis,
    Maverick,
    Tinkerer,
}

public static class PetClassExtensions
{
    public static readonly PetClass[] AllCases =
    {
        PetClass.Wellspring, PetClass.Juggernaut, PetClass.Aegis,
        PetClass.Maverick, PetClass.Tinkerer,
    };

    public static string DisplayName(this PetClass petClass) => petClass switch
    {
        PetClass.Wellspring => "Wellspring",
        PetClass.Juggernaut => "Juggernaut",
        PetClass.Aegis => "Aegis",
        PetClass.Maverick => "Maverick",
        PetClass.Tinkerer => "Tinkerer",
        _ => petClass.ToString(),
    };

    public static string Role(this PetClass petClass) => petClass switch
    {
        PetClass.Wellspring => "Healer",
        PetClass.Juggernaut => "Heavy Offense",
        PetClass.Aegis => "Tank",
        PetClass.Maverick => "Finesse Offense",
        PetClass.Tinkerer => "Mage-equivalent",
        _ => "",
    };

    public static PetStatKind SignatureStat(this PetClass petClass) => petClass switch
    {
        PetClass.Wellspring => PetStatKind.Vitality,
        PetClass.Juggernaut => PetStatKind.Power,
        PetClass.Aegis => PetStatKind.Defense,
        PetClass.Maverick => PetStatKind.Agility,
        PetClass.Tinkerer => PetStatKind.Wit,
        _ => PetStatKind.Power,
    };

    /// The Swift `rawValue`, which is what the JSON save format stores.
    public static string RawValue(this PetClass petClass) =>
        petClass.ToString().ToLowerInvariant();
}

/// Derives level from cumulative XP via a formula rather than a stored
/// value, so level and XP can never disagree with each other — the same
/// silent-desync failure mode this codebase has already hit twice with
/// other duplicated state (see the changelog for the Log Work fix).
public static class PetProgressionCurve
{
    /// Cumulative XP required to have reached `level`. Quadratic growth
    /// keeps early levels cheap and later ones meaningfully longer; the
    /// formula has no upper bound, so raising a level cap later never
    /// requires migrating this table.
    public static double TotalXPRequired(int forLevel)
    {
        if (forLevel <= 1)
        {
            return 0;
        }
        double steps = forLevel - 1;
        return 50 * steps * (steps + 1);
    }

    public static int Level(double forTotalXP)
    {
        int level = 1;
        while (forTotalXP >= TotalXPRequired(level + 1))
        {
            level += 1;
        }
        return level;
    }

    /// Everything a progress readout needs, derived once: the level, how far
    /// into it the total is, the level's full span, and the clamped 0...1
    /// fraction. Any surface showing an XP bar reads this instead of
    /// re-deriving the arithmetic.
    public readonly record struct Progress(
        int Level,
        double XPIntoLevel,
        double XPForLevel,
        double Fraction);

    public static Progress ProgressFor(double totalXP)
    {
        int level = Level(totalXP);
        double currentLevelXP = TotalXPRequired(level);
        double nextLevelXP = TotalXPRequired(level + 1);
        double xpIntoLevel = System.Math.Max(0, totalXP - currentLevelXP);
        double xpForLevel = nextLevelXP - currentLevelXP;
        double fraction = xpForLevel > 0
            ? System.Math.Min(System.Math.Max(xpIntoLevel / xpForLevel, 0), 1)
            : 1;
        return new Progress(level, xpIntoLevel, xpForLevel, fraction);
    }
}

/// Identifies which XP source a grant came from, purely for per-source
/// daily-cap bookkeeping. Distinct from ActivityEvent.SourceId, which
/// identifies *who reported* an event (system/manual/simulated); this
/// identifies *what kind of progress* it represents.
public enum XPSource
{
    DailyWake,
    FocusSession,
    Care,
    TaskCompleted,
    Milestone,
    WorkLogged,
}

public static class XPSourceExtensions
{
    public static readonly XPSource[] AllCases =
    {
        XPSource.DailyWake, XPSource.FocusSession, XPSource.Care,
        XPSource.TaskCompleted, XPSource.Milestone, XPSource.WorkLogged,
    };

    /// The Swift `rawValue`. This is a dictionary key in the persisted daily
    /// tallies, so it must stay camelCase and match Swift exactly.
    public static string RawValue(this XPSource source) => source switch
    {
        XPSource.DailyWake => "dailyWake",
        XPSource.FocusSession => "focusSession",
        XPSource.Care => "care",
        XPSource.TaskCompleted => "taskCompleted",
        XPSource.Milestone => "milestone",
        XPSource.WorkLogged => "workLogged",
        _ => source.ToString(),
    };
}

/// Every number here is alpha tuning, the same posture as
/// PetSimulationRates: sane defaults now, retuned from real usage later
/// without touching the mechanism.
public sealed class PetProgressionRates : System.IEquatable<PetProgressionRates>
{
    public double DailyWakeXP { get; }
    public double FocusSessionXPPerMinute { get; }
    public double FocusSessionMinimumMinutes { get; }
    public double FocusSessionDailyCap { get; }
    public double CareActionXP { get; }
    public double CareActionDailyCap { get; }
    public double TaskCompletedXP { get; }
    public double TaskCompletedDailyCap { get; }
    public double MilestoneXP { get; }
    public double MilestoneDailyCap { get; }
    public double WorkLoggedXP { get; }
    public double WorkLoggedDailyCap { get; }
    public double OverallDailyCap { get; }
    public int SignatureStatGainPerLevel { get; }
    public int OtherStatGainPerLevel { get; }
    public double ConditionMultiplierFloor { get; }

    /// Per-event geometric decay applied to milestone grants within a day:
    /// the Nth milestone credited today is worth MilestoneXP * factor^(N-1).
    /// A batch of commits therefore tapers rather than piling linearly toward
    /// the cap. 1.0 disables decay; the daily cap still backstops.
    public double MilestoneDecayFactor { get; }

    public PetProgressionRates(
        double dailyWakeXP = 20,
        double focusSessionXPPerMinute = 2,
        double focusSessionMinimumMinutes = 10,
        double focusSessionDailyCap = 200,
        double careActionXP = 3,
        double careActionDailyCap = 60,
        double taskCompletedXP = 15,
        double taskCompletedDailyCap = 150,
        double milestoneXP = 40,
        double milestoneDailyCap = 200,
        double workLoggedXP = 5,
        double workLoggedDailyCap = 30,
        double overallDailyCap = 500,
        int signatureStatGainPerLevel = 3,
        int otherStatGainPerLevel = 1,
        double conditionMultiplierFloor = 0.2,
        double milestoneDecayFactor = 0.7)
    {
        DailyWakeXP = System.Math.Max(dailyWakeXP, 0);
        FocusSessionXPPerMinute = System.Math.Max(focusSessionXPPerMinute, 0);
        FocusSessionMinimumMinutes = System.Math.Max(focusSessionMinimumMinutes, 0);
        FocusSessionDailyCap = System.Math.Max(focusSessionDailyCap, 0);
        CareActionXP = System.Math.Max(careActionXP, 0);
        CareActionDailyCap = System.Math.Max(careActionDailyCap, 0);
        TaskCompletedXP = System.Math.Max(taskCompletedXP, 0);
        TaskCompletedDailyCap = System.Math.Max(taskCompletedDailyCap, 0);
        MilestoneXP = System.Math.Max(milestoneXP, 0);
        MilestoneDailyCap = System.Math.Max(milestoneDailyCap, 0);
        WorkLoggedXP = System.Math.Max(workLoggedXP, 0);
        WorkLoggedDailyCap = System.Math.Max(workLoggedDailyCap, 0);
        OverallDailyCap = System.Math.Max(overallDailyCap, 0);
        SignatureStatGainPerLevel = System.Math.Max(signatureStatGainPerLevel, 0);
        OtherStatGainPerLevel = System.Math.Max(otherStatGainPerLevel, 0);
        ConditionMultiplierFloor =
            System.Math.Min(System.Math.Max(conditionMultiplierFloor, 0), 1);
        MilestoneDecayFactor =
            System.Math.Min(System.Math.Max(milestoneDecayFactor, 0), 1);
    }

    public double DailyCap(XPSource source) => source switch
    {
        XPSource.DailyWake => DailyWakeXP,
        XPSource.FocusSession => FocusSessionDailyCap,
        XPSource.Care => CareActionDailyCap,
        XPSource.TaskCompleted => TaskCompletedDailyCap,
        XPSource.Milestone => MilestoneDailyCap,
        XPSource.WorkLogged => WorkLoggedDailyCap,
        _ => 0,
    };

    /// Per-event geometric decay factor for a source's within-day grants.
    /// Only milestone tapers today; every other source is flat (1.0), so the
    /// count-based decay in GrantingXP is a no-op for them.
    public double DecayFactor(XPSource source) => source switch
    {
        XPSource.Milestone => MilestoneDecayFactor,
        _ => 1,
    };

    public bool Equals(PetProgressionRates? other) =>
        other is not null
        && DailyWakeXP == other.DailyWakeXP
        && FocusSessionXPPerMinute == other.FocusSessionXPPerMinute
        && FocusSessionMinimumMinutes == other.FocusSessionMinimumMinutes
        && FocusSessionDailyCap == other.FocusSessionDailyCap
        && CareActionXP == other.CareActionXP
        && CareActionDailyCap == other.CareActionDailyCap
        && TaskCompletedXP == other.TaskCompletedXP
        && TaskCompletedDailyCap == other.TaskCompletedDailyCap
        && MilestoneXP == other.MilestoneXP
        && MilestoneDailyCap == other.MilestoneDailyCap
        && WorkLoggedXP == other.WorkLoggedXP
        && WorkLoggedDailyCap == other.WorkLoggedDailyCap
        && OverallDailyCap == other.OverallDailyCap
        && SignatureStatGainPerLevel == other.SignatureStatGainPerLevel
        && OtherStatGainPerLevel == other.OtherStatGainPerLevel
        && ConditionMultiplierFloor == other.ConditionMultiplierFloor
        && MilestoneDecayFactor == other.MilestoneDecayFactor;

    public override bool Equals(object? obj) => Equals(obj as PetProgressionRates);

    public override int GetHashCode() => System.HashCode.Combine(
        DailyWakeXP, FocusSessionXPPerMinute, FocusSessionDailyCap,
        CareActionXP, TaskCompletedXP, MilestoneXP, OverallDailyCap,
        MilestoneDecayFactor);
}
