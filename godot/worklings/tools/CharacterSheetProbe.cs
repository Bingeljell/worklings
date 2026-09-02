using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the character-sheet readout against reference output captured from
/// the Swift original. The sheet's whole point is that it cannot drift from the
/// fight — it builds a real Combatant rather than recomputing — so this checks
/// all three rungs of the ladder (base, +gear, xcondition) on one pet.
public partial class CharacterSheetProbe : Node
{
    private static System.DateTimeOffset D(string s) =>
        System.DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static readonly System.DateTimeOffset Now = D("2026-09-02T10:00:00Z");
    private static readonly PetPreferences Prefs =
        new(PetFood.Berries, PetPlayActivity.Puzzle);

    private static string F(double x) => x.ToString("F4");
    private static string B(bool b) => b ? "true" : "false";

    private static string Names(IReadOnlyList<Item> l)
    {
        if (l.Count == 0) return "-";
        var parts = new List<string>();
        foreach (var i in l) parts.Add(i.RawValue());
        return string.Join(",", parts);
    }

    private readonly System.Text.StringBuilder _o = new();

    private void Dump(string label, PetState s)
    {
        var sheet = CharacterSheet.Make(s);
        _o.AppendLine($"-- {label} --");
        _o.AppendLine($"name {sheet.Name} family {sheet.Family.RawValue()} "
            + $"class {sheet.PetClass.RawValue()} level {sheet.Level}");
        _o.AppendLine($"progress L{sheet.Progress.Level} into {F(sheet.Progress.XPIntoLevel)}"
            + $"/{F(sheet.Progress.XPForLevel)} frac {F(sheet.Progress.Fraction)}");
        foreach (var r in sheet.Rows)
        {
            _o.AppendLine($"  {r.Stat.RawValue()}: base {r.Base} gear +{r.GearBonus} "
                + $"effective {r.Effective} signature {B(r.IsSignature)}");
        }
        _o.AppendLine($"combat HP {sheet.Combat.MaxHP} strike {sheet.Combat.Strike} "
            + $"crit {F(sheet.Combat.CritChance)} eff {F(sheet.Combat.Effectiveness)} "
            + $"diminished {B(sheet.Combat.IsDiminished)}");
        _o.AppendLine($"gearPoints {sheet.GearPointTotal} hasGear {B(sheet.HasGearEquipped)} "
            + $"attuned {Names(sheet.AttunedItems)}");
    }

    public override void _Ready()
    {
        Dump("fresh wildkin wellspring", PetState.NewPet(now: Now));

        var built = new PetState(
            name: "Anvil",
            needs: new PetNeeds(15, 95, 90, 88),
            preferences: Prefs,
            lastUpdatedAt: Now,
            family: PetFamily.Relicborn,
            totalXP: 2600,
            petClass: PetClass.Juggernaut,
            stats: new PetStats(vitality: 24, power: 26, defense: 16, agility: 12, wit: 9));
        Dump("juggernaut, no gear", built);

        built = built.Acquiring(Item.MastersHone).Equipping(Item.MastersHone);
        Dump("+ attuned Prime tool", built);

        built = built.Acquiring(Item.FailsafePlate).Equipping(Item.FailsafePlate);
        built = built.Acquiring(Item.RootCauseLens).Equipping(Item.RootCauseLens);
        Dump("+ ward + charm", built);

        Dump("same, neglected", built.Applying(needs: new PetNeeds(90, 12, 8, 15)));
        Dump("same, unattuned family", built.SelectingFamily(PetFamily.Bloomglass));

        Dump("scavenged only", new PetState(
            name: "Scrap",
            needs: new PetNeeds(10, 100, 100, 100),
            preferences: Prefs,
            lastUpdatedAt: Now,
            family: PetFamily.Glitchkin,
            totalXP: 0,
            petClass: PetClass.Maverick,
            stats: PetStats.Starting,
            ownedItems: new[] { Item.FrayedLanyard, Item.BentPotLid },
            loadout: Loadout.Empty.Equipping(Item.FrayedLanyard).Equipping(Item.BentPotLid)));

        GD.Print(_o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
