using Godot;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the PetState -> Combatant bridge against reference output captured
/// from the Swift original. This is the seam where a real Workling replaces the
/// dungeon's hardcoded stats, so it folds gear in before condition and rounds
/// half away from zero — two places a port disagrees quietly and permanently.
public partial class CombatantBridgeProbe : Node
{
    private static System.DateTimeOffset D(string s) =>
        System.DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static string F(double x) => x.ToString("F6");

    private static string Describe(Combatant c) =>
        $"{c.Name} HP {c.MaxHP} P{c.Stats.Power} D{c.Stats.Defense} "
        + $"A{c.Stats.Agility} W{c.Stats.Wit}";

    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();
        var now = D("2026-09-02T10:00:00Z");
        var rates = new PetCombatRates();
        var prefs = new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle);

        o.AppendLine("== combat effectiveness ==");
        foreach (var (h, e, hp, t) in new[]
        {
            (0.0, 100.0, 100.0, 100.0), (50.0, 50.0, 50.0, 50.0),
            (100.0, 0.0, 0.0, 0.0), (30.0, 70.0, 60.0, 80.0),
        })
        {
            o.AppendLine($"H{F(h)} -> {F(rates.CombatEffectiveness(new PetNeeds(h, e, hp, t)))}");
        }

        o.AppendLine("== pet combatant from stats ==");
        var sheets = new[]
        {
            PetStats.Starting,
            new PetStats(vitality: 20, power: 15, defense: 11, agility: 9, wit: 7),
            new PetStats(vitality: 41, power: 33, defense: 25, agility: 17, wit: 13),
        };
        var conditions = new (string Label, PetNeeds Needs)[]
        {
            ("perfect", new PetNeeds(0, 100, 100, 100)),
            ("mid", new PetNeeds(50, 50, 50, 50)),
            ("neglected", new PetNeeds(100, 0, 0, 0)),
        };
        foreach (var stats in sheets)
        {
            foreach (var (label, needs) in conditions)
            {
                var c = Combatant.Pet("W", stats, needs, rates);
                o.AppendLine($"V{stats.Vitality}P{stats.Power} {label}: HP {c.MaxHP}/{c.CurrentHP} "
                    + $"P{c.Stats.Power} D{c.Stats.Defense} A{c.Stats.Agility} W{c.Stats.Wit}");
            }
        }

        o.AppendLine("== from PetState, gear folded in ==");
        var s = new PetState(
            name: "Pixel",
            needs: new PetNeeds(20, 90, 85, 75),
            preferences: prefs,
            lastUpdatedAt: now,
            family: PetFamily.Relicborn,
            totalXP: 1200,
            stats: new PetStats(vitality: 14, power: 12, defense: 10, agility: 8, wit: 6));
        o.AppendLine($"ungeared: {Describe(Combatant.Pet(s, rates))}");
        s = s.Acquiring(Item.MastersHone).Equipping(Item.MastersHone);
        o.AppendLine($"+ mastersHone (attuned relicborn): {Describe(Combatant.Pet(s, rates))}");
        s = s.Acquiring(Item.EverburningBackup).Equipping(Item.EverburningBackup);
        o.AppendLine($"+ everburningBackup: {Describe(Combatant.Pet(s, rates))}");
        s = s.Acquiring(Item.HotpathSigil).Equipping(Item.HotpathSigil);
        o.AppendLine($"+ hotpathSigil: {Describe(Combatant.Pet(s, rates))}");
        var wildkin = s.SelectingFamily(PetFamily.Wildkin);
        o.AppendLine($"same gear, wildkin: {Describe(Combatant.Pet(wildkin, rates))}");
        var starved = s.Applying(needs: new PetNeeds(95, 10, 10, 10));
        o.AppendLine($"same gear, neglected: {Describe(Combatant.Pet(starved, rates))}");

        o.AppendLine("== rounding at the half ==");
        foreach (int v in new[] { 1, 3, 5, 7, 9, 11, 13, 15 })
        {
            var c = Combatant.Pet("R",
                new PetStats(vitality: v, power: v, defense: v, agility: v, wit: v),
                new PetNeeds(0, 100, 100, 0), rates);
            o.AppendLine($"base {v} at 0.75x -> P{c.Stats.Power} HP{c.MaxHP}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
