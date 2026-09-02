namespace Worklings.Core.Pet;

/// The four care needs, each held to 0...100 on construction so no caller can
/// build an out-of-range Workling.
///
/// Ported from Sources/CompanionCore/PetState.swift.
public sealed class PetNeeds : System.IEquatable<PetNeeds>
{
    public double Hunger { get; }
    public double Energy { get; }
    public double Happiness { get; }
    public double Trust { get; }

    /// Hunger read the way every surface phrases it. The internal name is the
    /// need; the design vocabulary is the inverse, the same hunger/Fullness
    /// split PetStatKind makes for defense/Guard.
    public double Fullness => 100 - Hunger;

    public PetNeeds(double hunger, double energy, double happiness, double trust)
    {
        Hunger = Clamp(hunger);
        Energy = Clamp(energy);
        Happiness = Clamp(happiness);
        Trust = Clamp(trust);
    }

    /// NaN and the infinities collapse to 0 rather than propagating: min/max
    /// against NaN returns NaN in both languages, so without the finite check a
    /// single bad number silently poisons every downstream average.
    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }
        return System.Math.Min(System.Math.Max(value, 0), 100);
    }

    /// XP accrues at a fraction of full rate when wellbeing is poor, floored so
    /// neglect slows progression without fully halting it — "learns slowly," not
    /// "can't learn at all."
    ///
    /// Swift hangs this off PetNeeds as an extension in PetProgression.swift;
    /// it lives on the type here because C# has no extension properties and the
    /// method has nowhere better to be.
    public double XPMultiplier(double floor)
    {
        double average = (Fullness + Energy + Happiness + Trust) / 4;
        return System.Math.Max(floor, average / 100);
    }

    public bool Equals(PetNeeds? other) =>
        other is not null && Hunger == other.Hunger && Energy == other.Energy
        && Happiness == other.Happiness && Trust == other.Trust;

    public override bool Equals(object? obj) => Equals(obj as PetNeeds);

    public override int GetHashCode() =>
        System.HashCode.Combine(Hunger, Energy, Happiness, Trust);
}

public enum PetFood
{
    Berries,
    Biscuit,
    Noodles,
}

public enum PetPlayActivity
{
    Chase,
    Dance,
    Puzzle,
}

public enum PetMood
{
    Happy,
    Content,
    Hungry,
    Sleepy,
    Sad,
    Wary,
}

public sealed class PetPreferences : System.IEquatable<PetPreferences>
{
    public PetFood FavouriteFood { get; }
    public PetPlayActivity FavouritePlayActivity { get; }

    public PetPreferences(PetFood favouriteFood, PetPlayActivity favouritePlayActivity)
    {
        FavouriteFood = favouriteFood;
        FavouritePlayActivity = favouritePlayActivity;
    }

    public bool Equals(PetPreferences? other) =>
        other is not null && FavouriteFood == other.FavouriteFood
        && FavouritePlayActivity == other.FavouritePlayActivity;

    public override bool Equals(object? obj) => Equals(obj as PetPreferences);

    public override int GetHashCode() =>
        System.HashCode.Combine(FavouriteFood, FavouritePlayActivity);
}

public static class PetNeedsEnumExtensions
{
    public static readonly PetFood[] AllFood =
        { PetFood.Berries, PetFood.Biscuit, PetFood.Noodles };

    public static readonly PetPlayActivity[] AllPlayActivities =
        { PetPlayActivity.Chase, PetPlayActivity.Dance, PetPlayActivity.Puzzle };

    public static string DisplayName(this PetFood food) => food switch
    {
        PetFood.Berries => "Berries",
        PetFood.Biscuit => "Biscuit",
        PetFood.Noodles => "Noodles",
        _ => food.ToString(),
    };

    public static string DisplayName(this PetPlayActivity activity) => activity switch
    {
        PetPlayActivity.Chase => "Chase",
        PetPlayActivity.Dance => "Dance",
        PetPlayActivity.Puzzle => "Puzzle",
        _ => activity.ToString(),
    };

    public static string RawValue(this PetFood food) => food.ToString().ToLowerInvariant();

    public static string RawValue(this PetPlayActivity activity) =>
        activity.ToString().ToLowerInvariant();

    public static string RawValue(this PetMood mood) => mood.ToString().ToLowerInvariant();
}
