using Godot;
using Worklings.Core.Progression;

/// Compares the C# progression layer against reference output captured from the
/// Swift original. The XP curve is quadratic and the level search is a loop, so
/// an off-by-one is entirely plausible and entirely invisible in a build log.
public partial class ProgressionProbe : Node
{
    private static string F(double d) => d.ToString("F6");

    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();

        o.AppendLine("== curve ==");
        for (int level = 1; level <= 25; level++)
        {
            o.AppendLine($"level {level} requires {F(PetProgressionCurve.TotalXPRequired(level))}");
        }

        o.AppendLine("== level(forTotalXP) ==");
        foreach (double xp in new[] { 0.0, 1, 99, 100, 101, 299, 300, 301, 599, 600, 5000, 12345.678, 100000 })
        {
            o.AppendLine($"xp {F(xp)} -> level {PetProgressionCurve.Level(xp)}");
        }

        o.AppendLine("== progress ==");
        foreach (double xp in new[] { 0.0, 50, 100, 250, 600, 1234.5, 99999 })
        {
            var p = PetProgressionCurve.ProgressFor(xp);
            o.AppendLine($"xp {F(xp)} -> L{p.Level} into {F(p.XPIntoLevel)} of {F(p.XPForLevel)} frac {F(p.Fraction)}");
        }

        o.AppendLine("== stats growth ==");
        foreach (var cls in PetClassExtensions.AllCases)
        {
            var s = new PetStats();
            for (int i = 0; i < 5; i++) s = s.Growing(cls.SignatureStat(), 3, 1);
            o.AppendLine($"{cls.RawValue()} sig={cls.SignatureStat().RawValue()} role={cls.Role()} "
                + $"-> V{s.Vitality} P{s.Power} D{s.Defense} A{s.Agility} W{s.Wit}");
        }

        o.AppendLine("== rates caps ==");
        var r = new PetProgressionRates();
        foreach (var src in XPSourceExtensions.AllCases)
        {
            o.AppendLine($"{src.RawValue()} cap {F(r.DailyCap(src))} decay {F(r.DecayFactor(src))}");
        }

        o.AppendLine("== rates clamping ==");
        var c = new PetProgressionRates(
            dailyWakeXP: -5, focusSessionXPPerMinute: -1, milestoneXP: -40,
            signatureStatGainPerLevel: -3, conditionMultiplierFloor: 2.5,
            milestoneDecayFactor: -0.4);
        o.AppendLine($"wake {F(c.DailyWakeXP)} perMin {F(c.FocusSessionXPPerMinute)} "
            + $"milestone {F(c.MilestoneXP)} sigGain {c.SignatureStatGainPerLevel} "
            + $"floor {F(c.ConditionMultiplierFloor)} decay {F(c.MilestoneDecayFactor)}");

        o.AppendLine("== stat display ==");
        foreach (var k in PetStatKindExtensions.AllCases)
        {
            o.AppendLine($"{k.RawValue()} -> {k.DisplayName()}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
